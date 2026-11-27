using System.Globalization;
using System.Text.Json;
using Npgsql;

namespace GeoAssets.Bench.Render;

/// <summary>
/// Developer tool (XD01-420, ADR-002 Track 0): seeds the deterministic 200k-feature PostGIS dataset and measures the
/// map's current viewport path at z8/z12/z16 — payload bytes, server time and a simulated client render — for
/// <c>docs/perf/render-baseline.md</c>. A console app rather than a test: it needs one stable, inspectable database
/// across seed/bench invocations (like <c>GeoAssets.Bench.GetPageAsync</c>). See <see cref="BenchOptions.Usage"/>.
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true };

    private static async Task<int> Main(string[] args)
    {
        // Console output is pasted into docs/perf/render-baseline.md: the same digits on every machine.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        var (options, error) = BenchOptions.Parse(args);
        if (options is null)
        {
            await Console.Error.WriteLineAsync(error);
            await Console.Error.WriteLineAsync(BenchOptions.Usage);
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        CancelOnCtrlC(cancellation);

        var connectionString = Environment.GetEnvironmentVariable(BenchOptions.ConnectionVariable) ?? BenchOptions.DefaultConnectionString;
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseNetTopologySuite();
        await using var dataSource = builder.Build();
        var time = TimeProvider.System;

        try
        {
            if (string.Equals(options.Command, "seed", StringComparison.Ordinal))
            {
                var generator = new DatasetGenerator(options.Settings.Seed);
                var summary = await new DatasetSeeder(dataSource, time, Console.Out)
                    .SeedAsync(generator, options.Features, cancellation.Token);
                foreach (var (type, count) in summary.Counts)
                    await Console.Out.WriteLineAsync($"  {type.Name,-24} {type.Kind,-10} {count,8:N0}  zoom {type.MinZoom?.ToString(CultureInfo.InvariantCulture) ?? "-"}..{type.MaxZoom?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
                await Console.Out.WriteLineAsync($"Seeded {options.Features:N0} features ({summary.TotalVertices:N0} vertices) in {summary.Elapsed.TotalSeconds:F1}s.");
                return 0;
            }

            var environment = await BenchEnvironment.CaptureAsync(dataSource, time, cancellation.Token);
            await Console.Out.WriteLineAsync($"Dataset: {environment.DatasetFeatures:N0} features, {environment.DatasetVertices:N0} vertices, geo_entity {environment.GeoEntityTotalSize}.");

            if (string.Equals(options.Command, "tiles", StringComparison.Ordinal))
            {
                var tiles = await new TileBenchmark(new TileEndpointPipeline(dataSource, time), time, Console.Out)
                    .RunAsync(options.Settings, new DatasetLayout(options.Settings.Seed), cancellation.Token);
                var tileReport = new TileBenchReport(environment, options.Settings, tiles);
                if (options.OutPath is not null)
                {
                    await File.WriteAllTextAsync(options.OutPath, JsonSerializer.Serialize(tileReport, ReportJson), cancellation.Token);
                    await Console.Out.WriteLineAsync($"Wrote {options.OutPath}");
                }

                await Console.Out.WriteLineAsync();
                await Console.Out.WriteLineAsync(tileReport.ToMarkdown());
                return 0;
            }

            var benchmark = new RenderBenchmark(new BoundsEndpointPipeline(dataSource, time), new SimulatedClientRenderer(time), Console.Out);
            var zooms = await benchmark.RunAsync(options.Settings, new DatasetLayout(options.Settings.Seed), cancellation.Token);
            var report = new BenchReport(environment, options.Settings, zooms);

            if (options.OutPath is not null)
            {
                await File.WriteAllTextAsync(options.OutPath, JsonSerializer.Serialize(report, ReportJson), cancellation.Token);
                await Console.Out.WriteLineAsync($"Wrote {options.OutPath}");
            }

            await Console.Out.WriteLineAsync();
            await Console.Out.WriteLineAsync(report.ToMarkdown());
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
            return 1;
        }
        catch (NpgsqlException ex)
        {
            await Console.Error.WriteLineAsync($"Database error ({ex.GetType().Name}): {ex.Message}");
            return 1;
        }
    }

    /// <summary>Ctrl+C stops the run instead of killing the process mid-COPY.</summary>
    private static void CancelOnCtrlC(CancellationTokenSource cancellation) =>
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
}

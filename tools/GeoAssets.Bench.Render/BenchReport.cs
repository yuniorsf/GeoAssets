using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using Npgsql;

namespace GeoAssets.Bench.Render;

/// <summary>Where a run was measured: machine, runtime, database, and the dataset actually in the table.</summary>
internal sealed record BenchEnvironment(
    DateTimeOffset MeasuredAtUtc,
    string OperatingSystem,
    string Architecture,
    int ProcessorCount,
    string Framework,
    bool ServerGc,
    string PostgresVersion,
    string PostGisVersion,
    long DatasetFeatures,
    long DatasetVertices,
    string GeoEntityTotalSize)
{
    public static async Task<BenchEnvironment> CaptureAsync(NpgsqlDataSource dataSource, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var command = dataSource.CreateCommand(
            """
            SELECT version(), postgis_lib_version(), count(*), coalesce(sum(ST_NPoints(geom)), 0),
                   pg_size_pretty(pg_total_relation_size('geo_entity'))
            FROM geo_entity
            """);
        await using (command.ConfigureAwait(false))
        {
            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                return new BenchEnvironment(
                    timeProvider.GetUtcNow(),
                    RuntimeInformation.OSDescription,
                    RuntimeInformation.ProcessArchitecture.ToString(),
                    Environment.ProcessorCount,
                    RuntimeInformation.FrameworkDescription,
                    GCSettings.IsServerGC,
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    reader.GetInt64(3),
                    reader.GetString(4));
            }
        }
    }
}

/// <summary>A run's full output: where, how, and what was measured (written as JSON with <c>--out</c>).</summary>
internal sealed record BenchReport(BenchEnvironment Environment, BenchSettings Settings, IReadOnlyList<ZoomResult> Zooms)
{
    /// <summary>The Markdown tables <c>docs/perf/render-baseline.md</c> records.</summary>
    public string ToMarkdown()
    {
        var inv = CultureInfo.InvariantCulture;
        var md = new StringBuilder();

        md.AppendLine("| Zoom | Matched rows | Returned features | Drawn features | Payload (uncompressed) | Payload (Brotli, on the wire) | Payload (gzip) | Server ms | ↳ DB query + read ms | Client render ms (simulated) |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var z in Zooms)
        {
            md.Append(inv, $"| z{z.Zoom} | {Count(z.MatchedRows)} | {Count(z.ReturnedFeatures)} | {Count(z.DrawnFeatures)} | ")
              .Append(inv, $"{Bytes(z.PayloadBytes)} | {Bytes(z.BrotliBytes)} | {Bytes(z.GzipBytes)} | {Ms(z.ServerMs)} | {Ms(z.DatabaseMs)} | {Ms(z.ClientMs)} |")
              .AppendLine();
        }

        md.AppendLine();
        md.AppendLine("Each cell is p50 / p95.");
        md.AppendLine();

        foreach (var z in Zooms)
        {
            md.AppendLine(inv, $"#### z{z.Zoom} — per viewport (first measured pass; ms = the viewport's p50)");
            md.AppendLine();
            md.AppendLine("| # | Center (lon, lat) | Matched | Returned | Drawn | Path vertices | Payload | Brotli | gzip | Server ms | Client ms |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            for (var i = 0; i < z.Viewports.Count; i++)
            {
                var v = z.Viewports[i];
                md.Append(inv, $"| {i + 1} | {v.CenterLon:F4}, {v.CenterLat:F4} | {v.MatchedRows:N0} | {v.ReturnedFeatures:N0} | {v.DrawnFeatures:N0} | ")
                  .Append(inv, $"{v.PathVertices:N0} | {Megabytes(v.PayloadBytes)} | {Megabytes(v.BrotliBytes)} | {Megabytes(v.GzipBytes)} | {v.ServerMsP50:F1} | {v.ClientMsP50:F1} |")
                  .AppendLine();
            }

            md.AppendLine();
        }

        return md.ToString();
    }

    private static string Count(PercentileSummary s) => string.Create(CultureInfo.InvariantCulture, $"{s.P50:N0} / {s.P95:N0}");

    private static string Ms(PercentileSummary s) => string.Create(CultureInfo.InvariantCulture, $"{s.P50:F1} / {s.P95:F1}");

    private static string Bytes(PercentileSummary s) => $"{Megabytes((long)s.P50)} / {Megabytes((long)s.P95)}";

    private static string Megabytes(long bytes) =>
        bytes >= 1_000_000
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes / 1_000_000.0:F2} MB")
            : string.Create(CultureInfo.InvariantCulture, $"{bytes / 1_000.0:F1} kB");
}

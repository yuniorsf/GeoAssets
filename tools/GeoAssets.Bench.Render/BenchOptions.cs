using System.Globalization;

namespace GeoAssets.Bench.Render;

/// <summary>The tool's command line (XD01-420).</summary>
internal sealed record BenchOptions(string Command, int Features, BenchSettings Settings, string? OutPath)
{
    /// <summary>The environment variable naming the benchmark database (never the kernel's — the seed empties <c>geo_entity</c>).</summary>
    public const string ConnectionVariable = "GEOASSETS_RENDER_BENCH_CONNECTION";

    /// <summary>The database of <c>tools/GeoAssets.Bench.Render/docker-compose.bench.yaml</c>.</summary>
    public const string DefaultConnectionString =
        "Host=localhost;Port=5435;Database=geoassets_render_bench;Username=postgres;Password=postgres";

    public const string Usage = """
        GeoAssets.Bench.Render — the GIS render baseline (XD01-420, docs/perf/render-baseline.md)

          seed   [--features 200000] [--seed 420]        replace geo_entity with the deterministic dataset
          bench  [options] [--out <results.json>]          measure the z8/z12/z16 viewports, print Markdown tables

        Bench options (defaults = the recorded baseline):
          --seed 420  --zooms 8,12,16  --viewports 12 (per zoom)  --iterations 5  --warmup 1  --viewport 1580x1020
        The database is read from GEOASSETS_RENDER_BENCH_CONNECTION (default: the docker-compose.bench.yaml container on
        localhost:5435); its schema comes from `dotnet ef database update` (GeoAssetsDbContext).
        """;

    public static (BenchOptions? Options, string? Error) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0 || args[0] is not ("seed" or "bench")) return (null, "The first argument must be 'seed' or 'bench'.");

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Count; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Count)
                return (null, $"'{args[i]}' needs a value.");
            if (!values.TryAdd(args[i], args[i + 1])) return (null, $"{args[i]} is given more than once.");
        }

        try
        {
            var (width, height) = Viewport(values.GetValueOrDefault("--viewport"));
            var settings = new BenchSettings(
                Seed: ulong.Parse(values.GetValueOrDefault("--seed", DatasetGenerator.DefaultSeed.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture),
                Zooms: Zooms(values.GetValueOrDefault("--zooms")),
                ViewportsPerZoom: Positive(values, "--viewports", 12),
                Iterations: Positive(values, "--iterations", 5),
                WarmupIterations: NonNegative(values, "--warmup", 1),
                WidthPx: width,
                HeightPx: height);
            var unknown = values.Keys.Except(
                ["--seed", "--zooms", "--viewports", "--iterations", "--warmup", "--viewport", "--features", "--out"],
                StringComparer.Ordinal).FirstOrDefault();
            if (unknown is not null) return (null, $"Unknown option {unknown}.");

            return (new BenchOptions(args[0], Positive(values, "--features", DatasetComposition.DefaultFeatureCount), settings, values.GetValueOrDefault("--out")), null);
        }
        catch (FormatException ex)
        {
            return (null, ex.Message);
        }
        catch (OverflowException ex)
        {
            return (null, ex.Message);
        }
    }

    private static int Positive(Dictionary<string, string> values, string name, int fallback)
    {
        var value = NonNegative(values, name, fallback);
        return value > 0 ? value : throw new FormatException($"{name} must be positive.");
    }

    private static int NonNegative(Dictionary<string, string> values, string name, int fallback)
    {
        if (!values.TryGetValue(name, out var raw)) return fallback;
        var value = int.Parse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture);
        return value >= 0 ? value : throw new FormatException($"{name} must not be negative.");
    }

    private static int[] Zooms(string? raw)
    {
        if (raw is null) return [.. ViewportPlan.StandardZooms];
        var zooms = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(z => int.Parse(z, NumberStyles.Integer, CultureInfo.InvariantCulture))
            .ToArray();
        return zooms.Length > 0 && zooms.All(z => z is >= 0 and <= 22) ? zooms : throw new FormatException("--zooms must list zooms 0–22.");
    }

    private static (int Width, int Height) Viewport(string? raw)
    {
        if (raw is null) return (ViewportPlan.DefaultWidthPx, ViewportPlan.DefaultHeightPx);
        var parts = raw.Split('x');
        if (parts.Length != 2) throw new FormatException("--viewport must be <width>x<height>.");
        var width = int.Parse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture);
        var height = int.Parse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture);
        return width > 0 && height > 0 ? (width, height) : throw new FormatException("--viewport sizes must be positive.");
    }
}

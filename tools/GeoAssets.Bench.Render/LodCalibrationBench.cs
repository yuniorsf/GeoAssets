using System.Globalization;
using System.Text;
using System.Text.Json;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Bench.Render;

/// <summary>One benchmark asset type's calibration.</summary>
internal sealed record BenchAssetTypeCalibration(string AssetTypeId, string Name, int? MinZoom, int? MaxZoom, AssetTypeLodCalibration Calibration);

/// <summary>
/// The <c>calibrate</c> command's output (XD01-439; written as JSON with <c>--out</c>, read back by <c>tiles --lod</c>):
/// <see cref="LodCalibrator"/> — the kernel's, from GeoAssets.Core — over <see cref="BenchTileDensitySampler"/> for every
/// type of the dataset, organization-less as the tile scenario.
/// </summary>
internal sealed record LodCalibrationReport(
    BenchEnvironment Environment, LodCalibrationSettings Settings, IReadOnlyList<BenchAssetTypeCalibration> AssetTypes, double ElapsedMs)
{
    public static async Task<LodCalibrationReport> RunAsync(
        BenchEnvironment environment, ITileDensitySampler sampler, LodCalibrationSettings settings, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var calibrator = new LodCalibrator(sampler, settings);
        var started = timeProvider.GetTimestamp();
        var results = new List<BenchAssetTypeCalibration>(DatasetComposition.AssetTypes.Count);
        foreach (var type in DatasetComposition.AssetTypes)
        {
            var id = type.Id.ToString("D");
            var calibration = await calibrator.CalibrateAsync(id, type.MinZoom, type.MaxZoom, cancellationToken).ConfigureAwait(false);
            results.Add(new BenchAssetTypeCalibration(id, type.Name, type.MinZoom, type.MaxZoom, calibration));
        }

        return new LodCalibrationReport(environment, settings, results, timeProvider.GetElapsedTime(started).TotalMilliseconds);
    }

    /// <summary>The recommended policy: vector at every zoom by default, each calibrated type's recommendation as its layer's.</summary>
    public LodPolicy Policy() => new(
        LodThresholds.VectorOnly,
        AssetTypes.Where(t => t.Calibration.Recommended is not null)
            .ToDictionary(t => t.AssetTypeId, t => t.Calibration.Recommended!.Value, StringComparer.Ordinal));

    /// <summary>The policy a <c>calibrate --out</c> file recommends.</summary>
    /// <exception cref="InvalidOperationException">The file is not a calibration report.</exception>
    public static LodPolicy ReadPolicy(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(nameof(AssetTypes), out var types) || types.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("--lod must name a file written by 'calibrate --out'.");

        var layers = new Dictionary<string, LodThresholds>(StringComparer.Ordinal);
        foreach (var type in types.EnumerateArray())
        {
            var recommended = type.GetProperty(nameof(BenchAssetTypeCalibration.Calibration)).GetProperty(nameof(AssetTypeLodCalibration.Recommended));
            if (recommended.ValueKind == JsonValueKind.Null) continue;

            layers[type.GetProperty(nameof(BenchAssetTypeCalibration.AssetTypeId)).GetString()!] = new LodThresholds(
                recommended.GetProperty(nameof(LodThresholds.RasterFromZoom)).GetInt32(),
                recommended.GetProperty(nameof(LodThresholds.VectorFromZoom)).GetInt32());
        }

        return new LodPolicy(LodThresholds.VectorOnly, layers);
    }

    /// <summary>The tables <c>docs/perf/render-baseline.md</c> records, and the settings that apply the policy.</summary>
    public string ToMarkdown()
    {
        var inv = CultureInfo.InvariantCulture;
        var md = new StringBuilder();

        md.AppendLine("| Asset type | Zooms drawn | Seeds | Recommended raster from | Recommended vector from |");
        md.AppendLine("|---|---|---|---|---|");
        foreach (var t in AssetTypes)
        {
            var recommended = t.Calibration.Recommended;
            md.AppendLine(inv, $"| {t.Name} | {Zoom(t.MinZoom, "0")}–{Zoom(t.MaxZoom, "open")} | {t.Calibration.Seeds:N0} | {(recommended is { } r ? "z" + r.RasterFromZoom.ToString(inv) : "–")} | {(recommended is { } v ? "z" + v.VectorFromZoom.ToString(inv) : "–")} |");
        }

        md.AppendLine();
        md.AppendLine(inv, $"Calibrated in {ElapsedMs / 1000:F1} s. Budgets (p95 per tile): vector ≤ {Settings.VectorMaxFeaturesPerTile:N0} features and ≤ {Settings.VectorMaxVerticesPerTile:N0} vertices; raster ≤ {Settings.RasterMaxFeaturesPerTile:N0} features and ≤ {Settings.RasterMaxVerticesPerTile:N0} vertices.");
        md.AppendLine();

        foreach (var t in AssetTypes.Where(t => t.Calibration.Densities.Count > 0))
        {
            md.AppendLine(inv, $"#### {t.Name} — per zoom (features / vertices per tile: p50 · p95 · p99)");
            md.AppendLine();
            md.AppendLine("| Zoom | Tiles measured | Saturated | Features | Vertices | Fits |");
            md.AppendLine("|---|---|---|---|---|---|");
            foreach (var d in t.Calibration.Densities)
            {
                var fits = LodCalibration.FitsVector(d, Settings) ? "vector" : LodCalibration.FitsRaster(d, Settings) ? "raster" : "aggregate";
                md.AppendLine(inv, $"| z{d.Zoom} | {d.SampledTiles} | {d.SaturatedTiles} | {Percentiles(d.Features)} | {Percentiles(d.Vertices)} | {fits} |");
            }

            md.AppendLine();
        }

        md.AppendLine("```text");
        foreach (var t in AssetTypes)
        {
            if (t.Calibration.Recommended is not { } r || r == LodThresholds.VectorOnly) continue;
            md.AppendLine(inv, $"GeoAssets__Tiles__Lod__Layers__{t.AssetTypeId}__RasterFromZoom={r.RasterFromZoom}");
            md.AppendLine(inv, $"GeoAssets__Tiles__Lod__Layers__{t.AssetTypeId}__VectorFromZoom={r.VectorFromZoom}");
        }

        md.AppendLine("```");
        return md.ToString();
    }

    private static string Zoom(int? zoom, string open) => zoom?.ToString(CultureInfo.InvariantCulture) ?? open;

    private static string Percentiles(DensityPercentiles p) => string.Create(CultureInfo.InvariantCulture, $"{p.P50:N0} · {p.P95:N0} · {p.P99:N0}");
}

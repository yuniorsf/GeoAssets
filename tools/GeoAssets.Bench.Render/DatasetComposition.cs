namespace GeoAssets.Bench.Render;

/// <summary>The geometry a benchmark asset type's features carry.</summary>
internal enum BenchGeometryKind
{
    Point,
    LineString,
    Polygon,
}

/// <summary>
/// One asset type of the benchmark dataset: its share of the features, the zoom range the map draws it in
/// (<c>asset_type."MinZoom"</c>/<c>"MaxZoom"</c>, <c>null</c> = open) and the size of its geometries.
/// </summary>
/// <param name="VertexRange">Vertices per line, or per polygon exterior ring (closing vertex excluded); 1 for points.</param>
/// <param name="SizeMetersRange">Line length, or polygon radius, in meters; unused for points.</param>
internal sealed record BenchAssetType(
    Guid Id,
    string Name,
    string Code,
    BenchGeometryKind Kind,
    double Share,
    int? MinZoom,
    int? MaxZoom,
    (int Min, int Max) VertexRange,
    (double Min, double Max) SizeMetersRange,
    string Color)
{
    /// <summary>Whether the map draws this type at <paramref name="zoom"/> (the bounds are inclusive, like the client's).</summary>
    public bool IsDrawnAt(int zoom) => (MinZoom is null || zoom >= MinZoom) && (MaxZoom is null || zoom <= MaxZoom);
}

/// <summary>
/// The benchmark dataset's make-up (XD01-420): a power-distribution utility's assets — mostly poles, then transformers,
/// dense medium-voltage feeders, low-voltage service lines, parcels and large district outlines — each type with the
/// zoom range a real deployment would give it. Shares sum to 1; at the default 200,000 features the counts are
/// 120k / 20k / 15k / 25k / 15k / 5k.
/// </summary>
internal static class DatasetComposition
{
    public const int DefaultFeatureCount = 200_000;

    /// <summary>Every row the tool writes has an id with this prefix.</summary>
    public const string IdPrefix = "bench-render-";

    public static readonly IReadOnlyList<BenchAssetType> AssetTypes =
    [
        new(Guid.Parse("42000000-0000-0000-0000-000000000001"), "Bench — Poste", "POSTE", BenchGeometryKind.Point,
            0.600, MinZoom: 15, MaxZoom: null, (1, 1), (0, 0), "#f9e2af"),
        new(Guid.Parse("42000000-0000-0000-0000-000000000002"), "Bench — Transformador", "TRAFO", BenchGeometryKind.Point,
            0.100, MinZoom: 13, MaxZoom: null, (1, 1), (0, 0), "#fab387"),
        new(Guid.Parse("42000000-0000-0000-0000-000000000003"), "Bench — Rede MT", "REDE-MT", BenchGeometryKind.LineString,
            0.075, MinZoom: 10, MaxZoom: null, (64, 160), (800, 3_000), "#f38ba8"),
        new(Guid.Parse("42000000-0000-0000-0000-000000000004"), "Bench — Rede BT", "REDE-BT", BenchGeometryKind.LineString,
            0.125, MinZoom: 14, MaxZoom: null, (8, 32), (80, 400), "#89b4fa"),
        new(Guid.Parse("42000000-0000-0000-0000-000000000005"), "Bench — Lote", "LOTE", BenchGeometryKind.Polygon,
            0.075, MinZoom: 16, MaxZoom: null, (6, 24), (10, 40), "#a6e3a1"),
        new(Guid.Parse("42000000-0000-0000-0000-000000000006"), "Bench — Setor", "SETOR", BenchGeometryKind.Polygon,
            0.025, MinZoom: 8, MaxZoom: 13, (64, 256), (500, 2_500), "#cba6f7"),
    ];

    /// <summary>
    /// How many of <paramref name="totalFeatures"/> each type gets: its share rounded down, the remainder to the
    /// first (largest) type, so the counts always add up.
    /// </summary>
    public static IReadOnlyList<(BenchAssetType Type, int Count)> Counts(int totalFeatures)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalFeatures);

        var counts = AssetTypes.Select(t => (Type: t, Count: (int)Math.Floor((totalFeatures * t.Share) + 1e-9))).ToList();
        var remainder = totalFeatures - counts.Sum(c => c.Count);
        counts[0] = (counts[0].Type, counts[0].Count + remainder);
        return counts;
    }

    public static BenchAssetType? Find(string assetTypeId) =>
        Guid.TryParse(assetTypeId, out var id) ? AssetTypes.FirstOrDefault(t => t.Id == id) : null;
}

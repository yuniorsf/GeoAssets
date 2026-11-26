using System.Globalization;
using System.Text.Json;
using NetTopologySuite.Geometries;

namespace GeoAssets.Bench.Render;

/// <summary>One <c>geo_entity</c> row of the benchmark dataset.</summary>
internal sealed record BenchFeatureRow(
    string Id,
    string Name,
    BenchAssetType AssetType,
    string Description,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Geometry Geometry,
    string CustomAttributesJson);

/// <summary>
/// Generates the benchmark dataset (XD01-420) from a seed: the same seed and count always give the same rows, in the
/// same order — types interleaved the way features accumulate in a real database, not grouped. Coordinates are rounded
/// to 7 decimals (~1 cm), the precision of surveyed field data.
/// </summary>
internal sealed class DatasetGenerator(ulong seed)
{
    public const ulong DefaultSeed = 420;

    private const int CoordinateDecimals = 7;
    private const double MetersPerDegreeLat = 111_320.0;

    private static readonly DateTime Epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string[] States = ["ativo", "ativo", "ativo", "projetado", "inativo"];
    private static readonly string[] Materials = ["concreto", "madeira", "aco", "fibra"];

    private readonly GeometryFactory _factory = new(new PrecisionModel(), 4326);

    public DatasetLayout Layout { get; } = new(seed);

    public IEnumerable<BenchFeatureRow> Generate(int totalFeatures)
    {
        var rng = new SplitMix64(seed);
        var order = InterleavedTypes(totalFeatures, rng);
        var perType = new Dictionary<Guid, int>();

        for (var i = 0; i < order.Length; i++)
        {
            var type = order[i];
            var ordinal = perType[type.Id] = perType.GetValueOrDefault(type.Id) + 1;
            yield return CreateRow(i, ordinal, type, rng);
        }
    }

    private static BenchAssetType[] InterleavedTypes(int totalFeatures, SplitMix64 rng)
    {
        var order = DatasetComposition.Counts(totalFeatures)
            .SelectMany(c => Enumerable.Repeat(c.Type, c.Count))
            .ToArray();

        // Fisher–Yates with the seeded sequence.
        for (var i = order.Length - 1; i > 0; i--)
        {
            var j = (int)(rng.NextUInt64() % (ulong)(i + 1));
            (order[i], order[j]) = (order[j], order[i]);
        }

        return order;
    }

    private BenchFeatureRow CreateRow(int index, int ordinal, BenchAssetType type, SplitMix64 rng)
    {
        var (lon, lat) = Layout.SamplePosition(rng);
        var geometry = type.Kind switch
        {
            BenchGeometryKind.Point => (Geometry)_factory.CreatePoint(Coordinate(lon, lat)),
            BenchGeometryKind.LineString => CreateLine(type, lon, lat, rng),
            BenchGeometryKind.Polygon => CreatePolygon(type, lon, lat, rng),
            _ => throw new InvalidOperationException($"Unknown geometry kind {type.Kind}."),
        };

        var code = string.Create(CultureInfo.InvariantCulture, $"{type.Code}-{ordinal:D6}");
        var feeder = string.Create(CultureInfo.InvariantCulture, $"AL-{rng.NextInt(1, 48):D2}");
        var state = States[rng.NextInt(0, States.Length - 1)];
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["codigo"] = code,
            ["alimentador"] = feeder,
            ["situacao"] = state,
            ["material"] = Materials[rng.NextInt(0, Materials.Length - 1)],
            ["lote_obra"] = string.Create(CultureInfo.InvariantCulture, $"OBRA-{rng.NextInt(1, 2_000):D4}"),
        };

        var createdAt = Epoch.AddSeconds(index);
        return new BenchFeatureRow(
            Id: string.Create(CultureInfo.InvariantCulture, $"{DatasetComposition.IdPrefix}{index + 1:D7}"),
            Name: code,
            AssetType: type,
            Description: string.Create(CultureInfo.InvariantCulture, $"{type.Name} {code}, alimentador {feeder}, {state}"),
            CreatedAt: createdAt,
            UpdatedAt: createdAt.AddSeconds(index % 86_400),
            Geometry: geometry,
            CustomAttributesJson: JsonSerializer.Serialize(attributes));
    }

    private LineString CreateLine(BenchAssetType type, double lon, double lat, SplitMix64 rng)
    {
        var vertices = rng.NextInt(type.VertexRange.Min, type.VertexRange.Max);
        var stepMeters = rng.NextDouble(type.SizeMetersRange.Min, type.SizeMetersRange.Max) / (vertices - 1);
        var heading = rng.NextDouble(0, 2 * Math.PI);
        var coordinates = new Coordinate[vertices];
        coordinates[0] = Coordinate(lon, lat);

        for (var v = 1; v < vertices; v++)
        {
            heading += rng.NextGaussian() * 0.12;
            lat += Math.Sin(heading) * stepMeters / MetersPerDegreeLat;
            lon += Math.Cos(heading) * stepMeters / MetersPerDegreeLon(lat);
            coordinates[v] = Coordinate(lon, lat);
        }

        return _factory.CreateLineString(coordinates);
    }

    /// <summary>A star-shaped ring (strictly increasing angles around the center), so the polygon is always simple and valid.</summary>
    private Polygon CreatePolygon(BenchAssetType type, double lon, double lat, SplitMix64 rng)
    {
        var vertices = rng.NextInt(type.VertexRange.Min, type.VertexRange.Max);
        var radiusMeters = rng.NextDouble(type.SizeMetersRange.Min, type.SizeMetersRange.Max);
        var step = 2 * Math.PI / vertices;
        var ring = new Coordinate[vertices + 1];

        for (var v = 0; v < vertices; v++)
        {
            var angle = (v * step) + rng.NextDouble(-0.25 * step, 0.25 * step);
            var r = radiusMeters * rng.NextDouble(0.8, 1.2);
            ring[v] = Coordinate(
                lon + (Math.Cos(angle) * r / MetersPerDegreeLon(lat)),
                lat + (Math.Sin(angle) * r / MetersPerDegreeLat));
        }

        ring[vertices] = ring[0].Copy();
        return _factory.CreatePolygon(ring);
    }

    private static double MetersPerDegreeLon(double lat) => MetersPerDegreeLat * Math.Cos(lat * Math.PI / 180.0);

    private static Coordinate Coordinate(double lon, double lat) =>
        new(Math.Round(lon, CoordinateDecimals), Math.Round(lat, CoordinateDecimals));
}

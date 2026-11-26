using GeoAssets.Core.Models.Geometry;
using GeoAssets.Ingestion.Model;
using NetTopologySuite.Geometries;

namespace GeoAssets.Ingestion.Tests.Fakes;

internal static class TestData
{
    public const string ProfileId = "test-profile";
    public const string PoleNamespace = "PG";
    public const string DeviceNamespace = "DEV";

    private static readonly GeometryFactory Factory = new();

    public static readonly IReadOnlyList<AssetClassDefinition> AssetClasses =
    [
        new("Pole", "test:pole", Guid.Parse("11111111-0000-0000-0000-000000000001"), "Pole", GeometryType.Point, "#111111", []),
        new("Device", "test:device", Guid.Parse("11111111-0000-0000-0000-000000000002"), "Device", GeometryType.Point, "#222222", []),
        new("Segment", "test:segment", Guid.Parse("11111111-0000-0000-0000-000000000003"), "Segment", GeometryType.LineString, "#333333", []),
    ];

    public static Point Point(double x, double y) => Factory.CreatePoint(new Coordinate(x, y));

    public static LineString Line(params (double X, double Y)[] points) =>
        Factory.CreateLineString([.. points.Select(p => new Coordinate(p.X, p.Y))]);

    public static MultiLineString MultiLine(params LineString[] parts) => Factory.CreateMultiLineString(parts);

    public static MultiPoint MultiPoint(params Point[] parts) => Factory.CreateMultiPoint(parts);

    public static SourceKey PoleKey(long id) => new(ProfileId, PoleNamespace, id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static string PoleId(long id) => DeterministicFeatureId.For(PoleKey(id));

    public static ElementReference Ref(
        ReferenceRole role, string targetNamespace, string? targetKey, int? localNode = null, Point? snap = null, string? qualifier = null) =>
        new(role, targetNamespace, targetKey, localNode, snap, qualifier);

    public static CanonicalElement Element(
        SourceKey key,
        ElementKind kind,
        string assetClass,
        NetTopologySuite.Geometries.Geometry geometry,
        string scope,
        string? name = null,
        Completeness completeness = Completeness.Full,
        IReadOnlyList<ElementReference>? references = null,
        IReadOnlyDictionary<string, string>? attributes = null) => new()
        {
            Key = key,
            Kind = kind,
            AssetClass = assetClass,
            Geometry = geometry,
            Scope = scope,
            Name = name,
            Completeness = completeness,
            References = references ?? [],
            Attributes = attributes ?? new Dictionary<string, string>(StringComparer.Ordinal),
        };
}

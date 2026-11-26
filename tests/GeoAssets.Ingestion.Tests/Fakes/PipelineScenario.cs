using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Pipeline;
using GeoAssets.Ingestion.Profiles;
using GeoAssets.Ingestion.Sources;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace GeoAssets.Ingestion.Tests.Fakes;

internal sealed class PipelineScenario
{
    public const string DefaultCrs = "CRS-A";
    public const string ScopePrefix = "net";

    private readonly Dictionary<(string Region, string Layer), long> _recordCounters = [];
    private readonly List<LayerFileSet> _fileSets = [];

    public PipelineScenario()
    {
        Mapper = new ScriptedLayerMapper();
        Layers =
        [
            new LayerDefinition
            {
                Name = "LINES",
                FileStem = "LINES",
                Kind = ElementKind.Segment,
                ProcessingOrder = 10,
                SnapToleranceMeters = 1.0,
                MaxEndpointDistanceMeters = 5.0,
            },
            new LayerDefinition
            {
                Name = "DEVICES",
                FileStem = "DEVICES",
                Kind = ElementKind.Device,
                ProcessingOrder = 30,
                IndexKeys = [new IndexKeyDefinition(TestData.DeviceNamespace, "ID")],
            },
            new LayerDefinition
            {
                Name = "POLES",
                FileStem = "POLES",
                Kind = ElementKind.Structure,
                ProcessingOrder = 40,
                SnapTarget = true,
                IndexKeys = [new IndexKeyDefinition(TestData.PoleNamespace, "ID")],
            },
        ];
    }

    public InMemoryFeatureSource Source { get; } = new();

    public ScriptedLayerMapper Mapper { get; }

    public RecordingFeatureSink Sink { get; } = new();

    public IdentityCoordinateTransformerFactory Transformers { get; } = new();

    public List<LayerDefinition> Layers { get; }

    public List<ImportIssue> InventoryIssues { get; } = [];

    public LayerFileSet AddLayer(string layer, string region = "", LayerVerdict verdict = LayerVerdict.Usable, string? crsWkt = DefaultCrs)
    {
        var fileSet = new LayerFileSet
        {
            Layer = layer,
            Region = region,
            Verdict = verdict,
            CrsWkt = crsWkt,
            GeometryPath = $"{region}/{layer}.shp",
        };
        _fileSets.Add(fileSet);
        return fileSet;
    }

    public static string ScopeOf(LayerFileSet layer) =>
        layer.Region.Length == 0 ? $"{ScopePrefix}/{layer.Layer}" : $"{ScopePrefix}/{layer.Region}/{layer.Layer}";

    public void AddElement(
        LayerFileSet layer,
        Func<string, CanonicalElement> make,
        IReadOnlyDictionary<string, object?>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(make);
        var counterKey = (layer.Region, layer.Layer);
        var number = _recordCounters.GetValueOrDefault(counterKey) + 1;
        _recordCounters[counterKey] = number;
        NtsGeometry geometry = make(ScopeOf(layer)).Geometry;
        Source.Add(layer, new RawRecord(layer, number, geometry, attributes ?? new Dictionary<string, object?>(StringComparer.Ordinal)));
        Mapper.Script(layer.Region, layer.Layer, number, scope => MappingResult.Mapped(make(scope)));
    }

    public string AddPole(
        LayerFileSet layer,
        long id,
        double x,
        double y,
        string? name = null,
        Completeness completeness = Completeness.Full,
        string assetClass = "Pole",
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        AddElement(
            layer,
            scope => TestData.Element(
                TestData.PoleKey(id), ElementKind.Structure, assetClass, TestData.Point(x, y), scope, name, completeness, attributes: attributes),
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["ID"] = id });
        return TestData.PoleId(id);
    }

    public void AddSegment(
        LayerFileSet layer,
        long id,
        NtsGeometry geometry,
        IReadOnlyList<ElementReference> references,
        Completeness completeness = Completeness.Full,
        string assetClass = "Segment") =>
        AddElement(
            layer,
            scope => TestData.Element(
                new SourceKey(TestData.ProfileId, "SEG", id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ElementKind.Segment,
                assetClass,
                geometry,
                scope,
                completeness: completeness,
                references: references));

    public static string SegmentId(long id) =>
        DeterministicFeatureId.For(new SourceKey(TestData.ProfileId, "SEG", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    public ImportRequest Request(Func<ImportRequest, ImportRequest>? customize = null)
    {
        var profile = new FakeSourceProfile(Layers, TestData.AssetClasses, Mapper);
        var request = new ImportRequest
        {
            Inventory = new DeliveryInventory("/delivery", TestData.ProfileId, _fileSets, InventoryIssues),
            Profile = profile,
            ScopePrefix = ScopePrefix,
            OrganizationId = Guid.Parse("0000000a-0000-0000-0000-000000000001"),
        };
        return customize is null ? request : customize(request);
    }

    public Task<ImportReport> RunAsync(Func<ImportRequest, ImportRequest>? customize = null, CancellationToken cancellationToken = default) =>
        new ImportPipeline(Source, Transformers).RunAsync(Request(customize), Sink, cancellationToken);
}

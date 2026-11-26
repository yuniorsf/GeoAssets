using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles.Electric;

namespace GeoAssets.Ingestion.Profiles.EquatorialQgisMunicipal;

/// <summary>
/// Equatorial (CEMAR) municipal QGIS extracts — one folder per municipality, SAD69 / UTM 23S, full field names:
/// <c>PGS</c> (poles, snapped to by coordinates), <c>TRAFOS</c> (installations, indexed by <c>INST</c> and the
/// group-anchor candidates of <c>TRAFO</c>), <c>TRECHO_PRIMARIO</c> (MV segments) and <c>TRECHO_SECUNDARIO</c> (LV
/// segments drawn 1 m off their poles, anchored to their transformer by circuit <c>MSLINK_INS</c>). File names carry
/// the municipality (<c>shape-trafos-paco-do-lumiar</c>, <c>shapes-pgs-sao-luis</c>…), so layers match by pattern.
/// Segments are processed first so the LV segments' <c>FedBy</c> edges attach to the transformers projected after them.
/// </summary>
public sealed class EquatorialQgisMunicipalProfile : ISourceProfile
{
    private const double SnapToleranceMeters = 1.0;
    private const double MaxEndpointDistanceMeters = 5.0;

    private static readonly IReadOnlyDictionary<string, string> NoAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, ILayerMapper> _mappers = new(StringComparer.OrdinalIgnoreCase)
    {
        [EquatorialFields.LayerTrechoSecundario] = new TrechoSecundarioLayerMapper(),
        [EquatorialFields.LayerTrechoPrimario] = new TrechoPrimarioLayerMapper(),
        [EquatorialFields.LayerTrafos] = new TrafosLayerMapper(),
        [EquatorialFields.LayerPgs] = new PgsLayerMapper(),
    };

    public string Id => EquatorialFields.ProfileId;

    public IReadOnlyList<LayerDefinition> Layers { get; } =
    [
        Layer(EquatorialFields.LayerTrechoSecundario, EquatorialFields.StemTrechoSecundario, EquatorialFields.PatternTrechoSecundario, ElementKind.Segment, 10) with
        {
            CollectsGroupAnchors = true,
            DrawingOffsetX = EquatorialFields.LvDrawingOffsetX,
            DrawingOffsetY = EquatorialFields.LvDrawingOffsetY,
        },
        Layer(EquatorialFields.LayerTrechoPrimario, EquatorialFields.StemTrechoPrimario, EquatorialFields.PatternTrechoPrimario, ElementKind.Segment, 20),
        Layer(EquatorialFields.LayerTrafos, EquatorialFields.StemTrafos, EquatorialFields.PatternTrafos, ElementKind.Device, 30) with
        {
            IndexKeys = [new IndexKeyDefinition(EquatorialFields.NamespaceInst, EquatorialFields.InstallationNumber)],
            AnchorNamespace = EquatorialFields.NamespaceTrafo,
        },
        Layer(EquatorialFields.LayerPgs, EquatorialFields.StemPgs, EquatorialFields.PatternPgs, ElementKind.Structure, 40) with
        {
            SnapTarget = true,
        },
    ];

    public IReadOnlyList<AssetClassDefinition> AssetClasses => ElectricAssetClasses.All;

    public IReadOnlyDictionary<string, string> FallbackFieldAliases(string layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return NoAliases;
    }

    public ILayerMapper MapperFor(string layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return _mappers.TryGetValue(layer, out var mapper)
            ? mapper
            : throw new KeyNotFoundException($"The {EquatorialFields.ProfileId} profile has no layer '{layer}'.");
    }

    private static LayerDefinition Layer(string name, string fileStem, string fileStemPattern, ElementKind kind, int order) => new()
    {
        Name = name,
        FileStem = fileStem,
        FileStemPattern = fileStemPattern,
        Kind = kind,
        SnapToleranceMeters = SnapToleranceMeters,
        MaxEndpointDistanceMeters = MaxEndpointDistanceMeters,
        ProcessingOrder = order,
    };
}

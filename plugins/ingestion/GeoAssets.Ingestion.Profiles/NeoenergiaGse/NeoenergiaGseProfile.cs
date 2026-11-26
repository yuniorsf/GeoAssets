using GeoAssets.Ingestion.Model;

namespace GeoAssets.Ingestion.Profiles.NeoenergiaGse;

/// <summary>
/// COELBA's GSE (NEOSDE) export: <c>POSTE</c> (structures, indexed and snapped to by key <c>PG</c>),
/// <c>INSTALACAO</c> (MV devices, indexed by <c>INST</c> and <c>CHAVE</c>) and the <c>REDE_BT</c>/<c>REDE_MT</c>/
/// <c>REDE_LT</c> segments, whose topology is keyed by PG and local node. Segments are processed first so the LV
/// segments' <c>FedBy</c> edges attach to the installations projected after them.
/// </summary>
public sealed class NeoenergiaGseProfile : ISourceProfile
{
    private const double SnapToleranceMeters = 1.0;
    private const double MaxEndpointDistanceMeters = 5.0;

    private readonly Dictionary<string, ILayerMapper> _mappers = new(StringComparer.OrdinalIgnoreCase)
    {
        [GseFields.LayerRedeBt] = new RedeBtLayerMapper(),
        [GseFields.LayerRedeMt] = new RedeMtLayerMapper(),
        [GseFields.LayerRedeLt] = new RedeLtLayerMapper(),
        [GseFields.LayerInstalacao] = new InstalacaoLayerMapper(),
        [GseFields.LayerPoste] = new PosteLayerMapper(),
    };

    /// <summary>The code tables this profile keeps as raw codes (asked for in the delivery's data request).</summary>
    public static IReadOnlyList<string> UndecodedCodeTables { get; } =
    [
        "CD_TIPO_PG",
        "CD_INDD_MATERIAL_PG",
        "CD_FASE",
        "CD_TENSAO_NOMINAL",
        "CD_TRAJETO",
        "NU_CONDUTOR_*_ID (conductor catalog)",
    ];

    public string Id => GseFields.ProfileId;

    public IReadOnlyList<LayerDefinition> Layers { get; } =
    [
        Layer(GseFields.LayerRedeBt, ElementKind.Segment, 10),
        Layer(GseFields.LayerRedeMt, ElementKind.Segment, 20),
        Layer(GseFields.LayerRedeLt, ElementKind.Segment, 20),
        Layer(GseFields.LayerInstalacao, ElementKind.Device, 30) with
        {
            IndexKeys =
            [
                new IndexKeyDefinition(GseFields.NamespaceInst, GseFields.InstallationId),
                new IndexKeyDefinition(GseFields.NamespaceChave, GseFields.SwitchId),
            ],
        },
        Layer(GseFields.LayerPoste, ElementKind.Structure, 40) with
        {
            IndexKeys = [new IndexKeyDefinition(GseFields.NamespacePg, GseFields.PgId)],
            SnapTarget = true,
        },
    ];

    public IReadOnlyList<AssetClassDefinition> AssetClasses => GseAssetClasses.All;

    public IReadOnlyDictionary<string, string> FallbackFieldAliases(string layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return GseFieldAliases.For(layer);
    }

    public ILayerMapper MapperFor(string layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return _mappers.TryGetValue(layer, out var mapper)
            ? mapper
            : throw new KeyNotFoundException($"The {GseFields.ProfileId} profile has no layer '{layer}'.");
    }

    private static LayerDefinition Layer(string name, ElementKind kind, int order) => new()
    {
        Name = name,
        FileStem = name,
        Kind = kind,
        SnapToleranceMeters = SnapToleranceMeters,
        MaxEndpointDistanceMeters = MaxEndpointDistanceMeters,
        ProcessingOrder = order,
    };
}

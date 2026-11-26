using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Sources;

namespace GeoAssets.Ingestion.Profiles.NeoenergiaGse;

/// <summary>
/// <c>INSTALACAO</c>: MV installations keyed (<c>INST</c>, <c>NU_INSTALACAO_MT_ID</c>), classified by
/// <c>DE_SIGLA_1</c>, named "{DE_SIGLA_1} {DE_PLACA ?? NU_INSTALACAO_MT_ID}". References: <c>Location</c> → the PG
/// <c>NU_PG_ID</c> at node <c>NU_NO</c> (snapping from the device point), <c>Connection</c> → the PG <c>NU_PG_P_ID</c>
/// at node <c>NU_NO_P</c> (when set). Geometry-only records are <c>MvDevice</c>s located by snap only.
/// </summary>
internal sealed class InstalacaoLayerMapper : ILayerMapper
{
    public MappingResult Map(RawRecord record, string scope)
    {
        ArgumentNullException.ThrowIfNull(record);
        var key = GseElements.Key(record, GseFields.NamespaceInst, GseFields.InstallationId);
        var devicePoint = GseGeometry.Representative(record.Geometry);
        if (GseElements.IsGeometryOnly(record))
        {
            ElementReference[] located = [new(ReferenceRole.Location, GseFields.NamespacePg, null, SnapPoint: devicePoint)];
            return MappingResult.Mapped(GseElements.Create(record, scope, key, ElementKind.Device, GseAssetClasses.MvDevice, null, located));
        }

        var attributes = record.Attributes;
        var acronym = GseValues.Text(attributes, GseFields.Acronym);
        var label = GseValues.Text(attributes, GseFields.Plate) ?? GseValues.Key(attributes, GseFields.InstallationId);
        var name = acronym is null ? label : label is null ? acronym : $"{acronym} {label}";

        var references = new List<ElementReference>
        {
            new(ReferenceRole.Location, GseFields.NamespacePg, GseValues.Key(attributes, GseFields.PgId),
                GseValues.Node(attributes, GseFields.Node), devicePoint),
        };
        if (GseValues.Key(attributes, GseFields.ParentPgId) is { } parent)
            references.Add(new(ReferenceRole.Connection, GseFields.NamespacePg, parent, GseValues.Node(attributes, GseFields.ParentNode)));

        return MappingResult.Mapped(GseElements.Create(record, scope, key, ElementKind.Device, Classify(acronym), name, references));
    }

    internal static string Classify(string? acronym)
    {
        var code = acronym?.Trim().ToUpperInvariant() ?? string.Empty;
        return code switch
        {
            "AUTRF" => GseAssetClasses.Transformer,
            "DJ" => GseAssetClasses.Breaker,
            "RELIG" => GseAssetClasses.Recloser,
            "REGUL" => GseAssetClasses.VoltageRegulator,
            "CUBIL" => GseAssetClasses.Cubicle,
            _ when code.StartsWith("TF", StringComparison.Ordinal) => GseAssetClasses.Transformer,
            _ when code.StartsWith("BC", StringComparison.Ordinal) => GseAssetClasses.CapacitorBank,
            _ when code.StartsWith("CH", StringComparison.Ordinal) || code.StartsWith("CF", StringComparison.Ordinal)
                || code.StartsWith("SEC", StringComparison.Ordinal) => GseAssetClasses.Switch,
            _ => GseAssetClasses.MvDevice,
        };
    }
}

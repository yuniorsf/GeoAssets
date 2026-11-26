using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles.Common;
using GeoAssets.Ingestion.Profiles.Electric;
using GeoAssets.Ingestion.Sources;

namespace GeoAssets.Ingestion.Profiles.EquatorialQgisMunicipal;

/// <summary>
/// <c>PGS</c>: poles keyed by the coordinates of their point (<c>PG</c>; <c>PGF_BARRAM</c> is a label, not unique),
/// named "Poste {PGF_BARRAM}" when it is set. No references.
/// </summary>
internal sealed class PgsLayerMapper : ILayerMapper
{
    public MappingResult Map(RawRecord record, string scope)
    {
        ArgumentNullException.ThrowIfNull(record);
        var key = EquatorialElements.CoordinateKey(record, EquatorialFields.NamespacePg);
        var label = SourceValues.Text(record.Attributes, EquatorialFields.PoleId);
        var name = label is null ? null : $"Poste {label}";
        return MappingResult.Mapped(EquatorialElements.Create(record, scope, key, ElementKind.Structure, ElectricAssetClasses.Pole, name, []));
    }
}

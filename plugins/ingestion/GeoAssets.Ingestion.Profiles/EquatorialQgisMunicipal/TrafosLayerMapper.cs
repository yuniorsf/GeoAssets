using System.Globalization;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles.Common;
using GeoAssets.Ingestion.Profiles.Electric;
using GeoAssets.Ingestion.Sources;

namespace GeoAssets.Ingestion.Profiles.EquatorialQgisMunicipal;

/// <summary>
/// <c>TRAFOS</c>: installations keyed (<c>INST</c>, <c>INT_NUM</c>) — the coordinate key of the point without one —,
/// named "Trafo {INT_NUM}". A <c>TB_IN_DESC</c> mentioning "transformadora" (ignoring case and accents) is a
/// <c>Transformer</c>, anything else (and a geometry-only record) an <c>MvDevice</c>. <c>Location</c> → the pole under
/// the device point, by snap only.
/// </summary>
internal sealed class TrafosLayerMapper : ILayerMapper
{
    public MappingResult Map(RawRecord record, string scope)
    {
        ArgumentNullException.ThrowIfNull(record);
        var attributes = record.Attributes;
        var number = SourceValues.Text(attributes, EquatorialFields.InstallationNumber);
        var key = EquatorialElements.Key(record, EquatorialFields.NamespaceInst, number);
        ElementReference[] references =
        [
            new(ReferenceRole.Location, EquatorialFields.NamespacePg, null, SnapPoint: SourceGeometry.Representative(record.Geometry)),
        ];
        var assetClass = Classify(SourceValues.Text(attributes, EquatorialFields.InstallationDescription));
        var name = number is null ? null : $"Trafo {number}";
        return MappingResult.Mapped(EquatorialElements.Create(record, scope, key, ElementKind.Device, assetClass, name, references));
    }

    internal static string Classify(string? description) =>
        description is not null
        && CultureInfo.InvariantCulture.CompareInfo.IndexOf(
            description, EquatorialFields.TransformerMarker, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0
            ? ElectricAssetClasses.Transformer
            : ElectricAssetClasses.MvDevice;
}

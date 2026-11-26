using GeoAssets.Core.FeatureImport;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Ingestion.Pipeline;

namespace GeoAssets.Ingestion.Projection;

/// <summary>
/// Projects a validated, reprojected element to a <see cref="GeoFeature"/>: deterministic id, name (fallback
/// <c>"{AssetClass} {NaturalKey}"</c>, at most 256 characters), the sink's asset type, the organization, SRID 4326,
/// the element's attributes plus the provenance attributes, and its edges.
/// </summary>
internal sealed class GeoFeatureProjector(string profileId, Guid organizationId, IFeatureSink sink)
{
    public const int MaxNameLength = 256;

    public GeoFeature Project(ProjectionInput input)
    {
        var element = input.Element.Element;
        var feature = new GeoFeature
        {
            Id = input.Id.Text,
            Geometry = GeoGeometry.FromNts(input.GeographicGeometry),
            Properties = new GeoFeatureProperties
            {
                Name = NameOf(element.Name, element.AssetClass, element.Key.NaturalKey),
                AssetTypeId = sink.AssetTypeIdFor(element.AssetClass),
                OrganizationId = organizationId,
                Srid = Reprojection.GeographicSrid,
            },
        };

        var attributes = feature.Properties.CustomAttributes;
        foreach (var (key, value) in element.Attributes)
        {
            attributes[key] = value;
        }

        attributes[FeatureImportAttributes.Profile] = profileId;
        attributes[FeatureImportAttributes.Key] = element.Key.ToString();
        attributes[FeatureImportAttributes.Scope] = input.Scope;
        attributes[FeatureImportAttributes.Completeness] = CompletenessLabels.For(input.Completeness);
        attributes[FeatureImportAttributes.PositionAccuracy] = input.PositionAccuracy;
        feature.Topology.AddRange(input.Edges);
        return feature;
    }

    private static string NameOf(string? name, string assetClass, string naturalKey)
    {
        var resolved = name ?? string.Concat(assetClass, " ", naturalKey);
        return resolved.Length <= MaxNameLength ? resolved : resolved[..MaxNameLength];
    }
}

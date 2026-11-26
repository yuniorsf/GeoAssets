using System.Globalization;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Pipeline;
using GeoAssets.Ingestion.Resolution;

namespace GeoAssets.Ingestion.Validation;

/// <summary>
/// A segment's Start/End resolved by key whose geometry endpoint lies farther than the layer's
/// <see cref="Profiles.LayerDefinition.MaxEndpointDistanceMeters"/> from the target's indexed point is reported as
/// <see cref="ImportIssueCodes.EndpointDistance"/> (warning).
/// </summary>
internal sealed class EndpointDistanceRule(ReferenceIndex index) : IReferenceRule
{
    public void Check(ReferenceRuleContext context, IssueLog issues)
    {
        if (context.Element.Element.Kind != ElementKind.Segment)
        {
            return;
        }

        var coordinates = context.Element.NativeGeometry.Coordinates;
        if (coordinates.Length == 0)
        {
            return;
        }

        foreach (var resolved in context.Resolved)
        {
            var reference = resolved.Reference;
            if (resolved.Method != ResolutionMethod.Key
                || reference.Role is not (ReferenceRole.Start or ReferenceRole.End)
                || reference.TargetKey is null
                || !index.TryGet(new IndexKey(reference.TargetNamespace, reference.TargetKey.Trim()), out var target))
            {
                continue;
            }

            var endpoint = reference.Role == ReferenceRole.Start ? coordinates[0] : coordinates[^1];
            var distance = target.DistanceTo(endpoint.X, endpoint.Y);
            if (distance > context.Layer.MaxEndpointDistanceMeters)
            {
                issues.Add(
                    IssueSeverity.Warning,
                    ImportIssueCodes.EndpointDistance,
                    context.Element.Element.Key.ToString(),
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{reference.Role} endpoint is {distance:F2} m from {reference.TargetNamespace} {reference.TargetKey} (max {context.Layer.MaxEndpointDistanceMeters:F2} m)."));
            }
        }
    }
}

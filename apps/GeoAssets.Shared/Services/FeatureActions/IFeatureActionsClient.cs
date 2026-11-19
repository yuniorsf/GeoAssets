using GeoAssets.Workflow.Registry;

namespace GeoAssets.Shared.Services.FeatureActions;

/// <summary>
/// The feature actions the kernel offers on one feature (XD01-370): <c>GET /api/geoassets/features/{id}/actions</c> — the
/// order-type registry's actions for the feature's asset-type roles that the caller may create (XD01-369). The client
/// knows no vertical; it renders whatever the kernel returns.
/// </summary>
public interface IFeatureActionsClient
{
    /// <summary>The actions for <paramref name="featureId"/>; empty for a feature the caller cannot see.</summary>
    Task<IReadOnlyList<FeatureActionDescriptor>> GetActionsAsync(string featureId, CancellationToken ct);
}

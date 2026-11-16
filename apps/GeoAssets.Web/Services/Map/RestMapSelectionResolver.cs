using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using GeoAssets.Workflow.Rest;

namespace GeoAssets.Web.Services.Map;

/// <summary>
/// REST adapter for <see cref="IMapSelectionResolver"/> (XD01-310): normalises the gesture with
/// <see cref="MapSelectionRequest.FromGesture"/> and runs it through the server's
/// <c>POST selection/resolve</c> via <see cref="RestMapSelectionClient"/>. It lives in the host
/// because <c>GeoAssets.Shared</c> owns the port but does not reference the workflow REST client.
/// The client's exceptions pass through unchanged — they are already the domain's
/// (<c>SelectionTooLargeException</c>, <c>InvalidSelectionFenceException</c>, …).
/// </summary>
internal sealed class RestMapSelectionResolver(RestMapSelectionClient client) : IMapSelectionResolver
{
    /// <remarks>An unusable area is rejected by <see cref="MapSelectionRequest.FromGesture"/>
    /// before any request is sent.</remarks>
    public Task<IReadOnlyList<string>> ResolveAsync(MapSelectionGesture gesture, CancellationToken ct)
    {
        var request = MapSelectionRequest.FromGesture(gesture);
        return client.ResolveAsync(request.StrategyId, request.Parameters, ct);
    }
}

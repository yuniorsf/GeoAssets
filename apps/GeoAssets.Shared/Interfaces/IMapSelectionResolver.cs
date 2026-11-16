namespace GeoAssets.Shared.Interfaces;

/// <summary>
/// Port that turns an area gesture into the ids of the features it selects (XD01-310). The
/// REST-backed adapter lives in the host (it needs the workflow REST client, which this library
/// does not reference).
/// </summary>
public interface IMapSelectionResolver
{
    /// <summary>
    /// Resolves <paramref name="gesture"/> to feature ids. Throws
    /// <see cref="GeoAssets.Core.Services.InvalidSelectionFenceException"/> for an unusable area,
    /// <see cref="GeoAssets.Workflow.Targets.SelectionTooLargeException"/> when it matches more
    /// features than one selection may hold, and <see cref="NotSupportedException"/> when the host
    /// cannot resolve areas at all.
    /// </summary>
    Task<IReadOnlyList<string>> ResolveAsync(MapSelectionGesture gesture, CancellationToken ct);
}

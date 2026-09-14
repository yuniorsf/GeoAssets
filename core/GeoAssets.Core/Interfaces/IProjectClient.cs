using GeoAssets.Core.Models;

namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Client-facing surface over the Projects REST API (<c>GeoAssets.Server</c>'s
/// <c>ProjectsRestApiExtensions</c>, XD01-141) — deliberately narrower than
/// <see cref="IProjectRepository"/>, which describes the server's own whole-row persistence
/// contract. The REST API exposes four independent per-scope mutation endpoints (not a
/// whole-row PUT) so each can carry its own <c>projects:manage-*</c> authorization and
/// copy-on-write fallback; this interface mirrors that shape instead of forcing a mismatched
/// whole-row <c>UpdateAsync</c> onto a client that can't actually call one.
///
/// Each <c>UpdateXAsync</c> returns the <see cref="Project"/> the mutation actually landed on —
/// the same <see cref="Project.Id"/> passed in when the caller holds the permission directly, or
/// a different (fork) id when copy-on-write redirected it (XD01-141). Throws
/// <see cref="UnauthorizedAccessException"/> on a 403 (caller lacks even <c>projects:read</c>)
/// and <see cref="KeyNotFoundException"/> on a 404.
/// </summary>
public interface IProjectClient
{
    /// <summary>Returns null on a 404. Throws <see cref="UnauthorizedAccessException"/> on a 403.</summary>
    Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Project> UpdateProvidersAsync(Guid id, List<ProjectProviderEntry>? providers, CancellationToken ct = default);

    Task<Project> UpdateAssetTypeScopeAsync(Guid id, ProjectAssetTypeScope? scope, CancellationToken ct = default);

    Task<Project> UpdateLayerScopeAsync(Guid id, ProjectLayerScope? scope, CancellationToken ct = default);

    Task<Project> UpdateViewStateAsync(Guid id, ProjectViewState? viewState, CancellationToken ct = default);
}

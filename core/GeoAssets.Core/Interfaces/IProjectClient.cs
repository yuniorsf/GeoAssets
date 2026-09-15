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

    /// <summary>Every Project (General and User) belonging to <paramref name="organizationId"/> —
    /// callers filter by <see cref="Project.Kind"/>/<see cref="Project.CreatedByUserId"/> themselves
    /// (e.g. XD01-145's ProjectPanel: the org's General Projects plus the caller's own forks).</summary>
    Task<IReadOnlyList<Project>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new <see cref="ProjectKind.User"/> Project ("Save As", XD01-145) forked from
    /// <paramref name="project"/>.<see cref="Project.ParentProjectId"/> — must reference a
    /// <see cref="ProjectKind.General"/> Project the caller can at least read. Only
    /// <see cref="Project.Name"/>/<see cref="Project.Description"/>/<see cref="Project.Providers"/>/
    /// <see cref="Project.AssetTypeScope"/>/<see cref="Project.LayerScope"/>/<see cref="Project.ViewState"/>
    /// are read from <paramref name="project"/> — every other field (id, organization, owner, kind,
    /// timestamps) is derived server-side.
    /// </summary>
    Task<Project> CreateAsync(Project project, CancellationToken ct = default);

    Task<Project> UpdateProvidersAsync(Guid id, List<ProjectProviderEntry>? providers, CancellationToken ct = default);

    Task<Project> UpdateAssetTypeScopeAsync(Guid id, ProjectAssetTypeScope? scope, CancellationToken ct = default);

    Task<Project> UpdateLayerScopeAsync(Guid id, ProjectLayerScope? scope, CancellationToken ct = default);

    Task<Project> UpdateViewStateAsync(Guid id, ProjectViewState? viewState, CancellationToken ct = default);
}

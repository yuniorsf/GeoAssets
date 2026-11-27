using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;

namespace GeoAssets.MAUI.Services.Projects;

/// <summary>
/// The app's <see cref="IProjectClient"/> (XD01-476): Projects are a Web workspace, so this app lists none — every
/// organization has no Project here, an id finds none, and a write is refused. See <see cref="NoProjectSessionService"/>
/// for why the app doesn't read the Projects API it could reach through the gateway.
/// </summary>
internal sealed class NoProjectClient : IProjectClient
{
    public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Project?>(null);

    public Task<IReadOnlyList<Project>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Project>>([]);

    public Task<Project> CreateAsync(Project project, CancellationToken ct = default) => Task.FromException<Project>(Unavailable());

    public Task<Project> UpdateProvidersAsync(Guid id, List<ProjectProviderEntry>? providers, CancellationToken ct = default) =>
        Task.FromException<Project>(Unavailable());

    public Task<Project> UpdateAssetTypeScopeAsync(Guid id, ProjectAssetTypeScope? scope, CancellationToken ct = default) =>
        Task.FromException<Project>(Unavailable());

    public Task<Project> UpdateLayerScopeAsync(Guid id, ProjectLayerScope? scope, CancellationToken ct = default) =>
        Task.FromException<Project>(Unavailable());

    public Task<Project> UpdateViewStateAsync(Guid id, ProjectViewState? viewState, CancellationToken ct = default) =>
        Task.FromException<Project>(Unavailable());

    internal static NotSupportedException Unavailable() => new("Projects are not available in the mobile/desktop app.");
}

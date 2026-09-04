using GeoAssets.Core.Models;

namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Read-only query surface over <see cref="Project"/>. Segregated from <see cref="IProjectWriter"/>
/// so a consumer that only ever reads projects (a reporting view, a read replica) can depend on
/// this alone. <see cref="IProjectRepository"/> composes both for the common case of needing full
/// read/write access.
///
/// Every method returns the <b>raw</b> stored row exactly as persisted — including preserved
/// <c>null</c>s on the four scope properties of a <see cref="ProjectKind.User"/> row. Resolving
/// those against the parent Project is a separate concern, handled by
/// <see cref="Services.ProjectResolver"/> one layer up, not here.
/// </summary>
public interface IProjectReader
{
    Task<Project?>               GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Project>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>Returns every <see cref="ProjectKind.User"/> Project forked from the given
    /// <see cref="ProjectKind.General"/> parent.</summary>
    Task<IReadOnlyList<Project>> GetForksOfAsync(Guid parentProjectId, CancellationToken ct = default);
}

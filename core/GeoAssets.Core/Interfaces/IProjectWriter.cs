using GeoAssets.Core.Models;

namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Write surface over <see cref="Project"/>, plus the events writes raise. Segregated from
/// <see cref="IProjectReader"/> so a consumer that only ever records changes (a command handler)
/// can depend on this alone. <see cref="IProjectRepository"/> composes both for the common case
/// of needing full read/write access.
/// </summary>
public interface IProjectWriter
{
    Task AddAsync(Project project, CancellationToken ct = default);

    /// <summary>
    /// Persists the mutable fields of <paramref name="project"/> — name, description, schema
    /// version, and the four scope properties. Ownership/identity fields set at creation
    /// (<see cref="Project.OrganizationId"/>, <see cref="Project.CreatedByUserId"/>,
    /// <see cref="Project.CreatedAt"/>, <see cref="Project.Kind"/>, <see cref="Project.ParentProjectId"/>)
    /// are left untouched. Throws <see cref="KeyNotFoundException"/> if no Project with this ID was
    /// previously added.
    /// </summary>
    Task UpdateAsync(Project project, CancellationToken ct = default);

    /// <summary>
    /// Soft-deletes the Project — flags it deleted rather than removing the row, so it never
    /// appears in a normal query again but still exists in the database. <c>projects:delete</c>
    /// (wired in XD01-141) never hard-deletes. A no-op if the Project doesn't exist or is already
    /// deleted.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    // ── Events ────────────────────────────────────────────────────────────────

    event EventHandler<Project>? ProjectSaved;
    event EventHandler<Guid>?    ProjectDeleted;
}

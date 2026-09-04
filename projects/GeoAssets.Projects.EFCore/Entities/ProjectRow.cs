using GeoAssets.Core.Models;

namespace GeoAssets.Projects.Persistence.Entities;

/// <summary>
/// EF Core entity that maps to the <c>Projects</c> table.
/// Kept separate from the domain <see cref="Project"/> so the domain model stays free of EF
/// annotations and infrastructure concerns.
///
/// The four scope properties on <see cref="Project"/> (<see cref="Project.Providers"/>,
/// <see cref="Project.AssetTypeScope"/>, <see cref="Project.LayerScope"/>,
/// <see cref="Project.ViewState"/>) are stored as nullable JSON text columns — mirroring
/// <c>ServiceOrderRecord.SelectionSpecJson</c>'s "nullable, no default" convention, not
/// <c>AttributesJson</c>'s "always populated" one, since <c>null</c> here is domain-significant
/// (inherit from parent) and must round-trip as <c>null</c>, never collapse to <c>"[]"</c>/<c>"{}"</c>.
/// </summary>
internal sealed class ProjectRow
{
    public Guid   Id          { get; set; } = Guid.NewGuid();
    public string Name        { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public Guid OrganizationId  { get; set; } = Guid.Empty;
    public Guid CreatedByUserId { get; set; } = Guid.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public int         SchemaVersion { get; set; } = Project.CurrentSchemaVersion;
    public ProjectKind Kind          { get; set; }

    /// <summary>FK to the parent General Project, or null for a General Project itself.</summary>
    public Guid? ParentProjectId { get; set; }

    /// <summary>JSON-serialised <c>List&lt;ProjectProviderEntry&gt;</c>, or null.</summary>
    public string? ProvidersJson { get; set; }

    /// <summary>JSON-serialised <see cref="ProjectAssetTypeScope"/>, or null.</summary>
    public string? AssetTypeScopeJson { get; set; }

    /// <summary>JSON-serialised <see cref="ProjectLayerScope"/>, or null.</summary>
    public string? LayerScopeJson { get; set; }

    /// <summary>JSON-serialised <see cref="ProjectViewState"/>, or null.</summary>
    public string? ViewStateJson { get; set; }

    // ── Soft delete (XD01-139) ───────────────────────────────────────────────────
    // New convention for this codebase — no existing IsDeleted/HasQueryFilter precedent to
    // match (the closest is a plain, unfiltered IsActive flag on Organization/AppUser/etc.).
    // See ProjectRowConfiguration.HasQueryFilter.

    public bool     IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

using GeoAssets.Core.Interfaces;

namespace GeoAssets.Core.Models;

/// <summary>
/// A versioned, JSON-serializable, server-persisted snapshot of a user's working configuration —
/// which supplemental providers are connected, which layers/asset types are in scope, and initial
/// map view. Reference-only: a Project never embeds feature data, it only references live sources
/// (providers, layers, asset types), which are resolved at open time.
///
/// Comes in two kinds sharing this one class (see <see cref="Kind"/>): a <see cref="ProjectKind.General"/>
/// Project (shared, governed, org-scoped) and a <see cref="ProjectKind.User"/> Project (a personal fork
/// of a General Project, created via copy-on-write). Two-tier resolution between the two is implemented
/// by <see cref="Services.ProjectResolver"/>, not by this class.
/// </summary>
public sealed class Project : IOrgOwnedResource
{
    /// <summary>Current on-disk shape of this class. Bump when adding/changing a persisted field
    /// and register the corresponding upgrade in <see cref="Services.ProjectSchemaMigrator"/>.</summary>
    public const int CurrentSchemaVersion = 1;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>See <see cref="IOrgOwnedResource"/>. Defaults to <see cref="Guid.Empty"/> —
    /// "no organization assigned".</summary>
    public Guid OrganizationId { get; set; } = Guid.Empty;

    /// <summary>Owner of this Project row. Set-once at creation.</summary>
    public Guid CreatedByUserId { get; set; } = Guid.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Schema version this instance was persisted/deserialized at. See <see cref="CurrentSchemaVersion"/>.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public ProjectKind Kind { get; set; }

    /// <summary>Set only when <see cref="Kind"/> is <see cref="ProjectKind.User"/> — the
    /// <see cref="ProjectKind.General"/> Project this one was forked from. One level only, no chains
    /// (a User Project's parent is never itself a User Project).</summary>
    public Guid? ParentProjectId { get; set; }

    /// <summary>
    /// Connected supplemental providers and their reconnect config. For a <see cref="ProjectKind.General"/>
    /// Project this is always populated. For a <see cref="ProjectKind.User"/> Project, <c>null</c> means
    /// "inherit the parent's current value"; non-null means "overridden locally" — whole-group override
    /// only, no per-entry merge with the parent's list.
    ///
    /// Deliberately has NO default initializer: giving this (or any of the other three scope properties
    /// below) a non-null default would make every freshly-forked User Project immediately override the
    /// parent with an empty scope instead of inheriting it, defeating the two-tier design.
    /// </summary>
    public List<ProjectProviderEntry>? Providers { get; set; }

    /// <summary>Asset-type visibility scope. See <see cref="Providers"/> for the inherit-vs-override contract
    /// shared by all four scope properties.</summary>
    public ProjectAssetTypeScope? AssetTypeScope { get; set; }

    /// <summary>Layer visibility/order overrides. See <see cref="Providers"/> for the inherit-vs-override
    /// contract shared by all four scope properties.</summary>
    public ProjectLayerScope? LayerScope { get; set; }

    /// <summary>Initial map view. See <see cref="Providers"/> for the inherit-vs-override contract shared
    /// by all four scope properties.</summary>
    public ProjectViewState? ViewState { get; set; }
}

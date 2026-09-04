using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Projects.Persistence.Entities;

namespace GeoAssets.Projects.Persistence;

/// <summary>
/// Converts between the domain <see cref="Project"/> and the EF entity <see cref="ProjectRow"/>.
///
/// The four scope properties are serialised to nullable JSON text columns, preserving <c>null</c>
/// exactly (never collapsing it to an empty JSON array/object) — see <see cref="ProjectRow"/>'s
/// doc comment for why that distinction matters.
/// </summary>
internal static class ProjectMapper
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        WriteIndented          = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Domain → EF ───────────────────────────────────────────────────────────

    public static ProjectRow ToRow(Project project) => new()
    {
        Id              = project.Id,
        Name            = project.Name,
        Description     = project.Description,
        OrganizationId  = project.OrganizationId,
        CreatedByUserId = project.CreatedByUserId,
        CreatedAt       = project.CreatedAt,
        UpdatedAt       = project.UpdatedAt,
        SchemaVersion   = project.SchemaVersion,
        Kind            = project.Kind,
        ParentProjectId = project.ParentProjectId,
        ProvidersJson      = Serialize(project.Providers),
        AssetTypeScopeJson = Serialize(project.AssetTypeScope),
        LayerScopeJson     = Serialize(project.LayerScope),
        ViewStateJson      = Serialize(project.ViewState),
    };

    // ── EF → Domain ───────────────────────────────────────────────────────────

    public static Project ToDomain(ProjectRow row) => new()
    {
        Id              = row.Id,
        Name            = row.Name,
        Description     = row.Description,
        OrganizationId  = row.OrganizationId,
        CreatedByUserId = row.CreatedByUserId,
        CreatedAt       = row.CreatedAt,
        UpdatedAt       = row.UpdatedAt,
        SchemaVersion   = row.SchemaVersion,
        Kind            = row.Kind,
        ParentProjectId = row.ParentProjectId,
        Providers      = Deserialize<List<ProjectProviderEntry>>(row.ProvidersJson),
        AssetTypeScope = Deserialize<ProjectAssetTypeScope>(row.AssetTypeScopeJson),
        LayerScope     = Deserialize<ProjectLayerScope>(row.LayerScopeJson),
        ViewState      = Deserialize<ProjectViewState>(row.ViewStateJson),
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? Serialize<T>(T? value) where T : class =>
        value is null ? null : JsonSerializer.Serialize(value, _json);

    private static T? Deserialize<T>(string? json) where T : class =>
        json is null ? null : JsonSerializer.Deserialize<T>(json, _json);
}

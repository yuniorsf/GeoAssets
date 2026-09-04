using GeoAssets.Core.Models;

namespace GeoAssets.Core.Services;

/// <summary>
/// Resolves a <see cref="ProjectKind.User"/> Project's four scopes against its <see cref="ProjectKind.General"/>
/// parent — pure logic, no I/O. A null scope on the User Project means "inherit the parent's current value";
/// a non-null scope means "overridden locally". The parent is expected to always have all four scopes populated.
/// </summary>
public static class ProjectResolver
{
    /// <summary>Builds the resolved <see cref="Project"/> used everywhere downstream — the raw User Project
    /// row (with its nulls) is a separate concern from what callers actually render/operate against.</summary>
    public static Project Resolve(Project userProject, Project parentProject) =>
        new()
        {
            Id = userProject.Id,
            Name = userProject.Name,
            Description = userProject.Description,
            OrganizationId = userProject.OrganizationId,
            CreatedByUserId = userProject.CreatedByUserId,
            CreatedAt = userProject.CreatedAt,
            UpdatedAt = userProject.UpdatedAt,
            SchemaVersion = userProject.SchemaVersion,
            Kind = userProject.Kind,
            ParentProjectId = userProject.ParentProjectId,
            Providers = userProject.Providers ?? parentProject.Providers,
            AssetTypeScope = userProject.AssetTypeScope ?? parentProject.AssetTypeScope,
            LayerScope = userProject.LayerScope ?? parentProject.LayerScope,
            ViewState = userProject.ViewState ?? parentProject.ViewState
        };
}

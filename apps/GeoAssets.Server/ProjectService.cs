using System.Security.Claims;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Server;

/// <summary>Default <see cref="IProjectService"/> — see that interface for the full contract.</summary>
public sealed class ProjectService(
    IAuthorizationService authz,
    IGeoAuthorizationService authorizationService,
    IProjectReader projectReader,
    IProjectWriter projectWriter,
    TimeProvider timeProvider) : IProjectService
{
    private const string ManageCodePrefix = "projects:manage-";

    public async Task<Project?> ResolveMutationTargetAsync(
        ClaimsPrincipal caller, Project project, string permissionCode, CancellationToken ct = default)
    {
        var context = await authorizationService.GetAuthorizationContextAsync(ct);

        // Ownership bypass (XD01-141 point 4): checked locally, not a change to
        // OrgResourceAuthorizationHandler, which stays untouched.
        if (project.Kind == ProjectKind.User && project.CreatedByUserId == context.User.Id)
            return project;

        var permitted = await authz.AuthorizeAsync(caller, project, new OrgResourceRequirement(permissionCode));
        if (permitted.Succeeded)
            return project;

        // Copy-on-write only ever applies to a projects:manage-* check against a General
        // Project — rename/delete have no "fork instead" fallback (there's nothing to
        // redirect a rename/delete of the shared row onto), so those fail closed here.
        if (project.Kind != ProjectKind.General ||
            !permissionCode.StartsWith(ManageCodePrefix, StringComparison.Ordinal))
            return null;

        var canRead = await authz.AuthorizeAsync(caller, project, new OrgResourceRequirement("projects:read"));
        if (!canRead.Succeeded)
            return null;

        return await FindOrCreateForkAsync(project, context.User.Id, ct);
    }

    private async Task<Project> FindOrCreateForkAsync(Project parent, Guid callerId, CancellationToken ct)
    {
        var forks = await projectReader.GetForksOfAsync(parent.Id, ct);
        var existing = forks.FirstOrDefault(f => f.CreatedByUserId == callerId);
        if (existing is not null)
            return existing;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var fork = new Project
        {
            Name            = parent.Name,
            Description     = parent.Description,
            OrganizationId  = parent.OrganizationId,
            CreatedByUserId = callerId,
            Kind            = ProjectKind.User,
            ParentProjectId = parent.Id,
            CreatedAt       = now,
            UpdatedAt       = now,
        };
        await projectWriter.AddAsync(fork, ct);
        return fork;
    }
}

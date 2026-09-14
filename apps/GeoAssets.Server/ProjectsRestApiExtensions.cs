using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Server;

/// <summary>
/// Maps Project REST API endpoints onto any <see cref="IEndpointRouteBuilder"/> (XD01-141),
/// backed by <see cref="IProjectReader"/>/<see cref="IProjectWriter"/> (see
/// <c>AddProjectPersistence</c>) and gated by <see cref="IProjectService"/>.
///
/// The four <c>projects:manage-*</c> scope endpoints (Providers/AssetTypeScope/LayerScope/
/// ViewState) route-require only <c>projects:read</c> — <em>not</em> the specific manage-*
/// code, unlike every other resource in this codebase (compare
/// <see cref="GeoAssetsRestApiExtensions"/>'s features endpoints, which route-require the
/// exact action). That's deliberate: <see cref="IProjectService.ResolveMutationTargetAsync"/>'s
/// copy-on-write fallback needs to run for a caller who holds <c>projects:read</c> but not the
/// specific manage-* permission — requiring the manage-* code at the route level would 403 that
/// caller before the endpoint body (and the fallback) ever executes. <c>projects:rename</c> and
/// <c>projects:delete</c> have no such fallback, so those two route-require their own exact code,
/// matching the rest of the codebase.
/// </summary>
public static class ProjectsRestApiExtensions
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapProjectsApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/projects")
    {
        // ── Reads ────────────────────────────────────────────────────────────────

        routes.MapGet(prefix, async (IProjectReader reader) =>
            Results.Json(await reader.GetAllAsync(), _opts))
            .RequireAuthorization("projects:read");

        routes.MapGet($"{prefix}/organization/{{organizationId}}", async (
            Guid organizationId, IProjectReader reader) =>
                Results.Json(await reader.GetByOrganizationAsync(organizationId), _opts))
            .RequireAuthorization("projects:read");

        routes.MapGet($"{prefix}/{{id}}", async (
            Guid id, IProjectReader reader, IAuthorizationService authz, HttpContext http) =>
        {
            var project = await reader.GetByIdAsync(id);
            if (project is null) return Results.NotFound();

            var result = await authz.AuthorizeAsync(http.User, project, new OrgResourceRequirement("projects:read"));
            if (!result.Succeeded)
                return Results.Json(new { reason = "Not authorized to read this project." },
                    statusCode: StatusCodes.Status403Forbidden);

            return Results.Json(project, _opts);
        })
            .RequireAuthorization("projects:read");

        // ── Scope mutations (copy-on-write eligible) ────────────────────────────

        routes.MapPut($"{prefix}/{{id}}/providers", async (
            Guid id, HttpRequest req, IProjectReader reader, IProjectWriter writer,
            IProjectService projectService, TimeProvider timeProvider, HttpContext http) =>
        {
            var providers = await JsonSerializer.DeserializeAsync<List<ProjectProviderEntry>>(req.Body, _opts);
            return await MutateScopeAsync(id, "projects:manage-providers", reader, writer, projectService,
                timeProvider, http, target => target.Providers = providers);
        })
            .RequireAuthorization("projects:read");

        routes.MapPut($"{prefix}/{{id}}/asset-types", async (
            Guid id, HttpRequest req, IProjectReader reader, IProjectWriter writer,
            IProjectService projectService, TimeProvider timeProvider, HttpContext http) =>
        {
            var scope = await JsonSerializer.DeserializeAsync<ProjectAssetTypeScope>(req.Body, _opts);
            return await MutateScopeAsync(id, "projects:manage-asset-types", reader, writer, projectService,
                timeProvider, http, target => target.AssetTypeScope = scope);
        })
            .RequireAuthorization("projects:read");

        routes.MapPut($"{prefix}/{{id}}/layers", async (
            Guid id, HttpRequest req, IProjectReader reader, IProjectWriter writer,
            IProjectService projectService, TimeProvider timeProvider, HttpContext http) =>
        {
            var scope = await JsonSerializer.DeserializeAsync<ProjectLayerScope>(req.Body, _opts);
            return await MutateScopeAsync(id, "projects:manage-layers", reader, writer, projectService,
                timeProvider, http, target => target.LayerScope = scope);
        })
            .RequireAuthorization("projects:read");

        routes.MapPut($"{prefix}/{{id}}/view", async (
            Guid id, HttpRequest req, IProjectReader reader, IProjectWriter writer,
            IProjectService projectService, TimeProvider timeProvider, HttpContext http) =>
        {
            var view = await JsonSerializer.DeserializeAsync<ProjectViewState>(req.Body, _opts);
            return await MutateScopeAsync(id, "projects:manage-view", reader, writer, projectService,
                timeProvider, http, target => target.ViewState = view);
        })
            .RequireAuthorization("projects:read");

        // ── Rename / Delete (no copy-on-write) ──────────────────────────────────

        routes.MapPut($"{prefix}/{{id}}/name", async (
            Guid id, HttpRequest req, IProjectReader reader, IProjectWriter writer,
            IProjectService projectService, TimeProvider timeProvider, HttpContext http) =>
        {
            var dto = await JsonSerializer.DeserializeAsync<ProjectRenameDto>(req.Body, _opts);
            if (dto is null) return Results.BadRequest("Invalid request.");
            return await MutateScopeAsync(id, "projects:rename", reader, writer, projectService,
                timeProvider, http, target =>
                {
                    target.Name        = dto.Name;
                    target.Description = dto.Description;
                });
        })
            .RequireAuthorization("projects:rename");

        routes.MapDelete($"{prefix}/{{id}}", async (
            Guid id, IProjectReader reader, IProjectWriter writer,
            IProjectService projectService, HttpContext http) =>
        {
            var project = await reader.GetByIdAsync(id);
            if (project is null) return Results.NotFound();

            var target = await projectService.ResolveMutationTargetAsync(http.User, project, "projects:delete");
            if (target is null)
                return Results.Json(new { reason = "Not authorized to delete this project." },
                    statusCode: StatusCodes.Status403Forbidden);

            await writer.DeleteAsync(target.Id);
            return Results.NoContent();
        })
            .RequireAuthorization("projects:delete");

        return routes;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<IResult> MutateScopeAsync(
        Guid id, string permissionCode, IProjectReader reader, IProjectWriter writer,
        IProjectService projectService, TimeProvider timeProvider, HttpContext http,
        Action<Project> applyMutation)
    {
        var project = await reader.GetByIdAsync(id);
        if (project is null) return Results.NotFound();

        var target = await projectService.ResolveMutationTargetAsync(http.User, project, permissionCode);
        if (target is null)
            return Results.Json(new { reason = $"Not authorized ({permissionCode}) on this project." },
                statusCode: StatusCodes.Status403Forbidden);

        applyMutation(target);
        target.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await writer.UpdateAsync(target);

        return Results.Json(target, _opts);
    }

    private sealed record ProjectRenameDto(string Name, string Description);
}

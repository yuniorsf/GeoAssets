using System.Text.Json;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Server;

/// <summary>
/// Maps identity/authorization REST endpoints onto any <see cref="IEndpointRouteBuilder"/>
/// (XD01-18) — lets <c>GeoAssets.Web</c> resolve "what can the current user do" against the real
/// server-side identity backend (<c>AddGeoIdentity()</c>, XD01-14) instead of client-only WASM state.
///
/// <c>/me</c> and <c>/policies</c> require only an authenticated caller, via the global
/// <c>AuthorizationOptions.FallbackPolicy</c> (XD01-12). The Users/Roles/Permissions admin
/// endpoints (XD01-54 Phase 1) additionally call <c>.RequireAuthorization("resource:action")</c> —
/// a raw <c>AppPermission.Code</c>, resolved by <see cref="GeoAuthorizationHandler"/> (XD01-15).
/// No user→role assignment endpoint exists here: production role membership is sourced from the
/// external provider's roles claim, not the local <c>UserRole</c> table (XD01-19) — see XD01-54's
/// Phase 2 for the provider-backed role-assignment seam. Permissions are code-seeded and read-only.
/// </summary>
public static class IdentityRestApiExtensions
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapIdentityApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/identity")
    {
        routes.MapGet($"{prefix}/me", async (IGeoAuthorizationService authService) =>
        {
            var ctx = await authService.GetAuthorizationContextAsync();
            var dto = new AuthorizationContextDto(
                Id:             ctx.User.Id,
                Email:          ctx.User.Email,
                DisplayName:    ctx.User.DisplayName,
                OrganizationId: ctx.User.OrganizationId,
                Roles:          ctx.Roles,
                Claims:         ctx.Claims.Select(c => new ClaimDto(c.Type, c.Value)).ToList(),
                Permissions:    ctx.Permissions);

            return Results.Json(dto, _opts);
        });

        routes.MapGet($"{prefix}/policies", async (IPolicyRepository policyRepo) =>
        {
            var policies = await policyRepo.GetAllAsync();
            var dtos = policies.Select(p => new PolicyDto(
                Id:           p.Id,
                Name:         p.Name,
                Description:  p.Description,
                Operator:     p.Operator,
                Requirements: p.Requirements
                    .Select(r => new PolicyRequirementDto(r.Type, r.Value, r.ClaimValue))
                    .ToList()));

            return Results.Json(dtos, _opts);
        });

        // ── Users ────────────────────────────────────────────────────────────────

        routes.MapGet($"{prefix}/users", async (IUserRepository userRepo) =>
        {
            var users = await userRepo.GetAllAsync();
            return Results.Json(users.Select(ToSummaryDto), _opts);
        }).RequireAuthorization("users:read");

        routes.MapGet($"{prefix}/users/{{id}}", async (Guid id, IUserRepository userRepo) =>
        {
            var user = await userRepo.GetByIdAsync(id);
            if (user is null) return Results.NotFound();

            var roles = await userRepo.GetRolesAsync(id);
            var dto = new UserDetailDto(
                Id:             user.Id,
                Email:          user.Email,
                DisplayName:    user.DisplayName,
                IsActive:       user.IsActive,
                OrganizationId: user.OrganizationId,
                CreatedAt:      user.CreatedAt,
                LastLoginAt:    user.LastLoginAt,
                RoleIds:        roles.Select(r => r.Id).ToList());

            return Results.Json(dto, _opts);
        }).RequireAuthorization("users:read");

        routes.MapPut($"{prefix}/users/{{id}}", async (Guid id, UserUpdateDto dto, IUserRepository userRepo) =>
        {
            var user = await userRepo.GetByIdAsync(id);
            if (user is null) return Results.NotFound();

            user.DisplayName    = dto.DisplayName;
            user.IsActive       = dto.IsActive;
            user.OrganizationId = dto.OrganizationId;

            await userRepo.UpdateAsync(user);
            await userRepo.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("users:edit");

        // ── Roles ────────────────────────────────────────────────────────────────

        routes.MapGet($"{prefix}/roles", async (IRoleRepository roleRepo) =>
        {
            var roles = await roleRepo.GetAllAsync();
            return Results.Json(roles.Select(ToSummaryDto), _opts);
        }).RequireAuthorization("roles:read");

        routes.MapGet($"{prefix}/roles/{{id}}", async (Guid id, IRoleRepository roleRepo) =>
        {
            var role = await roleRepo.GetByIdAsync(id);
            if (role is null) return Results.NotFound();

            var permissions = await roleRepo.GetPermissionsAsync(id);
            var dto = new RoleDetailDto(
                Id:            role.Id,
                Name:          role.Name,
                Description:   role.Description,
                IsBuiltIn:     role.IsBuiltIn,
                PermissionIds: permissions.Select(p => p.Id).ToList());

            return Results.Json(dto, _opts);
        }).RequireAuthorization("roles:read");

        routes.MapPost($"{prefix}/roles", async (RoleWriteDto dto, IRoleRepository roleRepo) =>
        {
            var role = new AppRole { Id = Guid.NewGuid(), Name = dto.Name, Description = dto.Description, IsBuiltIn = false };
            await roleRepo.AddAsync(role);
            await roleRepo.SaveChangesAsync();
            return Results.Created($"{prefix}/roles/{role.Id}", null);
        }).RequireAuthorization("roles:edit");

        routes.MapPut($"{prefix}/roles/{{id}}", async (Guid id, RoleWriteDto dto, IRoleRepository roleRepo) =>
        {
            var role = await roleRepo.GetByIdAsync(id);
            if (role is null) return Results.NotFound();

            role.Name        = dto.Name;
            role.Description = dto.Description;

            await roleRepo.UpdateAsync(role);
            await roleRepo.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("roles:edit");

        routes.MapDelete($"{prefix}/roles/{{id}}", async (Guid id, IRoleRepository roleRepo) =>
        {
            var role = await roleRepo.GetByIdAsync(id);
            if (role is null) return Results.NotFound();
            if (role.IsBuiltIn)
                return Results.Json(new { reason = "Built-in roles cannot be deleted." },
                    statusCode: StatusCodes.Status409Conflict);

            await roleRepo.DeleteAsync(id);
            await roleRepo.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("roles:delete");

        routes.MapPost($"{prefix}/roles/{{id}}/permissions/{{permId}}", async (Guid id, Guid permId, IRoleRepository roleRepo) =>
        {
            await roleRepo.GrantPermissionAsync(id, permId);
            await roleRepo.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("roles:edit");

        routes.MapDelete($"{prefix}/roles/{{id}}/permissions/{{permId}}", async (Guid id, Guid permId, IRoleRepository roleRepo) =>
        {
            await roleRepo.RevokePermissionAsync(id, permId);
            await roleRepo.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("roles:edit");

        // ── Permissions ──────────────────────────────────────────────────────────

        routes.MapGet($"{prefix}/permissions", async (IPermissionRepository permissionRepo) =>
        {
            var permissions = await permissionRepo.GetAllAsync();
            return Results.Json(permissions.Select(ToDto), _opts);
        }).RequireAuthorization("permissions:read");

        return routes;
    }

    private static UserSummaryDto ToSummaryDto(AppUser u) =>
        new(u.Id, u.Email, u.DisplayName, u.IsActive, u.OrganizationId);

    private static RoleSummaryDto ToSummaryDto(AppRole r) =>
        new(r.Id, r.Name, r.Description, r.IsBuiltIn);

    private static PermissionDto ToDto(AppPermission p) =>
        new(p.Id, p.Code, p.Resource, p.Action, p.Description);
}

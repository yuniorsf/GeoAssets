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
/// Permissions are code-seeded and read-only.
///
/// Production role <em>membership</em> is still sourced from the external provider's roles claim,
/// not the local <c>UserRole</c> table (XD01-19) — the <c>rolesync/*</c> endpoints (XD01-59 Phase
/// 2, XD01-63) don't change that; they let an admin push a locally-defined role and its
/// assignments <em>to</em> the external provider (via <see cref="IRoleAssignmentProvider"/>,
/// server-side only) so that claim ends up populated in the first place.
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

        // ── Role Sync (XD01-59 Phase 2, XD01-63) ────────────────────────────────

        // Authenticated-only, like /me and /policies — whether role sync is on isn't
        // sensitive, and both admin pages that ask already gate their own visibility on
        // users:read/roles:read.
        routes.MapGet($"{prefix}/rolesync/status", (IRoleAssignmentProvider roleSync) =>
            Results.Json(new RoleSyncStatusDto(roleSync is not NullRoleAssignmentProvider), _opts));

        routes.MapPost($"{prefix}/rolesync/roles/{{id}}", async (Guid id, IRoleRepository roleRepo, IRoleAssignmentProvider roleSync) =>
        {
            var role = await roleRepo.GetByIdAsync(id);
            if (role is null) return Results.NotFound();

            await roleSync.RegisterRoleAsync(role);
            return Results.NoContent();
        }).RequireAuthorization("roles:edit");

        routes.MapPost($"{prefix}/rolesync/users/{{externalObjectId}}/roles/{{roleName}}",
            async (string externalObjectId, string roleName, IRoleAssignmentProvider roleSync) =>
            {
                await roleSync.AssignRoleAsync(externalObjectId, roleName);
                return Results.NoContent();
            }).RequireAuthorization("users:edit");

        routes.MapDelete($"{prefix}/rolesync/users/{{externalObjectId}}/roles/{{roleName}}",
            async (string externalObjectId, string roleName, IRoleAssignmentProvider roleSync) =>
            {
                await roleSync.RevokeRoleAsync(externalObjectId, roleName);
                return Results.NoContent();
            }).RequireAuthorization("users:edit");

        routes.MapGet($"{prefix}/rolesync/users/{{externalObjectId}}/roles",
            async (string externalObjectId, IRoleAssignmentProvider roleSync) =>
            {
                var names = await roleSync.GetAssignedRoleNamesAsync(externalObjectId);
                return Results.Json(names, _opts);
            }).RequireAuthorization("users:read");

        // ── Invitations (XD01-59 Phase 3, XD01-69) ──────────────────────────────

        // Authenticated-only, like rolesync/status — whether invitations are on isn't sensitive,
        // and the admin page that asks already gates its own visibility on users:read.
        routes.MapGet($"{prefix}/invitations/status",
            (IUserInvitationProvider invitationProvider, IInvitationEmailSender invitationEmailSender) =>
                Results.Json(new InvitationStatusDto(
                    invitationProvider is not NullUserInvitationProvider &&
                    invitationEmailSender is not NullInvitationEmailSender), _opts));

        // Authenticated-only, self-service — "is there a Pending invitation for ME", derived
        // from the caller's own AuthorizationContext, never from a client-supplied id (XD01-92).
        // Distinct from the admin list below (users:read): a just-invited caller has no
        // permissions yet by design (XD01-19, no default role granted), so the redirect gate
        // that drives them to /complete-profile (InvitationRedirectGate, XD01-89) can only ever
        // work if checking "my own" invitation doesn't require an admin permission they'll never
        // have. Mirrors the userclaims endpoints' ownership philosophy further below.
        routes.MapGet($"{prefix}/invitations/me", async (
            IPendingInvitationRepository invitationRepo, IGeoAuthorizationService authService) =>
        {
            var ctx = await authService.GetAuthorizationContextAsync();
            var invitation = await invitationRepo.GetByExternalObjectIdAsync(ctx.User.ExternalObjectId);
            return invitation is null ? Results.NotFound() : Results.Json(ToDto(invitation), _opts);
        });

        routes.MapGet($"{prefix}/invitations", async (IPendingInvitationRepository invitationRepo) =>
        {
            var invitations = await invitationRepo.GetAllPendingAsync();
            return Results.Json(invitations.Select(ToDto), _opts);
        }).RequireAuthorization("users:read");

        routes.MapPost($"{prefix}/invitations", async (
            InvitationCreateDto dto,
            IUserInvitationProvider invitationProvider,
            IInvitationEmailSender invitationEmailSender,
            IPendingInvitationRepository invitationRepo,
            IGeoAuthorizationService authService) =>
        {
            var ctx = await authService.GetAuthorizationContextAsync();
            var externalObjectId = await invitationProvider.CreateInvitedAccountAsync(dto.Email, dto.DisplayName);

            var invitation = new PendingInvitation
            {
                Email            = dto.Email,
                ExternalObjectId = externalObjectId,
                InvitedByUserId  = ctx.User.Id,
                InvitedAt        = DateTime.UtcNow,
                Status           = InvitationStatus.Pending,
            };
            await invitationRepo.AddAsync(invitation);
            await invitationRepo.SaveChangesAsync();

            // The account and PendingInvitation row above must not be rolled back if the email
            // fails to send — an admin can still see the invitation in the pending list and
            // resend/handle it manually. Surface the partial failure with a distinct status
            // (202, not 201) rather than either losing that the email never went out or failing
            // the whole request and orphaning an already-created provider account.
            try
            {
                await invitationEmailSender.SendInvitationAsync(dto.Email, dto.DisplayName);
            }
            catch
            {
                return Results.Json(ToDto(invitation), _opts, statusCode: StatusCodes.Status202Accepted);
            }

            return Results.Json(ToDto(invitation), _opts, statusCode: StatusCodes.Status201Created);
        }).RequireAuthorization("users:edit");

        routes.MapDelete($"{prefix}/invitations/{{id}}", async (
            Guid id, IPendingInvitationRepository invitationRepo, IUserInvitationProvider invitationProvider) =>
        {
            var invitation = await invitationRepo.GetByIdAsync(id);
            if (invitation is null) return Results.NotFound();

            await invitationProvider.RevokeInvitedAccountAsync(invitation.ExternalObjectId);

            invitation.Status = InvitationStatus.Revoked;
            await invitationRepo.UpdateAsync(invitation);
            await invitationRepo.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("users:edit");

        // Authenticated-only — any signed-in user may redeem their own pending invitation.
        // Ownership is proven by matching the caller's own ExternalObjectId (resolved server-side
        // from their auth context) against the invitation's, never by trusting a client-supplied
        // user id. A non-owner (or unknown id) gets 404 either way, so probing ids can't be used
        // to learn whether a given invitation exists.
        routes.MapPost($"{prefix}/invitations/{{id}}/redeem", async (
            Guid id, IPendingInvitationRepository invitationRepo, IGeoAuthorizationService authService) =>
        {
            var invitation = await invitationRepo.GetByIdAsync(id);
            if (invitation is null) return Results.NotFound();

            var ctx = await authService.GetAuthorizationContextAsync();
            if (invitation.ExternalObjectId != ctx.User.ExternalObjectId)
                return Results.NotFound();

            invitation.Status     = InvitationStatus.Redeemed;
            invitation.RedeemedAt = DateTime.UtcNow;
            await invitationRepo.UpdateAsync(invitation);
            await invitationRepo.SaveChangesAsync();
            return Results.NoContent();
        });

        // ── User Claims (XD01-59 Phase 3, XD01-87) ──────────────────────────────

        // All authenticated-only, self-service — every check is "is this MY claim", derived
        // from the caller's own AuthorizationContext, never from a client-supplied user id. A
        // claim that exists but isn't the caller's own is indistinguishable from one that
        // doesn't exist at all (404, never 403), mirroring invitations/{id}/redeem's ownership
        // philosophy (XD01-69) so ids can't be probed to learn what belongs to someone else.

        routes.MapGet($"{prefix}/userclaims", async (IUserClaimRepository claimRepo, IGeoAuthorizationService authService) =>
        {
            var ctx = await authService.GetAuthorizationContextAsync();
            var claims = await claimRepo.GetByUserIdAsync(ctx.User.Id);
            return Results.Json(claims.Select(ToDto), _opts);
        });

        routes.MapGet($"{prefix}/userclaims/{{claimType}}", async (
            string claimType, IUserClaimRepository claimRepo, IGeoAuthorizationService authService) =>
        {
            var ctx = await authService.GetAuthorizationContextAsync();
            var claim = await claimRepo.GetAsync(ctx.User.Id, claimType);
            return claim is null ? Results.NotFound() : Results.Json(ToDto(claim), _opts);
        });

        routes.MapPost($"{prefix}/userclaims", async (
            UserClaimWriteDto dto, IUserClaimRepository claimRepo, IGeoAuthorizationService authService) =>
        {
            var ctx = await authService.GetAuthorizationContextAsync();
            var claim = new UserClaim { UserId = ctx.User.Id, Type = dto.Type, Value = dto.Value };

            await claimRepo.AddAsync(claim);
            await claimRepo.SaveChangesAsync();
            return Results.Json(ToDto(claim), _opts, statusCode: StatusCodes.Status201Created);
        });

        routes.MapPut($"{prefix}/userclaims/{{claimId}}", async (
            Guid claimId, UserClaimUpdateDto dto, IUserClaimRepository claimRepo, IGeoAuthorizationService authService) =>
        {
            var claim = await FindOwnClaimAsync(claimRepo, authService, claimId);
            if (claim is null) return Results.NotFound();

            claim.Value = dto.Value;
            await claimRepo.UpdateAsync(claim);
            await claimRepo.SaveChangesAsync();
            return Results.NoContent();
        });

        routes.MapDelete($"{prefix}/userclaims/{{claimId}}", async (
            Guid claimId, IUserClaimRepository claimRepo, IGeoAuthorizationService authService) =>
        {
            var claim = await FindOwnClaimAsync(claimRepo, authService, claimId);
            if (claim is null) return Results.NotFound();

            await claimRepo.RemoveAsync(claimId);
            await claimRepo.SaveChangesAsync();
            return Results.NoContent();
        });

        routes.MapDelete($"{prefix}/userclaims", async (IUserClaimRepository claimRepo, IGeoAuthorizationService authService) =>
        {
            var ctx = await authService.GetAuthorizationContextAsync();
            await claimRepo.RemoveAllAsync(ctx.User.Id);
            await claimRepo.SaveChangesAsync();
            return Results.NoContent();
        });

        return routes;
    }

    // GetByUserIdAsync, not a direct by-id lookup — IUserClaimRepository has no "get claim by its
    // own id" method, and filtering the caller's own list this way makes an unowned or unknown
    // claimId equally (and correctly) come back empty, without needing a new repository method.
    private static async Task<UserClaim?> FindOwnClaimAsync(
        IUserClaimRepository claimRepo, IGeoAuthorizationService authService, Guid claimId)
    {
        var ctx = await authService.GetAuthorizationContextAsync();
        var claims = await claimRepo.GetByUserIdAsync(ctx.User.Id);
        return claims.FirstOrDefault(c => c.Id == claimId);
    }

    private static UserSummaryDto ToSummaryDto(AppUser u) =>
        new(u.Id, u.Email, u.DisplayName, u.IsActive, u.OrganizationId);

    private static RoleSummaryDto ToSummaryDto(AppRole r) =>
        new(r.Id, r.Name, r.Description, r.IsBuiltIn);

    private static PermissionDto ToDto(AppPermission p) =>
        new(p.Id, p.Code, p.Resource, p.Action, p.Description);

    private static PendingInvitationDto ToDto(PendingInvitation i) =>
        new(i.Id, i.Email, i.ExternalObjectId, i.InvitedByUserId, i.InvitedAt, i.RedeemedAt, i.Status);

    private static UserClaimDto ToDto(UserClaim c) => new(c.Id, c.Type, c.Value);
}

using System.Text.Json;
using GeoAssets.Core.Interfaces;
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
/// <c>AuthorizationOptions.FallbackPolicy</c> (XD01-12). The Users/Roles/Permissions/
/// Organizations/Groups admin endpoints (XD01-54 Phase 1, XD01-128) additionally call
/// <c>.RequireAuthorization("resource:action")</c> — a raw <c>AppPermission.Code</c>, resolved by
/// <see cref="GeoAuthorizationHandler"/> (XD01-15). Permissions are code-seeded and read-only;
/// Organizations/Groups are real admin-managed data with full CRUD, same shape as Users/Roles.
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
        MapMeAndPolicies(routes, prefix);
        MapUsersApi(routes, prefix);
        MapRolesApi(routes, prefix);
        MapPermissionsApi(routes, prefix);
        MapOrganizationsApi(routes, prefix);
        MapGroupsApi(routes, prefix);
        MapRoleSyncApi(routes, prefix);
        MapInvitationsApi(routes, prefix);
        MapUserClaimsApi(routes, prefix);

        return routes;
    }

    // ── Me / Policies ────────────────────────────────────────────────────────

    private static void MapMeAndPolicies(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapGet($"{prefix}/me", async (IGeoAuthorizationService authService) =>
        {
            var ctx = await authService.GetAuthorizationContextAsync().ConfigureAwait(false);
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
            var policies = await policyRepo.GetAllAsync().ConfigureAwait(false);
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
    }

    // ── Users ────────────────────────────────────────────────────────────────

    private static void MapUsersApi(IEndpointRouteBuilder routes, string prefix)
    {

        routes.MapGet($"{prefix}/users", async (IUserRepository userRepo) =>
        {
            var users = await userRepo.GetAllAsync().ConfigureAwait(false);
            return Results.Json(users.Select(ToSummaryDto), _opts);
        }).RequireAuthorization("users:read");

        routes.MapGet($"{prefix}/users/{{id}}", async (Guid id, IUserRepository userRepo) =>
        {
            var user = await userRepo.GetByIdAsync(id).ConfigureAwait(false);
            if (user is null) return Results.NotFound();

            var roles = await userRepo.GetRolesAsync(id).ConfigureAwait(false);
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

        routes.MapGet($"{prefix}/users/by-external-id/{{oid}}", async (string oid, IUserRepository userRepo) =>
        {
            var user = await userRepo.GetByExternalObjectIdAsync(oid).ConfigureAwait(false);
            if (user is null) return Results.NotFound();

            var roles = await userRepo.GetRolesAsync(user.Id).ConfigureAwait(false);
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
            var user = await userRepo.GetByIdAsync(id).ConfigureAwait(false);
            if (user is null) return Results.NotFound();

            user.DisplayName    = dto.DisplayName;
            user.IsActive       = dto.IsActive;
            user.OrganizationId = dto.OrganizationId;

            await userRepo.UpdateAsync(user).ConfigureAwait(false);
            await userRepo.SaveChangesAsync().ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization("users:edit");
    }

    // ── Roles ────────────────────────────────────────────────────────────────

    private static void MapRolesApi(IEndpointRouteBuilder routes, string prefix)
    {
        MapRoleCrud(routes, prefix);
        MapRolePermissions(routes, prefix);
    }

    private static void MapRoleCrud(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapGet($"{prefix}/roles", async (IRoleRepository roleRepo) =>
        {
            var roles = await roleRepo.GetAllAsync().ConfigureAwait(false);
            return Results.Json(roles.Select(ToSummaryDto), _opts);
        }).RequireAuthorization("roles:read");

        routes.MapGet($"{prefix}/roles/{{id}}", async (Guid id, IRoleRepository roleRepo) =>
        {
            var role = await roleRepo.GetByIdAsync(id).ConfigureAwait(false);
            if (role is null) return Results.NotFound();

            var permissions = await roleRepo.GetPermissionsAsync(id).ConfigureAwait(false);
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
            await roleRepo.AddAsync(role).ConfigureAwait(false);
            await roleRepo.SaveChangesAsync().ConfigureAwait(false);
            return Results.Created(new Uri($"{prefix}/roles/{role.Id}", UriKind.Relative), null);
        }).RequireAuthorization("roles:edit");

        routes.MapPut($"{prefix}/roles/{{id}}", async (Guid id, RoleWriteDto dto, IRoleRepository roleRepo) =>
        {
            var role = await roleRepo.GetByIdAsync(id).ConfigureAwait(false);
            if (role is null) return Results.NotFound();

            role.Name        = dto.Name;
            role.Description = dto.Description;

            await roleRepo.UpdateAsync(role).ConfigureAwait(false);
            await roleRepo.SaveChangesAsync().ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization("roles:edit");

        routes.MapDelete($"{prefix}/roles/{{id}}", async (Guid id, IRoleRepository roleRepo) =>
        {
            var role = await roleRepo.GetByIdAsync(id).ConfigureAwait(false);
            if (role is null) return Results.NotFound();
            if (role.IsBuiltIn)
                return Results.Json(new { reason = "Built-in roles cannot be deleted." },
                    statusCode: StatusCodes.Status409Conflict);

            await roleRepo.DeleteAsync(id).ConfigureAwait(false);
            await roleRepo.SaveChangesAsync().ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization("roles:delete");
    }

    private static void MapRolePermissions(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapPost($"{prefix}/roles/{{id}}/permissions/{{permId}}", async (Guid id, Guid permId, IRoleRepository roleRepo) =>
        {
            await roleRepo.GrantPermissionAsync(id, permId).ConfigureAwait(false);
            await roleRepo.SaveChangesAsync().ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization("roles:edit");

        routes.MapDelete($"{prefix}/roles/{{id}}/permissions/{{permId}}", async (Guid id, Guid permId, IRoleRepository roleRepo) =>
        {
            await roleRepo.RevokePermissionAsync(id, permId).ConfigureAwait(false);
            await roleRepo.SaveChangesAsync().ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization("roles:edit");
    }

    // ── Permissions ──────────────────────────────────────────────────────────

    private static void MapPermissionsApi(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapGet($"{prefix}/permissions", async (IPermissionRepository permissionRepo) =>
        {
            var permissions = await permissionRepo.GetAllAsync().ConfigureAwait(false);
            return Results.Json(permissions.Select(ToDto), _opts);
        }).RequireAuthorization("permissions:read");
    }

    // ── Organizations (XD01-128) ────────────────────────────────────────────

    private static void MapOrganizationsApi(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapGet($"{prefix}/organizations", async (IOrganizationRepository orgRepo) =>
        {
            var orgs = await orgRepo.GetAllAsync().ConfigureAwait(false);
            return Results.Json(orgs.Select(ToDto), _opts);
        }).RequireAuthorization("organizations:read");

        routes.MapGet($"{prefix}/organizations/{{id}}", async (Guid id, IOrganizationRepository orgRepo) =>
        {
            var org = await orgRepo.GetByIdAsync(id).ConfigureAwait(false);
            return org is null ? Results.NotFound() : Results.Json(ToDto(org), _opts);
        }).RequireAuthorization("organizations:read");

        routes.MapGet($"{prefix}/organizations/{{id}}/users", async (Guid id, IOrganizationRepository orgRepo) =>
        {
            var users = await orgRepo.GetUsersAsync(id).ConfigureAwait(false);
            return Results.Json(users.Select(ToSummaryDto), _opts);
        }).RequireAuthorization("organizations:read");

        routes.MapPost($"{prefix}/organizations", async (
            OrganizationWriteDto dto, IOrganizationRepository orgRepo, IProjectRepository projects, TimeProvider timeProvider) =>
        {
            var org = new Organization
            {
                Id          = Guid.NewGuid(),
                Name        = dto.Name,
                Slug        = dto.Slug,
                Description = dto.Description,
                IsActive    = dto.IsActive,
                CreatedAt   = timeProvider.GetUtcNow().UtcDateTime,
            };
            await orgRepo.AddAsync(org).ConfigureAwait(false);
            await orgRepo.SaveChangesAsync().ConfigureAwait(false);

            // XD01-144: a brand-new org gets a usable default Project immediately, not just on
            // the next server restart (when SeedDefaultProjectsAsync would otherwise catch it).
            await DefaultProjectSeeder.EnsureDefaultProjectAsync(projects, org.Id, timeProvider).ConfigureAwait(false);

            return Results.Created(new Uri($"{prefix}/organizations/{org.Id}", UriKind.Relative), null);
        }).RequireAuthorization("organizations:edit");

        routes.MapPut($"{prefix}/organizations/{{id}}", async (Guid id, OrganizationWriteDto dto, IOrganizationRepository orgRepo) =>
        {
            var org = await orgRepo.GetByIdAsync(id).ConfigureAwait(false);
            if (org is null) return Results.NotFound();

            org.Name        = dto.Name;
            org.Slug        = dto.Slug;
            org.Description = dto.Description;
            org.IsActive    = dto.IsActive;

            await orgRepo.UpdateAsync(org).ConfigureAwait(false);
            await orgRepo.SaveChangesAsync().ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization("organizations:edit");
    }

    // ── Groups (XD01-128) ────────────────────────────────────────────────────

    private static void MapGroupsApi(IEndpointRouteBuilder routes, string prefix)
    {
        MapGroupCrud(routes, prefix);
        MapGroupMembers(routes, prefix);
    }

    private static void MapGroupCrud(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapGet($"{prefix}/groups", async (IGroupRepository groupRepo) =>
        {
            var groups = await groupRepo.GetAllAsync().ConfigureAwait(false);
            return Results.Json(groups.Select(ToDto), _opts);
        }).RequireAuthorization("groups:read");

        routes.MapGet($"{prefix}/groups/{{id}}", async (Guid id, IGroupRepository groupRepo) =>
        {
            var group = await groupRepo.GetByIdAsync(id).ConfigureAwait(false);
            return group is null ? Results.NotFound() : Results.Json(ToDto(group), _opts);
        }).RequireAuthorization("groups:read");

        routes.MapPost($"{prefix}/groups", async (
            GroupWriteDto dto, IGroupRepository groupRepo, TimeProvider timeProvider) =>
        {
            var group = new AppGroup
            {
                Id             = Guid.NewGuid(),
                Name           = dto.Name,
                Description    = dto.Description,
                OrganizationId = dto.OrganizationId,
                IsActive       = dto.IsActive,
                CreatedAt      = timeProvider.GetUtcNow().UtcDateTime,
            };
            await groupRepo.AddAsync(group).ConfigureAwait(false);
            await groupRepo.SaveChangesAsync().ConfigureAwait(false);
            return Results.Created(new Uri($"{prefix}/groups/{group.Id}", UriKind.Relative), null);
        }).RequireAuthorization("groups:edit");

        routes.MapPut($"{prefix}/groups/{{id}}", async (Guid id, GroupWriteDto dto, IGroupRepository groupRepo) =>
        {
            var group = await groupRepo.GetByIdAsync(id).ConfigureAwait(false);
            if (group is null) return Results.NotFound();

            group.Name           = dto.Name;
            group.Description    = dto.Description;
            group.OrganizationId = dto.OrganizationId;
            group.IsActive       = dto.IsActive;

            await groupRepo.UpdateAsync(group).ConfigureAwait(false);
            await groupRepo.SaveChangesAsync().ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization("groups:edit");
    }

    private static void MapGroupMembers(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapGet($"{prefix}/groups/{{id}}/members", async (Guid id, IGroupRepository groupRepo) =>
        {
            var members = await groupRepo.GetMembersAsync(id).ConfigureAwait(false);
            return Results.Json(members.Select(ToSummaryDto), _opts);
        }).RequireAuthorization("groups:read");

        routes.MapPost($"{prefix}/groups/{{id}}/members/{{userId}}", async (
            Guid id, Guid userId, IGroupRepository groupRepo, IGeoAuthorizationService authService) =>
        {
            var ctx = await authService.GetAuthorizationContextAsync().ConfigureAwait(false);
            await groupRepo.AddMemberAsync(id, userId, addedBy: ctx.User.Id.ToString()).ConfigureAwait(false);
            await groupRepo.SaveChangesAsync().ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization("groups:edit");

        routes.MapDelete($"{prefix}/groups/{{id}}/members/{{userId}}", async (Guid id, Guid userId, IGroupRepository groupRepo) =>
        {
            await groupRepo.RemoveMemberAsync(id, userId).ConfigureAwait(false);
            await groupRepo.SaveChangesAsync().ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization("groups:edit");
    }

    // ── Role Sync (XD01-59 Phase 2, XD01-63) ────────────────────────────────

    private static void MapRoleSyncApi(IEndpointRouteBuilder routes, string prefix)
    {
        // Authenticated-only, like /me and /policies — whether role sync is on isn't
        // sensitive, and both admin pages that ask already gate their own visibility on
        // users:read/roles:read.
        routes.MapGet($"{prefix}/rolesync/status", (IRoleAssignmentProvider roleSync) =>
            Results.Json(new RoleSyncStatusDto(roleSync is not NullRoleAssignmentProvider), _opts));

        routes.MapPost($"{prefix}/rolesync/roles/{{id}}", async (Guid id, IRoleRepository roleRepo, IRoleAssignmentProvider roleSync) =>
        {
            var role = await roleRepo.GetByIdAsync(id).ConfigureAwait(false);
            if (role is null) return Results.NotFound();

            await roleSync.RegisterRoleAsync(role).ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization("roles:edit");

        routes.MapPost($"{prefix}/rolesync/users/{{externalObjectId}}/roles/{{roleName}}",
            async (string externalObjectId, string roleName, IRoleAssignmentProvider roleSync) =>
            {
                await roleSync.AssignRoleAsync(externalObjectId, roleName).ConfigureAwait(false);
                return Results.NoContent();
            }).RequireAuthorization("users:edit");

        routes.MapDelete($"{prefix}/rolesync/users/{{externalObjectId}}/roles/{{roleName}}",
            async (string externalObjectId, string roleName, IRoleAssignmentProvider roleSync) =>
            {
                await roleSync.RevokeRoleAsync(externalObjectId, roleName).ConfigureAwait(false);
                return Results.NoContent();
            }).RequireAuthorization("users:edit");

        routes.MapGet($"{prefix}/rolesync/users/{{externalObjectId}}/roles",
            async (string externalObjectId, IRoleAssignmentProvider roleSync) =>
            {
                var names = await roleSync.GetAssignedRoleNamesAsync(externalObjectId).ConfigureAwait(false);
                return Results.Json(names, _opts);
            }).RequireAuthorization("users:read");
    }

    // ── Invitations (XD01-59 Phase 3, XD01-69) ──────────────────────────────

    private static void MapInvitationsApi(IEndpointRouteBuilder routes, string prefix)
    {
        MapInvitationReads(routes, prefix);
        MapInvitationCreate(routes, prefix);
        MapInvitationRevokeAndRedeem(routes, prefix);
    }

    private static void MapInvitationReads(IEndpointRouteBuilder routes, string prefix)
    {
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
        //
        // Always 200, never 404: having no pending invitation is the expected outcome for almost
        // every authenticated caller (anyone not mid-onboarding), not an error — a 404 shows up
        // as a browser-logged network error regardless of how gracefully the client handles it.
        routes.MapGet($"{prefix}/invitations/me", async (
            IPendingInvitationRepository invitationRepo, IGeoAuthorizationService authService) =>
        {
            var ctx = await authService.GetAuthorizationContextAsync().ConfigureAwait(false);
            var invitation = await invitationRepo.GetByExternalObjectIdAsync(ctx.User.ExternalObjectId).ConfigureAwait(false);
            return Results.Json(invitation is null ? null : ToDto(invitation), _opts);
        });

        routes.MapGet($"{prefix}/invitations", async (IPendingInvitationRepository invitationRepo) =>
        {
            var invitations = await invitationRepo.GetAllPendingAsync().ConfigureAwait(false);
            return Results.Json(invitations.Select(ToDto), _opts);
        }).RequireAuthorization("users:read");
    }

    private static void MapInvitationCreate(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapPost($"{prefix}/invitations", async (
            InvitationCreateDto dto,
            IUserInvitationProvider invitationProvider,
            IInvitationEmailSender invitationEmailSender,
            IPendingInvitationRepository invitationRepo,
            IGeoAuthorizationService authService,
            TimeProvider timeProvider) =>
        {
            var ctx = await authService.GetAuthorizationContextAsync().ConfigureAwait(false);
            var externalObjectId = await invitationProvider.CreateInvitedAccountAsync(dto.Email, dto.DisplayName).ConfigureAwait(false);

            var invitation = new PendingInvitation
            {
                Email            = dto.Email,
                ExternalObjectId = externalObjectId,
                InvitedByUserId  = ctx.User.Id,
                InvitedAt        = timeProvider.GetUtcNow().UtcDateTime,
                Status           = InvitationStatus.Pending,
            };
            await invitationRepo.AddAsync(invitation).ConfigureAwait(false);
            await invitationRepo.SaveChangesAsync().ConfigureAwait(false);

            // The account and PendingInvitation row above must not be rolled back if the email
            // fails to send — an admin can still see the invitation in the pending list and
            // resend/handle it manually. Surface the partial failure with a distinct status
            // (202, not 201) rather than either losing that the email never went out or failing
            // the whole request and orphaning an already-created provider account.
            try
            {
                await invitationEmailSender.SendInvitationAsync(dto.Email, dto.DisplayName).ConfigureAwait(false);
            }
            catch
            {
                return Results.Json(ToDto(invitation), _opts, statusCode: StatusCodes.Status202Accepted);
            }

            return Results.Json(ToDto(invitation), _opts, statusCode: StatusCodes.Status201Created);
        }).RequireAuthorization("users:edit");
    }

    private static void MapInvitationRevokeAndRedeem(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapDelete($"{prefix}/invitations/{{id}}", async (
            Guid id, IPendingInvitationRepository invitationRepo, IUserInvitationProvider invitationProvider) =>
        {
            var invitation = await invitationRepo.GetByIdAsync(id).ConfigureAwait(false);
            if (invitation is null) return Results.NotFound();

            await invitationProvider.RevokeInvitedAccountAsync(invitation.ExternalObjectId).ConfigureAwait(false);

            invitation.Status = InvitationStatus.Revoked;
            await invitationRepo.UpdateAsync(invitation).ConfigureAwait(false);
            await invitationRepo.SaveChangesAsync().ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization("users:edit");

        // Authenticated-only — any signed-in user may redeem their own pending invitation.
        // Ownership is proven by matching the caller's own ExternalObjectId (resolved server-side
        // from their auth context) against the invitation's, never by trusting a client-supplied
        // user id. A non-owner (or unknown id) gets 404 either way, so probing ids can't be used
        // to learn whether a given invitation exists.
        routes.MapPost($"{prefix}/invitations/{{id}}/redeem", async (
            Guid id, IPendingInvitationRepository invitationRepo, IGeoAuthorizationService authService,
            TimeProvider timeProvider) =>
        {
            var invitation = await invitationRepo.GetByIdAsync(id).ConfigureAwait(false);
            if (invitation is null) return Results.NotFound();

            var ctx = await authService.GetAuthorizationContextAsync().ConfigureAwait(false);
            if (!string.Equals(invitation.ExternalObjectId, ctx.User.ExternalObjectId, StringComparison.Ordinal))
                return Results.NotFound();

            invitation.Status     = InvitationStatus.Redeemed;
            invitation.RedeemedAt = timeProvider.GetUtcNow().UtcDateTime;
            await invitationRepo.UpdateAsync(invitation).ConfigureAwait(false);
            await invitationRepo.SaveChangesAsync().ConfigureAwait(false);
            return Results.NoContent();
        });
    }

    // ── User Claims (XD01-59 Phase 3, XD01-87) ──────────────────────────────

    private static void MapUserClaimsApi(IEndpointRouteBuilder routes, string prefix)
    {
        // All authenticated-only, self-service — every check is "is this MY claim", derived
        // from the caller's own AuthorizationContext, never from a client-supplied user id. A
        // claim that exists but isn't the caller's own is indistinguishable from one that
        // doesn't exist at all (404, never 403), mirroring invitations/{id}/redeem's ownership
        // philosophy (XD01-69) so ids can't be probed to learn what belongs to someone else.
        MapUserClaimReads(routes, prefix);
        MapUserClaimWrites(routes, prefix);
    }

    private static void MapUserClaimReads(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapGet($"{prefix}/userclaims", async (IUserClaimRepository claimRepo, IGeoAuthorizationService authService) =>
        {
            var ctx = await authService.GetAuthorizationContextAsync().ConfigureAwait(false);
            var claims = await claimRepo.GetByUserIdAsync(ctx.User.Id).ConfigureAwait(false);
            return Results.Json(claims.Select(ToDto), _opts);
        });

        routes.MapGet($"{prefix}/userclaims/{{claimType}}", async (
            string claimType, IUserClaimRepository claimRepo, IGeoAuthorizationService authService) =>
        {
            var ctx = await authService.GetAuthorizationContextAsync().ConfigureAwait(false);
            var claim = await claimRepo.GetAsync(ctx.User.Id, claimType).ConfigureAwait(false);
            return claim is null ? Results.NotFound() : Results.Json(ToDto(claim), _opts);
        });
    }

    private static void MapUserClaimWrites(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapPost($"{prefix}/userclaims", async (
            UserClaimWriteDto dto, IUserClaimRepository claimRepo, IGeoAuthorizationService authService) =>
        {
            var ctx = await authService.GetAuthorizationContextAsync().ConfigureAwait(false);
            var claim = new UserClaim { UserId = ctx.User.Id, Type = dto.Type, Value = dto.Value };

            await claimRepo.AddAsync(claim).ConfigureAwait(false);
            await claimRepo.SaveChangesAsync().ConfigureAwait(false);
            return Results.Json(ToDto(claim), _opts, statusCode: StatusCodes.Status201Created);
        });

        routes.MapPut($"{prefix}/userclaims/{{claimId}}", async (
            Guid claimId, UserClaimUpdateDto dto, IUserClaimRepository claimRepo, IGeoAuthorizationService authService) =>
        {
            var claim = await FindOwnClaimAsync(claimRepo, authService, claimId).ConfigureAwait(false);
            if (claim is null) return Results.NotFound();

            claim.Value = dto.Value;
            await claimRepo.UpdateAsync(claim).ConfigureAwait(false);
            await claimRepo.SaveChangesAsync().ConfigureAwait(false);
            return Results.NoContent();
        });

        routes.MapDelete($"{prefix}/userclaims/{{claimId}}", async (
            Guid claimId, IUserClaimRepository claimRepo, IGeoAuthorizationService authService) =>
        {
            var claim = await FindOwnClaimAsync(claimRepo, authService, claimId).ConfigureAwait(false);
            if (claim is null) return Results.NotFound();

            await claimRepo.RemoveAsync(claimId).ConfigureAwait(false);
            await claimRepo.SaveChangesAsync().ConfigureAwait(false);
            return Results.NoContent();
        });

        routes.MapDelete($"{prefix}/userclaims", async (IUserClaimRepository claimRepo, IGeoAuthorizationService authService) =>
        {
            var ctx = await authService.GetAuthorizationContextAsync().ConfigureAwait(false);
            await claimRepo.RemoveAllAsync(ctx.User.Id).ConfigureAwait(false);
            await claimRepo.SaveChangesAsync().ConfigureAwait(false);
            return Results.NoContent();
        });
    }

    // GetByUserIdAsync, not a direct by-id lookup — IUserClaimRepository has no "get claim by its
    // own id" method, and filtering the caller's own list this way makes an unowned or unknown
    // claimId equally (and correctly) come back empty, without needing a new repository method.
    private static async Task<UserClaim?> FindOwnClaimAsync(
        IUserClaimRepository claimRepo, IGeoAuthorizationService authService, Guid claimId)
    {
        var ctx = await authService.GetAuthorizationContextAsync().ConfigureAwait(false);
        var claims = await claimRepo.GetByUserIdAsync(ctx.User.Id).ConfigureAwait(false);
        return claims.FirstOrDefault(c => c.Id == claimId);
    }

    private static UserSummaryDto ToSummaryDto(AppUser u) =>
        new(u.Id, u.Email, u.DisplayName, u.IsActive, u.OrganizationId);

    private static RoleSummaryDto ToSummaryDto(AppRole r) =>
        new(r.Id, r.Name, r.Description, r.IsBuiltIn);

    private static PermissionDto ToDto(AppPermission p) =>
        new(p.Id, p.Code, p.Resource, p.Action, p.Description);

    private static OrganizationDto ToDto(Organization o) =>
        new(o.Id, o.Name, o.Slug, o.Description, o.IsActive, o.CreatedAt);

    private static GroupDto ToDto(AppGroup g) =>
        new(g.Id, g.Name, g.Description, g.OrganizationId, g.IsActive, g.CreatedAt);

    private static PendingInvitationDto ToDto(PendingInvitation i) =>
        new(i.Id, i.Email, i.ExternalObjectId, i.InvitedByUserId, i.InvitedAt, i.RedeemedAt, i.Status);

    private static UserClaimDto ToDto(UserClaim c) => new(c.Id, c.Type, c.Value);
}

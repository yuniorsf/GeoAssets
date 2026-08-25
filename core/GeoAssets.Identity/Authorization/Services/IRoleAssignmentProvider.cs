using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Provider-agnostic seam for registering a locally-defined <see cref="AppRole"/> with the
/// authentication provider so it becomes a real, assignable role in the sign-in flow, and for
/// assigning/revoking that role to/from a user (XD01-59 Phase 2). Mirrors the decoupling
/// discipline of <see cref="GeoAssets.Identity.Authentication.IGeoAuthenticationProvider"/>
/// (XD01-48) — no method here mentions Entra, Graph, App Roles, or any other vendor concept, so
/// swapping identity providers is a DI registration change, not a call-site change.
///
/// GeoAssets's fine-grained <c>resource:action</c> permissions (<see cref="AppPermission"/> via
/// <see cref="RolePermission"/>) stay entirely local and are never pushed to the provider — only
/// the role's identity (name/description/existence) needs to be known there.
/// </summary>
public interface IRoleAssignmentProvider
{
    /// <summary>
    /// Registers <paramref name="role"/> with the authentication provider — idempotent
    /// create-or-update. No-ops (from the caller's perspective) if the provider doesn't need
    /// role definitions registered ahead of assignment.
    /// </summary>
    Task RegisterRoleAsync(AppRole role, CancellationToken ct = default);

    /// <summary>Unregisters a role, e.g. after local deletion. No-ops if never registered.</summary>
    Task UnregisterRoleAsync(Guid roleId, CancellationToken ct = default);

    /// <summary>
    /// Assigns <paramref name="roleName"/> to the user identified by their external object id
    /// (same id space as <c>CurrentUser.ExternalObjectId</c> / <c>AppUser.ExternalObjectId</c>).
    /// Idempotent — no-ops if already assigned.
    /// </summary>
    Task AssignRoleAsync(string externalUserObjectId, string roleName, CancellationToken ct = default);

    /// <summary>Revokes <paramref name="roleName"/> from the user. No-ops if not assigned.</summary>
    Task RevokeRoleAsync(string externalUserObjectId, string roleName, CancellationToken ct = default);

    /// <summary>
    /// The role names currently assigned to the user, per the provider's own records (not the
    /// token — lets an admin screen show current state without requiring that user to be
    /// signed in right now).
    /// </summary>
    Task<IReadOnlyList<string>> GetAssignedRoleNamesAsync(string externalUserObjectId, CancellationToken ct = default);
}

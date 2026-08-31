namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Provider-agnostic seam for creating/revoking the identity-provider account behind an
/// invite-only registration (XD01-59 Phase 3). Mirrors the single-purpose discipline of
/// <see cref="GeoAssets.Identity.Authentication.IGeoAuthenticationProvider"/> (XD01-48) and
/// <see cref="IRoleAssignmentProvider"/> (XD01-59 Phase 2) — no method here mentions Entra,
/// Graph, or any other vendor concept, so swapping identity providers is a DI registration
/// change, not a call-site change.
/// </summary>
public interface IUserInvitationProvider
{
    /// <summary>Creates the account at the identity provider; returns its external object id.</summary>
    Task<string> CreateInvitedAccountAsync(string email, string displayName, CancellationToken ct = default);

    /// <summary>Revokes a previously-created invited account (soft-disable, not deletion).</summary>
    Task RevokeInvitedAccountAsync(string externalObjectId, CancellationToken ct = default);
}

using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Client-facing operations for the invite-only registration workflow (XD01-59 Phase 3, XD01-71).
///
/// Deliberately separate from <see cref="GeoAssets.Identity.Authorization.Repositories.IPendingInvitationRepository"/>:
/// creating, revoking, and redeeming an invitation are orchestrated, multi-step server operations
/// (Graph account creation, email send, ownership-checked redemption) exposed by
/// <c>GeoAssets.Server</c>'s <c>/api/identity/invitations/*</c> endpoints, not plain CRUD — see
/// that repository's own doc comments (XD01-70) for why they were kept off it.
///
/// Not registered at all under <c>Identity:Backend=InMemory</c> (no server round-trip exists in
/// that mode, so invitations can never be functional there) — callers resolve it optionally, the
/// same pattern already used for <c>UserProvisioningService</c>.
/// </summary>
public interface IInvitationClient
{
    /// <summary>Creates an invitation; returns the resulting row (see the XD01-69 endpoint's 201-vs-202 distinction for partial email-send failure).</summary>
    Task<PendingInvitation> CreateInvitationAsync(string email, string displayName, CancellationToken ct = default);

    Task RevokeInvitationAsync(Guid id, CancellationToken ct = default);

    /// <summary>Redeems the invitation identified by <paramref name="id"/> — must belong to the caller; the server rejects otherwise (XD01-69).</summary>
    Task RedeemInvitationAsync(Guid id, CancellationToken ct = default);
}

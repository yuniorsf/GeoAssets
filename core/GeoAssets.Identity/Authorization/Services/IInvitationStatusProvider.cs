namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Reports whether the invite-only registration feature is really usable (both
/// <see cref="IUserInvitationProvider"/> and <see cref="IInvitationEmailSender"/> Graph/ACS-backed,
/// vs. either resolving to its no-op default) — XD01-59 Phase 3, mirrors
/// <see cref="IRoleSyncStatusProvider"/>'s reasoning exactly: a client-side consumer can't tell
/// which concrete providers the server resolved just by having working
/// <see cref="IPendingInvitationRepository"/> access, so the admin UI asks this instead before
/// showing the "Invite user" control.
///
/// Not registered at all under <c>Identity:Backend=InMemory</c> (no server round-trip exists in
/// that mode, so invitations can never be functional there) — callers resolve it optionally, the
/// same pattern already used for <c>UserProvisioningService</c>.
/// </summary>
public interface IInvitationStatusProvider
{
    Task<bool> IsEnabledAsync(CancellationToken ct = default);
}

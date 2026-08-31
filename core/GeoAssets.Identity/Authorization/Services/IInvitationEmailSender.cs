namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Provider-agnostic seam for sending the invite-only registration email (XD01-59 Phase 3).
/// Mirrors the single-purpose discipline of <see cref="IUserInvitationProvider"/> and
/// <see cref="IRoleAssignmentProvider"/> — no method here mentions ACS or any other vendor
/// concept, so swapping email providers is a DI registration change, not a call-site change.
/// </summary>
public interface IInvitationEmailSender
{
    Task SendInvitationAsync(string toEmail, string displayName, CancellationToken ct = default);
}

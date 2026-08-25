namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// No-op implementation of <see cref="IUserInvitationProvider"/>.
///
/// Registered by default when invitations aren't configured, mirroring
/// <see cref="NullRoleAssignmentProvider"/> (XD01-61).
///
/// <code>
///   services.AddSingleton&lt;IUserInvitationProvider, NullUserInvitationProvider&gt;();
/// </code>
/// </summary>
public sealed class NullUserInvitationProvider : IUserInvitationProvider
{
    public Task<string> CreateInvitedAccountAsync(string email, string displayName, CancellationToken ct = default)
        => Task.FromResult(string.Empty);

    public Task RevokeInvitedAccountAsync(string externalObjectId, CancellationToken ct = default)
        => Task.CompletedTask;
}

namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// No-op implementation of <see cref="IInvitationEmailSender"/>.
///
/// Registered by default when invitations aren't configured, mirroring
/// <see cref="NullRoleAssignmentProvider"/> (XD01-61).
///
/// <code>
///   services.AddSingleton&lt;IInvitationEmailSender, NullInvitationEmailSender&gt;();
/// </code>
/// </summary>
public sealed class NullInvitationEmailSender : IInvitationEmailSender
{
    public Task SendInvitationAsync(string toEmail, string displayName, CancellationToken ct = default)
        => Task.CompletedTask;
}

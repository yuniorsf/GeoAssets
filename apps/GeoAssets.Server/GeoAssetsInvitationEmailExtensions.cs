using Azure;
using Azure.Communication.Email;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Server;

/// <summary>
/// Wires <see cref="IInvitationEmailSender"/> (XD01-59 Phase 3), mirroring
/// <see cref="GeoAssetsUserInvitationExtensions.AddUserInvitationProvider"/>'s
/// optional-provider-argument pattern and shared <c>Invitation:Enabled</c> gate: a
/// caller-supplied <paramref name="sender"/> always wins. Otherwise, <c>Invitation:Enabled=true</c>
/// registers the real, ACS-backed <see cref="AcsEmailInvitationSender"/> (XD01-68), reading its
/// credential from the <c>"AcsEmail"</c> configuration section (see <see cref="AcsEmailOptions"/>).
/// Anything else (including no <c>"Invitation"</c> section at all) registers
/// <see cref="NullInvitationEmailSender"/>.
/// </summary>
public static class GeoAssetsInvitationEmailExtensions
{
    public static IServiceCollection AddInvitationEmailSender(
        this IServiceCollection services,
        IConfiguration          configuration,
        IInvitationEmailSender? sender = null)
    {
        if (sender is not null)
        {
            services.AddSingleton(sender);
            return services;
        }

        var invitationOptions = configuration.GetSection("Invitation").Get<InvitationOptions>() ?? new InvitationOptions();
        if (!invitationOptions.Enabled)
        {
            services.AddSingleton<IInvitationEmailSender>(new NullInvitationEmailSender());
            return services;
        }

        var acsOptions = configuration.GetSection("AcsEmail").Get<AcsEmailOptions>() ?? new AcsEmailOptions();
        services.AddSingleton<IInvitationEmailSender>(_ => new AcsEmailInvitationSender(
            new EmailClient(new Uri(acsOptions.Endpoint), new AzureKeyCredential(acsOptions.AccessKey)),
            acsOptions,
            invitationOptions));

        return services;
    }
}

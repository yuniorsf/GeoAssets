using GeoAssets.Identity.Authorization.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Server;

/// <summary>
/// Wires <see cref="IUserInvitationProvider"/> (XD01-59 Phase 3), mirroring
/// <see cref="GeoAssetsRoleAssignmentExtensions.AddRoleAssignmentProvider"/>'s
/// optional-provider-argument pattern: a caller-supplied <paramref name="provider"/> always
/// wins. Otherwise, reads the <c>"Invitation"</c> configuration section (see
/// <see cref="InvitationOptions"/>) — <c>Invitation:Enabled=true</c> registers the real,
/// Graph-backed <see cref="EntraGraphUserInvitationProvider"/> (XD01-67), reusing the same
/// <c>"RoleSync"</c> credential (see <see cref="RoleSyncOptions.ToCredential"/>) rather than
/// provisioning a second one — and, if <see cref="GeoAssetsRoleAssignmentExtensions.AddRoleAssignmentProvider"/>
/// has already registered an <see cref="IGraphAccessTokenProvider"/> singleton, the exact same
/// MSAL confidential-client instance (<c>TryAddSingleton</c> — registration order between the two
/// extension methods doesn't matter). Anything else (including no <c>"Invitation"</c> section at
/// all) registers <see cref="NullUserInvitationProvider"/>.
/// </summary>
public static class GeoAssetsUserInvitationExtensions
{
    public static IServiceCollection AddUserInvitationProvider(
        this IServiceCollection services,
        IConfiguration           configuration,
        IUserInvitationProvider? provider = null)
    {
        if (provider is not null)
        {
            services.AddSingleton(provider);
            return services;
        }

        var invitationOptions = configuration.GetSection("Invitation").Get<InvitationOptions>() ?? new InvitationOptions();
        if (!invitationOptions.Enabled)
        {
            services.AddSingleton<IUserInvitationProvider>(new NullUserInvitationProvider());
            return services;
        }

        var credential = (configuration.GetSection("RoleSync").Get<RoleSyncOptions>() ?? new RoleSyncOptions()).ToCredential();

        services.TryAddSingleton<IGraphAccessTokenProvider>(new MsalGraphAccessTokenProvider(credential));
        services.AddHttpClient(GeoAssetsRoleAssignmentExtensions.GraphHttpClientName, c => c.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/"));
        services.AddSingleton<IUserInvitationProvider>(sp => new EntraGraphUserInvitationProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(GeoAssetsRoleAssignmentExtensions.GraphHttpClientName),
            sp.GetRequiredService<IGraphAccessTokenProvider>(),
            credential));

        return services;
    }
}

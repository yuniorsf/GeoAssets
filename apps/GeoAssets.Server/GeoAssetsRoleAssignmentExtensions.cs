using GeoAssets.Identity.Authorization.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Server;

/// <summary>
/// Wires <see cref="IRoleAssignmentProvider"/> (XD01-59 Phase 2), mirroring
/// <see cref="GeoAssetsAuthenticationExtensions.AddGeoAssetsAuthentication"/>'s
/// optional-provider-argument pattern: a caller-supplied <paramref name="provider"/> always
/// wins. Otherwise, reads the <c>"RoleSync"</c> configuration section (see
/// <see cref="RoleSyncOptions"/>) — <c>RoleSync:Enabled=true</c> registers the real,
/// Graph-backed <see cref="EntraGraphRoleAssignmentProvider"/> (XD01-62); anything else
/// (including no <c>"RoleSync"</c> section at all) registers
/// <see cref="NullRoleAssignmentProvider"/>, so Phase 1's admin UI is unaffected in every
/// deployment that hasn't opted in.
/// </summary>
public static class GeoAssetsRoleAssignmentExtensions
{
    /// <summary>Shared named Graph HttpClient — <c>GeoAssetsUserInvitationExtensions</c> (XD01-67) reuses the same name.</summary>
    internal const string GraphHttpClientName = "GeoAssetsGraphRoleSync";

    public static IServiceCollection AddRoleAssignmentProvider(
        this IServiceCollection services,
        IConfiguration          configuration,
        IRoleAssignmentProvider? provider = null)
    {
        if (provider is not null)
        {
            services.AddSingleton(provider);
            return services;
        }

        var options = configuration.GetSection("RoleSync").Get<RoleSyncOptions>() ?? new RoleSyncOptions();
        if (!options.Enabled)
        {
            services.AddSingleton<IRoleAssignmentProvider>(new NullRoleAssignmentProvider());
            return services;
        }

        services.AddSingleton(options);
        // TryAdd, not Add: GeoAssetsUserInvitationExtensions.AddUserInvitationProvider (XD01-67)
        // may have already registered this against the same RoleSync credential — whichever
        // extension method runs first wins, so only one MSAL confidential-client instance is
        // ever built regardless of call order.
        services.TryAddSingleton<IGraphAccessTokenProvider>(new MsalGraphAccessTokenProvider(options.ToCredential()));
        services.AddHttpClient(GraphHttpClientName, c => c.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/"));
        services.AddSingleton<IRoleAssignmentProvider>(sp => new EntraGraphRoleAssignmentProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(GraphHttpClientName),
            sp.GetRequiredService<IGraphAccessTokenProvider>(),
            options));

        return services;
    }
}

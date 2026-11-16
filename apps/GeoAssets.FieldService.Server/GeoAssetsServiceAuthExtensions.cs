using GeoAssets.Identity.Authentication;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Wires <see cref="IServiceAuthTokenProvider"/> (XD01-299), mirroring
/// <c>GeoAssetsRoleAssignmentExtensions.AddRoleAssignmentProvider</c>'s configuration-gated
/// pattern: reads the <c>"ServiceAuth"</c> section and only registers the real, Entra-backed
/// provider when <c>ServiceAuth:Enabled=true</c>. Anything else (including no
/// <c>"ServiceAuth"</c> section at all) registers nothing —
/// <c>GeoAssets.Provider.Rest.RestGeoCoreClient</c> and
/// <c>GeoAssets.Provider.Rest.FederatedGeoCoreClientResolver</c> already tolerate a missing
/// <see cref="IServiceAuthTokenProvider"/> by sending no auth header, unchanged from before this
/// ticket, so every deployment that hasn't provisioned a real Entra app registration for this
/// host keeps working exactly as it did.
/// </summary>
internal static class GeoAssetsServiceAuthExtensions
{
    internal static IServiceCollection AddServiceAuthTokenProvider(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.GetSection("ServiceAuth").Get<ServiceAuthOptions>() ?? new ServiceAuthOptions();
        if (!options.Enabled) return services;

        services.AddSingleton<IServiceAuthTokenProvider>(new EntraCiamServiceAuthTokenProvider(options));
        return services;
    }
}

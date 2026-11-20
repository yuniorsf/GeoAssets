using GeoAssets.Identity.Authentication;

namespace GeoAssets.NeutralOperator.Server;

/// <summary>
/// Wires <see cref="IServiceAuthTokenProvider"/> (XD01-299) from the <c>"ServiceAuth"</c> section — the same
/// configuration-gated pattern as <c>GeoAssets.FieldService.Server</c>: the Entra-backed provider only when
/// <c>ServiceAuth:Enabled=true</c>, nothing otherwise (see <see cref="ServiceAuthOptions"/> for what that means here).
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

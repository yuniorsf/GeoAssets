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

        var instance = string.IsNullOrWhiteSpace(options.Instance) ? configuration["AzureAdCiam:Instance"] : options.Instance;
        if (!CiamAuthority.TryCreate(instance, options.TenantId, out var authority))
            throw new InvalidOperationException(
                "ServiceAuth:Enabled is true but ServiceAuth has no CIAM authority: set ServiceAuth:TenantId and "
                + "ServiceAuth:Instance (or AzureAdCiam:Instance) to the CIAM instance, an absolute https URL such as "
                + "https://<tenant>.ciamlogin.com/ (XD01-452).");

        services.AddSingleton<IServiceAuthTokenProvider>(new EntraCiamServiceAuthTokenProvider(options, authority));
        return services;
    }
}

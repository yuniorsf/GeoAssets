using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Server;

/// <summary>
/// DI registration for the AuthZ bridge (XD01-13): lets minimal API endpoints declare
/// <c>.RequireAuthorization("SomePolicyName")</c> — matching an <c>AppPolicy.Name</c> row —
/// and get evaluated by <see cref="GeoAuthorizationHandler"/> against
/// <c>IGeoAuthorizationService</c> instead of a hand-coded ASP.NET Core policy.
///
/// Requires <c>AddGeoIdentity()</c> (XD01-14) already registered — <see cref="GeoAuthorizationHandler"/>
/// depends on <c>IGeoAuthorizationService</c>, which that call provides.
/// </summary>
public static class GeoAssetsAuthorizationExtensions
{
    public static IServiceCollection AddGeoAuthorizationPolicyBridge(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationPolicyProvider, GeoAuthorizationPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, GeoAuthorizationHandler>();

        return services;
    }
}

using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// The named-policy bridge a host that runs order types needs for its routes' <c>RequireAuthorization("serviceorders:read")</c>
/// (XD01-382): every policy name resolves to a <see cref="GeoPolicyRequirement"/> that <see cref="GeoAuthorizationHandler"/>
/// evaluates against the host's <c>IGeoAuthorizationService</c>. The kernel registers the same two through its own
/// <c>AddGeoAuthorizationPolicyBridge</c>, together with its feature-resource handlers.
/// </summary>
public static class GeoPermissionPolicyExtensions
{
    public static IServiceCollection AddGeoPermissionPolicies(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAuthorizationPolicyProvider, GeoAuthorizationPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, GeoAuthorizationHandler>();

        return services;
    }
}

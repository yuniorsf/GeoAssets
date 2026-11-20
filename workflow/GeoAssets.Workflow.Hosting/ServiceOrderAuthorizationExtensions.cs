using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// Registers the service-order requirement handlers (XD01-364). Separate from
/// <see cref="GeoAssetsAuthorizationExtensions.AddGeoAuthorizationPolicyBridge"/> because they need what a host that runs
/// service orders registers — <c>ServiceOrderRules</c>, the <c>OrderTypeRegistry</c> and
/// <see cref="ServerWorkflowPrincipalFactory"/> — and every authorization call resolves every handler.
/// </summary>
public static class ServiceOrderAuthorizationExtensions
{
    public static IServiceCollection AddServiceOrderAuthorization(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, ServiceOrderActionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, ServiceOrderCreateAuthorizationHandler>();

        return services;
    }
}

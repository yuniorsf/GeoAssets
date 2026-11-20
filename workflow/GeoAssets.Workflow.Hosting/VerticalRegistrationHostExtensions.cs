using GeoAssets.Identity.Authorization.Modules;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// A vertical host's bootstrap registration with the kernel (XD01-382, shared by every vertical host since XD01-385):
/// <see cref="VerticalRegistrationService"/> on the <see cref="VerticalRegistrationOptions"/> schedule, its
/// <see cref="VerticalRegistrationHealthCheck"/> — Degraded (so <c>/healthz</c> stays 200) until registered — and the
/// <c>/readyz</c> route that answers 503 until then. The host registers the kernel's machine-to-machine clients
/// (<c>AddKernelServiceCallerClients</c>) and its <see cref="IModulePermissionContributor"/>s itself.
/// </summary>
public static class VerticalRegistrationHostExtensions
{
    /// <summary>The route of the readiness probe <see cref="MapVerticalReadiness"/> maps.</summary>
    public const string ReadinessRoute = "/readyz";

    public static IServiceCollection AddVerticalRegistration(
        this IServiceCollection services, IConfiguration configuration, VerticalRegistrationSource source)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.VerticalId);

        services.Configure<VerticalRegistrationOptions>(configuration.GetSection(VerticalRegistrationOptions.SectionName));
        services.AddSingleton(source);
        services.AddSingleton<VerticalRegistrationState>();
        services.AddHostedService<VerticalRegistrationService>();
        services.AddHealthChecks()
            .AddCheck<VerticalRegistrationHealthCheck>(VerticalRegistrationHealthCheck.Name, tags: [VerticalRegistrationHealthCheck.ReadinessTag]);

        return services;
    }

    /// <summary>
    /// <c>GET /readyz</c>, anonymous: Healthy only once the kernel knows this vertical (every check tagged
    /// <see cref="VerticalRegistrationHealthCheck.ReadinessTag"/>); 503 otherwise.
    /// </summary>
    public static IEndpointConventionBuilder MapVerticalReadiness(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapHealthChecks(ReadinessRoute, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(VerticalRegistrationHealthCheck.ReadinessTag),
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        }).AllowAnonymous();
    }
}

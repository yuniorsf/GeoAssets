using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.SlaGuard;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Workflow;

/// <summary>
/// The SLA guard add-on's registration (XD01-376, moved out of the engine's <c>WorkflowServiceExtensions</c>; kept in the
/// <c>GeoAssets.Workflow</c> namespace so hosts register it as before).
/// </summary>
public static class SlaGuardServiceExtensions
{
    /// <summary>
    /// Registers <see cref="ISlaBreachQueryService"/> (XD01-233) for whatever
    /// <see cref="IServiceOrderReader"/> is already in DI. Pass a configured
    /// <see cref="SlaGuardEngine"/> (sourced from the current official regulatory text — see
    /// <see cref="SlaMatrixRow"/>'s doc comment) once one exists; without it, every query
    /// correctly reports <see cref="SlaBreachSummary.NotConfigured"/>.
    /// </summary>
    public static IServiceCollection AddSlaBreachReporting(
        this IServiceCollection services,
        SlaGuardEngine? engine = null)
    {
        services.AddScoped<ISlaBreachQueryService>(sp => new SlaBreachQueryService(
            sp.GetRequiredService<IServiceOrderReader>(),
            engine,
            sp.GetService<TimeProvider>() ?? TimeProvider.System));

        return services;
    }
}

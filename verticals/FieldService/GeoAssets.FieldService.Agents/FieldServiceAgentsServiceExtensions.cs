using GeoAssets.FieldService.Agents.Dispatch;
using GeoAssets.FieldService.Agents.Identity;
using GeoAssets.FieldService.Contractors;
using GeoAssets.Workflow.Dispatch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeoAssets.FieldService.Agents;

/// <summary>
/// DI registration helpers for AI-agent participation in the service order flow.
/// </summary>
/// <example>
/// <b>appsettings.json</b>
/// <code>
/// {
///   "FieldServiceAgents": {
///     "Agents": {
///       "agent-hydro-01": {
///         "RoleNames": [ "AutomationAgent" ]
///       }
///     }
///   }
/// }
/// </code>
///
/// <b>Program.cs — from configuration</b>
/// <code>
/// builder.Services.AddFieldServiceAgents(builder.Configuration);
/// </code>
///
/// <b>Program.cs — inline</b>
/// <code>
/// builder.Services.AddFieldServiceAgents(opts =>
/// {
///     opts.Agents["agent-hydro-01"] = new() { RoleNames = ["AutomationAgent"] };
/// });
/// </code>
/// </example>
public static class FieldServiceAgentsServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IAgentIdentityProvider"/> with options read from
    /// <c>configuration["FieldServiceAgents"]</c>. While that section is absent, the legacy
    /// <c>configuration["WorkflowAgents"]</c> section is read instead and a warning is logged
    /// (<see cref="AgentIdentityOptions.LegacySectionName"/>).
    /// </summary>
    public static IServiceCollection AddFieldServiceAgents(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        services.AddOptions();
        services.AddSingleton<IConfigureOptions<AgentIdentityOptions>>(sp =>
            new AgentIdentityOptionsSetup(
                configuration,
                sp.GetService<ILogger<AgentIdentityOptions>>() ?? NullLogger<AgentIdentityOptions>.Instance));

        services.AddSingleton<IAgentIdentityProvider, ConfiguredAgentIdentityProvider>();

        return services;
    }

    /// <summary>Registers <see cref="IAgentIdentityProvider"/> with inline options.</summary>
    public static IServiceCollection AddFieldServiceAgents(
        this IServiceCollection            services,
        Action<AgentIdentityOptions>       configure)
    {
        services.Configure(configure);

        services.AddSingleton<IAgentIdentityProvider, ConfiguredAgentIdentityProvider>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="ICrewRoutingEngine"/> (Clarke-Wright savings heuristic) and
    /// <see cref="FeederClusterAutoDispatchService"/> for feeder-clustered VRPTW crew dispatch
    /// (XD01-240). Does not itself register <see cref="IAgentIdentityProvider"/> — call one of
    /// the <see cref="AddFieldServiceAgents(IServiceCollection, IConfiguration)"/> overloads too.
    /// </summary>
    public static IServiceCollection AddFeederClusterAutoDispatch(this IServiceCollection services)
    {
        services.AddSingleton<ICrewRoutingEngine, ClarkeWrightVrptwRoutingEngine>();
        services.AddScoped<FeederClusterAutoDispatchService>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="ICrewRoutingEngine"/> (idempotently — safe alongside
    /// <see cref="AddFeederClusterAutoDispatch"/>, which registers the same engine), plus
    /// <see cref="OrderRemunerationCalculator"/> and <see cref="DynamicCrewDispatchAdvisorService"/>
    /// for XD01-256's day-to-day, yield-ranked dispatch advisory pass. Requires
    /// <see cref="IUsCatalogRepository"/> to already be registered (see
    /// <c>GeoAssets.Provider.PostgreSQL</c>'s <c>AddGeoAssetsPostgres</c> or an equivalent
    /// server-side registration) — this method does not provide one.
    /// </summary>
    public static IServiceCollection AddDynamicCrewDispatchAdvisor(this IServiceCollection services)
    {
        services.TryAddSingleton<ICrewRoutingEngine, ClarkeWrightVrptwRoutingEngine>();
        services.AddScoped<OrderRemunerationCalculator>();
        services.AddScoped<DynamicCrewDispatchAdvisorService>();

        return services;
    }
}

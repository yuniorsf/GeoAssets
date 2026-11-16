using GeoAssets.Workflow.Agents.Dispatch;
using GeoAssets.Workflow.Agents.Identity;
using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.Dispatch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Workflow.Agents;

/// <summary>
/// DI registration helpers for AI-agent participation in the service order flow.
/// </summary>
/// <example>
/// <b>appsettings.json</b>
/// <code>
/// {
///   "WorkflowAgents": {
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
/// builder.Services.AddWorkflowAgents(builder.Configuration);
/// </code>
///
/// <b>Program.cs — inline</b>
/// <code>
/// builder.Services.AddWorkflowAgents(opts =>
/// {
///     opts.Agents["agent-hydro-01"] = new() { RoleNames = ["AutomationAgent"] };
/// });
/// </code>
/// </example>
public static class WorkflowAgentsServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IAgentIdentityProvider"/> with options read from
    /// <c>configuration["WorkflowAgents"]</c>.
    /// </summary>
    public static IServiceCollection AddWorkflowAgents(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        services.Configure<AgentIdentityOptions>(opts =>
            configuration.GetSection(AgentIdentityOptions.SectionName).Bind(opts));

        services.AddSingleton<IAgentIdentityProvider, ConfiguredAgentIdentityProvider>();

        return services;
    }

    /// <summary>Registers <see cref="IAgentIdentityProvider"/> with inline options.</summary>
    public static IServiceCollection AddWorkflowAgents(
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
    /// the <see cref="AddWorkflowAgents(IServiceCollection, IConfiguration)"/> overloads too.
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

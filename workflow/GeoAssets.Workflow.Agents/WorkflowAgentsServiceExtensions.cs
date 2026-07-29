using GeoAssets.Workflow.Agents.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
}

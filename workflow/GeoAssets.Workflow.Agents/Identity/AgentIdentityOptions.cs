namespace GeoAssets.Workflow.Agents.Identity;

/// <summary>
/// Configuration surface for <see cref="WorkflowAgentsServiceExtensions.AddWorkflowAgents(Microsoft.Extensions.DependencyInjection.IServiceCollection, Microsoft.Extensions.Configuration.IConfiguration)"/>.
/// Maps a registered agent id to the identity claims it should present when acting on service orders.
/// </summary>
public sealed class AgentIdentityOptions
{
    public const string SectionName = "WorkflowAgents";

    public Dictionary<string, AgentIdentityDescriptor> Agents { get; set; } = [];
}

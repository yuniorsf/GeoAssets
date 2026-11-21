namespace GeoAssets.FieldService.Agents.Identity;

/// <summary>
/// Configuration surface for <see cref="FieldServiceAgentsServiceExtensions.AddFieldServiceAgents(Microsoft.Extensions.DependencyInjection.IServiceCollection, Microsoft.Extensions.Configuration.IConfiguration)"/>.
/// Maps a registered agent id to the identity claims it should present when acting on service orders.
/// </summary>
public sealed class AgentIdentityOptions
{
    public const string SectionName = "FieldServiceAgents";

    /// <summary>
    /// The section's name before the agents moved into the FieldService vertical (XD01-399).
    /// Read only while <see cref="SectionName"/> is absent, with a warning; to be removed in a
    /// later release (XD01-406).
    /// </summary>
    public const string LegacySectionName = "WorkflowAgents";

    public Dictionary<string, AgentIdentityDescriptor> Agents { get; set; } = [];
}

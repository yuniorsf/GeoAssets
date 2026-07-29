using GeoAssets.Workflow.Rules;

namespace GeoAssets.Workflow.Agents.Identity;

/// <summary>
/// Configuration-bound description of a registered AI agent's identity claims —
/// the agent-side analogue of the data a host would otherwise assemble from
/// <c>GeoAssets.Identity</c> to build a human <see cref="WorkflowPrincipal"/>.
/// </summary>
public sealed class AgentIdentityDescriptor
{
    public string?       OrganizationId  { get; set; }
    public List<string>  RoleNames       { get; set; } = [];
    public List<string>  GroupIds        { get; set; } = [];
    public List<string>  PermissionCodes { get; set; } = [];
}

using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Extensions.Options;

namespace GeoAssets.Workflow.Agents.Identity;

/// <summary>Resolves agent identities from <see cref="AgentIdentityOptions"/> (configuration-bound).</summary>
public sealed class ConfiguredAgentIdentityProvider(IOptions<AgentIdentityOptions> options) : IAgentIdentityProvider
{
    public WorkflowPrincipal Resolve(string agentId)
    {
        if (!options.Value.Agents.TryGetValue(agentId, out var descriptor))
            throw new KeyNotFoundException($"No agent identity is registered for '{agentId}'.");

        return new WorkflowPrincipal(
            UserId          : agentId,
            OrganizationId  : descriptor.OrganizationId,
            RoleNames       : descriptor.RoleNames,
            GroupIds        : descriptor.GroupIds,
            PermissionCodes : descriptor.PermissionCodes)
        {
            Kind = ActorKind.Agent
        };
    }
}

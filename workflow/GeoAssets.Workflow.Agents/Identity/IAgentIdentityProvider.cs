using GeoAssets.Workflow.Rules;

namespace GeoAssets.Workflow.Agents.Identity;

/// <summary>
/// Resolves a registered agent id to the <see cref="WorkflowPrincipal"/> it should act as —
/// the agent-side counterpart to a host building a human principal from Identity data.
/// Executors use this so <see cref="ServiceOrderRules"/> evaluates an agent's actions
/// exactly like a human's: same relationship/role checks, no special-casing.
/// </summary>
public interface IAgentIdentityProvider
{
    /// <exception cref="KeyNotFoundException">No agent is registered under <paramref name="agentId"/>.</exception>
    WorkflowPrincipal Resolve(string agentId);
}

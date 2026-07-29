using GeoAssets.Workflow.Agents.Identity;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Agents.AI.Workflows;
using MafWorkflow = Microsoft.Agents.AI.Workflows.Workflow;

namespace GeoAssets.Workflow.Agents.Executors;

/// <summary>
/// First end-to-end proof that a Service Order's full lifecycle (create → dispatch → activate)
/// can be driven entirely by an AI agent actor, through a graph shaped exactly like
/// <see cref="ServiceOrderTransitions"/>'s Draft → Pending edge — using the same domain and
/// rules-engine calls a human-driven caller would use, per order type/role configuration.
/// </summary>
public static class EmergencyRepairAgentWorkflow
{
    public static MafWorkflow Build(
        IServiceOrderRepository repository,
        ServiceOrderRules       rules,
        OrderTypeRegistry       orderTypeRegistry,
        IAgentIdentityProvider  identity)
    {
        var create   = new CreateServiceOrderExecutor(repository, rules, orderTypeRegistry, identity);
        var dispatch = new DispatchServiceOrderExecutor(repository, rules, identity);

        return new WorkflowBuilder(create)
            .AddEdge(create, dispatch)
            .WithOutputFrom(dispatch)
            .Build();
    }
}

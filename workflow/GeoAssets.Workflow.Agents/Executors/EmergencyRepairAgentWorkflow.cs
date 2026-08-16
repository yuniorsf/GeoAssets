using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.Agents.Identity;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
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
    /// <param name="tracer">
    /// Issues the <c>Agent.Create</c>/<c>Agent.Dispatch</c> spans each executor records — see
    /// <see cref="GeoAssetsActivitySource.StartAgentActivity"/>. Register
    /// <c>AddGeoAssetsObservability</c> in the host to get a real instance.
    /// </param>
    /// <param name="loggerFactory">
    /// Used to create each executor's own <c>ILogger&lt;T&gt;</c> — the executors aren't
    /// DI-resolved (this factory method constructs them directly), so a factory rather than a
    /// pre-resolved logger is required here.
    /// </param>
    public static MafWorkflow Build(
        IServiceOrderRepository repository,
        ServiceOrderRules       rules,
        OrderTypeRegistry       orderTypeRegistry,
        IAgentIdentityProvider  identity,
        TimeProvider            timeProvider,
        GeoAssetsActivitySource tracer,
        ILoggerFactory          loggerFactory)
    {
        var create   = new CreateServiceOrderExecutor(
            repository, rules, orderTypeRegistry, identity, tracer, loggerFactory.CreateLogger<CreateServiceOrderExecutor>());
        var dispatch = new DispatchServiceOrderExecutor(
            repository, rules, identity, timeProvider, tracer, loggerFactory.CreateLogger<DispatchServiceOrderExecutor>());

        return new WorkflowBuilder(create)
            .AddEdge(create, dispatch)
            .WithOutputFrom(dispatch)
            .Build();
    }
}

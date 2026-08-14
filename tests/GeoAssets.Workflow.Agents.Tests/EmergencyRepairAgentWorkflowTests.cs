using FluentAssertions;
using GeoAssets.Workflow.Agents.Executors;
using GeoAssets.Workflow.Agents.Identity;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.Workflow.Agents.Tests;

public class EmergencyRepairAgentWorkflowTests
{
    private const string AgentId = "agent-hydro-01";

    private static OrderTypeRegistry OrderTypeRegistry()
    {
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType
        {
            Id          = "emergency-repair",
            DisplayName = "Emergency Repair",
            CreationPolicies = [new(PolicyKind.Role, "AutomationAgent")],
        });
        return registry;
    }

    private static IAgentIdentityProvider AgentIdentity()
    {
        var options = new AgentIdentityOptions
        {
            Agents = { [AgentId] = new AgentIdentityDescriptor { RoleNames = ["AutomationAgent"] } }
        };
        return new ConfiguredAgentIdentityProvider(Options.Create(options));
    }

    private static async Task<(ServiceOrder Order, IServiceOrderRepository Repository)> RunWorkflowAsync(ServiceOrderRules rules)
    {
        var repository = new ValidatingServiceOrderRepository(new InMemoryServiceOrderRepository());
        var workflow = EmergencyRepairAgentWorkflow.Build(
            repository, rules, OrderTypeRegistry(), AgentIdentity(), TimeProvider.System);

        var request = new CreateServiceOrderRequest(
            AgentId,
            "emergency-repair",
            "Valve failure downstream",
            DispatchTargetId  : "crew-1",
            DispatchTargetType: DispatchTargetType.Group,
            AgentInvocationId : "run-1");

        var run = await InProcessExecution.RunAsync(workflow, request);

        var order = run.OutgoingEvents.OfType<WorkflowOutputEvent>().Single().Data.Should().BeOfType<ServiceOrder>().Subject;
        return (order, repository);
    }

    [Fact]
    public async Task FullyAgentGranted_DrivesOrderFromDraftToPending()
    {
        var rules = new ServiceOrderRules(
            roleGrants: new Dictionary<string, IReadOnlySet<OrderActionType>>
            {
                ["AutomationAgent"] = new HashSet<OrderActionType> { OrderActionType.Dispatch }
            });

        var (order, _) = await RunWorkflowAsync(rules);

        order.Status.Should().Be(ServiceOrderStatus.Pending);
        order.Dispatches.Should().ContainSingle(d => d.ActorKind == ActorKind.Agent && d.AgentInvocationId == "run-1");
        order.ActionLog.Should().Contain(a => a.Action == OrderActionType.Dispatch && a.ActorKind == ActorKind.Agent);
    }

    [Fact]
    public async Task AgentWithoutDispatchGrant_LeavesOrderInDraft()
    {
        // Hybrid case: the agent may create the order (CreationPolicies grants it) but this
        // deployment's role-grant configuration withholds Dispatch — the graph must not force
        // it. The order is left exactly where a human dispatcher would find and act on it.
        var rules = new ServiceOrderRules(); // no roleGrants override => "AutomationAgent" has no default grants

        var (order, _) = await RunWorkflowAsync(rules);

        order.Status.Should().Be(ServiceOrderStatus.Draft);
        order.Dispatches.Should().BeEmpty();
    }

    [Fact]
    public async Task AgentWithoutDispatchGrant_ThenHumanDispatches_CompletesTheHandoff()
    {
        // The key hybrid scenario: the agent stops at Draft (no Dispatch grant), then a human
        // supervisor finishes the job through the exact same domain/repository calls a fully
        // human-driven flow would use — no code path distinguishes the two.
        var rules = new ServiceOrderRules();
        var (order, repository) = await RunWorkflowAsync(rules);
        order.Status.Should().Be(ServiceOrderStatus.Draft); // sanity: agent really stopped here

        var humanPrincipal = new WorkflowPrincipal("supervisor-1", null, ["Supervisor"], [], []);
        rules.Evaluate(humanPrincipal, OrderActionType.Dispatch, order).Allowed.Should().BeTrue();

        var dispatchedAt = DateTime.UtcNow;
        await repository.AppendDispatchAsync(order.Id, new OrderDispatch(
            "crew-1", DispatchTargetType.Group, "supervisor-1", dispatchedAt, "Picking up after agent"));
        await repository.AppendActionAsync(order.Id, new OrderActionLog(
            OrderActionType.Dispatch, "supervisor-1", dispatchedAt, ResultingStatus: ServiceOrderStatus.Pending));

        var final = await repository.GetByIdAsync(order.Id);
        final!.Status.Should().Be(ServiceOrderStatus.Pending);
        final.Dispatches.Should().ContainSingle(d => d.ActorKind == ActorKind.Human && d.DispatchedBy == "supervisor-1");
        final.ActionLog.Should().Contain(a => a.Action == OrderActionType.Dispatch && a.ActorKind == ActorKind.Human);
        final.Dispatches.Should().NotContain(d => d.ActorKind == ActorKind.Agent);
    }
}

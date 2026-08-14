using FluentAssertions;
using GeoAssets.Workflow.Agents.Executors;
using GeoAssets.Workflow.Agents.Identity;
using GeoAssets.Workflow.Agents.Tests.TestDoubles;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.Workflow.Agents.Tests;

public class DispatchServiceOrderExecutorTests
{
    private const string AgentId = "agent-1";

    private static IAgentIdentityProvider AgentIdentity() =>
        new ConfiguredAgentIdentityProvider(Options.Create(new AgentIdentityOptions
        {
            Agents = { [AgentId] = new AgentIdentityDescriptor { RoleNames = ["AutomationAgent"] } }
        }));

    private static ServiceOrderRules GrantingRules() => new(
        roleGrants: new Dictionary<string, IReadOnlySet<OrderActionType>>
        {
            ["AutomationAgent"] = new HashSet<OrderActionType> { OrderActionType.Dispatch }
        });

    [Fact]
    public async Task HandleAsync_Granted_PersistsDispatchAndActionLogThroughAppendCalls_NotJustStatus()
    {
        // Regression test: an earlier version of this executor called UpdateAsync after mutating
        // Dispatches/ActionLog in-place, but IServiceOrderWriter.UpdateAsync explicitly does not
        // persist those collections. That bug was invisible against InMemoryServiceOrderRepository
        // (which aliases the same object reference) — this uses a non-aliasing double instead, the
        // same shape a real out-of-process repository (e.g. EF Core) has.
        var repository = new SnapshottingServiceOrderRepository();
        var order = new ServiceOrder { Id = "order-1", OrderTypeId = "emergency-repair", CreatedBy = AgentId };
        await repository.AddAsync(order);

        var executor = new DispatchServiceOrderExecutor(repository, GrantingRules(), AgentIdentity(), TimeProvider.System);
        var message = new ServiceOrderCreated(order, AgentId, "crew-1", DispatchTargetType.Group, "run-1");

        var result = await executor.HandleAsync(message, NoOpWorkflowContext.Instance);

        result.Status.Should().Be(ServiceOrderStatus.Pending);
        result.Dispatches.Should().ContainSingle(d =>
            d.TargetId == "crew-1" && d.ActorKind == ActorKind.Agent && d.AgentInvocationId == "run-1");
        result.ActionLog.Should().Contain(a =>
            a.Action == OrderActionType.Dispatch && a.ActorKind == ActorKind.Agent && a.ResultingStatus == ServiceOrderStatus.Pending);

        // Prove it against a fresh read too, not just the returned object.
        var reloaded = await repository.GetByIdAsync("order-1");
        reloaded!.Status.Should().Be(ServiceOrderStatus.Pending);
        reloaded.Dispatches.Should().ContainSingle();
        reloaded.ActionLog.Should().ContainSingle();
    }

    [Fact]
    public async Task HandleAsync_NotGranted_LeavesOrderInDraftWithNoDispatch()
    {
        var repository = new SnapshottingServiceOrderRepository();
        var order = new ServiceOrder { Id = "order-2", OrderTypeId = "emergency-repair", CreatedBy = AgentId };
        await repository.AddAsync(order);

        var executor = new DispatchServiceOrderExecutor(repository, new ServiceOrderRules(), AgentIdentity(), TimeProvider.System);
        var message = new ServiceOrderCreated(order, AgentId, "crew-1", DispatchTargetType.Group, "run-1");

        var result = await executor.HandleAsync(message, NoOpWorkflowContext.Instance);

        result.Status.Should().Be(ServiceOrderStatus.Draft);
        (await repository.GetByIdAsync("order-2"))!.Dispatches.Should().BeEmpty();
    }
}

using FluentAssertions;
using GeoAssets.Workflow.Agents.Executors;
using GeoAssets.Workflow.Agents.Identity;
using GeoAssets.Workflow.Agents.Tests.TestDoubles;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.Workflow.Agents.Tests;

/// <summary>
/// XD01-332 — an agent principal goes through the same organization gate as a person: no
/// executor has a way around <see cref="ServiceOrderRules"/>. An agent creates orders under its
/// own configured organization and can then act on them; it cannot act on another
/// organization's order, whatever role its configuration gives it.
/// </summary>
[Collection("AgentObservability")]
public class AgentOrganizationGateTests
{
    private const string AgentId = "agent-1";
    private static readonly Guid OrgA = new("a0a0a0a0-0000-4000-8000-000000000332");
    private static readonly Guid OrgB = new("b0b0b0b0-0000-4000-8000-000000000332");

    private static ConfiguredAgentIdentityProvider Identity(Guid? organizationId, params string[] roles) =>
        new(Options.Create(new AgentIdentityOptions
        {
            Agents = { [AgentId] = new AgentIdentityDescriptor { OrganizationId = organizationId?.ToString(), RoleNames = [.. roles] } },
        }));

    private static OrderTypeRegistry Registry()
    {
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType { Id = "emergency-repair", DisplayName = "Emergency" });
        return registry;
    }

    private static DispatchServiceOrderExecutor Dispatcher(IServiceOrderRepository repository, ServiceOrderRules rules, IAgentIdentityProvider identity) =>
        new(repository, rules, identity, TimeProvider.System, TestObservability.Tracer, NullLogger<DispatchServiceOrderExecutor>.Instance);

    private static CreateServiceOrderExecutor Creator(IServiceOrderWriter writer, ServiceOrderRules rules, IAgentIdentityProvider identity) =>
        new(writer, rules, Registry(), identity, TimeProvider.System, TestObservability.Tracer, NullLogger<CreateServiceOrderExecutor>.Instance);

    private static async Task<(SnapshottingServiceOrderRepository Repository, ServiceOrder Order)> SeedAsync(Guid organizationId)
    {
        var repository = new SnapshottingServiceOrderRepository();
        var order = new ServiceOrder { Id = "order-1", OrderTypeId = "emergency-repair", CreatedBy = "a-person", OrganizationId = organizationId };
        await repository.AddAsync(order);
        return (repository, order);
    }

    [Fact]
    public async Task XD01_332_Create_StampsTheAgentsOwnOrganizationOnTheOrder()
    {
        var repository = new SnapshottingServiceOrderRepository();
        var executor = Creator(repository, new ServiceOrderRules(), Identity(OrgA));

        var created = await executor.HandleAsync(
            new CreateServiceOrderRequest(AgentId, "emergency-repair", "T", "crew-1", DispatchTargetType.Group), NoOpWorkflowContext.Instance);

        created.Order.OrganizationId.Should().Be(OrgA);
        (await repository.GetByIdAsync(created.Order.Id))!.OrganizationId.Should().Be(OrgA);
    }

    [Fact]
    public async Task XD01_332_CreateThenDispatch_AgentWithAnOrganization_PassesTheGateOnItsOwnOrder_EvenWhenOrdersWithoutOrganizationAreRefused()
    {
        var repository = new SnapshottingServiceOrderRepository();
        var rules = new ServiceOrderRules(allowOrdersWithoutOrganization: false);
        var identity = Identity(OrgA, "Administrator");

        var created = await Creator(repository, rules, identity).HandleAsync(
            new CreateServiceOrderRequest(AgentId, "emergency-repair", "T", "crew-1", DispatchTargetType.Group), NoOpWorkflowContext.Instance);
        var dispatched = await Dispatcher(repository, rules, identity).HandleAsync(created, NoOpWorkflowContext.Instance);

        dispatched.Status.Should().Be(ServiceOrderStatus.Pending);
    }

    [Fact]
    public async Task XD01_332_Dispatch_AdministratorAgentOfAnotherOrganization_IsDeniedByTheGate_AndTheOrderIsUntouched()
    {
        var (repository, order) = await SeedAsync(OrgA);
        var executor = Dispatcher(repository, new ServiceOrderRules(), Identity(OrgB, "Administrator"));

        var result = await executor.HandleAsync(
            new ServiceOrderCreated(order, AgentId, "crew-1", DispatchTargetType.Group, "run-1"), NoOpWorkflowContext.Instance);

        result.Status.Should().Be(ServiceOrderStatus.Draft);
        var stored = await repository.GetByIdAsync("order-1");
        stored!.Dispatches.Should().BeEmpty();
        stored.ActionLog.Should().BeEmpty();
    }

    [Fact]
    public async Task XD01_332_Dispatch_AdministratorAgentOfTheOrdersOrganization_IsAllowedAsBefore()
    {
        var (repository, order) = await SeedAsync(OrgA);
        var executor = Dispatcher(repository, new ServiceOrderRules(), Identity(OrgA, "Administrator"));

        var result = await executor.HandleAsync(
            new ServiceOrderCreated(order, AgentId, "crew-1", DispatchTargetType.Group, "run-1"), NoOpWorkflowContext.Instance);

        result.Status.Should().Be(ServiceOrderStatus.Pending);
    }

    [Theory]
    [InlineData(true, ServiceOrderStatus.Pending)]
    [InlineData(false, ServiceOrderStatus.Draft)]
    public async Task XD01_332_Dispatch_AgentWithNoOrganization_ReachesAnOrderWithNoOrganization_OnlyWhileTheOptionAllowsIt(
        bool allowOrdersWithoutOrganization, string expectedStatus)
    {
        var (repository, order) = await SeedAsync(Guid.Empty);
        var rules = new ServiceOrderRules(allowOrdersWithoutOrganization: allowOrdersWithoutOrganization);
        var executor = Dispatcher(repository, rules, Identity(organizationId: null, "Administrator"));

        var result = await executor.HandleAsync(
            new ServiceOrderCreated(order, AgentId, "crew-1", DispatchTargetType.Group, "run-1"), NoOpWorkflowContext.Instance);

        result.Status.Should().Be(expectedStatus);
    }

    [Fact]
    public void XD01_332_AgentPrincipal_CarriesItsConfiguredOrganization_AndNoWayAroundTheGate()
    {
        var principal = Identity(OrgB, "Administrator").Resolve(AgentId);

        principal.Kind.Should().Be(ActorKind.Agent);
        principal.OrganizationId.Should().Be(OrgB.ToString());
        new ServiceOrderRules().Evaluate(principal, OrderActionType.View, new ServiceOrder { OrganizationId = OrgA })
            .Code.Should().Be(RuleDenialCode.OutsideOrganization);
    }
}

using FluentAssertions;
using GeoAssets.Workflow.Agents.Executors;
using GeoAssets.Workflow.Agents.Identity;
using GeoAssets.Workflow.Agents.Tests.TestDoubles;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.Workflow.Agents.Tests;

[Collection("AgentObservability")]
public class CreateServiceOrderExecutorTests
{
    private const string AgentId = "agent-1";

    private static IAgentIdentityProvider AgentIdentity(params string[] roles) =>
        new ConfiguredAgentIdentityProvider(Options.Create(new AgentIdentityOptions
        {
            Agents = { [AgentId] = new AgentIdentityDescriptor { RoleNames = [.. roles] } }
        }));

    private static CreateServiceOrderExecutor Executor(
        IServiceOrderWriter writer, ServiceOrderRules rules, OrderTypeRegistry registry,
        IAgentIdentityProvider identity, ILogger<CreateServiceOrderExecutor>? logger = null) =>
        new(writer, rules, registry, identity, TestObservability.Tracer,
            logger ?? NullLogger<CreateServiceOrderExecutor>.Instance);

    [Fact]
    public async Task HandleAsync_AgentLacksCreationGrant_ThrowsAndPersistsNothing()
    {
        var writer = new SnapshottingServiceOrderRepository();
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType
        {
            Id               = "emergency-repair",
            DisplayName      = "Emergency",
            CreationPolicies = [new(PolicyKind.Role, "Supervisor")], // agent only has AutomationAgent
        });
        var executor = Executor(writer, new ServiceOrderRules(), registry, AgentIdentity("AutomationAgent"));
        var request  = new CreateServiceOrderRequest(AgentId, "emergency-repair", "T", "crew-1", DispatchTargetType.Group);

        var act = async () => await executor.HandleAsync(request, NoOpWorkflowContext.Instance);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{AgentId}*emergency-repair*");
        (await writer.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_UnregisteredOrderType_ThrowsKeyNotFoundException()
    {
        var writer   = new SnapshottingServiceOrderRepository();
        var executor = Executor(writer, new ServiceOrderRules(), new OrderTypeRegistry(), AgentIdentity("AutomationAgent"));
        var request  = new CreateServiceOrderRequest(AgentId, "widget-repair", "T", "crew-1", DispatchTargetType.Group);

        var act = async () => await executor.HandleAsync(request, NoOpWorkflowContext.Instance);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        (await writer.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_UnregisteredAgent_ThrowsKeyNotFoundException()
    {
        var writer   = new SnapshottingServiceOrderRepository();
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType { Id = "emergency-repair", DisplayName = "Emergency" });
        var executor = Executor(writer, new ServiceOrderRules(), registry, AgentIdentity()); // no agents registered
        var request  = new CreateServiceOrderRequest("ghost-agent", "emergency-repair", "T", "crew-1", DispatchTargetType.Group);

        var act = async () => await executor.HandleAsync(request, NoOpWorkflowContext.Instance);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_Authorized_PersistsOrderWithAgentAsCreator()
    {
        var writer   = new SnapshottingServiceOrderRepository();
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType
        {
            Id               = "emergency-repair",
            DisplayName      = "Emergency",
            CreationPolicies = [new(PolicyKind.Role, "AutomationAgent")],
        });
        var executor = Executor(writer, new ServiceOrderRules(), registry, AgentIdentity("AutomationAgent"));
        var request  = new CreateServiceOrderRequest(AgentId, "emergency-repair", "Valve failure", "crew-1", DispatchTargetType.Group, "run-1");

        var result = await executor.HandleAsync(request, NoOpWorkflowContext.Instance);

        result.Order.CreatedBy.Should().Be(AgentId);
        result.Order.Status.Should().Be(ServiceOrderStatus.Draft);
        result.AgentInvocationId.Should().Be("run-1");
        (await writer.GetByIdAsync(result.Order.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAsync_OrderTypeDefinesInitialStateKey_NewOrderStartsThere()
    {
        var writer   = new SnapshottingServiceOrderRepository();
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType
        {
            Id               = "custom-intake",
            DisplayName      = "Custom Intake",
            CreationPolicies = [new(PolicyKind.Role, "AutomationAgent")],
            States           = [new("Intake", "Intake")],
            InitialStateKey  = "Intake",
        });
        var executor = Executor(writer, new ServiceOrderRules(), registry, AgentIdentity("AutomationAgent"));
        var request  = new CreateServiceOrderRequest(AgentId, "custom-intake", "T", "crew-1", DispatchTargetType.Group);

        var result = await executor.HandleAsync(request, NoOpWorkflowContext.Instance);

        result.Order.Status.Should().Be("Intake");
    }

    // ── Instrumentation (XD01-42) ─────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Authorized_RecordsSpanWithExpectedTags()
    {
        var writer   = new SnapshottingServiceOrderRepository();
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType
        {
            Id               = "emergency-repair",
            DisplayName      = "Emergency",
            CreationPolicies = [new(PolicyKind.Role, "AutomationAgent")],
        });
        var executor = Executor(writer, new ServiceOrderRules(), registry, AgentIdentity("AutomationAgent"));
        var request  = new CreateServiceOrderRequest(AgentId, "emergency-repair", "Valve failure", "crew-1", DispatchTargetType.Group, "run-1");
        using var activities = new ActivityCapture();

        var result = await executor.HandleAsync(request, NoOpWorkflowContext.Instance);

        activities.Activities.Should().ContainSingle();
        var span = activities.Activities[0];
        span.OperationName.Should().Be("Agent.Create");
        span.GetTagItem("order.id").Should().Be(result.Order.Id);
        span.GetTagItem("agent.id").Should().Be(AgentId);
        span.GetTagItem("agent.invocation_id").Should().Be("run-1");
        span.GetTagItem("decision.allowed").Should().Be(true);
    }

    [Fact]
    public async Task HandleAsync_AgentLacksCreationGrant_LogsWarningAndRecordsExceptionOnSpan()
    {
        var writer = new SnapshottingServiceOrderRepository();
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType
        {
            Id               = "emergency-repair",
            DisplayName      = "Emergency",
            CreationPolicies = [new(PolicyKind.Role, "Supervisor")],
        });
        var logger   = new CapturingLogger<CreateServiceOrderExecutor>();
        var executor = Executor(writer, new ServiceOrderRules(), registry, AgentIdentity("AutomationAgent"), logger);
        var request  = new CreateServiceOrderRequest(AgentId, "emergency-repair", "T", "crew-1", DispatchTargetType.Group);
        using var activities = new ActivityCapture();

        var act = async () => await executor.HandleAsync(request, NoOpWorkflowContext.Instance);
        await act.Should().ThrowAsync<InvalidOperationException>();

        logger.Entries.Should().ContainSingle();
        logger.Entries[0].Level.Should().Be(LogLevel.Warning);
        logger.Entries[0].Message.Should().Contain(AgentId).And.Contain("emergency-repair");

        activities.Activities.Should().ContainSingle();
        activities.Activities[0].Status.Should().Be(System.Diagnostics.ActivityStatusCode.Error);
        activities.Activities[0].GetTagItem("decision.allowed").Should().Be(false);
    }
}

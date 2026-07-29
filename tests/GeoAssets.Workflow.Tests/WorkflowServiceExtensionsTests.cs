using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Workflow.Tests;

public class WorkflowServiceExtensionsTests
{
    [Fact]
    public void AddWorkflowInMemory_ResolvesIServiceOrderRepositoryAsValidatingDecorator()
    {
        var services = new ServiceCollection();
        services.AddWorkflowInMemory();
        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IServiceOrderRepository>()
            .Should().BeOfType<ValidatingServiceOrderRepository>();
    }

    [Fact]
    public void AddWorkflowInMemory_IServiceOrderReaderAndWriterResolveToSameUnderlyingRepository()
    {
        var services = new ServiceCollection();
        services.AddWorkflowInMemory();
        using var sp = services.BuildServiceProvider();

        var repository = sp.GetRequiredService<IServiceOrderRepository>();
        var reader     = sp.GetRequiredService<IServiceOrderReader>();
        var writer     = sp.GetRequiredService<IServiceOrderWriter>();

        reader.Should().BeSameAs(repository);
        writer.Should().BeSameAs(repository);
    }

    [Fact]
    public async Task AddWorkflowInMemory_IServiceOrderReader_SeesWritesMadeThroughIServiceOrderWriter()
    {
        var services = new ServiceCollection();
        services.AddWorkflowInMemory();
        using var sp = services.BuildServiceProvider();

        var writer = sp.GetRequiredService<IServiceOrderWriter>();
        var reader = sp.GetRequiredService<IServiceOrderReader>();

        await writer.AddAsync(new ServiceOrder { Id = "a", Title = "Via writer-only dependency" });

        (await reader.GetByIdAsync("a"))!.Title.Should().Be("Via writer-only dependency");
    }

    // ── AddServiceOrderRules ─────────────────────────────────────────────────────

    [Fact]
    public void AddServiceOrderRules_ResolvesAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddServiceOrderRules();
        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<ServiceOrderRules>()
            .Should().BeSameAs(sp.GetRequiredService<ServiceOrderRules>());
    }

    [Fact]
    public void AddServiceOrderRules_UsesOrderTypeRegistryRegisteredByAddOrderTypeRegistry()
    {
        var services = new ServiceCollection();
        services.AddOrderTypeRegistry(r => r.Register(new OrderType
        {
            Id          = "custom",
            DisplayName = "Custom",
            ActionPermissions = [new(OrderActionType.Approve, PolicyKind.Role, "CustomApprover")],
        }));
        services.AddServiceOrderRules();
        using var sp = services.BuildServiceProvider();

        var rules = sp.GetRequiredService<ServiceOrderRules>();
        var order = new ServiceOrder { OrderTypeId = "custom" };
        var principal = new WorkflowPrincipal("u1", null, ["CustomApprover"], [], []);

        rules.Evaluate(principal, OrderActionType.Approve, order).Allowed.Should().BeTrue();
    }

    [Fact]
    public void AddServiceOrderRules_RoleGrantsOptionGrantsAnAgentRoleTheConfiguredActions()
    {
        var services = new ServiceCollection();
        services.AddServiceOrderRules(o =>
        {
            o.RoleGrants["AutomationAgent"] = new HashSet<OrderActionType> { OrderActionType.Dispatch };
        });
        using var sp = services.BuildServiceProvider();

        var rules = sp.GetRequiredService<ServiceOrderRules>();
        var order = new ServiceOrder();
        var agentPrincipal = new WorkflowPrincipal(
            "agent-01", null, ["AutomationAgent"], [], [])
        {
            Kind = ActorKind.Agent
        };

        rules.Evaluate(agentPrincipal, OrderActionType.Dispatch, order).Allowed.Should().BeTrue();
        rules.Evaluate(agentPrincipal, OrderActionType.Approve, order).Allowed.Should().BeFalse();
    }
}

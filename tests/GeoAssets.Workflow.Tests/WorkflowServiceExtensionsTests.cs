using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Workflow.Tests;

public class WorkflowServiceExtensionsTests
{
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

    [Fact]
    public void AddServiceOrderRules_RecipientRoleGrantsOptionGrantsDispatchRecipientTheConfiguredAction()
    {
        var services = new ServiceCollection();
        services.AddServiceOrderRules(o =>
        {
            o.RecipientRoleGrants["FieldTechnician"] = new HashSet<OrderActionType> { OrderActionType.Assign };
        });
        using var sp = services.BuildServiceProvider();

        var rules = sp.GetRequiredService<ServiceOrderRules>();
        var order = new ServiceOrder().DispatchTo("org-1", DispatchTargetType.Organization, "supervisor-1", TimeProvider.System);
        var principal = new WorkflowPrincipal("u1", "org-1", ["FieldTechnician"], [], []);

        rules.Evaluate(principal, OrderActionType.Assign, order).Allowed.Should().BeTrue();
        // Same role, but this order was never dispatched to org-1 — must not leak.
        rules.Evaluate(principal, OrderActionType.Assign, new ServiceOrder()).Allowed.Should().BeFalse();
    }
}

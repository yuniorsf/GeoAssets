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
            o.RoleGrants["AutomationAgent"] = new HashSet<OrderAction> { OrderActionType.Dispatch };
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
            o.RecipientRoleGrants["FieldTechnician"] = new HashSet<OrderAction> { OrderActionType.Assign };
        });
        using var sp = services.BuildServiceProvider();

        var rules = sp.GetRequiredService<ServiceOrderRules>();
        var order = new ServiceOrder().DispatchTo("org-1", DispatchTargetType.Organization, "supervisor-1", TimeProvider.System);
        var principal = new WorkflowPrincipal("u1", "org-1", ["FieldTechnician"], [], []);

        rules.Evaluate(principal, OrderActionType.Assign, order).Allowed.Should().BeTrue();
        // Same role, but this order was never dispatched to org-1 — must not leak.
        rules.Evaluate(principal, OrderActionType.Assign, new ServiceOrder()).Allowed.Should().BeFalse();
    }

    // ── AddServiceOrderRules: DI-registered rules (XD01-363) ─────────────────────

    private static readonly WorkflowPrincipal Approver = new("u1", null, ["Approver"], [], []);

    private sealed class FixedRule(string name, bool? verdict, List<string>? log = null) : IServiceOrderRule
    {
        public string Name => name;

        public bool? Evaluate(OrderAction action, RuleEvaluationContext context)
        {
            log?.Add(name);
            return verdict;
        }
    }

    private sealed class FixedCreationRule(string name, bool? verdict, List<string>? log = null) : IOrderCreationRule
    {
        public string Name => name;

        public bool? Evaluate(WorkflowPrincipal principal, OrderType orderType)
        {
            log?.Add(name);
            return verdict;
        }
    }

    private static OrderType RestrictedCreationType() => new()
    {
        Id          = "restricted",
        DisplayName = "Restricted",
        CreationPolicies = [new(PolicyKind.Role, "SomeoneElse")],
    };

    [Fact]
    public void AddServiceOrderRules_DiRegisteredDenyRule_BlocksAnActionTheDefaultsAllow()
    {
        var order = new ServiceOrder { OrderTypeId = "custom" };
        var registry = (OrderTypeRegistry r) => r.Register(new OrderType
        {
            Id                = "custom",
            DisplayName       = "Custom",
            ActionPermissions = [new(OrderActionType.Approve, PolicyKind.Role, "Approver")],
        });

        var without = new ServiceCollection();
        without.AddOrderTypeRegistry(registry);
        without.AddServiceOrderRules();
        using var spWithout = without.BuildServiceProvider();
        spWithout.GetRequiredService<ServiceOrderRules>()
            .Evaluate(Approver, OrderActionType.Approve, order).Allowed.Should().BeTrue();

        var with = new ServiceCollection();
        with.AddOrderTypeRegistry(registry);
        with.AddSingleton<IServiceOrderRule>(new FixedRule("module-deny", false));
        with.AddServiceOrderRules();
        using var spWith = with.BuildServiceProvider();

        spWith.GetRequiredService<ServiceOrderRules>()
            .Evaluate(Approver, OrderActionType.Approve, order).Allowed.Should().BeFalse();
    }

    [Fact]
    public void AddServiceOrderRules_DiRegisteredCreationRule_GrantsCreation()
    {
        var principal = new WorkflowPrincipal("u1", null, ["Nobody"], [], []);
        var orderType = RestrictedCreationType();

        var without = new ServiceCollection();
        without.AddServiceOrderRules();
        using var spWithout = without.BuildServiceProvider();
        spWithout.GetRequiredService<ServiceOrderRules>().CanCreate(principal, orderType).Should().BeFalse();

        var with = new ServiceCollection();
        with.AddSingleton<IOrderCreationRule>(new FixedCreationRule("module-grant", true));
        with.AddServiceOrderRules();
        using var spWith = with.BuildServiceProvider();

        spWith.GetRequiredService<ServiceOrderRules>().CanCreate(principal, orderType).Should().BeTrue();
    }

    [Fact]
    public void AddServiceOrderRules_DiRules_AreAppendedInRegistrationOrder()
    {
        var actionLog = new List<string>();
        var creationLog = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IServiceOrderRule>(new FixedRule("first", null, actionLog));
        services.AddSingleton<IServiceOrderRule>(new FixedRule("second", null, actionLog));
        services.AddSingleton<IServiceOrderRule>(new FixedRule("third", null, actionLog));
        services.AddSingleton<IOrderCreationRule>(new FixedCreationRule("c-first", null, creationLog));
        services.AddSingleton<IOrderCreationRule>(new FixedCreationRule("c-second", null, creationLog));
        services.AddServiceOrderRules();
        using var sp = services.BuildServiceProvider();
        var rules = sp.GetRequiredService<ServiceOrderRules>();

        rules.Evaluate(Approver, OrderActionType.Approve, new ServiceOrder());
        rules.CanCreate(Approver, RestrictedCreationType());

        actionLog.Should().Equal("first", "second", "third");
        creationLog.Should().Equal("c-first", "c-second");
    }

    [Fact]
    public void AddServiceOrderRules_NoDiRules_BehavesAsBefore()
    {
        var order = new ServiceOrder { OrderTypeId = "custom" };
        var services = new ServiceCollection();
        services.AddOrderTypeRegistry(r => r.Register(new OrderType
        {
            Id                = "custom",
            DisplayName       = "Custom",
            ActionPermissions = [new(OrderActionType.Approve, PolicyKind.Role, "Approver")],
        }));
        services.AddServiceOrderRules();
        using var sp = services.BuildServiceProvider();
        var rules = sp.GetRequiredService<ServiceOrderRules>();

        rules.Evaluate(Approver, OrderActionType.Approve, order).Allowed.Should().BeTrue();
        rules.Evaluate(new WorkflowPrincipal("u2", null, ["Other"], [], []), OrderActionType.Approve, order).Allowed.Should().BeFalse();
        rules.CanCreate(Approver, new OrderType { Id = "open", DisplayName = "Open" }).Should().BeTrue();
        rules.CanCreate(Approver, RestrictedCreationType()).Should().BeFalse();
    }
}

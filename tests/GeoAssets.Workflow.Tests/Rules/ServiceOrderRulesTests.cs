using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Xunit;

namespace GeoAssets.Workflow.Tests.Rules;

public class ServiceOrderRulesTests
{
    private static WorkflowPrincipal Principal(
        string userId = "u1",
        string? orgId = null,
        string[]? roles = null,
        string[]? groups = null,
        string[]? permissions = null)
        => new(userId, orgId, roles ?? [], groups ?? [], permissions ?? []);

    private static ServiceOrder Order(
        string createdBy = "",
        string? assignedTo = null,
        string orderTypeId = "",
        ServiceOrderStatus status = ServiceOrderStatus.Draft)
        => new() { CreatedBy = createdBy, AssignedTo = assignedTo, OrderTypeId = orderTypeId, Status = status };

    private sealed class DelegateCreationRule(Func<WorkflowPrincipal, OrderType, bool?> fn) : IOrderCreationRule
    {
        public string Name => "DelegateCreationRule";
        public bool? Evaluate(WorkflowPrincipal principal, OrderType orderType) => fn(principal, orderType);
    }

    private sealed class DelegateRule(Func<OrderActionType, RuleEvaluationContext, bool?> fn) : IServiceOrderRule
    {
        public string Name => "DelegateRule";
        public bool? Evaluate(OrderActionType action, RuleEvaluationContext ctx) => fn(action, ctx);
    }

    // ── CanCreate ──────────────────────────────────────────────────────────────

    [Fact]
    public void CanCreate_AnonymousPrincipal_ReturnsFalse()
    {
        var rules = new ServiceOrderRules();
        var orderType = new OrderType { Id = "t", DisplayName = "T" };

        rules.CanCreate(WorkflowPrincipal.Anonymous, orderType).Should().BeFalse();
    }

    [Fact]
    public void CanCreate_NoPolicies_ReturnsTrue()
    {
        var rules = new ServiceOrderRules();
        var orderType = new OrderType { Id = "t", DisplayName = "T" };

        rules.CanCreate(Principal(), orderType).Should().BeTrue();
    }

    [Fact]
    public void CanCreate_RolePolicySatisfied_ReturnsTrue()
    {
        var rules = new ServiceOrderRules();
        var orderType = new OrderType
        {
            Id = "t", DisplayName = "T",
            CreationPolicies = [new(PolicyKind.Role, "FieldTechnician")],
        };

        rules.CanCreate(Principal(roles: ["FieldTechnician"]), orderType).Should().BeTrue();
    }

    [Fact]
    public void CanCreate_GroupPolicySatisfied_ReturnsTrue()
    {
        var rules = new ServiceOrderRules();
        var orderType = new OrderType
        {
            Id = "t", DisplayName = "T",
            CreationPolicies = [new(PolicyKind.Group, "field-ops")],
        };

        rules.CanCreate(Principal(groups: ["field-ops"]), orderType).Should().BeTrue();
    }

    [Fact]
    public void CanCreate_OrganizationPolicySatisfied_ReturnsTrue()
    {
        var rules = new ServiceOrderRules();
        var orderType = new OrderType
        {
            Id = "t", DisplayName = "T",
            CreationPolicies = [new(PolicyKind.Organization, "org-1")],
        };

        rules.CanCreate(Principal(orgId: "org-1"), orderType).Should().BeTrue();
    }

    [Fact]
    public void CanCreate_UnknownPolicyKind_ReturnsFalse()
    {
        var rules = new ServiceOrderRules();
        var orderType = new OrderType
        {
            Id = "t", DisplayName = "T",
            CreationPolicies = [new((PolicyKind)99, "whatever")],
        };

        rules.CanCreate(Principal(), orderType).Should().BeFalse();
    }

    [Fact]
    public void CanCreate_PolicyNotSatisfied_ReturnsFalse()
    {
        var rules = new ServiceOrderRules();
        var orderType = new OrderType
        {
            Id = "t", DisplayName = "T",
            CreationPolicies = [new(PolicyKind.Role, "Supervisor")],
        };

        rules.CanCreate(Principal(), orderType).Should().BeFalse();
    }

    [Fact]
    public void CanCreate_CustomCreationRuleGrants_OverridesUnsatisfiedPolicy()
    {
        var orderType = new OrderType
        {
            Id = "t", DisplayName = "T",
            CreationPolicies = [new(PolicyKind.Role, "Supervisor")],
        };
        var rules = new ServiceOrderRules()
            .AddCreationRule(new DelegateCreationRule((_, _) => true));

        rules.CanCreate(Principal(), orderType).Should().BeTrue();
    }

    [Fact]
    public void CanCreate_CustomCreationRuleDenies_OverridesSatisfiedPolicy()
    {
        var orderType = new OrderType
        {
            Id = "t", DisplayName = "T",
            CreationPolicies = [new(PolicyKind.Role, "FieldTechnician")],
        };
        var rules = new ServiceOrderRules()
            .AddCreationRule(new DelegateCreationRule((_, _) => false));

        rules.CanCreate(Principal(roles: ["FieldTechnician"]), orderType).Should().BeFalse();
    }

    [Fact]
    public void AddCreationRule_ReturnsSameInstanceForChaining()
    {
        var rules = new ServiceOrderRules();

        rules.AddCreationRule(new DelegateCreationRule((_, _) => null)).Should().BeSameAs(rules);
    }

    // ── Evaluate / CanView core mechanics ──────────────────────────────────────

    [Fact]
    public void Evaluate_AnonymousPrincipal_Denies()
    {
        var rules = new ServiceOrderRules();

        rules.Evaluate(WorkflowPrincipal.Anonymous, OrderActionType.View, Order()).Allowed.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_NoRuleGrants_Denies()
    {
        var rules = new ServiceOrderRules();

        rules.Evaluate(Principal(), OrderActionType.Complete, Order()).Allowed.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_CustomRuleGrants_Allows()
    {
        var rules = new ServiceOrderRules()
            .AddRule(new DelegateRule((_, _) => true));

        rules.Evaluate(Principal(), OrderActionType.Complete, Order()).Allowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_CustomRuleDenies_OverridesOtherAllows()
    {
        var rules = new ServiceOrderRules()
            .AddRule(new DelegateRule((_, _) => false));

        rules.Evaluate(Principal(roles: ["Administrator"]), OrderActionType.Complete, Order())
            .Allowed.Should().BeFalse();
    }

    [Fact]
    public void AddRule_ReturnsSameInstanceForChaining()
    {
        var rules = new ServiceOrderRules();

        rules.AddRule(new DelegateRule((_, _) => null)).Should().BeSameAs(rules);
    }

    [Fact]
    public void CanView_Allowed_ReturnsTrue()
    {
        var rules = new ServiceOrderRules();
        var order = Order(createdBy: "u1");

        rules.CanView(Principal(), order).Should().BeTrue();
    }

    [Fact]
    public void CanView_Denied_ReturnsFalse()
    {
        var rules = new ServiceOrderRules();

        rules.CanView(Principal(), Order()).Should().BeFalse();
    }

    // ── CreatorRule ────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_Creator_CanViewAndAnnotate()
    {
        var rules = new ServiceOrderRules();
        var order = Order(createdBy: "u1");

        rules.Evaluate(Principal(), OrderActionType.View, order).Allowed.Should().BeTrue();
        rules.Evaluate(Principal(), OrderActionType.Annotate, order).Allowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Creator_CanCancelWhileDraft()
    {
        var rules = new ServiceOrderRules();
        var order = Order(createdBy: "u1", status: ServiceOrderStatus.Draft);

        rules.Evaluate(Principal(), OrderActionType.Cancel, order).Allowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Creator_CannotCancelWhileInProgress()
    {
        var rules = new ServiceOrderRules();
        var order = Order(createdBy: "u1", status: ServiceOrderStatus.InProgress);

        rules.Evaluate(Principal(), OrderActionType.Cancel, order).Allowed.Should().BeFalse();
    }

    // ── AssigneeRule ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrderActionType.View)]
    [InlineData(OrderActionType.Execute)]
    [InlineData(OrderActionType.Complete)]
    [InlineData(OrderActionType.Annotate)]
    public void Evaluate_Assignee_CanPerformGrantedActions(OrderActionType action)
    {
        var rules = new ServiceOrderRules();
        var order = Order(assignedTo: "u1");

        rules.Evaluate(Principal(), action, order).Allowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Assignee_CannotApprove()
    {
        var rules = new ServiceOrderRules();
        var order = Order(assignedTo: "u1");

        rules.Evaluate(Principal(), OrderActionType.Approve, order).Allowed.Should().BeFalse();
    }

    // ── DispatchRecipientRule ──────────────────────────────────────────────────

    [Fact]
    public void Evaluate_DirectDispatchRecipient_CanView()
    {
        var rules = new ServiceOrderRules();
        var order = Order().DispatchTo("u1", DispatchTargetType.User, "supervisor-1");

        rules.Evaluate(Principal(), OrderActionType.View, order).Allowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_GroupDispatchMember_CanAnnotate()
    {
        var rules = new ServiceOrderRules();
        var order = Order().DispatchTo("crew-1", DispatchTargetType.Group, "supervisor-1");

        rules.Evaluate(Principal(groups: ["crew-1"]), OrderActionType.Annotate, order).Allowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_OrgDispatchMember_CanView()
    {
        var rules = new ServiceOrderRules();
        var order = Order().DispatchTo("org-1", DispatchTargetType.Organization, "supervisor-1");

        rules.Evaluate(Principal(orgId: "org-1"), OrderActionType.View, order).Allowed.Should().BeTrue();
    }

    // ── RoleBasedActionRule ────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_Administrator_CanPerformAnyAction()
    {
        var rules = new ServiceOrderRules();

        rules.Evaluate(Principal(roles: ["Administrator"]), OrderActionType.Complete, Order())
            .Allowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(OrderActionType.Approve)]
    [InlineData(OrderActionType.Reject)]
    [InlineData(OrderActionType.Assign)]
    [InlineData(OrderActionType.Dispatch)]
    [InlineData(OrderActionType.Cancel)]
    [InlineData(OrderActionType.Annotate)]
    public void Evaluate_Supervisor_CanPerformGrantedActions(OrderActionType action)
    {
        var rules = new ServiceOrderRules();

        rules.Evaluate(Principal(roles: ["Supervisor"]), action, Order()).Allowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Supervisor_CannotExecute()
    {
        var rules = new ServiceOrderRules();

        rules.Evaluate(Principal(roles: ["Supervisor"]), OrderActionType.Execute, Order())
            .Allowed.Should().BeFalse();
    }

    // ── OrderTypeActionPermissionRule ──────────────────────────────────────────

    [Fact]
    public void Evaluate_NoRegistrySupplied_FallsBackToDefaults()
    {
        var rules = new ServiceOrderRules(orderTypeRegistry: null);
        var order = Order(orderTypeId: "emergency-repair");

        rules.Evaluate(Principal(roles: ["Supervisor"]), OrderActionType.Approve, order)
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_OrderTypeNotRegistered_FallsBackToDefaults()
    {
        var registry = new OrderTypeRegistry();
        var rules = new ServiceOrderRules(registry);
        var order = Order(orderTypeId: "unregistered-type");

        rules.Evaluate(Principal(roles: ["Supervisor"]), OrderActionType.Approve, order)
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_NoActionPermissionsForAction_FallsBackToDefaults()
    {
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType { Id = "emergency-repair", DisplayName = "Emergency" });
        var rules = new ServiceOrderRules(registry);
        var order = Order(orderTypeId: "emergency-repair");

        rules.Evaluate(Principal(roles: ["Supervisor"]), OrderActionType.Approve, order)
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_ActionPermissionSatisfied_Allows()
    {
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType
        {
            Id = "emergency-repair", DisplayName = "Emergency",
            ActionPermissions = [new(OrderActionType.Approve, PolicyKind.Permission, "emergency:approve")],
        });
        var rules = new ServiceOrderRules(registry);
        var order = Order(orderTypeId: "emergency-repair");

        rules.Evaluate(Principal(permissions: ["emergency:approve"]), OrderActionType.Approve, order)
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_ActionPermissionNotSatisfied_OverridesRoleBasedAllow()
    {
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType
        {
            Id = "emergency-repair", DisplayName = "Emergency",
            ActionPermissions = [new(OrderActionType.Approve, PolicyKind.Permission, "emergency:approve")],
        });
        var rules = new ServiceOrderRules(registry);
        var order = Order(orderTypeId: "emergency-repair");

        // Supervisor role alone would normally grant Approve (RoleBasedActionRule) —
        // the unsatisfied ActionPermissions entry must override that to a deny.
        rules.Evaluate(Principal(roles: ["Supervisor"]), OrderActionType.Approve, order)
            .Allowed.Should().BeFalse();
    }

    // ── RoleBasedActionRule configurability ─────────────────────────────────────

    [Fact]
    public void Evaluate_NarrowedSupervisorGrants_NoLongerAllowsRemovedAction()
    {
        var narrowedGrants = new Dictionary<string, IReadOnlySet<OrderActionType>>
        {
            ["Supervisor"] = new HashSet<OrderActionType>
            {
                OrderActionType.View, OrderActionType.Approve, OrderActionType.Assign,
                OrderActionType.Dispatch, OrderActionType.Cancel, OrderActionType.Annotate,
            },
        };
        var rules = new ServiceOrderRules(roleGrants: narrowedGrants);

        rules.Evaluate(Principal(roles: ["Supervisor"]), OrderActionType.Reject, Order())
            .Allowed.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_NarrowedSupervisorGrants_StillAllowsRetainedAction()
    {
        var narrowedGrants = new Dictionary<string, IReadOnlySet<OrderActionType>>
        {
            ["Supervisor"] = new HashSet<OrderActionType> { OrderActionType.Approve },
        };
        var rules = new ServiceOrderRules(roleGrants: narrowedGrants);

        rules.Evaluate(Principal(roles: ["Supervisor"]), OrderActionType.Approve, Order())
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_CustomRoleTier_GrantsItsConfiguredActions()
    {
        var customGrants = new Dictionary<string, IReadOnlySet<OrderActionType>>
        {
            ["RegionalManager"] = new HashSet<OrderActionType> { OrderActionType.Approve },
        };
        var rules = new ServiceOrderRules(roleGrants: customGrants);

        rules.Evaluate(Principal(roles: ["RegionalManager"]), OrderActionType.Approve, Order())
            .Allowed.Should().BeTrue();
        rules.Evaluate(Principal(roles: ["RegionalManager"]), OrderActionType.Reject, Order())
            .Allowed.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_CustomUnrestrictedRoles_GrantsAnyAction()
    {
        var rules = new ServiceOrderRules(unrestrictedRoles: new HashSet<string> { "SuperAdmin" });

        rules.Evaluate(Principal(roles: ["SuperAdmin"]), OrderActionType.Complete, Order())
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_DefaultAdministrator_NoLongerUnrestrictedWhenOverridden()
    {
        var rules = new ServiceOrderRules(unrestrictedRoles: new HashSet<string> { "SuperAdmin" });

        rules.Evaluate(Principal(roles: ["Administrator"]), OrderActionType.Complete, Order())
            .Allowed.Should().BeFalse();
    }

    // ── ResolveRelationship ────────────────────────────────────────────────────

    [Fact]
    public void ResolveRelationship_NoRelationship_ReturnsNone()
    {
        ServiceOrderRules.ResolveRelationship(Principal(), Order())
            .Should().Be(OrderUserRelationship.None);
    }

    [Fact]
    public void ResolveRelationship_Creator_SetsCreatorFlag()
    {
        var rel = ServiceOrderRules.ResolveRelationship(Principal(), Order(createdBy: "u1"));
        rel.Should().HaveFlag(OrderUserRelationship.Creator);
    }

    [Fact]
    public void ResolveRelationship_Assignee_SetsAssigneeFlag()
    {
        var rel = ServiceOrderRules.ResolveRelationship(Principal(), Order(assignedTo: "u1"));
        rel.Should().HaveFlag(OrderUserRelationship.Assignee);
    }

    [Fact]
    public void ResolveRelationship_UserDispatchTarget_SetsDirectRecipientFlag()
    {
        var order = Order().DispatchTo("u1", DispatchTargetType.User, "supervisor-1");
        var rel = ServiceOrderRules.ResolveRelationship(Principal(), order);
        rel.Should().HaveFlag(OrderUserRelationship.DirectRecipient);
    }

    [Fact]
    public void ResolveRelationship_GroupMember_SetsGroupMemberFlag()
    {
        var order = Order().DispatchTo("crew-1", DispatchTargetType.Group, "supervisor-1");
        var rel = ServiceOrderRules.ResolveRelationship(Principal(groups: ["crew-1"]), order);
        rel.Should().HaveFlag(OrderUserRelationship.GroupMember);
    }

    [Fact]
    public void ResolveRelationship_GroupDispatchButNotMember_DoesNotSetGroupMemberFlag()
    {
        var order = Order().DispatchTo("crew-1", DispatchTargetType.Group, "supervisor-1");
        var rel = ServiceOrderRules.ResolveRelationship(Principal(groups: ["other-crew"]), order);
        rel.Should().NotHaveFlag(OrderUserRelationship.GroupMember);
    }

    [Fact]
    public void ResolveRelationship_OrgMember_SetsOrgMemberFlag()
    {
        var order = Order().DispatchTo("org-1", DispatchTargetType.Organization, "supervisor-1");
        var rel = ServiceOrderRules.ResolveRelationship(Principal(orgId: "org-1"), order);
        rel.Should().HaveFlag(OrderUserRelationship.OrgMember);
    }

    [Fact]
    public void ResolveRelationship_OrgDispatchButNotMember_DoesNotSetOrgMemberFlag()
    {
        var order = Order().DispatchTo("org-1", DispatchTargetType.Organization, "supervisor-1");
        var rel = ServiceOrderRules.ResolveRelationship(Principal(orgId: "org-2"), order);
        rel.Should().NotHaveFlag(OrderUserRelationship.OrgMember);
    }

    [Fact]
    public void ResolveRelationship_Dispatcher_SetsDispatcherFlag()
    {
        var order = Order().DispatchTo("someone-else", DispatchTargetType.User, "u1");
        var rel = ServiceOrderRules.ResolveRelationship(Principal(), order);
        rel.Should().HaveFlag(OrderUserRelationship.Dispatcher);
    }

    [Fact]
    public void ResolveRelationship_MultipleRelationships_CombinesFlags()
    {
        var order = Order(createdBy: "u1", assignedTo: "u1");
        var rel = ServiceOrderRules.ResolveRelationship(Principal(), order);
        rel.Should().HaveFlag(OrderUserRelationship.Creator);
        rel.Should().HaveFlag(OrderUserRelationship.Assignee);
    }
}

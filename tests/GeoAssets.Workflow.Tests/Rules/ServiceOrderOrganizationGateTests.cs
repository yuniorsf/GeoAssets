using FluentAssertions;
using GeoAssets.Workflow.Isp.NeutralOperator;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests.Rules;

/// <summary>
/// XD01-332 — the mandatory organization gate of <see cref="ServiceOrderRules"/>.
///
/// The matrix is principal relation × every <see cref="OrderActionType"/> × representative
/// roles × how the principal relates to the order as a user (stranger, creator, assignee). It
/// proves two things: the gate refuses exactly the relations that are outside the order's
/// organization with no grant and no dispatch, and for every relation that passes, the outcome
/// is the one the rule chain alone gives — what <see cref="ServiceOrderRules.Evaluate"/>
/// returned before the gate existed.
/// </summary>
public class ServiceOrderOrganizationGateTests
{
    private const string UserId = "user-1";
    private const string GroupId = "crew-7";
    private const string Scope = "municipio-123";

    private static readonly Guid OrgA = new("a0a0a0a0-0000-4000-8000-000000000332");
    private static readonly Guid OrgB = new("b0b0b0b0-0000-4000-8000-000000000332");
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

    private static readonly OrderActionType[] AllActions = Enum.GetValues<OrderActionType>();

    /// <summary>No role, a technician-like role with no default grant, Supervisor, Administrator.</summary>
    private static readonly string[][] Roles = [[], ["FieldTechnician"], ["Supervisor"], ["Administrator"]];

    private enum Relation
    {
        SameOrganization,
        OtherOrganizationNoGrant,
        OtherOrganizationGrantWithoutScope,
        OtherOrganizationGrantMatchingScope,
        OtherOrganizationGrantWrongScope,
        OtherOrganizationGrantExpired,
        OtherOrganizationGrantInactive,
        OtherOrganizationGrantWrongResourceType,
        OtherOrganizationGrantRequiringARoleNotHeld,
        OtherOrganizationGrantForAnotherResourceKindOnly,
        OtherOrganizationGrantForAnotherOrganization,
        NoOrganizationNoGrant,
        DispatchRecipientByUser,
        DispatchRecipientByGroup,
        DispatchRecipientByOrganization,
    }

    private enum UserTie { Stranger, Creator, Assignee }

    private static readonly Relation[] PassingRelations =
    [
        Relation.SameOrganization,
        Relation.OtherOrganizationGrantWithoutScope,
        Relation.OtherOrganizationGrantMatchingScope,
        Relation.DispatchRecipientByUser,
        Relation.DispatchRecipientByGroup,
        Relation.DispatchRecipientByOrganization,
    ];

    // Theory rows carry the relation by name: the enum is private to this class.
    public static TheoryData<string> Passing() => [.. PassingRelations.Select(r => r.ToString())];

    public static TheoryData<string> Refused() => [.. Enum.GetValues<Relation>().Except(PassingRelations).Select(r => r.ToString())];

    private static Relation Parse(string relationName) => Enum.Parse<Relation>(relationName);

    /// <summary>
    /// The engine, with the orders' type registered at <see cref="OrderRecipientAccess.Edit"/> —
    /// the one recipient access that leaves the rule chain's outcome as it is (XD01-333) — so the
    /// proofs here keep isolating the gate. The ceiling a lower access puts on a recipient is
    /// <see cref="ServiceOrderRecipientAccessTests"/>'s.
    /// </summary>
    private static ServiceOrderRules Rules(bool allowOrdersWithoutOrganization = true)
    {
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType { Id = "any", DisplayName = "Any", RecipientAccess = OrderRecipientAccess.Edit });
        return new(registry, allowOrdersWithoutOrganization: allowOrdersWithoutOrganization, timeProvider: new FakeTimeProvider(Now));
    }

    private static WorkflowOrgGrant Grant(
        Guid? resource = null, string? resourceType = "ServiceOrder", string? scopeId = null,
        string? requiredRole = null, DateTime? expiresAt = null, params string[] actions) =>
        new((resource ?? OrgA).ToString(), resourceType, actions.Length == 0 ? ["serviceorders:view"] : actions, requiredRole, scopeId)
        {
            ExpiresAt = expiresAt,
        };

    /// <summary>The principal for a relation. An inactive grant is one no host resolves onto a principal, so it is absent.</summary>
    private static WorkflowPrincipal Principal(Relation relation, string[] roles, ActorKind kind = ActorKind.Human)
    {
        var organizationId = relation switch
        {
            Relation.SameOrganization => OrgA.ToString(),
            Relation.NoOrganizationNoGrant => null,
            _ => OrgB.ToString(),
        };

        WorkflowOrgGrant[] grants = relation switch
        {
            Relation.OtherOrganizationGrantWithoutScope => [Grant()],
            Relation.OtherOrganizationGrantMatchingScope => [Grant(scopeId: Scope)],
            Relation.OtherOrganizationGrantWrongScope => [Grant(scopeId: "municipio-999")],
            Relation.OtherOrganizationGrantExpired => [Grant(expiresAt: Now.UtcDateTime.AddMinutes(-1))],
            Relation.OtherOrganizationGrantWrongResourceType => [Grant(resourceType: "GeoFeature")],
            Relation.OtherOrganizationGrantRequiringARoleNotHeld => [Grant(requiredRole: "Auditor")],
            Relation.OtherOrganizationGrantForAnotherResourceKindOnly => [Grant(resourceType: null, actions: "features:read")],
            Relation.OtherOrganizationGrantForAnotherOrganization => [Grant(resource: Guid.NewGuid())],
            _ => [],
        };

        return new WorkflowPrincipal(
            UserId, organizationId, roles, relation == Relation.DispatchRecipientByGroup ? [GroupId] : [], [])
        {
            Kind = kind,
            OrgGrants = grants,
        };
    }

    /// <summary>An order of organization A in the scope the matching grant names, dispatched as the relation says.</summary>
    private static ServiceOrder Order(Relation relation, UserTie tie = UserTie.Stranger, Guid? organizationId = null)
    {
        var order = new ServiceOrder
        {
            Id = "ord-1",
            OrderTypeId = "any",
            Status = ServiceOrderStatus.Pending,
            OrganizationId = organizationId ?? OrgA,
            CreatedBy = tie == UserTie.Creator ? UserId : "someone-else",
            AssignedTo = tie == UserTie.Assignee ? UserId : null,
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["scopeId"] = Scope },
        };

        var dispatchedAt = Now.UtcDateTime.AddHours(-1);
        switch (relation)
        {
            case Relation.DispatchRecipientByUser:
                order.Dispatches.Add(new OrderDispatch(UserId, DispatchTargetType.User, "dispatcher", dispatchedAt));
                break;
            case Relation.DispatchRecipientByGroup:
                order.Dispatches.Add(new OrderDispatch(GroupId, DispatchTargetType.Group, "dispatcher", dispatchedAt));
                break;
            case Relation.DispatchRecipientByOrganization:
                order.Dispatches.Add(new OrderDispatch(OrgB.ToString(), DispatchTargetType.Organization, "dispatcher", dispatchedAt));
                break;
        }

        return order;
    }

    // ── The gate refuses exactly the outside relations ─────────────────────────

    [Theory]
    [MemberData(nameof(Refused))]
    public void XD01_332_Evaluate_PrincipalOutsideTheOrdersOrganization_IsDeniedEveryAction_WhateverItsRoleOrTieToTheOrder(string relationName)
    {
        var relation = Parse(relationName);
        var rules = Rules();

        foreach (var roles in Roles)
        foreach (var tie in Enum.GetValues<UserTie>())
        foreach (var action in AllActions)
        {
            var verdict = rules.Evaluate(Principal(relation, roles), action, Order(relation, tie));

            verdict.Allowed.Should().BeFalse("{0} as [{1}] ({2}) must not {3}", relation, string.Join(",", roles), tie, action);
            verdict.Code.Should().Be(RuleDenialCode.OutsideOrganization);
            verdict.IsOutsideOrganization.Should().BeTrue();
            verdict.Action.Should().Be((OrderAction)action);
        }
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public void XD01_332_PassesOrganizationGate_PrincipalOutsideTheOrdersOrganization_IsFalse(string relationName)
    {
        var relation = Parse(relationName);
        Rules().PassesOrganizationGate(Principal(relation, ["Administrator"]), Order(relation)).Should().BeFalse();
    }

    [Fact]
    public void XD01_332_Evaluate_AdministratorOfAnotherOrganization_WasAllowedByTheRuleChainAlone_AndIsNowDenied()
    {
        // The defect itself: before the gate, the role rule let an Administrator of
        // organization B do anything to an order of organization A it could name.
        var rules = Rules();
        var administratorOfB = Principal(Relation.OtherOrganizationNoGrant, ["Administrator"]);
        var orderOfA = Order(Relation.OtherOrganizationNoGrant);

        foreach (var action in AllActions)
        {
            rules.EvaluateRuleChainOnly(administratorOfB, action, orderOfA).Allowed.Should().BeTrue();
            rules.Evaluate(administratorOfB, action, orderOfA).Allowed.Should().BeFalse();
        }
    }

    // ── Passing the gate changes nothing ────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Passing))]
    public void XD01_332_Evaluate_PrincipalThatPassesTheGate_GetsExactlyTheRuleChainsOwnOutcome(string relationName)
    {
        var relation = Parse(relationName);
        var rules = Rules();

        foreach (var roles in Roles)
        foreach (var tie in Enum.GetValues<UserTie>())
        foreach (var action in AllActions)
        {
            var principal = Principal(relation, roles);
            var order = Order(relation, tie);

            rules.PassesOrganizationGate(principal, order).Should().BeTrue();
            rules.Evaluate(principal, action, order).Should().Be(
                rules.EvaluateRuleChainOnly(principal, action, order),
                "{0} as [{1}] ({2}) asking {3} passes the gate, so only the rule chain decides", relation, string.Join(",", roles), tie, action);
        }
    }

    [Fact]
    public void XD01_332_Evaluate_PassingTheGate_GrantsNothingByItself()
    {
        var rules = Rules();

        foreach (var relation in new[] { Relation.SameOrganization, Relation.OtherOrganizationGrantWithoutScope })
        {
            var verdict = rules.Evaluate(Principal(relation, []), OrderActionType.Approve, Order(relation));

            verdict.Allowed.Should().BeFalse();
            verdict.Code.Should().Be(RuleDenialCode.NoRuleGranted);
            verdict.IsOutsideOrganization.Should().BeFalse();
        }
    }

    [Fact]
    public void XD01_332_Evaluate_SameOrganizationOutcomes_AreTheDocumentedRoleGrants()
    {
        // Spot checks of absolute outcomes, so the equality proof above cannot be satisfied by
        // two wrong results that merely agree.
        var rules = Rules();
        var order = Order(Relation.SameOrganization);

        rules.Evaluate(Principal(Relation.SameOrganization, ["Administrator"]), OrderActionType.Complete, order).Allowed.Should().BeTrue();
        rules.Evaluate(Principal(Relation.SameOrganization, ["Supervisor"]), OrderActionType.Dispatch, order).Allowed.Should().BeTrue();
        rules.Evaluate(Principal(Relation.SameOrganization, ["Supervisor"]), OrderActionType.Complete, order).Allowed.Should().BeFalse();
        rules.Evaluate(Principal(Relation.SameOrganization, ["FieldTechnician"]), OrderActionType.View, order).Allowed.Should().BeFalse();
        rules.Evaluate(Principal(Relation.SameOrganization, []), OrderActionType.View, Order(Relation.SameOrganization, UserTie.Creator))
            .Allowed.Should().BeTrue();
        rules.Evaluate(Principal(Relation.SameOrganization, []), OrderActionType.Complete, Order(Relation.SameOrganization, UserTie.Assignee))
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void XD01_332_Evaluate_Grantee_IsGrantedTheActionsItsGrantLists_AndRefusedTheOthersByTheChainNotTheGate()
    {
        var rules = Rules();
        var grantee = Principal(Relation.OtherOrganizationGrantMatchingScope, []);
        var order = Order(Relation.OtherOrganizationGrantMatchingScope);

        rules.Evaluate(grantee, OrderActionType.View, order).Allowed.Should().BeTrue();

        var notListed = rules.Evaluate(grantee, OrderActionType.Complete, order);
        notListed.Allowed.Should().BeFalse();
        notListed.Code.Should().Be(RuleDenialCode.NoRuleGranted);
    }

    [Fact]
    public void XD01_332_Evaluate_DispatchRecipientOfAnotherOrganization_KeepsViewAnnotateAccept()
    {
        var rules = Rules();

        foreach (var relation in new[] { Relation.DispatchRecipientByUser, Relation.DispatchRecipientByGroup, Relation.DispatchRecipientByOrganization })
        {
            var recipient = Principal(relation, []);
            var order = Order(relation);

            rules.Evaluate(recipient, OrderActionType.View, order).Allowed.Should().BeTrue();
            rules.Evaluate(recipient, OrderActionType.Annotate, order).Allowed.Should().BeTrue();
            rules.Evaluate(recipient, OrderActionType.Accept, order).Allowed.Should().BeTrue();
            rules.Evaluate(recipient, OrderActionType.Complete, order).Code.Should().Be(RuleDenialCode.NoRuleGranted);
        }
    }

    [Fact]
    public void XD01_332_Evaluate_OrganizationIdsAreComparedAsGuids_NotAsText()
    {
        var principal = new WorkflowPrincipal(UserId, OrgA.ToString("N").ToUpperInvariant(), ["Administrator"], [], []);

        Rules().Evaluate(principal, OrderActionType.View, Order(Relation.SameOrganization)).Allowed.Should().BeTrue();
    }

    [Fact]
    public void XD01_332_Evaluate_GrantThatExpiresLater_OpensTheGateUntilItExpires()
    {
        var clock = new FakeTimeProvider(Now);
        var rules = new ServiceOrderRules(timeProvider: clock);
        var grantee = new WorkflowPrincipal(UserId, OrgB.ToString(), [], [], [])
        {
            OrgGrants = [Grant(expiresAt: Now.UtcDateTime.AddMinutes(5))],
        };
        var order = Order(Relation.OtherOrganizationGrantWithoutScope);

        rules.Evaluate(grantee, OrderActionType.View, order).Allowed.Should().BeTrue();

        clock.Advance(TimeSpan.FromMinutes(5));

        rules.Evaluate(grantee, OrderActionType.View, order).Code.Should().Be(RuleDenialCode.OutsideOrganization);
    }

    // ── Agent and system principals ──────────────────────────────────────────────

    [Theory]
    [InlineData(ActorKind.Agent)]
    [InlineData(ActorKind.System)]
    public void XD01_332_Evaluate_AgentAndSystemPrincipals_FollowTheSameGateAsAPerson(ActorKind kind)
    {
        var rules = Rules();

        foreach (var relation in Enum.GetValues<Relation>())
        foreach (var action in AllActions)
        {
            var order = Order(relation);

            rules.Evaluate(Principal(relation, ["Administrator"], kind), action, order).Should().Be(
                rules.Evaluate(Principal(relation, ["Administrator"]), action, order),
                "a principal's kind opens no way through the gate ({0}, {1})", relation, action);
        }
    }

    // ── Orders with no organization ─────────────────────────────────────────────

    public static TheoryData<string> EveryRelation() => [.. Enum.GetNames<Relation>()];

    [Theory]
    [MemberData(nameof(EveryRelation))]
    public void XD01_332_Evaluate_OrderWithNoOrganization_OptionOnByDefault_EveryPrincipalPassesTheGate_AsBeforeTheGate(string relationName)
    {
        var relation = Parse(relationName);
        var rules = new ServiceOrderRules(timeProvider: new FakeTimeProvider(Now));

        foreach (var roles in Roles)
        foreach (var tie in Enum.GetValues<UserTie>())
        foreach (var action in AllActions)
        {
            var principal = Principal(relation, roles);
            var order = Order(relation, tie, Guid.Empty);

            rules.PassesOrganizationGate(principal, order).Should().BeTrue();
            rules.Evaluate(principal, action, order).Should().Be(rules.EvaluateRuleChainOnly(principal, action, order));
        }
    }

    [Theory]
    [MemberData(nameof(EveryRelation))]
    public void XD01_332_Evaluate_OrderWithNoOrganization_OptionOff_OnlyItsDispatchRecipientsPassTheGate(string relationName)
    {
        var relation = Parse(relationName);
        var rules = Rules(allowOrdersWithoutOrganization: false);
        var isRecipient = relation is Relation.DispatchRecipientByUser or Relation.DispatchRecipientByGroup or Relation.DispatchRecipientByOrganization;

        foreach (var roles in Roles)
        foreach (var tie in Enum.GetValues<UserTie>())
        foreach (var action in AllActions)
        {
            var principal = Principal(relation, roles);
            var order = Order(relation, tie, Guid.Empty);
            var verdict = rules.Evaluate(principal, action, order);

            if (isRecipient)
                verdict.Should().Be(rules.EvaluateRuleChainOnly(principal, action, order));
            else
                verdict.Code.Should().Be(RuleDenialCode.OutsideOrganization, "{0} [{1}] ({2}) {3}", relation, string.Join(",", roles), tie, action);
        }
    }

    [Fact]
    public void XD01_332_Evaluate_OrderWithNoOrganization_OptionOff_AGrantNamingTheEmptyOrganizationOpensNothing()
    {
        var rules = Rules(allowOrdersWithoutOrganization: false);
        var principal = new WorkflowPrincipal(UserId, OrgB.ToString(), ["Administrator"], [], [])
        {
            OrgGrants = [Grant(resource: Guid.Empty)],
        };

        rules.Evaluate(principal, OrderActionType.View, Order(Relation.OtherOrganizationNoGrant, organizationId: Guid.Empty))
            .Code.Should().Be(RuleDenialCode.OutsideOrganization);
    }

    [Fact]
    public void XD01_332_AddServiceOrderRules_AllowOrdersWithoutOrganization_DefaultsToTrue_AndCanBeTurnedOff()
    {
        new ServiceOrderRulesOptions().AllowOrdersWithoutOrganization.Should().BeTrue();

        using var byDefault = new ServiceCollection().AddServiceOrderRules().BuildServiceProvider();
        using var turnedOff = new ServiceCollection()
            .AddServiceOrderRules(o => o.AllowOrdersWithoutOrganization = false)
            .BuildServiceProvider();
        var administrator = new WorkflowPrincipal(UserId, OrgB.ToString(), ["Administrator"], [], []);
        var orderWithNoOrganization = Order(Relation.OtherOrganizationNoGrant, organizationId: Guid.Empty);

        byDefault.GetRequiredService<ServiceOrderRules>()
            .Evaluate(administrator, OrderActionType.View, orderWithNoOrganization).Allowed.Should().BeTrue();
        turnedOff.GetRequiredService<ServiceOrderRules>()
            .Evaluate(administrator, OrderActionType.View, orderWithNoOrganization).Code.Should().Be(RuleDenialCode.OutsideOrganization);
    }

    // ── Result codes ────────────────────────────────────────────────────────────

    [Fact]
    public void XD01_332_Evaluate_ResultCode_TellsEveryKindOfOutcomeApart()
    {
        var rules = Rules().AddRule(new DenyDispute());
        var administrator = Principal(Relation.SameOrganization, ["Administrator"]);
        var order = Order(Relation.SameOrganization);

        rules.Evaluate(administrator, OrderActionType.View, order).Code.Should().Be(RuleDenialCode.None);
        rules.Evaluate(WorkflowPrincipal.Anonymous, OrderActionType.View, order).Code.Should().Be(RuleDenialCode.Anonymous);
        rules.Evaluate(administrator, NeutralOperatorActions.Dispute, order).Code.Should().Be(RuleDenialCode.DeniedByRule);
        rules.Evaluate(Principal(Relation.SameOrganization, []), OrderActionType.View, order).Code.Should().Be(RuleDenialCode.NoRuleGranted);
        rules.Evaluate(Principal(Relation.OtherOrganizationNoGrant, ["Administrator"]), OrderActionType.View, order)
            .Code.Should().Be(RuleDenialCode.OutsideOrganization);
    }

    [Fact]
    public void XD01_332_Evaluate_TheGateRunsBeforeEveryRule_SoACustomRuleCannotLetAnOutsiderIn()
    {
        var consulted = 0;
        var rules = Rules().AddRule(new CountingAllowRule(() => consulted++));

        rules.Evaluate(Principal(Relation.OtherOrganizationNoGrant, []), OrderActionType.View, Order(Relation.OtherOrganizationNoGrant))
            .Code.Should().Be(RuleDenialCode.OutsideOrganization);
        consulted.Should().Be(0);
    }

    [Fact]
    public void XD01_332_PassesOrganizationGate_AnonymousPrincipal_IsFalse()
    {
        Rules().PassesOrganizationGate(WorkflowPrincipal.Anonymous, Order(Relation.SameOrganization, organizationId: Guid.Empty))
            .Should().BeFalse();
    }

    private sealed class DenyDispute : IServiceOrderRule
    {
        public string Name => nameof(DenyDispute);

        public bool? Evaluate(OrderAction action, RuleEvaluationContext context) =>
            action == NeutralOperatorActions.Dispute ? false : null;
    }

    private sealed class CountingAllowRule(Action onEvaluate) : IServiceOrderRule
    {
        public string Name => nameof(CountingAllowRule);

        public bool? Evaluate(OrderAction action, RuleEvaluationContext context)
        {
            onEvaluate();
            return true;
        }
    }
}

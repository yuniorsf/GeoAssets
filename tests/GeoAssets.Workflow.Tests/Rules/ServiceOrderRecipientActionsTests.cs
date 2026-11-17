using FluentAssertions;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests.Rules;

/// <summary>
/// XD01-336 — an order type may let the recipients of its orders take some order actions on top of
/// its recipient access (<see cref="OrderType.RecipientActions"/>), intersected with what the rule
/// chain allows the acting user, each action moving the order only along its own transition. The
/// shared-use occupant notice lets its occupant accept it and comment on it, and not cancel it.
/// Owners and grantees are untouched. Every expectation is written out, not computed.
/// </summary>
public sealed class ServiceOrderRecipientActionsTests
{
    private const string UserId = "user-1";
    private const string ByRoleTypeId = "recipient-actions-by-role";

    private static readonly Guid Owner = new("a0a0a0a0-0000-4000-8000-000000000336");
    private static readonly Guid Occupant = new("c0c0c0c0-0000-4000-8000-000000000336");
    private static readonly Guid Grantee = new("b0b0b0b0-0000-4000-8000-000000000336");
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The real notice, plus a type that grants its recipients the same two actions but requires a
    /// role for each: Crew, Supervisor or Administrator to annotate, Supervisor or Administrator to accept.
    /// </summary>
    private static OrderTypeRegistry Registry()
    {
        var registry = new OrderTypeRegistry();
        SharedUseOrderTypes.RegisterAll(registry, ["pole"]);
        registry.Register(new OrderType
        {
            Id = ByRoleTypeId,
            DisplayName = "By role",
            RecipientActions = [OrderActionType.Accept, OrderActionType.Annotate],
            ActionPermissions =
            [
                new(OrderActionType.Annotate, PolicyKind.Role, "Crew"),
                new(OrderActionType.Annotate, PolicyKind.Role, "Supervisor"),
                new(OrderActionType.Annotate, PolicyKind.Role, "Administrator"),
                new(OrderActionType.Accept, PolicyKind.Role, "Supervisor"),
                new(OrderActionType.Accept, PolicyKind.Role, "Administrator"),
            ],
        });
        return registry;
    }

    private static ServiceOrderRules Rules(OrderTypeRegistry? registry = null) =>
        new(registry ?? Registry(), timeProvider: new FakeTimeProvider(Now));

    private static string[] RolesOf(string role) => role switch
    {
        "crew" => ["Crew"],
        "supervisor" => ["Supervisor"],
        "administrator" => ["Administrator"],
        _ => [],
    };

    private static WorkflowPrincipal Principal(string relation, string role) => relation switch
    {
        "owner" => new(UserId, Owner.ToString(), RolesOf(role), [], []),
        "recipient" => new(UserId, Occupant.ToString(), RolesOf(role), [], []),
        _ => new(UserId, Grantee.ToString(), RolesOf(role), [], [])
        {
            OrgGrants = [new WorkflowOrgGrant(Owner.ToString(), "ServiceOrder", ["serviceorders:view", "serviceorders:annotate"], null)],
        },
    };

    /// <summary>A dispatched order of the owner, of the given type, dispatched to the occupant organization.</summary>
    private static ServiceOrder Order(string orderTypeId = SharedUseOrderTypes.OccupantNoticeId, string status = ServiceOrderStatus.Pending)
    {
        var order = new ServiceOrder
        {
            Id = "ord-336",
            Title = "Notice",
            OrderTypeId = orderTypeId,
            Status = status,
            OrganizationId = Owner,
            CreatedBy = "someone-else",
        };
        order.Dispatches.Add(new OrderDispatch(Occupant.ToString(), DispatchTargetType.Organization, "dispatcher", Now.UtcDateTime));
        return order;
    }

    private static OrderActionLog Entry(OrderActionType action, string? resultingStatus = null) =>
        new(action, UserId, Now.UtcDateTime, "a comment", resultingStatus);

    private static ServiceOrder Retitled(ServiceOrder order) => new()
    {
        Id = order.Id,
        Title = "Rewritten",
        OrderTypeId = order.OrderTypeId,
        Status = order.Status,
        OrganizationId = order.OrganizationId,
        CreatedBy = order.CreatedBy,
    };

    // role, relation, View, Accept (Pending → InProgress), Annotate, Cancel (→ Cancelled),
    // an annotation that would cancel the order, the whole-order update of its title.
    public static TheoryData<string, string, bool, bool, bool, bool, bool, bool> NoticeMatrix() => new()
    {
        { "none",          "owner",     false, false, false, false, false, false },
        { "none",          "recipient", true,  true,  true,  false, false, false },
        { "none",          "grantee",   true,  false, true,  false, true,  true },
        { "crew",          "owner",     false, false, false, false, false, false },
        { "crew",          "recipient", true,  true,  true,  false, false, false },
        { "crew",          "grantee",   true,  false, true,  false, true,  true },
        { "supervisor",    "owner",     true,  false, true,  true,  true,  true },
        { "supervisor",    "recipient", true,  true,  true,  false, false, false },
        { "supervisor",    "grantee",   true,  false, true,  true,  true,  true },
        { "administrator", "owner",     true,  true,  true,  true,  true,  true },
        { "administrator", "recipient", true,  true,  true,  false, false, false },
        { "administrator", "grantee",   true,  true,  true,  true,  true,  true },
    };

    [Theory]
    [MemberData(nameof(NoticeMatrix))]
    public void Notice_RoleAndRelation_DecideAcceptAnnotateAndCancel(
        string role, string relation, bool view, bool accept, bool annotate, bool cancel, bool annotateCancelling, bool replace)
    {
        var rules = Rules();
        var principal = Principal(relation, role);
        var order = Order();

        rules.Evaluate(principal, OrderActionType.View, order).Allowed.Should().Be(view, "View");
        rules.EvaluateAppend(principal, order, Entry(OrderActionType.Accept, ServiceOrderStatus.InProgress)).Allowed.Should().Be(accept, "Accept");
        rules.EvaluateAppend(principal, order, Entry(OrderActionType.Annotate)).Allowed.Should().Be(annotate, "Annotate");
        rules.EvaluateAppend(principal, order, Entry(OrderActionType.Cancel, ServiceOrderStatus.Cancelled)).Allowed.Should().Be(cancel, "Cancel");
        rules.Evaluate(principal, OrderActionType.Cancel, order).Allowed.Should().Be(cancel, "Cancel, as DELETE evaluates it");
        rules.EvaluateAppend(principal, order, Entry(OrderActionType.Annotate, ServiceOrderStatus.Cancelled)).Allowed
            .Should().Be(annotateCancelling, "an annotation that cancels");
        rules.EvaluateReplace(principal, order, Retitled(order)).Allowed.Should().Be(replace, "replace the title");
        rules.EvaluateTargetWrite(principal, order, OrderTargetWrite.RecordWork).Allowed
            .Should().Be(annotate && !string.Equals(relation, "recipient", StringComparison.Ordinal), "record target work");
    }

    // role, Accept, Annotate — on a type whose rules require a role for each.
    public static TheoryData<string, bool, bool> ByRoleMatrix() => new()
    {
        { "none",          false, false },
        { "crew",          false, true },
        { "supervisor",    true,  true },
        { "administrator", true,  true },
    };

    [Theory]
    [MemberData(nameof(ByRoleMatrix))]
    public void ARecipientAction_IsStillTheActingUsersRulesToAllow(string role, bool accept, bool annotate)
    {
        var rules = Rules();
        var recipient = Principal("recipient", role);
        var order = Order(ByRoleTypeId);

        rules.Evaluate(recipient, OrderActionType.Accept, order).Allowed.Should().Be(accept, "Accept");
        rules.Evaluate(recipient, OrderActionType.Annotate, order).Allowed.Should().Be(annotate, "Annotate");
        rules.Evaluate(recipient, OrderActionType.Cancel, order).Allowed.Should().BeFalse("Cancel");
    }

    [Fact]
    public void Notice_ARecipientAdministrator_IsRefusedEveryActionButViewAcceptAndAnnotate_WithTheRecipientAccessCode()
    {
        var rules = Rules();
        var administrator = Principal("recipient", "administrator");
        var order = Order();

        foreach (var action in Enum.GetValues<OrderActionType>())
        {
            var verdict = rules.Evaluate(administrator, action, order);
            if (action is OrderActionType.View or OrderActionType.Accept or OrderActionType.Annotate)
            {
                verdict.Allowed.Should().BeTrue("{0}", action);
                continue;
            }

            verdict.Allowed.Should().BeFalse("{0}", action);
            verdict.Code.Should().Be(RuleDenialCode.DeniedByRecipientAccess);
            verdict.Reason.Should().Be(ServiceOrderRules.RecipientOrderIsReadOnlyReason);
        }
    }

    [Theory]
    [InlineData(OrderActionType.Accept, ServiceOrderStatus.Completed)]
    [InlineData(OrderActionType.Accept, ServiceOrderStatus.Cancelled)]
    [InlineData(OrderActionType.Annotate, ServiceOrderStatus.InProgress)]
    [InlineData(OrderActionType.Annotate, ServiceOrderStatus.Cancelled)]
    public void Notice_ARecipientActionOffItsTransition_IsRefusedWithTheTransitionReason(OrderActionType action, string resultingStatus)
    {
        var verdict = Rules().EvaluateAppend(Principal("recipient", "administrator"), Order(), Entry(action, resultingStatus));

        verdict.Allowed.Should().BeFalse();
        verdict.Action.Should().Be(action);
        verdict.Code.Should().Be(RuleDenialCode.DeniedByRecipientAccess);
        verdict.Reason.Should().Be(ServiceOrderRules.RecipientActionTransitionIsFixedReason);
    }

    [Fact]
    public void Notice_AcceptingAnOrderAlreadyInProgress_HasNoMoveToMake_SoOnlyAnEntryThatKeepsTheStatusIsAllowed()
    {
        var rules = Rules();
        var recipient = Principal("recipient", "crew");
        var order = Order(status: ServiceOrderStatus.InProgress);

        rules.EvaluateAppend(recipient, order, Entry(OrderActionType.Accept)).Allowed.Should().BeTrue();
        rules.EvaluateAppend(recipient, order, Entry(OrderActionType.Accept, ServiceOrderStatus.InProgress)).Allowed.Should().BeTrue();
        rules.EvaluateAppend(recipient, order, Entry(OrderActionType.Accept, ServiceOrderStatus.Completed)).Allowed.Should().BeFalse();
    }

    [Fact]
    public void Notice_ARecipientsReplace_IsRefusedAsReadOnly_EvenThoughItMayAnnotate()
    {
        var order = Order();

        var verdict = Rules().EvaluateReplace(Principal("recipient", "administrator"), order, Retitled(order));

        verdict.Allowed.Should().BeFalse();
        verdict.Code.Should().Be(RuleDenialCode.DeniedByRecipientAccess);
        verdict.Reason.Should().Be(ServiceOrderRules.RecipientOrderIsReadOnlyReason);
    }

    [Fact]
    public void AtEdit_EvaluateAppend_IsExactlyEvaluate_WhateverTheResultingStatus()
    {
        var registry = Registry();
        registry.Register(new OrderType { Id = "edit", DisplayName = "Edit", RecipientAccess = OrderRecipientAccess.Edit });
        var rules = Rules(registry);
        var administrator = Principal("recipient", "administrator");
        var order = Order("edit");

        rules.EvaluateAppend(administrator, order, Entry(OrderActionType.Annotate, ServiceOrderStatus.Cancelled))
            .Should().Be(rules.Evaluate(administrator, OrderActionType.Annotate, order));
        rules.EvaluateAppend(administrator, order, Entry(OrderActionType.Annotate, ServiceOrderStatus.Cancelled)).Allowed.Should().BeTrue();
    }

    [Fact]
    public void EvaluateAppend_RefusesAsEvaluateDoes_OutsideTheGate_AndAnonymous_AndThrowsOnNulls()
    {
        var rules = Rules();
        var order = Order();
        var stranger = new WorkflowPrincipal(UserId, Guid.NewGuid().ToString(), ["Administrator"], [], []);
        var entry = Entry(OrderActionType.Accept, ServiceOrderStatus.InProgress);

        rules.EvaluateAppend(stranger, order, entry).Code.Should().Be(RuleDenialCode.OutsideOrganization);
        rules.EvaluateAppend(WorkflowPrincipal.Anonymous, order, entry).Code.Should().Be(RuleDenialCode.Anonymous);
        ((Action)(() => rules.EvaluateAppend(null!, order, entry))).Should().Throw<ArgumentNullException>();
        ((Action)(() => rules.EvaluateAppend(stranger, null!, entry))).Should().Throw<ArgumentNullException>();
        ((Action)(() => rules.EvaluateAppend(stranger, order, null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RecipientActionsOf_IsTheTypesSet_AndNoneForAnUnknownTypeOrNoRegistry()
    {
        var rules = Rules();

        rules.RecipientActionsOf(Order()).Should().Equal(OrderActionType.Accept, OrderActionType.Annotate);
        rules.RecipientActionsOf(Order("SHARED-USE-OCCUPANT-NOTICE")).Should().Equal(OrderActionType.Accept, OrderActionType.Annotate);
        rules.RecipientActionsOf(Order(SharedUseOrderTypes.PoleWorkId)).Should().BeEmpty();
        rules.RecipientActionsOf(Order("not-registered")).Should().BeEmpty();
        new ServiceOrderRules(timeProvider: new FakeTimeProvider(Now)).RecipientActionsOf(Order()).Should().BeEmpty();
        ((Action)(() => rules.RecipientActionsOf(null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ATypeThatSaysNothing_GrantsItsRecipientsNoAction_WhileTheNoticeGrantsAcceptAndAnnotate_OnAnInformLevel()
    {
        new OrderType { Id = "plain", DisplayName = "Plain" }.RecipientActions.Should().BeEmpty();
        SharedUseOrderTypes.CreatePoleWork(["pole"]).RecipientActions.Should().BeEmpty();

        var notice = SharedUseOrderTypes.CreateOccupantNotice(["pole"]);
        notice.RecipientActions.Should().Equal(OrderActionType.Accept, OrderActionType.Annotate);
        notice.RecipientAccess.Should().Be(OrderRecipientAccess.Inform);
        ((int)OrderActionType.Accept, (int)OrderActionType.Annotate)
            .Should().Be((4, 9), "the row-level security seed and functions compare the stored numbers");
    }

    // ── The moves an action makes ─────────────────────────────────────────────

    [Fact]
    public void TransitionsFor_ACustomGraph_IsItsTransitionsTaggedWithTheAction()
    {
        var notice = SharedUseOrderTypes.CreateOccupantNotice(["pole"]);

        ServiceOrderTransitions.TransitionsFor(notice, OrderActionType.Accept)
            .Should().Equal(new WorkflowTransition(ServiceOrderStatus.Pending, ServiceOrderStatus.InProgress, OrderActionType.Accept));
        ServiceOrderTransitions.TransitionsFor(notice, OrderActionType.Annotate).Should().BeEmpty();
        ServiceOrderTransitions.TransitionsFor(notice, OrderActionType.Cancel).Select(t => t.FromStateKey)
            .Should().Equal(ServiceOrderStatus.Pending, ServiceOrderStatus.InProgress, ServiceOrderStatus.OnHold);
        ServiceOrderTransitions.TransitionsFor(SharedUseOrderTypes.CreatePoleWork(["pole"]), OrderActionType.Accept)
            .Should().BeEmpty("pole work is never dispatched to a recipient");
    }

    [Fact]
    public void TransitionsFor_TheDefaultGraph_IsTheSelfCancelFromDraftOrPending_AndNothingElse()
    {
        ServiceOrderTransitions.TransitionsFor(null, OrderActionType.Cancel).Should().Equal(
            new WorkflowTransition(ServiceOrderStatus.Draft, ServiceOrderStatus.Cancelled, OrderActionType.Cancel),
            new WorkflowTransition(ServiceOrderStatus.Pending, ServiceOrderStatus.Cancelled, OrderActionType.Cancel));
        ServiceOrderTransitions.TransitionsFor(new OrderType { Id = "plain", DisplayName = "Plain" }, OrderActionType.Accept).Should().BeEmpty();
        ServiceOrderTransitions.TransitionsFor(null, OrderActionType.Annotate).Should().BeEmpty();
    }
}

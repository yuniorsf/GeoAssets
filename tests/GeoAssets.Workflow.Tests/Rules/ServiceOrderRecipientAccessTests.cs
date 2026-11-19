using FluentAssertions;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests.Rules;

/// <summary>
/// XD01-333 — what a principal may do on a dispatched order is the order type's recipient
/// access intersected with what the rule chain allows the principal, for a principal that
/// reaches the order only as a recipient of its dispatch; a member of the owning organization
/// and a cross-organization grantee are untouched by the type's recipient access.
///
/// The matrix is type level × the acting user's role × how the principal reaches the order.
/// Each order type requires the Crew, Supervisor or Administrator role to annotate, so a user
/// with no role lacks <see cref="OrderActionType.Annotate"/> whatever else holds — the "user that
/// acts" half of the decision. Every expectation is written out, not computed.
/// </summary>
public sealed class ServiceOrderRecipientAccessTests
{
    private const string UserId = "user-1";

    private static readonly Guid Owner = new("a0a0a0a0-0000-4000-8000-000000000333");
    private static readonly Guid Occupant = new("c0c0c0c0-0000-4000-8000-000000000333");
    private static readonly Guid Grantee = new("b0b0b0b0-0000-4000-8000-000000000333");
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    private static string TypeId(OrderRecipientAccess level) => level switch
    {
        OrderRecipientAccess.Execute => "recipient-execute",
        OrderRecipientAccess.Edit => "recipient-edit",
        _ => "recipient-inform",
    };

    private static OrderTypeRegistry Registry()
    {
        var registry = new OrderTypeRegistry();
        foreach (var level in Enum.GetValues<OrderRecipientAccess>())
        {
            registry.Register(new OrderType
            {
                Id = TypeId(level),
                DisplayName = level.ToString(),
                RecipientAccess = level,
                ActionPermissions =
                [
                    new(OrderActionType.Annotate, PolicyKind.Role, "Crew"),
                    new(OrderActionType.Annotate, PolicyKind.Role, "Supervisor"),
                    new(OrderActionType.Annotate, PolicyKind.Role, "Administrator"),
                ],
            });
        }

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

    /// <summary>An order of the owner, of the given type, dispatched to the occupant organization.</summary>
    private static ServiceOrder Order(string orderTypeId, OrderDispatch? dispatch = null)
    {
        var order = new ServiceOrder
        {
            Id = "ord-333",
            Title = "Notice",
            OrderTypeId = orderTypeId,
            Status = ServiceOrderStatus.Pending,
            OrganizationId = Owner,
            CreatedBy = "someone-else",
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["region"] = "north" },
        };
        order.Dispatches.Add(dispatch ?? new OrderDispatch(Occupant.ToString(), DispatchTargetType.Organization, "dispatcher", Now.UtcDateTime));
        return order;
    }

    private static ServiceOrder Copy(ServiceOrder order, Action<ServiceOrder> change)
    {
        var copy = new ServiceOrder
        {
            Id = order.Id,
            Title = order.Title,
            Description = order.Description,
            OrderTypeId = order.OrderTypeId,
            Status = order.Status,
            Priority = order.Priority,
            OrganizationId = order.OrganizationId,
            CreatedBy = order.CreatedBy,
            AssignedTo = order.AssignedTo,
            ScheduledAt = order.ScheduledAt,
            CompletedAt = order.CompletedAt,
            ParentOrderId = order.ParentOrderId,
            Attributes = new Dictionary<string, string>(order.Attributes, StringComparer.Ordinal),
        };
        change(copy);
        return copy;
    }

    // level, role, relation, View, Annotate (and the whole-order update of its content), Accept,
    // record target work, add or remove targets, a whole-order update that changes the order's type.
    public static TheoryData<OrderRecipientAccess, string, string, bool, bool, bool, bool, bool, bool> Matrix() => new()
    {
        { OrderRecipientAccess.Inform,  "none",          "owner",     false, false, false, false, false, false },
        { OrderRecipientAccess.Inform,  "none",          "recipient", true,  false, false, false, false, false },
        { OrderRecipientAccess.Inform,  "none",          "grantee",   true,  false, false, false, false, false },
        { OrderRecipientAccess.Inform,  "crew",          "owner",     false, true,  false, true,  true,  true },
        { OrderRecipientAccess.Inform,  "crew",          "recipient", true,  false, false, false, false, false },
        { OrderRecipientAccess.Inform,  "crew",          "grantee",   true,  true,  false, true,  true,  true },
        { OrderRecipientAccess.Inform,  "supervisor",    "owner",     true,  true,  false, true,  true,  true },
        { OrderRecipientAccess.Inform,  "supervisor",    "recipient", true,  false, false, false, false, false },
        { OrderRecipientAccess.Inform,  "supervisor",    "grantee",   true,  true,  false, true,  true,  true },
        { OrderRecipientAccess.Inform,  "administrator", "owner",     true,  true,  true,  true,  true,  true },
        { OrderRecipientAccess.Inform,  "administrator", "recipient", true,  false, false, false, false, false },
        { OrderRecipientAccess.Inform,  "administrator", "grantee",   true,  true,  true,  true,  true,  true },
        { OrderRecipientAccess.Execute, "none",          "owner",     false, false, false, false, false, false },
        { OrderRecipientAccess.Execute, "none",          "recipient", true,  false, false, false, false, false },
        { OrderRecipientAccess.Execute, "none",          "grantee",   true,  false, false, false, false, false },
        { OrderRecipientAccess.Execute, "crew",          "owner",     false, true,  false, true,  true,  true },
        { OrderRecipientAccess.Execute, "crew",          "recipient", true,  false, false, true,  false, false },
        { OrderRecipientAccess.Execute, "crew",          "grantee",   true,  true,  false, true,  true,  true },
        { OrderRecipientAccess.Execute, "supervisor",    "owner",     true,  true,  false, true,  true,  true },
        { OrderRecipientAccess.Execute, "supervisor",    "recipient", true,  false, false, true,  false, false },
        { OrderRecipientAccess.Execute, "supervisor",    "grantee",   true,  true,  false, true,  true,  true },
        { OrderRecipientAccess.Execute, "administrator", "owner",     true,  true,  true,  true,  true,  true },
        { OrderRecipientAccess.Execute, "administrator", "recipient", true,  false, false, true,  false, false },
        { OrderRecipientAccess.Execute, "administrator", "grantee",   true,  true,  true,  true,  true,  true },
        { OrderRecipientAccess.Edit,    "none",          "owner",     false, false, false, false, false, false },
        { OrderRecipientAccess.Edit,    "none",          "recipient", true,  false, true,  false, false, false },
        { OrderRecipientAccess.Edit,    "none",          "grantee",   true,  false, false, false, false, false },
        { OrderRecipientAccess.Edit,    "crew",          "owner",     false, true,  false, true,  true,  true },
        { OrderRecipientAccess.Edit,    "crew",          "recipient", true,  true,  true,  true,  false, false },
        { OrderRecipientAccess.Edit,    "crew",          "grantee",   true,  true,  false, true,  true,  true },
        { OrderRecipientAccess.Edit,    "supervisor",    "owner",     true,  true,  false, true,  true,  true },
        { OrderRecipientAccess.Edit,    "supervisor",    "recipient", true,  true,  true,  true,  false, false },
        { OrderRecipientAccess.Edit,    "supervisor",    "grantee",   true,  true,  false, true,  true,  true },
        { OrderRecipientAccess.Edit,    "administrator", "owner",     true,  true,  true,  true,  true,  true },
        { OrderRecipientAccess.Edit,    "administrator", "recipient", true,  true,  true,  true,  false, false },
        { OrderRecipientAccess.Edit,    "administrator", "grantee",   true,  true,  true,  true,  true,  true },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Matrix_LevelRoleAndRelation_DecideEveryAction(
        OrderRecipientAccess level, string role, string relation,
        bool view, bool annotate, bool accept, bool recordWork, bool addOrRemove, bool replaceType)
    {
        var rules = Rules();
        var principal = Principal(relation, role);
        var order = Order(TypeId(level));

        rules.Evaluate(principal, OrderActionType.View, order).Allowed.Should().Be(view, "View");
        rules.Evaluate(principal, OrderActionType.Annotate, order).Allowed.Should().Be(annotate, "Annotate");
        rules.Evaluate(principal, OrderActionType.Accept, order).Allowed.Should().Be(accept, "Accept");
        rules.EvaluateTargetWrite(principal, order, OrderTargetWrite.RecordWork).Allowed.Should().Be(recordWork, "record work");
        rules.EvaluateTargetWrite(principal, order, OrderTargetWrite.AddOrRemove).Allowed.Should().Be(addOrRemove, "add or remove");
        rules.EvaluateReplace(principal, order, Copy(order, o => o.Title = "Edited")).Allowed.Should().Be(annotate, "replace the content");
        rules.EvaluateReplace(principal, order, Copy(order, o => o.OrderTypeId = "another-type")).Allowed
            .Should().Be(replaceType, "replace the type");
        rules.IsDispatchRecipientOnly(principal, order).Should().Be(string.Equals(relation, "recipient", StringComparison.Ordinal));
    }

    public static TheoryData<OrderRecipientAccess, string, string> OwnersAndGrantees()
    {
        var rows = new TheoryData<OrderRecipientAccess, string, string>();
        foreach (var level in Enum.GetValues<OrderRecipientAccess>())
        foreach (var role in new[] { "none", "crew", "supervisor", "administrator" })
        foreach (var relation in new[] { "owner", "grantee" })
            rows.Add(level, role, relation);
        return rows;
    }

    [Theory]
    [MemberData(nameof(OwnersAndGrantees))]
    public void OwnersAndGrantees_GetExactlyTheRuleChainsOutcome_WhateverTheLevel(OrderRecipientAccess level, string role, string relation)
    {
        var rules = Rules();
        var principal = Principal(relation, role);
        var order = Order(TypeId(level));

        foreach (var action in Enum.GetValues<OrderActionType>())
            rules.Evaluate(principal, action, order).Should().Be(rules.EvaluateRuleChainOnly(principal, action, order), "{0}", action);
        rules.EvaluateTargetWrite(principal, order, OrderTargetWrite.RecordWork)
            .Should().Be(rules.EvaluateRuleChainOnly(principal, OrderActionType.Annotate, order));
        rules.EvaluateTargetWrite(principal, order, OrderTargetWrite.AddOrRemove)
            .Should().Be(rules.EvaluateRuleChainOnly(principal, OrderActionType.Annotate, order));
    }

    // ── Refusals: code and reason ─────────────────────────────────────────────

    [Theory]
    [InlineData(OrderRecipientAccess.Inform)]
    [InlineData(OrderRecipientAccess.Execute)]
    public void BelowEdit_ARecipientTheChainAllows_IsRefusedEveryActionButView_WithTheRecipientAccessCode(OrderRecipientAccess level)
    {
        var rules = Rules();
        var administrator = Principal("recipient", "administrator");
        var order = Order(TypeId(level));

        foreach (var action in Enum.GetValues<OrderActionType>().Where(a => a != OrderActionType.View))
        {
            rules.EvaluateRuleChainOnly(administrator, action, order).Allowed.Should().BeTrue("an Administrator's role grants {0}", action);

            var verdict = rules.Evaluate(administrator, action, order);
            verdict.Allowed.Should().BeFalse("{0}", action);
            verdict.Action.Should().Be((OrderAction)action);
            verdict.Code.Should().Be(RuleDenialCode.DeniedByRecipientAccess);
            verdict.Reason.Should().Be(ServiceOrderRules.RecipientOrderIsReadOnlyReason);
            verdict.IsOutsideOrganization.Should().BeFalse();
        }
    }

    [Fact]
    public void AtEdit_ARecipientAdministrator_KeepsEveryActionTheChainAllows()
    {
        var rules = Rules();
        var administrator = Principal("recipient", "administrator");
        var order = Order(TypeId(OrderRecipientAccess.Edit));

        foreach (var action in Enum.GetValues<OrderActionType>())
            rules.Evaluate(administrator, action, order).Should().Be(rules.EvaluateRuleChainOnly(administrator, action, order), "{0}", action);
    }

    [Theory]
    [InlineData(OrderRecipientAccess.Inform, OrderTargetWrite.RecordWork)]
    [InlineData(OrderRecipientAccess.Inform, OrderTargetWrite.AddOrRemove)]
    [InlineData(OrderRecipientAccess.Execute, OrderTargetWrite.AddOrRemove)]
    [InlineData(OrderRecipientAccess.Edit, OrderTargetWrite.AddOrRemove)]
    public void TargetWrite_RefusedToARecipient_CarriesTheTargetsReadOnlyReason(OrderRecipientAccess level, OrderTargetWrite write)
    {
        var verdict = Rules().EvaluateTargetWrite(Principal("recipient", "supervisor"), Order(TypeId(level)), write);

        verdict.Allowed.Should().BeFalse();
        verdict.Action.Should().Be(OrderAction.Annotate);
        verdict.Code.Should().Be(RuleDenialCode.DeniedByRecipientAccess);
        verdict.Reason.Should().Be(ServiceOrderRules.RecipientTargetsAreReadOnlyReason);
    }

    [Fact]
    public void TargetWrite_ByARecipientWhoseRoleLacksAnnotate_IsRefusedByTheRuleItself_EvenOnAnEditType()
    {
        var verdict = Rules().EvaluateTargetWrite(Principal("recipient", "none"), Order(TypeId(OrderRecipientAccess.Edit)), OrderTargetWrite.RecordWork);

        verdict.Allowed.Should().BeFalse();
        verdict.Code.Should().Be(RuleDenialCode.DeniedByRule, "the type requires a role to annotate; the recipient access never grants");
    }

    [Fact]
    public void TargetWrite_OutsideTheGate_AndAnonymous_AreRefusedAsEvaluateRefusesThem()
    {
        var rules = Rules();
        var order = Order(TypeId(OrderRecipientAccess.Edit));
        var stranger = new WorkflowPrincipal(UserId, Guid.NewGuid().ToString(), ["Administrator"], [], []);

        rules.EvaluateTargetWrite(stranger, order, OrderTargetWrite.RecordWork).Code.Should().Be(RuleDenialCode.OutsideOrganization);
        rules.EvaluateTargetWrite(WorkflowPrincipal.Anonymous, order, OrderTargetWrite.RecordWork).Code.Should().Be(RuleDenialCode.Anonymous);
        ((Action)(() => rules.EvaluateTargetWrite(null!, order, OrderTargetWrite.RecordWork))).Should().Throw<ArgumentNullException>();
        ((Action)(() => rules.EvaluateTargetWrite(stranger, null!, OrderTargetWrite.RecordWork))).Should().Throw<ArgumentNullException>();
    }

    // ── The whole-order update of a recipient allowed to edit ─────────────────

    public static TheoryData<string> FixedFieldChanges() =>
    [
        "OrderTypeId", "Status", "AssignedTo", "CompletedAt", "ParentOrderId", "Attributes.scopeId", "Attributes.fanOutPartyOrganizationId",
    ];

    private static ServiceOrder Changing(ServiceOrder order, string field) => Copy(order, o =>
    {
        switch (field)
        {
            case "OrderTypeId": o.OrderTypeId = TypeId(OrderRecipientAccess.Execute); break;
            case "Status": o.Status = ServiceOrderStatus.InProgress; break;
            case "AssignedTo": o.AssignedTo = "occupant-crew"; break;
            case "CompletedAt": o.CompletedAt = Now.UtcDateTime; break;
            case "ParentOrderId": o.ParentOrderId = "another-order"; break;
            case "Attributes.scopeId": o.Attributes["scopeId"] = "municipio-1"; break;
            default: o.Attributes[SharedUseDispatchPlanner.PartyOrganizationAttribute] = Occupant.ToString(); break;
        }
    });

    [Theory]
    [MemberData(nameof(FixedFieldChanges))]
    public void Replace_ByARecipientOfAnEditType_ChangingWhatTheOrderIs_IsRefused_ButByTheOwnerAllowed(string field)
    {
        var rules = Rules();
        var order = Order(TypeId(OrderRecipientAccess.Edit));
        var replacement = Changing(order, field);

        ServiceOrderRecipientEdit.FixedFieldsChanged(order, replacement).Should().Equal(field);
        var verdict = rules.EvaluateReplace(Principal("recipient", "supervisor"), order, replacement);
        verdict.Allowed.Should().BeFalse();
        verdict.Code.Should().Be(RuleDenialCode.DeniedByRecipientAccess);
        verdict.Reason.Should().Be(ServiceOrderRules.RecipientOrderFieldsAreFixedReason);
        rules.EvaluateReplace(Principal("owner", "supervisor"), order, replacement).Allowed.Should().BeTrue();
        rules.EvaluateReplace(Principal("grantee", "supervisor"), order, replacement).Allowed.Should().BeTrue();
    }

    [Fact]
    public void Replace_ByARecipientOfAnEditType_ChangingOnlyTheContent_IsAllowed()
    {
        var order = Order(TypeId(OrderRecipientAccess.Edit));
        var replacement = Copy(order, o =>
        {
            o.Title = "Edited";
            o.Description = "Rewritten";
            o.Priority = ServiceOrderPriority.High;
            o.ScheduledAt = Now.UtcDateTime.AddDays(3);
            o.Attributes["region"] = "south";
            o.Attributes["crew"] = "c-7";
        });

        ServiceOrderRecipientEdit.FixedFieldsChanged(order, replacement).Should().BeEmpty();
        Rules().EvaluateReplace(Principal("recipient", "crew"), order, replacement).Allowed.Should().BeTrue();
    }

    [Fact]
    public void Replace_BelowEdit_IsRefusedAsReadOnly_BeforeTheFieldsAreLookedAt()
    {
        var order = Order(TypeId(OrderRecipientAccess.Execute));

        var verdict = Rules().EvaluateReplace(Principal("recipient", "administrator"), order, Changing(order, "Status"));

        verdict.Reason.Should().Be(ServiceOrderRules.RecipientOrderIsReadOnlyReason);
    }

    [Fact]
    public void Replace_NullReplacement_Throws()
    {
        var order = Order(TypeId(OrderRecipientAccess.Edit));

        ((Action)(() => Rules().EvaluateReplace(Principal("owner", "administrator"), order, null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => ServiceOrderRecipientEdit.FixedFieldsChanged(null!, order))).Should().Throw<ArgumentNullException>();
        ((Action)(() => ServiceOrderRecipientEdit.FixedFieldsChanged(order, null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FixedFieldsChanged_ListsEveryChangedFieldInItsFixedOrder_AndRemovingAReservedAttributeIsAChange()
    {
        var order = Copy(Order(TypeId(OrderRecipientAccess.Edit)), o => o.Attributes["scopeId"] = "municipio-1");
        var replacement = Copy(order, o =>
        {
            o.ParentOrderId = "p";
            o.OrderTypeId = "other";
            o.Attributes.Remove("scopeId");
        });

        ServiceOrderRecipientEdit.FixedFieldsChanged(order, replacement).Should().Equal("OrderTypeId", "ParentOrderId", "Attributes.scopeId");
    }

    // ── Which level applies ───────────────────────────────────────────────────

    [Fact]
    public void RecipientAccessOf_IsTheTypesLevel_AndInformForAnUnknownTypeNoRegistryOrAnUndefinedValue()
    {
        var registry = Registry();
        registry.Register(new OrderType { Id = "undefined-level", DisplayName = "Undefined", RecipientAccess = (OrderRecipientAccess)7 });
        var rules = Rules(registry);

        rules.RecipientAccessOf(Order(TypeId(OrderRecipientAccess.Execute))).Should().Be(OrderRecipientAccess.Execute);
        rules.RecipientAccessOf(Order("RECIPIENT-EDIT")).Should().Be(OrderRecipientAccess.Edit, "the registry finds a type whatever its letter case");
        rules.RecipientAccessOf(Order("not-registered")).Should().Be(OrderRecipientAccess.Inform);
        rules.RecipientAccessOf(Order("undefined-level")).Should().Be(OrderRecipientAccess.Inform);
        new ServiceOrderRules(timeProvider: new FakeTimeProvider(Now)).RecipientAccessOf(Order(TypeId(OrderRecipientAccess.Edit)))
            .Should().Be(OrderRecipientAccess.Inform, "without a registry no type is known");
        ((Action)(() => rules.RecipientAccessOf(null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AnOrderTypeThatSaysNothing_OnlyInformsItsRecipients_AndSoDoTheTwoSharedUseTypes()
    {
        new OrderType { Id = "plain", DisplayName = "Plain" }.RecipientAccess.Should().Be(OrderRecipientAccess.Inform);
        SharedUseOrderTypes.CreatePoleWork(["pole"]).RecipientAccess.Should().Be(OrderRecipientAccess.Inform);
        SharedUseOrderTypes.CreateOccupantNotice(["pole"]).RecipientAccess.Should().Be(OrderRecipientAccess.Inform);
        ((int)OrderRecipientAccess.Inform, (int)OrderRecipientAccess.Execute, (int)OrderRecipientAccess.Edit)
            .Should().Be((0, 1, 2), "the row-level security functions compare the stored numbers");
    }

    [Theory]
    [InlineData(DispatchTargetType.User, UserId)]
    [InlineData(DispatchTargetType.Group, "crew-7")]
    public void ARecipientByUserOrGroup_IsHeldToTheSameLevels(DispatchTargetType targetType, string targetId)
    {
        var rules = Rules();
        var recipient = new WorkflowPrincipal(UserId, Occupant.ToString(), ["Supervisor"], ["crew-7"], []);
        var dispatch = new OrderDispatch(targetId, targetType, "dispatcher", Now.UtcDateTime);

        rules.Evaluate(recipient, OrderActionType.Annotate, Order(TypeId(OrderRecipientAccess.Execute), dispatch)).Allowed.Should().BeFalse();
        rules.EvaluateTargetWrite(recipient, Order(TypeId(OrderRecipientAccess.Execute), dispatch), OrderTargetWrite.RecordWork).Allowed.Should().BeTrue();
        rules.EvaluateTargetWrite(recipient, Order(TypeId(OrderRecipientAccess.Inform), dispatch), OrderTargetWrite.RecordWork).Allowed.Should().BeFalse();
        rules.Evaluate(recipient, OrderActionType.Annotate, Order(TypeId(OrderRecipientAccess.Edit), dispatch)).Allowed.Should().BeTrue();
    }

    [Fact]
    public void ARecipientThatAlsoHoldsAGrant_IsAGrantee_AndNotHeldToTheLevel()
    {
        var rules = Rules();
        var both = new WorkflowPrincipal(UserId, Occupant.ToString(), ["Supervisor"], [], [])
        {
            OrgGrants = [new WorkflowOrgGrant(Owner.ToString(), "ServiceOrder", ["serviceorders:view"], null)],
        };
        var order = Order(TypeId(OrderRecipientAccess.Inform));

        rules.IsDispatchRecipientOnly(both, order).Should().BeFalse();
        rules.Evaluate(both, OrderActionType.Annotate, order).Allowed.Should().BeTrue();
        rules.EvaluateTargetWrite(both, order, OrderTargetWrite.AddOrRemove).Allowed.Should().BeTrue();
    }
}

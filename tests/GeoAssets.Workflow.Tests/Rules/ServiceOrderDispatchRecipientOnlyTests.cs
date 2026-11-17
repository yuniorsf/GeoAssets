using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests.Rules;

/// <summary>
/// <see cref="ServiceOrderRules.IsDispatchRecipientOnly"/> (XD01-316): true exactly for a
/// principal that passes the organization gate through a dispatch and through nothing else. It
/// is what keeps the targets of an order read-only for the organization it was dispatched to,
/// while the rule chain still lets that organization view, annotate and accept the order.
/// </summary>
public sealed class ServiceOrderDispatchRecipientOnlyTests
{
    private const string UserId = "user-1";
    private const string GroupId = "crew-7";

    private static readonly Guid Owner = new("a0a0a0a0-0000-4000-8000-000000000316");
    private static readonly Guid Occupant = new("b0b0b0b0-0000-4000-8000-000000000316");
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

    private readonly ServiceOrderRules _rules = new(timeProvider: new FakeTimeProvider(Now));

    [Fact]
    public void RecipientOrganizationOfAnotherOrganizationsOrder_IsRecipientOnly_AndStillPassesTheGate()
    {
        var order = Order(Owner, new OrderDispatch(Occupant.ToString(), DispatchTargetType.Organization, "dispatcher", Now.UtcDateTime));
        var recipient = Principal(Occupant);

        _rules.IsDispatchRecipientOnly(recipient, order).Should().BeTrue();
        _rules.PassesOrganizationGate(recipient, order).Should().BeTrue();
        _rules.Evaluate(recipient, OrderActionType.View, order).Allowed.Should().BeTrue();
        _rules.Evaluate(recipient, OrderActionType.Annotate, order).Allowed
            .Should().BeTrue("the rules still let a recipient annotate the order; it is its targets that are read-only");
    }

    [Theory]
    [InlineData(DispatchTargetType.User, UserId)]
    [InlineData(DispatchTargetType.Group, GroupId)]
    public void RecipientByUserOrGroup_FromAnotherOrganization_IsRecipientOnlyToo(DispatchTargetType targetType, string targetId)
    {
        var order = Order(Owner, new OrderDispatch(targetId, targetType, "dispatcher", Now.UtcDateTime));

        _rules.IsDispatchRecipientOnly(Principal(Occupant), order).Should().BeTrue();
    }

    [Fact]
    public void RecipientOrganizationIdInAnotherLetterCase_IsStillTheRecipient()
    {
        var order = Order(Owner, new OrderDispatch(Occupant.ToString().ToUpperInvariant(), DispatchTargetType.Organization, "dispatcher", Now.UtcDateTime));

        _rules.IsDispatchRecipientOnly(Principal(Occupant), order).Should().BeTrue();
    }

    [Fact]
    public void MemberOfTheOrdersOrganization_IsNeverRecipientOnly_EvenWhenTheOrderIsDispatchedToIt()
    {
        var plain = Order(Owner);
        var dispatchedInside = Order(Owner, new OrderDispatch(UserId, DispatchTargetType.User, "dispatcher", Now.UtcDateTime));

        _rules.IsDispatchRecipientOnly(Principal(Owner), plain).Should().BeFalse();
        _rules.IsDispatchRecipientOnly(Principal(Owner), dispatchedInside).Should().BeFalse();
    }

    [Fact]
    public void RecipientThatAlsoHoldsAGrantCoveringTheOrder_IsNotRecipientOnly()
    {
        var order = Order(Owner, new OrderDispatch(Occupant.ToString(), DispatchTargetType.Organization, "dispatcher", Now.UtcDateTime));
        var grantee = Principal(Occupant) with
        {
            OrgGrants = [new WorkflowOrgGrant(Owner.ToString(), "ServiceOrder", ["serviceorders:annotate"], RequiredRole: null)],
        };

        _rules.IsDispatchRecipientOnly(grantee, order).Should().BeFalse("the grant reaches the order by itself");
    }

    [Fact]
    public void RecipientWhoseGrantHasExpired_IsRecipientOnlyAgain()
    {
        var order = Order(Owner, new OrderDispatch(Occupant.ToString(), DispatchTargetType.Organization, "dispatcher", Now.UtcDateTime));
        var expired = Principal(Occupant) with
        {
            OrgGrants =
            [
                new WorkflowOrgGrant(Owner.ToString(), "ServiceOrder", ["serviceorders:annotate"], RequiredRole: null) { ExpiresAt = Now.UtcDateTime },
            ],
        };

        _rules.IsDispatchRecipientOnly(expired, order).Should().BeTrue();
    }

    [Fact]
    public void GranteeWithoutAnyDispatch_AndAStrangerTheGateRefuses_AreNotRecipients()
    {
        var order = Order(Owner);
        var grantee = Principal(Occupant) with
        {
            OrgGrants = [new WorkflowOrgGrant(Owner.ToString(), null, ["serviceorders:view"], RequiredRole: null)],
        };

        _rules.IsDispatchRecipientOnly(grantee, order).Should().BeFalse();
        _rules.IsDispatchRecipientOnly(Principal(Occupant), order).Should().BeFalse("it does not reach the order at all");
        _rules.PassesOrganizationGate(Principal(Occupant), order).Should().BeFalse();
    }

    [Fact]
    public void DispatchToAThirdOrganization_MakesNobodyElseARecipient()
    {
        var third = new Guid("c0c0c0c0-0000-4000-8000-000000000316");
        var order = Order(Owner, new OrderDispatch(third.ToString(), DispatchTargetType.Organization, "dispatcher", Now.UtcDateTime));

        _rules.IsDispatchRecipientOnly(Principal(Occupant), order).Should().BeFalse();
        _rules.PassesOrganizationGate(Principal(Occupant), order).Should().BeFalse("a child dispatched to one occupant is not another occupant's to see");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void RecipientOfAnOrderWithNoOrganization_IsRecipientOnly_ExactlyWhenTheHostClosesSuchOrders(
        bool allowOrdersWithoutOrganization, bool expected)
    {
        var rules = new ServiceOrderRules(allowOrdersWithoutOrganization: allowOrdersWithoutOrganization, timeProvider: new FakeTimeProvider(Now));
        var order = Order(Guid.Empty, new OrderDispatch(Occupant.ToString(), DispatchTargetType.Organization, "dispatcher", Now.UtcDateTime));

        rules.IsDispatchRecipientOnly(Principal(Occupant), order).Should().Be(expected);
    }

    [Fact]
    public void AnonymousPrincipal_IsNotARecipient_AndANullPrincipalThrows()
    {
        var order = Order(Owner, new OrderDispatch(string.Empty, DispatchTargetType.User, "dispatcher", Now.UtcDateTime));

        _rules.IsDispatchRecipientOnly(WorkflowPrincipal.Anonymous, order).Should().BeFalse();
        ((Action)(() => _rules.IsDispatchRecipientOnly(null!, order))).Should().Throw<ArgumentNullException>();
    }

    private static WorkflowPrincipal Principal(Guid organizationId) =>
        new(UserId, organizationId.ToString(), ["Supervisor"], [GroupId], []);

    private static ServiceOrder Order(Guid organizationId, params OrderDispatch[] dispatches)
    {
        var order = new ServiceOrder
        {
            Id = "ord-1",
            Title = "Order",
            OrderTypeId = "inspection",
            CreatedBy = "planner-1",
            OrganizationId = organizationId,
        };
        order.Dispatches.AddRange(dispatches);
        return order;
    }
}

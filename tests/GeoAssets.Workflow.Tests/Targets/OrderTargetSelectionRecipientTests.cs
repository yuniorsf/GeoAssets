using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Workflow.Tests.Targets;

/// <summary>
/// XD01-316 — the targets of an order are read-only for the organization it was dispatched to:
/// the rules let such a recipient annotate the order, and
/// <see cref="OrderTargetSelectionService.ApplyAsync"/> still adds no target for it.
/// </summary>
public sealed class OrderTargetSelectionRecipientTests
{
    private static readonly WorkflowPrincipal Recipient =
        new("occupant-1", SelectionServiceFixture.OrgB.ToString(), ["Supervisor"], [], []);

    [Fact]
    public async Task Apply_ByARecipientOfTheOrdersDispatch_IsDenied_AndAddsNothing()
    {
        using var fixture = new SelectionServiceFixture(answer: SelectionServiceFixture.Poles(3));
        var order = await fixture.SeedOrderAsync();
        order.Dispatches.Add(new OrderDispatch(SelectionServiceFixture.OrgB.ToString(), DispatchTargetType.Organization, "admin-1", SelectionServiceFixture.Now.UtcDateTime));
        fixture.Rules.Evaluate(Recipient, OrderActionType.Annotate, order).Allowed
            .Should().BeTrue("the rule chain alone allows a recipient to annotate");

        var act = () => fixture.Sut.ApplyAsync(SelectionServiceFixture.FenceRequest(), Recipient, CancellationToken.None);

        var denied = (await act.Should().ThrowAsync<OrderActionDeniedException>()).Which;
        denied.Action.Should().Be(OrderActionType.Annotate);
        denied.Evaluation!.Reason.Should().Be(ServiceOrderRules.RecipientTargetsAreReadOnlyReason);
        denied.Evaluation.IsOutsideOrganization.Should().BeFalse();
        fixture.StoredFor().Should().BeEmpty();
        order.ActionLog.Should().BeEmpty();
    }

    [Fact]
    public async Task Preview_ByARecipient_IsStillAllowed_ItReadsAndWritesNothing()
    {
        using var fixture = new SelectionServiceFixture(answer: SelectionServiceFixture.Poles(2));
        var order = await fixture.SeedOrderAsync();
        order.Dispatches.Add(new OrderDispatch(SelectionServiceFixture.OrgB.ToString(), DispatchTargetType.Organization, "admin-1", SelectionServiceFixture.Now.UtcDateTime));

        var preview = await fixture.Sut.PreviewAsync(SelectionServiceFixture.FenceRequest(), Recipient, CancellationToken.None);

        preview.Matched.Should().Be(2);
        fixture.StoredFor().Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_ByAMemberTheOrderWasAlsoDispatchedTo_IsUnaffected()
    {
        using var fixture = new SelectionServiceFixture(answer: SelectionServiceFixture.Poles(2));
        var order = await fixture.SeedOrderAsync();
        order.Dispatches.Add(new OrderDispatch(SelectionServiceFixture.Admin.UserId, DispatchTargetType.User, "admin-1", SelectionServiceFixture.Now.UtcDateTime));

        var result = await fixture.Sut.ApplyAsync(SelectionServiceFixture.FenceRequest(), SelectionServiceFixture.Admin, CancellationToken.None);

        result.Added.Should().Be(2);
    }
}

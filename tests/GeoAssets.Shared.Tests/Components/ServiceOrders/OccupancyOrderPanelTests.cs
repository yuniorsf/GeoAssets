using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using GeoAssets.Core.Localization;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.UI.Components.ServiceOrders;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.ServiceOrders;

/// <summary>
/// bUnit coverage of the occupancy panel the NeutralOperator vertical contributes to the order detail (XD01-378): the
/// approval that commits staged occupancy (XD01-318) and the targets panel's <c>CanEdit</c>/<c>CanOpenOccupancy</c>
/// (XD01-315/316/333). The targets panel itself is stubbed; its parameters are what is asserted.
/// </summary>
public sealed class OccupancyOrderPanelTests : BunitContext
{
    private const string ExecuteRecipientTypeId = "execute-recipient";

    private static readonly Guid Owner = new("a0a0a0a0-0000-4000-8000-000000000378");
    private static readonly Guid Occupant = new("b0b0b0b0-0000-4000-8000-000000000378");
    private static readonly WorkflowPrincipal OwnerSupervisor = new("sup-1", Owner.ToString(), ["Supervisor"], [], []);
    private static readonly WorkflowPrincipal OccupantUser = new("occ-1", Occupant.ToString(), [], [], []);
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly RecordingOrderRepository _repository = new();
    private int _changed;

    private static ServiceOrder Order(
        string typeId = SharedUseOrderTypes.PoleWorkId, string status = ServiceOrderStatus.Completed, Guid? organization = null, OrderDispatch? dispatch = null)
    {
        var order = new ServiceOrder
        {
            Id = "ord-1",
            Title = "Pole work",
            OrderTypeId = typeId,
            Status = status,
            CreatedBy = "planner-1",
            OrganizationId = organization ?? Owner,
        };
        if (dispatch is not null) order.Dispatches.Add(dispatch);
        return order;
    }

    private IRenderedComponent<OccupancyOrderPanel> RenderPanel(ServiceOrder order, WorkflowPrincipal principal)
    {
        var registry = new OrderTypeRegistry();
        SharedUseOrderTypes.RegisterAll(registry);
        registry.Register(new OrderType { Id = "plain", DisplayName = "Plain" });
        registry.Register(new OrderType
        {
            Id = ExecuteRecipientTypeId,
            DisplayName = "Execute recipient",
            RecipientAccess = OrderRecipientAccess.Execute,
            Capabilities = [OccupancyCapabilities.Commit],
            TargetPolicy = new OrderTargetPolicy { TargetAssetTypeIds = ["pole"] },
        });

        Services.AddSingleton(registry);
        Services.AddSingleton(new ServiceOrderRules(registry));
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        Services.AddSingleton<IServiceOrderRepository>(_repository);
        Services.AddSingleton<IJsonStringLocalizer>(new KeyLocalizer());
        ComponentFactories.AddStub<OrderTargetOccupancyPanel>("<div class=\"targets-stub\"></div>");

        return Render<OccupancyOrderPanel>(p => p
            .Add(c => c.Order, order)
            .Add(c => c.Principal, principal)
            .Add(c => c.OnChanged, EventCallback.Factory.Create(this, () => _changed++)));
    }

    private static bool HasApproveButton(IRenderedComponent<OccupancyOrderPanel> cut) =>
        cut.FindAll("button").Any(b => b.TextContent.Contains("orderApproval.approve", StringComparison.Ordinal));

    private static IRenderedComponent<Stub<OrderTargetOccupancyPanel>> TargetsPanel(IRenderedComponent<OccupancyOrderPanel> cut) =>
        cut.FindComponent<Stub<OrderTargetOccupancyPanel>>();

    // ── Which orders show it ──────────────────────────────────────────────────

    [Theory]
    [InlineData(SharedUseOrderTypes.PoleWorkId)]
    [InlineData(SharedUseOrderTypes.OccupantNoticeId)]
    public void Panel_ATypeThatCommitsOccupancy_ShowsItsTitleAndTheTargetsPanel(string typeId)
    {
        var cut = RenderPanel(Order(typeId), OwnerSupervisor);

        cut.Markup.Should().Contain("occupancy.targets.title");
        TargetsPanel(cut).Instance.Parameters.Get(p => p.Order).Id.Should().Be("ord-1");
        TargetsPanel(cut).Instance.Parameters.Get(p => p.Principal).Should().BeSameAs(OwnerSupervisor);
    }

    [Fact]
    public void Panel_ATypeThatDoesNotCommitOccupancy_RendersNothing()
    {
        var cut = RenderPanel(Order("plain"), OwnerSupervisor);

        cut.Markup.Trim().Should().BeEmpty();
        cut.FindComponents<Stub<OrderTargetOccupancyPanel>>().Should().BeEmpty();
    }

    // ── The approve button ────────────────────────────────────────────────────

    [Fact]
    public void Approve_ACompletedOrder_ForASupervisorOfItsOrganization_IsVisibleAndEnabled()
    {
        var cut = RenderPanel(Order(), OwnerSupervisor);

        var button = cut.FindAll("button").Single(b => b.TextContent.Contains("orderApproval.approve", StringComparison.Ordinal));
        button.HasAttribute("disabled").Should().BeFalse();
        button.GetAttribute("aria-busy").Should().Be("false");
    }

    [Theory]
    [InlineData(ServiceOrderStatus.Draft)]
    [InlineData(ServiceOrderStatus.InProgress)]
    [InlineData(SharedUseOrderTypes.ApprovedStateKey)]
    public void Approve_AStatusWithNoApprovalTransition_IsHidden(string status)
    {
        var cut = RenderPanel(Order(status: status), OwnerSupervisor);

        HasApproveButton(cut).Should().BeFalse();
        TargetsPanel(cut).Should().NotBeNull("the targets still show");
    }

    [Fact]
    public void Approve_ForACallerOfAnotherOrganization_IsHidden()
    {
        var cut = RenderPanel(Order(), OwnerSupervisor with { OrganizationId = Occupant.ToString() });

        HasApproveButton(cut).Should().BeFalse();
    }

    [Fact]
    public void Approve_ForACallerWithoutTheApproveRole_IsHidden()
    {
        var cut = RenderPanel(Order(), OwnerSupervisor with { RoleNames = [] });

        HasApproveButton(cut).Should().BeFalse();
    }

    [Fact]
    public async Task Approve_Success_AppendsTheApprovalAndRaisesOnChanged()
    {
        var cut = RenderPanel(Order(), OwnerSupervisor);

        await cut.Find("button.btn-primary").ClickAsync();

        var (orderId, entry) = _repository.Actions.Should().ContainSingle().Subject;
        orderId.Should().Be("ord-1");
        entry.Action.Should().Be(OrderAction.Approve);
        entry.PerformedBy.Should().Be("sup-1");
        entry.PerformedAt.Should().Be(Now.UtcDateTime);
        entry.ResultingStatus.Should().Be(SharedUseOrderTypes.ApprovedStateKey);
        _changed.Should().Be(1);
        cut.FindAll(".alert-danger").Should().BeEmpty();
    }

    public static TheoryData<Exception, string> Failures => new()
    {
        { new OccupancyConflictException(["pole-1"]), "orderApproval.error.conflict" },
        { new ServiceOrderConcurrencyException("ord-1"), "orderApproval.error.concurrency" },
        { new InvalidServiceOrderTransitionException("Completed", "Approved"), "orderApproval.error.invalidState" },
        { new OrderActionDeniedException("ord-1", RuleEvaluationResult.Deny(OrderAction.Approve, "owner only")), "orderApproval.error.denied" },
        { new KeyNotFoundException("gone"), "orderApproval.error.notFound" },
        { new HttpRequestException("503"), "orderApproval.error.unexpected" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Approve_Failure_ShowsTheErrorKeyOfTheFailure_AndDoesNotRaiseOnChanged(Exception failure, string expectedKey)
    {
        _repository.ActionFailure = failure;
        var cut = RenderPanel(Order(), OwnerSupervisor);

        await cut.Find("button.btn-primary").ClickAsync();

        cut.Find(".alert-danger").TextContent.Should().Contain(expectedKey);
        _changed.Should().Be(0);
        cut.Find("button.btn-primary").HasAttribute("disabled").Should().BeFalse("the approval can be retried");
    }

    [Fact]
    public async Task Approve_AfterAFailure_ASuccessfulRetryClearsTheError()
    {
        _repository.ActionFailure = new ServiceOrderConcurrencyException("ord-1");
        var cut = RenderPanel(Order(), OwnerSupervisor);
        await cut.Find("button.btn-primary").ClickAsync();
        cut.FindAll(".alert-danger").Should().ContainSingle();

        _repository.ActionFailure = null;
        await cut.Find("button.btn-primary").ClickAsync();

        cut.FindAll(".alert-danger").Should().BeEmpty();
        _changed.Should().Be(1);
    }

    // ── CanEdit / CanOpenOccupancy ────────────────────────────────────────────

    [Fact]
    public void TargetsPanel_ForAMemberWhoMayAnnotate_CanEditAndOpenOccupancy()
    {
        var cut = RenderPanel(Order(), OwnerSupervisor);

        var targets = TargetsPanel(cut).Instance.Parameters;
        targets.Get(p => p.CanEdit).Should().BeTrue();
        targets.Get(p => p.CanOpenOccupancy).Should().BeTrue();
    }

    [Fact]
    public void TargetsPanel_ForAMemberWhoMayNotAnnotate_CannotEdit_ButStillOpensOccupancy()
    {
        var cut = RenderPanel(Order(), new WorkflowPrincipal("viewer-1", Owner.ToString(), [], [], []));

        var targets = TargetsPanel(cut).Instance.Parameters;
        targets.Get(p => p.CanEdit).Should().BeFalse();
        targets.Get(p => p.CanOpenOccupancy).Should().BeTrue();
    }

    [Fact]
    public void TargetsPanel_ForARecipientOfAnInformOnlyType_CannotEditNorOpenOccupancy()
    {
        var order = Order(SharedUseOrderTypes.OccupantNoticeId, ServiceOrderStatus.Pending,
            dispatch: new OrderDispatch(Occupant.ToString(), DispatchTargetType.Organization, "sup-1", Now.UtcDateTime));

        var cut = RenderPanel(order, OccupantUser);

        var targets = TargetsPanel(cut).Instance.Parameters;
        targets.Get(p => p.CanEdit).Should().BeFalse("an Inform type lets a recipient record nothing");
        targets.Get(p => p.CanOpenOccupancy).Should().BeFalse("the occupancy is the pole owner's, not the recipient's");
        HasApproveButton(cut).Should().BeFalse();
    }

    [Fact]
    public void TargetsPanel_ForARecipientOfAnExecuteType_CanEdit_ButNotOpenOccupancy()
    {
        var order = Order(ExecuteRecipientTypeId, ServiceOrderStatus.Pending,
            dispatch: new OrderDispatch(Occupant.ToString(), DispatchTargetType.Organization, "sup-1", Now.UtcDateTime));

        var cut = RenderPanel(order, OccupantUser);

        var targets = TargetsPanel(cut).Instance.Parameters;
        targets.Get(p => p.CanEdit).Should().BeTrue("an Execute type lets a recipient record the work (XD01-333)");
        targets.Get(p => p.CanOpenOccupancy).Should().BeFalse();
    }
}

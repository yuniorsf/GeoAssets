using Bunit;
using FluentAssertions;
using GeoAssets.Core.Localization;
using GeoAssets.Shared.Components.ServiceOrders;
using GeoAssets.Shared.Services.OrderDetail;
using GeoAssets.Shared.Tests.Services.OrderDetail;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.ServiceOrders;

/// <summary>
/// bUnit coverage of the generic order detail's extension points (XD01-378): the panels and the dispatch dialog a
/// vertical registers are rendered through <c>DynamicComponent</c> with the parameters the registry documents; with none
/// registered the detail is the generic one.
/// </summary>
public sealed class ServiceOrderDetailTests : BunitContext
{
    private const string PanelCapability = "panel-capability";
    private const string PanelTypeId = "panel-type";
    private const string DialogTypeId = "dialog-type";
    private const string CommitCapability = "occupancy-commit";

    private static readonly Guid Organization = new("a0a0a0a0-0000-4000-8000-000000000378");
    private static readonly WorkflowPrincipal Supervisor = new("sup-1", Organization.ToString(), ["Supervisor"], [], []);

    private static ServiceOrder Order(string typeId) => new()
    {
        Id = "ord-1",
        Title = "Order one",
        OrderTypeId = typeId,
        Status = ServiceOrderStatus.Draft,
        CreatedBy = "planner-1",
        OrganizationId = Organization,
    };

    private void Arrange(Action<IServiceCollection>? register = null)
    {
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType { Id = PanelTypeId, DisplayName = "Panel type", Capabilities = [PanelCapability, CommitCapability] });
        registry.Register(new OrderType { Id = DialogTypeId, DisplayName = "Dialog type" });
        registry.Register(new OrderType { Id = "plain", DisplayName = "Plain" });

        Services.AddSingleton(registry);
        Services.AddSingleton(new ServiceOrderRules(registry));
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<IServiceOrderRepository>(new RecordingOrderRepository());
        Services.AddSingleton<IJsonStringLocalizer>(new KeyLocalizer());
        Services.AddOrderDetailPanels();
        register?.Invoke(Services);
    }

    private int _changed;

    private IRenderedComponent<ServiceOrderDetail> RenderDetail(string typeId) =>
        Render<ServiceOrderDetail>(p => p
            .Add(c => c.Order, Order(typeId))
            .Add(c => c.Principal, Supervisor)
            .Add(c => c.OnChanged, EventCallback.Factory.Create(this, () => _changed++)));

    [Fact]
    public void Detail_ARegisteredPanelOfTheTypesCapability_IsRenderedWithOrderPrincipalAndOnChanged()
    {
        Arrange(s => s.AddOrderDetailPanel<FakeOrderPanel>(PanelCapability));

        var cut = RenderDetail(PanelTypeId);

        var panel = cut.FindComponent<FakeOrderPanel>();
        panel.Instance.Order!.Id.Should().Be("ord-1");
        panel.Instance.Principal.Should().BeSameAs(Supervisor);
        panel.Instance.OnChanged.HasDelegate.Should().BeTrue();
        cut.Find(".fake-order-panel").TextContent.Should().Be("ord-1");
    }

    [Fact]
    public async Task Detail_APanelsOnChanged_ReachesTheDetailsOwnCallback()
    {
        Arrange(s => s.AddOrderDetailPanel<FakeOrderPanel>(PanelCapability));
        var cut = RenderDetail(PanelTypeId);
        var panel = cut.FindComponent<FakeOrderPanel>();

        await cut.InvokeAsync(() => panel.Instance.OnChanged.InvokeAsync());

        _changed.Should().Be(1);
    }

    [Fact]
    public void Detail_PanelsAreRenderedInRegistrationOrder_AndOnlyForTheTypesCapabilities()
    {
        Arrange(s => s
            .AddOrderDetailPanel<FakeSecondOrderPanel>(PanelCapability)
            .AddOrderDetailPanel<FakeOrderPanel>(PanelCapability)
            .AddOrderDetailPanel<FakeOrderPanel>("some-other-capability"));

        var cut = RenderDetail(PanelTypeId);

        cut.FindComponents<FakeOrderPanel>().Select(p => p.Instance.GetType()).Should().Equal(typeof(FakeSecondOrderPanel), typeof(FakeOrderPanel));
    }

    [Fact]
    public void Detail_NoRegistration_RendersNoPanel_AndTheGenericDispatchDialog()
    {
        Arrange();

        var cut = RenderDetail(PanelTypeId);

        cut.FindComponents<FakeOrderPanel>().Should().BeEmpty();
        cut.FindComponents<ServiceOrderDispatchDialog>().Should().ContainSingle();
        cut.FindComponents<FakeDispatchDialog>().Should().BeEmpty();
    }

    [Fact]
    public void Detail_APanelRegisteredForAnotherCapability_IsNotRendered_ForATypeWithoutIt()
    {
        Arrange(s => s.AddOrderDetailPanel<FakeOrderPanel>(PanelCapability));

        var cut = RenderDetail("plain");

        cut.FindComponents<FakeOrderPanel>().Should().BeEmpty();
    }

    [Fact]
    public void Detail_ARegisteredDialogWhosePredicateMatches_ReplacesTheGenericDispatchDialog()
    {
        Arrange(s => s.AddOrderDispatchDialog<FakeDispatchDialog>(t => string.Equals(t.Id, DialogTypeId, StringComparison.Ordinal)));

        var cut = RenderDetail(DialogTypeId);

        var dialog = cut.FindComponent<FakeDispatchDialog>();
        dialog.Instance.Order!.Id.Should().Be("ord-1");
        dialog.Instance.Principal.Should().BeSameAs(Supervisor);
        dialog.Instance.Visible.Should().BeFalse();
        cut.FindComponents<ServiceOrderDispatchDialog>().Should().BeEmpty();
    }

    [Fact]
    public void Detail_ARegisteredDialogWhosePredicateDoesNotMatch_LeavesTheGenericDispatchDialog()
    {
        Arrange(s => s.AddOrderDispatchDialog<FakeDispatchDialog>(t => string.Equals(t.Id, DialogTypeId, StringComparison.Ordinal)));

        var cut = RenderDetail("plain");

        cut.FindComponents<FakeDispatchDialog>().Should().BeEmpty();
        cut.FindComponents<ServiceOrderDispatchDialog>().Should().ContainSingle();
    }

    [Fact]
    public async Task Detail_TheDispatchButtonShowsTheRegisteredDialog_AndItsCallbacksReachTheDetail()
    {
        Arrange(s => s.AddOrderDispatchDialog<FakeDispatchDialog>(_ => true));
        var cut = RenderDetail(DialogTypeId);
        var dialog = cut.FindComponent<FakeDispatchDialog>();

        await cut.FindAll("button").Single(b => b.TextContent.Contains("orders.dispatch", StringComparison.Ordinal)).ClickAsync();
        dialog.Instance.Visible.Should().BeTrue();

        await cut.InvokeAsync(() => dialog.Instance.OnDispatched.InvokeAsync());
        _changed.Should().Be(1, "a dispatch reloads the order");

        await cut.InvokeAsync(() => dialog.Instance.OnCancel.InvokeAsync());
        dialog.Instance.Visible.Should().BeFalse();
    }

    [Fact]
    public void Detail_HasNoApproveButton_EvenForATypeThatCommitsOccupancy()
    {
        Arrange();

        var cut = RenderDetail(PanelTypeId);

        cut.Markup.Should().NotContain("orderApproval.approve");
        cut.FindAll("button").Select(b => b.TextContent.Trim()).Should().NotContain("orderApproval.approve");
    }
}

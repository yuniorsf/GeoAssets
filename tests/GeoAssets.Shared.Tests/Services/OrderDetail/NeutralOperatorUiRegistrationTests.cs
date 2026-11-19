using FluentAssertions;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.UI;
using GeoAssets.NeutralOperator.UI.Components.ServiceOrders;
using GeoAssets.Shared.Services.OrderDetail;
using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.OrderDetail;

/// <summary><c>AddNeutralOperatorUi()</c> contributes the vertical's order-detail components to the generic registry (XD01-378).</summary>
public sealed class NeutralOperatorUiRegistrationTests
{
    private static readonly OrderType PoleWork = SharedUseOrderTypes.CreatePoleWork();
    private static readonly OrderType OccupantNotice = SharedUseOrderTypes.CreateOccupantNotice();
    private static readonly OrderType Plain = new() { Id = "plain", DisplayName = "Plain" };

    private static IOrderDetailPanelRegistry Resolve()
    {
        var services = new ServiceCollection().AddNeutralOperatorUi();
        return services.BuildServiceProvider().GetRequiredService<IOrderDetailPanelRegistry>();
    }

    [Fact]
    public void AddNeutralOperatorUi_ContributesTheOccupancyPanel_ToTheSharedUseTypes()
    {
        var registry = Resolve();

        registry.PanelsFor(PoleWork).Should().Equal(typeof(OccupancyOrderPanel));
        registry.PanelsFor(OccupantNotice).Should().Equal(typeof(OccupancyOrderPanel));
    }

    [Fact]
    public void AddNeutralOperatorUi_ContributesTheSharedUseDispatchDialog_ToAFanOutTypeOnly()
    {
        var registry = Resolve();

        registry.DispatchDialogFor(PoleWork).Should().Be<SharedUseDispatchDialog>();
        registry.DispatchDialogFor(OccupantNotice).Should().BeNull("a notice does not fan out");
    }

    [Fact]
    public void AddNeutralOperatorUi_APlainType_GetsNeitherThePanelNorTheDialog()
    {
        var registry = Resolve();

        registry.PanelsFor(Plain).Should().BeEmpty();
        registry.DispatchDialogFor(Plain).Should().BeNull();
        registry.PanelsFor(null).Should().BeEmpty();
        registry.DispatchDialogFor(null).Should().BeNull();
    }

    [Fact]
    public void AddNeutralOperatorUi_CalledTwice_StillResolvesOneRegistry()
    {
        var services = new ServiceCollection().AddNeutralOperatorUi();

        services.Count(d => d.ServiceType == typeof(IOrderDetailPanelRegistry)).Should().Be(1);
    }

    [Fact]
    public void AddNeutralOperatorUi_RejectsANullServiceCollection()
    {
        ((Action)(() => NeutralOperatorUiServiceExtensions.AddNeutralOperatorUi(null!))).Should().Throw<ArgumentNullException>();
    }
}

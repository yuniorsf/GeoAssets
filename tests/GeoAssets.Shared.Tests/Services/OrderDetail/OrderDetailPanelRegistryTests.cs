using FluentAssertions;
using GeoAssets.Shared.Services.OrderDetail;
using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.OrderDetail;

/// <summary>The registry a vertical contributes its order-detail panels and dispatch dialog to (XD01-378).</summary>
public sealed class OrderDetailPanelRegistryTests
{
    private const string Inspect = "inspect";
    private const string Bill = "bill";

    private static OrderType Type(params string[] capabilities) => new() { Id = "t", DisplayName = "T", Capabilities = [.. capabilities] };

    private static IOrderDetailPanelRegistry Resolve(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        return services.BuildServiceProvider().GetRequiredService<IOrderDetailPanelRegistry>();
    }

    [Fact]
    public void PanelsFor_ReturnsThePanelsOfTheTypesCapabilities_InRegistrationOrder()
    {
        var registry = Resolve(s => s
            .AddOrderDetailPanel<FakeSecondOrderPanel>(Bill)
            .AddOrderDetailPanel<FakeOrderPanel>(Inspect)
            .AddOrderDetailPanel<FakeSecondOrderPanel>(Inspect));

        registry.PanelsFor(Type(Inspect, Bill)).Should().Equal(typeof(FakeSecondOrderPanel), typeof(FakeOrderPanel), typeof(FakeSecondOrderPanel));
        registry.PanelsFor(Type(Inspect)).Should().Equal(typeof(FakeOrderPanel), typeof(FakeSecondOrderPanel));
        registry.PanelsFor(Type(Bill)).Should().Equal(typeof(FakeSecondOrderPanel));
    }

    [Fact]
    public void PanelsFor_NullType_IsEmpty()
    {
        var registry = Resolve(s => s.AddOrderDetailPanel<FakeOrderPanel>(Inspect));

        registry.PanelsFor(null).Should().BeEmpty();
    }

    [Fact]
    public void PanelsFor_TypeWithNoMatchingCapability_IsEmpty()
    {
        var registry = Resolve(s => s.AddOrderDetailPanel<FakeOrderPanel>(Inspect));

        registry.PanelsFor(Type(Bill)).Should().BeEmpty();
        registry.PanelsFor(Type()).Should().BeEmpty();
    }

    [Fact]
    public void PanelsFor_ComparesCapabilitiesOrdinally()
    {
        var registry = Resolve(s => s.AddOrderDetailPanel<FakeOrderPanel>(Inspect));

        registry.PanelsFor(Type("INSPECT")).Should().BeEmpty();
    }

    [Fact]
    public void DispatchDialogFor_TheFirstRegisteredDialogThatAppliesWins()
    {
        var registry = Resolve(s => s
            .AddOrderDispatchDialog<FakeDispatchDialog>(t => t.HasCapability(Bill))
            .AddOrderDispatchDialog<FakeSecondDispatchDialog>(t => t.HasCapability(Inspect))
            .AddOrderDispatchDialog<FakeDispatchDialog>(_ => true));

        registry.DispatchDialogFor(Type(Inspect)).Should().Be<FakeSecondDispatchDialog>();
        registry.DispatchDialogFor(Type(Inspect, Bill)).Should().Be<FakeDispatchDialog>();
        registry.DispatchDialogFor(Type()).Should().Be<FakeDispatchDialog>("the catch-all registered last still applies");
    }

    [Fact]
    public void DispatchDialogFor_NoDialogApplies_IsNull()
    {
        var registry = Resolve(s => s.AddOrderDispatchDialog<FakeDispatchDialog>(t => t.HasCapability(Bill)));

        registry.DispatchDialogFor(Type(Inspect)).Should().BeNull();
    }

    [Fact]
    public void DispatchDialogFor_NullType_IsNull_WithoutAskingThePredicate()
    {
        var asked = false;
        var registry = Resolve(s => s.AddOrderDispatchDialog<FakeDispatchDialog>(_ => asked = true));

        registry.DispatchDialogFor(null).Should().BeNull();
        asked.Should().BeFalse();
    }

    [Fact]
    public void AddOrderDetailPanels_Alone_ResolvesAnEmptyRegistry()
    {
        var registry = Resolve(s => s.AddOrderDetailPanels());

        registry.PanelsFor(Type(Inspect)).Should().BeEmpty();
        registry.DispatchDialogFor(Type(Inspect)).Should().BeNull();
    }

    [Fact]
    public void AddOrderDetailPanels_CalledTwice_RegistersTheRegistryOnce()
    {
        var services = new ServiceCollection();

        services.AddOrderDetailPanels().AddOrderDetailPanels();

        services.Count(d => d.ServiceType == typeof(IOrderDetailPanelRegistry)).Should().Be(1);
    }

    [Fact]
    public void AddOrderDetailPanel_ImpliesTheRegistry_AndAddingTheRegistryAfterwardsKeepsThePanels()
    {
        var services = new ServiceCollection();
        services.AddOrderDetailPanel<FakeOrderPanel>(Inspect);
        services.AddOrderDetailPanels();
        var registry = services.BuildServiceProvider().GetRequiredService<IOrderDetailPanelRegistry>();

        services.Count(d => d.ServiceType == typeof(IOrderDetailPanelRegistry)).Should().Be(1);
        registry.PanelsFor(Type(Inspect)).Should().Equal(typeof(FakeOrderPanel));
    }

    [Fact]
    public void AddOrderDispatchDialog_ImpliesTheRegistry()
    {
        var registry = Resolve(s => s.AddOrderDispatchDialog<FakeDispatchDialog>(_ => true));

        registry.DispatchDialogFor(Type()).Should().Be<FakeDispatchDialog>();
    }

    [Fact]
    public void Registration_RejectsANullServiceCollection_ABlankCapability_AndANullPredicate()
    {
        var services = new ServiceCollection();

        ((Action)(() => OrderDetailPanelServiceExtensions.AddOrderDetailPanels(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => services.AddOrderDetailPanel<FakeOrderPanel>(" "))).Should().Throw<ArgumentException>();
        ((Action)(() => services.AddOrderDispatchDialog<FakeDispatchDialog>(null!))).Should().Throw<ArgumentNullException>();
    }
}

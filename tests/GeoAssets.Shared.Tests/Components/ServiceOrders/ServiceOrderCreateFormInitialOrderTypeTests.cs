using Bunit;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Localization;
using GeoAssets.Shared.Components.ServiceOrders;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.ServiceOrders;

/// <summary>bUnit coverage of <see cref="ServiceOrderCreateForm.InitialOrderTypeId"/> (XD01-370).</summary>
public sealed class ServiceOrderCreateFormInitialOrderTypeTests : BunitContext
{
    private static readonly WorkflowPrincipal Principal = new("user-1", null, [], [], []);

    private sealed class KeyLocalizer : IJsonStringLocalizer
    {
        public string this[string key] => key;
        public string GetString(string key, params object[] args) => key;
        public event EventHandler LocalizationChanged { add { } remove { } }
    }

    private void Arrange()
    {
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType { Id = "inspection", DisplayName = "Inspection" });
        registry.Register(new OrderType { Id = "shared-use-pole-work", DisplayName = "Shared-use pole work" });

        var selection = new MapSelectionSet();
        Services.AddSingleton(registry);
        Services.AddSingleton(new ServiceOrderRules(registry));
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<IMapSelectionSet>(selection);
        Services.AddSingleton(new MapSelectionFenceMemory(selection));
        Services.AddSingleton<IAssetProvider>(new TestAssetProvider());
        Services.AddSingleton<IServiceOrderRepository>(new UnusedOrderRepository());
        Services.AddSingleton<IMapOrderFocus>(new UnusedMapOrderFocus());
        Services.AddSingleton<IJsonStringLocalizer>(new KeyLocalizer());
        Services.AddSingleton(NullLogger<ServiceOrderCreateForm>.Instance);
    }

    private IRenderedComponent<ServiceOrderCreateForm> RenderForm(string? initialOrderTypeId)
    {
        Arrange();
        return Render<ServiceOrderCreateForm>(p => p
            .Add(c => c.Principal, Principal)
            .Add(c => c.InitialOrderTypeId, initialOrderTypeId));
    }

    private static string SelectedType(IRenderedComponent<ServiceOrderCreateForm> cut) =>
        cut.Find("#order-type-select").GetAttribute("value") ?? string.Empty;

    [Fact]
    public void Form_InitialOrderTypeOffered_PreselectsIt()
    {
        var cut = RenderForm("shared-use-pole-work");

        SelectedType(cut).Should().Be("shared-use-pole-work");
    }

    [Theory]
    [InlineData("not-offered")]
    [InlineData("")]
    [InlineData(null)]
    public void Form_InitialOrderTypeNotOfferedOrAbsent_FallsBackToTheFirstOfferedType(string? initial)
    {
        var cut = RenderForm(initial);

        SelectedType(cut).Should().Be("inspection");
    }

    [Fact]
    public void Form_InitialOrderTypeChangedAfterFirstRender_IsIgnored()
    {
        var cut = RenderForm("shared-use-pole-work");

        cut.Render(p => p.Add(c => c.InitialOrderTypeId, "inspection"));

        SelectedType(cut).Should().Be("shared-use-pole-work");
    }
}

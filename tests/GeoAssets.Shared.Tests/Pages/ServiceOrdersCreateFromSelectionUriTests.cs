using FluentAssertions;
using GeoAssets.Shared.Pages;
using Xunit;

namespace GeoAssets.Shared.Tests.Pages;

/// <summary>Covers <see cref="ServiceOrders.CreateFromSelectionForOrderType"/> (XD01-370).</summary>
public class ServiceOrdersCreateFromSelectionUriTests
{
    [Fact]
    public void CreateFromSelectionForOrderType_AppendsTheOrderTypeToTheFromSelectionAddress() =>
        ServiceOrders.CreateFromSelectionForOrderType("shared-use-pole-work")
            .Should().Be(ServiceOrders.CreateFromSelectionUri + "&orderTypeId=shared-use-pole-work");

    [Fact]
    public void CreateFromSelectionForOrderType_EscapesTheOrderTypeId() =>
        ServiceOrders.CreateFromSelectionForOrderType("a b&c=d/é")
            .Should().Be(ServiceOrders.CreateFromSelectionUri + "&orderTypeId=" + Uri.EscapeDataString("a b&c=d/é"))
            .And.Contain("a%20b%26c%3Dd%2F");
}

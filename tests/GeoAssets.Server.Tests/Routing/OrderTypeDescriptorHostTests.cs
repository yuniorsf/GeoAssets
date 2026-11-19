using FluentAssertions;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Registry;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Routing;

/// <summary>
/// Acceptance for XD01-369: the real <c>Program</c> registers the kernel's order-type descriptor registry, and the
/// NeutralOperator module publishes its descriptor into it at bootstrap.
/// </summary>
[Trait("Category", "RouteInventory")]
public sealed class OrderTypeDescriptorHostTests
{
    [Fact]
    public async Task ServerHost_Registry_ContainsTheNeutralOperatorDescriptor()
    {
        var services = await ServerHostFixture.GetServicesAsync();

        var descriptors = services.GetRequiredService<IOrderTypeDescriptorRegistry>().GetAll();

        var descriptor = descriptors.Should().ContainSingle(d => d.VerticalId == "neutral-operator").Subject;
        descriptor.OrderTypeId.Should().Be(SharedUseOrderTypes.PoleWorkId);
        descriptor.FeatureActions.Should().ContainSingle(a => a.Role == "occupiable-support" && a.OrderTypeId == SharedUseOrderTypes.PoleWorkId);
    }

    [Fact]
    public async Task NeutralOperatorDisabledHost_Registry_HasNoDescriptors()
    {
        var services = await ServerHostFixture.GetNeutralOperatorDisabledServicesAsync();

        services.GetRequiredService<IOrderTypeDescriptorRegistry>().GetAll().Should().BeEmpty();
    }
}

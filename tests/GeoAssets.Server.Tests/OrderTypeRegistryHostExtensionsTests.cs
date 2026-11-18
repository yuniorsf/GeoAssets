using FluentAssertions;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>How a module adds its order types to the host's registry (XD01-358).</summary>
public sealed class OrderTypeRegistryHostExtensionsTests
{
    [Fact]
    public void ConfigureOrderTypeRegistry_AppliesToTheRegisteredInstance()
    {
        var services = new ServiceCollection();
        services.AddOrderTypeRegistry();
        OrderTypeRegistry? seen = null;

        var returned = services.ConfigureOrderTypeRegistry(registry => seen = registry);

        returned.Should().BeSameAs(services);
        seen.Should().NotBeNull().And.BeSameAs(services.BuildServiceProvider().GetRequiredService<OrderTypeRegistry>());
    }

    [Fact]
    public void ConfigureOrderTypeRegistry_WithoutRegistry_Throws()
    {
        var act = () => new ServiceCollection().ConfigureOrderTypeRegistry(_ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*AddOrderTypeRegistry*");
    }

    [Fact]
    public void ConfigureOrderTypeRegistry_NullArguments_Throw()
    {
        var services = new ServiceCollection();

        var nullServices = () => OrderTypeRegistryHostExtensions.ConfigureOrderTypeRegistry(null!, _ => { });
        var nullConfigure = () => services.ConfigureOrderTypeRegistry(null!);

        nullServices.Should().Throw<ArgumentNullException>();
        nullConfigure.Should().Throw<ArgumentNullException>();
    }
}

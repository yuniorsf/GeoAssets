using FluentAssertions;
using GeoAssets.Workflow.Registry;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GeoAssets.Workflow.Tests.Registry.RegistryTestData;

namespace GeoAssets.Workflow.Tests.Registry;

public sealed class OrderTypeDescriptorServiceExtensionsTests
{
    [Fact]
    public void AddOrderTypeDescriptorRegistry_ResolvesASingletonInMemoryRegistry()
    {
        var services = new ServiceCollection();
        services.AddOrderTypeDescriptorRegistry();
        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<IOrderTypeDescriptorRegistry>();

        registry.Should().BeOfType<InMemoryOrderTypeDescriptorRegistry>().And.BeSameAs(sp.GetRequiredService<IOrderTypeDescriptorRegistry>());
        registry.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void AddOrderTypeDescriptorRegistry_BuildsFromEveryRegisteredSource()
    {
        var services = new ServiceCollection();
        services.AddOrderTypeDescriptors("vertical-b", [Descriptor("vertical-b", "b-1")]);
        services.AddOrderTypeDescriptorRegistry();
        services.AddOrderTypeDescriptors("vertical-a", [Descriptor("vertical-a", "a-1")]);
        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IOrderTypeDescriptorRegistry>().GetAll().Select(d => d.OrderTypeId).Should().Equal("a-1", "b-1");
    }

    [Fact]
    public void AddOrderTypeDescriptorRegistry_CalledTwice_KeepsTheFirstRegistration()
    {
        var services = new ServiceCollection();

        services.AddOrderTypeDescriptorRegistry();
        services.AddOrderTypeDescriptorRegistry();

        services.Should().ContainSingle(d => d.ServiceType == typeof(IOrderTypeDescriptorRegistry));
    }

    [Fact]
    public void AddOrderTypeDescriptorRegistry_NullServices_Throws()
    {
        var act = () => OrderTypeDescriptorServiceExtensions.AddOrderTypeDescriptorRegistry(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddOrderTypeDescriptors_RegistersASourceForTheVertical()
    {
        var services = new ServiceCollection();
        var descriptors = new[] { Descriptor() };

        services.AddOrderTypeDescriptors("vertical-a", descriptors);

        using var sp = services.BuildServiceProvider();
        var source = sp.GetRequiredService<OrderTypeDescriptorSource>();
        source.VerticalId.Should().Be("vertical-a");
        source.Descriptors.Should().Equal(descriptors);
    }

    [Fact]
    public void AddOrderTypeDescriptors_BlankVerticalId_Throws()
    {
        var act = () => new ServiceCollection().AddOrderTypeDescriptors(" ", []);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddOrderTypeDescriptors_NullDescriptors_Throws()
    {
        var act = () => new ServiceCollection().AddOrderTypeDescriptors("vertical-a", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddOrderTypeDescriptors_NullServices_Throws()
    {
        var act = () => OrderTypeDescriptorServiceExtensions.AddOrderTypeDescriptors(null!, "vertical-a", []);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddOrderTypeDescriptors_InvalidSet_ThrowsWhenTheRegistryIsCreated()
    {
        var services = new ServiceCollection();
        services.AddOrderTypeDescriptors("vertical-a", [Descriptor("vertical-b")]);
        services.AddOrderTypeDescriptorRegistry();
        using var sp = services.BuildServiceProvider();

        var act = () => sp.GetRequiredService<IOrderTypeDescriptorRegistry>();

        act.Should().Throw<ArgumentException>();
    }
}

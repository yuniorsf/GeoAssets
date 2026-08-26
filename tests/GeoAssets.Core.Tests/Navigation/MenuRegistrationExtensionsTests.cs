using System.Reflection;
using FluentAssertions;
using GeoAssets.Core.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Core.Tests.Navigation;

public class MenuRegistrationExtensionsTests
{
    private static readonly Assembly ThisAssembly = typeof(MenuRegistrationExtensionsTests).Assembly;

    private sealed class DummyPageA : MenuPageItem
    {
        public override string Id => "dummy-a";
        public override string LabelKey => "label.dummy-a";
        public override string RouteHref => "dummy-a";
    }

    private sealed class DummyGroupB : MenuGroupItem
    {
        public override string Id => "dummy-b";
        public override string LabelKey => "label.dummy-b";
    }

    // Never discovered — proves the abstract-type filter, not just that this specific type
    // happens to be unused.
    private abstract class AbstractDummyItem : MenuLeafItem
    {
        public override string Id => "abstract-dummy";
        public override string LabelKey => "label.abstract-dummy";
    }

    private sealed class DuplicateIdItemOne : MenuSectionItem
    {
        public override string Id => "duplicate-id";
        public override string LabelKey => "label.dup-1";
    }

    private sealed class DuplicateIdItemTwo : MenuSectionItem
    {
        public override string Id => "duplicate-id";
        public override string LabelKey => "label.dup-2";
    }

    [Fact]
    public void AddGeoAssetsNavigation_RegistersConcreteMenuItemTypes_ButNotAbstractOnes()
    {
        var services = new ServiceCollection();

        services.AddGeoAssetsNavigation(ThisAssembly);

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(DummyPageA));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(DummyGroupB));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(AbstractDummyItem));
    }

    [Fact]
    public void AddGeoAssetsNavigation_RegistersEachDiscoveredTypeAlsoAsMenuItemBase()
    {
        var services = new ServiceCollection();
        services.AddGeoAssetsNavigation(ThisAssembly);
        using var provider = services.BuildServiceProvider();

        var items = provider.GetServices<MenuItemBase>().ToList();

        items.Should().Contain(item => item.Id == "dummy-a");
        items.Should().Contain(item => item.Id == "dummy-b");
    }

    [Fact]
    public void AddGeoAssetsNavigation_DuplicateId_ThrowsWhenRegistryIsResolved()
    {
        // Fails without the fix: without duplicate detection, MenuRegistry would silently
        // collect both same-Id items instead of failing fast on this authoring bug.
        var services = new ServiceCollection();
        services.AddGeoAssetsNavigation(ThisAssembly);
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<MenuRegistry>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*duplicate-id*");
    }
}

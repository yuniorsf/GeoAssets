using FluentAssertions;
using GeoAssets.Core.Navigation;
using GeoAssets.IpBilling.UI.Navigation;
using GeoAssets.NeutralOperator.UI.Navigation;
using GeoAssets.Shared.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Shared.Tests.Navigation;

/// <summary>
/// The menu items of the vertical UI libraries (XD01-378) are found when a host scans their assemblies, and sit under
/// the shell's own groups; the shell assembly holds none of them.
/// </summary>
public sealed class VerticalMenuItemsDiscoveryTests
{
    private static readonly string[] IpBillingIds =
    [
        "admin-ip-billing-municipios", "admin-ip-billing-distribuidoras", "admin-ip-billing-levantamentos",
        "admin-ip-billing-fiscalizacoes", "admin-ip-billing-oficios", "admin-ip-billing-faturamento", "admin-ip-billing-lamp-reference",
    ];

    private static MenuRegistry Scan()
    {
        var services = new ServiceCollection().AddGeoAssetsNavigation(
            typeof(OverviewMenuItem).Assembly,
            typeof(IspTenantPortalMenuItem).Assembly,
            typeof(AdminIpBillingMunicipiosMenuItem).Assembly);
        return services.BuildServiceProvider().GetRequiredService<MenuRegistry>();
    }

    [Fact]
    public void Scan_FindsTheTenantPortalAndEveryIpBillingMenuItem()
    {
        var ids = Scan().All.Select(i => i.Id).ToList();

        ids.Should().Contain("isp-tenant-portal").And.Contain(IpBillingIds);
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Scan_EveryItemsParentIsAnItemOfTheRegistry()
    {
        var registry = Scan();
        var ids = registry.All.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);

        registry.All.Where(i => i.ParentId is not null && !ids.Contains(i.ParentId)).Select(i => i.Id).Should().BeEmpty();
        var tree = MenuTreeBuilder.Build(registry.All);
        tree.Should().NotBeEmpty();
    }

    [Fact]
    public void Scan_WithoutTheVerticalAssemblies_FindsNoneOfThem()
    {
        var services = new ServiceCollection().AddGeoAssetsNavigation(typeof(OverviewMenuItem).Assembly);

        var ids = services.BuildServiceProvider().GetRequiredService<MenuRegistry>().All.Select(i => i.Id).ToList();

        ids.Should().NotContain("isp-tenant-portal").And.NotContain(IpBillingIds);
    }

    [Fact]
    public void TheShellAssembly_HoldsNoVerticalMenuItem()
    {
        var shellItems = typeof(OverviewMenuItem).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(MenuItemBase).IsAssignableFrom(t))
            .Select(t => t.Name);

        shellItems.Should().NotContain(name => name.Contains("IpBilling", StringComparison.Ordinal) || name.Contains("IspTenantPortal", StringComparison.Ordinal));
    }
}

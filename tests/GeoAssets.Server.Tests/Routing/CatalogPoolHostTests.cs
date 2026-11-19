using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Providers;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Provider.PostgreSQL.Repositories;
using GeoAssets.Server.Tests.Catalogs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Routing;

/// <summary>
/// Acceptance for XD01-371: the real <c>Program</c> builds its asset catalogs from <c>GeoAssets:Providers</c> — the
/// <c>primary</c> catalog is the Postgres store, and a second catalog from another plugin is addressable by id.
/// </summary>
[Trait("Category", "RouteInventory")]
public sealed class CatalogPoolHostTests
{
    [Fact]
    public async Task ServerHost_PrimaryCatalog_IsThePostgresStore()
    {
        var services = await ServerHostFixture.GetServicesAsync();
        var pool = services.GetRequiredService<ICatalogPool>();

        pool.CatalogIds.Should().Equal("primary");
        pool.DefaultCatalog.UnwrapToConcrete().Should().BeOfType<PostgresAssetProvider>();
        services.GetRequiredKeyedService<IAssetProvider>(PostgresOrganizationCatalogServiceExtensions.SharedCatalogKey)
            .Should().BeSameAs(pool.DefaultCatalog);
    }

    [Fact]
    public async Task ServerHost_SecondCatalogFromATestPlugin_IsAddressableById()
    {
        var factory = await ServerHostFixture.GetSecondCatalogFactoryAsync();
        var pool = factory.Services.GetRequiredService<ICatalogPool>();
        var plugin = factory.Services.GetServices<IProviderPlugin>().OfType<TestProviderPlugin>().Single();

        pool.CatalogIds.Should().Equal("primary", "secondary");
        pool.GetCatalog("secondary").UnwrapToConcrete().Should().BeSameAs(plugin.Created.Single());
        pool.GetCatalog("primary").UnwrapToConcrete().Should().BeOfType<PostgresAssetProvider>();
    }
}

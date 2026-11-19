using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Providers;
using GeoAssets.Server.Catalogs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Catalogs;

/// <summary>
/// XD01-373: a host whose catalog comes from a provider plugin with no native organization scoping still serves each
/// organization its own view, through the generic fallback.
/// </summary>
public sealed class OrganizationCatalogWithInMemoryPluginTests
{
    private static readonly Guid OrgA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid OrgB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private sealed class SwitchableTenant : ITenantContext
    {
        public Guid OrganizationId { get; set; }

        public Guid CurrentOrganizationId => OrganizationId;
    }

    private sealed class NativeFactory : IOrganizationCatalogFactory
    {
        public IAssetProvider GetForOrganization(Guid organizationId) => new TestAssetProvider();

        public ValueTask<IAssetProvider> GetForOrganizationAsync(Guid organizationId, CancellationToken ct) =>
            ValueTask.FromResult(GetForOrganization(organizationId));
    }

    private static ServiceCollection NewServices(SwitchableTenant tenant)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [$"{CatalogConfiguration.ProvidersKey}:0:Id"] = "primary",
                [$"{CatalogConfiguration.ProvidersKey}:0:Plugin"] = TestProviderPlugin.DefaultId,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IProviderPlugin>(new TestProviderPlugin());
        services.AddSingleton<ITenantContext>(tenant);
        services.AddServerCatalogPool(configuration);
        return services;
    }

    private static GeoFeature Feature(string id, Guid organizationId) => new()
    {
        Id = id,
        Geometry = new GeoPoint(1, 1),
        Properties = new GeoFeatureProperties { Name = id, OrganizationId = organizationId },
    };

    private static string[] VisibleTo(ServiceProvider provider, SwitchableTenant tenant, Guid organizationId)
    {
        tenant.OrganizationId = organizationId;
        using var scope = provider.CreateScope();
        return [.. scope.ServiceProvider.GetRequiredService<IAssetProvider>().GetAll().Select(f => f.Id).Order(StringComparer.Ordinal)];
    }

    [Fact]
    public async Task InMemoryCatalog_OrgA_SeesOwnAndShared_NotOrgB()
    {
        var tenant = new SwitchableTenant();
        var services = NewServices(tenant);
        services.AddServerOrganizationAssetCatalogs();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await provider.InitializeCatalogPoolAsync();
        var primary = provider.GetRequiredService<ICatalogPool>().DefaultCatalog;
        primary.AddRange([Feature("a-1", OrgA), Feature("b-1", OrgB), Feature("shared-1", Guid.Empty)]);

        VisibleTo(provider, tenant, OrgA).Should().Equal("a-1", "shared-1");
        VisibleTo(provider, tenant, OrgB).Should().Equal("b-1", "shared-1");
        VisibleTo(provider, tenant, Guid.Empty).Should().Equal("shared-1");
        provider.GetRequiredService<IOrganizationCatalogFactory>().Should().BeOfType<FilteringOrganizationCatalogFactory>();
    }

    [Fact]
    public async Task InMemoryCatalog_WriteThroughAnOrganizationView_LandsInThePrimaryCatalogStamped()
    {
        var tenant = new SwitchableTenant();
        var services = NewServices(tenant);
        services.AddServerOrganizationAssetCatalogs();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await provider.InitializeCatalogPoolAsync();
        tenant.OrganizationId = OrgA;

        using (var scope = provider.CreateScope())
            scope.ServiceProvider.GetRequiredService<IAssetProvider>().Add(Feature("new", Guid.Empty));

        provider.GetRequiredService<ICatalogPool>().DefaultCatalog.GetById("new")!.Properties.OrganizationId.Should().Be(OrgA);
    }

    [Fact]
    public async Task InMemoryCatalog_TheSharedCatalogIsKeyed_AndIsThePrimaryCatalog()
    {
        var tenant = new SwitchableTenant();
        var services = NewServices(tenant);
        services.AddServerOrganizationAssetCatalogs();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await provider.InitializeCatalogPoolAsync();

        provider.GetRequiredKeyedService<IAssetProvider>(OrganizationCatalogKeys.SharedCatalog)
            .Should().BeSameAs(provider.GetRequiredService<ICatalogPool>().DefaultCatalog);
    }

    [Fact]
    public void AddServerOrganizationAssetCatalogs_NullServices_Throws()
    {
        var act = () => OrganizationAssetCatalogHostExtensions.AddServerOrganizationAssetCatalogs(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task AddServerOrganizationAssetCatalogs_WithANativeFactoryRegistered_DoesNothing()
    {
        var tenant = new SwitchableTenant();
        var services = NewServices(tenant);
        var native = new NativeFactory();
        services.AddSingleton<IOrganizationCatalogFactory>(native);
        var before = services.Count;

        services.AddServerOrganizationAssetCatalogs();

        services.Count.Should().Be(before);
        services.Any(d => d.IsKeyedService).Should().BeFalse();
        services.Count(d => d.ServiceType == typeof(IAssetProvider) && !d.IsKeyedService).Should().Be(1);
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOrganizationCatalogFactory>().Should().BeSameAs(native);
    }
}

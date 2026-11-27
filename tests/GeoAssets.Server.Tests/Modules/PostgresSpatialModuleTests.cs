using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Plugins;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Server.Modules.PostgresSpatial;
using GeoAssets.Server.Tests.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Modules;

/// <summary>
/// The PostgreSQL/PostGIS spatial plane as a module of this host (XD01-359): its identity, the connection string it
/// insists on, and the two WMS routes it maps.
/// </summary>
public sealed class PostgresSpatialModuleTests
{
    private static async Task<IReadOnlyList<string>> MappedRoutesAsync(params (string Key, string? Value)[] settings)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)));
        // The WMS routes resolve both per request; the route inventory only needs them known to the container.
        builder.Services.AddSingleton<IAssetProvider>(new TestAssetProvider());
        builder.Services.AddScoped<IAssetRenderQuery>(sp => new AssetProviderRenderQuery(sp.GetRequiredService<IAssetProvider>()));
        await using var app = builder.Build();

        new PostgresSpatialModule().MapEndpoints(app);

        return RouteInventory.Describe(((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints));
    }

    [Fact]
    public void Id_IsPostgresSpatial()
    {
        new PostgresSpatialModule().Id.Should().Be("postgres-spatial").And.Be(PostgresSpatialModule.ModuleId);
    }

    [Fact]
    public void ConfigureServices_WithoutConnectionString_Throws()
    {
        var configuration = new ConfigurationBuilder().Build();

        var act = () => new PostgresSpatialModule().ConfigureServices(new ServiceCollection(), configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*postgres-spatial*GeoAssets*connection string*");
    }

    [Fact]
    public void ConfigureServices_RegistersThePostgresProviderPlugin()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["ConnectionStrings:GeoAssets"] = "Host=localhost;Database=unused" })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IAssetProvider>(new TestAssetProvider());

        new PostgresSpatialModule().ConfigureServices(services, configuration);

        services.Where(d => d.ServiceType == typeof(IProviderPlugin)).Should().ContainSingle()
            .Which.ImplementationType.Should().Be<PostgresProviderPlugin>();
    }

    [Fact]
    public void ConfigureServices_RegistersThePostgresPluginThroughItsModule_Once()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["ConnectionStrings:GeoAssets"] = "Host=localhost;Database=unused" })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IAssetProvider>(new TestAssetProvider());

        new PostgresSpatialModule().ConfigureServices(services, configuration);

        services.Where(d => d.ServiceType == typeof(PostgresProviderFactory)).Should().ContainSingle();
        services.Where(d => d.ServiceType == typeof(ProviderPluginRegistry)).Should().ContainSingle();
    }

    [Fact]
    public void ConfigureServices_AfterADropInPluginClaimedThePostgresId_FailsFast()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["ConnectionStrings:GeoAssets"] = "Host=localhost;Database=unused" })
            .Build();
        var services = new ServiceCollection();
        services.AddDiscoveredProviderPlugins(
            configuration, ProviderHostTargets.Server, new StaticAssemblyPluginSource().Add<DropInPostgresPluginModule>());

        var act = () => new PostgresSpatialModule().ConfigureServices(services, configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("Duplicate provider plugin module id 'postgres'*earlier*");
    }

    [Fact]
    public async Task MapEndpoints_MapsBothWmsRoutesAndTheTileRoutes_RequiringFeaturesRead()
    {
        var routes = await MappedRoutesAsync();

        routes.Should().BeEquivalentTo(
        [
            "GET /api/geoassets/wms | policy:features:read",
            "GET /wms | policy:features:read",
            "GET /tiles/lod-calibration | policy:features:read, policy:organizations:edit",
            "GET /tiles/style.json | policy:features:read",
            "GET /tiles/{layer}/{v}/{z}/{x}/{y}.mvt | policy:features:read",
            "GET /tiles/{layer}/{v}/{z}/{x}/{y}.png | policy:features:read",
        ]);
    }

    [Fact]
    public void ConfigureServices_RegistersTheTileServing_AndItsInvalidationForTheWriters()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["ConnectionStrings:GeoAssets"] = "Host=localhost;Database=x" })
            .Build();
        services.AddSingleton<IAssetProvider>(new TestAssetProvider());

        new PostgresSpatialModule().ConfigureServices(services, configuration);

        services.Should().ContainSingle(d => d.ServiceType == typeof(ITileInvalidation));
        services.Should().ContainSingle(d => d.ServiceType == typeof(ITileCache));
        services.Should().ContainSingle(d => d.ServiceType == typeof(GeoAssets.Server.Tiles.TileServing));
    }
}

using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Server.Modules.PostgresSpatial;
using GeoAssets.Server.Tests.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
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
        builder.Services.AddSingleton<IDbContextFactory<GeoAssetsDbContext>, NeverCalledDbContextFactory>();
        builder.Services.AddSingleton<WmsPostGisRenderer>();
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
    public async Task MapEndpoints_MapsBothWmsRoutes_AnonymousByDefault()
    {
        var routes = await MappedRoutesAsync();

        routes.Should().BeEquivalentTo(["GET /api/geoassets/wms | anonymous", "GET /wms | anonymous"]);
    }

    [Fact]
    public async Task MapEndpoints_WithWmsRequireAuthentication_RequiresAuthorization()
    {
        var routes = await MappedRoutesAsync((PostgresSpatialModule.WmsRequireAuthenticationKey, "true"));

        routes.Should().BeEquivalentTo(["GET /api/geoassets/wms | policy:features:read", "GET /wms | policy:features:read"]);
    }
}

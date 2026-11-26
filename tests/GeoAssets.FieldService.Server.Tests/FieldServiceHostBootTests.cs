using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.FieldService.FieldWork;
using GeoAssets.Integrations.Scada;
using GeoAssets.FieldService.Agents.Dispatch;
using GeoAssets.Workflow.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// Boots the real <c>GeoAssets.FieldService.Server</c> <c>Program</c> (XD01-386, Invariant 1): the
/// host has no core asset database — no <c>ConnectionStrings:GeoAssets</c> — and answers its
/// asset-read ports over the Core GeoAssets Server's API. Keyed on <see cref="GeoCoreAssetQueryAdapter"/>
/// (any type of the host assembly locates its entry point). Nothing here opens a database
/// connection: resolving a <c>DbContext</c> only builds its options.
/// </summary>
public sealed class FieldServiceHostBootTests
{
    private const string FieldServiceConnectionString =
        "Host=localhost;Port=1;Database=fieldservice_boot_test;Username=boot;Password=boot;Timeout=1";

    private static WebApplicationFactory<GeoCoreAssetQueryAdapter> CreateFactory(int? statementTimeoutMs = null) =>
        new WebApplicationFactory<GeoCoreAssetQueryAdapter>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Testing");
            host.UseSetting("ConnectionStrings:FieldService", FieldServiceConnectionString);
            if (statementTimeoutMs is { } timeout)
                host.UseSetting("Postgres:StatementTimeoutMs", timeout.ToString(System.Globalization.CultureInfo.InvariantCulture));
        });

    [Fact]
    public void Host_WithNoGeoAssetsConnectionString_StartsAndResolvesTheAssetPortsToTheGeoCoreAdapter()
    {
        using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        services.GetRequiredService<IConfiguration>().GetConnectionString("GeoAssets")
            .Should().BeNull("the FieldService host never opens the core asset database");

        var proximity = services.GetRequiredService<IAssetProximityQuery>();
        var topology = services.GetRequiredService<IAssetTopologyQuery>();
        var dispatch = services.GetRequiredService<IDispatchAssetQuery>();

        proximity.Should().BeOfType<GeoCoreAssetQueryAdapter>();
        topology.Should().BeOfType<GeoCoreAssetQueryAdapter>();
        dispatch.Should().BeOfType<GeoCoreAssetQueryAdapter>();
        proximity.Should().BeSameAs(topology).And.BeSameAs(dispatch, "one scoped adapter serves all three ports");
        services.GetRequiredService<IOrganizationScopedGeoCoreClient>().Should().NotBeNull();
        services.GetRequiredService<IGeoCoreClient>().Should().NotBeNull();
    }

    [Fact]
    public void Host_ResolvesTheServicesThatConsumeTheAssetPorts()
    {
        using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        services.GetRequiredService<GeofenceArrivalTransitionGuard>().Should().NotBeNull();
        services.GetRequiredService<IFieldWorkTransitionGuard>().Should().NotBeNull();
        services.GetRequiredService<IFeederOutageDispatchService>().Should().NotBeNull();
        services.GetRequiredService<DynamicCrewDispatchAdvisorService>().Should().NotBeNull();
    }

    [Fact]
    public void Host_RegistersNoCoreAssetProviderAndDoesNotReferenceThePostgresAssetProvider()
    {
        using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetService<IAssetProvider>().Should().BeNull();
        factory.Services.GetService<IProviderPool>().Should().BeNull();
        typeof(GeoCoreAssetQueryAdapter).Assembly.GetReferencedAssemblies().Select(a => a.Name)
            .Should().NotContain("GeoAssets.Provider.PostgreSQL");
    }

    [Fact]
    public void Host_RegistersTheShapefileToolsButNoProviderPlugin()
    {
        using var factory = CreateFactory();

        factory.Services.GetRequiredService<IShapefileExportService>().Should().NotBeNull(
            "DisNor056Exporter writes its dossier's Shapefiles through it");
        factory.Services.GetRequiredService<IBdgdPonnotImportService>().Should().NotBeNull();
        factory.Services.GetServices<IProviderPlugin>().Should().BeEmpty(
            "the host opens no provider catalogs, so it registers the Shapefile tools without the plugin (XD01-424)");
    }

    [Fact]
    public void Host_StatementTimeoutConfigured_IsAppliedToTheFieldServiceConnectionString()
    {
        using var factory = CreateFactory(statementTimeoutMs: 4_500);
        using var scope = factory.Services.CreateScope();

        var connectionString = scope.ServiceProvider.GetRequiredService<ServiceOrderDbContext>().Database.GetConnectionString();

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.Options.Should().Be("-c statement_timeout=4500");
        builder.Database.Should().Be("fieldservice_boot_test");
    }

    [Fact]
    public void Host_StatementTimeoutNotConfigured_LeavesTheFieldServiceConnectionStringUntouched()
    {
        using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();

        var connectionString = scope.ServiceProvider.GetRequiredService<ServiceOrderDbContext>().Database.GetConnectionString();

        new NpgsqlConnectionStringBuilder(connectionString).Options.Should().BeNullOrEmpty();
    }
}

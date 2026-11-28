using System.Net;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using GeoAssets.Server.Tests.OrganizationGrants;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace GeoAssets.Server.Tests.FeatureCatalogs;

/// <summary>
/// XD01-481 end to end over PostGIS: the kernel's generic WMS render fallback (<see cref="AssetProviderRenderQuery"/>, what
/// WMS renders with when no module registers a pushdown) over an organization's catalog with more features in the box than
/// its enumeration limit. Served from PostgreSQL, the catalog's bounds read is refused and the request answers
/// <c>422 catalog.enumeration-limit</c> (<see cref="CatalogEnumerationLimitTranslationExtensions"/>) instead of reading the
/// box whole; cached whole, the tile renders as before.
/// </summary>
public sealed class WmsGenericRenderBoundedPostgresTests : IAsyncLifetime
{
    private const int Limit = 1_000;
    private const int Seeded = Limit + 500;
    private const string GetMap = "/wms?SERVICE=WMS&REQUEST=GetMap&BBOX=-50,-20,-49,-19&WIDTH=256&HEIGHT=256&TRANSPARENT=TRUE";

    // A click at (-49.5, -19.5) whose ±1 % tolerance of a 50° map is the seeded 1° box.
    private const string GetFeatureInfo = "/wms?SERVICE=WMS&REQUEST=GetFeatureInfo&BBOX=-75,-45,-25,5&WIDTH=100&HEIGHT=100&I=51&J=49&FEATURE_COUNT=5";

    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static (string ConnectionString, Guid Org)? _seeded;

    private Guid _org;
    private string _connectionString = string.Empty;
    private readonly List<IAsyncDisposable> _owned = [];
    private IHost? _host;
    private HttpClient _client = null!;

    /// <summary>One migrated database for every test here, seeded once: <see cref="Seeded"/> features of one organization inside the box.</summary>
    private static async Task<(string ConnectionString, Guid Org)> SeededDatabaseAsync()
    {
        await SeedLock.WaitAsync();
        try
        {
            if (_seeded is { } seeded) return seeded;

            var org = Guid.NewGuid();
            var connectionString = await OrganizationGrantsPostgres.CreateDatabaseAsync();
            await using (var db = new GeoAssetsDbContext(Options(connectionString).Options))
                await db.Database.MigrateAsync();

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "Description", "OrganizationId", "CreatedAt", "UpdatedAt", geom, custom_attributes, topology)
                SELECT 'in-' || lpad(i::text, 6, '0'), 'n' || i, 'pole', '', @org, now(), now(),
                       ST_SetSRID(ST_MakePoint(-49.9 + (i % 100) * 0.008, -19.9 + (i / 100) * 0.008), 4326), '{}'::jsonb, '[]'::jsonb
                FROM generate_series(0, @count - 1) AS i;
                ANALYZE geo_entity;
                """,
                connection);
            command.Parameters.AddWithValue("org", org);
            command.Parameters.AddWithValue("count", Seeded);
            await command.ExecuteNonQueryAsync();

            _seeded = (connectionString, org);
            return (connectionString, org);
        }
        finally
        {
            SeedLock.Release();
        }
    }

    public async Task InitializeAsync() => (_connectionString, _org) = await SeededDatabaseAsync();

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        foreach (var owned in Enumerable.Reverse(_owned))
            await owned.DisposeAsync();
    }

    private static DbContextOptionsBuilder<GeoAssetsDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<GeoAssetsDbContext>().UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite());

    private async Task StartAsync(bool queryBacked)
    {
        var shared = new ValidatingAssetProvider(await new PostgresProviderFactory(NullLoggerFactory.Instance, TimeProvider.System).CreateAsync(_connectionString));
        _owned.Add(shared);
        var catalogs = new PostgresOrganizationAssetCatalogs(
            Options(_connectionString).Options, shared, NullLoggerFactory.Instance, TimeProvider.System,
            tileInvalidation: null,
            catalogOptions: new OrganizationCatalogOptions { FullCacheMaxFeatures = queryBacked ? 0 : 1_000_000, EnumerationLimit = Limit });
        _owned.Add(catalogs);
        await catalogs.WarmAsync(_org, CancellationToken.None);
        catalogs.IsQueryBacked(_org).Should().Be(queryBacked);
        var catalog = catalogs.For(_org);

        _host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthorization(options => options.AddPolicy("features:read", policy => policy.RequireAssertion(_ => true)));
                    services.AddSingleton(catalog);
                    services.AddSingleton<IAssetRenderQuery>(new AssetProviderRenderQuery(catalog));
                });
                webHost.Configure(app =>
                {
                    app.UseCatalogEnumerationLimitTranslation();
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapWmsApi());
                });
            })
            .StartAsync();
        _client = _host.GetTestServer().CreateClient();
    }

    [Theory]
    [InlineData(GetMap)]
    [InlineData(GetFeatureInfo)]
    public async Task QueryBacked_ABoxOverMoreFeaturesThanTheLimit_Answers422(string request)
    {
        await StartAsync(queryBacked: true);

        using var response = await _client.GetAsync(new Uri(request, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be(CatalogEnumerationLimitTranslationExtensions.ProblemCode);
        problem.RootElement.GetProperty("operation").GetString().Should().Be("GetInBoundsAsync");
        problem.RootElement.GetProperty("limit").GetInt32().Should().Be(Limit);
    }

    [Fact]
    public async Task CachedWhole_TheSameBox_RendersAsBefore()
    {
        await StartAsync(queryBacked: false);

        using var map = await _client.GetAsync(new Uri(GetMap, UriKind.Relative));
        using var info = await _client.GetAsync(new Uri(GetFeatureInfo, UriKind.Relative));

        map.StatusCode.Should().Be(HttpStatusCode.OK);
        map.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        info.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

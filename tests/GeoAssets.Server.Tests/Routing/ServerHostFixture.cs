using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Projects.Persistence;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Workflow.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace GeoAssets.Server.Tests.Routing;

/// <summary>
/// Boots the real <c>GeoAssets.Server</c> <c>Program</c> (XD01-354) against a Postgres+PostGIS Testcontainer with
/// the production EF Core migrations applied, so the route inventory is read from the host exactly as Program.cs
/// composes it — including whatever module bootstrap later phases put there — rather than from a hand-built test host.
/// Program needs a live database before and after <c>Build()</c> (warmed asset provider, registry overlay, seeders).
/// Same lazily-started, never-disposed static container pattern as <c>SecurityTestContainerFixture</c>.
/// The factory is keyed on <see cref="HttpTenantContext"/> (any type of the Server assembly locates its entry point):
/// <c>Program</c> itself is ambiguous here, since this project also references <c>GeoAssets.IpBilling.Server</c>.
/// </summary>
internal static class ServerHostFixture
{
    private static readonly SemaphoreSlim StartLock = new(1, 1);
    private static PostgreSqlContainer? _container;
    private static string? _connectionString;
    private static WebApplicationFactory<HttpTenantContext>? _factory;
    private static WebApplicationFactory<HttpTenantContext>? _kernelOnlyFactory;

    public static async Task<IServiceProvider> GetServicesAsync()
    {
        await StartLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _factory ??= CreateFactory(await GetConnectionStringAsync().ConfigureAwait(false), kernelOnly: false);
            return _factory.Services;
        }
        finally
        {
            StartLock.Release();
        }
    }

    /// <summary>
    /// The same host on the same database, with <c>GeoAssets:Modules:0</c> blanked so no module is enabled (XD01-358):
    /// what the kernel alone maps and registers.
    /// </summary>
    public static async Task<IServiceProvider> GetKernelOnlyServicesAsync()
    {
        await StartLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _kernelOnlyFactory ??= CreateFactory(await GetConnectionStringAsync().ConfigureAwait(false), kernelOnly: true);
            return _kernelOnlyFactory.Services;
        }
        finally
        {
            StartLock.Release();
        }
    }

    private static async Task<string> GetConnectionStringAsync() =>
        _connectionString ??= await StartDatabaseAsync().ConfigureAwait(false);

    private static WebApplicationFactory<HttpTenantContext> CreateFactory(string connectionString, bool kernelOnly) =>
        new WebApplicationFactory<HttpTenantContext>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Testing");
            host.UseSetting("ConnectionStrings:GeoAssets", connectionString);
            if (kernelOnly)
            {
                host.UseSetting("GeoAssets:Modules:0", "");
            }
        });

    private static async Task<string> StartDatabaseAsync()
    {
        _container = new PostgreSqlBuilder("postgis/postgis:16-3.4")
            .WithDatabase("geoassets_route_inventory_test")
            .WithUsername("geoassets")
            .WithPassword("geoassets")
            .Build();
        await _container.StartAsync().ConfigureAwait(false);
        var connectionString = _container.GetConnectionString();

        await using (var geoAssets = new GeoAssetsDbContext(new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options))
        {
            await geoAssets.Database.MigrateAsync().ConfigureAwait(false);
        }

        await using (var identity = new GeoIdentityDbContext(ServerMigrationsOptions<GeoIdentityDbContext>(connectionString)))
        {
            await identity.Database.MigrateAsync().ConfigureAwait(false);
        }

        await using (var serviceOrders = new ServiceOrderDbContext(ServerMigrationsOptions<ServiceOrderDbContext>(connectionString)))
        {
            await serviceOrders.Database.MigrateAsync().ConfigureAwait(false);
        }

        await using (var projects = new ProjectDbContext(ServerMigrationsOptions<ProjectDbContext>(connectionString)))
        {
            await projects.Database.MigrateAsync().ConfigureAwait(false);
        }

        return connectionString;
    }

    private static DbContextOptions<TContext> ServerMigrationsOptions<TContext>(string connectionString)
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("GeoAssets.Server"))
            .Options;
}

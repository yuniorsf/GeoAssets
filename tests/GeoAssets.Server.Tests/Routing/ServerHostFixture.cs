using GeoAssets.Core.Interfaces;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Projects.Persistence;
using GeoAssets.Server.Catalogs;
using GeoAssets.Server.Modules;
using GeoAssets.Server.Tests.Catalogs;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Workflow.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    private static WebApplicationFactory<HttpTenantContext>? _neutralOperatorEnabledFactory;
    private static WebApplicationFactory<HttpTenantContext>? _neutralOperatorDisabledFactory;
    private static WebApplicationFactory<HttpTenantContext>? _ipBillingBridgeDisabledFactory;
    private static WebApplicationFactory<HttpTenantContext>? _kernelOnlyFactory;
    private static WebApplicationFactory<HttpTenantContext>? _secondCatalogFactory;

    /// <summary>The host as <c>appsettings.json</c> composes it — since the XD01-384 cut-over, without the neutral-operator module.</summary>
    public static async Task<IServiceProvider> GetServicesAsync()
    {
        await StartLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _factory ??= CreateFactory(await GetConnectionStringAsync().ConfigureAwait(false), modules: null);
            return _factory.Services;
        }
        finally
        {
            StartLock.Release();
        }
    }

    /// <summary>
    /// The same host on the same database with every built-in module listed explicitly — postgres-spatial, neutral-operator,
    /// ip-billing-bridge (the cut-over's rollback configuration, XD01-384): what the host maps and registers while it still
    /// hosts the ISP neutral-operator vertical.
    /// </summary>
    public static async Task<IServiceProvider> GetNeutralOperatorEnabledServicesAsync()
    {
        await StartLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _neutralOperatorEnabledFactory ??= CreateFactory(
                await GetConnectionStringAsync().ConfigureAwait(false), modules: ["postgres-spatial", "neutral-operator", "ip-billing-bridge"]);
            return _neutralOperatorEnabledFactory.Services;
        }
        finally
        {
            StartLock.Release();
        }
    }

    /// <summary>
    /// The same host on the same database with the modules listed explicitly without neutral-operator (XD01-358): what the
    /// host maps and registers without the ISP neutral-operator vertical, whatever <c>appsettings.json</c> lists.
    /// </summary>
    public static async Task<IServiceProvider> GetNeutralOperatorDisabledServicesAsync()
    {
        await StartLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _neutralOperatorDisabledFactory ??= CreateFactory(
                await GetConnectionStringAsync().ConfigureAwait(false), modules: ["postgres-spatial", "ip-billing-bridge"]);
            return _neutralOperatorDisabledFactory.Services;
        }
        finally
        {
            StartLock.Release();
        }
    }

    /// <summary>
    /// The same host on the same database with the modules listed explicitly without ip-billing-bridge (XD01-377): what the
    /// host registers and seeds without the IP-billing vertical.
    /// </summary>
    public static async Task<IServiceProvider> GetIpBillingBridgeDisabledServicesAsync()
    {
        await StartLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _ipBillingBridgeDisabledFactory ??= CreateFactory(
                await GetConnectionStringAsync().ConfigureAwait(false), modules: ["postgres-spatial", "neutral-operator"]);
            return _ipBillingBridgeDisabledFactory.Services;
        }
        finally
        {
            StartLock.Release();
        }
    }

    /// <summary>
    /// The same host on the same database with every module entry blanked (XD01-359, invariant I5): the kernel alone, on a
    /// test-only in-memory <see cref="IAssetProvider"/> with a signed-in, fully permitted caller so authorized endpoints can
    /// be called.
    /// </summary>
    public static async Task<WebApplicationFactory<HttpTenantContext>> GetKernelOnlyFactoryAsync()
    {
        await StartLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _kernelOnlyFactory ??= CreateFactory(
                await GetConnectionStringAsync().ConfigureAwait(false), modules: [], kernelTestDoubles: true);
            _ = _kernelOnlyFactory.Services; // start the host inside the lock: every host seeds the same database on startup
            return _kernelOnlyFactory;
        }
        finally
        {
            StartLock.Release();
        }
    }

    /// <summary>
    /// The same host on the same database with a second catalog, <c>secondary</c>, built by a test-only
    /// <c>IProviderPlugin</c> (XD01-371): a catalog addressable by id next to the Postgres <c>primary</c>.
    /// </summary>
    public static async Task<WebApplicationFactory<HttpTenantContext>> GetSecondCatalogFactoryAsync()
    {
        await StartLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _secondCatalogFactory ??= CreateFactory(
                await GetConnectionStringAsync().ConfigureAwait(false), modules: null, secondCatalogPlugin: true);
            _ = _secondCatalogFactory.Services; // start the host inside the lock: every host seeds the same database on startup
            return _secondCatalogFactory;
        }
        finally
        {
            StartLock.Release();
        }
    }

    private static async Task<string> GetConnectionStringAsync() =>
        _connectionString ??= await StartDatabaseAsync().ConfigureAwait(false);

    private static WebApplicationFactory<HttpTenantContext> CreateFactory(
        string connectionString, string[]? modules, bool kernelTestDoubles = false, bool secondCatalogPlugin = false) =>
        new WebApplicationFactory<HttpTenantContext>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Testing");
            host.UseSetting("ConnectionStrings:GeoAssets", connectionString);

            // null keeps appsettings.json's list; otherwise exactly these modules, every other position up to the number of
            // built-ins blanked (a blank entry is ignored), so no entry of appsettings.json survives the override.
            if (modules is not null)
            {
                var positions = Math.Max(modules.Length, ServerModules.BuiltIns().Count);
                for (var index = 0; index < positions; index++)
                {
                    host.UseSetting($"GeoAssets:Modules:{index}", index < modules.Length ? modules[index] : "");
                }
            }

            if (secondCatalogPlugin)
            {
                host.UseSetting($"{CatalogConfiguration.ProvidersKey}:1:Id", "secondary");
                host.UseSetting($"{CatalogConfiguration.ProvidersKey}:1:Plugin", TestProviderPlugin.DefaultId);
                host.ConfigureTestServices(services => services.AddSingleton<IProviderPlugin>(new TestProviderPlugin()));
            }

            if (kernelTestDoubles)
            {
                host.ConfigureTestServices(KernelOnlyTestDoubles.Register);
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

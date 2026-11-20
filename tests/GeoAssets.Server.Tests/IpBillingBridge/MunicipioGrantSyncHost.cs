using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.IpBilling.GrantSync;
using GeoAssets.IpBilling.Persistence;
using GeoAssets.IpBilling.Server;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GeoAssets.Server.Tests.IpBillingBridge;

/// <summary>
/// XD01-395: the real IpBilling host over an in-memory SQLite <see cref="IpBillingDbContext"/> and the in-process kernel
/// (<see cref="StubKernel"/>). Time is a <see cref="FakeTimeProvider"/> and the background loop is off, so a test runs the
/// dispatcher one deterministic cycle at a time with <see cref="DispatchAsync"/>.
/// </summary>
internal sealed class MunicipioGrantSyncHost : IAsyncDisposable
{
    public const string IbgeCode = "3550308";

    public static readonly Guid Distribuidora = new("d0d0d0d0-0000-4000-8000-000000000395");
    public static readonly Guid PrefeituraA = new("a0a0a0a0-0000-4000-8000-000000000395");
    public static readonly Guid PrefeituraB = new("b0b0b0b0-0000-4000-8000-000000000395");

    private static readonly Uri Municipios = new("/api/ip-billing/municipios", UriKind.Relative);

    private readonly string _connectionString;
    private readonly SqliteConnection _keeper;
    private readonly MunicipioGrantSyncDispatcher _dispatcher;

    private MunicipioGrantSyncHost(IpBillingBridgeHost host, FakeTimeProvider time, SqliteConnection keeper, string connectionString, MunicipioGrantSyncDispatcher dispatcher)
    {
        Host = host;
        Time = time;
        _keeper = keeper;
        _connectionString = connectionString;
        _dispatcher = dispatcher;
    }

    public IpBillingBridgeHost Host { get; }

    public StubKernel Kernel => Host.Kernel;

    public FakeTimeProvider Time { get; }

    public static async Task<MunicipioGrantSyncHost> StartAsync(MunicipioGrantSyncOptions? options = null)
    {
        var connectionString = $"Data Source=file:grant-sync-{Guid.NewGuid():N}?mode=memory&cache=shared";
        var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var effective = (options ?? new MunicipioGrantSyncOptions()) with { Enabled = false };

        var host = await IpBillingBridgeHost.StartAsync(configureServices: services =>
        {
            RemoveIpBillingDbContext(services);
            services.AddDbContext<IpBillingDbContext>(o => o.UseSqlite(connectionString));
            services.AddSingleton<TimeProvider>(time);
            services.AddSingleton<IOptions<MunicipioGrantSyncOptions>>(Options.Create(effective));
        });

        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IpBillingDbContext>().Database.EnsureCreatedAsync();
        }

        var dispatcher = host.Services.GetServices<IHostedService>().OfType<MunicipioGrantSyncDispatcher>().Single();
        return new MunicipioGrantSyncHost(host, time, keeper, connectionString, dispatcher);
    }

    public Task<int> DispatchAsync() => _dispatcher.DispatchOnceAsync(CancellationToken.None);

    /// <summary>Runs cycles until nothing is claimed (bounded).</summary>
    public async Task<int> DrainAsync()
    {
        var total = 0;
        for (var i = 0; i < 20; i++)
        {
            var claimed = await DispatchAsync();
            if (claimed == 0) break;
            total += claimed;
        }

        return total;
    }

    public async Task<(HttpStatusCode Status, Guid Id, string? State)> CreateAsync(
        Guid? prefeitura, string ibgeCode = IbgeCode, Guid? distribuidora = null, Action<HttpRequestMessage>? configure = null)
    {
        using var client = Host.ClientFor(BridgeCallers.ReferenceManagerToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, Municipios) { Content = JsonContent.Create(Body(ibgeCode, distribuidora ?? Distribuidora, prefeitura)) };
        configure?.Invoke(request);
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode) return (response.StatusCode, Guid.Empty, null);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, doc.RootElement.GetProperty("id").GetGuid(), doc.RootElement.GetProperty("grantSyncState").GetString());
    }

    public async Task<HttpStatusCode> UpdateAsync(
        Guid id, Guid? prefeitura, string ibgeCode = IbgeCode, Guid? distribuidora = null, string name = "São Paulo")
    {
        using var client = Host.ClientFor(BridgeCallers.ReferenceManagerToken);
        using var response = await client.PutAsJsonAsync(
            new Uri($"/api/ip-billing/municipios/{id}", UriKind.Relative), Body(ibgeCode, distribuidora ?? Distribuidora, prefeitura, name));
        return response.StatusCode;
    }

    public async Task<HttpStatusCode> DeleteAsync(Guid id)
    {
        using var client = Host.ClientFor(BridgeCallers.ReferenceManagerToken);
        using var response = await client.DeleteAsync(new Uri($"/api/ip-billing/municipios/{id}", UriKind.Relative));
        return response.StatusCode;
    }

    public async Task<(string? State, string? Error, DateTimeOffset? SyncedAt)> StatusAsync(string ibgeCode = IbgeCode)
    {
        using var client = Host.ClientFor(BridgeCallers.ReferenceManagerToken);
        using var response = await client.GetAsync(new Uri($"/api/ip-billing/municipios/{ibgeCode}", UriKind.Relative));
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var error = root.TryGetProperty("grantSyncError", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        DateTimeOffset? syncedAt = root.TryGetProperty("grantSyncedAt", out var s) && s.ValueKind == JsonValueKind.String ? s.GetDateTimeOffset() : null;
        return (root.GetProperty("grantSyncState").GetString(), error, syncedAt);
    }

    public async Task<int> RequestCountAsync()
    {
        await using var db = NewContext();
        return await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM \"MunicipioGrantSyncRequests\"").SingleAsync();
    }

    public async Task ExecuteSqlAsync(string sql)
    {
        await using var db = NewContext();
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    public async Task<string?> RequestedByOfFirstRequestAsync()
    {
        await using var db = NewContext();
        return await db.Database.SqlQueryRaw<string>("SELECT \"RequestedBy\" AS \"Value\" FROM \"MunicipioGrantSyncRequests\" ORDER BY \"Id\" LIMIT 1").SingleOrDefaultAsync();
    }

    /// <summary>Claims one due request as another worker and walks away (a crashed worker).</summary>
    public async Task<int> ClaimAsAnotherWorkerAsync(string workerId, TimeSpan lease)
    {
        using var scope = Host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IMunicipioGrantSyncStore>();
        return (await store.ClaimDueAsync(10, lease, workerId)).Count;
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        await _keeper.DisposeAsync();
    }

    private IpBillingDbContext NewContext() =>
        new(new DbContextOptionsBuilder<IpBillingDbContext>().UseSqlite(_connectionString).Options);

    private static object Body(string ibgeCode, Guid distribuidora, Guid? prefeitura, string name = "São Paulo") => new
    {
        ibgeCode,
        name,
        uf = "SP",
        distribuidoraOrganizationId = distribuidora,
        prefeituraOrganizationId = prefeitura,
    };

    /// <summary>Drops the host's Npgsql registration of the IP-billing context (its options included) so a SQLite one can replace it.</summary>
    private static void RemoveIpBillingDbContext(IServiceCollection services)
    {
        var contextType = typeof(IpBillingDbContext);
        var stale = services
            .Where(d => d.ServiceType == contextType
                || (d.ServiceType.IsGenericType && d.ServiceType.GenericTypeArguments.Contains(contextType)))
            .ToList();
        foreach (var descriptor in stale) services.Remove(descriptor);
    }
}

using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using GeoAssets.Server.Tests.OrganizationGrants;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace GeoAssets.Server.Tests.FeatureCatalogs;

/// <summary>
/// XD01-479 end to end over PostGIS: <c>POST /api/geoassets/features/query</c> on an organization's catalog — cached whole
/// and served from PostgreSQL — returns at most <see cref="GeoAssetsRestApiExtensions.MaxQueryTake"/> features for a larger
/// <c>take</c>, its page and count SQL both <c>LIMIT</c>ed even without a <c>countLimit</c>, and a client that goes away
/// cancels the database command.
/// </summary>
public sealed class FeaturesQueryPostgresTests : IAsyncLifetime
{
    private const int Seeded = 12_000;
    private static readonly Uri QueryUri = new("/api/geoassets/features/query", UriKind.Relative);

    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static (string ConnectionString, Guid Org)? _seeded;

    private Guid _org;
    private string _connectionString = string.Empty;
    private readonly CommandCapturingInterceptor _commands = new();
    private readonly List<IAsyncDisposable> _owned = [];
    private IHost? _host;
    private HttpClient _client = null!;

    /// <summary>Records every command's SQL text.</summary>
    private sealed class CommandCapturingInterceptor : DbCommandInterceptor
    {
        private readonly Lock _gate = new();
        private readonly List<string> _texts = [];

        public IReadOnlyList<string> Texts
        {
            get
            {
                lock (_gate) return [.. _texts];
            }
        }

        public void Clear()
        {
            lock (_gate) _texts.Clear();
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            lock (_gate) _texts.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>One migrated database for every test here, created and seeded once: <see cref="Seeded"/> poles of one organization, set-based.</summary>
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
                SELECT 'p-' || lpad(i::text, 6, '0'), 'n' || i, 'pole', '', @org, now(), now(),
                       ST_SetSRID(ST_MakePoint(-49.9 + (i % 200) * 0.004, -19.9 + (i / 200) * 0.004), 4326), '{}'::jsonb, '[]'::jsonb
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
            Options(_connectionString).AddInterceptors(_commands).Options, shared, NullLoggerFactory.Instance, TimeProvider.System,
            tileInvalidation: null,
            catalogOptions: new OrganizationCatalogOptions { FullCacheMaxFeatures = queryBacked ? 0 : 1_000_000 });
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
                    services.AddSingleton(TimeProvider.System);
                    services.AddSingleton(catalog);
                    services.AddScoped<IAssetRenderQuery>(sp => new AssetProviderRenderQuery(sp.GetRequiredService<IAssetProvider>()));
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapGeoAssetsApi());
                });
            })
            .StartAsync();
        _client = _host.GetTestServer().CreateClient();
        _commands.Clear();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ATakeAboveTheMaximum_ReturnsAtMostTheMaximum_WithTheLimitsInTheSql(bool queryBacked)
    {
        await StartAsync(queryBacked);

        using var response = await _client.PostAsJsonAsync(QueryUri, new AssetQuery { AssetTypeId = "pole", Take = 5_000_000 }, GeoJsonSerializer.Options);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await response.Content.ReadFromJsonAsync<PagedResult<GeoFeature>>(GeoJsonSerializer.Options))!;

        page.Items.Should().HaveCount(GeoAssetsRestApiExtensions.MaxQueryTake);
        page.TotalCount.Should().Be(Seeded, "the catalog holds fewer features than the count limit, so the count is exact");
        page.TotalCountCapped.Should().BeFalse();
        _commands.Texts.Should().Contain(sql => !sql.Contains("count(", StringComparison.OrdinalIgnoreCase) && sql.Contains("LIMIT", StringComparison.Ordinal));
        _commands.Texts.Should().Contain(
            sql => sql.Contains("count(", StringComparison.OrdinalIgnoreCase) && sql.Contains("LIMIT", StringComparison.Ordinal),
            "the server applies its count limit although the client sent none");
    }

    [Fact]
    public async Task AClientThatGoesAway_CancelsTheDatabaseCommand()
    {
        await StartAsync(queryBacked: true);
        await using var locker = new NpgsqlConnection(_connectionString);
        await locker.OpenAsync();
        var lockTransaction = await locker.BeginTransactionAsync();
        try
        {
            await using (var lockTable = new NpgsqlCommand("LOCK TABLE geo_entity IN ACCESS EXCLUSIVE MODE", locker, lockTransaction))
                await lockTable.ExecuteNonQueryAsync();

            using var abandon = new CancellationTokenSource();
            var request = _client.PostAsJsonAsync(QueryUri, new AssetQuery { AssetTypeId = "pole", Take = 50 }, GeoJsonSerializer.Options, abandon.Token);
            (await BlockedReadsAsync(blocked => blocked > 0)).Should().BeGreaterThan(0, "the read waits for the table lock");

            await abandon.CancelAsync();
            var act = () => request;
            await act.Should().ThrowAsync<OperationCanceledException>();

            (await BlockedReadsAsync(blocked => blocked == 0)).Should().Be(0, "the request's abort cancelled the command on the server");
        }
        finally
        {
            await lockTransaction.RollbackAsync();
            await lockTransaction.DisposeAsync();
        }
    }

    /// <summary>Polls, for up to 30 s, the backends waiting on a lock for a <c>geo_entity</c> query until <paramref name="done"/>.</summary>
    private async Task<long> BlockedReadsAsync(Func<long, bool> done)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE pid <> pg_backend_pid() AND datname = current_database() AND wait_event_type = 'Lock' AND query LIKE '%geo_entity%'",
            connection);
        var started = TimeProvider.System.GetTimestamp();
        long blocked;
        do
        {
            blocked = (long)(await command.ExecuteScalarAsync())!;
            if (done(blocked)) return blocked;
            await Task.Delay(TimeSpan.FromMilliseconds(50), TimeProvider.System);
        }
        while (TimeProvider.System.GetElapsedTime(started) < TimeSpan.FromSeconds(30));

        return blocked;
    }
}

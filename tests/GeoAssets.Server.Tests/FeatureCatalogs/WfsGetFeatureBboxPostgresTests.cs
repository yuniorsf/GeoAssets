using System.Data.Common;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Providers;
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
/// XD01-478 end to end over PostGIS: a WFS GetFeature with a BBOX covering an organization's catalog larger than the page
/// cap — cached whole and served from PostgreSQL — returns at most COUNT (≤ <see cref="WfsEndpointExtensions.MaxFeaturesPerPage"/>)
/// features, pages with STARTINDEX, reports <c>numberMatched</c>/<c>numberReturned</c> exactly, its SQL carries the
/// <c>LIMIT</c> and the box, and a client that goes away cancels the database command. XD01-480: without a BBOX the count
/// is bounded too — exact for a catalog within <see cref="WfsEndpointExtensions.MaxNumberMatched"/>, <c>"unknown"</c> for
/// one above it, counted over <c>LIMIT</c> limit + 1.
/// </summary>
public sealed class WfsGetFeatureBboxPostgresTests : IAsyncLifetime
{
    private const int Seeded = 25_000;
    private const string Box = "&BBOX=-50,-20,-49,-19,urn:ogc:def:crs:EPSG::4326";

    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static (string ConnectionString, Guid Org, Guid LargeOrg)? _seeded;

    private Guid _org;
    private Guid _largeOrg;
    private string _connectionString = string.Empty;
    private readonly CommandCapturingInterceptor _commands = new();
    private readonly List<IAsyncDisposable> _owned = [];
    private IHost? _host;
    private HttpClient _client = null!;

    /// <summary>Records every command's SQL text and parameter values.</summary>
    private sealed class CommandCapturingInterceptor : DbCommandInterceptor
    {
        private readonly Lock _gate = new();
        private readonly List<(string Text, IReadOnlyList<object?> Parameters)> _commands = [];

        public IReadOnlyList<string> Texts
        {
            get
            {
                lock (_gate) return [.. _commands.Select(c => c.Text)];
            }
        }

        public IReadOnlyList<(string Text, IReadOnlyList<object?> Parameters)> Commands
        {
            get
            {
                lock (_gate) return [.. _commands];
            }
        }

        public void Clear()
        {
            lock (_gate) _commands.Clear();
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            var parameters = command.Parameters.Cast<DbParameter>().Select(p => p.Value).ToList();
            lock (_gate) _commands.Add((command.CommandText, parameters));
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>
    /// One migrated database for every test here, created and seeded once (the pattern of <c>ServerHostFixture</c>):
    /// <see cref="Seeded"/> features of one organization on a grid inside the box, set-based, and three outside it; and
    /// <see cref="WfsEndpointExtensions.MaxNumberMatched"/> + 1 features of a second organization, outside the box.
    /// </summary>
    private static async Task<(string ConnectionString, Guid Org, Guid LargeOrg)> SeededDatabaseAsync()
    {
        await SeedLock.WaitAsync();
        try
        {
            if (_seeded is { } seeded) return seeded;

            var org = Guid.NewGuid();
            var largeOrg = Guid.NewGuid();
            var connectionString = await OrganizationGrantsPostgres.CreateDatabaseAsync();
            await using (var db = new GeoAssetsDbContext(Options(connectionString).Options))
                await db.Database.MigrateAsync();

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "Description", "OrganizationId", "CreatedAt", "UpdatedAt", geom, custom_attributes, topology)
                SELECT kind || '-' || lpad(i::text, 6, '0'), 'n' || i, 'pole', '', @org, now(), now(),
                       ST_SetSRID(ST_MakePoint(lon, lat), 4326), '{}'::jsonb, '[]'::jsonb
                FROM (
                    SELECT 'in' AS kind, i, -49.9 + (i % 200) * 0.004 AS lon, -19.9 + (i / 200) * 0.004 AS lat FROM generate_series(0, @count - 1) AS i
                    UNION ALL SELECT 'out', i, -40.5, -10.5 FROM generate_series(0, 2) AS i
                ) AS rows;
                INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "Description", "OrganizationId", "CreatedAt", "UpdatedAt", geom, custom_attributes, topology)
                SELECT 'large-' || lpad(i::text, 6, '0'), 'l' || i, 'pole', '', @largeOrg, now(), now(),
                       ST_SetSRID(ST_MakePoint(-30 + (i % 400) * 0.001, -10 + (i / 400) * 0.001), 4326), '{}'::jsonb, '[]'::jsonb
                FROM generate_series(0, @largeCount - 1) AS i;
                ANALYZE geo_entity;
                """,
                connection);
            command.Parameters.AddWithValue("org", org);
            command.Parameters.AddWithValue("count", Seeded);
            command.Parameters.AddWithValue("largeOrg", largeOrg);
            command.Parameters.AddWithValue("largeCount", WfsEndpointExtensions.MaxNumberMatched + 1);
            await command.ExecuteNonQueryAsync();

            _seeded = (connectionString, org, largeOrg);
            return (connectionString, org, largeOrg);
        }
        finally
        {
            SeedLock.Release();
        }
    }

    public async Task InitializeAsync() => (_connectionString, _org, _largeOrg) = await SeededDatabaseAsync();

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

    private Task StartAsync(bool queryBacked) => StartAsync(queryBacked, _org);

    private async Task StartAsync(bool queryBacked, Guid org)
    {
        var shared = new ValidatingAssetProvider(await new PostgresProviderFactory(NullLoggerFactory.Instance, TimeProvider.System).CreateAsync(_connectionString));
        _owned.Add(shared);
        var catalogs = new PostgresOrganizationAssetCatalogs(
            Options(_connectionString).AddInterceptors(_commands).Options, shared, NullLoggerFactory.Instance, TimeProvider.System,
            tileInvalidation: null,
            catalogOptions: new OrganizationCatalogOptions { FullCacheMaxFeatures = queryBacked ? 0 : 1_000_000 });
        _owned.Add(catalogs);
        await catalogs.WarmAsync(org, CancellationToken.None);
        catalogs.IsQueryBacked(org).Should().Be(queryBacked);
        var catalog = catalogs.For(org);

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
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapWfsApi());
                });
            })
            .StartAsync();
        _client = _host.GetTestServer().CreateClient();
        _commands.Clear();
    }

    private static Uri FeatureUri(string query) => new($"/wfs?SERVICE=WFS&REQUEST=GetFeature{query}", UriKind.Relative);

    private async Task<(int Matched, int Returned, List<string> Ids)> GetFeatureAsync(string query)
    {
        var (matched, returned, ids) = await GetFeatureRawAsync(query);
        return (matched.GetInt32(), returned, ids);
    }

    private async Task<(JsonElement Matched, int Returned, List<string> Ids)> GetFeatureRawAsync(string query)
    {
        using var response = await _client.GetAsync(FeatureUri(query));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var ids = root.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("id").GetString()!).ToList();
        return (root.GetProperty("numberMatched").Clone(), root.GetProperty("numberReturned").GetInt32(), ids);
    }

    /// <summary>The one count command of the request — over a <c>LIMIT</c>ed subquery whose limit is the count limit + 1.</summary>
    private (string Text, IReadOnlyList<object?> Parameters) TheBoundedCountCommand()
    {
        var count = _commands.Commands.Should().ContainSingle(c => c.Text.Contains("count(", StringComparison.OrdinalIgnoreCase)).Subject;
        count.Text.Should().Contain("LIMIT");
        count.Parameters.Should().Contain(WfsEndpointExtensions.MaxNumberMatched + 1);
        return count;
    }

    [Fact]
    public async Task WithoutABox_ACatalogAboveTheCountLimit_ReportsNumberMatchedAsUnknown_CountingOverLimitPlusOne()
    {
        await StartAsync(queryBacked: true, _largeOrg);

        var (matched, returned, ids) = await GetFeatureRawAsync("&COUNT=5");

        matched.ValueKind.Should().Be(JsonValueKind.String);
        matched.GetString().Should().Be("unknown");
        returned.Should().Be(5);
        ids.Should().Equal("large-000000", "large-000001", "large-000002", "large-000003", "large-000004");
        TheBoundedCountCommand();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithoutABox_ACatalogWithinTheCountLimit_ReportsTheExactNumberMatched(bool queryBacked)
    {
        await StartAsync(queryBacked);

        var (matched, returned, ids) = await GetFeatureAsync("&COUNT=50000");

        matched.Should().Be(Seeded + 3, "the organization's catalog is within the count limit, so the count is exact");
        returned.Should().Be(WfsEndpointExtensions.MaxFeaturesPerPage);
        ids.Should().HaveCount(WfsEndpointExtensions.MaxFeaturesPerPage);
        TheBoundedCountCommand();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ABoxOverTheWholeCatalog_ReturnsAtMostThePageCap_WithTheLimitInTheSql(bool queryBacked)
    {
        await StartAsync(queryBacked);

        var (matched, returned, ids) = await GetFeatureAsync($"&COUNT=50000{Box}");

        returned.Should().Be(WfsEndpointExtensions.MaxFeaturesPerPage);
        ids.Should().HaveCount(WfsEndpointExtensions.MaxFeaturesPerPage).And.OnlyContain(id => id.StartsWith("in-", StringComparison.Ordinal));
        matched.Should().Be(Seeded, "the box holds fewer features than the count limit, so the count is exact");
        var pageSql = _commands.Texts.Should().ContainSingle(sql => !sql.Contains("count(", StringComparison.OrdinalIgnoreCase)).Subject;
        pageSql.Should().Contain("LIMIT").And.Contain("ST_Intersects");
        _commands.Texts.Should().Contain(sql => sql.Contains("count(", StringComparison.OrdinalIgnoreCase) && sql.Contains("LIMIT", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CountAndStartIndex_PageThroughTheBox(bool queryBacked)
    {
        await StartAsync(queryBacked);

        var first = await GetFeatureAsync($"&COUNT=10000{Box}");
        var last = await GetFeatureAsync($"&COUNT=10000&STARTINDEX=20000{Box}");
        var middle = await GetFeatureAsync($"&COUNT=3&STARTINDEX=10000{Box}");

        first.Returned.Should().Be(10_000);
        last.Returned.Should().Be(Seeded - 20_000);
        last.Ids.Should().HaveCount(Seeded - 20_000).And.NotIntersectWith(first.Ids);
        middle.Ids.Should().HaveCount(3).And.NotIntersectWith(first.Ids).And.NotIntersectWith(last.Ids);
        new[] { first.Matched, last.Matched, middle.Matched }.Should().AllBeEquivalentTo(Seeded);
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
            var request = _client.GetAsync(FeatureUri(Box), abandon.Token);
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

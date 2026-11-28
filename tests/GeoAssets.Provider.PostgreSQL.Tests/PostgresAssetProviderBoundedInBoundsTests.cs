using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// XD01-481: <see cref="IAssetProvider.GetInBoundsAsync(double, double, double, double, CancellationToken)"/> against the
/// collection's PostGIS, through an organization's catalog cached whole and one served from PostgreSQL. Served from
/// PostgreSQL, a box over more features than <see cref="OrganizationCatalogOptions.EnumerationLimit"/> is refused with
/// <see cref="CatalogEnumerationLimitException"/> after reading at most the limit + 1 rows (a <c>LIMIT</c>ed query), as
/// are the kernel's generic fallbacks over it; cached whole, the box is read whole as before; and cancelling the token
/// cancels the running database command.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresAssetProviderBoundedInBoundsTests(PostgresContainerFixture fixture)
{
    private const int Limit = 1_000;

    // A box no other test seeds in (their random worlds stay within ±170°/±80°).
    private static readonly BoundingBox Box = new(175, 85, 176, 86);

    private sealed record World(Guid Org, Guid Other, string AssetType, string Prefix);

    /// <summary>
    /// <paramref name="count"/> own features on a grid inside <see cref="Box"/>, set-based; plus, of the same type, two
    /// shared and two of another organization inside it and two own outside it.
    /// </summary>
    private async Task<World> SeedAsync(int count)
    {
        var world = new World(Guid.NewGuid(), Guid.NewGuid(), $"t-{Guid.NewGuid():N}"[..30], $"i{Guid.NewGuid():N}"[..9]);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "Description", "OrganizationId", "CreatedAt", "UpdatedAt", geom, custom_attributes, topology)
            SELECT @prefix || '-' || kind || '-' || lpad(i::text, 6, '0'), 'n' || i, @type, '', org, now(), now(),
                   ST_SetSRID(ST_MakePoint(lon, lat), 4326), '{}'::jsonb, '[]'::jsonb
            FROM (
                SELECT 'own' AS kind, i, @org AS org, 175.1 + (i % 100) * 0.008 AS lon, 85.1 + (i / 100) * 0.008 AS lat
                FROM generate_series(0, @count - 1) AS i
                UNION ALL SELECT 'shr', i, '00000000-0000-0000-0000-000000000000'::uuid, 175.5, 85.5 FROM generate_series(0, 1) AS i
                UNION ALL SELECT 'frg', i, @other, 175.5, 85.5 FROM generate_series(0, 1) AS i
                UNION ALL SELECT 'out', i, @org, 178.5, 88.5 FROM generate_series(0, 1) AS i
            ) AS rows;
            ANALYZE geo_entity;
            """,
            connection);
        command.Parameters.AddWithValue("prefix", world.Prefix);
        command.Parameters.AddWithValue("type", world.AssetType);
        command.Parameters.AddWithValue("org", world.Org);
        command.Parameters.AddWithValue("other", world.Other);
        command.Parameters.AddWithValue("count", count);
        await command.ExecuteNonQueryAsync();
        return world;
    }

    private async Task<(PostgresOrganizationAssetCatalogs Catalogs, ValidatingAssetProvider Shared, CommandCapturingInterceptor Commands)> CatalogsAsync(
        World world, bool queryBacked, bool warm = true)
    {
        var commands = new CommandCapturingInterceptor();
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>(fixture.CreateOptions()).AddInterceptors(commands).Options;
        var shared = new ValidatingAssetProvider(await new PostgresProviderFactory(NullLoggerFactory.Instance, TimeProvider.System).CreateAsync(fixture.ConnectionString));
        var catalogs = new PostgresOrganizationAssetCatalogs(
            options, shared, NullLoggerFactory.Instance, TimeProvider.System, tileInvalidation: null,
            catalogOptions: new OrganizationCatalogOptions { FullCacheMaxFeatures = queryBacked ? 0 : 1_000_000, EnumerationLimit = Limit });
        if (warm)
        {
            await catalogs.WarmAsync(world.Org, CancellationToken.None);
            catalogs.IsQueryBacked(world.Org).Should().Be(queryBacked);
        }

        commands.CommandTexts.Clear();
        return (catalogs, shared, commands);
    }

    private async Task DeleteAsync(World world)
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.GeoEntities.IgnoreQueryFilters().Where(r => r.AssetTypeId == world.AssetType).ExecuteDeleteAsync();
    }

    private static Task<IReadOnlyList<GeoFeature>> InBoxAsync(IAssetProvider catalog, CancellationToken ct = default) =>
        catalog.GetInBoundsAsync(Box.MinLon, Box.MinLat, Box.MaxLon, Box.MaxLat, ct);

    [Fact]
    public async Task QueryBacked_ABoxOverMoreFeaturesThanTheLimit_IsRefused_AfterALimitedRead()
    {
        var world = await SeedAsync(Limit + 500);
        var (catalogs, shared, commands) = await CatalogsAsync(world, queryBacked: true);
        try
        {
            var catalog = catalogs.For(world.Org);

            var withToken = () => InBoxAsync(catalog, CancellationToken.None);
            var withoutToken = () => catalog.GetInBoundsAsync(Box.MinLon, Box.MinLat, Box.MaxLon, Box.MaxLat);
            var asJson = () => catalog.GetInBoundsJsonAsync(Box.MinLon, Box.MinLat, Box.MaxLon, Box.MaxLat);

            foreach (var read in new[] { withToken, withoutToken })
            {
                (await read.Should().ThrowAsync<CatalogEnumerationLimitException>()).Which.Should().Match<CatalogEnumerationLimitException>(
                    e => e.Operation == "GetInBoundsAsync" && e.Limit == Limit);
            }

            await asJson.Should().ThrowAsync<CatalogEnumerationLimitException>();
            commands.CommandTexts.Should().NotBeEmpty()
                .And.OnlyContain(sql => sql.Contains("ST_Intersects", StringComparison.Ordinal) && sql.Contains("LIMIT", StringComparison.Ordinal),
                    "every read stops after the limit + 1 rows instead of reading the box whole");
        }
        finally
        {
            await catalogs.DisposeAsync();
            await shared.DisposeAsync();
            await DeleteAsync(world);
        }
    }

    [Fact]
    public async Task QueryBacked_ABoxOverExactlyTheLimit_IsServed_OwnAndShared()
    {
        var world = await SeedAsync(Limit - 2);
        var (catalogs, shared, _) = await CatalogsAsync(world, queryBacked: true);
        try
        {
            var features = await InBoxAsync(catalogs.For(world.Org));

            features.Should().HaveCount(Limit, "the organization's own features in the box and the two shared ones");
            features.Should().OnlyContain(f => f.Properties.OrganizationId == world.Org || f.Properties.OrganizationId == Guid.Empty);
            features.Select(f => f.Id).Should().OnlyHaveUniqueItems()
                .And.NotContain(id => id.Contains("-frg-", StringComparison.Ordinal) || id.Contains("-out-", StringComparison.Ordinal));
        }
        finally
        {
            await catalogs.DisposeAsync();
            await shared.DisposeAsync();
            await DeleteAsync(world);
        }
    }

    [Fact]
    public async Task CachedWhole_ABoxOverMoreFeaturesThanTheLimit_IsReadWhole_AsBefore()
    {
        var world = await SeedAsync(Limit + 500);
        var (catalogs, shared, commands) = await CatalogsAsync(world, queryBacked: false);
        try
        {
            var catalog = catalogs.For(world.Org);

            (await InBoxAsync(catalog)).Should().HaveCount(Limit + 502);
            (await catalog.GetInBoundsAsync(Box.MinLon, Box.MinLat, Box.MaxLon, Box.MaxLat)).Should().HaveCount(Limit + 502);
            commands.CommandTexts.Should().NotBeEmpty().And.OnlyContain(sql => !sql.Contains("LIMIT", StringComparison.Ordinal));
        }
        finally
        {
            await catalogs.DisposeAsync();
            await shared.DisposeAsync();
            await DeleteAsync(world);
        }
    }

    [Fact]
    public async Task NotYetSized_TheReadSizesTheCatalogAsynchronously_ThenRefusesALargeBox()
    {
        var world = await SeedAsync(Limit + 500);
        var (catalogs, shared, _) = await CatalogsAsync(world, queryBacked: true, warm: false);
        try
        {
            var catalog = catalogs.For(world.Org);
            catalogs.IsQueryBacked(world.Org).Should().BeFalse("nothing sized the catalog yet");

            var read = () => InBoxAsync(catalog);

            await read.Should().ThrowAsync<CatalogEnumerationLimitException>();
            catalogs.IsQueryBacked(world.Org).Should().BeTrue("the read counted the organization's rows and switched it to PostgreSQL");
        }
        finally
        {
            await catalogs.DisposeAsync();
            await shared.DisposeAsync();
            await DeleteAsync(world);
        }
    }

    [Fact]
    public async Task TheKernelsGenericFallbacks_OverAQueryBackedCatalog_AreRefused_AndOverAWholeCacheBounded()
    {
        var world = await SeedAsync(Limit + 500);
        var (queried, queriedShared, _) = await CatalogsAsync(world, queryBacked: true);
        var (cached, cachedShared, _) = await CatalogsAsync(world, queryBacked: false);
        try
        {
            var filter = new ReferenceFeatureFilter(null, null, 10);
            var refusedReference = () => new OrganizationCatalogReferenceFeatureQuery(queried).GetInBoundsAsync(world.Org, Box, filter, CancellationToken.None);
            var refusedRender = () => new AssetProviderRenderQuery(queried.For(world.Org)).GetRenderableAsync(Box, new RenderQueryOptions { MaxCount = 10 }, CancellationToken.None);
            var refusedInfo = () => new AssetProviderRenderQuery(queried.For(world.Org)).GetFeatureInfoAsync(Box, 10, CancellationToken.None);

            await refusedReference.Should().ThrowAsync<CatalogEnumerationLimitException>();
            await refusedRender.Should().ThrowAsync<CatalogEnumerationLimitException>();
            await refusedInfo.Should().ThrowAsync<CatalogEnumerationLimitException>();
            (await new OrganizationCatalogReferenceFeatureQuery(cached).GetInBoundsAsync(world.Org, Box, filter, CancellationToken.None))
                .Should().HaveCount(10);
            (await new AssetProviderRenderQuery(cached.For(world.Org)).GetRenderableAsync(Box, new RenderQueryOptions { MaxCount = 10 }, CancellationToken.None))
                .Should().HaveCount(10);
        }
        finally
        {
            await queried.DisposeAsync();
            await queriedShared.DisposeAsync();
            await cached.DisposeAsync();
            await cachedShared.DisposeAsync();
            await DeleteAsync(world);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingTheToken_CancelsTheRunningDatabaseCommand_AndLeavesNoBackendBehind(bool queryBacked)
    {
        var world = await SeedAsync(30);
        var (catalogs, shared, _) = await CatalogsAsync(world, queryBacked);
        await using var locker = new NpgsqlConnection(fixture.ConnectionString);
        await locker.OpenAsync();
        var lockTransaction = await locker.BeginTransactionAsync();
        try
        {
            await using (var lockTable = new NpgsqlCommand("LOCK TABLE geo_entity IN ACCESS EXCLUSIVE MODE", locker, lockTransaction))
                await lockTable.ExecuteNonQueryAsync();

            using var cts = new CancellationTokenSource();
            var read = InBoxAsync(catalogs.For(world.Org), cts.Token);
            (await WaitForAsync(blocked => blocked > 0)).Should().BeGreaterThan(0, "the read waits for the table lock");

            await cts.CancelAsync();
            var act = () => read.WaitAsync(TimeSpan.FromSeconds(30));

            await act.Should().ThrowAsync<OperationCanceledException>();
            (await WaitForAsync(blocked => blocked == 0)).Should().Be(0, "the server cancelled the command rather than leaving it waiting");
        }
        finally
        {
            await lockTransaction.RollbackAsync();
            await lockTransaction.DisposeAsync();
            await catalogs.DisposeAsync();
            await shared.DisposeAsync();
            await DeleteAsync(world);
        }
    }

    [Fact]
    public async Task AnAlreadyCancelledToken_NeverReachesTheDatabase()
    {
        var world = await SeedAsync(3);
        var (catalogs, shared, commands) = await CatalogsAsync(world, queryBacked: true);
        try
        {
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            var read = () => InBoxAsync(catalogs.For(world.Org), cts.Token);

            await read.Should().ThrowAsync<OperationCanceledException>();
            commands.CommandTexts.Should().BeEmpty();
        }
        finally
        {
            await catalogs.DisposeAsync();
            await shared.DisposeAsync();
            await DeleteAsync(world);
        }
    }

    /// <summary>Polls, for up to 30 s, the number of backends waiting on a lock for a <c>geo_entity</c> query until <paramref name="done"/>.</summary>
    private async Task<long> WaitForAsync(Func<long, bool> done)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE pid <> pg_backend_pid() AND wait_event_type = 'Lock' AND query LIKE '%geo_entity%'",
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

using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// XD01-478: <see cref="IAssetProvider.GetPageAsync(AssetQuery, CancellationToken)"/> with <see cref="AssetQuery.Bounds"/>
/// against the collection's PostGIS, through an organization's catalog cached whole and one served from PostgreSQL
/// (<see cref="OrganizationCatalogOptions.FullCacheMaxFeatures"/>): the box is an <c>ST_Intersects</c> in the same SQL as
/// the page's <c>LIMIT</c>, <see cref="AssetQuery.CountLimit"/> counts over a <c>LIMIT</c>ed subquery, the organization
/// scoping holds, and cancelling the token cancels the running database command.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresAssetProviderBoundedPageTests(PostgresContainerFixture fixture)
{
    private const int Seeded = 25_000;
    private const int Page = 10_000;

    private sealed record World(Guid Org, Guid Other, string AssetType, string Prefix, BoundingBox Box);

    /// <summary>
    /// <paramref name="count"/> own features on a grid inside a fresh 1° box, set-based; plus, of the same type, two shared
    /// and two of another organization inside it and two own outside it.
    /// </summary>
    private async Task<World> SeedAsync(int count)
    {
        var world = new World(
            Guid.NewGuid(), Guid.NewGuid(), $"t-{Guid.NewGuid():N}"[..30], $"b{Guid.NewGuid():N}"[..9],
            new BoundingBox(-50, -20, -49, -19));
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "Description", "OrganizationId", "CreatedAt", "UpdatedAt", geom, custom_attributes, topology)
            SELECT @prefix || '-' || kind || '-' || lpad(i::text, 6, '0'), 'n' || i, @type, '', org, now(), now(),
                   ST_SetSRID(ST_MakePoint(lon, lat), 4326), '{}'::jsonb, '[]'::jsonb
            FROM (
                SELECT 'own' AS kind, i, @org AS org, -49.9 + (i % 200) * 0.004 AS lon, -19.9 + (i / 200) * 0.004 AS lat
                FROM generate_series(0, @count - 1) AS i
                UNION ALL SELECT 'shr', i, '00000000-0000-0000-0000-000000000000'::uuid, -49.5, -19.5 FROM generate_series(0, 1) AS i
                UNION ALL SELECT 'frg', i, @other, -49.5, -19.5 FROM generate_series(0, 1) AS i
                UNION ALL SELECT 'out', i, @org, -45.5, -15.5 FROM generate_series(0, 1) AS i
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
        World world, bool queryBacked)
    {
        var commands = new CommandCapturingInterceptor();
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>(fixture.CreateOptions()).AddInterceptors(commands).Options;
        var shared = new ValidatingAssetProvider(await new PostgresProviderFactory(NullLoggerFactory.Instance, TimeProvider.System).CreateAsync(fixture.ConnectionString));
        var catalogs = new PostgresOrganizationAssetCatalogs(
            options, shared, NullLoggerFactory.Instance, TimeProvider.System, tileInvalidation: null,
            catalogOptions: new OrganizationCatalogOptions { FullCacheMaxFeatures = queryBacked ? 0 : 1_000_000, EnumerationLimit = 1_000 });
        await catalogs.WarmAsync(world.Org, CancellationToken.None);
        catalogs.IsQueryBacked(world.Org).Should().Be(queryBacked);
        commands.CommandTexts.Clear();
        return (catalogs, shared, commands);
    }

    private async Task DeleteAsync(World world)
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.GeoEntities.IgnoreQueryFilters().Where(r => r.AssetTypeId == world.AssetType).ExecuteDeleteAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ABoxOverMoreFeaturesThanThePage_ReturnsOnePage_WithTheLimitAndTheBoxInTheSql(bool queryBacked)
    {
        var world = await SeedAsync(Seeded);
        var (catalogs, shared, commands) = await CatalogsAsync(world, queryBacked);
        try
        {
            var page = await catalogs.For(world.Org).GetPageAsync(
                new AssetQuery { AssetTypeId = world.AssetType, Bounds = world.Box, Take = Page }, CancellationToken.None);

            page.Items.Should().HaveCount(Page);
            page.TotalCount.Should().Be(Seeded + 2, "the organization's own features in the box and the shared ones — not another organization's, nor any outside the box");
            page.Items.Should().OnlyContain(f => f.Properties.OrganizationId == world.Org || f.Properties.OrganizationId == Guid.Empty);
            var pageSql = commands.CommandTexts.Should().ContainSingle(sql => !sql.Contains("count(", StringComparison.OrdinalIgnoreCase)).Subject;
            pageSql.Should().Contain("LIMIT").And.Contain("ST_Intersects");
        }
        finally
        {
            await catalogs.DisposeAsync();
            await shared.DisposeAsync();
            await DeleteAsync(world);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SkipPagesThroughTheBox_InIdOrder_WithoutOverlap(bool queryBacked)
    {
        var world = await SeedAsync(30);
        var (catalogs, shared, _) = await CatalogsAsync(world, queryBacked);
        try
        {
            var catalog = catalogs.For(world.Org);
            var query = new AssetQuery { AssetTypeId = world.AssetType, Bounds = world.Box, Take = 20 };

            var first = await catalog.GetPageAsync(query, CancellationToken.None);
            var second = await catalog.GetPageAsync(query with { Skip = 20 }, CancellationToken.None);

            first.Items.Should().HaveCount(20);
            second.Items.Should().HaveCount(12);
            first.TotalCount.Should().Be(32);
            second.TotalCount.Should().Be(32);
            var ids = first.Items.Concat(second.Items).Select(f => f.Id).ToList();
            ids.Should().OnlyHaveUniqueItems().And.HaveCount(32);
            ids.Should().NotContain(id => id.Contains("-frg-", StringComparison.Ordinal) || id.Contains("-out-", StringComparison.Ordinal));
        }
        finally
        {
            await catalogs.DisposeAsync();
            await shared.DisposeAsync();
            await DeleteAsync(world);
        }
    }

    [Theory]
    [InlineData(100, 101)]
    [InlineData(501, 502)]
    [InlineData(502, 502)]
    [InlineData(1_000, 502)]
    public async Task ACountLimit_StopsCountingAfterIt_InALimitedSubquery(int countLimit, int expectedTotal)
    {
        var world = await SeedAsync(500);
        var (catalogs, shared, commands) = await CatalogsAsync(world, queryBacked: true);
        try
        {
            var page = await catalogs.For(world.Org).GetPageAsync(
                new AssetQuery { AssetTypeId = world.AssetType, Bounds = world.Box, CountLimit = countLimit, Take = 5 }, CancellationToken.None);

            page.TotalCount.Should().Be(expectedTotal);
            page.Items.Should().HaveCount(5);
            commands.CommandTexts.Should().Contain(sql => sql.Contains("count(*)", StringComparison.OrdinalIgnoreCase) && sql.Contains("LIMIT", StringComparison.Ordinal));
        }
        finally
        {
            await catalogs.DisposeAsync();
            await shared.DisposeAsync();
            await DeleteAsync(world);
        }
    }

    [Fact]
    public async Task WithoutBoundsOrCountLimit_ThePageAndItsExactCountAreUnchanged()
    {
        var world = await SeedAsync(30);
        var (catalogs, shared, _) = await CatalogsAsync(world, queryBacked: true);
        try
        {
            var page = await catalogs.For(world.Org).GetPageAsync(new AssetQuery { AssetTypeId = world.AssetType, Take = 100 }, CancellationToken.None);

            page.TotalCount.Should().Be(34, "the box no longer leaves out the two own features outside it");
            page.Items.Should().HaveCount(34);
        }
        finally
        {
            await catalogs.DisposeAsync();
            await shared.DisposeAsync();
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
            var read = catalogs.For(world.Org).GetPageAsync(
                new AssetQuery { AssetTypeId = world.AssetType, Bounds = world.Box, CountLimit = 100, Take = 10 }, cts.Token);
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

    /// <summary>Polls, for up to 30 s, the number of backends waiting on a lock for a <c>geo_entity</c> query until <paramref name="done"/>.</summary>
    private async Task<long> WaitForAsync(Func<long, bool> done)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE pid <> pg_backend_pid() AND wait_event_type = 'Lock' AND query LIKE '%geo_entity%'",
            connection);
        var deadline = TimeProvider.System.GetTimestamp();
        long blocked;
        do
        {
            blocked = (long)(await command.ExecuteScalarAsync())!;
            if (done(blocked)) return blocked;
            await Task.Delay(TimeSpan.FromMilliseconds(50), TimeProvider.System);
        }
        while (TimeProvider.System.GetElapsedTime(deadline) < TimeSpan.FromSeconds(30));

        return blocked;
    }
}

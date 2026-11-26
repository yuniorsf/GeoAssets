using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// <see cref="PostgresAssetProvider.GetInViewportAsync"/> — the <c>GET /features/bounds</c> read (XD01-430) — against the
/// collection's migrated PostGIS, through the request's catalog (<see cref="PostgresOrganizationAssetCatalogs.For"/>):
/// the zoom's hidden asset types, the ordinal-id order and the <c>LIMIT</c> run in SQL, the organization scoping still
/// holds, and the answer is the one <see cref="IAssetProvider"/>'s in-memory default gives over the same catalog.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresAssetProviderViewportTests(PostgresContainerFixture fixture)
{
    private static async Task<string[]> ViewportIdsAsync(IAssetProvider catalog, BoundingBox bounds, int? zoom, int maxCount = 100) =>
        [.. (await catalog.GetInViewportAsync(new ViewportQuery(bounds, zoom), maxCount)).Select(f => f.Id)];

    private static PostgresAssetProvider TenantlessProvider(DbContextOptions<GeoAssetsDbContext> options) =>
        new(new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(14)]
    [InlineData(18)]
    public async Task AsOrganizationA_Zoom_LeavesOutTheTypesItHides_InOrdinalIdOrder(int zoom)
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);

        var ids = await ViewportIdsAsync(world.Catalogs.For(world.OrgA), world.Whole, zoom);

        ids.Should().Equal(world.VisibleToAAt(zoom).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AsOrganizationA_NoZoom_ReturnsEveryFeatureItSees()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);

        var features = await world.Catalogs.For(world.OrgA).GetInViewportAsync(new ViewportQuery(world.Whole), 100);

        features.Select(f => f.Id).Should().Equal(world.VisibleToA.Order(StringComparer.Ordinal));
        var plain = features.Single(f => string.Equals(f.Id, world.PlainId, StringComparison.Ordinal));
        plain.Properties.Description.Should().Be($"description {world.PlainId}", "the bounds read keeps returning whole features");
        plain.Properties.OrganizationId.Should().Be(world.OrgA);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(18)]
    public async Task AsOrganizationB_NeverSeesOrganizationAsFeatures(int? zoom)
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);

        var ids = await ViewportIdsAsync(world.Catalogs.For(world.OrgB), world.Whole, zoom);

        ids.Should().BeEquivalentTo([world.OrgBId, world.SharedId]);
    }

    [Fact]
    public async Task WithoutAnOrganization_SeesTheSharedFeaturesOnly()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);

        var ids = await ViewportIdsAsync(world.Catalogs.For(Guid.Empty), world.Whole, 18);

        ids.Should().Equal(world.SharedId);
    }

    [Fact]
    public async Task MaxCount_TakesTheFirstByOrdinalId_AfterTheZoomFilter()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var catalog = world.Catalogs.For(world.OrgA);

        (await ViewportIdsAsync(catalog, world.Whole, 18, maxCount: 2))
            .Should().Equal(world.VisibleToAAt(18).Order(StringComparer.Ordinal).Take(2));
        (await ViewportIdsAsync(catalog, world.Whole, null, maxCount: 1))
            .Should().Equal(world.VisibleToA.Order(StringComparer.Ordinal).Take(1));
    }

    [Fact]
    public async Task ForEveryZoomAndSeveralCaps_AnswersWhatTheInMemoryRuleAnswersOverTheSameCatalog()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var catalog = world.Catalogs.For(world.OrgA);
        var inBounds = await catalog.GetInBoundsAsync(world.Whole.MinLon, world.Whole.MinLat, world.Whole.MaxLon, world.Whole.MaxLat);

        foreach (var zoom in Enumerable.Range(0, 21))
        {
            var hidden = AssetTypeZoomRange.TypeIdsHiddenAt(catalog.GetAssetTypes(), zoom);
            foreach (var maxCount in new[] { 1, 3, 100 })
            {
                var expected = inBounds
                    .Where(f => !hidden.Contains(f.Properties.AssetTypeId))
                    .Select(f => f.Id)
                    .Order(StringComparer.Ordinal)
                    .Take(maxCount);

                (await ViewportIdsAsync(catalog, world.Whole, zoom, maxCount)).Should().Equal(expected, "at zoom {0} with at most {1}", zoom, maxCount);
            }
        }
    }

    [Fact]
    public async Task TheZoomFilterTheOrderAndTheLimit_RunInTheSql()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var interceptor = new CommandCapturingInterceptor();
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>(fixture.CreateOptions()).AddInterceptors(interceptor).Options;
        await using var sut = TenantlessProvider(options);

        var features = await sut.GetInViewportAsync(new ViewportQuery(world.Whole, 12), 7);

        features.Select(f => f.Id).Should().Equal(world.SharedId);
        interceptor.CommandTexts.Should().HaveCount(2, "the hidden types, then the features — nothing read to be filtered or cut in memory");
        interceptor.CommandTexts[0].Should().Contain("asset_type").And.Contain("MinZoom").And.Contain("MaxZoom");
        interceptor.CommandTexts[1].Should().Contain("geo_entity").And.Contain("ST_Intersects").And.Contain("ANY")
            .And.Contain("ORDER BY").And.Contain("COLLATE \"C\"").And.Contain("LIMIT");
    }

    [Fact]
    public async Task WithoutAZoom_SkipsTheAssetTypeQuery()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var interceptor = new CommandCapturingInterceptor();
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>(fixture.CreateOptions()).AddInterceptors(interceptor).Options;
        await using var sut = TenantlessProvider(options);

        await sut.GetInViewportAsync(new ViewportQuery(world.Whole), 7);

        interceptor.CommandTexts.Should().ContainSingle().Which.Should().Contain("geo_entity").And.NotContain("asset_type");
    }

    [Fact]
    public async Task AlreadyCancelled_ThrowsWithoutTouchingTheDatabase()
    {
        await using var sut = TenantlessProvider(UnreachableOptions());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => sut.GetInViewportAsync(new ViewportQuery(new BoundingBox(0, 0, 1, 1), 12), 10, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositiveMaxCount_ReturnsNothingWithoutTouchingTheDatabase(int maxCount)
    {
        await using var sut = TenantlessProvider(UnreachableOptions());

        var features = await sut.GetInViewportAsync(new ViewportQuery(new BoundingBox(0, 0, 1, 1), 12), maxCount);

        features.Should().BeEmpty();
    }

    /// <summary>A database no statement can reach: any read fails, so a result proves none was sent.</summary>
    private static DbContextOptions<GeoAssetsDbContext> UnreachableOptions() =>
        new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Timeout=1", npgsql => npgsql.UseNetTopologySuite())
            .Options;
}

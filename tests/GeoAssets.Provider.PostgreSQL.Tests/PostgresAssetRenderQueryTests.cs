using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using GeoAssets.Provider.PostgreSQL.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// <see cref="PostgresAssetRenderQuery"/> and <see cref="PostgresRenderQueryServiceExtensions.AddPostgresAssetRenderQuery"/>
/// (XD01-372) against the collection's migrated PostGIS instance: tenant scoping, the asset type filter, the
/// cancellation and <c>maxCount</c> short-circuits that never reach the database, and the registration.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresAssetRenderQueryTests(PostgresContainerFixture fixture)
{
    private PostgresAssetRenderQuery NewSut(RenderQueryWorld world, Guid? organization) =>
        new(
            fixture.CreateOptions(),
            organization is { } id ? new FakeTenantContext(id) : null,
            world.Catalogs.For(organization ?? Guid.Empty),
            TimeProvider.System);

    /// <summary>A query whose database is unreachable: any statement it sends fails, so a result proves none was sent.</summary>
    private static PostgresAssetRenderQuery NewUnreachableSut(RenderQueryWorld world) =>
        new(
            new DbContextOptionsBuilder<GeoAssetsDbContext>()
                .UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Timeout=1", npgsql => npgsql.UseNetTopologySuite())
                .Options,
            new FakeTenantContext(world.OrgA),
            world.Catalogs.For(world.OrgA),
            TimeProvider.System);

    private static async Task<string[]> RenderedIdsAsync(PostgresAssetRenderQuery sut, BoundingBox bbox, RenderQueryOptions? options = null) =>
        [.. (await sut.GetRenderableAsync(bbox, options ?? new RenderQueryOptions(), CancellationToken.None))
            .Select(f => f.Id)
            .Order(StringComparer.Ordinal)];

    private static async Task<string[]> InfoIdsAsync(PostgresAssetRenderQuery sut, BoundingBox bbox) =>
        [.. (await sut.GetFeatureInfoAsync(bbox, 100, CancellationToken.None)).Select(f => f.Id)];

    // ── Cancellation and count short-circuits ─────────────────────────────────

    [Fact]
    public async Task GetRenderableAsync_PreCancelledToken_ReturnsEmptyWithoutTouchingTheDatabase()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await NewUnreachableSut(world).GetRenderableAsync(world.Whole, new RenderQueryOptions(), cts.Token);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFeatureInfoAsync_PreCancelledToken_ReturnsEmptyWithoutTouchingTheDatabase()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await NewUnreachableSut(world).GetFeatureInfoAsync(world.Whole, 10, cts.Token);

        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetFeatureInfoAsync_NonPositiveMaxCount_ReturnsEmptyWithoutTouchingTheDatabase(int maxCount)
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);

        var result = await NewUnreachableSut(world).GetFeatureInfoAsync(world.Whole, maxCount, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFeatureInfoAsync_MaxCount_TakesTheFirstFeaturesByOrdinalId()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var sut = NewSut(world, world.OrgA);

        var result = await sut.GetFeatureInfoAsync(world.Whole, 2, CancellationToken.None);

        result.Select(f => f.Id).Should().Equal(world.VisibleToA.Order(StringComparer.Ordinal).Take(2));
    }

    // ── Filtering ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRenderableAsync_AssetTypeFilter_KeepsOnlyThatType()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var sut = NewSut(world, world.OrgA);

        var ids = await RenderedIdsAsync(sut, world.Whole, new RenderQueryOptions { AssetTypeId = world.RuledType.Id.ToString() });

        ids.Should().BeEquivalentTo([world.OverrideId, world.RuleId, world.RuledNoMatchId]);
    }

    [Fact]
    public async Task GetRenderableAsync_UnknownAssetTypeFilter_IsEmpty()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var sut = NewSut(world, world.OrgA);

        var ids = await RenderedIdsAsync(sut, world.Whole, new RenderQueryOptions { AssetTypeId = Guid.NewGuid().ToString() });

        ids.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRenderableAsync_BoxWithoutFeatures_IsEmpty_AndFeaturesWithoutGeometryAreNeverReturned()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var sut = NewSut(world, world.OrgA);

        (await RenderedIdsAsync(sut, world.Empty)).Should().BeEmpty();
        (await RenderedIdsAsync(sut, world.Whole)).Should().NotContain(world.GeometrylessId);
        (await InfoIdsAsync(sut, world.Whole)).Should().NotContain(world.GeometrylessId);
    }

    // ── Zoom and MaxCount in SQL (XD01-430) ───────────────────────────────────

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(14)]
    [InlineData(18)]
    public async Task GetRenderableAsync_Zoom_LeavesOutTheTypesItHides(int zoom)
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var sut = NewSut(world, world.OrgA);

        var ids = await RenderedIdsAsync(sut, world.Whole, new RenderQueryOptions { Zoom = zoom });

        ids.Should().BeEquivalentTo(world.VisibleToAAt(zoom));
    }

    [Fact]
    public async Task GetRenderableAsync_Zoom_AsOrganizationB_StillNeverSeesOrganizationAs()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var sut = NewSut(world, world.OrgB);

        var ids = await RenderedIdsAsync(sut, world.Whole, new RenderQueryOptions { Zoom = 18 });

        ids.Should().BeEquivalentTo([world.OrgBId, world.SharedId]);
    }

    [Fact]
    public async Task GetRenderableAsync_MaxCount_TakesTheFirstFeaturesByOrdinalId()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var sut = NewSut(world, world.OrgA);

        var result = await sut.GetRenderableAsync(world.Whole, new RenderQueryOptions { MaxCount = 3 }, CancellationToken.None);

        result.Select(f => f.Id).Should().Equal(world.VisibleToA.Order(StringComparer.Ordinal).Take(3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetRenderableAsync_NonPositiveMaxCount_ReturnsEmptyWithoutTouchingTheDatabase(int maxCount)
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);

        var result = await NewUnreachableSut(world).GetRenderableAsync(world.Whole, new RenderQueryOptions { MaxCount = maxCount }, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRenderableAsync_ZoomAndMaxCount_RunInTheSql()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var interceptor = new CommandCapturingInterceptor();
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>(fixture.CreateOptions()).AddInterceptors(interceptor).Options;
        var sut = new PostgresAssetRenderQuery(options, new FakeTenantContext(world.OrgA), world.Catalogs.For(world.OrgA), TimeProvider.System);

        await sut.GetRenderableAsync(world.Whole, new RenderQueryOptions { Zoom = 12, MaxCount = 50 }, CancellationToken.None);

        interceptor.CommandTexts.Should().Contain(sql => sql.Contains("asset_type", StringComparison.Ordinal) && sql.Contains("MinZoom", StringComparison.Ordinal));
        var read = interceptor.CommandTexts.Should().ContainSingle(sql => sql.Contains("geo_entity", StringComparison.Ordinal)).Subject;
        read.Should().Contain("ST_Intersects").And.Contain("ANY").And.Contain("ORDER BY").And.Contain("COLLATE \"C\"").And.Contain("LIMIT");
    }

    // ── Tenant scoping ────────────────────────────────────────────────────────

    [Fact]
    public async Task Reads_AsOrganizationA_SeeItsFeaturesAndTheSharedOnes_NeverOrganizationBs()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var sut = NewSut(world, world.OrgA);

        var rendered = await RenderedIdsAsync(sut, world.Whole);
        var info = await InfoIdsAsync(sut, world.Whole);

        rendered.Should().BeEquivalentTo(world.VisibleToA).And.NotContain(world.OrgBId);
        info.Should().BeEquivalentTo(world.VisibleToA).And.NotContain(world.OrgBId);
    }

    [Fact]
    public async Task Reads_AsOrganizationB_SeeItsFeatureAndTheSharedOne_NeverOrganizationAs()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var sut = NewSut(world, world.OrgB);

        var rendered = await RenderedIdsAsync(sut, world.Whole);
        var info = await InfoIdsAsync(sut, world.Whole);

        rendered.Should().BeEquivalentTo([world.OrgBId, world.SharedId]);
        info.Should().BeEquivalentTo([world.OrgBId, world.SharedId]);
    }

    [Fact]
    public async Task Reads_WithoutATenant_SeeTheSharedFeaturesOnly()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var sut = NewSut(world, null);

        var rendered = await RenderedIdsAsync(sut, world.Whole);
        var info = await InfoIdsAsync(sut, world.Whole);

        rendered.Should().BeEquivalentTo([world.SharedId]);
        info.Should().BeEquivalentTo([world.SharedId]);
    }

    // ── Registration ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AddPostgresAssetRenderQuery_ReplacesAnEarlierRegistration_AsAScopedService()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var services = new ServiceCollection();
        services.AddScoped<IAssetRenderQuery>(sp => new AssetProviderRenderQuery(sp.GetRequiredService<IAssetProvider>()));
        services.AddSingleton(world.Catalogs.For(world.OrgA));

        services.AddPostgresAssetRenderQuery(fixture.ConnectionString);

        var descriptor = services.Where(d => d.ServiceType == typeof(IAssetRenderQuery)).Should().ContainSingle().Subject;
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
        await using var provider = services.BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var inFirst = first.ServiceProvider.GetRequiredService<IAssetRenderQuery>();
        inFirst.Should().BeOfType<PostgresAssetRenderQuery>();
        first.ServiceProvider.GetRequiredService<IAssetRenderQuery>().Should().BeSameAs(inFirst);
        second.ServiceProvider.GetRequiredService<IAssetRenderQuery>().Should().NotBeSameAs(inFirst);
    }

    [Fact]
    public void AddPostgresAssetRenderQuery_BlankConnectionString_Throws()
    {
        var act = () => new ServiceCollection().AddPostgresAssetRenderQuery(" ");

        act.Should().Throw<ArgumentException>();
    }
}

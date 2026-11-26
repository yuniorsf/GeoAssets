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
/// <see cref="PostgresVectorTileSource"/> and <see cref="PostgresVectorTileServiceExtensions.AddPostgresVectorTileSource"/>
/// (XD01-432) against the collection's migrated PostGIS instance, each tile decoded (<see cref="MvtTile"/>): the layers
/// and their order, the features' properties and positions, organization scoping, zoom hiding (the asset type's range and
/// the LOD policy), aggregation, the feature budget, empty tiles, and the short-circuits that never reach the database.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresVectorTileSourceTests(PostgresContainerFixture fixture)
{
    private PostgresVectorTileSource NewSut(Guid? organization, ILodPolicy? policy = null, int budget = PostgresVectorTileSource.DefaultMaxFeaturesPerTile) =>
        new(fixture.CreateOptions(), organization is { } id ? new FakeTenantContext(id) : null, policy, TimeProvider.System, budget);

    /// <summary>A source whose database is unreachable: any statement it sends fails, so a result proves none was sent.</summary>
    private static PostgresVectorTileSource NewUnreachableSut(ILodPolicy? policy = null) =>
        new(
            new DbContextOptionsBuilder<GeoAssetsDbContext>()
                .UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Timeout=1", npgsql => npgsql.UseNetTopologySuite())
                .Options,
            new FakeTenantContext(Guid.NewGuid()),
            policy,
            TimeProvider.System);

    private static async Task<MvtTile> TileAsync(PostgresVectorTileSource sut, TileRequest request)
    {
        var payload = await sut.GetVectorTileAsync(request, CancellationToken.None);
        payload.ContentType.Should().Be(TileFormats.MapboxVectorTileContentType);
        payload.ContentEncoding.Should().BeNull();
        return MvtTile.Decode(payload.Content);
    }

    // ── Layers, properties, geometry ──────────────────────────────────────────

    [Fact]
    public async Task ATile_HasOneLayerPerRequestedTypeWithFeaturesThere_InTheRequestsOrder()
    {
        await using var world = await VectorTileWorld.SeedAsync(fixture);

        var tile = await TileAsync(NewSut(world.OrgA), world.Request(world.Lines, world.Poles, world.Late, "no-such-type"));

        tile.LayerNames.Should().Equal(world.Lines, world.Poles);
        tile[world.Lines].Extent.Should().Be(PostgresVectorTileSource.Extent);
        tile[world.Lines].Ids.Should().Equal(world.LineA);
        tile[world.Poles].Ids.Should().BeEquivalentTo([world.PoleA, world.PoleA2, world.SharedPole]);
    }

    [Fact]
    public async Task AFeature_CarriesItsIdNameTypeAndLayer_AndTheAttributesItsTypesLayerRulesRead()
    {
        await using var world = await VectorTileWorld.SeedAsync(fixture);

        var tile = await TileAsync(NewSut(world.OrgA), world.Request(world.Poles, world.Lines));

        tile[world.Poles].ById(world.PoleA).Properties.Should().Equal(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["id"] = world.PoleA,
            ["name"] = $"name {world.PoleA}",
            ["assetTypeId"] = world.Poles,
            ["layerId"] = world.OverrideLayerId.ToString(),
            ["status"] = "ok",
        });
        tile[world.Poles].ById(world.PoleA2).Properties.Should().NotContainKey("layerId").And.Contain("status", "bad");
        tile[world.Lines].ById(world.LineA).Properties.Keys.Should().BeEquivalentTo(
            ["id", "name", "assetTypeId"], "the line type has no layer rule, so none of its attributes is carried");
    }

    [Fact]
    public async Task AFeature_IsDrawnWhereItIs_InTileUnits()
    {
        await using var world = await VectorTileWorld.SeedAsync(fixture);

        var tile = await TileAsync(NewSut(world.OrgA), world.Request(world.Poles, world.Lines));

        var pole = tile[world.Poles].ById(world.PoleA);
        pole.Type.Should().Be(1);
        var (x, y) = WebMercator.Project(world.PoleAPosition.Lon, world.PoleAPosition.Lat);
        var envelope = world.Tile.Envelope3857;
        var expectedX = (x - envelope.MinX) / envelope.Width * PostgresVectorTileSource.Extent;
        var expectedY = (envelope.MaxY - y) / envelope.Height * PostgresVectorTileSource.Extent;
        pole.Parts.Should().ContainSingle().Which.Should().ContainSingle();
        pole.Parts[0][0].X.Should().BeCloseTo((int)Math.Round(expectedX), 1);
        pole.Parts[0][0].Y.Should().BeCloseTo((int)Math.Round(expectedY), 1);

        var line = tile[world.Lines].ById(world.LineA);
        line.Type.Should().Be(2);
        line.Parts.Should().ContainSingle().Which.Should().HaveCount(2);
    }

    [Fact]
    public async Task ATileWithoutFeatures_IsEmpty()
    {
        await using var world = await VectorTileWorld.SeedAsync(fixture);
        var elsewhere = new TileAddress(VectorTileWorld.Zoom, (world.Tile.X + 3) % TileAddress.TilesPerAxis(VectorTileWorld.Zoom), world.Tile.Y);

        var payload = await NewSut(world.OrgA).GetVectorTileAsync(VectorTileWorld.Request(elsewhere, world.Poles), CancellationToken.None);

        payload.IsEmpty.Should().BeTrue();
        payload.ContentType.Should().Be(TileFormats.MapboxVectorTileContentType);
    }

    // ── Scoping ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AsOrganizationB_ATileHoldsItsFeaturesAndTheSharedOnes_NeverOrganizationAs()
    {
        await using var world = await VectorTileWorld.SeedAsync(fixture);

        var tile = await TileAsync(NewSut(world.OrgB), world.Request(world.Poles, world.Lines));

        tile.LayerNames.Should().Equal(world.Poles);
        tile[world.Poles].Ids.Should().BeEquivalentTo([world.PoleB, world.SharedPole]);
    }

    [Fact]
    public async Task WithoutAnOrganization_ATileHoldsTheSharedFeaturesOnly()
    {
        await using var world = await VectorTileWorld.SeedAsync(fixture);

        var tile = await TileAsync(NewSut(organization: null), world.Request(world.Poles, world.Lines));

        tile.LayerNames.Should().Equal(world.Poles);
        tile[world.Poles].Ids.Should().Equal(world.SharedPole);
    }

    // ── Zoom hiding and aggregation ───────────────────────────────────────────

    [Fact]
    public async Task ATypesZoomRange_HidesItsFeaturesOutsideIt_AndShowsThemInside()
    {
        await using var world = await VectorTileWorld.SeedAsync(fixture);
        var sut = NewSut(world.OrgA);
        var inside = TileAddress.Containing(world.LateAPosition.Lon, world.LateAPosition.Lat, VectorTileWorld.LateMinZoom);

        (await TileAsync(sut, world.Request(world.Late))).Layers.Should().BeEmpty();
        (await TileAsync(sut, VectorTileWorld.Request(inside, world.Late)))[world.Late].Ids.Should().Equal(world.LateA);
    }

    [Fact]
    public async Task ALayerThePolicyHidesAtTheZoom_IsLeftOut()
    {
        await using var world = await VectorTileWorld.SeedAsync(fixture);
        var policy = new LodPolicy(
            LodThresholds.VectorOnly,
            new Dictionary<string, LodThresholds>(StringComparer.Ordinal) { [world.Poles] = new(0, 0, MinZoom: VectorTileWorld.Zoom + 2) });

        var tile = await TileAsync(NewSut(world.OrgA, policy), world.Request(world.Poles, world.Lines));

        tile.LayerNames.Should().Equal(world.Lines);
    }

    [Fact]
    public async Task AnAggregateLayer_IsCountedPoints_OfTheCallersFeaturesOnly()
    {
        await using var world = await VectorTileWorld.SeedAsync(fixture);
        var policy = new LodPolicy(
            LodThresholds.VectorOnly,
            new Dictionary<string, LodThresholds>(StringComparer.Ordinal) { [world.Poles] = new(VectorTileWorld.Zoom + 1, VectorTileWorld.Zoom + 1) });

        var tile = await TileAsync(NewSut(world.OrgA, policy), world.Request(world.Poles, world.Lines));

        tile.LayerNames.Should().Equal(world.Poles, world.Lines);
        var cells = tile[world.Poles].Features;
        cells.Should().OnlyContain(f => f.Type == 1 && f.Properties.Keys.Order(StringComparer.Ordinal).SequenceEqual(new[] { "assetTypeId", "count" }));
        cells.Sum(f => Convert.ToInt64(f.Properties["count"], System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(3, "organization A's two poles in the tile and the shared one — never organization B's");
        tile[world.Lines].Ids.Should().ContainSingle().Which.Should().Be(world.LineA, "the other layer is still drawn as features");
    }

    // ── Budget ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheBudget_KeepsTheFirstFeaturesByLayerThenOrdinalId()
    {
        await using var world = await VectorTileWorld.SeedAsync(fixture);

        var tile = await TileAsync(NewSut(world.OrgA, budget: 2), world.Request(world.Poles, world.Lines));

        tile.LayerNames.Should().Equal(world.Poles);
        tile[world.Poles].Ids.Should().Equal(new[] { world.PoleA, world.PoleA2, world.SharedPole }.Order(StringComparer.Ordinal).Take(2));
    }

    [Fact]
    public void TheBudget_MustBePositive()
    {
        var act = () => NewSut(null, budget: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── Short-circuits ────────────────────────────────────────────────────────

    [Fact]
    public async Task ATileWhoseLayersThePolicyAllHides_IsEmpty_WithoutTouchingTheDatabase()
    {
        var policy = new LodPolicy(new LodThresholds(0, 0, MinZoom: 20));

        var payload = await NewUnreachableSut(policy).GetVectorTileAsync(
            new TileRequest(new TileLayerSet("test", ["a", "b"]), new TileAddress(10, 1, 1)), CancellationToken.None);

        payload.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task ACancelledRequest_Throws_WithoutTouchingTheDatabase()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => NewUnreachableSut().GetVectorTileAsync(
            new TileRequest(new TileLayerSet("test", ["a"]), new TileAddress(10, 1, 1)), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Registration ──────────────────────────────────────────────────────────

    [Fact]
    public void AddPostgresVectorTileSource_RegistersAScopedSourceReplacingAnyEarlierOne_WithTheHostsPolicy()
    {
        var services = new ServiceCollection();
        services.AddScoped<IVectorTileSource, UnusedVectorTileSource>();
        services.AddScoped<ITenantContext>(_ => new FakeTenantContext(Guid.NewGuid()));
        services.AddSingleton<ILodPolicy>(new LodPolicy(LodThresholds.VectorOnly));

        services.AddPostgresVectorTileSource(fixture.ConnectionString);

        services.Should().ContainSingle(d => d.ServiceType == typeof(IVectorTileSource))
            .Which.Lifetime.Should().Be(ServiceLifetime.Scoped);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IVectorTileSource>().Should().BeOfType<PostgresVectorTileSource>();
    }

    private sealed class UnusedVectorTileSource : IVectorTileSource
    {
        public Task<TilePayload> GetVectorTileAsync(TileRequest request, CancellationToken ct) => throw new NotSupportedException();
    }
}

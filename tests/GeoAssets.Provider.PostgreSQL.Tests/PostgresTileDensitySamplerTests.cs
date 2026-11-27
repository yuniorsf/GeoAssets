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
/// XD01-439 — <see cref="PostgresTileDensitySampler"/> against the collection's migrated PostGIS instance, over
/// <see cref="VectorTileWorld"/>'s rows: organization scoping exactly as the vector tile source's (the caller's rows and the
/// shared ones, never another organization's), counts and <c>ST_NPoints</c> sums per tile, the per-tile cap, the seed bound,
/// the order of the answers, the short-circuits that never reach the database, and <see cref="LodCalibrator"/> end to end.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresTileDensitySamplerTests(PostgresContainerFixture fixture)
{
    private PostgresTileDensitySampler NewSut(Guid? organization) =>
        new(fixture.CreateOptions(), organization is { } id ? new FakeTenantContext(id) : null, TimeProvider.System);

    /// <summary>A sampler whose database is unreachable: any statement it sends fails, so a result proves none was sent.</summary>
    private static PostgresTileDensitySampler NewUnreachableSut() =>
        new(
            new DbContextOptionsBuilder<GeoAssetsDbContext>()
                .UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Timeout=1", npgsql => npgsql.UseNetTopologySuite())
                .Options,
            new FakeTenantContext(Guid.NewGuid()),
            TimeProvider.System);

    // ── Scoping ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AsOrganizationA_ATileCountsItsFeaturesAndTheSharedOnes_NeverOrganizationBs()
    {
        await using var world = await VectorTileWorld.SeedAsync(fixture);

        var samples = await NewSut(world.OrgA).MeasureAsync(world.Poles, [world.Tile], 1_000, CancellationToken.None);

        samples.Should().Equal(new TileDensitySample(world.Tile, Features: 3, Vertices: 3, Saturated: false));
    }

    [Fact]
    public async Task AsOrganizationB_OrWithoutOne_ATileCountsWhatThatCallerSees()
    {
        await using var world = await VectorTileWorld.SeedAsync(fixture);

        var asB = await NewSut(world.OrgB).MeasureAsync(world.Poles, [world.Tile], 1_000, CancellationToken.None);
        var shared = await NewSut(organization: null).MeasureAsync(world.Poles, [world.Tile], 1_000, CancellationToken.None);

        asB.Single().Features.Should().Be(2, "PoleB and the shared pole");
        shared.Single().Features.Should().Be(1, "the shared pole alone");
    }

    [Fact]
    public async Task TheSeeds_AreTheCallersFeaturesAndTheSharedOnes()
    {
        await using var world = await VectorTileWorld.SeedAsync(fixture);

        var asA = await NewSut(world.OrgA).SampleSeedsAsync(world.Poles, 100, CancellationToken.None);
        var asB = await NewSut(world.OrgB).SampleSeedsAsync(world.Poles, 100, CancellationToken.None);
        var poleA = world.PoleAPosition;
        var poleB = (Lon: world.CenterLon, Lat: world.CenterLat + (3 * world.Step));

        asA.Should().HaveCount(4, "PoleA, PoleA2, FarPoleA and the shared pole");
        asA.Count(s => Near(s, poleA)).Should().Be(1);
        asA.Count(s => Near(s, poleB)).Should().Be(0, "PoleB is organization B's");
        asB.Should().HaveCount(2);
        asB.Count(s => Near(s, poleB)).Should().Be(1);
    }

    // ── Counts, vertices, bounds ──────────────────────────────────────────────

    [Fact]
    public async Task ATile_SumsItsFeaturesVertices_AndAnswersInTheOrderAsked()
    {
        await using var world = await VectorTileWorld.SeedAsync(fixture);
        var empty = new TileAddress(VectorTileWorld.Zoom, (world.Tile.X + 7) % TileAddress.TilesPerAxis(VectorTileWorld.Zoom), world.Tile.Y);

        var samples = await NewSut(world.OrgA).MeasureAsync(world.Lines, [empty, world.Tile], 1_000, CancellationToken.None);

        samples.Should().Equal(
            new TileDensitySample(empty, 0, 0, false),
            new TileDensitySample(world.Tile, 1, 2, false));
    }

    [Fact]
    public async Task ATileReachingTheCap_IsSaturated_ItsCountsLowerBounds()
    {
        await using var world = await VectorTileWorld.SeedAsync(fixture);

        var samples = await NewSut(world.OrgA).MeasureAsync(world.Poles, [world.Tile], 2, CancellationToken.None);

        samples.Should().Equal(new TileDensitySample(world.Tile, 2, 2, Saturated: true));
    }

    [Fact]
    public async Task TheSeeds_AreBoundedByTheirCap()
    {
        await using var world = await VectorTileWorld.SeedAsync(fixture);

        var seeds = await NewSut(world.OrgA).SampleSeedsAsync(world.Poles, 2, CancellationToken.None);

        seeds.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(0d, 100d)]
    [InlineData(-1d, 100d)]
    [InlineData(1e3, 100d)]
    [InlineData(1e7, 0.08192)]
    [InlineData(1e11, 0.0001)]
    public void TheBlockSample_ReadsAboutSixteenRowsPerSeed(double estimatedRows, double percent)
    {
        PostgresTileDensitySampler.SamplePercent(512, estimatedRows).Should().BeApproximately(percent, 0.0001);
    }

    // ── Short-circuits ────────────────────────────────────────────────────────

    [Fact]
    public async Task NoTiles_AreAnsweredWithoutTheDatabase()
    {
        var samples = await NewUnreachableSut().MeasureAsync("any", [], 10, CancellationToken.None);

        samples.Should().BeEmpty();
    }

    [Fact]
    public async Task ACancelledToken_ThrowsBeforeTheDatabase()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var sut = NewUnreachableSut();

        var seeds = () => sut.SampleSeedsAsync("any", 10, cancelled.Token);
        var measure = () => sut.MeasureAsync("any", [new TileAddress(3, 1, 1)], 10, cancelled.Token);

        await seeds.Should().ThrowAsync<OperationCanceledException>();
        await measure.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task TilesOfTwoZooms_OrABadArgument_AreRefused()
    {
        var sut = NewUnreachableSut();

        var mixed = () => sut.MeasureAsync("any", [new TileAddress(3, 1, 1), new TileAddress(4, 1, 1)], 10, CancellationToken.None);
        var blank = () => sut.SampleSeedsAsync(" ", 10, CancellationToken.None);
        var noCap = () => sut.MeasureAsync("any", [new TileAddress(3, 1, 1)], 0, CancellationToken.None);

        await mixed.Should().ThrowAsync<ArgumentException>();
        await blank.Should().ThrowAsync<ArgumentException>();
        await noCap.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    // ── End to end ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheCalibrator_OverTheSampler_MeasuresTheCallersTilesAndRecommends()
    {
        await using var world = await VectorTileWorld.SeedAsync(fixture);
        var settings = LodCalibrationSettings.Default with { VectorMaxFeaturesPerTile = 2, RasterMaxFeaturesPerTile = 3, MaxFeaturesPerTile = 10, MaxZoom = VectorTileWorld.Zoom };

        var asA = await new LodCalibrator(NewSut(world.OrgA), settings).CalibrateAsync(world.Poles, null, null, CancellationToken.None);
        var asB = await new LodCalibrator(NewSut(world.OrgB), settings).CalibrateAsync(world.Poles, null, null, CancellationToken.None);

        asA.Seeds.Should().Be(4);
        asA.Densities[0].Zoom.Should().Be(VectorTileWorld.Zoom);
        asA.Densities[0].Features.P99.Should().Be(3, "the world's tile holds PoleA, PoleA2 and the shared pole for organization A");
        asA.Recommended.Should().NotBeNull();
        asA.Recommended!.Value.VectorFromZoom.Should().BeGreaterThan(VectorTileWorld.Zoom, "three features exceed the vector budget of two at the highest zoom");
        asB.Densities[0].Features.P99.Should().BeLessThanOrEqualTo(2, "organization B never counts organization A's poles");
    }

    [Fact]
    public void TheServiceExtension_RegistersTheSamplerScoped_InTheRequestsTenant()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext>(_ => new FakeTenantContext(Guid.NewGuid()));

        services.AddPostgresTileDensitySampler("Host=localhost;Database=x");

        services.Should().ContainSingle(d => d.ServiceType == typeof(ITileDensitySampler)).Which.Lifetime.Should().Be(ServiceLifetime.Scoped);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITileDensitySampler>().Should().BeOfType<PostgresTileDensitySampler>();
    }

    private static bool Near(TileDensitySeed seed, (double Lon, double Lat) position) =>
        Math.Abs(seed.Longitude - position.Lon) < 1e-9 && Math.Abs(seed.Latitude - position.Lat) < 1e-9;
}

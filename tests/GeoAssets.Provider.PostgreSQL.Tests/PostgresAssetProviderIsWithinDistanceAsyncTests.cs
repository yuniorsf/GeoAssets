using FluentAssertions;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// Exercises <see cref="PostgresAssetProvider.IsWithinDistanceAsync"/> (XD01-221) against a real
/// PostGIS instance — proves the actual <c>ST_DWithin</c> query, not the in-memory Haversine
/// default every other <see cref="GeoAssets.Core.Interfaces.IAssetProvider"/> implementation
/// falls back to (see <c>GeoAssets.Core.Tests.IAssetProviderDefaultsTests</c> for that path).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresAssetProviderIsWithinDistanceAsyncTests(PostgresContainerFixture fixture)
{
    private static readonly GeoPoint AssetLocation = new(-46.633, -23.55);

    private async Task ClearAsync()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE geo_entity CASCADE");
    }

    private async Task SeedAssetAsync(string id, GeoPoint location)
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", geom)
            VALUES ({id}, 'Asset', 'type-a', ST_SetSRID(ST_MakePoint({location.Longitude}, {location.Latitude}), 4326));
            """);
    }

    private static PostgresAssetProvider CreateSut(DbContextOptions<GeoAssetsDbContext> options) =>
        new(new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);

    [Fact]
    public async Task IsWithinDistanceAsync_PointWellWithinTolerance_ReturnsTrue()
    {
        await ClearAsync();
        await SeedAssetAsync("asset-near", AssetLocation);
        await using var sut = CreateSut(fixture.CreateOptions());

        var result = await sut.IsWithinDistanceAsync("asset-near", AssetLocation, toleranceMeters: 30);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsWithinDistanceAsync_PointFarOutsideTolerance_ReturnsFalse()
    {
        await ClearAsync();
        await SeedAssetAsync("asset-far", AssetLocation);
        await using var sut = CreateSut(fixture.CreateOptions());

        var farAway = new GeoPoint(AssetLocation.Longitude, AssetLocation.Latitude + 1); // ~110km
        var result = await sut.IsWithinDistanceAsync("asset-far", farAway, toleranceMeters: 30);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsWithinDistanceAsync_UnknownFeatureId_ReturnsFalse()
    {
        await ClearAsync();
        await using var sut = CreateSut(fixture.CreateOptions());

        var result = await sut.IsWithinDistanceAsync("does-not-exist", AssetLocation, toleranceMeters: 30);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsWithinDistanceAsync_FeatureWithNullGeometry_ReturnsFalse()
    {
        await ClearAsync();
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId") VALUES ('asset-no-geom', 'Asset', 'type-a');
            """);
        await using var sut = CreateSut(fixture.CreateOptions());

        var result = await sut.IsWithinDistanceAsync("asset-no-geom", AssetLocation, toleranceMeters: 30);

        result.Should().BeFalse();
    }
}

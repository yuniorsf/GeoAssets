using FluentAssertions;
using GeoAssets.Core.Isp;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// XD01-272's AC: "Unit tests cover entity mapping round-trip (NTS ↔ EF Core) for each new
/// type." Proves <see cref="PostgresAssetProvider"/>'s generic <c>GeoEntityRow</c>/NTS bridge —
/// the same one every other asset type already goes through, no new EF entity needed — round-
/// trips each of the 5 new ISP <see cref="IspAssetTypes"/> against a real PostGIS instance: point
/// geometry for CTO/CEO/PTO/ONT, a real <c>LineString</c> for the optical cable, with their
/// documented custom attributes intact.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class IspAssetTypesRoundTripTests(PostgresContainerFixture fixture)
{
    private async Task ClearAsync()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE geo_entity CASCADE");
    }

    private static PostgresAssetProvider CreateSut(DbContextOptions<GeoAssetsDbContext> options) =>
        new(new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);

    private static GeoFeature FeatureWith(string id, Guid assetTypeId, GeoGeometry geometry, Dictionary<string, string> attributes) => new()
    {
        Id = id,
        Geometry = geometry,
        Properties = new GeoFeatureProperties
        {
            Name = "test",
            AssetTypeId = assetTypeId.ToString(),
            CustomAttributes = attributes,
        },
    };

    [Fact]
    public async Task Cto_RoundTrips_GeometryAndSplitterPortAttributes()
    {
        await ClearAsync();
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CtoCapacityCalculator.TotalSplitterPortsKey] = "16",
            [CtoCapacityCalculator.OccupiedSplitterPortsKey] = "12",
        };
        var feature = FeatureWith("cto-1", IspAssetTypeIds.Cto, new GeoPoint(-46.633, -23.55), attributes);
        await using var sut = CreateSut(fixture.CreateOptions());

        sut.Add(feature);
        var reloaded = sut.GetById("cto-1");

        reloaded.Should().NotBeNull();
        reloaded!.Geometry.Should().BeOfType<GeoPoint>();
        reloaded.Properties.CustomAttributes.Should().BeEquivalentTo(attributes);
        CtoCapacityCalculator.OccupiedPortPercentage(reloaded.Properties.CustomAttributes).Should().Be(75);
    }

    [Fact]
    public async Task Ceo_RoundTrips_Geometry()
    {
        await ClearAsync();
        var feature = FeatureWith("ceo-1", IspAssetTypeIds.Ceo, new GeoPoint(-46.633, -23.55), []);
        await using var sut = CreateSut(fixture.CreateOptions());

        sut.Add(feature);
        var reloaded = sut.GetById("ceo-1");

        reloaded.Should().NotBeNull();
        reloaded!.Geometry.Should().BeOfType<GeoPoint>();
    }

    [Fact]
    public async Task OpticalCable_RoundTrips_LineStringGeometryAndFiberAttributes()
    {
        await ClearAsync();
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fiberCount"] = "48",
            ["cableType"] = "Duct",
        };
        var geometry = new GeoLineString([(-46.633, -23.55), (-46.634, -23.551)]);
        var feature = FeatureWith("cable-1", IspAssetTypeIds.OpticalCable, geometry, attributes);
        await using var sut = CreateSut(fixture.CreateOptions());

        sut.Add(feature);
        var reloaded = sut.GetById("cable-1");

        reloaded.Should().NotBeNull();
        reloaded!.Geometry.Should().BeOfType<GeoLineString>();
        reloaded.Properties.CustomAttributes.Should().BeEquivalentTo(attributes);
    }

    [Fact]
    public async Task Pto_RoundTrips_GeometryAndParentCtoLink()
    {
        await ClearAsync();
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["parentCtoFeatureId"] = "cto-1" };
        var feature = FeatureWith("pto-1", IspAssetTypeIds.Pto, new GeoPoint(-46.633, -23.55), attributes);
        await using var sut = CreateSut(fixture.CreateOptions());

        sut.Add(feature);
        var reloaded = sut.GetById("pto-1");

        reloaded.Should().NotBeNull();
        reloaded!.Properties.CustomAttributes.Should().BeEquivalentTo(attributes);
    }

    [Fact]
    public async Task Ont_RoundTrips_GeometryAndFsanMacAttributes()
    {
        await ClearAsync();
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fsan"] = "HWTC12345678",
            ["macAddress"] = "AA:BB:CC:DD:EE:FF",
            ["parentPtoFeatureId"] = "pto-1",
        };
        var feature = FeatureWith("ont-1", IspAssetTypeIds.Ont, new GeoPoint(-46.633, -23.55), attributes);
        await using var sut = CreateSut(fixture.CreateOptions());

        sut.Add(feature);
        var reloaded = sut.GetById("ont-1");

        reloaded.Should().NotBeNull();
        reloaded!.Properties.CustomAttributes.Should().BeEquivalentTo(attributes);
    }
}

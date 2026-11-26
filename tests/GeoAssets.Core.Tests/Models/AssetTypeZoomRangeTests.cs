using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Bdgd;
using GeoAssets.NeutralOperator.Domain;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Models;

/// <summary>
/// The optional per-type zoom range (XD01-311): <see cref="AssetTypeZoomRange"/>'s rules, and
/// that <see cref="AssetType.MinZoom"/>/<see cref="AssetType.MaxZoom"/> survive the JSON every
/// provider, the REST API and local/project storage use (<see cref="GeoJsonSerializer.Options"/>).
/// </summary>
public class AssetTypeZoomRangeTests
{
    // ── Rules ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(null, 24)]
    [InlineData(0, 24)]
    [InlineData(12, 12)]
    [InlineData(13, 15)]
    [InlineData(24, null)]
    [InlineData(null, 0)]
    public void FindDefect_OpenBoundsAndOrderedBoundsWithinRange_AreValid(int? min, int? max)
    {
        AssetTypeZoomRange.FindDefect(min, max).Should().BeNull();

        var act = () => AssetTypeZoomRange.EnsureValid(min, max);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(-1, null, AssetTypeZoomRangeDefect.MinZoomOutOfBounds)]
    [InlineData(25, null, AssetTypeZoomRangeDefect.MinZoomOutOfBounds)]
    [InlineData(int.MinValue, 5, AssetTypeZoomRangeDefect.MinZoomOutOfBounds)]
    [InlineData(null, -1, AssetTypeZoomRangeDefect.MaxZoomOutOfBounds)]
    [InlineData(null, 25, AssetTypeZoomRangeDefect.MaxZoomOutOfBounds)]
    [InlineData(3, int.MaxValue, AssetTypeZoomRangeDefect.MaxZoomOutOfBounds)]
    [InlineData(-1, 25, AssetTypeZoomRangeDefect.MinZoomOutOfBounds)]
    [InlineData(13, 12, AssetTypeZoomRangeDefect.MinZoomAboveMaxZoom)]
    [InlineData(24, 0, AssetTypeZoomRangeDefect.MinZoomAboveMaxZoom)]
    public void FindDefect_ReportsTheFirstBrokenRule_AndEnsureValidThrowsIt(int? min, int? max, AssetTypeZoomRangeDefect expected)
    {
        AssetTypeZoomRange.FindDefect(min, max).Should().Be(expected);

        var act = () => AssetTypeZoomRange.EnsureValid(min, max);

        var thrown = act.Should().Throw<InvalidAssetTypeZoomRangeException>().Which;
        thrown.Defect.Should().Be(expected);
        thrown.MinZoom.Should().Be(min);
        thrown.MaxZoom.Should().Be(max);
        thrown.Message.Should().Contain(expected.ToString());
    }

    [Fact]
    public void Bounds_AreZeroToTwentyFour()
    {
        AssetTypeZoomRange.LowestZoom.Should().Be(0);
        AssetTypeZoomRange.HighestZoom.Should().Be(24);
    }

    [Theory]
    [InlineData(null, null, 0, true)]
    [InlineData(null, null, 24, true)]
    [InlineData(13, 15, 12, false)]
    [InlineData(13, 15, 13, true)]
    [InlineData(13, 15, 14, true)]
    [InlineData(13, 15, 15, true)]
    [InlineData(13, 15, 16, false)]
    [InlineData(15, null, 14, false)]
    [InlineData(15, null, 15, true)]
    [InlineData(15, null, 24, true)]
    [InlineData(null, 13, 0, true)]
    [InlineData(null, 13, 13, true)]
    [InlineData(null, 13, 14, false)]
    public void Contains_IsInclusiveAtBothBounds_AndANullBoundIsOpen(int? min, int? max, int zoom, bool expected) =>
        AssetTypeZoomRange.Contains(min, max, zoom).Should().Be(expected);

    [Fact]
    public void Exception_StandardConstructors_Work()
    {
        var inner = new InvalidOperationException("inner");

        new InvalidAssetTypeZoomRangeException().Defect.Should().Be(default(AssetTypeZoomRangeDefect));
        new InvalidAssetTypeZoomRangeException("m").Message.Should().Be("m");
        new InvalidAssetTypeZoomRangeException("m", inner).InnerException.Should().BeSameAs(inner);
    }

    // ── Model ────────────────────────────────────────────────────────────────

    [Fact]
    public void ZoomRange_DefaultsToOpenOnBothSides()
    {
        var assetType = new AssetType();

        assetType.MinZoom.Should().BeNull();
        assetType.MaxZoom.Should().BeNull();
    }

    [Fact]
    public void BuiltInAndCatalogTypes_CarryNoZoomRange()
    {
        var shipped = AssetType.Defaults.Concat(IspAssetTypes.All).Concat(BdgdAssetTypes.All);

        shipped.Should().OnlyContain(t => t.MinZoom == null && t.MaxZoom == null);
    }

    // ── JSON round trip ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(13, 15)]
    [InlineData(0, 24)]
    [InlineData(15, null)]
    [InlineData(null, 13)]
    [InlineData(null, null)]
    public void AssetType_RoundTripsItsZoomRangeThroughTheSharedJsonOptions(int? min, int? max)
    {
        var original = new AssetType { Name = "Pole", MinZoom = min, MaxZoom = max };

        var json = JsonSerializer.Serialize(original, GeoJsonSerializer.Options);
        var reloaded = JsonSerializer.Deserialize<AssetType>(json, GeoJsonSerializer.Options)!;

        reloaded.Id.Should().Be(original.Id);
        reloaded.MinZoom.Should().Be(min);
        reloaded.MaxZoom.Should().Be(max);
    }

    [Fact]
    public void AssetType_SerializesTheRangeAsCamelCaseNumbers_AndOmitsOpenBounds()
    {
        using var bounded = JsonDocument.Parse(JsonSerializer.Serialize(
            new AssetType { MinZoom = 13, MaxZoom = 15 }, GeoJsonSerializer.Options));
        using var open = JsonDocument.Parse(JsonSerializer.Serialize(new AssetType(), GeoJsonSerializer.Options));

        bounded.RootElement.GetProperty("minZoom").GetInt32().Should().Be(13);
        bounded.RootElement.GetProperty("maxZoom").GetInt32().Should().Be(15);
        open.RootElement.TryGetProperty("minZoom", out _).Should().BeFalse();
        open.RootElement.TryGetProperty("maxZoom", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"id":"11111111-1111-1111-1111-111111111111","name":"Legacy","color":"#112233"}""")]
    [InlineData("""{"id":"11111111-1111-1111-1111-111111111111","name":"Legacy","minZoom":null,"maxZoom":null}""")]
    public void AssetType_StoredBeforeZoomRangesExisted_OrWithExplicitNulls_ReadsAsOpen(string json)
    {
        var reloaded = JsonSerializer.Deserialize<AssetType>(json, GeoJsonSerializer.Options)!;

        reloaded.Name.Should().Be("Legacy");
        reloaded.MinZoom.Should().BeNull();
        reloaded.MaxZoom.Should().BeNull();
    }

    [Fact]
    public void FeatureCollection_RoundTripsTheZoomRangeOfItsAssetTypes()
    {
        // The shape AssetService persists to local storage and a project file carries.
        var ranged = new AssetType { Name = "Pole", MinZoom = 15 };
        var bounded = new AssetType { Name = "Feeder", MinZoom = 8, MaxZoom = 14 };
        var collection = new GeoFeatureCollection
        {
            Metadata = new() { Name = "c", AssetTypes = [.. AssetType.Defaults, ranged, bounded] }
        };

        var reloaded = GeoJsonSerializer.Deserialize(GeoJsonSerializer.Serialize(collection))!;

        var types = reloaded.Metadata.AssetTypes.ToDictionary(t => t.Id);
        (types[ranged.Id].MinZoom, types[ranged.Id].MaxZoom).Should().Be((15, null));
        (types[bounded.Id].MinZoom, types[bounded.Id].MaxZoom).Should().Be((8, 14));
        (types[AssetType.Point.Id].MinZoom, types[AssetType.Point.Id].MaxZoom).Should().Be((null, null));
    }

    // ── TypeIdsHiddenAt (XD01-430) ───────────────────────────────────────────

    [Theory]
    [InlineData(7, new[] { "pole", "feeder" })]
    [InlineData(8, new[] { "pole" })]
    [InlineData(14, new[] { "pole" })]
    [InlineData(15, new[] { "feeder" })]
    [InlineData(20, new[] { "feeder" })]
    public void TypeIdsHiddenAt_ListsTheTypesWhoseRangeExcludesTheZoom_BoundsInclusive(int zoom, string[] hidden)
    {
        var pole = new AssetType { Name = "pole", MinZoom = 15 };
        var feeder = new AssetType { Name = "feeder", MinZoom = 8, MaxZoom = 14 };
        var open = new AssetType { Name = "open" };
        var byName = new Dictionary<string, AssetType>(StringComparer.Ordinal) { ["pole"] = pole, ["feeder"] = feeder };

        var result = AssetTypeZoomRange.TypeIdsHiddenAt([pole, feeder, open], zoom);

        result.Should().BeEquivalentTo(hidden.Select(name => byName[name].Id.ToString()));
        result.Should().NotContain(open.Id.ToString(), "an open range is drawn at every zoom");
    }

    [Fact]
    public void TypeIdsHiddenAt_ComparesIdsAsFeaturesStoreThem_Ordinally()
    {
        var pole = new AssetType { Name = "pole", MinZoom = 15 };

        var result = AssetTypeZoomRange.TypeIdsHiddenAt([pole], 10);

        result.Should().Contain(pole.Id.ToString());
        result.Should().NotContain(pole.Id.ToString().ToUpperInvariant(), "the map matches type ids exactly, so the server does too");
    }

    [Fact]
    public void TypeIdsHiddenAt_NoTypes_HidesNothing() =>
        AssetTypeZoomRange.TypeIdsHiddenAt([], 3).Should().BeEmpty();
}

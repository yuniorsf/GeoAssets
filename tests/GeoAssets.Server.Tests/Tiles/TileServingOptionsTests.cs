using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.Server.Tiles;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GeoAssets.Server.Tests.Tiles;

/// <summary>
/// XD01-434 / XD01-439 — <see cref="TileServingOptions"/> from <c>GeoAssets:Tiles</c>: defaults, overrides, and fail-fast on
/// every invalid value: numbers, zoom thresholds (out of range, inverted, per layer too), unknown keys and typos, layer ids
/// that are not asset type ids, values where sections belong and the reverse, and the calibration settings.
/// </summary>
public sealed class TileServingOptionsTests
{
    private const string Poles = "0f0e0d0c-0000-4000-8000-000000000439";

    private static IConfiguration Config(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>($"{TileServingOptions.SectionKey}:{s.Key}", s.Value)))
            .Build();

    [Fact]
    public void WithoutASection_EveryValueIsTheDefault()
    {
        var options = TileServingOptions.From(Config());

        options.MaxCachedZoom.Should().Be(18);
        options.MaxEvictedTilesPerWrite.Should().Be(65_536);
        options.CacheMaxBytes.Should().Be(256L * 1024 * 1024);
        options.Lod.Default.Should().Be(LodThresholds.VectorOnly);
        options.Lod.Layers.Should().BeEmpty();
        options.Budget.MaxFeaturesPerTile.Should().Be(10_000);
        options.Budget.MaxFeaturesPerLayer.Should().Be(10_000, "unset, a layer may spend the tile's budget — the behaviour before XD01-457");
        options.Budget.Layers.Should().BeEmpty();
    }

    // ── Budget (XD01-457) ─────────────────────────────────────────────────────

    [Fact]
    public void TheFeatureBudget_IsRead_IncludingPerLayerBudgets()
    {
        var options = TileServingOptions.From(Config(
            ("Budget:MaxFeaturesPerTile", "8000"),
            ("Budget:MaxFeaturesPerLayer", "3000"),
            ($"Budget:Layers:{Poles.ToUpperInvariant()}", "1500")));

        options.Budget.MaxFeaturesPerTile.Should().Be(8_000);
        options.Budget.MaxFeaturesPerLayer.Should().Be(3_000);
        options.Budget.Layers.Keys.Should().Equal(Poles);
        options.Budget.ForLayer(Poles).Should().Be(1_500);
        options.Budget.ForLayer("other").Should().Be(3_000);
    }

    [Fact]
    public void ALayerBudget_WithoutALayerCap_IsReadAgainstTheTilesBudget()
    {
        var options = TileServingOptions.From(Config(($"Budget:Layers:{Poles}", "10000")));

        options.Budget.ForLayer(Poles).Should().Be(10_000);
        options.Budget.MaxFeaturesPerLayer.Should().Be(10_000);
    }

    [Theory]
    [InlineData("Budget:MaxFeaturesPerTile", "0", "'GeoAssets:Tiles:Budget:MaxFeaturesPerTile' must be a number of features in [1, 1000000]")]
    [InlineData("Budget:MaxFeaturesPerTile", "1000001", "'GeoAssets:Tiles:Budget:MaxFeaturesPerTile' must be")]
    [InlineData("Budget:MaxFeaturesPerTile", "lots", "'GeoAssets:Tiles:Budget:MaxFeaturesPerTile' must be a whole number")]
    [InlineData("Budget:MaxFeaturesPerLayer", "0", "'GeoAssets:Tiles:Budget:MaxFeaturesPerLayer' must be a number of features in [1, 10000]")]
    [InlineData("Budget:MaxFeaturesPerLayer", "10001", "'GeoAssets:Tiles:Budget:MaxFeaturesPerLayer' must be a number of features in [1, 10000] (at most MaxFeaturesPerTile)")]
    [InlineData("Budget:Layers:" + Poles, "0", "'GeoAssets:Tiles:Budget:Layers:" + Poles + "' must be a number of features in [1, 10000]")]
    [InlineData("Budget:Layers:" + Poles, "20000", "(at most MaxFeaturesPerTile)")]
    [InlineData("Budget:Layers:" + Poles, "many", "must be a whole number")]
    [InlineData("Budget:Layers:" + Poles + ":MaxFeaturesPerTile", "10", "'GeoAssets:Tiles:Budget:Layers:" + Poles + "' must be a value, not a section")]
    [InlineData("Budget:Layers:poles", "10", "'GeoAssets:Tiles:Budget:Layers:poles' must be named by an asset type id")]
    [InlineData("Budget:MaxFeaturesPerLayr", "10", "'GeoAssets:Tiles:Budget:MaxFeaturesPerLayr' is not a setting of 'GeoAssets:Tiles:Budget'")]
    [InlineData("Budget", "10", "'GeoAssets:Tiles:Budget' must be a section")]
    [InlineData("Budget:Layers", "10", "'GeoAssets:Tiles:Budget:Layers' must be a section")]
    public void AnInvalidFeatureBudget_FailsTheStartup_NamingTheKey(string key, string value, string message)
    {
        var act = () => TileServingOptions.From(Config((key, value)));

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain(message);
    }

    [Fact]
    public void ALayerBudget_IsCheckedAgainstAConfiguredTileBudget()
    {
        var act = () => TileServingOptions.From(Config(("Budget:MaxFeaturesPerTile", "500"), ("Budget:MaxFeaturesPerLayer", "600")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxFeaturesPerLayer*[1, 500]*");
    }

    [Fact]
    public void EveryValue_IsRead_IncludingPerLayerThresholds()
    {
        var options = TileServingOptions.From(Config(
            ("MaxCachedZoom", "16"),
            ("MaxEvictedTilesPerWrite", "1000"),
            ("CacheMaxMegabytes", "64"),
            ("Lod:RasterFromZoom", "6"),
            ("Lod:VectorFromZoom", "12"),
            ($"Lod:Layers:{Poles}:RasterFromZoom", "10"),
            ($"Lod:Layers:{Poles}:VectorFromZoom", "15"),
            ($"Lod:Layers:{Poles}:MinZoom", "8"),
            ("Calibration:SampleTiles", "8"),
            ("Calibration:VectorMaxVerticesPerTile", "30000")));

        options.MaxCachedZoom.Should().Be(16);
        options.MaxEvictedTilesPerWrite.Should().Be(1000);
        options.CacheMaxBytes.Should().Be(64L * 1024 * 1024);
        options.Lod.Default.Should().Be(new LodThresholds(6, 12));
        options.Lod.GetThresholds(Poles).Should().Be(new LodThresholds(10, 15, MinZoom: 8));
        options.Lod.Decide("other", 7).Should().Be(TileRenderTier.Raster);
        options.Calibration.Should().Be(LodCalibrationSettings.Default with { SampleTiles = 8, VectorMaxVerticesPerTile = 30_000 });
    }

    [Fact]
    public void TheLodThresholds_DecideTheTiers_SoChangingThemChangesTheTiles()
    {
        var vectorOnly = TileServingOptions.From(Config()).Lod;
        var calibrated = TileServingOptions.From(Config(($"Lod:Layers:{Poles}:RasterFromZoom", "9"), ($"Lod:Layers:{Poles}:VectorFromZoom", "13"))).Lod;

        vectorOnly.Decide(Poles, 8).Should().Be(TileRenderTier.Vector);
        calibrated.Decide(Poles, 8).Should().Be(TileRenderTier.Aggregate);
        calibrated.Decide(Poles, 10).Should().Be(TileRenderTier.Raster);
        calibrated.Decide(Poles, 13).Should().Be(TileRenderTier.Vector);
    }

    [Fact]
    public void ALayerId_IsReadInTheFormTheTileSourcesCompare()
    {
        var options = TileServingOptions.From(Config(($"Lod:Layers:{Poles.ToUpperInvariant()}:VectorFromZoom", "12")));

        options.Lod.Layers.Keys.Should().Equal(Poles);
        options.Lod.Decide(Poles, 11).Should().Be(TileRenderTier.Raster);
    }

    [Fact]
    public void TheDefaults_AreTheCalibrationsDefaults()
    {
        TileServingOptions.From(Config()).Calibration.Should().Be(LodCalibrationSettings.Default);
    }

    [Theory]
    [InlineData("MaxCachedZoom", "25")]
    [InlineData("MaxCachedZoom", "-1")]
    [InlineData("MaxCachedZoom", "eighteen")]
    [InlineData("MaxEvictedTilesPerWrite", "0")]
    [InlineData("CacheMaxMegabytes", "0")]
    [InlineData("CacheMaxMegabytes", "lots")]
    public void AnInvalidValue_FailsTheStartup(string key, string value)
    {
        var act = () => TileServingOptions.From(Config((key, value)));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{key}*");
    }

    [Theory]
    [InlineData("Lod:RasterFromZoom", "14", "Lod:VectorFromZoom", "10", LodThresholdsDefect.RasterFromZoomAboveVectorFromZoom, false)]
    [InlineData("Lod:RasterFromZoom", "-1", "Lod:VectorFromZoom", "10", LodThresholdsDefect.RasterFromZoomOutOfBounds, false)]
    [InlineData("Lod:RasterFromZoom", "0", "Lod:VectorFromZoom", "25", LodThresholdsDefect.VectorFromZoomOutOfBounds, false)]
    [InlineData("Lod:MinZoom", "12", "Lod:MaxZoom", "10", LodThresholdsDefect.MinZoomAboveMaxZoom, false)]
    [InlineData("Lod:MinZoom", "-2", "Lod:VectorFromZoom", "0", LodThresholdsDefect.MinZoomOutOfBounds, false)]
    [InlineData("Lod:MaxZoom", "30", "Lod:VectorFromZoom", "0", LodThresholdsDefect.MaxZoomOutOfBounds, false)]
    [InlineData("RasterFromZoom", "16", "VectorFromZoom", "12", LodThresholdsDefect.RasterFromZoomAboveVectorFromZoom, true)]
    [InlineData("VectorFromZoom", "40", "RasterFromZoom", "0", LodThresholdsDefect.VectorFromZoomOutOfBounds, true)]
    [InlineData("MinZoom", "9", "MaxZoom", "3", LodThresholdsDefect.MinZoomAboveMaxZoom, true)]
    public void InvalidLodThresholds_FailTheStartup_DefaultOrPerLayer(
        string key, string value, string otherKey, string otherValue, LodThresholdsDefect defect, bool perLayer)
    {
        var prefix = perLayer ? $"Lod:Layers:{Poles}:" : string.Empty;

        var act = () => TileServingOptions.From(Config((prefix + key, value), (prefix + otherKey, otherValue)));

        var thrown = act.Should().Throw<InvalidLodPolicyException>().Which;
        thrown.Defect.Should().Be(defect);
        thrown.LayerId.Should().Be(perLayer ? Poles : null);
    }

    [Theory]
    [InlineData("MaxCachedZom", "12", "'GeoAssets:Tiles:MaxCachedZom' is not a setting of 'GeoAssets:Tiles'")]
    [InlineData("Lod:VectorFromZom", "12", "'GeoAssets:Tiles:Lod:VectorFromZom' is not a setting of 'GeoAssets:Tiles:Lod'")]
    [InlineData("Lod:Layers:0f0e0d0c-0000-4000-8000-000000000439:RasterFrom", "3", "'GeoAssets:Tiles:Lod:Layers:0f0e0d0c-0000-4000-8000-000000000439:RasterFrom' is not a setting")]
    [InlineData("Calibration:SampleTile", "3", "'GeoAssets:Tiles:Calibration:SampleTile' is not a setting of 'GeoAssets:Tiles:Calibration'")]
    [InlineData("Lod:Layers:poles:VectorFromZoom", "12", "'GeoAssets:Tiles:Lod:Layers:poles' must be named by an asset type id")]
    [InlineData("Lod:Layers:0f0e0d0c-0000-4000-8000-000000000439", "12", "must be a section of RasterFromZoom, VectorFromZoom, MinZoom, MaxZoom")]
    [InlineData("Lod", "14", "'GeoAssets:Tiles:Lod' must be a section")]
    [InlineData("Lod:Layers", "x", "'GeoAssets:Tiles:Lod:Layers' must be a section")]
    [InlineData("Calibration", "on", "'GeoAssets:Tiles:Calibration' must be a section")]
    [InlineData("MaxCachedZoom:0", "12", "'GeoAssets:Tiles:MaxCachedZoom' must be a value, not a section")]
    [InlineData("Lod:VectorFromZoom:x", "12", "'GeoAssets:Tiles:Lod:VectorFromZoom' must be a value, not a section")]
    public void AKeyTheSectionDoesNotDefine_OrOfTheWrongShape_FailsTheStartup_NamingIt(string key, string value, string message)
    {
        var act = () => TileServingOptions.From(Config((key, value)));

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain(message);
    }

    [Theory]
    [InlineData("Calibration:MinZoom", "-1", "MinZoom")]
    [InlineData("Calibration:MaxZoom", "25", "MaxZoom")]
    [InlineData("Calibration:SampleTiles", "0", "SampleTiles")]
    [InlineData("Calibration:VectorMaxVerticesPerTile", "0", "VectorMaxVerticesPerTile")]
    [InlineData("Calibration:MaxFeaturesPerTile", "100", "MaxFeaturesPerTile")]
    [InlineData("Calibration:SeedFeatures", "many", "SeedFeatures")]
    public void InvalidCalibrationSettings_FailTheStartup(string key, string value, string named)
    {
        var act = () => TileServingOptions.From(Config((key, value)));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{named}*");
    }

    [Fact]
    public void KeysCompareAsConfigurationDoes_IgnoringCase()
    {
        var options = TileServingOptions.From(Config(("lod:rasterfromzoom", "4"), ("LOD:VECTORFROMZOOM", "9"), ("calibration:sampletiles", "2")));

        options.Lod.Default.Should().Be(new LodThresholds(4, 9));
        options.Calibration.SampleTiles.Should().Be(2);
    }
}

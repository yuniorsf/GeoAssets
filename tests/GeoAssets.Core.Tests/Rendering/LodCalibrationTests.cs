using FluentAssertions;
using GeoAssets.Core.Rendering;
using Xunit;

namespace GeoAssets.Core.Tests.Rendering;

/// <summary>
/// XD01-439 — the LOD calibration's pure math: nearest-rank percentiles, per-zoom densities, the tier budgets, the tiles a
/// zoom measures, and the thresholds measured densities call for; plus the proof the ticket asks for that a threshold
/// change changes <see cref="LodPolicy.Decide"/>.
/// </summary>
public class LodCalibrationTests
{
    private static readonly LodCalibrationSettings Budgets = new()
    {
        VectorMaxFeaturesPerTile = 100,
        VectorMaxVerticesPerTile = 1_000,
        RasterMaxFeaturesPerTile = 500,
        RasterMaxVerticesPerTile = 5_000,
        MaxFeaturesPerTile = 1_000,
    };

    private static TileDensityAtZoom Density(int zoom, long features, long vertices, int tiles = 10) =>
        new(zoom, tiles, 0, new DensityPercentiles(features / 2, features, features), new DensityPercentiles(vertices / 2, vertices, vertices));

    /// <summary>A vector-cheap zoom, a raster-cheap one, and one that fits neither.</summary>
    private static TileDensityAtZoom Vector(int zoom) => Density(zoom, 50, 800);

    private static TileDensityAtZoom Raster(int zoom) => Density(zoom, 300, 4_000);

    private static TileDensityAtZoom Neither(int zoom) => Density(zoom, 800, 9_000);

    // ── LodPolicy.Decide follows the thresholds ─────────────────────────────

    [Fact]
    public void ChangingAThreshold_ChangesWhatDecideAnswers()
    {
        var before = new LodPolicy(LodThresholds.VectorOnly);
        var after = new LodPolicy(LodThresholds.VectorOnly, new Dictionary<string, LodThresholds>(StringComparer.Ordinal) { ["feeders"] = new(9, 13) });

        before.Decide("feeders", 8).Should().Be(TileRenderTier.Vector);
        after.Decide("feeders", 8).Should().Be(TileRenderTier.Aggregate);
        after.Decide("feeders", 9).Should().Be(TileRenderTier.Raster);
        after.Decide("feeders", 12).Should().Be(TileRenderTier.Raster);
        after.Decide("feeders", 13).Should().Be(TileRenderTier.Vector);
        after.Decide("poles", 8).Should().Be(TileRenderTier.Vector, "a layer without its own thresholds keeps the default ones");
    }

    [Theory]
    [InlineData(0, 0, TileRenderTier.Vector)]
    [InlineData(0, 10, TileRenderTier.Vector)]
    [InlineData(0, 11, TileRenderTier.Raster)]
    [InlineData(10, 11, TileRenderTier.Raster)]
    [InlineData(11, 11, TileRenderTier.Aggregate)]
    [InlineData(11, 14, TileRenderTier.Aggregate)]
    public void MovingTheRasterOrVectorZoom_MovesTheTierAtAZoom(int rasterFrom, int vectorFrom, TileRenderTier atZoom10)
    {
        new LodPolicy(new LodThresholds(rasterFrom, vectorFrom)).Decide("any", 10).Should().Be(atZoom10);
    }

    // ── Percentiles ─────────────────────────────────────────────────────────

    [Fact]
    public void Percentiles_AreNearestRank()
    {
        var values = Enumerable.Range(1, 100).Select(i => (long)i).Reverse();

        DensityPercentiles.Of(values).Should().Be(new DensityPercentiles(50, 95, 99));
        DensityPercentiles.Of([7]).Should().Be(new DensityPercentiles(7, 7, 7));
        DensityPercentiles.Of([1, 2, 3, 4]).Should().Be(new DensityPercentiles(2, 4, 4));
        DensityPercentiles.Of([]).Should().Be(default(DensityPercentiles));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void NearestRank_RefusesAPercentileOutside1To100(int percentile)
    {
        var act = () => DensityPercentiles.NearestRank([1, 2], percentile);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NearestRank_RefusesNoValues()
    {
        var act = () => DensityPercentiles.NearestRank([], 50);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ADensity_CountsItsTilesAndSaturatedOnes_WithEachMeasuresPercentiles()
    {
        var tile = new TileAddress(10, 1, 1);
        TileDensitySample[] samples = [new(tile, 10, 100, false), new(tile, 20, 400, false), new(tile, 1_000, 9_000, true)];

        var density = TileDensityAtZoom.From(10, samples);

        density.Should().Be(new TileDensityAtZoom(10, 3, 1, new DensityPercentiles(20, 1_000, 1_000), new DensityPercentiles(400, 9_000, 9_000)));
    }

    [Fact]
    public void ADensity_RefusesAZoomOutsideThePyramid()
    {
        var act = () => TileDensityAtZoom.From(25, []);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── Budgets ─────────────────────────────────────────────────────────────

    [Fact]
    public void FitsVector_NeedsBothP95FeaturesAndVerticesWithinTheVectorBudgets()
    {
        LodCalibration.FitsVector(Density(5, 100, 1_000), Budgets).Should().BeTrue("the budgets are inclusive");
        LodCalibration.FitsVector(Density(5, 101, 10), Budgets).Should().BeFalse();
        LodCalibration.FitsVector(Density(5, 1, 1_001), Budgets).Should().BeFalse();
        LodCalibration.FitsVector(Density(5, 9_999, 9_999, tiles: 0), Budgets).Should().BeTrue("a zoom without a measured tile has nothing to draw");
    }

    [Fact]
    public void FitsRaster_NeedsBothP95FeaturesAndVerticesWithinTheRasterBudgets()
    {
        LodCalibration.FitsRaster(Density(5, 500, 5_000), Budgets).Should().BeTrue();
        LodCalibration.FitsRaster(Density(5, 501, 10), Budgets).Should().BeFalse();
        LodCalibration.FitsRaster(Density(5, 1, 5_001), Budgets).Should().BeFalse();
        LodCalibration.FitsRaster(Density(5, 9_999, 9_999, tiles: 0), Budgets).Should().BeTrue();
    }

    // ── The tiles measured ──────────────────────────────────────────────────

    [Fact]
    public void SampleTiles_IsEachSeedsTileOnce_OrderedByColumnThenRow()
    {
        TileDensitySeed[] seeds = [new(10.01, 10.01), new(10.02, 10.02), new(-20, 5), new(double.NaN, 0)];

        var tiles = LodCalibration.SampleTiles(seeds, 6, 10);

        tiles.Should().Equal(
            new[] { TileAddress.Containing(-20, 5, 6), TileAddress.Containing(10.01, 10.01, 6) }.OrderBy(t => t.X).ThenBy(t => t.Y));
    }

    [Fact]
    public void SampleTiles_BeyondTheCap_PicksTheSameTilesEveryTime_WhateverTheSeedOrder()
    {
        var seeds = Enumerable.Range(0, 200).Select(i => new TileDensitySeed(-50 + (i * 0.5), -10 + (i % 20 * 0.5))).ToList();

        var first = LodCalibration.SampleTiles(seeds, 10, 16);
        var again = LodCalibration.SampleTiles(Enumerable.Reverse(seeds), 10, 16);
        var all = LodCalibration.SampleTiles(seeds, 10, 1_000);

        first.Should().HaveCount(16).And.OnlyHaveUniqueItems().And.Equal(again);
        all.Count.Should().BeGreaterThan(16);
        first.Should().BeSubsetOf(all);
        first.Should().Equal(first.OrderBy(t => t.X).ThenBy(t => t.Y));
    }

    [Fact]
    public void SampleTiles_RefusesANonPositiveCap()
    {
        var act = () => LodCalibration.SampleTiles([], 5, 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── Recommendations ─────────────────────────────────────────────────────

    [Fact]
    public void NothingMeasured_IsVectorEverywhere()
    {
        LodCalibration.Recommend([], Budgets).Should().Be(LodThresholds.VectorOnly);
    }

    [Fact]
    public void EveryZoomFittingVector_DownToTheLowestDrawn_IsVectorEverywhere()
    {
        var densities = Enumerable.Range(10, 9).Select(Vector).ToList();

        LodCalibration.Recommend(densities, Budgets, typeMinZoom: 10).Should().Be(LodThresholds.VectorOnly);
    }

    [Fact]
    public void VectorAbove_RasterBelow_DownToTheLowestDrawn_IsRasterFromZeroAndVectorFromTheLastFit()
    {
        TileDensityAtZoom[] densities = [Vector(14), Vector(13), Raster(12), Raster(11), Raster(10)];

        LodCalibration.Recommend(densities, Budgets, typeMinZoom: 10).Should().Be(new LodThresholds(0, 13));
    }

    [Fact]
    public void AZoomFittingNoTier_EndsTheRasterRun_AndAggregatesBelow()
    {
        TileDensityAtZoom[] densities = [Vector(16), Vector(15), Raster(14), Raster(13), Neither(12)];

        LodCalibration.Recommend(densities, Budgets, typeMinZoom: 8).Should().Be(new LodThresholds(13, 15));
    }

    [Fact]
    public void NoRasterZoom_BetweenVectorAndAggregate_IsRasterFromEqualToVectorFrom()
    {
        TileDensityAtZoom[] densities = [Vector(14), Neither(13)];

        LodCalibration.Recommend(densities, Budgets, typeMinZoom: 10).Should().Be(new LodThresholds(14, 14));
    }

    [Fact]
    public void TheHighestZoomFailingVector_IsVectorFromTheZoomAboveIt()
    {
        TileDensityAtZoom[] densities = [Raster(13), Raster(12), Raster(11)];

        LodCalibration.Recommend(densities, Budgets, typeMinZoom: 11).Should().Be(new LodThresholds(0, 14));
    }

    [Fact]
    public void AtThePyramidsTop_VectorFromStaysInIt()
    {
        TileDensityAtZoom[] densities = [Neither(TileAddress.MaxZoom)];

        var recommended = LodCalibration.Recommend(densities, Budgets, typeMinZoom: TileAddress.MaxZoom);

        recommended.Should().Be(new LodThresholds(TileAddress.MaxZoom, TileAddress.MaxZoom));
        recommended.FindDefect().Should().BeNull();
    }

    [Fact]
    public void ADipBelowAVectorZoom_IsNotTrusted_TheVectorRunMustBeUnbrokenFromTheTop()
    {
        TileDensityAtZoom[] densities = [Vector(13), Raster(12), Vector(11), Raster(10)];

        LodCalibration.Recommend(densities, Budgets, typeMinZoom: 10).Should().Be(new LodThresholds(0, 13));
    }

    [Fact]
    public void ZoomsBelowTheCalibratedRange_AreGivenTheCoarsestTierMeasured()
    {
        TileDensityAtZoom[] vectorOnly = [Vector(5), Vector(4)];
        TileDensityAtZoom[] rasterBottom = [Vector(5), Raster(4)];

        LodCalibration.Recommend(vectorOnly, Budgets, typeMinZoom: null).Should().Be(new LodThresholds(4, 4), "zooms 0–3 were not measured");
        LodCalibration.Recommend(rasterBottom, Budgets, typeMinZoom: 0).Should().Be(new LodThresholds(4, 5));
    }

    [Fact]
    public void TheMeasuredZooms_MayComeInAnyOrder()
    {
        TileDensityAtZoom[] densities = [Raster(12), Vector(14), Raster(11), Vector(13), Raster(10)];

        LodCalibration.Recommend(densities, Budgets, typeMinZoom: 10).Should().Be(new LodThresholds(0, 13));
    }

    [Theory]
    [InlineData(new[] { 14, 12 })]
    [InlineData(new[] { 14, 14 })]
    public void NonContiguousOrRepeatedZooms_AreRefused(int[] zooms)
    {
        var act = () => LodCalibration.Recommend([.. zooms.Select(Vector)], Budgets);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EveryRecommendation_IsAValidPolicy()
    {
        var builders = new Func<int, TileDensityAtZoom>[] { Vector, Raster, Neither };
        for (var mask = 0; mask < 81; mask++)
        {
            var densities = new List<TileDensityAtZoom>();
            var m = mask;
            for (var zoom = 18; zoom >= 15; zoom--, m /= 3) densities.Add(builders[m % 3](zoom));

            var recommended = LodCalibration.Recommend(densities, Budgets, typeMinZoom: 15);

            recommended.FindDefect().Should().BeNull("mask {0}", mask);
            _ = new LodPolicy(recommended);
        }
    }

    // ── Settings ────────────────────────────────────────────────────────────

    [Fact]
    public void TheDefaultSettings_AreValid()
    {
        LodCalibrationSettings.Default.FindDefect().Should().BeNull();
        LodCalibrationSettings.Default.MaxZoom.Should().Be(18);
    }

    [Theory]
    [InlineData(nameof(LodCalibrationSettings.MinZoom))]
    [InlineData(nameof(LodCalibrationSettings.MaxZoom))]
    [InlineData("Inverted")]
    [InlineData(nameof(LodCalibrationSettings.SeedFeatures))]
    [InlineData(nameof(LodCalibrationSettings.SampleTiles))]
    [InlineData(nameof(LodCalibrationSettings.VectorMaxFeaturesPerTile))]
    [InlineData(nameof(LodCalibrationSettings.VectorMaxVerticesPerTile))]
    [InlineData(nameof(LodCalibrationSettings.RasterMaxFeaturesPerTile))]
    [InlineData(nameof(LodCalibrationSettings.RasterMaxVerticesPerTile))]
    [InlineData(nameof(LodCalibrationSettings.MaxFeaturesPerTile))]
    [InlineData("CapBelowABudget")]
    public void AnInvalidSetting_IsNamed(string defect)
    {
        var settings = defect switch
        {
            nameof(LodCalibrationSettings.MinZoom) => Budgets with { MinZoom = -1 },
            nameof(LodCalibrationSettings.MaxZoom) => Budgets with { MaxZoom = 25 },
            "Inverted" => Budgets with { MinZoom = 12, MaxZoom = 10 },
            nameof(LodCalibrationSettings.SeedFeatures) => Budgets with { SeedFeatures = 0 },
            nameof(LodCalibrationSettings.SampleTiles) => Budgets with { SampleTiles = 1_001 },
            nameof(LodCalibrationSettings.VectorMaxFeaturesPerTile) => Budgets with { VectorMaxFeaturesPerTile = 0 },
            nameof(LodCalibrationSettings.VectorMaxVerticesPerTile) => Budgets with { VectorMaxVerticesPerTile = 0 },
            nameof(LodCalibrationSettings.RasterMaxFeaturesPerTile) => Budgets with { RasterMaxFeaturesPerTile = -5 },
            nameof(LodCalibrationSettings.RasterMaxVerticesPerTile) => Budgets with { RasterMaxVerticesPerTile = 0 },
            nameof(LodCalibrationSettings.MaxFeaturesPerTile) => Budgets with { MaxFeaturesPerTile = 0 },
            _ => Budgets with { MaxFeaturesPerTile = 500 },
        };

        var expected = defect switch
        {
            "Inverted" => nameof(LodCalibrationSettings.MinZoom),
            "CapBelowABudget" => nameof(LodCalibrationSettings.MaxFeaturesPerTile),
            _ => defect,
        };
        settings.FindDefect().Should().StartWith(expected);
        var act = settings.EnsureValid;
        act.Should().Throw<InvalidOperationException>().WithMessage($"*{expected}*");
    }
}

using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using Xunit;

namespace GeoAssets.Core.Tests.Rendering;

/// <summary>XD01-431 — <see cref="LodThresholds"/> validation, <see cref="LodPolicy.Decide"/>'s truth table, fail-fast construction.</summary>
public class LodPolicyTests
{
    private static readonly LodThresholds Poles = new(RasterFromZoom: 6, VectorFromZoom: 12, MinZoom: 3, MaxZoom: 18);

    // ── Truth table ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, TileRenderTier.Hidden)]
    [InlineData(2, TileRenderTier.Hidden)]
    [InlineData(3, TileRenderTier.Aggregate)]
    [InlineData(5, TileRenderTier.Aggregate)]
    [InlineData(6, TileRenderTier.Raster)]
    [InlineData(11, TileRenderTier.Raster)]
    [InlineData(12, TileRenderTier.Vector)]
    [InlineData(18, TileRenderTier.Vector)]
    [InlineData(19, TileRenderTier.Hidden)]
    [InlineData(24, TileRenderTier.Hidden)]
    public void Decide_WalksHiddenAggregateRasterVectorHidden(int zoom, TileRenderTier expected)
    {
        Poles.Decide(zoom).Should().Be(expected);
        new LodPolicy(LodThresholds.VectorOnly, new Dictionary<string, LodThresholds>(StringComparer.Ordinal) { ["poles"] = Poles })
            .Decide("poles", zoom).Should().Be(expected);
    }

    [Fact]
    public void Decide_EveryZoom_MatchesTheThresholdsInOrder()
    {
        for (var zoom = TileAddress.MinZoom; zoom <= TileAddress.MaxZoom; zoom++)
        {
            var expected = zoom < 3 || zoom > 18 ? TileRenderTier.Hidden
                : zoom >= 12 ? TileRenderTier.Vector
                : zoom >= 6 ? TileRenderTier.Raster
                : TileRenderTier.Aggregate;

            Poles.Decide(zoom).Should().Be(expected, "zoom {0}", zoom);
        }
    }

    [Fact]
    public void Decide_OpenBounds_AreNeverHidden()
    {
        var thresholds = new LodThresholds(4, 10);

        thresholds.Decide(0).Should().Be(TileRenderTier.Aggregate);
        thresholds.Decide(4).Should().Be(TileRenderTier.Raster);
        thresholds.Decide(24).Should().Be(TileRenderTier.Vector);
    }

    [Fact]
    public void Decide_RasterFromZero_SkipsAggregation()
    {
        var thresholds = new LodThresholds(0, 14);

        thresholds.Decide(0).Should().Be(TileRenderTier.Raster);
        thresholds.Decide(13).Should().Be(TileRenderTier.Raster);
        thresholds.Decide(14).Should().Be(TileRenderTier.Vector);
    }

    [Fact]
    public void Decide_EqualRasterAndVectorZooms_SkipsRaster()
    {
        var thresholds = new LodThresholds(8, 8);

        thresholds.Decide(7).Should().Be(TileRenderTier.Aggregate);
        thresholds.Decide(8).Should().Be(TileRenderTier.Vector);
    }

    [Fact]
    public void VectorOnly_IsVectorAtEveryZoom_AndTheDefault()
    {
        LodThresholds.VectorOnly.Should().Be(default(LodThresholds));
        Enumerable.Range(0, 25).Select(LodThresholds.VectorOnly.Decide).Should().OnlyContain(t => t == TileRenderTier.Vector);
    }

    [Fact]
    public void Decide_HidesOutsideTheAssetTypeZoomRange_LikeTheMapDoes()
    {
        var type = new AssetType { Name = "Manholes", MinZoom = 14, MaxZoom = 20 };
        var thresholds = new LodThresholds(0, 16, type.MinZoom, type.MaxZoom);

        for (var zoom = 0; zoom <= 24; zoom++)
        {
            (thresholds.Decide(zoom) != TileRenderTier.Hidden)
                .Should().Be(AssetTypeZoomRange.Contains(type.MinZoom, type.MaxZoom, zoom), "zoom {0}", zoom);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(25)]
    public void Decide_RefusesAZoomOutsideThePyramid(int zoom)
    {
        var act = () => Poles.Decide(zoom);

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("zoom");
    }

    // ── Validation ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(-1, 5, null, null, LodThresholdsDefect.RasterFromZoomOutOfBounds)]
    [InlineData(25, 25, null, null, LodThresholdsDefect.RasterFromZoomOutOfBounds)]
    [InlineData(0, -1, null, null, LodThresholdsDefect.VectorFromZoomOutOfBounds)]
    [InlineData(0, 25, null, null, LodThresholdsDefect.VectorFromZoomOutOfBounds)]
    [InlineData(10, 9, null, null, LodThresholdsDefect.RasterFromZoomAboveVectorFromZoom)]
    [InlineData(0, 0, -1, null, LodThresholdsDefect.MinZoomOutOfBounds)]
    [InlineData(0, 0, 25, null, LodThresholdsDefect.MinZoomOutOfBounds)]
    [InlineData(0, 0, null, -1, LodThresholdsDefect.MaxZoomOutOfBounds)]
    [InlineData(0, 0, null, 25, LodThresholdsDefect.MaxZoomOutOfBounds)]
    [InlineData(0, 0, 12, 11, LodThresholdsDefect.MinZoomAboveMaxZoom)]
    public void FindDefect_NamesTheFirstRuleBroken(int rasterFrom, int vectorFrom, int? minZoom, int? maxZoom, LodThresholdsDefect defect)
    {
        var thresholds = new LodThresholds(rasterFrom, vectorFrom, minZoom, maxZoom);

        thresholds.FindDefect().Should().Be(defect);
        var act = () => thresholds.EnsureValid("poles");
        var thrown = act.Should().Throw<InvalidLodPolicyException>().Which;
        thrown.Defect.Should().Be(defect);
        thrown.LayerId.Should().Be("poles");
        thrown.Thresholds.Should().Be(thresholds);
    }

    [Theory]
    [InlineData(0, 0, null, null)]
    [InlineData(0, 24, 0, 24)]
    [InlineData(24, 24, 24, 24)]
    [InlineData(6, 12, 3, 18)]
    [InlineData(6, 12, 15, null)]
    [InlineData(6, 12, null, 2)]
    public void FindDefect_ValidThresholds_HaveNone(int rasterFrom, int vectorFrom, int? minZoom, int? maxZoom)
    {
        var thresholds = new LodThresholds(rasterFrom, vectorFrom, minZoom, maxZoom);

        thresholds.FindDefect().Should().BeNull();
        var act = () => thresholds.EnsureValid();
        act.Should().NotThrow();
    }

    [Fact]
    public void InvalidLodPolicyException_MessageNamesTheLayerAndThresholdsInvariantly()
    {
        var thrown = new InvalidLodPolicyException(LodThresholdsDefect.MinZoomAboveMaxZoom, "poles", new LodThresholds(1, 2, 9, 8));
        var defaults = new InvalidLodPolicyException(LodThresholdsDefect.RasterFromZoomAboveVectorFromZoom, null, new LodThresholds(5, 4));

        thrown.Message.Should().Be("The LOD thresholds of layer 'poles' (raster from z1, vector from z2, visible z9–z8) are invalid: MinZoomAboveMaxZoom.");
        defaults.Message.Should().Be("The LOD thresholds of the default layer (raster from z5, vector from z4, visible zopen–zopen) are invalid: RasterFromZoomAboveVectorFromZoom.");
        thrown.Should().BeAssignableTo<InvalidOperationException>();
    }

    [Fact]
    public void InvalidLodPolicyException_HasTheStandardConstructors()
    {
        var inner = new InvalidOperationException("inner");

        new InvalidLodPolicyException().Defect.Should().BeNull();
        new InvalidLodPolicyException("m").Message.Should().Be("m");
        new InvalidLodPolicyException("m", inner).InnerException.Should().BeSameAs(inner);
    }

    // ── LodPolicy (per-layer, default, fail fast) ───────────────────────────

    [Fact]
    public void LodPolicy_UsesTheLayersThresholds_ElseTheDefault()
    {
        var defaults = new LodThresholds(4, 10);
        var policy = new LodPolicy(defaults, new Dictionary<string, LodThresholds>(StringComparer.Ordinal) { ["poles"] = Poles });

        policy.Default.Should().Be(defaults);
        policy.GetThresholds("poles").Should().Be(Poles);
        policy.GetThresholds("cables").Should().Be(defaults);
        policy.Decide("poles", 2).Should().Be(TileRenderTier.Hidden);
        policy.Decide("cables", 2).Should().Be(TileRenderTier.Aggregate);
        policy.Layers.Should().ContainKey("poles").And.HaveCount(1);
    }

    [Fact]
    public void LodPolicy_ComparesLayerIdsOrdinally()
    {
        var policy = new LodPolicy(LodThresholds.VectorOnly, new Dictionary<string, LodThresholds>(StringComparer.OrdinalIgnoreCase) { ["Poles"] = Poles });

        policy.GetThresholds("Poles").Should().Be(Poles);
        policy.GetThresholds("poles").Should().Be(LodThresholds.VectorOnly);
    }

    [Fact]
    public void LodPolicy_WithoutLayers_AppliesTheDefaultEverywhere()
    {
        var policy = new LodPolicy(Poles);

        policy.Layers.Should().BeEmpty();
        policy.Decide("anything", 7).Should().Be(TileRenderTier.Raster);
    }

    [Fact]
    public void LodPolicy_ThresholdChanges_ChangeTheDecision()
    {
        var before = new LodPolicy(new LodThresholds(6, 12));
        var after = new LodPolicy(new LodThresholds(6, 10));

        before.Decide("poles", 11).Should().Be(TileRenderTier.Raster);
        after.Decide("poles", 11).Should().Be(TileRenderTier.Vector);
    }

    [Fact]
    public void LodPolicy_FailsFast_OnInvalidDefaultThresholds()
    {
        var act = () => new LodPolicy(new LodThresholds(12, 6));

        var thrown = act.Should().Throw<InvalidLodPolicyException>().Which;
        thrown.Defect.Should().Be(LodThresholdsDefect.RasterFromZoomAboveVectorFromZoom);
        thrown.LayerId.Should().BeNull();
    }

    [Fact]
    public void LodPolicy_FailsFast_OnALayersInvalidThresholds()
    {
        var layers = new Dictionary<string, LodThresholds>(StringComparer.Ordinal)
        {
            ["poles"] = Poles,
            ["cables"] = new(0, 30),
        };

        var act = () => new LodPolicy(LodThresholds.VectorOnly, layers);

        var thrown = act.Should().Throw<InvalidLodPolicyException>().Which;
        thrown.Defect.Should().Be(LodThresholdsDefect.VectorFromZoomOutOfBounds);
        thrown.LayerId.Should().Be("cables");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void LodPolicy_RefusesABlankLayerId(string layerId)
    {
        var act = () => new LodPolicy(LodThresholds.VectorOnly, new Dictionary<string, LodThresholds>(StringComparer.Ordinal) { [layerId] = Poles });

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("layerThresholds");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void LodPolicy_Decide_RefusesABlankLayerId(string? layerId)
    {
        var policy = new LodPolicy(Poles);

        var decide = () => policy.Decide(layerId!, 5);
        var thresholds = () => policy.GetThresholds(layerId!);

        decide.Should().Throw<ArgumentException>();
        thresholds.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LodPolicy_IsAnILodPolicy()
    {
        new LodPolicy(Poles).Should().BeAssignableTo<ILodPolicy>();
    }
}

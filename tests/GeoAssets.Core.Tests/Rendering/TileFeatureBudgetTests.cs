using FluentAssertions;
using GeoAssets.Core.Rendering;
using Xunit;

namespace GeoAssets.Core.Tests.Rendering;

/// <summary>XD01-457 — <see cref="TileFeatureBudget"/>: the defaults (today's behaviour: no layer cap), per-layer lookups, and fail-fast construction.</summary>
public class TileFeatureBudgetTests
{
    private const string Poles = "0f0e0d0c-0000-4000-8000-000000000457";
    private const string Lines = "0f0e0d0c-0000-4000-8000-000000000458";

    [Fact]
    public void TheDefault_IsTenThousandPerTile_WithoutALayerCap()
    {
        var budget = TileFeatureBudget.Default;

        budget.MaxFeaturesPerTile.Should().Be(10_000).And.Be(TileFeatureBudget.DefaultMaxFeaturesPerTile);
        budget.MaxFeaturesPerLayer.Should().Be(10_000, "without a layer cap, one layer may spend the whole tile's budget");
        budget.Layers.Should().BeEmpty();
        budget.ForLayer(Poles).Should().Be(10_000);
    }

    [Fact]
    public void ALayer_GetsItsOwnBudget_ElseTheLayerCap_ElseTheTiles()
    {
        var capped = new TileFeatureBudget(5_000, 2_000, new Dictionary<string, int>(StringComparer.Ordinal) { [Poles] = 500 });
        var uncapped = new TileFeatureBudget(5_000, layerBudgets: new Dictionary<string, int>(StringComparer.Ordinal) { [Poles] = 500 });

        capped.ForLayer(Poles).Should().Be(500);
        capped.ForLayer(Lines).Should().Be(2_000);
        uncapped.ForLayer(Lines).Should().Be(5_000);
        capped.Layers.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, int>(Poles, 500));
    }

    [Fact]
    public void LayerIds_CompareOrdinally()
    {
        var budget = new TileFeatureBudget(100, 50, new Dictionary<string, int>(StringComparer.Ordinal) { [Poles] = 10 });

        budget.ForLayer(Poles.ToUpperInvariant()).Should().Be(50, "layer ids compare as the LOD policy's do, ordinally");
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(TileFeatureBudget.Limit + 1, null)]
    [InlineData(100, 0)]
    [InlineData(100, 101)]
    public void AnOutOfRangeBudget_IsRefused(int perTile, int? perLayer)
    {
        var act = () => new TileFeatureBudget(perTile, perLayer);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void AnOutOfRangeLayerBudget_IsRefused_NamingTheLayer(int layerBudget)
    {
        var act = () => new TileFeatureBudget(100, layerBudgets: new Dictionary<string, int>(StringComparer.Ordinal) { [Poles] = layerBudget });

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Contain(Poles);
    }

    [Fact]
    public void ABlankLayerId_IsRefused()
    {
        var act = () => new TileFeatureBudget(100, layerBudgets: new Dictionary<string, int>(StringComparer.Ordinal) { [" "] = 10 });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ForLayer_RefusesABlankLayerId()
    {
        var act = () => TileFeatureBudget.Default.ForLayer("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ALayerBudget_MayEqualTheTiles()
    {
        var budget = new TileFeatureBudget(TileFeatureBudget.Limit, TileFeatureBudget.Limit, new Dictionary<string, int>(StringComparer.Ordinal) { [Poles] = TileFeatureBudget.Limit });

        budget.ForLayer(Poles).Should().Be(TileFeatureBudget.Limit);
        budget.MaxFeaturesPerLayer.Should().Be(TileFeatureBudget.Limit);
    }
}

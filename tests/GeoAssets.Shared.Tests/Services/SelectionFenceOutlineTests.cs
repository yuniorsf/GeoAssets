using System.Text.Json;
using FluentAssertions;
using GeoAssets.Shared.Services;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Selection.Strategies;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

/// <summary>
/// Mapping from an order's <see cref="FeatureSelectionSpec"/> to the fence the map outlines
/// (XD01-311): only a <c>polygon-fence</c> spec with usable rings is drawable; nothing throws.
/// </summary>
public class SelectionFenceOutlineTests
{
    private static readonly DateTime ExecutedAt = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions _web = new(JsonSerializerDefaults.Web);

    private static readonly double[][][] Square =
    [
        [[-46.70, -23.60], [-46.60, -23.60], [-46.60, -23.50], [-46.70, -23.50], [-46.70, -23.60]]
    ];

    private static FeatureSelectionSpec Spec(string strategyId, Dictionary<string, object> parameters) => new()
    {
        StrategyId = strategyId,
        Parameters = parameters,
        ExecutedAt = ExecutedAt,
    };

    private static FeatureSelectionSpec FenceSpec(object rings) =>
        Spec("polygon-fence", new Dictionary<string, object>(StringComparer.Ordinal) { ["rings"] = rings });

    /// <summary>The spec as an order reloaded from storage carries it: parameter values are <see cref="JsonElement"/>s.</summary>
    private static FeatureSelectionSpec Reloaded(FeatureSelectionSpec spec)
    {
        return JsonSerializer.Deserialize<FeatureSelectionSpec>(JsonSerializer.Serialize(spec, _web), _web)!;
    }

    [Fact]
    public void SpecWithRings_IsDrawable_WithTheRingsUnchanged()
    {
        var result = SelectionFenceOutline.Read(FenceSpec(Square));

        result.Kind.Should().Be(SelectionFenceOutlineKind.Drawable);
        result.Rings.Should().BeEquivalentTo(Square, o => o.WithStrictOrdering());
        result.Problem.Should().BeNull();
    }

    [Fact]
    public void SpecWithExtraParameters_AsTheSelectionServiceStoresIt_IsDrawable()
    {
        var spec = Spec("polygon-fence", new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["rings"] = Square,
            ["assetTypeIds"] = new[] { "t1" },
            ["maxResults"] = 10_000,
        });

        SelectionFenceOutline.Read(spec).Kind.Should().Be(SelectionFenceOutlineKind.Drawable);
    }

    [Fact]
    public void SpecReloadedFromJson_IsDrawable_WithTheSameRings()
    {
        var reloaded = Reloaded(FenceSpec(Square));
        reloaded.Parameters["rings"].Should().BeOfType<JsonElement>("this is the reloaded form the reader must cope with");

        var result = SelectionFenceOutline.Read(reloaded);

        result.Kind.Should().Be(SelectionFenceOutlineKind.Drawable);
        result.Rings.Should().BeEquivalentTo(Square, o => o.WithStrictOrdering());
    }

    [Fact]
    public void SpecWithAHole_KeepsEveryRing()
    {
        double[][][] withHole = [Square[0], [[-46.68, -23.58], [-46.62, -23.58], [-46.62, -23.52], [-46.68, -23.58]]];

        var result = SelectionFenceOutline.Read(FenceSpec(withHole));

        result.Kind.Should().Be(SelectionFenceOutlineKind.Drawable);
        result.Rings.Should().HaveCount(2);
    }

    [Fact]
    public void NoSpec_HasNoOutline_AndNoProblem()
    {
        var result = SelectionFenceOutline.Read(null);

        result.Kind.Should().Be(SelectionFenceOutlineKind.None);
        result.Rings.Should().BeEmpty();
        result.Problem.Should().BeNull();
    }

    [Theory]
    [InlineData("identifier-list")]
    [InlineData("attribute-filter")]
    [InlineData("")]
    [InlineData("Polygon-Fence")]
    public void OtherStrategy_HasNoOutline_EvenIfItCarriesRings(string strategyId)
    {
        var spec = Spec(strategyId, new Dictionary<string, object>(StringComparer.Ordinal) { ["rings"] = Square, ["ids"] = new[] { "a" } });

        var result = SelectionFenceOutline.Read(spec);

        result.Kind.Should().Be(SelectionFenceOutlineKind.None);
        result.Problem.Should().BeNull();
    }

    [Fact]
    public void FenceWithoutARingsParameter_IsMalformed()
    {
        var result = SelectionFenceOutline.Read(Spec("polygon-fence", new Dictionary<string, object>(StringComparer.Ordinal)));

        result.Kind.Should().Be(SelectionFenceOutlineKind.Malformed);
        result.Rings.Should().BeEmpty();
        result.Problem.Should().Contain("rings");
    }

    public static TheoryData<string, object> MalformedRings => new()
    {
        { "a string", "not rings" },
        { "a number", 42 },
        { "no rings", Array.Empty<double[][]>() },
        { "an empty ring", new double[][][] { [] } },
        { "a two-position ring", new double[][][] { [[0, 0], [1, 1]] } },
        { "a null ring", new double[][]?[] { null } },
        { "a null position", new double[]?[][] { [[0, 0], null, [1, 1]] } },
        { "a one-number position", new double[][][] { [[0, 0], [1], [1, 1]] } },
        { "a three-number position", new double[][][] { [[0, 0, 5], [1, 1, 5], [1, 0, 5]] } },
        { "a NaN coordinate", new double[][][] { [[0, 0], [double.NaN, 1], [1, 0]] } },
        { "an infinite coordinate", new double[][][] { [[0, 0], [1, double.PositiveInfinity], [1, 0]] } },
        { "a longitude out of range", new double[][][] { [[0, 0], [181, 1], [1, 0]] } },
        { "a latitude out of range", new double[][][] { [[0, 0], [1, -91], [1, 0]] } },
    };

    [Theory]
    [MemberData(nameof(MalformedRings))]
    public void MalformedRings_AreReportedAsMalformed_NotThrown(string what, object rings)
    {
        var result = SelectionFenceOutline.Read(FenceSpec(rings));

        result.Kind.Should().Be(SelectionFenceOutlineKind.Malformed, what);
        result.Rings.Should().BeEmpty();
        result.Problem.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("""{"strategyId":"polygon-fence","parameters":{"rings":"oops"},"executedAt":"2026-11-16T12:00:00Z"}""")]
    [InlineData("""{"strategyId":"polygon-fence","parameters":{"rings":null},"executedAt":"2026-11-16T12:00:00Z"}""")]
    [InlineData("""{"strategyId":"polygon-fence","parameters":{"rings":[]},"executedAt":"2026-11-16T12:00:00Z"}""")]
    [InlineData("""{"strategyId":"polygon-fence","parameters":{"rings":[[["a","b"],[1,1],[1,0]]]},"executedAt":"2026-11-16T12:00:00Z"}""")]
    [InlineData("""{"strategyId":"polygon-fence","parameters":{"rings":[[[0,0],[1,1]]]},"executedAt":"2026-11-16T12:00:00Z"}""")]
    [InlineData("""{"strategyId":"polygon-fence","parameters":{"rings":{"type":"Polygon"}},"executedAt":"2026-11-16T12:00:00Z"}""")]
    [InlineData("""{"strategyId":"polygon-fence","parameters":{},"executedAt":"2026-11-16T12:00:00Z"}""")]
    public void MalformedRingsInAReloadedSpec_AreReportedAsMalformed_NotThrown(string json)
    {
        var spec = JsonSerializer.Deserialize<FeatureSelectionSpec>(json, _web)!;

        var result = SelectionFenceOutline.Read(spec);

        result.Kind.Should().Be(SelectionFenceOutlineKind.Malformed);
        result.Problem.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void StrategyAndParameterNames_AreTheOnesTheMapSelectionRequestSends()
    {
        // The outline reads the server strategy's names; the map's own request must use the same.
        MapSelectionRequest.PolygonFenceStrategyId.Should().Be(PolygonFenceSelectionStrategy.Id).And.Be("polygon-fence");
        MapSelectionRequest.RingsParameter.Should().Be(PolygonFenceSelectionStrategy.RingsParameter).And.Be("rings");
    }
}

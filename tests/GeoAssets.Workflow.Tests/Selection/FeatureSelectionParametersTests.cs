using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Selection.Strategies;
using Xunit;

namespace GeoAssets.Workflow.Tests.Selection;

public class FeatureSelectionParametersTests
{
    private static JsonElement AsJsonElement<T>(T value) =>
        JsonSerializer.SerializeToElement(value);

    // ── ToDouble ─────────────────────────────────────────────────────────────

    [Fact]
    public void ToDouble_FreshClrValue_Converts()
        => FeatureSelectionParameters.ToDouble(3.5).Should().Be(3.5);

    [Fact]
    public void ToDouble_JsonElement_Converts()
        => FeatureSelectionParameters.ToDouble(AsJsonElement(3.5)).Should().Be(3.5);

    // ── ToBoolean ────────────────────────────────────────────────────────────

    [Fact]
    public void ToBoolean_FreshClrValue_Converts()
        => FeatureSelectionParameters.ToBoolean(true).Should().BeTrue();

    [Fact]
    public void ToBoolean_JsonElement_Converts()
        => FeatureSelectionParameters.ToBoolean(AsJsonElement(true)).Should().BeTrue();

    // ── ToStringValue ────────────────────────────────────────────────────────

    [Fact]
    public void ToStringValue_FreshClrValue_Converts()
        => FeatureSelectionParameters.ToStringValue("abc").Should().Be("abc");

    [Fact]
    public void ToStringValue_JsonElement_Converts()
        => FeatureSelectionParameters.ToStringValue(AsJsonElement("abc")).Should().Be("abc");

    [Fact]
    public void ToStringValue_UnsupportedType_Throws()
        => FluentActions.Invoking(() => FeatureSelectionParameters.ToStringValue(42))
            .Should().Throw<InvalidCastException>();

    // ── ToEnum ───────────────────────────────────────────────────────────────

    [Fact]
    public void ToEnum_FreshClrValue_Converts()
        => FeatureSelectionParameters.ToEnum<TraversalDirection>(TraversalDirection.Upstream)
            .Should().Be(TraversalDirection.Upstream);

    [Fact]
    public void ToEnum_JsonElementString_Converts()
        => FeatureSelectionParameters.ToEnum<TraversalDirection>(AsJsonElement("Upstream"))
            .Should().Be(TraversalDirection.Upstream);

    [Fact]
    public void ToEnum_JsonElementNumber_Converts()
        => FeatureSelectionParameters.ToEnum<TraversalDirection>(AsJsonElement((int)TraversalDirection.Both))
            .Should().Be(TraversalDirection.Both);

    [Fact]
    public void ToEnum_UnsupportedType_Throws()
        => FluentActions.Invoking(() => FeatureSelectionParameters.ToEnum<TraversalDirection>(3.14))
            .Should().Throw<InvalidCastException>();

    // ── To<T> ────────────────────────────────────────────────────────────────

    [Fact]
    public void To_FreshClrValue_ReturnsAsIs()
    {
        var point = new GeoPoint(1, 2);
        FeatureSelectionParameters.To<GeoPoint>(point).Should().BeSameAs(point);
    }

    [Fact]
    public void To_JsonElement_Deserializes()
    {
        var point = new GeoPoint(1, 2);
        var result = FeatureSelectionParameters.To<GeoPoint>(AsJsonElement(point));

        result.Longitude.Should().Be(1);
        result.Latitude.Should().Be(2);
    }

    [Fact]
    public void To_UnsupportedType_Throws()
        => FluentActions.Invoking(() => FeatureSelectionParameters.To<GeoPoint>("not a point"))
            .Should().Throw<InvalidCastException>();

    // ── ToStringList ─────────────────────────────────────────────────────────

    [Fact]
    public void ToStringList_ReadOnlyList_ReturnsAsIs()
    {
        IReadOnlyList<string> list = ["a", "b"];
        FeatureSelectionParameters.ToStringList(list).Should().BeSameAs(list);
    }

    [Fact]
    public void ToStringList_Enumerable_Materializes()
    {
        IEnumerable<string> seq = new[] { "a", "b" }.Select(x => x);
        FeatureSelectionParameters.ToStringList(seq).Should().Equal("a", "b");
    }

    [Fact]
    public void ToStringList_JsonElementArray_Converts()
        => FeatureSelectionParameters.ToStringList(AsJsonElement(new[] { "a", "b" }))
            .Should().Equal("a", "b");

    [Fact]
    public void ToStringList_UnsupportedType_Throws()
        => FluentActions.Invoking(() => FeatureSelectionParameters.ToStringList(42))
            .Should().Throw<InvalidCastException>();

    [Fact]
    public void ToStringList_JsonElementArrayWithNonStringElement_ThrowsInvalidCastException()
        => FluentActions.Invoking(() => FeatureSelectionParameters.ToStringList(AsJsonElement(new[] { 1, 2 })))
            .Should().Throw<InvalidCastException>();

    // ── ToInt32 ──────────────────────────────────────────────────────────────

    [Fact]
    public void ToInt32_FreshClrValue_Converts()
        => FeatureSelectionParameters.ToInt32(7).Should().Be(7);

    [Fact]
    public void ToInt32_JsonElement_Converts()
        => FeatureSelectionParameters.ToInt32(AsJsonElement(7)).Should().Be(7);

    // ── ToRings ──────────────────────────────────────────────────────────────

    private static readonly double[][][] Rings = [[[0, 0], [1, 0], [1, 1], [0, 0]], [[0.2, 0.1], [0.4, 0.1], [0.4, 0.3], [0.2, 0.1]]];

    [Fact]
    public void ToRings_FreshArray_ReturnsAsIs()
        => FeatureSelectionParameters.ToRings(Rings).Should().BeSameAs(Rings);

    [Fact]
    public void ToRings_NestedLists_Materializes()
    {
        var nested = Rings.Select(ring => ring.Select(position => position.ToList()).ToList()).ToList();

        FeatureSelectionParameters.ToRings(nested).Should().BeEquivalentTo(Rings, o => o.WithStrictOrdering());
    }

    [Fact]
    public void ToRings_NestedListsWithMissingRingOrPosition_CarriesTheNullsThrough()
    {
        var nested = new List<List<double[]>?> { new() { new[] { 0.0, 0.0 }, null! }, null };

        var rings = FeatureSelectionParameters.ToRings(nested);

        rings.Should().HaveCount(2);
        rings[0].Should().HaveCount(2);
        rings[0][1].Should().BeNull();
        rings[1].Should().BeNull();
    }

    [Fact]
    public void ToRings_JsonElement_Converts()
        => FeatureSelectionParameters.ToRings(AsJsonElement(Rings))
            .Should().BeEquivalentTo(Rings, o => o.WithStrictOrdering());

    [Fact]
    public void ToRings_JsonElementOfTheWrongShape_ThrowsInvalidCastException()
        => FluentActions.Invoking(() => FeatureSelectionParameters.ToRings(AsJsonElement(new[] { "a" })))
            .Should().Throw<InvalidCastException>().WithInnerException<JsonException>();

    [Fact]
    public void ToRings_UnsupportedType_Throws()
        => FluentActions.Invoking(() => FeatureSelectionParameters.ToRings("rings"))
            .Should().Throw<InvalidCastException>();

    // ── Optional getters ─────────────────────────────────────────────────────

    [Fact]
    public void OptionalGetters_AbsentKey_ReturnNull()
    {
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);

        parameters.GetOptionalStringList("list").Should().BeNull();
        parameters.GetOptionalInt32("count").Should().BeNull();
    }

    [Fact]
    public void OptionalGetters_ClrNullOrJsonNull_ReturnNull()
    {
        var clrNull = new Dictionary<string, object>(StringComparer.Ordinal) { ["list"] = null!, ["count"] = null! };
        var jsonNull = JsonSerializer.Deserialize<Dictionary<string, object>>("""{"list":null,"count":null}""")!;
        var jsonNullElement = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["list"] = AsJsonElement<string[]?>(null),
            ["count"] = AsJsonElement<int?>(null),
        };

        foreach (var parameters in new[] { clrNull, jsonNull, jsonNullElement })
        {
            parameters.GetOptionalStringList("list").Should().BeNull();
            parameters.GetOptionalInt32("count").Should().BeNull();
        }
    }

    [Fact]
    public void OptionalGetters_PresentValue_ConvertFreshAndReloadedValuesAlike()
    {
        var fresh = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["list"] = new List<string> { "x", "y" },
            ["count"] = 3,
        };
        var reloaded = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(fresh))!;

        foreach (var parameters in new[] { fresh, reloaded })
        {
            parameters.GetOptionalStringList("list").Should().Equal("x", "y");
            parameters.GetOptionalInt32("count").Should().Be(3);
        }
    }

    [Fact]
    public void GetRings_MissingKey_ThrowsKeyNotFoundException()
        => FluentActions.Invoking(() => new Dictionary<string, object>(StringComparer.Ordinal).GetRings("rings"))
            .Should().Throw<KeyNotFoundException>();

    [Fact]
    public void GetRings_PresentKey_Converts()
        => new Dictionary<string, object>(StringComparer.Ordinal) { ["rings"] = AsJsonElement(Rings) }
            .GetRings("rings").Should().BeEquivalentTo(Rings, o => o.WithStrictOrdering());

    // ── Dictionary convenience wrappers ─────────────────────────────────────

    [Fact]
    public void DictionaryWrappers_DelegateToValueConverters()
    {
        var point = new GeoPoint(5, 6);
        IReadOnlyDictionary<string, object> parameters = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["d"]    = 1.5,
            ["b"]    = true,
            ["s"]    = "hi",
            ["e"]    = TraversalDirection.Both,
            ["p"]    = point,
            ["list"] = new List<string> { "x", "y" },
        };

        parameters.GetDouble("d").Should().Be(1.5);
        parameters.GetBoolean("b").Should().BeTrue();
        parameters.GetString("s").Should().Be("hi");
        parameters.GetEnum<TraversalDirection>("e").Should().Be(TraversalDirection.Both);
        parameters.GetValue<GeoPoint>("p").Should().BeSameAs(point);
        parameters.GetStringList("list").Should().Equal("x", "y");
    }
}

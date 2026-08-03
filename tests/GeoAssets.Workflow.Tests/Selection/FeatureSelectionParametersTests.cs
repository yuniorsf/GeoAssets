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

    // ── Dictionary convenience wrappers ─────────────────────────────────────

    [Fact]
    public void DictionaryWrappers_DelegateToValueConverters()
    {
        var point = new GeoPoint(5, 6);
        IReadOnlyDictionary<string, object> parameters = new Dictionary<string, object>
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

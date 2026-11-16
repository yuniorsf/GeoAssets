using FluentAssertions;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

/// <summary>
/// MAP-B23 (XD01-310): the guard behind <c>MapContainer.OnSelectionGeometryFromJs</c>. The
/// component method itself only logs a rejection or raises <c>OnSelectionGesture</c> with what
/// this parser returns; the parser has no access to <c>IAssetProvider</c>, so a selection
/// gesture cannot create or touch a feature.
/// </summary>
public class MapSelectionGestureParserTests
{
    private const string SquareJson = "[[[-70,18],[-69,18],[-69,19],[-70,19],[-70,18]]]";

    [Theory]
    [InlineData("box", MapSelectionTool.Box, false)]
    [InlineData("fence", MapSelectionTool.Fence, true)]
    public void MAP_B23_ValidInput_YieldsTheGestureWithParsedRingsToolAndModifier(string tool, MapSelectionTool expected, bool additive)
    {
        var parsed = MapSelectionGestureParser.TryParse(tool, SquareJson, additive, out var gesture, out var rejection);

        parsed.Should().BeTrue();
        rejection.Should().BeNull();
        gesture!.Tool.Should().Be(expected);
        gesture.Additive.Should().Be(additive);
        gesture.Rings.Should().ContainSingle().Which.Should().HaveCount(5);
        gesture.Rings[0][1].Should().Equal(-69, 18);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("click")]
    [InlineData("lasso")]
    [InlineData("Box")]
    [InlineData("Fence")]
    public void MAP_B23_UnknownOrNonAreaTool_IsRejected(string? tool)
    {
        var parsed = MapSelectionGestureParser.TryParse(tool, SquareJson, false, out var gesture, out var rejection);

        parsed.Should().BeFalse();
        gesture.Should().BeNull();
        rejection.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{\"type\":\"Polygon\"}")]
    [InlineData("[[[-70,18],[-69,18]")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("[[-70,18],[-69,18]]")]
    [InlineData("[[[-70],[-69,18]]]")]
    [InlineData("[[[-70,18,0],[-69,18,0]]]")]
    [InlineData("[[[\"a\",\"b\"]]]")]
    [InlineData("[[[-70,18],null]]")]
    public void MAP_B23_MalformedRingsJson_IsRejected(string? ringsJson)
    {
        var parsed = MapSelectionGestureParser.TryParse("fence", ringsJson, false, out var gesture, out var rejection);

        parsed.Should().BeFalse();
        gesture.Should().BeNull();
        rejection.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(MapSelectionTool.None, "none")]
    [InlineData(MapSelectionTool.ClickToggle, "click")]
    [InlineData(MapSelectionTool.Box, "box")]
    [InlineData(MapSelectionTool.Fence, "fence")]
    public void ToolWireNames_RoundTrip(MapSelectionTool tool, string name)
    {
        tool.ToJsName().Should().Be(name);
        MapSelectionToolExtensions.TryParseJsName(name, out var parsed).Should().BeTrue();
        parsed.Should().Be(tool);
    }

    [Fact]
    public void ToJsName_UndefinedTool_Throws()
    {
        var act = () => ((MapSelectionTool)99).ToJsName();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

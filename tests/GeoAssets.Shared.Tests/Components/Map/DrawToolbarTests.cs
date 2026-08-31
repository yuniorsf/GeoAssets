using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Shared.Components.Map;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.Map;

/// <summary>
/// <see cref="DrawToolbar"/>'s type-search filtering and style-resolution logic are factored out
/// as static methods specifically so they're directly unit-testable without a Blazor render tree
/// (this repo has no bUnit yet — see the pattern already used by <c>AssetsTable</c>/
/// <c>MainLayout</c>/<c>NavMenu</c>).
/// </summary>
public class DrawToolbarTests
{
    private static AssetType Type(string name, Guid? defaultLayerId = null) => new()
    {
        Name = name,
        AllowedGeometryType = GeometryType.Point,
        DefaultLayerId = defaultLayerId
    };

    // ── FilterAndSort ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FilterAndSort_BlankSearch_ReturnsAllTypesSorted(string search)
    {
        var types = new[] { Type("Bravo"), Type("Alpha") };

        var result = DrawToolbar.FilterAndSort(types, search);

        result.Select(t => t.Name).Should().Equal("Alpha", "Bravo");
    }

    [Fact]
    public void FilterAndSort_MatchesCaseInsensitiveSubstring()
    {
        var types = new[] { Type("Pole"), Type("Transformer") };

        var result = DrawToolbar.FilterAndSort(types, "pol");

        result.Should().ContainSingle().Which.Name.Should().Be("Pole");
    }

    [Fact]
    public void FilterAndSort_NoMatch_ReturnsEmpty()
    {
        var types = new[] { Type("Pole") };

        var result = DrawToolbar.FilterAndSort(types, "substation");

        result.Should().BeEmpty();
    }

    [Fact]
    public void FilterAndSort_SortIsCaseInsensitive()
    {
        var types = new[] { Type("bravo"), Type("Alpha") };

        var result = DrawToolbar.FilterAndSort(types, "");

        result.Select(t => t.Name).Should().Equal("Alpha", "bravo");
    }

    // ── ResolveStyle ───────────────────────────────────────────────────────────

    [Fact]
    public void ResolveStyle_NoDefaultLayerAndNoRules_ReturnsNull()
    {
        var type = Type("Generic");

        var result = DrawToolbar.ResolveStyle(type, layers: [], layerRules: []);

        result.Should().BeNull();
    }

    [Fact]
    public void ResolveStyle_FallsBackToAssetTypeDefaultLayer()
    {
        var layer = new Layer { Name = "Pole style", Color = "#8b5a2b" };
        var type = Type("Pole", defaultLayerId: layer.Id);

        var result = DrawToolbar.ResolveStyle(type, layers: [layer], layerRules: []);

        result.Should().BeSameAs(layer);
    }

    [Fact]
    public void ResolveStyle_UnconditionalLayerRule_OverridesDefaultLayer()
    {
        // Proves the palette reuses the real tiered resolver rather than a naive
        // "just look at DefaultLayerId" shortcut — an unconditional rule (empty Conditions)
        // still applies pre-draw, even though there's no real feature/CustomAttributes yet.
        var defaultLayer = new Layer { Name = "Default" };
        var ruleLayer    = new Layer { Name = "Rule" };
        var type = Type("Pole", defaultLayerId: defaultLayer.Id);
        var rule = new LayerRule { AssetTypeId = type.Id, LayerId = ruleLayer.Id, Priority = 0 };

        var result = DrawToolbar.ResolveStyle(type, layers: [defaultLayer, ruleLayer], layerRules: [rule]);

        result.Should().BeSameAs(ruleLayer);
    }

    [Fact]
    public void ResolveStyle_DanglingDefaultLayerId_FallsThroughToNull()
    {
        var type = Type("Pole", defaultLayerId: Guid.NewGuid());

        var result = DrawToolbar.ResolveStyle(type, layers: [], layerRules: []);

        result.Should().BeNull();
    }
}

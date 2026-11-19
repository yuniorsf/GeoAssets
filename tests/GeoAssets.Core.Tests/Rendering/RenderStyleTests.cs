using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using Xunit;

namespace GeoAssets.Core.Tests.Rendering;

public class RenderStyleTests
{
    private const string TypeColor = "#111111";
    private const string DefaultLayerColor = "#222222";
    private const string RuleLayerColor = "#333333";
    private const string OverrideLayerColor = "#444444";

    private static readonly AssetType Pole = new() { Name = "Pole", Color = TypeColor };

    private static Layer LayerOf(string color) => new() { Name = color, Color = color };

    private static GeoFeature FeatureOf(AssetType type, string? layerId = null, params (string Key, string Value)[] attributes)
    {
        var feature = new GeoFeature { Properties = { AssetTypeId = type.Id.ToString() } };
        if (layerId is not null)
            feature.Properties.LayerId = layerId;
        foreach (var (key, value) in attributes)
            feature.Properties.CustomAttributes[key] = value;
        return feature;
    }

    private static LayerRule RuleOf(AssetType type, Layer layer) => new()
    {
        AssetTypeId = type.Id,
        LayerId = layer.Id,
        Conditions = [new LayerRuleCondition { Attribute = "voltage", Operator = LayerRuleOperator.Equals, Value = "high" }],
    };

    [Fact]
    public void ColorOf_FeatureOverrideLayer_WinsOverEverything()
    {
        var overrideLayer = LayerOf(OverrideLayerColor);
        var ruleLayer = LayerOf(RuleLayerColor);
        var defaultLayer = LayerOf(DefaultLayerColor);
        var type = new AssetType { Name = "T", Color = TypeColor, DefaultLayerId = defaultLayer.Id };
        var style = new RenderStyle([type], [overrideLayer, ruleLayer, defaultLayer], [RuleOf(type, ruleLayer)]);

        var color = style.ColorOf(FeatureOf(type, overrideLayer.Id.ToString(), ("voltage", "high")));

        color.Should().Be(OverrideLayerColor);
    }

    [Fact]
    public void ColorOf_MatchingLayerRule_WinsOverTheTypesDefaultLayer()
    {
        var ruleLayer = LayerOf(RuleLayerColor);
        var defaultLayer = LayerOf(DefaultLayerColor);
        var type = new AssetType { Name = "T", Color = TypeColor, DefaultLayerId = defaultLayer.Id };
        var style = new RenderStyle([type], [ruleLayer, defaultLayer], [RuleOf(type, ruleLayer)]);

        var color = style.ColorOf(FeatureOf(type, null, ("voltage", "high")));

        color.Should().Be(RuleLayerColor);
    }

    [Fact]
    public void ColorOf_NoMatchingRule_UsesTheTypesDefaultLayer()
    {
        var ruleLayer = LayerOf(RuleLayerColor);
        var defaultLayer = LayerOf(DefaultLayerColor);
        var type = new AssetType { Name = "T", Color = TypeColor, DefaultLayerId = defaultLayer.Id };
        var style = new RenderStyle([type], [ruleLayer, defaultLayer], [RuleOf(type, ruleLayer)]);

        var color = style.ColorOf(FeatureOf(type, null, ("voltage", "low")));

        color.Should().Be(DefaultLayerColor);
    }

    [Fact]
    public void ColorOf_NoLayerAtAll_UsesTheAssetTypeColor()
    {
        var style = new RenderStyle([Pole], [], []);

        style.ColorOf(FeatureOf(Pole)).Should().Be(TypeColor);
    }

    [Fact]
    public void ColorOf_UnknownAssetType_UsesTheDefaultColor()
    {
        var style = new RenderStyle([Pole], [], []);
        var feature = new GeoFeature { Properties = { AssetTypeId = Guid.NewGuid().ToString() } };

        style.ColorOf(feature).Should().Be(RenderStyle.DefaultColor).And.Be("#3388ff");
    }

    [Fact]
    public void ColorOf_NonGuidAssetTypeId_UsesTheDefaultColor()
    {
        var style = new RenderStyle([Pole], [], []);
        var feature = new GeoFeature { Properties = { AssetTypeId = "not-a-guid" } };

        style.ColorOf(feature).Should().Be(RenderStyle.DefaultColor);
    }

    [Fact]
    public void ColorOf_DanglingOverrideLayer_FallsThroughToTheRule()
    {
        var ruleLayer = LayerOf(RuleLayerColor);
        var type = new AssetType { Name = "T", Color = TypeColor };
        var style = new RenderStyle([type], [ruleLayer], [RuleOf(type, ruleLayer)]);

        var color = style.ColorOf(FeatureOf(type, Guid.NewGuid().ToString(), ("voltage", "high")));

        color.Should().Be(RuleLayerColor);
    }

    [Fact]
    public void ColorOf_DanglingRuleAndDefaultLayers_FallThroughToTheAssetTypeColor()
    {
        var missing = LayerOf(RuleLayerColor);
        var type = new AssetType { Name = "T", Color = TypeColor, DefaultLayerId = Guid.NewGuid() };
        var style = new RenderStyle([type], [], [RuleOf(type, missing)]);

        var color = style.ColorOf(FeatureOf(type, Guid.NewGuid().ToString(), ("voltage", "high")));

        color.Should().Be(TypeColor);
    }

    [Fact]
    public void ColorOf_AssetTypeIdIsMatchedIgnoringCase()
    {
        var style = new RenderStyle([Pole], [], []);
        var feature = new GeoFeature { Properties = { AssetTypeId = Pole.Id.ToString().ToUpperInvariant() } };

        style.ColorOf(feature).Should().Be(TypeColor);
    }

    [Fact]
    public void ColorOf_NullFeature_Throws()
    {
        var style = new RenderStyle([Pole], [], []);

        var act = () => style.ColorOf(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        var typesNull = () => new RenderStyle(null!, [], []);
        var layersNull = () => new RenderStyle([], null!, []);
        var rulesNull = () => new RenderStyle([], [], null!);

        typesNull.Should().Throw<ArgumentNullException>();
        layersNull.Should().Throw<ArgumentNullException>();
        rulesNull.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Of_ReadsTheCatalogsTypesLayersAndEveryTypesRules()
    {
        var ruleLayer = LayerOf(RuleLayerColor);
        var type = new AssetType { Name = "Ruled", Color = TypeColor };
        var catalog = new TestAssetProvider();
        catalog.AddAssetType(type);
        catalog.AddLayer(ruleLayer);
        catalog.AddLayerRule(RuleOf(type, ruleLayer));

        var style = RenderStyle.Of(catalog);

        style.ColorOf(FeatureOf(type, null, ("voltage", "high"))).Should().Be(RuleLayerColor);
        style.ColorOf(FeatureOf(type)).Should().Be(TypeColor);
    }

    [Fact]
    public void Of_NullCatalog_Throws()
    {
        var act = () => RenderStyle.Of(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}

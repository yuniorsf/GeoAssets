using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

public class LayerResolverTests
{
    private static readonly Guid AssetTypeId = Guid.NewGuid();

    private static AssetType AssetType(Guid? defaultLayerId = null) => new()
    {
        Id = AssetTypeId,
        DefaultLayerId = defaultLayerId
    };

    private static GeoFeature Feature(string? layerIdOverride = null, params (string key, string value)[] attributes)
    {
        var feature = new GeoFeature { Properties = { AssetTypeId = AssetTypeId.ToString() } };
        if (layerIdOverride is not null)
            feature.Properties.LayerId = layerIdOverride;
        foreach (var (key, value) in attributes)
            feature.Properties.CustomAttributes[key] = value;
        return feature;
    }

    // ── Branch 1: per-feature override ────────────────────────────────────────

    [Fact]
    public void Resolve_OverridePresent_ReturnsOverrideLayer()
    {
        var overrideLayer = new Layer { Name = "Override" };
        var defaultLayer = new Layer { Name = "Default" };
        var feature = Feature(overrideLayer.Id.ToString());

        var result = LayerResolver.Resolve(
            feature, AssetType(defaultLayer.Id), [overrideLayer, defaultLayer], []);

        result.Should().Be(overrideLayer);
    }

    [Fact]
    public void Resolve_OverrideIdDoesNotExist_FallsThroughToDefault()
    {
        var defaultLayer = new Layer { Name = "Default" };
        var feature = Feature(Guid.NewGuid().ToString()); // dangling reference

        var result = LayerResolver.Resolve(
            feature, AssetType(defaultLayer.Id), [defaultLayer], []);

        result.Should().Be(defaultLayer);
    }

    [Fact]
    public void Resolve_OverrideEmpty_FallsThroughToDefault()
    {
        var defaultLayer = new Layer { Name = "Default" };
        var feature = Feature(); // no override

        var result = LayerResolver.Resolve(
            feature, AssetType(defaultLayer.Id), [defaultLayer], []);

        result.Should().Be(defaultLayer);
    }

    // ── Branch 2: matching rule ────────────────────────────────────────────────

    [Fact]
    public void Resolve_RuleMatches_ReturnsRuleLayer()
    {
        var ruleLayer = new Layer { Name = "Rule" };
        var rule = new LayerRule
        {
            AssetTypeId = AssetTypeId,
            LayerId = ruleLayer.Id,
            Conditions = [new LayerRuleCondition { Attribute = "material", Operator = LayerRuleOperator.Equals, Value = "steel" }]
        };
        var feature = Feature(attributes: ("material", "steel"));

        var result = LayerResolver.Resolve(feature, AssetType(), [ruleLayer], [rule]);

        result.Should().Be(ruleLayer);
    }

    [Fact]
    public void Resolve_MultipleRules_EvaluatesInPriorityOrder()
    {
        var lowPriorityLayer = new Layer { Name = "Low" };
        var highPriorityLayer = new Layer { Name = "High" };
        var lowPriorityRule = new LayerRule { AssetTypeId = AssetTypeId, LayerId = lowPriorityLayer.Id, Priority = 10 };
        var highPriorityRule = new LayerRule { AssetTypeId = AssetTypeId, LayerId = highPriorityLayer.Id, Priority = 1 };
        var feature = Feature();

        var result = LayerResolver.Resolve(
            feature, AssetType(), [lowPriorityLayer, highPriorityLayer], [lowPriorityRule, highPriorityRule]);

        result.Should().Be(highPriorityLayer);
    }

    [Fact]
    public void Resolve_RuleConditions_AllMustMatch()
    {
        var ruleLayer = new Layer { Name = "Rule" };
        var rule = new LayerRule
        {
            AssetTypeId = AssetTypeId,
            LayerId = ruleLayer.Id,
            Conditions =
            [
                new LayerRuleCondition { Attribute = "material", Operator = LayerRuleOperator.Equals, Value = "steel" },
                new LayerRuleCondition { Attribute = "status", Operator = LayerRuleOperator.Equals, Value = "active" }
            ]
        };
        // Only one of the two conditions matches.
        var feature = Feature(attributes: ("material", "steel"));

        var result = LayerResolver.Resolve(feature, AssetType(), [ruleLayer], [rule]);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(LayerRuleOperator.NotEquals, "300", true)]
    [InlineData(LayerRuleOperator.NotEquals, "200", false)]
    [InlineData(LayerRuleOperator.GreaterThanOrEqual, "100", true)]
    [InlineData(LayerRuleOperator.GreaterThanOrEqual, "300", false)]
    [InlineData(LayerRuleOperator.LessThanOrEqual, "300", true)]
    [InlineData(LayerRuleOperator.LessThanOrEqual, "100", false)]
    public void Resolve_ComparisonOperators_MatchAsExpected(LayerRuleOperator op, string conditionValue, bool expectMatch)
    {
        var ruleLayer = new Layer { Name = "Rule" };
        var rule = new LayerRule
        {
            AssetTypeId = AssetTypeId,
            LayerId = ruleLayer.Id,
            Conditions = [new LayerRuleCondition { Attribute = "voltage", Operator = op, Value = conditionValue }]
        };
        var feature = Feature(attributes: ("voltage", "200"));

        var result = LayerResolver.Resolve(feature, AssetType(), [ruleLayer], [rule]);

        (result == ruleLayer).Should().Be(expectMatch);
    }

    [Fact]
    public void Resolve_NumericOperator_NonNumericAttribute_DoesNotMatch()
    {
        var ruleLayer = new Layer { Name = "Rule" };
        var rule = new LayerRule
        {
            AssetTypeId = AssetTypeId,
            LayerId = ruleLayer.Id,
            Conditions = [new LayerRuleCondition { Attribute = "voltage", Operator = LayerRuleOperator.GreaterThanOrEqual, Value = "100" }]
        };
        var feature = Feature(attributes: ("voltage", "high"));

        var result = LayerResolver.Resolve(feature, AssetType(), [ruleLayer], [rule]);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_ConditionAttributeMissing_DoesNotMatch()
    {
        var ruleLayer = new Layer { Name = "Rule" };
        var rule = new LayerRule
        {
            AssetTypeId = AssetTypeId,
            LayerId = ruleLayer.Id,
            Conditions = [new LayerRuleCondition { Attribute = "material", Operator = LayerRuleOperator.Equals, Value = "steel" }]
        };
        var feature = Feature(); // no custom attributes at all

        var result = LayerResolver.Resolve(feature, AssetType(), [ruleLayer], [rule]);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_RuleForDifferentAssetType_IsIgnored()
    {
        var ruleLayer = new Layer { Name = "Rule" };
        var rule = new LayerRule { AssetTypeId = Guid.NewGuid(), LayerId = ruleLayer.Id };
        var feature = Feature();

        var result = LayerResolver.Resolve(feature, AssetType(), [ruleLayer], [rule]);

        result.Should().BeNull();
    }

    // ── Branch 3: default layer ────────────────────────────────────────────────

    [Fact]
    public void Resolve_NoRuleMatches_DefaultPresent_ReturnsDefaultLayer()
    {
        var defaultLayer = new Layer { Name = "Default" };
        var ruleLayer = new Layer { Name = "Rule" };
        var rule = new LayerRule
        {
            AssetTypeId = AssetTypeId,
            LayerId = ruleLayer.Id,
            Conditions = [new LayerRuleCondition { Attribute = "material", Operator = LayerRuleOperator.Equals, Value = "steel" }]
        };
        var feature = Feature(); // no matching attributes

        var result = LayerResolver.Resolve(
            feature, AssetType(defaultLayer.Id), [defaultLayer, ruleLayer], [rule]);

        result.Should().Be(defaultLayer);
    }

    [Fact]
    public void Resolve_DefaultLayerIdDoesNotExist_ReturnsNull()
    {
        var feature = Feature();

        var result = LayerResolver.Resolve(feature, AssetType(Guid.NewGuid()), [], []);

        result.Should().BeNull();
    }

    // ── Branch 4: nothing present ──────────────────────────────────────────────

    [Fact]
    public void Resolve_NothingPresent_ReturnsNull()
    {
        var feature = Feature();

        var result = LayerResolver.Resolve(feature, AssetType(), [], []);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_UnknownAssetType_NoOverride_ReturnsNull()
    {
        var feature = Feature();

        var result = LayerResolver.Resolve(feature, assetType: null, [], []);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_UnknownAssetType_OverridePresent_ReturnsOverrideLayer()
    {
        var overrideLayer = new Layer { Name = "Override" };
        var feature = Feature(overrideLayer.Id.ToString());

        var result = LayerResolver.Resolve(feature, assetType: null, [overrideLayer], []);

        result.Should().Be(overrideLayer);
    }
}

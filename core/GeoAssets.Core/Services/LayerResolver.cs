using System.Globalization;
using GeoAssets.Core.Models;

namespace GeoAssets.Core.Services;

/// <summary>
/// Resolves the effective <see cref="Layer"/> style for a <see cref="GeoFeature"/>, checking in
/// order: (1) the feature's own <see cref="GeoFeatureProperties.LayerId"/> override, (2) the first
/// <see cref="LayerRule"/> for the feature's <see cref="AssetType"/> (by ascending
/// <see cref="LayerRule.Priority"/>) whose <see cref="LayerRule.Conditions"/> all match
/// <see cref="GeoFeatureProperties.CustomAttributes"/>, (3) the asset type's
/// <see cref="AssetType.DefaultLayerId"/>, (4) <c>null</c> — caller falls back to a hardcoded
/// generic style. A tier whose referenced <see cref="Layer"/> no longer exists (a dangling
/// reference, e.g. a deleted layer) falls through to the next tier rather than stopping.
/// </summary>
public static class LayerResolver
{
    public static Layer? Resolve(
        GeoFeature feature,
        AssetType? assetType,
        IReadOnlyList<Layer> layers,
        IReadOnlyList<LayerRule> layerRules)
    {
        // 1. Per-feature override
        if (Guid.TryParse(feature.Properties.LayerId, out var overrideId))
        {
            var overrideLayer = layers.FirstOrDefault(l => l.Id == overrideId);
            if (overrideLayer is not null) return overrideLayer;
        }

        if (assetType is null) return null;

        // 2. First matching rule, by ascending priority
        var matchingRule = layerRules
            .Where(r => r.AssetTypeId == assetType.Id)
            .OrderBy(r => r.Priority)
            .FirstOrDefault(r => r.Conditions.All(c => Matches(c, feature.Properties.CustomAttributes)));
        if (matchingRule is not null)
        {
            var ruleLayer = layers.FirstOrDefault(l => l.Id == matchingRule.LayerId);
            if (ruleLayer is not null) return ruleLayer;
        }

        // 3. Asset type default
        if (assetType.DefaultLayerId is { } defaultLayerId)
        {
            var defaultLayer = layers.FirstOrDefault(l => l.Id == defaultLayerId);
            if (defaultLayer is not null) return defaultLayer;
        }

        // 4. Nothing resolved
        return null;
    }

    private static bool Matches(LayerRuleCondition condition, IReadOnlyDictionary<string, string> attributes)
    {
        if (!attributes.TryGetValue(condition.Attribute, out var actual))
            return false;

        return condition.Operator switch
        {
            LayerRuleOperator.Equals => actual == condition.Value,
            LayerRuleOperator.NotEquals => actual != condition.Value,
            LayerRuleOperator.GreaterThanOrEqual => TryCompareNumeric(actual, condition.Value, out var ge) && ge >= 0,
            LayerRuleOperator.LessThanOrEqual => TryCompareNumeric(actual, condition.Value, out var le) && le <= 0,
            _ => false
        };
    }

    /// <summary>
    /// Numeric comparison for the ordering operators. Returns <c>false</c> (no match) when
    /// either side isn't a number — <see cref="GeoFeatureProperties.CustomAttributes"/> values
    /// are free-form strings, so a non-numeric attribute can never satisfy an ordering condition.
    /// </summary>
    private static bool TryCompareNumeric(string actual, string conditionValue, out int comparison)
    {
        comparison = 0;
        if (!double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var actualNum))
            return false;
        if (!double.TryParse(conditionValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var conditionNum))
            return false;

        comparison = actualNum.CompareTo(conditionNum);
        return true;
    }
}

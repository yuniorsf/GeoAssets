namespace GeoAssets.Core.Models;

/// <summary>
/// Resolves which <see cref="Layer"/> style applies to features of a given <see cref="AssetType"/>
/// when all <see cref="Conditions"/> match. Multiple rules may target the same asset type;
/// <see cref="Priority"/> (lower = evaluated first) breaks the tie. Resolution logic itself lands
/// in XD01-111.
/// </summary>
public sealed class LayerRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssetTypeId { get; set; }
    public Guid LayerId { get; set; }

    /// <summary>Evaluation order — lower values are evaluated first.</summary>
    public int Priority { get; set; }

    /// <summary>All conditions must match (ANDed) for this rule to apply.</summary>
    public List<LayerRuleCondition> Conditions { get; set; } = [];
}

/// <summary>One condition in a <see cref="LayerRule"/>, matched against <see cref="GeoFeatureProperties.CustomAttributes"/>.</summary>
public sealed class LayerRuleCondition
{
    /// <summary>Key to look up in <see cref="GeoFeatureProperties.CustomAttributes"/>.</summary>
    public string Attribute { get; set; } = string.Empty;

    public LayerRuleOperator Operator { get; set; } = LayerRuleOperator.Equals;

    public string Value { get; set; } = string.Empty;
}

/// <summary>Comparison operators available to a <see cref="LayerRuleCondition"/>. Kept minimal for v1.</summary>
public enum LayerRuleOperator
{
    Equals,
    NotEquals,
    GreaterThanOrEqual,
    LessThanOrEqual
}

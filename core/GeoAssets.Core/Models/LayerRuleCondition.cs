namespace GeoAssets.Core.Models;

/// <summary>One condition in a <see cref="LayerRule"/>, matched against <see cref="GeoFeatureProperties.CustomAttributes"/>.</summary>
public sealed class LayerRuleCondition
{
    /// <summary>Key to look up in <see cref="GeoFeatureProperties.CustomAttributes"/>.</summary>
    public string Attribute { get; set; } = string.Empty;

    public LayerRuleOperator Operator { get; set; } = LayerRuleOperator.Equals;

    public string Value { get; set; } = string.Empty;
}

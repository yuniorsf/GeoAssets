namespace GeoAssets.Core.Models;

/// <summary>Comparison operators available to a <see cref="LayerRuleCondition"/>. Kept minimal for v1.</summary>
public enum LayerRuleOperator
{
    Equals,
    NotEquals,
    GreaterThanOrEqual,
    LessThanOrEqual
}

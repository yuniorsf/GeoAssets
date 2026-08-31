using GeoAssets.Core.Models;

namespace GeoAssets.Provider.PostgreSQL.Entities;

/// <summary>EF Core entity that maps to the <c>layer_rule_condition</c> table.</summary>
public sealed class LayerRuleConditionRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LayerRuleId { get; set; }
    public string Attribute { get; set; } = string.Empty;
    public LayerRuleOperator Operator { get; set; }
    public string Value { get; set; } = string.Empty;

    // ── Navigation ──────────────────────────────────────────────────────────────
    public LayerRuleRow? LayerRule { get; set; }
}

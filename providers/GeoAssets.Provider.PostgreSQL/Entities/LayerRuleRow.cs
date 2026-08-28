namespace GeoAssets.Provider.PostgreSQL.Entities;

/// <summary>EF Core entity that maps to the <c>layer_rule</c> table.</summary>
public sealed class LayerRuleRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssetTypeId { get; set; }
    public Guid LayerId { get; set; }
    public int Priority { get; set; }

    // ── Navigation ──────────────────────────────────────────────────────────────
    public AssetTypeRow? AssetType { get; set; }
    public LayerRow? Layer { get; set; }
    public List<LayerRuleConditionRow> Conditions { get; set; } = [];
}

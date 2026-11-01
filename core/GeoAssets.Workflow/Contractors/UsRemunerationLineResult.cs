namespace GeoAssets.Workflow.Contractors;

/// <summary>One resolved BoQ line's contribution to an <see cref="OrderRemunerationResult"/> (XD01-247).</summary>
public sealed class UsRemunerationLineResult
{
    public string ItemCode { get; init; } = string.Empty;
    public decimal Quantity { get; init; }

    /// <summary>The catalog item's own <c>FactorUS × RegionalMultiplier</c>, before quantity.</summary>
    public decimal UnitUs { get; init; }

    /// <summary><c>Quantity × UnitUs</c>.</summary>
    public decimal LineUs { get; init; }

    /// <summary><c>LineUs × FinancialConversionRate</c>.</summary>
    public decimal LineBrl { get; init; }
}

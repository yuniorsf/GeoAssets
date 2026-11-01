namespace GeoAssets.Workflow.Contractors;

/// <summary>
/// Result of <see cref="OrderRemunerationCalculator.CalculateAsync"/> (XD01-247): the order's
/// aggregate <c>Total Order US</c>/<c>Total Order Remuneration (BRL)</c> plus a per-line
/// breakdown for the ledger/dossier to attribute the total back to individual BoQ items.
/// </summary>
public sealed class OrderRemunerationResult
{
    public decimal TotalUs { get; init; }
    public decimal TotalBrl { get; init; }
    public IReadOnlyList<UsRemunerationLineResult> Lines { get; init; } = [];
}

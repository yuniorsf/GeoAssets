namespace GeoAssets.Workflow.Contractors;

/// <summary>
/// Computes <c>TotalUS = Σ(ItemQuantity × FactorUS × RegionalMultiplier)</c> (XD01-241's own
/// formula) over a Bill-of-Quantities line list — pure arithmetic, no persistence dependency.
/// </summary>
public static class UsTotalCalculator
{
    public static decimal TotalUs(IEnumerable<(UsCatalogItem Item, decimal Quantity)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        decimal total = 0;
        foreach (var (item, quantity) in lines)
        {
            ArgumentNullException.ThrowIfNull(item);
            total += quantity * item.BaseUsFactor * item.RegionalMultiplier;
        }

        return total;
    }
}

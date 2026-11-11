namespace GeoAssets.Workflow.Erp.Sienge;

/// <summary>
/// Compares Sienge purchase-order quantities against field-reported consumption per
/// (<see cref="SiengePurchaseOrderRecord.CostCenterCode"/>, <see cref="SiengePurchaseOrderRecord.MaterialCode"/>)
/// pair and flags discrepancies beyond <paramref name="toleranceFraction"/> (XD01-254's
/// "reconciliation report flags discrepancies between field-reported consumption and Sienge
/// purchase orders" AC). Pure, no I/O — purchase orders/consumption are pulled by the caller.
/// </summary>
public static class PurchaseOrderReconciler
{
    public static IReadOnlyList<ConsumptionDiscrepancyFlag> Reconcile(
        IReadOnlyList<SiengePurchaseOrderRecord> purchaseOrders,
        IReadOnlyList<FieldConsumptionRecord> consumption,
        decimal toleranceFraction)
    {
        ArgumentNullException.ThrowIfNull(purchaseOrders);
        ArgumentNullException.ThrowIfNull(consumption);
        ArgumentOutOfRangeException.ThrowIfNegative(toleranceFraction);

        var purchasedByKey = purchaseOrders
            .GroupBy(p => (p.CostCenterCode, p.MaterialCode))
            .ToDictionary(g => g.Key, g => g.Sum(p => p.QuantityPurchased));

        var consumedByKey = consumption
            .GroupBy(c => (c.CostCenterCode, c.MaterialCode))
            .ToDictionary(g => g.Key, g => g.Sum(c => c.QuantityConsumed));

        var flags = new List<ConsumptionDiscrepancyFlag>();
        foreach (var (key, purchasedQuantity) in purchasedByKey)
        {
            var consumedQuantity = consumedByKey.GetValueOrDefault(key, 0m);
            var discrepancy = purchasedQuantity - consumedQuantity;
            if (discrepancy == 0 || purchasedQuantity == 0) continue;

            var discrepancyFraction = Math.Abs(discrepancy) / purchasedQuantity;
            if (discrepancyFraction > toleranceFraction)
            {
                flags.Add(new ConsumptionDiscrepancyFlag(key.CostCenterCode, key.MaterialCode, purchasedQuantity, consumedQuantity, discrepancy, discrepancyFraction));
            }
        }

        return flags;
    }
}

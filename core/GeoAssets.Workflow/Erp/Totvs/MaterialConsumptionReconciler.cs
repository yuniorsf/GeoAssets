namespace GeoAssets.Workflow.Erp.Totvs;

/// <summary>
/// Compares warehouse-issued quantities against field-consumed quantities per
/// (<see cref="WarehouseStockIssuanceRecord.MaterialCode"/>, <c>ServiceOrderId</c>) pair and
/// flags shrinkage beyond <paramref name="toleranceFraction"/> (XD01-253's "reconcile warehouse
/// stock issues against field material consumption to flag shrinkage" AC). Pure, no I/O —
/// issuances/consumption are pulled/mapped by the caller.
/// </summary>
public static class MaterialConsumptionReconciler
{
    public static IReadOnlyList<MaterialShrinkageFlag> Reconcile(
        IReadOnlyList<WarehouseStockIssuanceRecord> issuances,
        IReadOnlyList<FieldMaterialConsumptionRecord> consumption,
        decimal toleranceFraction)
    {
        ArgumentNullException.ThrowIfNull(issuances);
        ArgumentNullException.ThrowIfNull(consumption);
        ArgumentOutOfRangeException.ThrowIfNegative(toleranceFraction);

        var issuedByKey = issuances
            .GroupBy(i => (i.MaterialCode, i.ServiceOrderId))
            .ToDictionary(g => g.Key, g => g.Sum(i => i.QuantityIssued));

        var consumedByKey = consumption
            .GroupBy(c => (c.MaterialCode, c.ServiceOrderId))
            .ToDictionary(g => g.Key, g => g.Sum(c => c.QuantityConsumed));

        var flags = new List<MaterialShrinkageFlag>();
        foreach (var (key, issuedQuantity) in issuedByKey)
        {
            var consumedQuantity = consumedByKey.GetValueOrDefault(key, 0m);
            var shrinkage = issuedQuantity - consumedQuantity;
            if (shrinkage <= 0 || issuedQuantity == 0) continue;

            var shrinkageFraction = shrinkage / issuedQuantity;
            if (shrinkageFraction > toleranceFraction)
            {
                flags.Add(new MaterialShrinkageFlag(key.MaterialCode, key.ServiceOrderId, issuedQuantity, consumedQuantity, shrinkage, shrinkageFraction));
            }
        }

        return flags;
    }
}

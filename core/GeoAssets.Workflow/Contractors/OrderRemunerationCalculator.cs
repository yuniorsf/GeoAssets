namespace GeoAssets.Workflow.Contractors;

/// <summary>
/// Computes an order's <c>Total Order US = Σ(ItemQuantity × FactorUS × RegionalMultiplier)</c>
/// (delegating to <see cref="UsTotalCalculator"/>) and
/// <c>Total Order Remuneration (BRL) = Total Order US × FinancialConversionRate</c> (XD01-247)
/// from a posted BoQ line list, resolving each line's <see cref="UsCatalogItem"/> through
/// <see cref="IUsCatalogRepository"/> — which is itself scoped per <c>organizationId</c>, the
/// "pluggable lookup" this ticket's AC asks for so CEMIG and CPFL rates never collide.
///
/// <see cref="FinancialConversionRate"/> is required to be the same across every resolved line
/// (see <see cref="AmbiguousFinancialConversionRateException"/>) since the order-level formula
/// only has one well-defined answer when it is.
/// </summary>
public sealed class OrderRemunerationCalculator(IUsCatalogRepository catalog)
{
    public async Task<OrderRemunerationResult> CalculateAsync(
        Guid organizationId,
        IReadOnlyList<(string ItemCode, decimal Quantity)> lines,
        DateTime asOfUtc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            return new OrderRemunerationResult { TotalUs = 0, TotalBrl = 0, Lines = [] };
        }

        var resolved = new List<(UsCatalogItem Item, decimal Quantity)>(lines.Count);
        foreach (var (itemCode, quantity) in lines)
        {
            var item = await catalog.FindEffectiveAsync(organizationId, itemCode, asOfUtc, ct).ConfigureAwait(false)
                ?? throw new UsCatalogItemNotFoundException(organizationId, itemCode, asOfUtc);
            resolved.Add((item, quantity));
        }

        var distinctRates = resolved.Select(r => r.Item.MonetaryConversionRate).Distinct().ToList();
        if (distinctRates.Count > 1)
        {
            throw new AmbiguousFinancialConversionRateException(distinctRates);
        }

        var financialConversionRate = distinctRates[0];
        var totalUs = UsTotalCalculator.TotalUs(resolved);
        var totalBrl = Math.Round(totalUs * financialConversionRate, 2, MidpointRounding.AwayFromZero);

        var lineResults = resolved.Select(r =>
        {
            var unitUs = r.Item.BaseUsFactor * r.Item.RegionalMultiplier;
            var lineUs = r.Quantity * unitUs;
            return new UsRemunerationLineResult
            {
                ItemCode = r.Item.ItemCode,
                Quantity = r.Quantity,
                UnitUs = unitUs,
                LineUs = lineUs,
                LineBrl = Math.Round(lineUs * financialConversionRate, 2, MidpointRounding.AwayFromZero),
            };
        }).ToList();

        return new OrderRemunerationResult { TotalUs = totalUs, TotalBrl = totalBrl, Lines = lineResults };
    }
}

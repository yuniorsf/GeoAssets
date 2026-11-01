namespace GeoAssets.Workflow.Contractors;

/// <summary>
/// Thrown by <see cref="OrderRemunerationCalculator"/> when an order's BoQ lines resolve to
/// catalog items carrying more than one distinct <see cref="UsCatalogItem.MonetaryConversionRate"/>
/// (XD01-247). "Total Order Remuneration (BRL) = Total Order US × FinancialConversionRate" only
/// has one well-defined answer when every line shares the same rate — silently picking one
/// line's rate over another's would misstate the order's total, so this fails loud instead of
/// guessing.
/// </summary>
public sealed class AmbiguousFinancialConversionRateException : InvalidOperationException
{
    public IReadOnlyList<decimal> DistinctRates { get; } = [];

    public AmbiguousFinancialConversionRateException(IReadOnlyList<decimal> distinctRates)
        : base(BuildMessage(distinctRates))
    {
        DistinctRates = distinctRates;
    }

    private static string BuildMessage(IReadOnlyList<decimal> distinctRates)
    {
        ArgumentNullException.ThrowIfNull(distinctRates);
        return $"Order's BoQ lines resolve to {distinctRates.Count} distinct financial conversion rates ({string.Join(", ", distinctRates)}); a single order-level remuneration total requires exactly one.";
    }

    public AmbiguousFinancialConversionRateException()
    {
    }

    public AmbiguousFinancialConversionRateException(string message) : base(message)
    {
    }

    public AmbiguousFinancialConversionRateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

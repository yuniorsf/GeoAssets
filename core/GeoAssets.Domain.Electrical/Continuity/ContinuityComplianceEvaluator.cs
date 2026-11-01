namespace GeoAssets.Domain.Electrical.Continuity;

/// <summary>Compares apurado <see cref="ContinuityIndicators"/> against a consumer unit's <see cref="ContinuityComplianceLimits"/>.</summary>
public static class ContinuityComplianceEvaluator
{
    public static ContinuityComplianceResult Evaluate(ContinuityIndicators indicators, ContinuityComplianceLimits limits)
    {
        ArgumentNullException.ThrowIfNull(indicators);
        ArgumentNullException.ThrowIfNull(limits);

        return new ContinuityComplianceResult(
            Dic: Compare(indicators.Dic.TotalSeconds, limits.DicLimit.TotalSeconds),
            Fic: Compare(indicators.Fic, limits.FicLimit),
            Dmic: Compare(indicators.Dmic.TotalSeconds, limits.DmicLimit.TotalSeconds));
    }

    private static IndicatorComplianceStatus Compare(double apurado, double limite)
    {
        if (limite <= 0) return new IndicatorComplianceStatus(apurado > 0, apurado > 0 ? double.PositiveInfinity : 0);

        var ratio = apurado / limite;
        return new IndicatorComplianceStatus(ratio > 1.0, Math.Max(0, ratio - 1.0));
    }
}

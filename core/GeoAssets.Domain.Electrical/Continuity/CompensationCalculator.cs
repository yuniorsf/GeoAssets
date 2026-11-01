namespace GeoAssets.Domain.Electrical.Continuity;

/// <summary>
/// Computes a PRODIST-style excess-ratio compensation amount per violated indicator:
/// <c>coefficient × excessRatio × referenceValue</c>, summed across DIC/FIC/DMIC. Zero for an
/// indicator that didn't exceed its limit.
///
/// This implements the general excess-ratio <i>shape</i> PRODIST Módulo 8 compensation
/// calculations follow, not a specific, numerically-verified ANEEL formula — see
/// <see cref="CompensationCoefficients"/>'s own doc comment for why the coefficients are a
/// required caller-supplied parameter rather than a constant. <paramref name="referenceValue"/>
/// (the consumer unit's billing reference PRODIST's formula multiplies by — e.g. its average
/// monthly energy consumption value) is likewise caller-supplied, not computed here.
/// </summary>
public static class CompensationCalculator
{
    public static CompensationResult Calculate(
        ContinuityComplianceResult compliance,
        CompensationCoefficients coefficients,
        decimal referenceValue)
    {
        ArgumentNullException.ThrowIfNull(compliance);
        ArgumentNullException.ThrowIfNull(coefficients);

        return new CompensationResult(
            Dic: AmountFor(compliance.Dic, coefficients.Dic, referenceValue),
            Fic: AmountFor(compliance.Fic, coefficients.Fic, referenceValue),
            Dmic: AmountFor(compliance.Dmic, coefficients.Dmic, referenceValue));
    }

    private static decimal AmountFor(IndicatorComplianceStatus status, decimal coefficient, decimal referenceValue)
    {
        if (!status.Exceeded || double.IsPositiveInfinity(status.ExcessRatio)) return status.Exceeded ? coefficient * referenceValue : 0m;
        return coefficient * (decimal)status.ExcessRatio * referenceValue;
    }
}

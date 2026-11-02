using GeoAssets.IpBilling.Calculation;

namespace GeoAssets.IpBilling.Levantamento;

/// <summary>
/// Reconstructs the legacy GEOS_FAT_IP "Tipo de Retroação" decision for one transformer on a
/// Levantamento (XD01-179) — see <see cref="TipoRetroacao"/>'s own doc comment for the
/// reconstruction caveat and the full decision tree this implements.
/// </summary>
public static class LevantamentoRetroactiveCalculator
{
    public static RetroactiveOutcome DetermineOutcome(
        TransformerHistoryContext history,
        DateOnly asOf,
        decimal dailyNormalKwhRate)
    {
        ArgumentNullException.ThrowIfNull(history);

        if (!history.MunicipioHasAnyValidatedLevantamento)
        {
            // Code 30 — município's very first Levantamento ever: no baseline to retroact
            // against, so no retroactive kWh is computed even though this ticket's working
            // default still flags every transformer as "new" for audit/reporting purposes.
            return new RetroactiveOutcome(true, TipoRetroacao.PrimeiraLevantamentoDoMunicipio30, 0m);
        }

        if (history.SeenInMostRecentValidatedLevantamento)
        {
            // Existing transformer — no code, no persisted retroactive figure (display-time-only
            // comparison residual, computed elsewhere, never here).
            return new RetroactiveOutcome(false, null, null);
        }

        if (!history.EverSeenInAnyValidatedLevantamento)
        {
            // Code 20 — never surveyed before at all (not just missing from the latest survey):
            // anchor at the município's oldest available baseline.
            var anchor = history.FirstValidatedLevantamentoCompetenciaStart
                ?? throw new InvalidOperationException(
                    "MunicipioHasAnyValidatedLevantamento is true but FirstValidatedLevantamentoCompetenciaStart is null.");
            var retro = ComputeRetroKwh(anchor, asOf, dailyNormalKwhRate, excludeKwh: 0m);
            return new RetroactiveOutcome(true, TipoRetroacao.HistoricoCompleto20, retro);
        }

        // Reappeared since the most recent validated Levantamento. Which anchor applies (codes
        // 00/01 vs. 10/11) and whether anything needs excluding from it (naive vs. [FIX]) are
        // two independent questions — see TransformerHistoryContext's own doc comment.
        if (history.FiscalizacaoAnchorDate is { } fiscAnchor)
        {
            var retro = ComputeRetroKwh(fiscAnchor, asOf, dailyNormalKwhRate, history.AlreadyBilledKwhWithinWindow);
            var tipo = history.AlreadyBilledKwhWithinWindow > 0m
                ? TipoRetroacao.FiscalizacaoFixExclusao11
                : TipoRetroacao.FiscalizacaoNaive10;
            return new RetroactiveOutcome(true, tipo, retro);
        }

        var levantamentoAnchor = history.MostRecentValidatedLevantamentoCompetenciaStart
            ?? throw new InvalidOperationException(
                "SeenInMostRecentValidatedLevantamento is false but MostRecentValidatedLevantamentoCompetenciaStart is null.");
        var naiveRetro = ComputeRetroKwh(levantamentoAnchor, asOf, dailyNormalKwhRate, history.AlreadyBilledKwhWithinWindow);
        var naiveTipo = history.AlreadyBilledKwhWithinWindow > 0m
            ? TipoRetroacao.FixExclusao01
            : TipoRetroacao.Naive00;
        return new RetroactiveOutcome(true, naiveTipo, naiveRetro);
    }

    /// <summary>
    /// Raw retroactive kWh for the window <c>[max(windowStart, asOf - 730 days), asOf]</c>,
    /// minus <paramref name="excludeKwh"/> (the [FIX] for codes 01/11 — see
    /// <see cref="TipoRetroacao"/>), floored at zero, rounded per <see cref="IpEnergyMath.Round"/>.
    /// </summary>
    private static decimal ComputeRetroKwh(
        DateOnly windowStart, DateOnly asOf, decimal dailyNormalKwhRate, decimal excludeKwh)
    {
        var cappedStart = CapWindowStart(windowStart, asOf);
        var days = Math.Max(0, asOf.DayNumber - cappedStart.DayNumber);
        var raw = dailyNormalKwhRate * days;
        return IpEnergyMath.Round(Math.Max(0m, raw - excludeKwh));
    }

    /// <summary>
    /// Moves <paramref name="windowStart"/> forward to the 730-day cap boundary when it's
    /// further back than that from <paramref name="asOf"/> — exactly <see cref="IpEnergyMath.RetroactiveCapDays"/>
    /// days back is still within the cap (see <see cref="IpEnergyMath.IsWithinRetroactiveCap"/>'s
    /// own boundary semantics, mirrored here).
    /// </summary>
    private static DateOnly CapWindowStart(DateOnly windowStart, DateOnly asOf)
    {
        var earliestAllowed = asOf.AddDays(-IpEnergyMath.RetroactiveCapDays);
        return windowStart < earliestAllowed ? earliestAllowed : windowStart;
    }
}

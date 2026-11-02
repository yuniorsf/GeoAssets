namespace GeoAssets.IpBilling.Levantamento;

/// <summary>
/// Everything <see cref="LevantamentoCalculationService"/> knows about one transformer's prior
/// billing history, immediately before deciding its <see cref="TipoRetroacao"/> — a pure-data
/// input so the decision itself (<see cref="LevantamentoRetroactiveCalculator.DetermineOutcome"/>)
/// stays a deterministic, unit-testable function with no I/O.
/// </summary>
public sealed record TransformerHistoryContext(
    bool MunicipioHasAnyValidatedLevantamento,
    bool SeenInMostRecentValidatedLevantamento,
    bool EverSeenInAnyValidatedLevantamento,
    DateOnly? MostRecentValidatedLevantamentoCompetenciaStart,
    DateOnly? FirstValidatedLevantamentoCompetenciaStart,
    /// <summary>Non-null when a validated Fiscalização/Ofício exists for this transformer —
    /// shifts the retroactive window's anchor there (codes 10/11) instead of the most recent
    /// validated Levantamento (codes 00/01). Independent of <see cref="AlreadyBilledKwhWithinWindow"/>:
    /// which anchor is chosen and how much (if anything) to exclude from it are two separate
    /// questions — see <see cref="TipoRetroacao"/>'s reconstruction notice.</summary>
    DateOnly? FiscalizacaoAnchorDate,
    /// <summary>kWh already billed by a validated Fiscalização/Ofício within whichever window
    /// ends up anchored — the [FIX] exclusion amount for codes 01/11. Zero/absent means the
    /// naive codes 00/10 apply instead.</summary>
    decimal AlreadyBilledKwhWithinWindow = 0m);

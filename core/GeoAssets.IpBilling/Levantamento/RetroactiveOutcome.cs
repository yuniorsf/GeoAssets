namespace GeoAssets.IpBilling.Levantamento;

/// <summary>Result of <see cref="LevantamentoRetroactiveCalculator.DetermineOutcome"/>.</summary>
public sealed record RetroactiveOutcome(
    bool IsNewTransformer,
    TipoRetroacao? Tipo,
    decimal? RetroativoKwh);

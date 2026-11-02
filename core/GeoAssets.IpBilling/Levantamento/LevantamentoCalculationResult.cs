namespace GeoAssets.IpBilling.Levantamento;

public sealed record LevantamentoCalculationResult(
    string ServiceOrderId,
    string Status,
    Guid MunicipioId,
    string Competencia,
    decimal NormalKwh,
    decimal RetroativoKwh,
    decimal Irregular24hKwh,
    decimal TotalCalculatedKwh,
    IReadOnlyList<LevantamentoTransformerLine> TransformerLines,
    IReadOnlyList<Levantamento24hLine> Lines24h,
    IReadOnlyList<string> UnresolvedCodigosInstalacao);

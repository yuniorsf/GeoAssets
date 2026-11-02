namespace GeoAssets.IpBilling.Levantamento;

public sealed record LevantamentoValidationResult(
    string ServiceOrderId,
    string Status,
    Models.Faturamento Faturamento);

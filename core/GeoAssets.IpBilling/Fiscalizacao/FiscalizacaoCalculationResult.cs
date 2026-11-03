namespace GeoAssets.IpBilling.Fiscalizacao;

public sealed record FiscalizacaoCalculationResult(
    string ServiceOrderId,
    string Status,
    Guid MunicipioId,
    string Competencia,
    int TotalAgregado,
    int TotalRetroativo,
    IReadOnlyList<FiscalizacaoLine> Lines);

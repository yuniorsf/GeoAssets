namespace GeoAssets.IpBilling.Oficio;

public sealed record OficioCalculationResult(
    string ServiceOrderId,
    string Status,
    Guid MunicipioId,
    string Competencia,
    string? NumeroPrefeitura,
    int TotalAgregado,
    int TotalRetroativo,
    IReadOnlyList<OficioLine> Lines,
    IReadOnlyList<OficioLineSwapResult> Swaps);

namespace GeoAssets.IpBilling.Calculation;

public sealed record IpBillingCalculationResult(
    string OrderId,
    string Status,
    Guid MunicipioId,
    string Competencia,
    decimal NormalKwh,
    decimal Irregular24hKwh,
    decimal TotalCalculatedKwh,
    int ResolvedPoleCount,
    IReadOnlyList<Guid> ResolvedPoleIds);

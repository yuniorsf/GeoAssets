namespace GeoAssets.IpBilling.Calculation;

/// <summary>
/// Orchestrates the <c>GEOS_FAT_IP</c> two-step workflow:
/// <list type="number">
///   <item><see cref="CalculateOrderAsync"/> (<c>Carregado -> Calculado</c>): queries the Core Server
///   (<c>IGeoCoreClient</c> / <c>IPoleResolver</c>) for topological pole neighbors and standing 24h
///   irregular lamp groups, applies <see cref="IpEnergyMath"/>, and saves the calculation breakdown.</item>
///   <item><see cref="ValidateOrderAsync"/> (<c>Calculado -> Validado</c>): atomically transitions the
///   order to <c>Validado</c> and posts the calculated kWh to the <see cref="Faturamento"/> ledger
///   using idempotent <c>SET</c> (Levantamento) or <c>ACCUMULATE</c> (Fiscalização/Ofício) semantics.</item>
/// </list>
/// </summary>
public interface IIpBillingCalculationService
{
    Task<IpBillingCalculationResult> CalculateOrderAsync(
        string orderId,
        IpBillingCalculationRequest? request = null,
        CancellationToken ct = default);

    Task<IpBillingValidationResult> ValidateOrderAsync(
        string orderId,
        string? performedBy = null,
        CancellationToken ct = default);
}

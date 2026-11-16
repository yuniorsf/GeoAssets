using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Oficio;

/// <summary>
/// Outcome of validating an Ofício: the point-count totals plus the kWh delta ACCUMULATEd onto
/// the Faturamento ledger (XD01-301).
/// </summary>
/// <param name="ServiceOrderId">The validated order.</param>
/// <param name="Status">Always <c>Validado</c>.</param>
/// <param name="TotalAgregado">Signed point-count total.</param>
/// <param name="TotalRetroativo">Signed retroactive point-count total.</param>
/// <param name="Faturamento">The ledger row after this order's posting.</param>
/// <param name="DeltaKwh">Signed kWh this order posted (monthly + retroactive).</param>
/// <param name="IsProvisional"><c>true</c> when the posting used the chart's mean watts-per-point
/// for at least one point and is pending recalculation.</param>
/// <param name="ExactPoints">Unsigned count of points converted from lamp detail.</param>
/// <param name="ProvisionalPoints">Unsigned count of points without lamp detail.</param>
public sealed record OficioValidationResult(
    string ServiceOrderId,
    string Status,
    int TotalAgregado,
    int TotalRetroativo,
    Faturamento? Faturamento = null,
    decimal DeltaKwh = 0m,
    bool IsProvisional = false,
    int ExactPoints = 0,
    int ProvisionalPoints = 0);

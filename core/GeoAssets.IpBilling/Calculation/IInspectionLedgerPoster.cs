namespace GeoAssets.IpBilling.Calculation;

/// <summary>
/// Converts a validated Fiscalização/Ofício's points to kWh and ACCUMULATEs the result onto the
/// Faturamento ledger — the one step both flows' <c>ValidateAsync</c> share (XD01-301).
/// </summary>
public interface IInspectionLedgerPoster
{
    /// <summary>
    /// Posts <paramref name="request"/>'s kWh delta and commits the ledger write before returning,
    /// so the caller can transition its order afterwards (XD01-188's crash-safe ordering).
    /// Idempotent per <c>(ServiceOrderId, Competencia)</c>: when the order already posted, nothing
    /// is converted or written and the stored figures are returned.
    /// </summary>
    /// <exception cref="InspectionLampNotInChartException">A lamp detail is in neither the
    /// Distribuidora chart nor the standard chart.</exception>
    /// <exception cref="InspectionStandardWattageUnavailableException">Points without lamp detail
    /// exist but no chart has any row.</exception>
    Task<InspectionLedgerPosting> PostAsync(InspectionLedgerPostingRequest request, CancellationToken ct = default);

    /// <summary>
    /// Read-only: the posting <paramref name="request"/>'s order already made for its competência
    /// (<see cref="InspectionLedgerPosting.WasAlreadyPosted"/> is <c>true</c>), or <c>null</c>
    /// when it never posted. Never converts and never writes — what a caller uses for an order
    /// that is already <c>Validado</c>, which must not start posting retroactively (an order
    /// validated before XD01-301 has no posting, and its competência may long be closed).
    /// </summary>
    Task<InspectionLedgerPosting?> FindExistingAsync(InspectionLedgerPostingRequest request, CancellationToken ct = default);
}

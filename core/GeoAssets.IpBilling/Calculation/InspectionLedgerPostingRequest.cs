using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Calculation;

/// <summary>Input to <see cref="IInspectionLedgerPoster.PostAsync"/> (XD01-301).</summary>
/// <param name="ServiceOrderId">The Fiscalização/Ofício order being validated — the idempotency key.</param>
/// <param name="Municipio">The order's município; its <see cref="Municipio.DistribuidoraId"/>
/// selects the lamp wattage chart.</param>
/// <param name="Competencia">Billing period, "yyyy-MM".</param>
/// <param name="Entries">The order's points, already normalized by the flow's own entry mapper.</param>
/// <param name="PostedAt">Timestamp recorded on the posting.</param>
public sealed record InspectionLedgerPostingRequest(
    string ServiceOrderId,
    Municipio Municipio,
    string Competencia,
    IReadOnlyList<InspectionPointEntry> Entries,
    DateTimeOffset PostedAt);

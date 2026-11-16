using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Calculation;

/// <summary>Outcome of <see cref="IInspectionLedgerPoster.PostAsync"/> (XD01-301).</summary>
/// <param name="Faturamento">The ledger row after the posting.</param>
/// <param name="DeltaKwh">The signed kWh delta this order posted. On a retry
/// (<paramref name="WasAlreadyPosted"/>), the figure stored by the original posting — not a recomputation.</param>
/// <param name="IsProvisional">Whether the posting is marked provisional. On a retry, the stored marker.</param>
/// <param name="ExactPoints">Unsigned count of points carrying lamp detail.</param>
/// <param name="ProvisionalPoints">Unsigned count of points without lamp detail.</param>
/// <param name="WasAlreadyPosted"><c>true</c> when this order had already posted for the
/// competência and nothing new was written.</param>
public sealed record InspectionLedgerPosting(
    Faturamento Faturamento,
    decimal DeltaKwh,
    bool IsProvisional,
    int ExactPoints,
    int ProvisionalPoints,
    bool WasAlreadyPosted);

namespace GeoAssets.Server.Targets;

/// <summary>
/// Body of <c>POST /api/targets/ledger/{vertical}/{orderId}/release</c> and <c>…/commit</c> (XD01-379).
/// </summary>
/// <param name="OrganizationId">Required, not the empty GUID: the organization the calling vertical acts for.</param>
/// <param name="TargetIds">1 to 500 target ids of the order; absent means every open target of the order.</param>
/// <param name="Actor">Who the vertical acts for, recorded on the integration event; the caller's identity when absent.</param>
public sealed record TargetLedgerChangeRequest(Guid? OrganizationId, IReadOnlyList<string>? TargetIds = null, string? Actor = null);

namespace GeoAssets.Server.Targets;

/// <summary>
/// Body of <c>POST /api/targets/ledger/{vertical}/{orderId}/reserve</c> (XD01-379). The vertical and the order come from
/// the route; <paramref name="OrganizationId"/> is the organization the calling vertical acts for — never read from a
/// header. <paramref name="Preview"/> reports what the reservation would add and writes nothing.
/// </summary>
/// <param name="OrganizationId">Required, not the empty GUID.</param>
/// <param name="OrderTypeId">Required: an order type the vertical published, with a target policy.</param>
/// <param name="StrategyId">Required: the selection strategy.</param>
/// <param name="Parameters">The strategy's parameters.</param>
/// <param name="Preview">True for preview semantics; false (the default) reserves.</param>
/// <param name="AcceptTruncation">Reserve what fits under the selection limit instead of refusing the selection.</param>
/// <param name="Page">The preview's 1-based page of targets.</param>
/// <param name="PageSize">The preview's page size, 1 to 200.</param>
/// <param name="Actor">Who the vertical acts for, recorded as each target's <c>addedBy</c>; the caller's identity when absent.</param>
public sealed record TargetLedgerReserveRequest(
    Guid? OrganizationId,
    string? OrderTypeId,
    string? StrategyId,
    Dictionary<string, object>? Parameters,
    bool Preview = false,
    bool AcceptTruncation = false,
    int Page = 1,
    int PageSize = 50,
    string? Actor = null);

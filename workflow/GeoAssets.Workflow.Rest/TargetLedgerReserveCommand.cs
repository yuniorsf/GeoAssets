using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Rest;

/// <summary>
/// A preview or reservation request to the kernel's target ledger (XD01-382): the vertical's order
/// (<paramref name="Key"/>), the organization it acts for, the order type it published (with its target policy), and the
/// selection itself. <paramref name="Actor"/> is who the vertical acts for, recorded as each target's <c>addedBy</c>.
/// </summary>
public sealed record TargetLedgerReserveCommand(
    TargetLedgerKey Key,
    Guid OrganizationId,
    string OrderTypeId,
    string StrategyId,
    IReadOnlyDictionary<string, object> Parameters,
    bool AcceptTruncation = false,
    int Page = 1,
    int PageSize = 50,
    string? Actor = null);

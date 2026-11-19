using GeoAssets.Core.Models;

namespace GeoAssets.Workflow.Targets;

/// <summary>
/// Read-only query surface over the <see cref="ServiceOrderTarget"/>s of service orders.
/// Segregated from <see cref="IServiceOrderTargetWriter"/> for the same reason
/// <see cref="Orders.IServiceOrderReader"/> is segregated from its writer.
///
/// The member documentation below is the contract every adapter must honour identically
/// (a database store, a REST client, an in-memory fake). Rules common to all members:
/// a null argument throws <see cref="ArgumentNullException"/>; a blank order id throws
/// <see cref="ArgumentException"/>; an order that does not exist, or has no targets, is not an
/// error — it reads as empty; a cancelled <see cref="CancellationToken"/> surfaces as
/// <see cref="OperationCanceledException"/>.
///
/// Tenancy: a member addressed to one order returns that order's targets and leaves deciding
/// whether the caller may see the order to the caller — as
/// <see cref="Orders.IServiceOrderReader.GetByIdAsync"/> does, so that an organization granted
/// access to another organization's order reaches its targets too. The one search across
/// orders, <see cref="FindOpenUsagesAsync"/>, never leaves a single organization (see there).
/// </summary>
public interface IServiceOrderTargetReader
{
    /// <summary>
    /// Returns one page of the targets of <see cref="OrderTargetQuery.OrderId"/>.
    /// <list type="bullet">
    /// <item><description>
    /// Filters: <see cref="OrderTargetQuery.Status"/> (exact, case-sensitive) and
    /// <see cref="OrderTargetQuery.ConflictLevel"/>, each applied only when non-null.
    /// <see cref="PagedResult{T}.TotalCount"/> counts every target matching the filters, not
    /// just the returned page.
    /// </description></item>
    /// <item><description>
    /// Order: by the key <see cref="OrderTargetSortKeys.Resolve"/> gives for
    /// <see cref="OrderTargetQuery.SortBy"/>, reversed when
    /// <see cref="OrderTargetQuery.Descending"/> is set, then always by
    /// <see cref="ServiceOrderTarget.Id"/> ascending (ordinal), so the order is total and pages
    /// never overlap. Conflict level sorts None, Warning, Blocked. A null
    /// <see cref="ServiceOrderTarget.Alias"/> sorts after every non-null alias when ascending
    /// and before them when descending.
    /// </description></item>
    /// <item><description>
    /// Paging: skips <see cref="OrderTargetQuery.Skip"/> items and returns at most
    /// <see cref="OrderTargetQuery.PageSize"/>. A page past the end, or an order with no
    /// targets, returns empty <see cref="PagedResult{T}.Items"/> (with the true
    /// <see cref="PagedResult{T}.TotalCount"/>, 0 for an order with no targets) — never null,
    /// never an exception. Out-of-range paging cannot reach an adapter:
    /// <see cref="OrderTargetQuery"/> rejects it with <see cref="ArgumentOutOfRangeException"/>.
    /// </description></item>
    /// </list>
    /// </summary>
    Task<PagedResult<ServiceOrderTarget>> GetPageAsync(OrderTargetQuery query, CancellationToken ct);

    /// <summary>
    /// Returns the target <paramref name="targetId"/> of <paramref name="orderId"/>, or null when
    /// that order holds no target with that id — including an unknown order, and a target id that
    /// exists but belongs to another order. A blank <paramref name="targetId"/> throws
    /// <see cref="ArgumentException"/>, like a blank order id. Ids compare ordinally.
    /// </summary>
    Task<ServiceOrderTarget?> GetByIdAsync(string orderId, string targetId, CancellationToken ct);

    /// <summary>
    /// Returns how many targets <paramref name="orderId"/> has, in any status and whether or
    /// not they are released; 0 for an order with no targets or an unknown order.
    /// </summary>
    Task<int> CountAsync(string orderId, CancellationToken ct);

    /// <summary>
    /// Returns which of <paramref name="featureIds"/> are currently held by open orders other
    /// than <paramref name="excludingOrderId"/>: one <see cref="OrderTargetUsage"/> per
    /// (feature, holding order) pair, in no guaranteed order.
    /// <list type="bullet">
    /// <item><description>
    /// Targets of <paramref name="excludingOrderId"/> itself are never returned. Every member of this reader addresses
    /// in-process orders (<see cref="TargetLedger.KernelVerticalId"/>); a remote vertical's order that shares the id
    /// is a different order, so its targets ARE returned here, as are any other remote vertical's open targets
    /// (XD01-379: exclusivity holds across verticals), each with its <see cref="OrderTargetUsage.VerticalId"/>.
    /// </description></item>
    /// <item><description>
    /// Released targets are never returned. A target is released once its order has reached a
    /// terminal state (<see cref="Orders.ServiceOrderTransitions.IsTerminalState"/>); the
    /// per-target <see cref="ServiceOrderTarget.Status"/> plays no part.
    /// </description></item>
    /// <item><description>
    /// Same-family orders (parent, children, siblings) ARE returned; ignoring them is
    /// <see cref="TargetConflictEvaluator"/>'s job.
    /// </description></item>
    /// <item><description>
    /// Only orders of one organization are searched: the organization that owns
    /// <paramref name="excludingOrderId"/> (the order the caller is working on, and has been
    /// authorized for), or the caller's own organization when that order does not exist yet.
    /// </description></item>
    /// <item><description>
    /// Empty <paramref name="featureIds"/> returns an empty list without querying the store.
    /// Repeated feature ids count once. A feature nobody else holds simply has no entry.
    /// </description></item>
    /// <item><description>
    /// The result is bounded by the caller's own <paramref name="featureIds"/>, which is why
    /// it is a list and not a page; an adapter must cope with many thousands of ids (e.g. by
    /// querying in chunks).
    /// </description></item>
    /// </list>
    /// </summary>
    Task<IReadOnlyList<OrderTargetUsage>> FindOpenUsagesAsync(IReadOnlyCollection<string> featureIds, string excludingOrderId, CancellationToken ct);

    /// <summary>
    /// Returns the bounding box of <paramref name="orderId"/>'s positioned targets — those with
    /// both <see cref="ServiceOrderTarget.Longitude"/> and <see cref="ServiceOrderTarget.Latitude"/>
    /// set; the others are ignored. Returns null when the order has no positioned target
    /// (including an unknown order or one with no targets at all).
    /// </summary>
    Task<OrderTargetExtent?> GetExtentAsync(string orderId, CancellationToken ct);
}

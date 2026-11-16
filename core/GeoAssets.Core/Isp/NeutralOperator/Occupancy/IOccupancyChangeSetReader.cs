using GeoAssets.Core.Models;

namespace GeoAssets.Core.Isp.NeutralOperator.Occupancy;

/// <summary>
/// Read side of the store of staged occupancy changes (XD01-315).
///
/// <para><b>Adapter contract, common to every member of both ports.</b></para>
/// <list type="bullet">
///   <item><description><b>Owner only.</b> An adapter is scoped to one organization — the
///   caller's (<c>ITenantContext.CurrentOrganizationId</c>) — and only ever returns or changes
///   sets that organization owns as pole owner. Staged changes describe the pole owner's
///   occupancy, occupants' identities included: no other organization reads them through this
///   port, not a grantee on the order and not an occupant named in a change.</description></item>
///   <item><description><b>They follow their target.</b> A set exists only as long as the order
///   target it was staged on: removing the target, or deleting its order, removes its sets, in
///   every status.</description></item>
///   <item><description><b>A closed order holds no staged set.</b> When an order reaches a
///   terminal state, whatever is still staged on its targets is discarded (it reads as
///   <see cref="OccupancyChangeSetStatus.Discarded"/> with a closing instant). Committed and
///   already discarded sets are left as they are.</description></item>
///   <item><description><b>Ordinal text.</b> Order, target and pole ids are compared and
///   ordered ordinally (case-sensitive, byte-wise), on every adapter.</description></item>
///   <item><description><b>Cancellation.</b> A token already cancelled on entry throws
///   <see cref="OperationCanceledException"/> before anything is read or written. Argument
///   checks come first.</description></item>
/// </list>
/// </summary>
public interface IOccupancyChangeSetReader
{
    /// <summary>
    /// The set currently staged on one target of one order, or null when there is none — also
    /// when the order or target does not exist, the target belongs to another order, or the set
    /// belongs to another organization. A target has at most one staged set.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="orderId"/> or <paramref name="targetId"/> is null or blank.</exception>
    Task<OccupancyChangeSet?> GetStagedAsync(string orderId, string targetId, CancellationToken ct);

    /// <summary>
    /// One page of an order's change sets, optionally only those in one status, ordered by pole
    /// feature id and then by the canonical text of the set's id (<c>Guid.ToString("D")</c>),
    /// both ordinally — a total order. <see cref="PagedResult{T}.TotalCount"/> counts every
    /// match; a page past the end has no items and the same total; an unknown order has none.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is null.</exception>
    Task<PagedResult<OccupancyChangeSet>> GetPageAsync(OccupancyChangeSetQuery query, CancellationToken ct);
}

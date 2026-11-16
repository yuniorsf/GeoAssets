using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Workflow.Persistence;

/// <summary>
/// Releases an order's targets when the order closes (XD01-306): every status-changing write in
/// <see cref="EFServiceOrderRepository"/> calls <see cref="StampIfClosingAsync"/> before its own
/// <c>SaveChanges</c>, so the order's new status and its targets' release stamp are one
/// database write — an order can never be seen closed while still holding its features, nor
/// the other way round.
/// </summary>
internal static class ServiceOrderTargetRelease
{
    /// <summary>
    /// When moving from <paramref name="previousStatus"/> to <paramref name="nextStatus"/> takes
    /// the order into a terminal state (<see cref="ServiceOrderTransitions.IsTerminalState"/>),
    /// marks its not-yet-released targets as released at <paramref name="timeProvider"/>'s
    /// current instant — tracked changes only, written by the caller's <c>SaveChanges</c>.
    /// Any other move, including a write that keeps the status, touches nothing. Returns the
    /// targets it stamped, for <see cref="Discard"/>.
    /// </summary>
    public static async Task<IReadOnlyList<ServiceOrderTargetRecord>> StampIfClosingAsync(
        ServiceOrderDbContext db,
        string orderId,
        OrderType? orderType,
        string previousStatus,
        string nextStatus,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (string.Equals(previousStatus, nextStatus, StringComparison.Ordinal)
            || !ServiceOrderTransitions.IsTerminalState(orderType, nextStatus))
        {
            return [];
        }

        // Unfiltered for the same reason the order itself is fetched unfiltered: whoever is
        // allowed to close the order (possibly a cross-org grantee) releases all of its targets.
        var open = await db.ServiceOrderTargets.IgnoreQueryFilters()
            .Where(t => t.ServiceOrderId == orderId && t.ReleasedAt == null)
            .ToListAsync(ct).ConfigureAwait(false);

        var releasedAt = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var target in open)
            target.ReleasedAt = releasedAt;

        return open;
    }

    /// <summary>
    /// Undoes <see cref="StampIfClosingAsync"/> in the change tracker after the caller's
    /// <c>SaveChanges</c> failed, so a later save on the same context cannot release the targets
    /// of an order that never closed.
    /// </summary>
    public static void Discard(ServiceOrderDbContext db, IReadOnlyList<ServiceOrderTargetRecord> stamped)
    {
        foreach (var target in stamped)
        {
            var releasedAt = db.Entry(target).Property(t => t.ReleasedAt);
            releasedAt.CurrentValue = releasedAt.OriginalValue;
            releasedAt.IsModified = false;
        }
    }
}

using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Entry point for moving a <see cref="ServiceOrder"/> through
/// <see cref="FtthDropInstallWorkflowGraph"/>: additionally guards the
/// <see cref="FtthDropInstallStatus.TagAffixed"/>→<see cref="FtthDropInstallStatus.PtoPower"/>
/// edge on a passing CTO OPM (optical power meter) reading already recorded on the order's
/// <see cref="IServiceOrder.Attributes"/>, then delegates to
/// <see cref="ServiceOrder.Transition(string, TimeProvider, OrderType?)"/> for the underlying
/// structural-legality check and mutation. Computing *whether* a raw OPM reading passes the
/// optical budget is XD01-274's scope (a separate verification service this ticket is declared
/// to depend on only loosely — it blocks XD01-274, not the other way around); this guard only
/// consults the already-recorded pass/fail outcome, same "guard reads a precondition, doesn't
/// compute it" shape as <see cref="SafetyChecklistTransitionGuard"/>.
///
/// Synchronous and dependency-free (unlike <see cref="FieldWorkOrderTransitioner"/>'s
/// <see cref="IFieldWorkTransitionGuard"/> abstraction) because the guard here is a pure
/// in-memory attribute check with no I/O — introduce an async guard seam only if a future
/// ticket actually needs one.
/// </summary>
public static class FtthDropInstallTransitioner
{
    /// <summary>
    /// <see cref="IServiceOrder.Attributes"/> key holding the already-recorded CTO OPM reading
    /// outcome (<c>"true"</c>/<c>"false"</c>) gating entry into <see cref="FtthDropInstallStatus.PtoPower"/>.
    /// </summary>
    public const string OpmReadingPassedAttributeKey = "opmReadingPassed";

    /// <summary>
    /// Throws <see cref="InvalidServiceOrderTransitionException"/> both for a structurally
    /// illegal edge and for the guarded edge when no passing OPM reading is recorded.
    /// </summary>
    public static ServiceOrder Transition(ServiceOrder order, string newStatus, TimeProvider timeProvider, OrderType ftthOrderType)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(ftthOrderType);

        if (IsEnteringPtoPower(order.Status, newStatus) && !HasPassingOpmReading(order))
            throw new InvalidServiceOrderTransitionException(order.Status, newStatus);

        return order.Transition(newStatus, timeProvider, ftthOrderType);
    }

    private static bool IsEnteringPtoPower(string from, string to) =>
        string.Equals(from, FtthDropInstallStatus.TagAffixed, StringComparison.Ordinal)
        && string.Equals(to, FtthDropInstallStatus.PtoPower, StringComparison.Ordinal);

    private static bool HasPassingOpmReading(ServiceOrder order) =>
        order.Attributes.TryGetValue(OpmReadingPassedAttributeKey, out var raw)
        && bool.TryParse(raw, out var passed)
        && passed;
}

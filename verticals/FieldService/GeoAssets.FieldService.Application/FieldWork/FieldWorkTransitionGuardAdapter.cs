using GeoAssets.Workflow.Orders;

namespace GeoAssets.FieldService.FieldWork;

/// <summary>
/// An <see cref="IFieldWorkTransitionGuard"/> as a generic <see cref="ITransitionGuard"/> (XD01-362): the
/// <see cref="FieldWorkStatus.OnSite"/> → <see cref="FieldWorkStatus.InProgress"/> edge asks
/// <see cref="IFieldWorkTransitionGuard.CanStartWorkAsync"/>, <see cref="FieldWorkStatus.InProgress"/> →
/// <see cref="FieldWorkStatus.Completed"/> asks <see cref="IFieldWorkTransitionGuard.CanCompleteAsync"/>; every other move is
/// allowed. The arrival edge stays with <see cref="FieldWorkOrderTransitioner"/>, which alone has the device's position.
///
/// Not registered by any host yet: registering it makes the repository enforce these two checks on every path that changes
/// a field-work order's status — including the generic ones that today bypass them — which is a behaviour change to decide
/// separately.
/// </summary>
public sealed class FieldWorkTransitionGuardAdapter(IFieldWorkTransitionGuard guard) : ITransitionGuard
{
    public const string StartWorkDeniedCode = "fieldwork.start-work-denied";

    public const string CompletionDeniedCode = "fieldwork.completion-denied";

    public async Task<GuardVerdict> EvaluateAsync(OrderTransitionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (IsEdge(context, FieldWorkStatus.OnSite, FieldWorkStatus.InProgress))
        {
            return await guard.CanStartWorkAsync(context.Order, cancellationToken).ConfigureAwait(false)
                ? GuardVerdict.Allow
                : GuardVerdict.Deny(StartWorkDeniedCode, "Work cannot start: the start-of-work preconditions are not met.");
        }

        if (IsEdge(context, FieldWorkStatus.InProgress, FieldWorkStatus.Completed))
        {
            return await guard.CanCompleteAsync(context.Order, cancellationToken).ConfigureAwait(false)
                ? GuardVerdict.Allow
                : GuardVerdict.Deny(CompletionDeniedCode, "The order cannot be completed: the completion preconditions are not met.");
        }

        return GuardVerdict.Allow;
    }

    private static bool IsEdge(OrderTransitionContext context, string from, string to) =>
        string.Equals(context.FromStatus, from, StringComparison.Ordinal)
        && string.Equals(context.ToStatus, to, StringComparison.Ordinal);
}

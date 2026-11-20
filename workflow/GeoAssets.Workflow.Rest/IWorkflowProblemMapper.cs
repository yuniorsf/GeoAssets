using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rest;

/// <summary>
/// A vertical's translation of its own problem codes into the exceptions its in-process services throw (XD01-388): the
/// chassis' REST clients consult every registered mapper before the generic codes of <see cref="WorkflowProblemTranslator"/>,
/// so the chassis names no vertical's code. Register one with <c>TryAddEnumerable</c>; the clients resolve them when first
/// built, so the order of the registrations does not matter.
/// </summary>
public interface IWorkflowProblemMapper
{
    /// <summary>The exception for a problem this vertical owns; null for any other code.</summary>
    /// <param name="problem">The problem body the host answered with.</param>
    /// <param name="orderId">The order the call was about, for exceptions that carry one.</param>
    /// <param name="action">The action the call needed.</param>
    Exception? ToException(WorkflowProblem problem, string? orderId, OrderAction action);

    /// <summary>
    /// The exception a service-order write answered <c>409 Conflict</c> with <paramref name="problem"/> throws instead of
    /// <see cref="ServiceOrderConcurrencyException"/>; null — the default — keeps the concurrency exception. Separate from
    /// <see cref="ToException"/> because a write's conflict is a changed order unless the vertical says otherwise.
    /// </summary>
    /// <param name="problem">The problem body of the conflict.</param>
    /// <param name="orderId">The order that was written.</param>
    Exception? ToWriteConflictException(WorkflowProblem problem, string orderId) => null;
}

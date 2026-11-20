using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rest;

namespace GeoAssets.NeutralOperator.Rest;

/// <summary>
/// The NeutralOperator vertical's problem codes (<see cref="OccupancyProblemCodes"/>, <see cref="SharedUseProblemCodes"/>)
/// back into the exceptions its in-process services throw (XD01-388, out of the chassis' <see cref="WorkflowProblemTranslator"/>).
/// Registered by <see cref="NeutralOperatorRestServiceExtensions"/> for the chassis' clients, and always used by this vertical's
/// own clients.
/// </summary>
public sealed class NeutralOperatorProblemMapper : IWorkflowProblemMapper
{
    /// <inheritdoc/>
    public Exception? ToException(WorkflowProblem problem, string? orderId, OrderAction action)
    {
        ArgumentNullException.ThrowIfNull(problem);

        return problem.Code switch
        {
            // Staged occupancy (XD01-315).
            OccupancyProblemCodes.InvalidChange => new InvalidOccupancyChangeException(problem.Detail),
            OccupancyProblemCodes.EntryNotFound => new OccupancyEntryNotFoundException(problem.Detail),
            OccupancyProblemCodes.NotAPole => new TargetIsNotAPoleException(problem.Detail),
            OccupancyProblemCodes.TargetReleased => new OccupancyTargetReleasedException(problem.Detail),
            // Approval that commits staged occupancy (XD01-318).
            OccupancyProblemCodes.ConcurrentModification => new OccupancyConflictException(problem.GetStrings("poleFeatureIds")),
            // Approval saga (XD01-383).
            OccupancyProblemCodes.ApplyPending => new OccupancyApplyPendingException(orderId ?? string.Empty, problem.GetStrings("poleFeatureIds")),
            OccupancyProblemCodes.ApplyNotPending => new OccupancyApplyNotPendingException(orderId ?? string.Empty, SagaStateOf(problem)),
            // Shared-use dispatch (XD01-316).
            SharedUseProblemCodes.NoFanOut => new OrderTypeHasNoFanOutException(
                problem.GetString("orderTypeId") ?? string.Empty, problem.Detail),
            _ when SharedUseProblemCodes.ReasonOf(problem.Code) is { } reason => Blocked(problem, orderId, reason),
            _ => null,
        };
    }

    /// <summary>
    /// An approval refused because a pole's occupancy changed after its changes were staged (XD01-318) is an
    /// <see cref="OccupancyConflictException"/>; every other conflict stays the order's concurrency exception.
    /// </summary>
    public Exception? ToWriteConflictException(WorkflowProblem problem, string orderId)
    {
        ArgumentNullException.ThrowIfNull(problem);

        return string.Equals(problem.Code, OccupancyProblemCodes.ConcurrentModification, StringComparison.Ordinal)
            ? ToException(problem, orderId, OrderAction.Approve)
            : null;
    }

    /// <summary>The saga state a not-pending retry names; null when the order has no saga.</summary>
    private static OccupancyApplySagaState? SagaStateOf(WorkflowProblem problem) =>
        Enum.TryParse<OccupancyApplySagaState>(problem.GetString("state"), ignoreCase: false, out var state) && Enum.IsDefined(state)
            ? state
            : null;

    /// <summary>A blocked dispatch comes back with its reason and the offending targets the host listed.</summary>
    private static SharedUseDispatchBlockedException Blocked(WorkflowProblem problem, string? orderId, SharedUseDispatchBlockReason reason)
    {
        var targetIds = problem.GetStrings("targetIds");
        return new SharedUseDispatchBlockedException(
            orderId ?? string.Empty, reason, targetIds, targetIds.Count > 0 ? problem.GetInt32("targetCount") : 0);
    }
}

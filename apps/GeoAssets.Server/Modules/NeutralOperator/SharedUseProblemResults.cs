using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Server.Modules.NeutralOperator;

/// <summary>
/// The RFC 9457 answer for each exception the shared-use dispatch service throws (XD01-316),
/// in the shape and with the base codes of <see cref="WorkflowProblemResults"/>.
/// </summary>
internal static class SharedUseProblemResults
{
    /// <summary>The problem for <paramref name="exception"/>, or null when it is not one of the service's own.</summary>
    public static IResult? FromException(Exception exception, string orderId) => exception switch
    {
        // A caller the organization gate refused gets the unknown-order answer (XD01-332).
        OrderActionDeniedException { Evaluation: { } verdict } => WorkflowProblemResults.Refused(orderId, verdict),
        OrderActionDeniedException denied => WorkflowProblemResults.Denied(denied.Message),
        OrderTypeNotRegisteredException missingType => WorkflowProblemResults.Create(
            StatusCodes.Status404NotFound, WorkflowProblemCodes.OrderTypeNotFound,
            "Order type not found.", missingType.Message, ("orderTypeId", missingType.OrderTypeId)),
        KeyNotFoundException => WorkflowProblemResults.OrderNotFound(orderId),
        OrderTypeHasNoFanOutException noFanOut => WorkflowProblemResults.Create(
            StatusCodes.Status422UnprocessableEntity, SharedUseProblemCodes.NoFanOut,
            "Orders of this type are not dispatched to occupants.", noFanOut.Message, ("orderTypeId", noFanOut.OrderTypeId)),
        SharedUseDispatchBlockedException blocked => WorkflowProblemResults.Create(
            StatusCodes.Status422UnprocessableEntity, SharedUseProblemCodes.For(blocked.Reason),
            "The order cannot be dispatched as it stands.", blocked.Message,
            ("targetCount", blocked.TargetIds.Count > 0 ? blocked.TargetCount : null),
            ("targetIds", blocked.TargetIds.Count > 0 ? blocked.TargetIds : null)),
        InvalidServiceOrderTransitionException transition => WorkflowProblemResults.Create(
            StatusCodes.Status409Conflict, WorkflowProblemCodes.OrderInvalidState,
            "The order's state does not allow this change.", transition.Message,
            ("from", transition.From), ("to", transition.To)),
        ServiceOrderConcurrencyException => WorkflowProblemResults.ConcurrentModification(orderId),
        ArgumentOutOfRangeException range => WorkflowProblemResults.PagingOutOfRange(range.Message),
        ArgumentException argument => WorkflowProblemResults.Validation(argument.Message),
        _ => null,
    };
}

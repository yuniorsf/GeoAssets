using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Server.Modules.NeutralOperator;

/// <summary>
/// Turns what <see cref="IOrderOccupancyStagingService"/> throws into the
/// <c>application/problem+json</c> bodies of the staged-occupancy routes (XD01-315), in the
/// shape and with the codes of <see cref="WorkflowProblemResults"/> plus
/// <see cref="OccupancyProblemCodes"/>.
/// </summary>
internal static class OccupancyProblemResults
{
    /// <summary>The problem for <paramref name="exception"/>, or null when it is not a client-facing failure.</summary>
    public static IResult? FromException(Exception exception, string orderId, string? targetId) => exception switch
    {
        // A caller the organization gate refused gets the unknown-order answer (XD01-332).
        OrderActionDeniedException { Evaluation: { } verdict } => WorkflowProblemResults.Refused(orderId, verdict),
        OrderActionDeniedException denied => WorkflowProblemResults.Denied(denied.Message),
        OrderTargetNotFoundException => WorkflowProblemResults.TargetNotFound(orderId, targetId ?? string.Empty),
        OccupancyEntryNotFoundException missing => WorkflowProblemResults.Create(
            StatusCodes.Status404NotFound, OccupancyProblemCodes.EntryNotFound,
            "The pole holds no such occupancy entry.", missing.Message,
            ("slotIndex", missing.SlotIndex), ("entryId", missing.EntryId)),
        KeyNotFoundException => WorkflowProblemResults.OrderNotFound(orderId),
        TargetIsNotAPoleException notAPole => WorkflowProblemResults.Create(
            StatusCodes.Status422UnprocessableEntity, OccupancyProblemCodes.NotAPole,
            "The target is not a pole.", notAPole.Message, ("assetTypeId", notAPole.AssetTypeId)),
        InvalidServiceOrderTransitionException transition => WorkflowProblemResults.Create(
            StatusCodes.Status409Conflict, WorkflowProblemCodes.OrderInvalidState,
            "The order's state does not allow this change.", transition.Message,
            ("from", transition.From), ("to", transition.To)),
        OccupancyTargetReleasedException released => WorkflowProblemResults.Create(
            StatusCodes.Status409Conflict, OccupancyProblemCodes.TargetReleased,
            "The target was released when its order closed.", released.Message),
        InvalidOccupancyChangeException invalid => WorkflowProblemResults.Create(
            StatusCodes.Status400BadRequest, OccupancyProblemCodes.InvalidChange,
            "The occupancy changes are invalid.", invalid.Message),
        ArgumentOutOfRangeException range => WorkflowProblemResults.PagingOutOfRange(range.Message),
        ArgumentException argument => WorkflowProblemResults.Validation(argument.Message),
        _ => null,
    };
}

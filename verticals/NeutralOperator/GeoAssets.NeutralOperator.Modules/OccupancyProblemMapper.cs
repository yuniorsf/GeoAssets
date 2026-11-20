using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Hosting.Abstractions;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.Hosting;

namespace GeoAssets.NeutralOperator.Modules;

/// <summary>
/// Turns what <see cref="IOrderOccupancyStagingService"/> throws into the
/// <c>application/problem+json</c> bodies of the staged-occupancy routes (XD01-315), in the
/// shape and with the codes of <see cref="WorkflowProblemResults"/> plus
/// <see cref="OccupancyProblemCodes"/>.
/// </summary>
public sealed class OccupancyProblemMapper : IProblemDetailsMapper
{
    /// <summary>The <see cref="ProblemContext.Scope"/> of this module's routes that this mapper answers for.</summary>
    public const string Scope = "occupancy";

    /// <inheritdoc/>
    public IResult? TryMap(Exception exception, ProblemContext context)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(context);

        return string.Equals(context.Scope, Scope, StringComparison.Ordinal)
            ? FromException(exception, context.OrderId ?? string.Empty, context.TargetId)
            : null;
    }

    /// <summary>The problem for <paramref name="exception"/> on one of this module's routes, or null when it is not one of its own.</summary>
    private static IResult? FromException(Exception exception, string orderId, string? targetId) => exception switch
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
        OccupancyApplyPendingException pending => WorkflowProblemResults.Create(
            StatusCodes.Status409Conflict, OccupancyProblemCodes.ApplyPending,
            "An earlier approval awaits the asset catalog's apply.", pending.Message,
            ("poleFeatureIds", pending.PoleFeatureIds)),
        OccupancyApplyNotPendingException notPending => WorkflowProblemResults.Create(
            StatusCodes.Status409Conflict, OccupancyProblemCodes.ApplyNotPending,
            "The order has no asset-catalog apply to retry.", notPending.Message,
            ("state", notPending.State?.ToString())),
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

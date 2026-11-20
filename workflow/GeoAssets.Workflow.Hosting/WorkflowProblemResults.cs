using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// Builds the <c>application/problem+json</c> bodies of the order-target and selection routes
/// (XD01-308). Every body carries <c>type</c>, <c>title</c>, <c>status</c> and a stable
/// <c>code</c> from <see cref="WorkflowProblemCodes"/> — the member a client switches on — plus
/// the extension members named on each factory below.
/// </summary>
public static class WorkflowProblemResults
{
    public const string ContentType = "application/problem+json";

    public static IResult Create(
        int status, string code, string title, string? detail = null, params (string Name, object? Value)[] extensions)
    {
        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = $"urn:geoassets:problem:{code}",
            ["title"] = title,
            ["status"] = status,
            ["code"] = code,
        };

        if (detail is not null) body["detail"] = detail;
        foreach (var (name, value) in extensions ?? [])
        {
            if (value is not null) body[name] = value;
        }

        return Results.Json(body, GeoJsonSerializer.Options, ContentType, status);
    }

    public static IResult Validation(string detail) =>
        Create(StatusCodes.Status400BadRequest, WorkflowProblemCodes.ValidationFailed, "The request is invalid.", detail);

    public static IResult PagingOutOfRange(string detail) =>
        Create(StatusCodes.Status400BadRequest, WorkflowProblemCodes.PagingOutOfRange, "Paging is out of range.", detail);

    public static IResult OrderNotFound(string orderId) =>
        Create(StatusCodes.Status404NotFound, WorkflowProblemCodes.OrderNotFound, "Service order not found.",
            null, ("orderId", orderId));

    public static IResult TargetNotFound(string orderId, string targetId) =>
        Create(StatusCodes.Status404NotFound, WorkflowProblemCodes.TargetNotFound, "Service order target not found.",
            null, ("orderId", orderId), ("targetId", targetId));

    /// <summary>403 with the rule engine's own reason under <c>reason</c>, the member the older order routes use.</summary>
    public static IResult Denied(string reason) =>
        Create(StatusCodes.Status403Forbidden, WorkflowProblemCodes.Denied, "The action is not allowed on this order.",
            null, ("reason", reason));

    /// <summary>
    /// The response for a verdict that did not allow the action (XD01-332): the same
    /// <c>orders.not-found</c> as an unknown order for a caller the organization gate refused —
    /// so a refusal never confirms another tenant's order id — and <see cref="Denied"/> for a
    /// caller inside the gate whom the rules declined.
    /// </summary>
    public static IResult Refused(string orderId, RuleEvaluationResult verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        return verdict.IsOutsideOrganization ? OrderNotFound(orderId) : Denied(verdict.Reason);
    }

    public static IResult InvalidSelectionParameters(string detail, string? defect = null) =>
        Create(StatusCodes.Status400BadRequest, WorkflowProblemCodes.InvalidSelectionParameters,
            "The selection parameters are invalid.", detail, ("defect", defect));

    public static IResult UnknownStrategy(string strategyId) =>
        Create(StatusCodes.Status400BadRequest, WorkflowProblemCodes.UnknownSelectionStrategy,
            "Unknown selection strategy.", null, ("strategyId", strategyId));

    public static IResult ConcurrentModification(string orderId) =>
        Create(StatusCodes.Status409Conflict, WorkflowProblemCodes.TargetConcurrentModification,
            "The target or its order was modified by someone else.", null, ("orderId", orderId));

    /// <summary>
    /// The problem for a failure whose exception type alone decides it, or null when the type
    /// needs context (<see cref="KeyNotFoundException"/>) or is not a client-facing failure.
    /// </summary>
    public static IResult? FromException(Exception exception, string orderId)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            OrderActionDeniedException { Evaluation: { } verdict } denied => Refused(denied.OrderId ?? orderId, verdict),
            OrderActionDeniedException denied => Denied(denied.Message),
            SelectionLimitExceededException limit => Create(
                StatusCodes.Status422UnprocessableEntity, WorkflowProblemCodes.TargetLimitExceeded,
                "The selection exceeds the order type's selection limit.", limit.Message,
                ("limit", limit.Limit), ("current", limit.Current), ("requested", limit.Requested)),
            SelectionTooLargeException tooLarge => Create(
                StatusCodes.Status422UnprocessableEntity, WorkflowProblemCodes.SelectionTooLarge,
                "The selection matches too many features.", tooLarge.Message,
                ("ceiling", tooLarge.Ceiling), ("matchedAtLeast", tooLarge.MatchedAtLeast)),
            ServiceOrderConcurrencyException => ConcurrentModification(orderId),
            InvalidServiceOrderTransitionException transition => Create(
                StatusCodes.Status409Conflict, WorkflowProblemCodes.OrderInvalidState,
                "The order's state does not allow this change.", transition.Message,
                ("from", transition.From), ("to", transition.To)),
            InvalidSelectionFenceException fence => InvalidSelectionParameters(fence.Message, fence.Defect.ToString()),
            TargetLedgerConflictException conflict => Create(
                StatusCodes.Status409Conflict, TargetLedger.ConflictProblemCode,
                "Another order holds some of these features with blocking exclusivity.", conflict.Message,
                ("orderId", orderId), ("conflicts", conflict.Conflicts)),
            TargetLedgerUnavailableException unavailable => Create(
                StatusCodes.Status503ServiceUnavailable, WorkflowProblemCodes.TargetLedgerUnavailable,
                "The global target ledger cannot serve this request now; retry later.", unavailable.Message),
            ArgumentException or InvalidCastException or FormatException => InvalidSelectionParameters(exception.Message),
            _ => null,
        };
    }
}

using System.Text.Json;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Rest;

/// <summary>
/// Turns a failed response of the order-target and selection routes back into the exception
/// the in-process adapter or service throws for the same condition, keyed on the problem body's
/// stable <c>code</c> (<see cref="WorkflowProblemCodes"/>) — so code written against the ports
/// behaves the same whether the port is local or remote.
///
/// A failure that is not a recognised problem (no body, another content, an unknown code — a
/// 401 or a 500, say) is not guessed at: it surfaces as <see cref="HttpRequestException"/>.
/// </summary>
internal static class WorkflowProblemTranslator
{
    /// <summary>
    /// Returns normally for a successful response; otherwise throws the translated exception.
    /// </summary>
    /// <param name="response">The response to judge.</param>
    /// <param name="orderId">The order the call was about, for exceptions that carry one.</param>
    /// <param name="action">The action the call needed, reported on a denial.</param>
    /// <param name="ct">Cancels reading the problem body.</param>
    public static async Task ThrowIfFailedAsync(
        HttpResponseMessage response, string? orderId, OrderActionType action, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var problem = await ReadAsync(response, ct).ConfigureAwait(false);
        if (problem is not null && ToException(problem, orderId, action) is { } translated)
            throw translated;

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Reads the problem body of a failed response; null when the response succeeded or its
    /// body is not a problem object carrying a <c>code</c>.
    /// </summary>
    public static async Task<WorkflowProblem?> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return null;

        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text)) return null;

        try
        {
            using var document = JsonDocument.Parse(text);
            var body = document.RootElement;
            if (body.ValueKind != JsonValueKind.Object
                || !body.TryGetProperty("code", out var code)
                || code.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var problem = new WorkflowProblem(code.GetString()!, string.Empty, body.Clone());
            return problem with { Detail = problem.String("detail") ?? problem.String("title") ?? problem.Code };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The exception for a problem; null for a code this client does not know.</summary>
    public static Exception? ToException(WorkflowProblem problem, string? orderId, OrderActionType action) => problem.Code switch
    {
        WorkflowProblemCodes.ValidationFailed => InvalidRequest(problem.Detail),
        WorkflowProblemCodes.InvalidSelectionParameters => InvalidParameters(problem),
        WorkflowProblemCodes.UnknownSelectionStrategy => new KeyNotFoundException(problem.Detail),
        WorkflowProblemCodes.SelectionTooLarge => new SelectionTooLargeException(problem.Int32("matchedAtLeast"), problem.Int32("ceiling")),
        WorkflowProblemCodes.PagingOutOfRange => new ArgumentOutOfRangeException(null, problem.Detail),
        WorkflowProblemCodes.OrderNotFound
            or WorkflowProblemCodes.TargetNotFound
            or WorkflowProblemCodes.OrderTypeNotFound => new KeyNotFoundException(problem.Detail),
        WorkflowProblemCodes.NoTargetPolicy => new InvalidOperationException(problem.Detail),
        WorkflowProblemCodes.OrderInvalidState => new InvalidServiceOrderTransitionException(
            problem.String("from") ?? string.Empty, problem.String("to") ?? string.Empty),
        WorkflowProblemCodes.TargetConcurrentModification => new ServiceOrderConcurrencyException(
            problem.String("orderId") ?? orderId ?? string.Empty),
        WorkflowProblemCodes.TargetLimitExceeded => new SelectionLimitExceededException(
            problem.Int32("limit"), problem.Int32("current"), problem.Int32("requested")),
        WorkflowProblemCodes.Denied => new OrderActionDeniedException(
            orderId ?? string.Empty, RuleEvaluationResult.Deny(action, problem.String("reason") ?? problem.Detail)),
        // Staged occupancy (XD01-315).
        OccupancyProblemCodes.InvalidChange => new InvalidOccupancyChangeException(problem.Detail),
        OccupancyProblemCodes.EntryNotFound => new OccupancyEntryNotFoundException(problem.Detail),
        OccupancyProblemCodes.NotAPole => new TargetIsNotAPoleException(problem.Detail),
        OccupancyProblemCodes.TargetReleased => new OccupancyTargetReleasedException(problem.Detail),
        _ => null,
    };

    /// <summary>An invalid fence comes back as the typed exception the validator throws; anything else as <see cref="ArgumentException"/>.</summary>
    private static Exception InvalidParameters(WorkflowProblem problem) =>
        Enum.TryParse<SelectionFenceDefect>(problem.String("defect"), ignoreCase: false, out var defect) && Enum.IsDefined(defect)
            ? new InvalidSelectionFenceException(defect, problem.Detail)
            : InvalidRequest(problem.Detail);

    /// <summary>
    /// The host rejected what was sent. The exception names <c>request</c> as the offending
    /// argument because that is all a client can say; which member was wrong is in the message.
    /// </summary>
    /// <param name="request">The host's description of what is wrong with the request.</param>
    private static ArgumentException InvalidRequest(string request) => new(request, nameof(request));
}

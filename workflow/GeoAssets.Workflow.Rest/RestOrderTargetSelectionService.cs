using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Rest;

/// <summary>
/// REST client implementation of <see cref="IOrderTargetSelectionService"/> over the selection
/// preview and apply routes. <paramref name="http"/>'s base address is the workflow API root.
///
/// The <see cref="WorkflowPrincipal"/> argument is required by the port but never sent: the
/// host derives the caller from the request's token, and nothing a client says about itself is
/// trusted. Failures come back as the exceptions the in-process service throws
/// (<see cref="WorkflowProblemTranslator"/>).
/// </summary>
public sealed class RestOrderTargetSelectionService(HttpClient http) : IOrderTargetSelectionService
{
    private static readonly JsonSerializerOptions _opts = GeoJsonSerializer.Options;

    public async Task<TargetSelectionPreview> PreviewAsync(TargetSelectionRequest request, WorkflowPrincipal principal, CancellationToken ct)
    {
        using var response = await SendAsync("preview", request, principal, ct).ConfigureAwait(false);
        await WorkflowProblemTranslator.ThrowIfFailedAsync(response, request.OrderId, OrderActionType.View, ct).ConfigureAwait(false);

        return await response.Content.ReadFromJsonAsync<TargetSelectionPreview>(_opts, ct).ConfigureAwait(false)
            ?? throw new JsonException("The selection preview response is empty.");
    }

    public async Task<TargetSelectionResult> ApplyAsync(TargetSelectionRequest request, WorkflowPrincipal principal, CancellationToken ct)
    {
        using var response = await SendAsync("apply", request, principal, ct).ConfigureAwait(false);
        await WorkflowProblemTranslator.ThrowIfFailedAsync(response, request.OrderId, OrderActionType.Annotate, ct).ConfigureAwait(false);

        return await response.Content.ReadFromJsonAsync<TargetSelectionResult>(_opts, ct).ConfigureAwait(false)
            ?? throw new JsonException("The selection result response is empty.");
    }

    private Task<HttpResponseMessage> SendAsync(
        string operation, TargetSelectionRequest request, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OrderId, nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StrategyId, nameof(request));

        var body = new
        {
            strategyId = request.StrategyId,
            parameters = request.Parameters,
            page = request.Page,
            pageSize = request.PageSize,
            acceptTruncation = request.AcceptTruncation,
        };

        return http.PostAsJsonAsync(
            new Uri($"service-orders/{Uri.EscapeDataString(request.OrderId)}/targets/selection/{operation}", UriKind.Relative),
            body,
            _opts,
            ct);
    }
}

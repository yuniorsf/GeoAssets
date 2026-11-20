using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rest;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.NeutralOperator.Rest;

/// <summary>
/// The shared-use dispatch ports over the host's routes (XD01-316):
/// <see cref="ISharedUseDispatchService"/> and <see cref="ISharedUseDispatchPlanReader"/> for a
/// client that cannot reach the stores.
///
/// The principal is required by the ports and never sent: the host takes the caller from the
/// token. Arguments are checked, and an already-cancelled token throws, before any request. A
/// failure the host reports as a problem comes back as the exception the in-process service
/// throws for it (see <see cref="WorkflowProblemTranslator"/> and <see cref="NeutralOperatorProblemMapper"/>); anything else is an
/// <see cref="HttpRequestException"/>.
/// </summary>
public sealed class RestSharedUseDispatchClient(HttpClient http) : ISharedUseDispatchService, ISharedUseDispatchPlanReader
{
    private const string PlanRoute = "shared-use/dispatch-plan";

    private static readonly JsonSerializerOptions _opts = GeoJsonSerializer.Options;

    private static readonly IWorkflowProblemMapper[] ProblemMappers = [new NeutralOperatorProblemMapper()];

    public async Task<SharedUseDispatchPreview> PreviewAsync(
        string orderId, int page, int pageSize, WorkflowPrincipal principal, CancellationToken ct)
    {
        Check(orderId, page, pageSize, principal, ct);

        using var response = await http.GetAsync(PagedRoute(orderId, PlanRoute, page, pageSize), ct).ConfigureAwait(false);
        await ThrowIfFailedAsync(response, orderId, ct).ConfigureAwait(false);

        return await ReadAsync<SharedUseDispatchPreview>(response, ct).ConfigureAwait(false);
    }

    public Task<PagedResult<SharedUseSkippedEntry>> GetSkippedEntriesAsync(
        string orderId, int page, int pageSize, WorkflowPrincipal principal, CancellationToken ct) =>
        PageAsync<SharedUseSkippedEntry>(orderId, $"{PlanRoute}/skipped-entries", page, pageSize, principal, ct);

    public Task<PagedResult<string>> GetUnnotifiablePolesAsync(
        string orderId, int page, int pageSize, WorkflowPrincipal principal, CancellationToken ct) =>
        PageAsync<string>(orderId, $"{PlanRoute}/unnotifiable-poles", page, pageSize, principal, ct);

    public async Task<SharedUseDispatchResult> DispatchAsync(
        string orderId, byte[] expectedRowVersion, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(expectedRowVersion);
        ArgumentNullException.ThrowIfNull(principal);
        ct.ThrowIfCancellationRequested();

        var uri = new Uri($"service-orders/{Uri.EscapeDataString(orderId)}/shared-use/dispatch", UriKind.Relative);
        using var response = await http
            .PostAsJsonAsync(uri, new DispatchBody(expectedRowVersion), _opts, ct)
            .ConfigureAwait(false);
        await ThrowIfFailedAsync(response, orderId, ct).ConfigureAwait(false);

        return await ReadAsync<SharedUseDispatchResult>(response, ct).ConfigureAwait(false);
    }

    private async Task<PagedResult<T>> PageAsync<T>(
        string orderId, string route, int page, int pageSize, WorkflowPrincipal principal, CancellationToken ct)
    {
        Check(orderId, page, pageSize, principal, ct);

        using var response = await http.GetAsync(PagedRoute(orderId, route, page, pageSize), ct).ConfigureAwait(false);
        await ThrowIfFailedAsync(response, orderId, ct).ConfigureAwait(false);

        var envelope = await ReadAsync<PagedEnvelope<T>>(response, ct).ConfigureAwait(false);
        return new PagedResult<T> { Items = envelope.Items ?? [], TotalCount = envelope.TotalCount };
    }

    private static void Check(string orderId, int page, int pageSize, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(principal);

        // The same limits the host and the in-process service apply, checked before the request.
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, OrderTargetQuery.MaxPageSize);
        ct.ThrowIfCancellationRequested();
    }

    private static Uri PagedRoute(string orderId, string route, int page, int pageSize) => new(
        string.Create(
            CultureInfo.InvariantCulture,
            $"service-orders/{Uri.EscapeDataString(orderId)}/{route}?page={page}&pageSize={pageSize}"),
        UriKind.Relative);

    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, string orderId, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var problem = await WorkflowProblemTranslator.ReadAsync(response, ct).ConfigureAwait(false);
        if (problem is { Code: WorkflowProblemCodes.OrderTypeNotFound })
            throw new OrderTypeNotRegisteredException(problem.GetString("orderTypeId") ?? string.Empty, problem.Detail);
        if (problem is not null && WorkflowProblemTranslator.ToException(problem, orderId, OrderAction.Dispatch, ProblemMappers) is { } translated)
            throw translated;

        response.EnsureSuccessStatusCode();
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct) =>
        await response.Content.ReadFromJsonAsync<T>(_opts, ct).ConfigureAwait(false)
            ?? throw new JsonException("The shared-use dispatch response is empty.");

    private sealed record DispatchBody(byte[] RowVersion);
}

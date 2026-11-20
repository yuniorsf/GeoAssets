using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rest;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.NeutralOperator.Rest;

/// <summary>
/// REST client implementation of <see cref="IOrderOccupancyStagingService"/> over the
/// staged-occupancy routes (XD01-315). <paramref name="http"/>'s base address is the workflow
/// API root.
///
/// The <see cref="WorkflowPrincipal"/> argument is required by the port but never sent: the
/// host derives the caller from the request's token. Failures come back as the exceptions the
/// in-process service throws (<see cref="WorkflowProblemTranslator"/> with <see cref="NeutralOperatorProblemMapper"/>); arguments are checked,
/// and an already-cancelled token throws, before any request.
/// </summary>
public sealed class RestOrderOccupancyStagingService(HttpClient http) : IOrderOccupancyStagingService
{
    private static readonly JsonSerializerOptions _opts = GeoJsonSerializer.Options;

    private static readonly IWorkflowProblemMapper[] ProblemMappers = [new NeutralOperatorProblemMapper()];

    public async Task<TargetOccupancyView> GetAsync(string orderId, string targetId, WorkflowPrincipal principal, CancellationToken ct)
    {
        Check(orderId, targetId, principal, ct);

        using var response = await http.GetAsync(TargetRoute(orderId, targetId, "occupancy"), ct).ConfigureAwait(false);
        await ThrowIfFailedAsync(response, orderId, targetId, OrderAction.View, ct).ConfigureAwait(false);

        return await ReadAsync<TargetOccupancyView>(response, ct).ConfigureAwait(false);
    }

    public async Task<PagedResult<OccupancyChangeSetView>> GetPageAsync(
        string orderId, int page, int pageSize, OccupancyChangeSetStatus? status, WorkflowPrincipal principal, CancellationToken ct)
    {
        // The query record is what rejects paging out of range, exactly as it does in process.
        var query = new OccupancyChangeSetQuery(orderId, page, pageSize, status);
        ArgumentNullException.ThrowIfNull(principal);
        ct.ThrowIfCancellationRequested();

        var statusValue = query.Status is { } wanted ? wanted.ToString() : "all";
        var uri = new Uri(
            string.Create(
                CultureInfo.InvariantCulture,
                $"service-orders/{Uri.EscapeDataString(orderId)}/occupancy-changes?page={query.Page}&pageSize={query.PageSize}&status={statusValue}"),
            UriKind.Relative);

        using var response = await http.GetAsync(uri, ct).ConfigureAwait(false);
        await ThrowIfFailedAsync(response, orderId, targetId: null, OrderAction.View, ct).ConfigureAwait(false);

        var envelope = await ReadAsync<PagedEnvelope<OccupancyChangeSetView>>(response, ct).ConfigureAwait(false);
        return new PagedResult<OccupancyChangeSetView> { Items = envelope.Items ?? [], TotalCount = envelope.TotalCount };
    }

    public async Task<TargetOccupancyView> StageAsync(
        string orderId, string targetId, IReadOnlyCollection<OccupancyChange> changes, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(changes);
        Check(orderId, targetId, principal, ct);

        using var response = await http
            .PutAsJsonAsync(TargetRoute(orderId, targetId, "occupancy-changes"), new { changes }, _opts, ct)
            .ConfigureAwait(false);
        await ThrowIfFailedAsync(response, orderId, targetId, OrderAction.Annotate, ct).ConfigureAwait(false);

        return await ReadAsync<TargetOccupancyView>(response, ct).ConfigureAwait(false);
    }

    public async Task<bool> DiscardAsync(string orderId, string targetId, WorkflowPrincipal principal, CancellationToken ct)
    {
        Check(orderId, targetId, principal, ct);

        using var response = await http.DeleteAsync(TargetRoute(orderId, targetId, "occupancy-changes"), ct).ConfigureAwait(false);
        await ThrowIfFailedAsync(response, orderId, targetId, OrderAction.Annotate, ct).ConfigureAwait(false);

        return (await ReadAsync<DiscardedEnvelope>(response, ct).ConfigureAwait(false)).Discarded > 0;
    }

    public async Task<int> DiscardAllAsync(string orderId, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(principal);
        ct.ThrowIfCancellationRequested();

        var uri = new Uri($"service-orders/{Uri.EscapeDataString(orderId)}/occupancy-changes", UriKind.Relative);
        using var response = await http.DeleteAsync(uri, ct).ConfigureAwait(false);
        await ThrowIfFailedAsync(response, orderId, targetId: null, OrderAction.Annotate, ct).ConfigureAwait(false);

        return (await ReadAsync<DiscardedEnvelope>(response, ct).ConfigureAwait(false)).Discarded;
    }

    private static void Check(string orderId, string targetId, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ArgumentNullException.ThrowIfNull(principal);
        ct.ThrowIfCancellationRequested();
    }

    private static Uri TargetRoute(string orderId, string targetId, string leaf) => new(
        $"service-orders/{Uri.EscapeDataString(orderId)}/targets/{Uri.EscapeDataString(targetId)}/{leaf}", UriKind.Relative);

    /// <summary>
    /// As <see cref="WorkflowProblemTranslator.ThrowIfFailedAsync"/>, except that a missing
    /// target comes back as the port's own <see cref="OrderTargetNotFoundException"/> — the
    /// shared translation only says "not found".
    /// </summary>
    private static async Task ThrowIfFailedAsync(
        HttpResponseMessage response, string orderId, string? targetId, OrderAction action, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var problem = await WorkflowProblemTranslator.ReadAsync(response, ct).ConfigureAwait(false);
        if (problem is { Code: WorkflowProblemCodes.TargetNotFound } && targetId is not null)
            throw new OrderTargetNotFoundException(orderId, targetId);
        if (problem is not null && WorkflowProblemTranslator.ToException(problem, orderId, action, ProblemMappers) is { } translated)
            throw translated;

        response.EnsureSuccessStatusCode();
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct) =>
        await response.Content.ReadFromJsonAsync<T>(_opts, ct).ConfigureAwait(false)
            ?? throw new JsonException("The occupancy response is empty.");

    private sealed record DiscardedEnvelope(int Discarded);
}

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Rest;

/// <summary>
/// REST client implementation of <see cref="IServiceOrderTargetReader"/> and
/// <see cref="IServiceOrderTargetWriter"/> over the order-target routes of
/// <c>ServiceOrderTargetsRestApiExtensions</c> — the WASM-side counterpart of
/// <c>EFServiceOrderTargetRepository</c>. <paramref name="http"/>'s base address is the
/// workflow API root, e.g. <c>https://host/api/workflow/</c>.
///
/// Failures are turned back into the exceptions the ports document by
/// <see cref="WorkflowProblemTranslator"/>. Two differences from a store are by design:
/// <list type="bullet">
/// <item><description>
/// <see cref="AddRangeAsync"/> is not offered. The host has no route that stores caller-built
/// targets — a target's alias, position, conflict level and organization are derived on the
/// server from the order and the asset — so targets are added through
/// <see cref="IOrderTargetSelectionService.ApplyAsync"/>. A non-empty call throws
/// <see cref="NotSupportedException"/>.
/// </description></item>
/// <item><description>
/// <see cref="UpdateAsync"/> goes through the host's edit route, which re-validates what a
/// store takes on trust: activity ids must belong to the order type's catalog and the status
/// must be a move the target lifecycle allows (<see cref="ArgumentException"/> otherwise), and
/// the conflict level is not writable. The caller must also be allowed to annotate the order.
/// </description></item>
/// </list>
/// </summary>
public sealed class RestServiceOrderTargetRepository(HttpClient http, IEnumerable<IWorkflowProblemMapper>? problemMappers = null)
    : IServiceOrderTargetReader, IServiceOrderTargetWriter
{
    /// <summary>The most ids the usages and remove routes accept in one request.</summary>
    public const int MaxIdsPerRequest = 500;

    private static readonly JsonSerializerOptions _opts = GeoJsonSerializer.Options;

    private readonly IWorkflowProblemMapper[] _problemMappers = [.. problemMappers ?? []];

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<PagedResult<ServiceOrderTarget>> GetPageAsync(OrderTargetQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        // A status that is not one of the defined keys can match no target; the host rejects it
        // as a malformed filter, where a store simply finds nothing.
        if (query.Status is not null && !OrderTargetStatus.IsDefined(query.Status))
            return new PagedResult<ServiceOrderTarget> { Items = [], TotalCount = 0 };

        using var response = await http.GetAsync(PageUri(query), ct).ConfigureAwait(false);
        if (await IsUnknownOrderAsync(response, ct).ConfigureAwait(false))
            return new PagedResult<ServiceOrderTarget> { Items = [], TotalCount = 0 };

        await WorkflowProblemTranslator.ThrowIfFailedAsync(response, query.OrderId, OrderAction.View, _problemMappers, ct).ConfigureAwait(false);

        var page = await response.Content.ReadFromJsonAsync<PagedEnvelope<ServiceOrderTarget>>(_opts, ct).ConfigureAwait(false);
        return new PagedResult<ServiceOrderTarget> { Items = page?.Items ?? [], TotalCount = page?.TotalCount ?? 0 };
    }

    public async Task<ServiceOrderTarget?> GetByIdAsync(string orderId, string targetId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ct.ThrowIfCancellationRequested();

        using var response = await http.GetAsync(
            Relative($"service-orders/{Uri.EscapeDataString(orderId)}/targets/{Uri.EscapeDataString(targetId)}"), ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var problem = await WorkflowProblemTranslator.ReadAsync(response, ct).ConfigureAwait(false);
            if (problem?.Code is WorkflowProblemCodes.TargetNotFound or WorkflowProblemCodes.OrderNotFound) return null;
        }

        await WorkflowProblemTranslator.ThrowIfFailedAsync(response, orderId, OrderAction.View, _problemMappers, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<ServiceOrderTarget>(_opts, ct).ConfigureAwait(false);
    }

    public async Task<int> CountAsync(string orderId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ct.ThrowIfCancellationRequested();

        using var response = await http.GetAsync(
            Relative($"service-orders/{Uri.EscapeDataString(orderId)}/targets/count"), ct).ConfigureAwait(false);
        if (await IsUnknownOrderAsync(response, ct).ConfigureAwait(false)) return 0;

        await WorkflowProblemTranslator.ThrowIfFailedAsync(response, orderId, OrderAction.View, _problemMappers, ct).ConfigureAwait(false);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_opts, ct).ConfigureAwait(false);
        return body.GetProperty("count").GetInt32();
    }

    public async Task<IReadOnlyList<OrderTargetUsage>> FindOpenUsagesAsync(
        IReadOnlyCollection<string> featureIds, string excludingOrderId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(featureIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(excludingOrderId);
        ct.ThrowIfCancellationRequested();

        var wanted = featureIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        var usages = new List<OrderTargetUsage>();

        foreach (var chunk in wanted.Chunk(MaxIdsPerRequest))
        {
            for (var page = 1; ; page++)
            {
                var request = new { featureIds = chunk, excludingOrderId, page, pageSize = OrderTargetQuery.MaxPageSize };
                using var response = await http
                    .PostAsJsonAsync(Relative("service-orders/targets/usages"), request, _opts, ct).ConfigureAwait(false);
                await WorkflowProblemTranslator.ThrowIfFailedAsync(response, excludingOrderId, OrderAction.View, _problemMappers, ct).ConfigureAwait(false);

                var envelope = await response.Content.ReadFromJsonAsync<PagedEnvelope<OrderTargetUsage>>(_opts, ct).ConfigureAwait(false);
                var items = envelope?.Items ?? [];
                usages.AddRange(items);

                if (items.Count == 0 || (long)page * OrderTargetQuery.MaxPageSize >= (envelope?.TotalCount ?? 0)) break;
            }
        }

        return usages;
    }

    public async Task<OrderTargetExtent?> GetExtentAsync(string orderId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ct.ThrowIfCancellationRequested();

        using var response = await http.GetAsync(
            Relative($"service-orders/{Uri.EscapeDataString(orderId)}/targets/extent"), ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        if (await IsUnknownOrderAsync(response, ct).ConfigureAwait(false)) return null;

        await WorkflowProblemTranslator.ThrowIfFailedAsync(response, orderId, OrderAction.View, _problemMappers, ct).ConfigureAwait(false);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_opts, ct).ConfigureAwait(false);
        if (body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("bbox", out var bbox)
            || bbox.ValueKind != JsonValueKind.Array
            || bbox.GetArrayLength() != 4
            || bbox.EnumerateArray().Any(n => n.ValueKind != JsonValueKind.Number))
        {
            throw new JsonException("The extent response is not a [minLon, minLat, maxLon, maxLat] bounding box.");
        }

        return new OrderTargetExtent(bbox[0].GetDouble(), bbox[1].GetDouble(), bbox[2].GetDouble(), bbox[3].GetDouble());
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public Task<int> AddRangeAsync(string orderId, IReadOnlyCollection<ServiceOrderTarget> targets, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(targets);
        ct.ThrowIfCancellationRequested();

        if (targets.Count == 0) return Task.FromResult(0);

        throw new NotSupportedException(
            $"Targets cannot be added through the REST client: the host derives them from a selection. " +
            $"Use {nameof(IOrderTargetSelectionService)}.{nameof(IOrderTargetSelectionService.ApplyAsync)}.");
    }

    public async Task UpdateAsync(ServiceOrderTarget target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        ct.ThrowIfCancellationRequested();

        // "note" is always sent, null included: UpdateAsync writes the note as given, and the
        // route clears it only when the member is present.
        using var content = new StringContent(
            JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["activityIds"] = target.ActivityIds,
                ["note"] = target.Note,
                ["status"] = target.Status,
                ["version"] = target.Version,
            }),
            Encoding.UTF8,
            "application/json");

        using var response = await http.PatchAsync(
            Relative($"service-orders/{Uri.EscapeDataString(target.OrderId)}/targets/{Uri.EscapeDataString(target.Id)}"),
            content,
            ct).ConfigureAwait(false);

        await WorkflowProblemTranslator.ThrowIfFailedAsync(response, target.OrderId, OrderAction.Annotate, _problemMappers, ct).ConfigureAwait(false);
    }

    public async Task<int> RemoveAsync(string orderId, IReadOnlyCollection<string> targetIds, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(targetIds);
        ct.ThrowIfCancellationRequested();

        var wanted = targetIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        var removed = 0;

        foreach (var chunk in wanted.Chunk(MaxIdsPerRequest))
        {
            using var response = await http.PostAsJsonAsync(
                Relative($"service-orders/{Uri.EscapeDataString(orderId)}/targets/remove"), new { targetIds = chunk }, _opts, ct).ConfigureAwait(false);
            if (await IsUnknownOrderAsync(response, ct).ConfigureAwait(false)) return removed;

            await WorkflowProblemTranslator.ThrowIfFailedAsync(response, orderId, OrderAction.Annotate, _problemMappers, ct).ConfigureAwait(false);

            var body = await response.Content.ReadFromJsonAsync<JsonElement>(_opts, ct).ConfigureAwait(false);
            removed += body.GetProperty("removed").GetInt32();
        }

        return removed;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Uri Relative(string path) => new(path, UriKind.Relative);

    private static Uri PageUri(OrderTargetQuery query)
    {
        var uri = new StringBuilder($"service-orders/{Uri.EscapeDataString(query.OrderId)}/targets")
            .Append(CultureInfo.InvariantCulture, $"?page={query.Page}&pageSize={query.PageSize}");

        if (query.SortBy is not null) uri.Append("&sortBy=").Append(Uri.EscapeDataString(query.SortBy));
        if (query.Descending) uri.Append("&descending=true");
        if (query.Status is not null) uri.Append("&status=").Append(Uri.EscapeDataString(query.Status));
        if (query.ConflictLevel is { } level) uri.Append("&conflictLevel=").Append(level.ToString());

        return Relative(uri.ToString());
    }

    /// <summary>
    /// Whether the host answered that the order does not exist — which the reader contract, and
    /// <see cref="RemoveAsync"/>, read as "no targets", not as an error.
    /// </summary>
    private static async Task<bool> IsUnknownOrderAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode != HttpStatusCode.NotFound) return false;

        var problem = await WorkflowProblemTranslator.ReadAsync(response, ct).ConfigureAwait(false);
        return problem?.Code is WorkflowProblemCodes.OrderNotFound;
    }
}

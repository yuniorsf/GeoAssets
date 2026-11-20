using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Identity.Authentication;
using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Rest;

/// <summary>
/// HTTP implementation of <see cref="ITargetLedgerClient"/> over the kernel's machine-to-machine ledger routes
/// (<c>/api/targets/ledger/{vertical}/{orderId}/reserve|release|commit|usages</c>, XD01-379), relative to
/// <paramref name="http"/>'s base address — the kernel's root, e.g. <c>http://geoassets-server:8080/</c>. Every request
/// carries a fresh <c>GeoAssets.ServiceCaller</c> bearer token from <paramref name="tokenProvider"/> when one is registered
/// (without one the kernel answers 401, which surfaces as <see cref="TargetLedgerUnavailableException"/>).
/// <list type="bullet">
///   <item>409 <c>targets.conflict</c> → <see cref="TargetLedgerConflictException"/> with the blocking holders.</item>
///   <item>404 <c>order-types.not-found</c> (the vertical's descriptors are not published yet), a transport failure, a 5xx
///   or an authentication failure → <see cref="TargetLedgerUnavailableException"/>: nothing was reserved, retry later.</item>
///   <item>Every other problem → the exception the in-process selection throws (<see cref="WorkflowProblemTranslator"/>).</item>
/// </list>
/// </summary>
public sealed class RestTargetLedgerClient(HttpClient http, IServiceAuthTokenProvider? tokenProvider = null) : ITargetLedgerClient
{
    private static readonly JsonSerializerOptions _opts = GeoJsonSerializer.Options;

    public async Task<TargetSelectionPreview> PreviewAsync(TargetLedgerReserveCommand command, CancellationToken ct)
    {
        using var response = await PostReserveAsync(command, preview: true, ct).ConfigureAwait(false);
        await ThrowIfReserveFailedAsync(response, command, OrderAction.View, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<TargetSelectionPreview>(_opts, ct).ConfigureAwait(false)
            ?? throw new JsonException("The ledger's preview response is empty.");
    }

    public async Task<TargetSelectionResult> ReserveAsync(TargetLedgerReserveCommand command, CancellationToken ct)
    {
        using var response = await PostReserveAsync(command, preview: false, ct).ConfigureAwait(false);
        await ThrowIfReserveFailedAsync(response, command, OrderAction.Annotate, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<TargetSelectionResult>(_opts, ct).ConfigureAwait(false)
            ?? throw new JsonException("The ledger's reservation response is empty.");
    }

    public Task<IReadOnlyList<TargetLedgerEventItem>?> ReleaseAsync(
        TargetLedgerKey key, Guid organizationId, IReadOnlyCollection<string>? targetIds, string? actor, CancellationToken ct) =>
        ChangeAsync("release", key, organizationId, targetIds, actor, ct);

    public Task<IReadOnlyList<TargetLedgerEventItem>?> CommitAsync(
        TargetLedgerKey key, Guid organizationId, IReadOnlyCollection<string>? targetIds, string? actor, CancellationToken ct) =>
        ChangeAsync("commit", key, organizationId, targetIds, actor, ct);

    public async Task<PagedResult<TargetLedgerEntry>> GetUsagesAsync(
        TargetLedgerKey key, Guid organizationId, int skip, int take, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);

        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"{OrderPath(key)}/usages?organizationId={organizationId:D}&skip={skip}&take={take}");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return new PagedResult<TargetLedgerEntry> { Items = [], TotalCount = 0 };
        await ThrowIfFailedAsync(response, key.OrderId, OrderAction.View, ct).ConfigureAwait(false);

        var page = await response.Content.ReadFromJsonAsync<TargetLedgerUsagesEnvelope>(_opts, ct).ConfigureAwait(false)
            ?? throw new JsonException("The ledger's usages response is empty.");
        return new PagedResult<TargetLedgerEntry> { Items = page.Items, TotalCount = page.TotalCount };
    }

    private async Task<HttpResponseMessage> PostReserveAsync(TargetLedgerReserveCommand command, bool preview, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.OrderTypeId, nameof(command));
        ArgumentException.ThrowIfNullOrWhiteSpace(command.StrategyId, nameof(command));

        var body = new
        {
            organizationId = command.OrganizationId,
            orderTypeId = command.OrderTypeId,
            strategyId = command.StrategyId,
            parameters = command.Parameters,
            preview,
            acceptTruncation = command.AcceptTruncation,
            page = command.Page,
            pageSize = command.PageSize,
            actor = command.Actor,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{OrderPath(command.Key)}/reserve", UriKind.Relative))
        {
            Content = JsonContent.Create(body, options: _opts),
        };
        return await SendAsync(request, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<TargetLedgerEventItem>?> ChangeAsync(
        string operation, TargetLedgerKey key, Guid organizationId, IReadOnlyCollection<string>? targetIds, string? actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);

        var body = new { organizationId, targetIds, actor };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{OrderPath(key)}/{operation}", UriKind.Relative))
        {
            Content = JsonContent.Create(body, options: _opts),
        };
        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await ThrowIfFailedAsync(response, key.OrderId, OrderAction.Annotate, ct).ConfigureAwait(false);

        var changed = await response.Content.ReadFromJsonAsync<TargetLedgerChangeEnvelope>(_opts, ct).ConfigureAwait(false)
            ?? throw new JsonException($"The ledger's {operation} response is empty.");
        return changed.Targets;
    }

    private static string OrderPath(TargetLedgerKey key) =>
        $"api/targets/ledger/{Uri.EscapeDataString(key.VerticalId)}/{Uri.EscapeDataString(key.OrderId)}";

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            if (tokenProvider is not null)
            {
                var token = await tokenProvider.GetAccessTokenAsync(ct).ConfigureAwait(false);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            return await http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new TargetLedgerUnavailableException("The kernel's target ledger is unreachable.", ex);
        }
    }

    private static async Task ThrowIfReserveFailedAsync(
        HttpResponseMessage response, TargetLedgerReserveCommand command, OrderAction action, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var problem = await WorkflowProblemTranslator.ReadAsync(response, ct).ConfigureAwait(false);
        if (problem is { Code: TargetLedger.ConflictProblemCode })
        {
            var conflicts = problem.Body.TryGetProperty("conflicts", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.Deserialize<List<TargetLedgerConflict>>(_opts) ?? []
                : [];
            throw new TargetLedgerConflictException(conflicts);
        }

        if (problem is { Code: WorkflowProblemCodes.OrderTypeNotFound })
        {
            throw new TargetLedgerUnavailableException(
                $"The kernel does not know order type '{command.OrderTypeId}' of vertical '{command.Key.VerticalId}' yet: its registration is pending.");
        }

        Throw(response, problem, command.Key.OrderId, action);
    }

    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, string orderId, OrderAction action, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        Throw(response, await WorkflowProblemTranslator.ReadAsync(response, ct).ConfigureAwait(false), orderId, action);
    }

    private static void Throw(HttpResponseMessage response, WorkflowProblem? problem, string orderId, OrderAction action)
    {
        if (problem is not null && WorkflowProblemTranslator.ToException(problem, orderId, action) is { } translated)
            throw translated;

        throw new TargetLedgerUnavailableException(string.Create(
            CultureInfo.InvariantCulture,
            $"The kernel's target ledger answered {(int)response.StatusCode} {response.ReasonPhrase}."));
    }
}

using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rest;

/// <summary>
/// Typed client for <c>POST selection/resolve</c>: turns a map gesture, expressed as a selection
/// strategy run (<c>polygon-fence</c> for a box or fence, <c>identifier-list</c> for typed ids),
/// into the ids of the caller's organization's features it selects. <paramref name="http"/>'s
/// base address is the workflow API root.
///
/// This is deliberately not a port implementation: the map-selection resolver port belongs to
/// the UI slice that introduces it, which wraps this client.
/// </summary>
public sealed class RestMapSelectionClient(HttpClient http)
{
    private static readonly JsonSerializerOptions _opts = GeoJsonSerializer.Options;

    /// <summary>
    /// Returns the selected feature ids, ordered by id (ordinal). The result is bounded, not
    /// paged: a gesture selecting more than the host allows throws
    /// <see cref="Targets.SelectionTooLargeException"/>. An unknown strategy throws
    /// <see cref="KeyNotFoundException"/>; an invalid fence throws
    /// <see cref="GeoAssets.Core.Services.InvalidSelectionFenceException"/>; other parameter
    /// errors throw <see cref="ArgumentException"/>.
    /// </summary>
    public async Task<IReadOnlyList<string>> ResolveAsync(
        string strategyId, IReadOnlyDictionary<string, object> parameters, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);
        ArgumentNullException.ThrowIfNull(parameters);

        using var response = await http.PostAsJsonAsync(
            new Uri("selection/resolve", UriKind.Relative), new { strategyId, parameters }, _opts, ct).ConfigureAwait(false);
        await WorkflowProblemTranslator.ThrowIfFailedAsync(response, orderId: null, OrderActionType.View, ct).ConfigureAwait(false);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_opts, ct).ConfigureAwait(false);
        return body.ValueKind == JsonValueKind.Object && body.TryGetProperty("featureIds", out var ids) && ids.ValueKind == JsonValueKind.Array
            ? [.. ids.EnumerateArray().Select(id => id.GetString()!)]
            : throw new JsonException("The selection resolve response carries no featureIds array.");
    }
}

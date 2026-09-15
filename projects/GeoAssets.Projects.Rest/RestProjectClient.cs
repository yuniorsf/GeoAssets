using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;

namespace GeoAssets.Projects.Rest;

/// <summary>
/// <see cref="IProjectClient"/> backed by a remote GeoAssets Projects REST API (see
/// <c>GeoAssets.Server</c>'s <c>ProjectsRestApiExtensions.MapProjectsApi</c>, XD01-141).
///
/// Direct, non-caching client — every method awaits its own HTTP round trip and
/// returns/throws exactly what the server reports, matching <c>RestServiceOrderRepository</c>'s
/// shape rather than <c>RestAssetProvider</c>'s local-cache-plus-fire-and-forget one: callers
/// need to observe copy-on-write redirects and authorization failures precisely, which a cache
/// would obscure.
/// </summary>
public sealed class RestProjectClient(HttpClient http) : IProjectClient
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, id);
        return await response.Content.ReadFromJsonAsync<Project>(_opts, ct);
    }

    public async Task<IReadOnlyList<Project>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"organization/{organizationId}", ct);
        await EnsureSuccessAsync(response, organizationId);
        return await response.Content.ReadFromJsonAsync<List<Project>>(_opts, ct) ?? [];
    }

    public async Task<Project> CreateAsync(Project project, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(string.Empty, project, _opts, ct);
        await EnsureSuccessAsync(response, project.ParentProjectId ?? Guid.Empty);
        return await response.Content.ReadFromJsonAsync<Project>(_opts, ct)
            ?? throw new InvalidOperationException("Server returned an empty response for a successful Project create.");
    }

    public Task<Project> UpdateProvidersAsync(Guid id, List<ProjectProviderEntry>? providers, CancellationToken ct = default)
        => PutScopeAsync(id, "providers", providers, ct);

    public Task<Project> UpdateAssetTypeScopeAsync(Guid id, ProjectAssetTypeScope? scope, CancellationToken ct = default)
        => PutScopeAsync(id, "asset-types", scope, ct);

    public Task<Project> UpdateLayerScopeAsync(Guid id, ProjectLayerScope? scope, CancellationToken ct = default)
        => PutScopeAsync(id, "layers", scope, ct);

    public Task<Project> UpdateViewStateAsync(Guid id, ProjectViewState? viewState, CancellationToken ct = default)
        => PutScopeAsync(id, "view", viewState, ct);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<Project> PutScopeAsync<T>(Guid id, string segment, T? body, CancellationToken ct)
    {
        var response = await http.PutAsJsonAsync($"{id}/{segment}", body, _opts, ct);
        await EnsureSuccessAsync(response, id);
        return await response.Content.ReadFromJsonAsync<Project>(_opts, ct)
            ?? throw new InvalidOperationException("Server returned an empty response for a successful Project update.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, Guid id)
    {
        if (response.IsSuccessStatusCode) return;

        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                throw new KeyNotFoundException($"Project '{id}' not found.");

            case HttpStatusCode.Forbidden:
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                var reason = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("reason", out var reasonEl)
                    ? reasonEl.GetString() ?? "Not authorized."
                    : "Not authorized.";
                throw new UnauthorizedAccessException(reason);

            default:
                response.EnsureSuccessStatusCode();
                break;
        }
    }
}

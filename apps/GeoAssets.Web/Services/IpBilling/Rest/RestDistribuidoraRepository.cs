using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using GeoAssets.Core.Models;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;

namespace GeoAssets.Web.Services.IpBilling.Rest;

/// <summary>
/// <see cref="IDistribuidoraRepository"/> backed by <c>GeoAssets.IpBilling.Server</c>'s
/// <c>/api/ip-billing/distribuidoras</c> endpoints (XD01-301). Reads deserialize straight into
/// the domain type; the paged list unwraps the server's
/// <c>{ items, totalCount, page, pageSize }</c> envelope into a <see cref="PagedResult{T}"/>.
/// Every write is sent immediately, so <see cref="SaveChangesAsync"/> is a no-op — same as
/// <see cref="RestMunicipioRepository"/>.
/// </summary>
internal sealed class RestDistribuidoraRepository(HttpClient http) : IDistribuidoraRepository
{
    private sealed record DistribuidoraWriteDto(Guid OrganizationId, string Name);

    public async Task<Distribuidora?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.GetAsync(new Uri($"distribuidoras/{id}", UriKind.Relative), ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<Distribuidora>(ct)
            ?? throw new InvalidOperationException($"GET distribuidoras/{id} returned an empty response.");
    }

    public async Task<PagedResult<Distribuidora>> GetPageAsync(int page, int pageSize, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var body = await http.GetFromJsonAsync<RestPage<Distribuidora>>(
            string.Create(CultureInfo.InvariantCulture, $"distribuidoras?page={page}&pageSize={pageSize}"), ct);
        return RestPage<Distribuidora>.ToPagedResult(body);
    }

    public async Task AddAsync(Distribuidora distribuidora, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(distribuidora);
        var response = await http.PostAsJsonAsync(
            "distribuidoras", new DistribuidoraWriteDto(distribuidora.OrganizationId, distribuidora.Name), ct);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<Distribuidora>(ct)
            ?? throw new InvalidOperationException("POST distribuidoras returned an empty response.");
        distribuidora.Id = created.Id;
    }

    public async Task UpdateAsync(Distribuidora distribuidora, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(distribuidora);
        var response = await http.PutAsJsonAsync(
            $"distribuidoras/{distribuidora.Id}",
            new DistribuidoraWriteDto(distribuidora.OrganizationId, distribuidora.Name),
            ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync(new Uri($"distribuidoras/{id}", UriKind.Relative), ct);
        response.EnsureSuccessStatusCode();
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

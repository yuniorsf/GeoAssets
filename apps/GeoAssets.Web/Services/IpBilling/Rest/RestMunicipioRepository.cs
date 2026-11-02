using System.Net;
using System.Net.Http.Json;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;

namespace GeoAssets.Web.Services.IpBilling.Rest;

/// <summary>
/// <see cref="IMunicipioRepository"/> backed by <c>GeoAssets.IpBilling.Server</c>'s
/// <c>/api/ip-billing/municipios</c> endpoints (XD01-178). The server returns <see cref="Municipio"/>
/// directly (no DTO wrapping) on every read, so GET responses deserialize straight into the
/// domain type — only writes need a local request shape, mirroring
/// <c>IpBillingRestApiExtensions.MunicipioWriteRequest</c> on the server.
///
/// <see cref="GetByIdAsync"/> has no matching by-id server endpoint (only by-ibge-code lookup
/// exists for reads; by-id is only a route parameter on the write endpoints) — resolved
/// client-side from <see cref="GetAllAsync"/>, same workaround as <see cref="RestLampTypeRepository.GetByCodeAsync"/>.
/// </summary>
internal sealed class RestMunicipioRepository(HttpClient http) : IMunicipioRepository
{
    private sealed record MunicipioWriteDto(
        string IbgeCode,
        string Name,
        string Uf,
        Guid DistribuidoraOrganizationId,
        Guid? PrefeituraOrganizationId,
        OrigemWorkflow OrigemLevantamento,
        OrigemWorkflow OrigemFiscalizacao,
        OrigemWorkflow OrigemOficio,
        OrigemWorkflow OrigemMemoriaCalculo);

    public async Task<Municipio?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.FirstOrDefault(m => m.Id == id);
    }

    public async Task<Municipio?> GetByIbgeCodeAsync(string ibgeCode, CancellationToken ct = default)
    {
        var response = await http.GetAsync(new Uri($"municipios/{ibgeCode}", UriKind.Relative), ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<Municipio>(ct)
            ?? throw new InvalidOperationException($"GET municipios/{ibgeCode} returned an empty response.");
    }

    public async Task<IReadOnlyList<Municipio>> GetAllAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<Municipio>>("municipios", ct) ?? [];

    public async Task AddAsync(Municipio municipio, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(municipio);
        var response = await http.PostAsJsonAsync("municipios", ToWriteDto(municipio), ct);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<Municipio>(ct)
            ?? throw new InvalidOperationException("POST municipios returned an empty response.");
        municipio.Id = created.Id;
    }

    public async Task UpdateAsync(Municipio municipio, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(municipio);
        var response = await http.PutAsJsonAsync($"municipios/{municipio.Id}", ToWriteDto(municipio), ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync(new Uri($"municipios/{id}", UriKind.Relative), ct);
        response.EnsureSuccessStatusCode();
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;

    private static MunicipioWriteDto ToWriteDto(Municipio municipio) => new(
        municipio.IbgeCode,
        municipio.Name,
        municipio.Uf,
        municipio.DistribuidoraOrganizationId,
        municipio.PrefeituraOrganizationId,
        municipio.OrigemLevantamento,
        municipio.OrigemFiscalizacao,
        municipio.OrigemOficio,
        municipio.OrigemMemoriaCalculo);
}

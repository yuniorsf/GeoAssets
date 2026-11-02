using System.Net.Http.Json;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;

namespace GeoAssets.Web.Services.IpBilling.Rest;

/// <summary>
/// <see cref="ILampTypeRepository"/> backed by <c>GeoAssets.IpBilling.Server</c>'s
/// <c>/api/ip-billing/lamp-types</c> endpoint (XD01-178) — read-only, matching the seeded,
/// never-user-editable catalog <see cref="ILampTypeRepository"/>'s own doc comment describes.
///
/// <see cref="GetByCodeAsync"/> has no matching server endpoint (only "get all" exists) —
/// resolved client-side from <see cref="GetAllAsync"/>.
/// </summary>
internal sealed class RestLampTypeRepository(HttpClient http) : ILampTypeRepository
{
    public async Task<LampType?> GetByCodeAsync(LampTypeCode code, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.FirstOrDefault(l => l.Code == code);
    }

    public async Task<IReadOnlyList<LampType>> GetAllAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<LampType>>("lamp-types", ct) ?? [];
}

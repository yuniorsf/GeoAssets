using System.Globalization;
using System.Net.Http.Json;
using GeoAssets.Core.Models;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;

namespace GeoAssets.Web.Services.IpBilling.Rest;

/// <summary>
/// <see cref="IDistribuidoraLampWattageRepository"/> backed by <c>GeoAssets.IpBilling.Server</c>'s
/// <c>/api/ip-billing/distribuidoras/{id}/lamp-wattages</c> (paged list, upsert) and
/// <c>/api/ip-billing/distribuidoras/lamp-wattages/{rowId}</c> (delete) endpoints (XD01-301).
///
/// The server exposes only the paged list, upsert and delete — everything the chart editor
/// needs. <see cref="FindAsync"/> and <see cref="GetMeanTotalBilledWAsync"/> (used server-side
/// by the kWh conversion, never by the UI) are answered here by walking the paged list, so the
/// port is honoured without adding routes nothing calls.
/// </summary>
internal sealed class RestDistribuidoraLampWattageRepository(HttpClient http) : IDistribuidoraLampWattageRepository
{
    /// <summary>Page size used when a whole chart has to be walked; the server's own cap.</summary>
    public const int WalkPageSize = 100;

    private sealed record UpsertDto(LampTypeCode Code, int WattageW, decimal BallastLossW);

    public async Task<PagedResult<DistribuidoraLampWattage>> GetPageAsync(
        Guid distribuidoraId, int page, int pageSize, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var body = await http.GetFromJsonAsync<RestPage<DistribuidoraLampWattage>>(
            string.Create(
                CultureInfo.InvariantCulture,
                $"distribuidoras/{distribuidoraId}/lamp-wattages?page={page}&pageSize={pageSize}"),
            ct);
        return RestPage<DistribuidoraLampWattage>.ToPagedResult(body);
    }

    public async Task<DistribuidoraLampWattage?> FindAsync(
        Guid distribuidoraId, LampTypeCode code, int wattageW, CancellationToken ct = default)
    {
        var rows = await GetAllAsync(distribuidoraId, ct);
        return rows.FirstOrDefault(r => r.Code == code && r.WattageW == wattageW);
    }

    public async Task<decimal?> GetMeanTotalBilledWAsync(Guid distribuidoraId, CancellationToken ct = default)
    {
        var rows = await GetAllAsync(distribuidoraId, ct);
        return rows.Count == 0 ? null : rows.Average(r => r.TotalBilledW);
    }

    public async Task<DistribuidoraLampWattage> UpsertAsync(DistribuidoraLampWattage row, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        var response = await http.PutAsJsonAsync(
            $"distribuidoras/{row.DistribuidoraId}/lamp-wattages",
            new UpsertDto(row.Code, row.WattageW, row.BallastLossW),
            ct);
        await RestErrors.EnsureSuccessAsync(response, ct);

        return await response.Content.ReadFromJsonAsync<DistribuidoraLampWattage>(ct)
            ?? throw new InvalidOperationException("PUT distribuidoras/lamp-wattages returned an empty response.");
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync(new Uri($"distribuidoras/lamp-wattages/{id}", UriKind.Relative), ct);
        response.EnsureSuccessStatusCode();
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;

    private async Task<List<DistribuidoraLampWattage>> GetAllAsync(Guid distribuidoraId, CancellationToken ct)
    {
        var rows = new List<DistribuidoraLampWattage>();
        for (var page = 1; ; page++)
        {
            var result = await GetPageAsync(distribuidoraId, page, WalkPageSize, ct);
            rows.AddRange(result.Items);
            if (result.Items.Count == 0 || rows.Count >= result.TotalCount) return rows;
        }
    }
}

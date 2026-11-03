using System.Net;
using System.Net.Http.Json;
using GeoAssets.IpBilling.FaturamentoLedger;
using GeoAssets.IpBilling.Models;

namespace GeoAssets.Web.Services.IpBilling.Rest;

/// <summary>
/// <see cref="IFaturamentoRestClient"/> backed by <c>GeoAssets.IpBilling.Server</c>'s
/// <c>/api/ip-billing/faturamentos/*</c> endpoints (XD01-189).
/// </summary>
internal sealed class FaturamentoRestClient(HttpClient http) : IFaturamentoRestClient
{
    private sealed record ManualEntryRequest(Guid MunicipioId, string Competencia, decimal KwhFaturado, string? PerformedBy);

    private sealed record BatchRequest(IReadOnlyList<FaturamentoBatchItem> Items, string? PerformedBy);

    private sealed record FaturamentoAlreadyPostedBody(string Message, Guid MunicipioId, string Competencia);

    public async Task<IReadOnlyList<Faturamento>> ConsultaAsync(
        Guid municipioId, string? competencia, CancellationToken ct = default)
    {
        var query = string.IsNullOrWhiteSpace(competencia)
            ? string.Empty
            : $"?competencia={Uri.EscapeDataString(competencia)}";

        var response = await http.GetAsync(
            new Uri($"faturamentos/{municipioId}/consulta{query}", UriKind.Relative), ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<Faturamento>>(ct) ?? [];
    }

    public async Task<Faturamento> ManualEntryAsync(
        Guid municipioId, string competencia, decimal kwhFaturado, string? performedBy, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            new Uri("faturamentos/manual-entry", UriKind.Relative),
            new ManualEntryRequest(municipioId, competencia, kwhFaturado, performedBy),
            ct);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var conflict = await response.Content.ReadFromJsonAsync<FaturamentoAlreadyPostedBody>(ct);
            throw conflict is not null
                ? new FaturamentoAlreadyPostedException(conflict.MunicipioId, conflict.Competencia)
                : new FaturamentoAlreadyPostedException();
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Faturamento>(ct)
            ?? throw new InvalidOperationException("POST faturamentos/manual-entry returned an empty response.");
    }

    public async Task<FaturamentoBatchResult> RecargaAsync(
        Guid municipioId, string competencia, decimal kwhFaturado, string? performedBy, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            new Uri("faturamentos/recarga", UriKind.Relative),
            new BatchRequest([new FaturamentoBatchItem(municipioId, competencia, kwhFaturado)], performedBy),
            ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<FaturamentoBatchResult>(ct)
            ?? throw new InvalidOperationException("POST faturamentos/recarga returned an empty response.");
    }

    public async Task<FaturamentoBatchResult> BatchLoadAsync(
        IReadOnlyList<FaturamentoBatchItem> items, string? performedBy, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            new Uri("faturamentos/batch-load", UriKind.Relative),
            new BatchRequest(items, performedBy),
            ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<FaturamentoBatchResult>(ct)
            ?? throw new InvalidOperationException("POST faturamentos/batch-load returned an empty response.");
    }
}

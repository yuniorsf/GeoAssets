using System.Net;
using System.Net.Http.Json;
using GeoAssets.IpBilling.Levantamento;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Web.Services.IpBilling.Rest;

/// <summary>
/// <see cref="ILevantamentoRestClient"/> backed by <c>GeoAssets.IpBilling.Server</c>'s
/// <c>/api/ip-billing/orders</c> and <c>/api/ip-billing/levantamento/*</c> endpoints (XD01-180).
/// </summary>
internal sealed class LevantamentoRestClient(HttpClient http) : ILevantamentoRestClient
{
    private sealed record CreateOrderRequest(
        string OrderTypeId, string Title, string MunicipioIbgeCode, string Competencia,
        string? CodigoInstalacao, string? CreatedBy);

    private sealed record CalculateRequest(DateOnly? AsOf, string? PerformedBy);

    private sealed record ValidateRequest(string? PerformedBy);

    public async Task<ServiceOrder> CreateAsync(
        string municipioIbgeCode, string competencia, string? createdBy, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            "orders",
            new CreateOrderRequest("ip-levantamento", $"Levantamento {competencia}", municipioIbgeCode, competencia, null, createdBy),
            ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ServiceOrder>(ct)
            ?? throw new InvalidOperationException("POST orders returned an empty response.");
    }

    public async Task<int> UploadAsync(string orderId, Stream fileStream, string fileName, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(fileStream);
        content.Add(streamContent, "file", fileName);

        var response = await http.PostAsync(new Uri($"levantamento/{orderId}/upload", UriKind.Relative), content, ct);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowUploadRejectedAsync(response, ct);
        }

        var body = await response.Content.ReadFromJsonAsync<UploadResponse>(ct)
            ?? throw new InvalidOperationException("POST levantamento/upload returned an empty response.");
        return body.TransformerLineCount;
    }

    private sealed record UploadResponse(int TransformerLineCount);

    private static async Task ThrowUploadRejectedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var errorBody = await response.Content.ReadFromJsonAsync<UploadErrorBody>(ct);
            if (errorBody?.Errors is { Count: > 0 } errors)
            {
                throw new LevantamentoUploadRejectedException(
                    $"Levantamento upload contains {errors.Count} invalid row(s).", errors);
            }

            throw new LevantamentoUploadRejectedException(errorBody?.Message ?? "Upload was rejected.", []);
        }

        response.EnsureSuccessStatusCode();
    }

    private sealed record UploadErrorBody(string? Message, IReadOnlyList<LevantamentoUploadRowError>? Errors);

    public async Task<LevantamentoCalculationResult> CalculateAsync(
        string orderId, DateOnly? asOf, string? performedBy, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            new Uri($"levantamento/{orderId}/calculate", UriKind.Relative),
            new CalculateRequest(asOf, performedBy),
            ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<LevantamentoCalculationResult>(ct)
            ?? throw new InvalidOperationException("POST levantamento/calculate returned an empty response.");
    }

    public async Task<LevantamentoValidationResult> ValidateAsync(
        string orderId, string? performedBy, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            new Uri($"levantamento/{orderId}/validate", UriKind.Relative),
            new ValidateRequest(performedBy),
            ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<LevantamentoValidationResult>(ct)
            ?? throw new InvalidOperationException("POST levantamento/validate returned an empty response.");
    }

    public async Task<LevantamentoMemoriaCalculoDto> GetMemoriaCalculoAsync(string orderId, CancellationToken ct = default)
    {
        var response = await http.GetAsync(new Uri($"levantamento/{orderId}/memoria-calculo", UriKind.Relative), ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<LevantamentoMemoriaCalculoDto>(ct)
            ?? throw new InvalidOperationException("GET levantamento/memoria-calculo returned an empty response.");
    }
}

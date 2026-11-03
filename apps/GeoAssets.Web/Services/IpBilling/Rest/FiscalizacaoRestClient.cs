using System.Net;
using System.Net.Http.Json;
using GeoAssets.IpBilling.Fiscalizacao;
using GeoAssets.IpBilling.Workflow;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Web.Services.IpBilling.Rest;

/// <summary>
/// <see cref="IFiscalizacaoRestClient"/> backed by <c>GeoAssets.IpBilling.Server</c>'s
/// <c>/api/ip-billing/orders</c> and <c>/api/ip-billing/fiscalizacao/*</c> endpoints (XD01-183).
/// </summary>
internal sealed class FiscalizacaoRestClient(HttpClient http) : IFiscalizacaoRestClient
{
    private sealed record CreateOrderRequest(
        string OrderTypeId, string Title, string MunicipioIbgeCode, string Competencia,
        string? CodigoInstalacao, string? CreatedBy);

    private sealed record CalculateRequest(string? PerformedBy);

    private sealed record ValidateRequest(string? PerformedBy);

    public async Task<ServiceOrder> CreateAsync(
        string municipioIbgeCode, string competencia, string? createdBy, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            "orders",
            new CreateOrderRequest(IpBillingOrderTypes.FiscalizacaoId, $"Fiscalização {competencia}", municipioIbgeCode, competencia, null, createdBy),
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

        var response = await http.PostAsync(new Uri($"fiscalizacao/{orderId}/upload", UriKind.Relative), content, ct);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowUploadRejectedAsync(response, ct);
        }

        var body = await response.Content.ReadFromJsonAsync<UploadResponse>(ct)
            ?? throw new InvalidOperationException("POST fiscalizacao/upload returned an empty response.");
        return body.LineCount;
    }

    private sealed record UploadResponse(int LineCount);

    private static async Task ThrowUploadRejectedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var errorBody = await response.Content.ReadFromJsonAsync<UploadErrorBody>(ct);
            if (errorBody?.Errors is { Count: > 0 } errors)
            {
                throw new FiscalizacaoUploadRejectedException(
                    $"Fiscalização upload contains {errors.Count} invalid row(s).", errors);
            }

            throw new FiscalizacaoUploadRejectedException(errorBody?.Message ?? "Upload was rejected.", []);
        }

        response.EnsureSuccessStatusCode();
    }

    private sealed record UploadErrorBody(string? Message, IReadOnlyList<FiscalizacaoUploadRowError>? Errors);

    public async Task<FiscalizacaoCalculationResult> CalculateAsync(
        string orderId, string? performedBy, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            new Uri($"fiscalizacao/{orderId}/calculate", UriKind.Relative),
            new CalculateRequest(performedBy),
            ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<FiscalizacaoCalculationResult>(ct)
            ?? throw new InvalidOperationException("POST fiscalizacao/calculate returned an empty response.");
    }

    public async Task<FiscalizacaoValidationResult> ValidateAsync(
        string orderId, string? performedBy, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            new Uri($"fiscalizacao/{orderId}/validate", UriKind.Relative),
            new ValidateRequest(performedBy),
            ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<FiscalizacaoValidationResult>(ct)
            ?? throw new InvalidOperationException("POST fiscalizacao/validate returned an empty response.");
    }
}

using System.Net;
using System.Net.Http.Json;
using GeoAssets.IpBilling.Oficio;
using GeoAssets.IpBilling.Workflow;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Web.Services.IpBilling.Rest;

/// <summary>
/// <see cref="IOficioRestClient"/> backed by <c>GeoAssets.IpBilling.Server</c>'s
/// <c>/api/ip-billing/orders</c> and <c>/api/ip-billing/oficio/*</c> endpoints (XD01-186).
/// </summary>
internal sealed class OficioRestClient(HttpClient http) : IOficioRestClient
{
    private sealed record CreateOrderRequest(
        string OrderTypeId, string Title, string MunicipioIbgeCode, string Competencia,
        string? CodigoInstalacao, string? CreatedBy);

    private sealed record CalculateRequest(string? PerformedBy);

    private sealed record ValidateRequest(string? PerformedBy);

    private sealed record SupersedeRequest(string? PerformedBy);

    public async Task<ServiceOrder> CreateAsync(
        string municipioIbgeCode, string competencia, string? createdBy, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            "orders",
            new CreateOrderRequest(IpBillingOrderTypes.OficioId, $"Ofício {competencia}", municipioIbgeCode, competencia, null, createdBy),
            ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ServiceOrder>(ct)
            ?? throw new InvalidOperationException("POST orders returned an empty response.");
    }

    public async Task<int> UploadAsync(
        string orderId, string? numeroPrefeitura, Stream fileStream, string fileName, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(fileStream);
        content.Add(streamContent, "file", fileName);

        var query = string.IsNullOrEmpty(numeroPrefeitura)
            ? string.Empty
            : $"?numeroPrefeitura={Uri.EscapeDataString(numeroPrefeitura)}";

        var response = await http.PostAsync(new Uri($"oficio/{orderId}/upload{query}", UriKind.Relative), content, ct);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowUploadRejectedAsync(response, ct);
        }

        var body = await response.Content.ReadFromJsonAsync<UploadResponse>(ct)
            ?? throw new InvalidOperationException("POST oficio/upload returned an empty response.");
        return body.LineCount;
    }

    private sealed record UploadResponse(int LineCount);

    private static async Task ThrowUploadRejectedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var conflict = await response.Content.ReadFromJsonAsync<DuplicateNumeroBody>(ct);
            if (conflict is not null)
            {
                throw new OficioDuplicateNumeroRejectedException(
                    conflict.Message, conflict.MunicipioId, conflict.NumeroPrefeitura, conflict.ExistingServiceOrderId);
            }

            response.EnsureSuccessStatusCode();
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var errorBody = await response.Content.ReadFromJsonAsync<UploadErrorBody>(ct);
            if (errorBody?.Errors is { Count: > 0 } errors)
            {
                throw new OficioUploadRejectedException(
                    $"Ofício upload contains {errors.Count} invalid row(s).", errors);
            }

            throw new OficioUploadRejectedException(errorBody?.Message ?? "Upload was rejected.", []);
        }

        response.EnsureSuccessStatusCode();
    }

    private sealed record DuplicateNumeroBody(
        string Message, Guid MunicipioId, string NumeroPrefeitura, string ExistingServiceOrderId);

    private sealed record UploadErrorBody(string? Message, IReadOnlyList<OficioUploadRowError>? Errors);

    public async Task<OficioCalculationResult> CalculateAsync(
        string orderId, string? performedBy, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            new Uri($"oficio/{orderId}/calculate", UriKind.Relative),
            new CalculateRequest(performedBy),
            ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<OficioCalculationResult>(ct)
            ?? throw new InvalidOperationException("POST oficio/calculate returned an empty response.");
    }

    public async Task<OficioValidationResult> ValidateAsync(
        string orderId, string? performedBy, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            new Uri($"oficio/{orderId}/validate", UriKind.Relative),
            new ValidateRequest(performedBy),
            ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<OficioValidationResult>(ct)
            ?? throw new InvalidOperationException("POST oficio/validate returned an empty response.");
    }

    public async Task SupersedeAsync(string orderId, string? performedBy, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            new Uri($"oficio/{orderId}/supersede", UriKind.Relative),
            new SupersedeRequest(performedBy),
            ct);
        response.EnsureSuccessStatusCode();
    }
}

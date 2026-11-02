using GeoAssets.Workflow.Orders;

namespace GeoAssets.IpBilling.Levantamento;

/// <summary>
/// Client-side gateway to <c>GeoAssets.IpBilling.Server</c>'s <c>/api/ip-billing/levantamento/*</c>
/// and <c>/api/ip-billing/orders</c> endpoints (XD01-180) — the Blazor-facing counterpart of
/// <c>ILevantamentoCalculationService</c>, shaped around what the upload/detail/memória-de-cálculo
/// pages (XD01-181, in <c>GeoAssets.Shared</c>) actually call. Lives here (not in
/// <c>GeoAssets.Web</c>, which implements it) because <c>GeoAssets.Shared</c> components inject
/// it and cannot reference <c>GeoAssets.Web</c> — that dependency only runs the other way.
/// </summary>
public interface ILevantamentoRestClient
{
    /// <summary>Creates a new Levantamento <c>ServiceOrder</c> (<c>Carregado</c>, no rows yet).</summary>
    Task<ServiceOrder> CreateAsync(
        string municipioIbgeCode, string competencia, string? createdBy, CancellationToken ct = default);

    /// <summary>Uploads a raw .xlsx stream — server-side ClosedXML parsing, structured row-level
    /// errors on failure (surfaced as <see cref="LevantamentoUploadRejectedException"/>).</summary>
    Task<int> UploadAsync(string orderId, Stream fileStream, string fileName, CancellationToken ct = default);

    Task<LevantamentoCalculationResult> CalculateAsync(
        string orderId, DateOnly? asOf, string? performedBy, CancellationToken ct = default);

    Task<LevantamentoValidationResult> ValidateAsync(
        string orderId, string? performedBy, CancellationToken ct = default);

    Task<LevantamentoMemoriaCalculoDto> GetMemoriaCalculoAsync(string orderId, CancellationToken ct = default);
}

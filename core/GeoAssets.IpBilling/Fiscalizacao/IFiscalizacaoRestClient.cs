using GeoAssets.Workflow.Orders;

namespace GeoAssets.IpBilling.Fiscalizacao;

/// <summary>
/// Client-side gateway to <c>GeoAssets.IpBilling.Server</c>'s <c>/api/ip-billing/orders</c> and
/// <c>/api/ip-billing/fiscalizacao/*</c> endpoints (XD01-183) — the Blazor-facing counterpart of
/// <see cref="IFiscalizacaoCalculationService"/>, shaped around what the upload/detail pages
/// (XD01-184, in <c>GeoAssets.Shared</c>) actually call. Mirrors
/// <c>Levantamento.ILevantamentoRestClient</c>'s shape, minus a memória-de-cálculo accessor —
/// there is no such endpoint for Fiscalização (XD01-183's own decision: Calculate/Validate
/// already return the full totals, and there's no separate UI-facing read model). Lives here
/// (not in <c>GeoAssets.Web</c>, which implements it) because <c>GeoAssets.Shared</c> components
/// inject it and cannot reference <c>GeoAssets.Web</c>.
/// </summary>
public interface IFiscalizacaoRestClient
{
    /// <summary>Creates a new Fiscalização <c>ServiceOrder</c> (<c>Carregado</c>, no rows yet).</summary>
    Task<ServiceOrder> CreateAsync(
        string municipioIbgeCode, string competencia, string? createdBy, CancellationToken ct = default);

    /// <summary>Uploads a raw .xlsx stream — server-side ClosedXML parsing, structured row-level
    /// errors on failure (surfaced as <see cref="FiscalizacaoUploadRejectedException"/>).</summary>
    Task<int> UploadAsync(string orderId, Stream fileStream, string fileName, CancellationToken ct = default);

    Task<FiscalizacaoCalculationResult> CalculateAsync(
        string orderId, string? performedBy, CancellationToken ct = default);

    Task<FiscalizacaoValidationResult> ValidateAsync(
        string orderId, string? performedBy, CancellationToken ct = default);
}

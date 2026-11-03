using GeoAssets.Workflow.Orders;

namespace GeoAssets.IpBilling.Oficio;

/// <summary>
/// Client-side gateway to <c>GeoAssets.IpBilling.Server</c>'s <c>/api/ip-billing/orders</c> and
/// <c>/api/ip-billing/oficio/*</c> endpoints (XD01-186) — the Blazor-facing counterpart of
/// <see cref="IOficioCalculationService"/>, shaped around what the upload/detail pages
/// (XD01-187, in <c>GeoAssets.Shared</c>) actually call. Mirrors
/// <c>Fiscalizacao.IFiscalizacaoRestClient</c>'s shape, plus <see cref="SupersedeAsync"/> for
/// the duplicate-número correction path. Lives here (not in <c>GeoAssets.Web</c>, which
/// implements it) because <c>GeoAssets.Shared</c> components inject it and cannot reference
/// <c>GeoAssets.Web</c>.
/// </summary>
public interface IOficioRestClient
{
    /// <summary>Creates a new Ofício <c>ServiceOrder</c> (<c>Carregado</c>, no rows yet).</summary>
    Task<ServiceOrder> CreateAsync(
        string municipioIbgeCode, string competencia, string? createdBy, CancellationToken ct = default);

    /// <summary>Uploads a raw .xlsx stream — server-side ClosedXML parsing, structured row-level
    /// errors on failure (<see cref="OficioUploadRejectedException"/>), or a 409 Conflict if
    /// <paramref name="numeroPrefeitura"/> already belongs to another active order
    /// (<see cref="OficioDuplicateNumeroRejectedException"/>).</summary>
    Task<int> UploadAsync(
        string orderId, string? numeroPrefeitura, Stream fileStream, string fileName, CancellationToken ct = default);

    Task<OficioCalculationResult> CalculateAsync(
        string orderId, string? performedBy, CancellationToken ct = default);

    Task<OficioValidationResult> ValidateAsync(
        string orderId, string? performedBy, CancellationToken ct = default);

    /// <summary>Releases this order's claim on its current número, freeing it for a corrective
    /// re-upload on a different order.</summary>
    Task SupersedeAsync(string orderId, string? performedBy, CancellationToken ct = default);
}

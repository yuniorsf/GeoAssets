namespace GeoAssets.FieldService.Server;

/// <summary>
/// Request body for the SAP WeBUY clearing-package export endpoint (XD01-255). <c>TenantSecret</c>
/// carried in the body rather than a query string — same placement as
/// <c>EspelhoDeMedicaoDossierRequest</c> — since a GET query string would end up logged in access
/// logs/browser history. <c>PoNumber</c>/<c>OutlineAgreementNumber</c>/<c>EvidenceDeepLinkBaseUrl</c>
/// are SAP-side/environment values the caller supplies per call — no config convention for them
/// exists yet on this host (see <c>SapWeBuyClearingPackageRequest.PoNumber</c>'s own XML doc).
/// </summary>
internal sealed record SapWeBuyExportClearingPackageApiRequest(
    string TenantSecret,
    string PoNumber,
    string OutlineAgreementNumber,
    string EvidenceDeepLinkBaseUrl,
    decimal RetentionRate = 0m);

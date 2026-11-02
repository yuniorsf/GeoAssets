namespace GeoAssets.FieldService.Server;

/// <summary>
/// Request body for the Espelho de Medição dossier endpoint (XD01-249). <c>TenantSecret</c>
/// carried in the body rather than a query string — same placement as
/// <c>VerifyEvidenceRequest</c> — since a secret in a GET query string would end up logged in
/// access logs/browser history; this is why the endpoint is a POST despite being read-only.
/// </summary>
internal sealed record EspelhoDeMedicaoDossierRequest(string TenantSecret, decimal RetentionRate = 0m);

namespace GeoAssets.IpBilling.Server;

/// <summary>
/// Payload for <c>POST /api/ip-billing/municipio-grants/sync</c> (XD01-177; on this host since XD01-385) — primitive
/// fields only (no <c>GeoAssets.IpBilling.Models.Municipio</c> reference), the same shape the endpoint had in
/// <c>GeoAssets.Server</c>, so callers only change the URL.
/// </summary>
internal sealed record MunicipioGrantSyncRequest(
    string IbgeCode,
    Guid DistribuidoraOrganizationId,
    Guid? PrefeituraOrganizationId,
    Guid? PreviousPrefeituraOrganizationId,
    string GrantedBy);

namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Payload for <c>POST /api/identity/municipio-grants/sync</c> (XD01-177) — primitive fields
/// only (no <c>GeoAssets.IpBilling.Models.Municipio</c> reference), so a future caller in
/// <c>GeoAssets.IpBilling.Server</c> can reference this type across the same architectural
/// boundary <see cref="MunicipioGrantRevokeRequest"/> respects.
/// </summary>
public sealed record MunicipioGrantSyncRequest(
    string IbgeCode,
    Guid DistribuidoraOrganizationId,
    Guid? PrefeituraOrganizationId,
    Guid? PreviousPrefeituraOrganizationId,
    string GrantedBy);

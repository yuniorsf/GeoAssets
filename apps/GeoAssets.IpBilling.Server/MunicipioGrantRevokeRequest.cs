namespace GeoAssets.IpBilling.Server;

/// <summary>Payload for <c>POST /api/ip-billing/municipio-grants/revoke</c> — see
/// <see cref="MunicipioGrantSyncRequest"/>'s doc comment for why these fields are primitives.</summary>
internal sealed record MunicipioGrantRevokeRequest(
    string IbgeCode,
    Guid DistribuidoraOrganizationId,
    Guid? PrefeituraOrganizationId);

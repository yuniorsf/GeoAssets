namespace GeoAssets.Identity.Authorization.Services;

/// <summary>Payload for <c>POST /api/identity/municipio-grants/revoke</c> — see
/// <see cref="MunicipioGrantSyncRequest"/>'s doc comment for why these fields are primitives.</summary>
public sealed record MunicipioGrantRevokeRequest(
    string IbgeCode,
    Guid DistribuidoraOrganizationId,
    Guid? PrefeituraOrganizationId);

namespace GeoAssets.IpBilling.GrantSync;

/// <summary>
/// One grant change a município save calls for (XD01-395), as <see cref="MunicipioGrantSyncPlanner"/> plans it and
/// <see cref="IMunicipioGrantSyncQueue"/> queues it.
/// <list type="bullet">
///   <item><see cref="MunicipioGrantSyncKind.Sync"/>: upsert the grant of (<see cref="PrefeituraOrganizationId"/>,
///   <see cref="DistribuidoraOrganizationId"/>, <see cref="IbgeCode"/>). <see cref="PreviousPrefeituraOrganizationId"/> is
///   the old Prefeitura when the link changed to another one (its grant is revoked first). <see cref="PreviousIbgeCode"/> and
///   <see cref="PreviousDistribuidoraOrganizationId"/> are the old grant key's when the IBGE code or the Distribuidora
///   changed under an existing Prefeitura link (that old key's grant is revoked first), null otherwise.</item>
///   <item><see cref="MunicipioGrantSyncKind.Revoke"/>: revoke the grant of (<see cref="PrefeituraOrganizationId"/>,
///   <see cref="DistribuidoraOrganizationId"/>, <see cref="IbgeCode"/>) — the key as it was before the save.</item>
/// </list>
/// </summary>
public sealed record MunicipioGrantChange(
    Guid MunicipioId,
    MunicipioGrantSyncKind Kind,
    string IbgeCode,
    Guid DistribuidoraOrganizationId,
    Guid? PrefeituraOrganizationId,
    Guid? PreviousPrefeituraOrganizationId = null,
    string? PreviousIbgeCode = null,
    Guid? PreviousDistribuidoraOrganizationId = null);

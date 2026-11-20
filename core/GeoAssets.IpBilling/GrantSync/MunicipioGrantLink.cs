using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.GrantSync;

/// <summary>
/// The fields of a <see cref="Municipio"/> its Prefeitura grant is keyed on (XD01-395): the grant is
/// <c>(PrefeituraOrganizationId → DistribuidoraOrganizationId, ScopeId = IbgeCode)</c>. Taken before and after a save, so
/// <see cref="MunicipioGrantSyncPlanner"/> can compare them.
/// </summary>
public readonly record struct MunicipioGrantLink(
    Guid MunicipioId,
    string IbgeCode,
    Guid DistribuidoraOrganizationId,
    Guid? PrefeituraOrganizationId)
{
    public static MunicipioGrantLink Of(Municipio municipio)
    {
        ArgumentNullException.ThrowIfNull(municipio);
        return new(municipio.Id, municipio.IbgeCode, municipio.DistribuidoraOrganizationId, municipio.PrefeituraOrganizationId);
    }
}

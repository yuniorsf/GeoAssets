using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;

namespace GeoAssets.Server.IpBilling;

/// <summary>
/// Keeps a município's Prefeitura↔Distribuidora <see cref="OrganizationGrant"/> in sync with its
/// Prefeitura-organization link (XD01-177) — the grant side of the scope-filtered authorization
/// model XD01-174 built. Lives directly in <c>GeoAssets.Server</c> as a feature-specific bridge
/// rather than inside a module, same convention as <see cref="ProjectService"/>; exposed to the
/// standalone <c>GeoAssets.IpBilling.Server</c> microservice over REST
/// (<see cref="IdentityRestApiExtensions"/>'s <c>MapMunicipioGrantsApi</c>), never a direct
/// assembly reference — <c>ServerArchitectureTests.ServerAssembly_ShouldNotReference_IpBillingVerticalAssemblies</c>
/// and <c>MessagingArchitectureTests.Invariant10_VerticalMicroserviceAndCoreServer_EnforceDatabaseAndAssemblyDecoupling</c>
/// enforce that <c>GeoAssets.Server</c> never references <c>GeoAssets.IpBilling</c> (or vice
/// versa), which is why this service takes the município's own fields as primitives rather than
/// the <c>GeoAssets.IpBilling.Models.Municipio</c> domain type.
///
/// One scoped grant per município, covering every GEOS_FAT_IP resource kind for that município
/// (<c>ResourceType = null</c>, <c>ScopeId = ibgeCode</c>) — not a per-resource-kind grant, since
/// the Prefeitura's visibility into its own município's slice of the Distribuidora's data is a
/// single, coarse-grained concern (ip-billing:read), not something that needs splitting by
/// <c>GeoFeature</c> vs. <c>ServiceOrder</c>.
/// </summary>
public sealed class MunicipioProvisioningService(
    IOrganizationGrantRepository grantRepository,
    TimeProvider timeProvider)
{
    private const string IpBillingReadAction = "ip-billing:read";

    /// <summary>
    /// Call after every município save. Idempotent: a second call with the same
    /// <paramref name="prefeituraOrganizationId"/> and the same
    /// <paramref name="previousPrefeituraOrganizationId"/> creates no duplicate grant. If the
    /// Prefeitura link changed to a different organization (or was cleared), the previous
    /// organization's grant for this município is revoked (<see cref="OrganizationGrant.IsActive"/>
    /// set to <c>false</c> — this repository has no hard-delete, matching its own contract).
    /// </summary>
    public async Task SyncGrantAsync(
        string ibgeCode,
        Guid distribuidoraOrganizationId,
        Guid? prefeituraOrganizationId,
        Guid? previousPrefeituraOrganizationId,
        string grantedBy,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ibgeCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(grantedBy);

        if (previousPrefeituraOrganizationId is Guid previousOrgId &&
            previousOrgId != prefeituraOrganizationId)
        {
            await RevokeAsync(previousOrgId, distribuidoraOrganizationId, ibgeCode, ct).ConfigureAwait(false);
        }

        if (prefeituraOrganizationId is not Guid newOrgId)
            return;

        var existing = await FindActiveScopedGrantAsync(
            newOrgId, distribuidoraOrganizationId, ibgeCode, ct).ConfigureAwait(false);
        if (existing is not null)
            return;

        await grantRepository.AddAsync(new OrganizationGrant
        {
            GranteeOrganizationId = newOrgId,
            ResourceOrganizationId = distribuidoraOrganizationId,
            ResourceType = null,
            ScopeId = ibgeCode,
            AllowedActions = [IpBillingReadAction],
            GrantedBy = grantedBy,
            GrantedAt = timeProvider.GetUtcNow().UtcDateTime,
            IsActive = true,
        }, ct).ConfigureAwait(false);
        await grantRepository.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Revokes the município's scoped grant, if any — called when a município is
    /// deleted so a stale active grant doesn't outlive the reference row it was provisioned for.</summary>
    public async Task RevokeAsync(
        string ibgeCode, Guid distribuidoraOrganizationId, Guid? prefeituraOrganizationId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ibgeCode);
        if (prefeituraOrganizationId is Guid orgId)
            await RevokeAsync(orgId, distribuidoraOrganizationId, ibgeCode, ct).ConfigureAwait(false);
    }

    private async Task RevokeAsync(Guid granteeOrganizationId, Guid resourceOrganizationId, string scopeId, CancellationToken ct)
    {
        var grant = await FindActiveScopedGrantAsync(granteeOrganizationId, resourceOrganizationId, scopeId, ct)
            .ConfigureAwait(false);
        if (grant is null)
            return;

        grant.IsActive = false;
        await grantRepository.UpdateAsync(grant, ct).ConfigureAwait(false);
        await grantRepository.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<OrganizationGrant?> FindActiveScopedGrantAsync(
        Guid granteeOrganizationId, Guid resourceOrganizationId, string scopeId, CancellationToken ct)
    {
        var activeGrants = await grantRepository
            .GetActiveGrantsAsync(granteeOrganizationId, resourceOrganizationId, ct)
            .ConfigureAwait(false);

        return activeGrants.FirstOrDefault(g => string.Equals(g.ScopeId, scopeId, StringComparison.Ordinal));
    }
}

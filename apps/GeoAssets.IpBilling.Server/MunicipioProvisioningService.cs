using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Workflow.Rest;

namespace GeoAssets.IpBilling.Server;

/// <summary>
/// Keeps a município's Prefeitura↔Distribuidora <see cref="OrganizationGrant"/> in sync with its Prefeitura-organization
/// link (XD01-177) — the grant side of the scope-filtered authorization model XD01-174 built. This vertical's own since
/// XD01-385 (ADR-001 Phase 7): it lived in <c>GeoAssets.Server</c> as a bridge module and now provisions the grant through
/// the kernel's generic organization-grant API (<see cref="IOrganizationGrantClient"/>, as <c>GeoAssets.ServiceCaller</c>),
/// so the kernel knows nothing of municípios. It takes the município's own fields as primitives, as before.
///
/// One scoped grant per município, covering every GEOS_FAT_IP resource kind for that município
/// (<c>ResourceType = null</c>, <c>ScopeId = ibgeCode</c>) — not a per-resource-kind grant, since the Prefeitura's
/// visibility into its own município's slice of the Distribuidora's data is a single, coarse-grained concern
/// (ip-billing:read), not something that needs splitting by <c>GeoFeature</c> vs. <c>ServiceOrder</c>.
/// </summary>
internal sealed class MunicipioProvisioningService(IOrganizationGrantClient grants)
{
    private const string IpBillingReadAction = "ip-billing:read";

    /// <summary>
    /// Call after every município save. Idempotent: a second call with the same <paramref name="prefeituraOrganizationId"/>
    /// and the same <paramref name="previousPrefeituraOrganizationId"/> creates no duplicate grant (the kernel's upsert
    /// leaves a matching grant unchanged). If the Prefeitura link changed to a different organization (or was cleared), the
    /// previous organization's grant for this município is revoked (deactivated — the kernel never hard-deletes a grant).
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
            await grants.RevokeAsync(Key(previousOrgId, distribuidoraOrganizationId, ibgeCode), ct).ConfigureAwait(false);
        }

        if (prefeituraOrganizationId is not Guid newOrgId)
            return;

        await grants.UpsertAsync(
            new OrganizationGrantUpsertRequest(
                Key(newOrgId, distribuidoraOrganizationId, ibgeCode),
                [IpBillingReadAction],
                RequiredRole: null,
                ExpiresAt: null,
                grantedBy),
            ct).ConfigureAwait(false);
    }

    /// <summary>Revokes the município's scoped grant, if any — called when a município is
    /// deleted so a stale active grant doesn't outlive the reference row it was provisioned for.</summary>
    public async Task RevokeAsync(
        string ibgeCode, Guid distribuidoraOrganizationId, Guid? prefeituraOrganizationId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ibgeCode);
        if (prefeituraOrganizationId is Guid orgId)
            await grants.RevokeAsync(Key(orgId, distribuidoraOrganizationId, ibgeCode), ct).ConfigureAwait(false);
    }

    private static OrganizationGrantKey Key(Guid granteeOrganizationId, Guid resourceOrganizationId, string ibgeCode) =>
        new(granteeOrganizationId, resourceOrganizationId, ResourceType: null, ScopeId: ibgeCode);
}

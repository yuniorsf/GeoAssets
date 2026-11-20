using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// The active, unexpired organization grants held by the caller's own organization (XD01-382): what
/// <see cref="ServerWorkflowPrincipalFactory"/> reads into <c>WorkflowPrincipal.OrgGrants</c> on a host that has no
/// identity database of its own and asks the kernel instead.
/// </summary>
public interface IOwnOrganizationGrantSource
{
    /// <summary>The grants <paramref name="granteeOrganizationId"/> — the caller's own organization — holds; empty when none.</summary>
    Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(Guid granteeOrganizationId, CancellationToken ct);
}

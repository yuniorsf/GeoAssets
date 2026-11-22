namespace GeoAssets.Workflow.Persistence;

/// <summary>Reads <see cref="MirroredEntitlements"/> for a caller (XD01-328).</summary>
public interface IMirroredCrossOrganizationEntitlements
{
    /// <summary>
    /// The grants mirrored for <paramref name="granteeOrganizationId"/> that are active and unexpired now, and what the user
    /// whose identity-provider id is <paramref name="externalObjectId"/> holds on other organizations. An empty organization
    /// holds no grant; an empty id holds no role.
    /// </summary>
    Task<MirroredEntitlements> GetAsync(Guid granteeOrganizationId, string externalObjectId, CancellationToken ct = default);
}

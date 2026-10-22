namespace GeoAssets.Identity.Authorization.Models;

/// <summary>
/// Grants one organization ("grantee") specific access into another organization's
/// ("resource") owned resources — the exception mechanism to the default
/// same-organization-only visibility implied by
/// <see cref="GeoAssets.Core.Interfaces.IOrgOwnedResource"/>.
/// </summary>
public sealed class OrganizationGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The organization being granted access.</summary>
    public Guid GranteeOrganizationId { get; set; }

    /// <summary>The organization whose resources are being made accessible.</summary>
    public Guid ResourceOrganizationId { get; set; }

    /// <summary>
    /// Restricts the grant to one resource kind (e.g. "ServiceOrder", "GeoFeature").
    /// Null means the grant applies to all resource kinds.
    /// </summary>
    public string? ResourceType { get; set; }

    /// <summary>
    /// Restricts the grant to resources belonging to one named subset within
    /// <see cref="ResourceType"/> — e.g. one município's worth of a Distribuidora's
    /// <c>GeoFeature</c>s, not everything it owns. Null (every grant created before this field
    /// existed, and any grant that doesn't need this) means "ungated within
    /// <see cref="ResourceType"/>" — the original, unaffected behavior. Only resources
    /// implementing <c>IScopeableResource</c> can be matched against a non-null value here;
    /// other resource types simply cannot be granted at this granularity (XD01-174).
    /// </summary>
    public string? ScopeId { get; set; }

    /// <summary>
    /// Permission codes (matching <see cref="AppPermission.Code"/> format, e.g.
    /// "serviceorders:view") the grantee organization is allowed to perform on the
    /// resource organization's resources. Empty means the grant authorizes nothing.
    /// </summary>
    public List<string> AllowedActions { get; set; } = [];

    /// <summary>
    /// Restricts the grant to grantee-organization users holding this role.
    /// Null means any grantee-organization user qualifies.
    /// </summary>
    public string? RequiredRole { get; set; }

    /// <summary>Null means the grant never expires.</summary>
    public DateTime? ExpiresAt { get; set; }

    public string   GrantedBy { get; set; } = string.Empty;
    public DateTime GrantedAt { get; set; }

    public bool IsActive { get; set; } = true;
}

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
    public List<string> AllowedActions { get; init; } = [];

    /// <summary>
    /// Restricts the grant to grantee-organization users holding this role.
    /// Null means any grantee-organization user qualifies. Matched against the user's home
    /// roles, or — when <see cref="RequiresScopedRole"/> is set — against the roles the user
    /// holds on <see cref="ResourceOrganizationId"/>.
    /// </summary>
    public string? RequiredRole { get; set; }

    /// <summary>
    /// How a grantee-organization user qualifies for an action this grant lists (XD01-326).
    /// False (every grant created before this field existed, and the default): by holding the
    /// action's permission at home, the original organization-wide behavior. True: only by
    /// holding it through a role assigned on <see cref="ResourceOrganizationId"/>
    /// (<c>UserRole.OrganizationId</c>, XD01-325) — home roles and home permissions count for
    /// nothing, and <see cref="RequiredRole"/> is matched against the roles held there.
    /// </summary>
    public bool RequiresScopedRole { get; set; }

    /// <summary>
    /// Restricts a service-order grant to orders of these order types (XD01-326), compared
    /// ignoring case. Empty (every grant created before this field existed, and the default)
    /// means every order type. Only meaningful for <see cref="ResourceType"/> <c>"ServiceOrder"</c>:
    /// a grant listing order types never matches a resource that has none.
    /// </summary>
    public List<string> OrderTypeIds { get; init; } = [];

    /// <summary>Null means the grant never expires.</summary>
    public DateTime? ExpiresAt { get; set; }

    public string   GrantedBy { get; set; } = string.Empty;
    public DateTime GrantedAt { get; set; }

    public bool IsActive { get; set; } = true;
}

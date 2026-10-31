namespace GeoAssets.Identity.Authentication;

/// <summary>
/// Snapshot of the currently authenticated principal, populated from the IdP's JWT claims via
/// <see cref="ClaimMapping"/> (XD01-48). <see cref="ExternalObjectId"/>/<see cref="ExternalRoles"/>
/// use vendor-neutral names — the values come from whichever IdP is actually configured, not
/// necessarily Azure AD (XD01-51).
///
/// <see cref="OrganizationId"/> (XD01-200) defaults to <see cref="Guid.Empty"/> — the existing
/// single-tenant sentinel — when the configured <see cref="ClaimMapping.OrganizationIdClaimTypes"/>
/// find no matching claim (e.g. <see cref="ClaimMapping.EntraDefault"/>, which doesn't set one),
/// so hosts that haven't configured an organization claim are unaffected.
/// </summary>
public sealed record CurrentUser(
    string ExternalObjectId,
    string Email,
    string DisplayName,
    IReadOnlyList<string> ExternalRoles,
    Guid OrganizationId = default);

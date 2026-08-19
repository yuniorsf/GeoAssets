namespace GeoAssets.Identity.Authentication;

/// <summary>
/// Snapshot of the currently authenticated principal, populated from the IdP's JWT claims via
/// <see cref="ClaimMapping"/> (XD01-48). <see cref="ExternalObjectId"/>/<see cref="ExternalRoles"/>
/// use vendor-neutral names — the values come from whichever IdP is actually configured, not
/// necessarily Azure AD (XD01-51).
/// </summary>
public sealed record CurrentUser(
    string             ExternalObjectId,
    string             Email,
    string             DisplayName,
    IReadOnlyList<string> ExternalRoles);

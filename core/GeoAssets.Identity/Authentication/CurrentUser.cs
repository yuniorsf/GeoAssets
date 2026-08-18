namespace GeoAssets.Identity.Authentication;

/// <summary>
/// Snapshot of the currently authenticated principal, populated from the IdP's JWT claims via
/// <see cref="ClaimMapping"/> (XD01-48) — <see cref="AzureObjectId"/>/<see cref="AzureRoles"/>
/// keep their Entra-era names for source compatibility, but the values themselves come from
/// whichever IdP is actually configured, not necessarily Azure AD.
/// </summary>
public sealed record CurrentUser(
    string             AzureObjectId,
    string             Email,
    string             DisplayName,
    IReadOnlyList<string> AzureRoles);

namespace GeoAssets.Identity.Authentication;

/// <summary>
/// Snapshot of the currently authenticated principal, populated from the Azure AD JWT claims.
///
/// Azure AD claim mappings used:
///   oid  (or objectidentifier) → <see cref="AzureObjectId"/>
///   preferred_username / upn   → <see cref="Email"/>
///   name                       → <see cref="DisplayName"/>
///   roles                      → <see cref="AzureRoles"/> (App Roles configured in Azure AD)
/// </summary>
public sealed record CurrentUser(
    string             AzureObjectId,
    string             Email,
    string             DisplayName,
    IReadOnlyList<string> AzureRoles);

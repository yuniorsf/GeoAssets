namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Admin write payload for a user. <c>Email</c>/<c>ExternalObjectId</c> are deliberately absent —
/// users are JIT-provisioned from the IdP on first login (see <c>AppUser</c>), so there is no
/// "create user" operation and identity fields stay immutable via this admin surface.
/// </summary>
public sealed record UserUpdateDto(
    string DisplayName,
    bool   IsActive,
    Guid?  OrganizationId);

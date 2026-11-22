namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// The roles the caller holds on one other organization (XD01-325) and the permission codes they grant there — one entry of
/// <see cref="CrossOrgEntitlementsDto.ScopedRoles"/> (XD01-329).
/// </summary>
public sealed record ScopedRoleEntitlementDto(
    Guid                  OrganizationId,
    string?               OrganizationName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

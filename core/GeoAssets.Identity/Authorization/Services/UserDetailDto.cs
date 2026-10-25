namespace GeoAssets.Identity.Authorization.Services;

public sealed record UserDetailDto(
    Guid                  Id,
    string                Email,
    string                DisplayName,
    bool                  IsActive,
    Guid?                 OrganizationId,
    DateTime              CreatedAt,
    DateTime?             LastLoginAt,
    IReadOnlyList<Guid>   RoleIds);

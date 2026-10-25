namespace GeoAssets.Identity.Authorization.Services;

public sealed record RoleDetailDto(
    Guid                Id,
    string              Name,
    string              Description,
    bool                IsBuiltIn,
    IReadOnlyList<Guid> PermissionIds);

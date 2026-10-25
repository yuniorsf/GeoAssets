namespace GeoAssets.Identity.Authorization.Services;

public sealed record PermissionDto(
    Guid   Id,
    string Code,
    string Resource,
    string Action,
    string Description);

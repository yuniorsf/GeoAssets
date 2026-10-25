namespace GeoAssets.Identity.Authorization.Services;

/// <summary>Admin write payload for a group (XD01-128).</summary>
public sealed record GroupWriteDto(
    string  Name,
    string? Description,
    Guid?   OrganizationId,
    bool    IsActive);

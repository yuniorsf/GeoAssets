namespace GeoAssets.Identity.Authorization.Services;

/// <summary>Admin write payload for an organization (XD01-128).</summary>
public sealed record OrganizationWriteDto(
    string  Name,
    string  Slug,
    string? Description,
    bool    IsActive);

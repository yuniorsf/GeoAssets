namespace GeoAssets.Identity.Authorization.Services;

/// <summary>Wire shape for an <c>Organization</c> row (XD01-128).</summary>
public sealed record OrganizationDto(
    Guid     Id,
    string   Name,
    string   Slug,
    string?  Description,
    bool     IsActive,
    DateTime CreatedAt);

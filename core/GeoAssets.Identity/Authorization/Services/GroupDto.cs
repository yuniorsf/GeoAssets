namespace GeoAssets.Identity.Authorization.Services;

/// <summary>Wire shape for an <c>AppGroup</c> row (XD01-128).</summary>
public sealed record GroupDto(
    Guid     Id,
    string   Name,
    string?  Description,
    Guid?    OrganizationId,
    bool     IsActive,
    DateTime CreatedAt);

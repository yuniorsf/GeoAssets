namespace GeoAssets.Identity.Authorization.Services;

public sealed record RoleSummaryDto(
    Guid   Id,
    string Name,
    string Description,
    bool   IsBuiltIn);

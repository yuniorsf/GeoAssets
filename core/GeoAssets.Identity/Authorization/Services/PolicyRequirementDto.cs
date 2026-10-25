using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Services;

public sealed record PolicyRequirementDto(RequirementType Type, string Value, string? ClaimValue);

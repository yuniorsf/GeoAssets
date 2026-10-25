namespace GeoAssets.Identity.Authorization.Services;

/// <summary>Wire shape for a caller's own <c>UserClaim</c> (XD01-87, self-service).</summary>
public sealed record UserClaimDto(Guid Id, string Type, string Value);

namespace GeoAssets.Identity.Authorization.Services;

/// <summary>Write payload to update an existing claim's value (XD01-87). <c>Type</c> is immutable once created.</summary>
public sealed record UserClaimUpdateDto(string Value);

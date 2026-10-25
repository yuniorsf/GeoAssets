namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Write payload to create a claim for the caller (XD01-87). <c>UserId</c> is deliberately
/// absent — the server always uses the caller's own id from their auth context, never a
/// client-supplied one.
/// </summary>
public sealed record UserClaimWriteDto(string Type, string Value);

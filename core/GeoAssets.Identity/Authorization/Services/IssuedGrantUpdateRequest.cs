namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Body of <c>PUT {prefix}/organizations/{orgId}/issued-grants/{grantId}</c> (XD01-411): the new rules of a grant the
/// organization issued. The grantee, resource type and scope are the grant's key and don't change: revoke the grant and
/// issue another to move it.
/// </summary>
public sealed record IssuedGrantUpdateRequest(IssuedGrantRules Rules);

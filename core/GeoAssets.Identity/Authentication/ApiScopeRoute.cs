namespace GeoAssets.Identity.Authentication;

/// <summary>One configured API route and the access-token scope a request under it carries (XD01-397).</summary>
public sealed record ApiScopeRoute(string PathPrefix, string Scope);

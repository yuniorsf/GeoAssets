namespace GeoAssets.Gateway;

/// <summary>
/// The caller a request's rate-limit bucket belongs to (XD01-398): its partition <paramref name="Key"/>
/// (<c>org:…</c>, <c>sub:…</c>, <c>ip:…</c> or <see cref="RateLimitPartitionKeyResolver.UnresolvedPartitionKey"/>), the
/// <paramref name="Source"/> that chose it (<c>org</c>|<c>sub</c>|<c>ip</c>|<c>unresolved</c>) and the token's
/// <paramref name="Validation"/> outcome. Only the source and the outcome are ever metric tags, never the key.
/// </summary>
internal sealed record PartitionCaller(string Key, string Source, GatewayTokenValidationOutcome Validation);

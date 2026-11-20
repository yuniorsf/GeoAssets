namespace GeoAssets.Gateway;

/// <summary>
/// The gateway's rate-limit policies (XD01-393), each its own partitioned token bucket with its own
/// <c>RateLimiting:&lt;policy&gt;</c> settings, so exhausting one never rejects a request of the other.
/// </summary>
internal enum GatewayRateLimitPolicy
{
    /// <summary>Every proxied API call — <c>RateLimiting:Api</c>.</summary>
    Api,

    /// <summary>The anonymous OGC map traffic (<see cref="GatewayRouteCatalog.WmsPath"/>, <see cref="GatewayRouteCatalog.WfsPath"/>) a pan issues by the dozen — <c>RateLimiting:Tiles</c>.</summary>
    Tiles,
}

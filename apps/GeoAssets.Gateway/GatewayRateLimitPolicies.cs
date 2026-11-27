namespace GeoAssets.Gateway;

/// <summary>Which <see cref="GatewayRateLimitPolicy"/> a request is limited by, and each policy's name (XD01-393).</summary>
internal static class GatewayRateLimitPolicies
{
    /// <summary>The paths of <see cref="GatewayRateLimitPolicy.Tiles"/>: the kernel catch-all's OGC routes and its tile endpoints (XD01-434) of <see cref="GatewayRouteCatalog"/>.</summary>
    public static readonly IReadOnlyList<string> TilePaths = [GatewayRouteCatalog.WmsPath, GatewayRouteCatalog.WfsPath, GatewayRouteCatalog.TilesPathPrefix];

    /// <summary><see cref="GatewayRateLimitPolicy.Tiles"/> for a <see cref="TilePaths"/> request (case-insensitive, like the route match), <see cref="GatewayRateLimitPolicy.Api"/> otherwise.</summary>
    public static GatewayRateLimitPolicy Classify(PathString path)
    {
        foreach (var tilePath in TilePaths)
        {
            if (path.StartsWithSegments(tilePath, StringComparison.OrdinalIgnoreCase)) return GatewayRateLimitPolicy.Tiles;
        }

        return GatewayRateLimitPolicy.Api;
    }

    /// <summary>The policy's name — its configuration section's lower-case form, the <c>policy</c> metric tag and the 429 body's <c>policy</c>.</summary>
    public static string ToName(this GatewayRateLimitPolicy policy) => policy switch
    {
        GatewayRateLimitPolicy.Api => "api",
        GatewayRateLimitPolicy.Tiles => "tiles",
        _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown rate-limit policy."),
    };
}

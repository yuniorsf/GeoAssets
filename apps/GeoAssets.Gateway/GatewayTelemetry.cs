namespace GeoAssets.Gateway;

/// <summary>The gateway's tracing sources and meter beyond the shared observability pipeline's (XD01-384, XD01-393, XD01-398).</summary>
internal static class GatewayTelemetry
{
    /// <summary>YARP's own activity source (the forwarder's proxy activity); the HttpClient span of each forward is the shared pipeline's.</summary>
    public const string YarpActivitySourceName = "Yarp.ReverseProxy";

    /// <summary>The gateway's own meter (XD01-393), exported by the shared pipeline's OTLP metrics exporter.</summary>
    public const string MeterName = "GeoAssets.Gateway";

    /// <summary>Requests answered 429 by the rate limiter (tag <see cref="PolicyTag"/>: <c>api</c>|<c>tiles</c>; never a partition key).</summary>
    public const string RateLimitedCounterName = "geoassets.gateway.rate_limited";

    /// <summary>
    /// Requests whose rate-limit caller was resolved (XD01-398), tagged <see cref="PolicyTag"/>, <see cref="SourceTag"/>
    /// and <see cref="ValidationTag"/> — never the partition key.
    /// </summary>
    public const string RateLimitPartitionCounterName = "geoassets.gateway.rate_limit_partition";

    /// <summary>The rate-limit policy tag of <see cref="RateLimitedCounterName"/> and <see cref="RateLimitPartitionCounterName"/>.</summary>
    public const string PolicyTag = "policy";

    /// <summary>What chose the bucket, a tag of <see cref="RateLimitPartitionCounterName"/>: <c>org</c>|<c>sub</c>|<c>ip</c>|<c>unresolved</c>.</summary>
    public const string SourceTag = "source";

    /// <summary>The token's validation outcome, a tag of <see cref="RateLimitPartitionCounterName"/>: <c>absent</c>|<c>valid</c>|<c>invalid</c>|<c>unavailable</c>|<c>unvalidated</c>.</summary>
    public const string ValidationTag = "validation";
}

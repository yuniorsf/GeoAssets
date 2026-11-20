namespace GeoAssets.Gateway;

/// <summary>The gateway's tracing sources and meter beyond the shared observability pipeline's (XD01-384, XD01-393).</summary>
internal static class GatewayTelemetry
{
    /// <summary>YARP's own activity source (the forwarder's proxy activity); the HttpClient span of each forward is the shared pipeline's.</summary>
    public const string YarpActivitySourceName = "Yarp.ReverseProxy";

    /// <summary>The gateway's own meter (XD01-393), exported by the shared pipeline's OTLP metrics exporter.</summary>
    public const string MeterName = "GeoAssets.Gateway";

    /// <summary>Requests answered 429 by the rate limiter (tag <see cref="PolicyTag"/>: <c>api</c>|<c>tiles</c>; never a partition key).</summary>
    public const string RateLimitedCounterName = "geoassets.gateway.rate_limited";

    /// <summary>The rate-limit policy tag of <see cref="RateLimitedCounterName"/>.</summary>
    public const string PolicyTag = "policy";
}

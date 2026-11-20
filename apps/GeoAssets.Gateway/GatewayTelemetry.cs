namespace GeoAssets.Gateway;

/// <summary>The gateway's tracing sources beyond the shared observability pipeline's (XD01-384).</summary>
internal static class GatewayTelemetry
{
    /// <summary>YARP's own activity source (the forwarder's proxy activity); the HttpClient span of each forward is the shared pipeline's.</summary>
    public const string YarpActivitySourceName = "Yarp.ReverseProxy";
}

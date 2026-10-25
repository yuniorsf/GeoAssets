namespace GeoAssets.Infrastructure.Observability;

public sealed class OtlpOptions
{
    /// <summary>
    /// OTLP collector/backend endpoint, e.g. <c>https://otlp.nr-data.net:4317</c>
    /// (New Relic, US region, gRPC) or <c>http://localhost:4317</c> for a local
    /// OpenTelemetry Collector. Leave empty to disable the OTLP exporter
    /// (useful for local dev).
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Wire protocol: <c>Grpc</c> (port 4317) or <c>HttpProtobuf</c> (port 4318).</summary>
    public string Protocol { get; set; } = "Grpc";

    /// <summary>
    /// Raw OTLP header string, e.g. <c>api-key=...</c> (New Relic license key).
    /// Override with environment variable <c>OTEL_EXPORTER_OTLP_HEADERS</c>,
    /// or set the <c>NEW_RELIC_LICENSE_KEY</c> environment variable to have it
    /// wired in automatically as <c>api-key=&lt;value&gt;</c>.
    /// </summary>
    public string Headers { get; set; } = string.Empty;
}

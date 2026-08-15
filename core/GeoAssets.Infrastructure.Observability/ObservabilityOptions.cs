namespace GeoAssets.Infrastructure.Observability;

/// <summary>
/// Top-level observability configuration.
/// Bind from <c>appsettings.json → "Observability"</c>.
///
/// <code>
/// "Observability": {
///   "ServiceName": "geoassets-api",
///   "ServiceVersion": "1.0.0",
///   "Otlp": {
///     "Endpoint": "https://otlp.nr-data.net:4317",
///     "Protocol": "Grpc",
///     "Headers": ""
///   },
///   "Sampling": { "RatioForProduction": 0.1 },
///   "Instrumentation": {
///     "EnableEFCore": true,
///     "EnableRuntime": true,
///     "EnableProcess": false,
///     "RecordExceptionOnHttpErrors": true
///   }
/// }
/// </code>
/// </summary>
public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary>Logical name of this service, emitted as the <c>service.name</c> resource attribute.</summary>
    public string ServiceName    { get; set; } = "geoassets";

    /// <summary>Semantic version of the deployed build.</summary>
    public string ServiceVersion { get; set; } = "1.0.0";

    public OtlpOptions            Otlp            { get; set; } = new();
    public SamplingOptions        Sampling        { get; set; } = new();
    public InstrumentationOptions Instrumentation { get; set; } = new();
}

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

public sealed class SamplingOptions
{
    /// <summary>
    /// Probability of sampling a trace in production (0.0–1.0).
    /// 1.0 = sample everything (good for dev/staging).
    /// 0.1 = sample 10 % (sensible default for high-traffic prod).
    /// </summary>
    public double RatioForProduction { get; set; } = 1.0;
}

public sealed class InstrumentationOptions
{
    /// <summary>Instrument EF Core commands as spans. Disable if the project has no EF dependency.</summary>
    public bool EnableEFCore { get; set; } = true;

    /// <summary>Collect .NET runtime metrics (GC, thread pool, JIT).</summary>
    public bool EnableRuntime { get; set; } = true;

    /// <summary>Collect process-level metrics (CPU, memory, handles).</summary>
    public bool EnableProcess { get; set; } = false;

    /// <summary>Record HTTP 4xx/5xx responses as span exceptions.</summary>
    public bool RecordExceptionOnHttpErrors { get; set; } = true;
}

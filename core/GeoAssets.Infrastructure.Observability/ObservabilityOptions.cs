namespace GeoAssets.Infrastructure.Observability;

/// <summary>
/// Top-level observability configuration.
/// Bind from <c>appsettings.json → "Observability"</c>.
///
/// <code>
/// "Observability": {
///   "ServiceName": "geoassets-api",
///   "ServiceVersion": "1.0.0",
///   "ExportEnabled": false,
///   "Otlp": {
///     "Endpoint": "https://otlp.nr-data.net:4317",
///     "Protocol": "Grpc",
///     "Headers": ""
///   },
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

    /// <summary>
    /// Master switch for sending telemetry out of the process. <c>false</c> (the default): no OTLP exporter is
    /// registered for traces, metrics or logs, whatever <see cref="OtlpOptions.Endpoint"/> and the vendor credentials
    /// (<c>NEW_RELIC_LICENSE_KEY</c>, <c>OTEL_EXPORTER_OTLP_HEADERS</c>) say — instrumentation still runs in process
    /// (spans, meters, trace ids in log scopes), it just reaches no backend. Turn it on explicitly, e.g.
    /// <c>Observability__ExportEnabled=true</c>.
    /// </summary>
    public bool ExportEnabled { get; set; }

    public OtlpOptions            Otlp            { get; set; } = new();
    public InstrumentationOptions Instrumentation { get; set; } = new();
}

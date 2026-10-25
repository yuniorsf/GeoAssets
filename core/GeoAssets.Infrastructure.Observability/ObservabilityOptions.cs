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
    public InstrumentationOptions Instrumentation { get; set; } = new();
}

namespace GeoAssets.Infrastructure.Observability;

/// <summary>
/// Top-level observability configuration.
/// Bind from <c>appsettings.json → "Observability"</c>.
///
/// <code>
/// "Observability": {
///   "ServiceName": "geoassets-api",
///   "ServiceVersion": "1.0.0",
///   "AzureMonitor": {
///     "ConnectionString": "InstrumentationKey=...;IngestionEndpoint=..."
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

    public AzureMonitorOptions    AzureMonitor    { get; set; } = new();
    public SamplingOptions        Sampling        { get; set; } = new();
    public InstrumentationOptions Instrumentation { get; set; } = new();
}

public sealed class AzureMonitorOptions
{
    /// <summary>
    /// Full Application Insights / Azure Monitor connection string.
    /// Override with environment variable <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c>
    /// (Azure App Service injects this automatically).
    /// Leave empty to disable the Azure Monitor exporter (useful for local dev).
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
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

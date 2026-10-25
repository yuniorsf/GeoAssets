using GeoAssets.Core.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace GeoAssets.Infrastructure.Observability;

/// <summary>
/// DI registration helpers for the GeoAssets observability layer.
///
/// <b>Typical usage (ASP.NET Core host)</b>
/// <code>
/// // Program.cs
/// builder.Services.AddGeoAssetsObservability(builder.Configuration);
///
/// // appsettings.json
/// {
///   "Observability": {
///     "ServiceName": "geoassets-api",
///     "ServiceVersion": "2.1.0",
///     "Otlp": {
///       "Endpoint": "https://otlp.nr-data.net:4317",
///       "Protocol": "Grpc"
///     },
///     "Instrumentation": { "EnableEFCore": true, "EnableRuntime": true }
///   }
/// }
/// </code>
///
/// <b>Architecture</b>
/// <code>
/// ILogger  ──┐
///             │  OpenTelemetry SDK  ──→  OTLP exporter  ──→  New Relic / Collector / any OTLP backend
/// Activity ──┤      (traces + metrics + logs)
///             │
/// Meter    ──┘
/// </code>
///
/// The exporter target is a config choice (<see cref="OtlpOptions.Endpoint"/>) —
/// swapping vendors (New Relic, Datadog, a local Collector) never requires a
/// code change.
/// </summary>
public static class ObservabilityServiceExtensions
{
    /// <summary>
    /// Adds OpenTelemetry instrumentation and the OTLP exporter.
    /// Also registers <see cref="GeoAssetsActivitySource"/> and
    /// <see cref="GeoAssetsMeter"/> as singletons for injection into
    /// application services.
    /// </summary>
    public static IServiceCollection AddGeoAssetsObservability(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var opts = ResolveOptions(configuration);
        Action<OtlpExporterOptions> configureOtlp = o => ConfigureOtlp(o, opts);

        // ── Domain singletons ─────────────────────────────────────────────────
        // Both registered via a factory (not a pre-built instance) so the DI container takes
        // ownership and disposes them — a singleton instance handed to AddSingleton(instance)
        // is never disposed by the container.
        services.AddSingleton(_ => new GeoAssetsActivitySource(opts.ServiceVersion));
        services.AddSingleton(_ => new GeoAssetsMeter(opts.ServiceVersion));

        // ── OpenTelemetry SDK ─────────────────────────────────────────────────
        var otelBuilder = services
            .AddOpenTelemetry()
            .ConfigureResource(b => ConfigureResource(b, opts));

        otelBuilder.WithTracing(tracing => ConfigureTracing(tracing, opts, configureOtlp));
        otelBuilder.WithMetrics(metrics => ConfigureMetrics(metrics, opts, configureOtlp));

        // ── ILogger → OpenTelemetry bridge ───────────────────────────────────
        // Ensures ILogger output is funnelled into the OTel pipeline and
        // therefore into the OTLP exporter below.  The bridge is always
        // registered; when Otlp.Endpoint is empty, logs go to the console only.
        services.AddLogging(logging => ConfigureLoggingBridge(logging, opts, configureOtlp));

        return services;
    }

    /// <summary>
    /// Binds <see cref="ObservabilityOptions"/> from configuration, then applies environment-
    /// variable overrides for OTLP headers. NEW_RELIC_LICENSE_KEY is the common case (New Relic's
    /// OTLP ingest authenticates via an `api-key` header); OTEL_EXPORTER_OTLP_HEADERS takes
    /// precedence if both are set, matching the OTel SDK convention.
    /// </summary>
    private static ObservabilityOptions ResolveOptions(IConfiguration configuration)
    {
        var opts = new ObservabilityOptions();
        configuration.GetSection(ObservabilityOptions.SectionName).Bind(opts);

        var newRelicKey = Environment.GetEnvironmentVariable("NEW_RELIC_LICENSE_KEY");
        if (!string.IsNullOrWhiteSpace(newRelicKey))
            opts.Otlp.Headers = $"api-key={newRelicKey}";

        var envHeaders = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_HEADERS");
        if (!string.IsNullOrWhiteSpace(envHeaders))
            opts.Otlp.Headers = envHeaders;

        return opts;
    }

    private static void ConfigureOtlp(OtlpExporterOptions o, ObservabilityOptions opts)
    {
        var otlpProtocol = opts.Otlp.Protocol.Equals("HttpProtobuf", StringComparison.OrdinalIgnoreCase)
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc;

        o.Endpoint = new Uri(opts.Otlp.Endpoint);
        o.Protocol = otlpProtocol;
        if (!string.IsNullOrWhiteSpace(opts.Otlp.Headers))
            o.Headers = opts.Otlp.Headers;
    }

    /// <summary>These attributes appear as dimensions on every trace, metric, and log entry.</summary>
    private static void ConfigureResource(ResourceBuilder b, ObservabilityOptions opts)
    {
        var resourceAttrs = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["deployment.environment"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
            ["host.name"]              = Environment.MachineName,
        };

        b.AddService(serviceName: opts.ServiceName, serviceVersion: opts.ServiceVersion)
         .AddAttributes(resourceAttrs);
    }

    private static void ConfigureTracing(TracerProviderBuilder tracing, ObservabilityOptions opts, Action<OtlpExporterOptions> configureOtlp)
    {
        tracing
            .AddSource(GeoAssetsActivitySource.SourceName)   // domain spans
            .AddSource(ImportDiagnostics.ActivitySourceName) // import pipeline spans
            .AddAspNetCoreInstrumentation(o =>
            {
                o.RecordException = true;
                // Skip health-check and metrics endpoints from traces
                o.Filter = ctx =>
                    !ctx.Request.Path.StartsWithSegments("/healthz", StringComparison.Ordinal) &&
                    !ctx.Request.Path.StartsWithSegments("/metrics", StringComparison.Ordinal);
            })
            .AddHttpClientInstrumentation(o =>
            {
                o.RecordException = opts.Instrumentation.RecordExceptionOnHttpErrors;
            });

        if (opts.Instrumentation.EnableEFCore)
            tracing.AddEntityFrameworkCoreInstrumentation(o =>
                o.SetDbStatementForText = true);

        // Export everything from the SDK; the retain/discard decision for
        // tail-based sampling (100% of errors + P95+ latency, ~1% baseline)
        // belongs to an OTel Collector `tail_sampling` processor placed
        // between this exporter and the backend — not deployed yet. Until
        // that Collector exists, every exported trace is billed/stored
        // as-is by the OTLP backend (see XD01-31).
        tracing.SetSampler(new AlwaysOnSampler());

        if (!string.IsNullOrWhiteSpace(opts.Otlp.Endpoint))
            tracing.AddOtlpExporter(configureOtlp);
    }

    private static void ConfigureMetrics(MeterProviderBuilder metrics, ObservabilityOptions opts, Action<OtlpExporterOptions> configureOtlp)
    {
        metrics
            .AddMeter(GeoAssetsMeter.MeterName)              // domain metrics
            .AddMeter(ImportDiagnostics.MeterName)           // import pipeline metrics
            .AddAspNetCoreInstrumentation()                  // request rate, latency
            .AddHttpClientInstrumentation();                 // outbound http

        if (opts.Instrumentation.EnableRuntime)
            metrics.AddRuntimeInstrumentation();

        if (opts.Instrumentation.EnableProcess)
            metrics.AddProcessInstrumentation();

        if (!string.IsNullOrWhiteSpace(opts.Otlp.Endpoint))
            metrics.AddOtlpExporter(configureOtlp);
    }

    private static void ConfigureLoggingBridge(ILoggingBuilder logging, ObservabilityOptions opts, Action<OtlpExporterOptions> configureOtlp)
    {
        logging.AddOpenTelemetry(otelLogging =>
        {
            otelLogging.IncludeFormattedMessage = true;
            otelLogging.IncludeScopes           = true;
            otelLogging.ParseStateValues        = true;
            otelLogging.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(opts.ServiceName, opts.ServiceVersion));

            if (!string.IsNullOrWhiteSpace(opts.Otlp.Endpoint))
                otelLogging.AddOtlpExporter(configureOtlp);
        });
    }
}

using Azure.Monitor.OpenTelemetry.AspNetCore;
using GeoAssets.Core.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
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
///     "AzureMonitor": {
///       "ConnectionString": "InstrumentationKey=...;IngestionEndpoint=..."
///     },
///     "Sampling": { "RatioForProduction": 0.25 },
///     "Instrumentation": { "EnableEFCore": true, "EnableRuntime": true }
///   }
/// }
/// </code>
///
/// <b>Architecture</b>
/// <code>
/// ILogger  ──┐
///             │  OpenTelemetry SDK  ──→  AzureMonitorExporter  ──→  Application Insights
/// Activity ──┤      (OTLP pipeline)
///             │
/// Meter    ──┘
/// </code>
///
/// The <c>Azure.Monitor.OpenTelemetry.AspNetCore</c> distro package
/// registers a single exporter that handles <b>traces, metrics, and logs</b>,
/// so no per-signal exporter configuration is needed.
/// </summary>
public static class ObservabilityServiceExtensions
{
    /// <summary>
    /// Adds OpenTelemetry instrumentation and Azure Monitor exporter.
    /// Also registers <see cref="GeoAssetsActivitySource"/> and
    /// <see cref="GeoAssetsMeter"/> as singletons for injection into
    /// application services.
    /// </summary>
    public static IServiceCollection AddGeoAssetsObservability(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        var opts = new ObservabilityOptions();
        configuration.GetSection(ObservabilityOptions.SectionName).Bind(opts);

        // Override connection string from environment variable if present
        // (Azure App Service / Container Apps inject this automatically).
        var envConnStr = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(envConnStr))
            opts.AzureMonitor.ConnectionString = envConnStr;

        // ── Domain singletons ─────────────────────────────────────────────────
        var activitySource = new GeoAssetsActivitySource(opts.ServiceVersion);
        var meter          = new GeoAssetsMeter(opts.ServiceVersion);

        services.AddSingleton(activitySource);
        services.AddSingleton(meter);

        // ── Resource attributes ───────────────────────────────────────────────
        // These appear as dimensions on every trace, metric, and log entry.
        var resourceAttrs = new Dictionary<string, object>
        {
            ["deployment.environment"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
            ["host.name"]              = Environment.MachineName,
        };

        void ConfigureResource(ResourceBuilder b) =>
            b.AddService(serviceName: opts.ServiceName, serviceVersion: opts.ServiceVersion)
             .AddAttributes(resourceAttrs);

        // ── OpenTelemetry SDK ─────────────────────────────────────────────────
        var otelBuilder = services
            .AddOpenTelemetry()
            .ConfigureResource(ConfigureResource);

        // ── Tracing pipeline ──────────────────────────────────────────────────
        otelBuilder.WithTracing(tracing =>
        {
            tracing
                .AddSource(GeoAssetsActivitySource.SourceName)   // domain spans
                .AddSource(ImportDiagnostics.ActivitySourceName) // import pipeline spans
                .AddAspNetCoreInstrumentation(o =>
                {
                    o.RecordException = true;
                    // Skip health-check and metrics endpoints from traces
                    o.Filter = ctx =>
                        !ctx.Request.Path.StartsWithSegments("/healthz") &&
                        !ctx.Request.Path.StartsWithSegments("/metrics");
                })
                .AddHttpClientInstrumentation(o =>
                {
                    o.RecordException = opts.Instrumentation.RecordExceptionOnHttpErrors;
                });

            if (opts.Instrumentation.EnableEFCore)
                tracing.AddEntityFrameworkCoreInstrumentation(o =>
                    o.SetDbStatementForText = true);

            // Tail-based sampling: always sample errors; probabilistic for the rest.
            tracing.SetSampler(new ParentBasedSampler(
                new TraceIdRatioBasedSampler(opts.Sampling.RatioForProduction)));
        });

        // ── Metrics pipeline ──────────────────────────────────────────────────
        otelBuilder.WithMetrics(metrics =>
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
        });

        // ── Azure Monitor exporter ────────────────────────────────────────────
        // Handles traces + metrics + ILogger logs in one call.
        // When ConnectionString is empty (local dev) the exporter is skipped.
        if (!string.IsNullOrWhiteSpace(opts.AzureMonitor.ConnectionString))
        {
            otelBuilder.UseAzureMonitor(o =>
            {
                o.ConnectionString = opts.AzureMonitor.ConnectionString;

                // Sampling ratio applied at the Azure Monitor ingestion level
                // (backup to SDK-side sampler above).
                o.SamplingRatio = (float)opts.Sampling.RatioForProduction;
            });
        }

        // ── ILogger → OpenTelemetry bridge ───────────────────────────────────
        // Ensures ILogger output is funnelled into the OTel pipeline and
        // therefore into Azure Monitor.  The bridge is always registered;
        // when there is no Azure Monitor exporter, logs go to the console only.
        services.AddLogging(logging =>
        {
            logging.AddOpenTelemetry(otelLogging =>
            {
                otelLogging.IncludeFormattedMessage = true;
                otelLogging.IncludeScopes           = true;
                otelLogging.ParseStateValues        = true;
                otelLogging.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(opts.ServiceName, opts.ServiceVersion));
            });
        });

        return services;
    }
}

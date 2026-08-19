# Semantic and vendor-agnostic instrumentation

**Status: `current`** — general observability engineering practice, not
gated by a language or runtime version.

**Source**: engineering directives provided by the user (observability
specialist), 2026-08-06. Distilled and paraphrased, not a reproduction.

## Vendor SDKs in business logic are an architecture error

Coupling business-logic source code directly to a commercial vendor's SDK
(New Relic SDK, Datadog SDK, etc.) creates vendor lock-in and raises the
technical cost of ever migrating telemetry backends — every instrumented
call site becomes something that has to be rewritten, not just
re-pointed.

## Instrument against open, universal APIs

Write instrumentation using OpenTelemetry's abstractions (`Tracer`, `Span`)
or a runtime's native, zero-cost primitives (`ActivitySource`/`Activity`
in .NET, which OpenTelemetry itself is built on). Business logic should
never `import` or reference a specific vendor's tracing/metrics package.

## Push the export decision to infrastructure

Where the data actually goes (Datadog, New Relic, Honeycomb, Azure Monitor)
should be configured out-of-process, at the infrastructure layer — via an
OpenTelemetry Collector or a runtime-attached agent — not compiled into the
application. Swapping backends should be a configuration/deployment change,
never a code change in the instrumented service.

## Where this would apply in GeoAssets

- `ObservabilityServiceExtensions.AddGeoAssetsObservability`
  (`core/GeoAssets.Infrastructure.Observability/ObservabilityServiceExtensions.cs`)
  already follows this shape correctly: `GeoAssetsActivitySource` and
  `GeoAssetsMeter` are the only tracing/metrics types application code
  should ever inject — they wrap `System.Diagnostics.ActivitySource` /
  `System.Diagnostics.Metrics.Meter`, which are OpenTelemetry-native, not
  a vendor SDK.
- **Updated 2026-08-16 (XD01-46).** The exporter is now the standard
  `OpenTelemetry.Exporter.OpenTelemetryProtocol` (OTLP) package —
  `tracing.AddOtlpExporter(...)` / `metrics.AddOtlpExporter(...)` /
  `otelLogging.AddOtlpExporter(...)` in `ObservabilityServiceExtensions.cs`
  (lines 144/163/180), all pointed at `ObservabilityOptions.Otlp.Endpoint`
  (line 45) — not the vendor-specific `Azure.Monitor.OpenTelemetry.AspNetCore`
  distro this section previously described (removed in XD01-30). This is
  an even stronger instance of the directive than a vendor distro isolated
  to one file: OTLP is itself the vendor-neutral wire protocol, so swapping
  backends (New Relic, Datadog, an OTel Collector) is a config change to
  `Otlp.Endpoint`/`Otlp.Headers`, not a package swap — see
  `deploy/otel/README.md` for the per-vendor configuration paths.
- Application code (providers, workflow publishers, Blazor services) should
  keep depending on `GeoAssetsActivitySource`/`GeoAssetsMeter` for any new
  instrumentation rather than adding a direct OpenTelemetry SDK or vendor
  package reference to a project that doesn't already have one.

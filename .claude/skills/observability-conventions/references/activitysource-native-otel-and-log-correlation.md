# ActivitySource/Activity: native .NET OpenTelemetry mapping and log correlation

**Status: `current`** — `System.Diagnostics.Activity`/`ActivitySource` and
`Activity.AddException` have shipped since .NET 6 (the exception helper
since .NET 9); no unshipped feature involved.

**Source**: engineering directives provided by the user (observability
specialist), 2026-08-06. Distilled and paraphrased, not a reproduction.

## Concept mapping: OpenTelemetry ↔ native .NET

Since .NET 6, Microsoft ships the OpenTelemetry abstractions natively in
the runtime — the OTel API itself is a thin wrapper over
`System.Diagnostics`. Knowing the exact equivalence matters when reading
vendor docs (Datadog, New Relic) that use OTel vocabulary but you're
writing plain .NET types:

| OpenTelemetry concept | Native .NET class (.NET 6+) | Purpose |
|---|---|---|
| Tracer | `ActivitySource` | Factory that decides whether telemetry is created at all |
| Span | `Activity` | The timed block representing one operation (a node in the trace DAG) |
| Attributes / Tags | `Activity.SetTag` | High-cardinality key-value metadata for indexing |
| Span Events | `ActivityEvent` (via `Activity.AddEvent`/`AddException`) | Structured, timestamped markers inside a span (e.g. an exception) |

## Zero-cost-when-unsampled is the point, not an implementation detail

`ActivitySource.StartActivity(...)` returns `null` immediately when nothing
is listening to that source (no APM agent attached, or the sampler
decided not to record). That's why every call site should be written as
`activity?.SetTag(...)` — when `activity` is `null`, the tag call never
executes, so no string allocation or boxing of numeric values happens on
the heap. The overhead of a fully-instrumented code path with telemetry
turned off is O(1), not proportional to how many tags the code *would*
have set.

## Recording an exception on a span

Prefer `Activity.AddException(ex)` (recorded as a structured
`ActivityEvent` following OTel's exception semantic conventions —
`exception.type`, `exception.message`, `exception.stacktrace`) over
manually setting ad hoc top-level tags like `error.stack`. Pair it with
`activity.SetStatus(ActivityStatusCode.Error, ex.Message)` so the span
itself is marked as failed — that status, not the presence of an
exception event, is what most APM backends use to compute error rate.

## Log correlation without a vendor logging package

`Serilog.Enrich.WithSpan()` (from `Serilog.Enrichers.Span`) plus
`Enrich.FromLogContext()` is one way to get `TraceId`/`SpanId` injected
into every structured log line automatically, satisfying the MDC
requirement from
[unified-correlation-of-pillars.md](unified-correlation-of-pillars.md) —
this is an alternative to the OpenTelemetry `ILogger` bridge
(`logging.AddOpenTelemetry(...)` with `IncludeScopes = true`), not an
addition to it. Pick one correlation mechanism per service; running both
means the same `trace_id` gets attached to log output twice, through two
different pipelines, which is redundant rather than more correct.

## Zero vendor lock-in, restated concretely

Domain code (e.g. an `OrderService.ProcessOrderAsync`) should only ever
touch `System.Diagnostics.Activity` and `Microsoft.Extensions.Logging.ILogger`
— it has no idea Datadog, New Relic, or even OpenTelemetry exist. The
`AddOtlpExporter(...)` (or vendor-specific distro package) call lives
exclusively in the composition root (`Program.cs` / the DI registration
extension), which is the same boundary
[semantic-vendor-agnostic-instrumentation.md](semantic-vendor-agnostic-instrumentation.md)
already describes — this source reinforces that directive with a concrete
code shape rather than introducing a new one.

## Where this would apply in GeoAssets

- `GeoAssetsActivitySource`
  (`core/GeoAssets.Infrastructure.Observability/GeoAssetsActivitySource.cs`)
  already implements this pattern correctly: `StartActivity` returns
  `Activity?` and every tag call site in the codebase chains off it with
  `?.AddTag(...)` (e.g. `StartOrderActivity`, `StartNotificationActivity`
  at lines 33-40), so the zero-cost-when-unsampled property already holds.
  Tag naming (`order.id`, `messaging.system`) already follows the
  dot-notation convention this source's examples use (`tenant.id`,
  `customer.id`) — no change needed there.
- `GeoAssetsActivitySource.RecordException`
  (`GeoAssetsActivitySource.cs:53-58`) already calls
  `activity.AddException(ex)` followed by
  `SetStatus(ActivityStatusCode.Error, ex.Message)` — this is the
  semantic-convention-correct form this reference recommends, and it's
  *better* than the raw `error.type`/`error.message`/`error.stack` tags
  shown in the source material, which duplicate what `AddException`
  already records as structured event attributes. No change needed; if
  anyone adds manual `error.*` tags alongside `RecordException` in new
  code, that's redundant and should be flagged in review.
- GeoAssets does **not** use Serilog anywhere in the tree (no
  `Serilog`/`Serilog.Enrichers.Span` package reference exists). Log/trace
  correlation is already handled via the OpenTelemetry `ILogger` bridge in
  `ObservabilityServiceExtensions.cs:156-165` (`IncludeScopes = true`) —
  per the note above, that already satisfies the MDC requirement.
  **Do not suggest adding Serilog** to get span-correlated logs; the
  runtime-native bridge already in place does the same job through one
  pipeline. Only reconsider this if the user explicitly wants Serilog's
  sink ecosystem (e.g. file rotation, Seq) for reasons unrelated to trace
  correlation.
- The current exporter is `Azure.Monitor.OpenTelemetry.AspNetCore`'s
  `UseAzureMonitor(...)` (`ObservabilityServiceExtensions.cs:142-150`), not
  a raw `AddOtlpExporter(...)` pointed at a local Collector. If GeoAssets
  ever needs the Collector-side `tail_sampling` processor described in
  [tail-based-sampling.md](tail-based-sampling.md), swapping to
  `AddOtlpExporter` targeting the Collector (which can itself forward to
  Azure Monitor) is the concrete migration path — confined to this one
  file, per the vendor-agnostic boundary above.

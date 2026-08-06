# Unifying entropy: strict correlation of the three pillars

**Status: `current`** — general observability engineering practice, not
gated by a language or runtime version.

**Source**: engineering directives provided by the user (observability
specialist), 2026-08-06. Distilled and paraphrased, not a reproduction.

## The cost of siloed pillars

Keeping logs, metrics, and traces in separate tools (or separate, unlinked
indices within the same tool) sharply increases Mean Time To Resolution
(MTTR) — every context switch between "which log line?" and "which trace?"
costs an engineer's attention during an incident, and that cost compounds
under pressure.

## Mandatory metadata injection (MDC / ambient context)

Every log line emitted by the application must carry the active span's
contextual attributes: `trace_id`, `span_id`, `service.name`, and `env`.
This isn't a nice-to-have field — it's what makes the log line addressable
from the trace view (and vice versa) at all. Ambient-context mechanisms
(MDC in Java/Scala logging frameworks, `ILogger` scopes + `Activity.Current`
in .NET) exist specifically to inject this without threading the values
through every method signature by hand.

## Why this matters: O(1) correlation

Without this injection, finding the root cause of an anomalous log line
means full-text search across billions of unrelated lines. With
`trace_id`/`span_id` on every entry, the same lookup becomes a direct
primary-key index hit — one click from "this log line looks wrong" to "here
is the exact trace graph that produced it."

## Where this would apply in GeoAssets

- `ObservabilityServiceExtensions.AddGeoAssetsObservability`
  (`core/GeoAssets.Infrastructure.Observability/ObservabilityServiceExtensions.cs:156-165`)
  already sets `IncludeScopes = true` on the OpenTelemetry `ILogger` bridge,
  which is the mechanism that attaches the active `Activity`'s `trace_id`/
  `span_id` to every log record — this part of the directive is already
  satisfied for any service registered through this extension.
- `resourceAttrs` in the same file
  (`ObservabilityServiceExtensions.cs:78-82`) sets `deployment.environment`
  and `host.name` as resource-level attributes shared by traces, metrics,
  *and* logs (since the Azure Monitor distro exports all three signals
  through one exporter) — this is the `service.name`/`env` half of the
  directive, already correctly unified at the resource level rather than
  per-signal.
- Callers still need to log through `ILogger` (not `Console.Write` or a
  separate file-based logger) for the scope injection to apply — worth
  checking for any component that bypasses `ILogger` for its own log
  output, since that would silently opt out of the correlation this
  extension provides.

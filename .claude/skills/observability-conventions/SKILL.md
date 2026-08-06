---
name: observability-conventions
description: >-
  Distributed-tracing, logging, metrics, and alerting conventions for
  GeoAssets — context propagation, log/trace correlation, high-cardinality
  structured telemetry, vendor-agnostic instrumentation, SLO-based alerting,
  and tail-based sampling. Use when writing or reviewing code that emits
  logs/traces/metrics, touches GeoAssets.Infrastructure.Observability,
  crosses an async/queue boundary (Kafka, Service Bus, background Task), or
  when discussing alerting/sampling strategy.
---

# Observability Conventions

Distilled, source-attributed engineering guidance — not a restatement of
things you already know. Each reference file below traces back to a
specific set of directives; when a claim turns out to be wrong or
superseded, fix it at the source file, don't just override it in a
conversation.

## How this skill is organized

- `references/` holds one file per topic. Read only the file relevant to
  the task at hand — don't load the whole set speculatively.
- Every entry records **status**: `current` (usable today, not gated by
  version) since observability practice here isn't tied to a language or
  runtime version the way `csharp-conventions`' `future` entries are.
- Each file ends with a **"Where this would apply in GeoAssets"** section
  grounding the directive in the actual state of
  `GeoAssets.Infrastructure.Observability` and its call sites — treat those
  notes as a snapshot, not a guarantee; re-check the referenced file/line
  before relying on it, since the code moves.

## References

| File | Topic | Status |
|---|---|---|
| [context-propagation-and-causality.md](references/context-propagation-and-causality.md) | W3C `traceparent`/`tracestate`, deterministic propagation across threads/tasks/queues | `current` |
| [unified-correlation-of-pillars.md](references/unified-correlation-of-pillars.md) | MDC/ambient context, mandatory `trace_id`/`span_id`/`service.name`/`env` on every log line | `current` |
| [high-cardinality-structured-telemetry.md](references/high-cardinality-structured-telemetry.md) | High-cardinality span/log attributes, structured logging vs. string interpolation | `current` |
| [semantic-vendor-agnostic-instrumentation.md](references/semantic-vendor-agnostic-instrumentation.md) | OpenTelemetry/`ActivitySource` over vendor SDKs, exporter config pushed to infrastructure | `current` |
| [slo-based-alerting.md](references/slo-based-alerting.md) | Golden signals, error budgets, burn-rate alerting vs. raw resource thresholds | `current` |
| [tail-based-sampling.md](references/tail-based-sampling.md) | Retain 100% of errors + P95+ latency, small % baseline; head-based vs. tail-based sampling | `current` |
| [activitysource-native-otel-and-log-correlation.md](references/activitysource-native-otel-and-log-correlation.md) | Native `ActivitySource`/`Activity` ↔ OTel concept mapping, zero-cost-when-unsampled pattern, `AddException`, Serilog vs. OTel `ILogger` bridge for log correlation | `current` |

As more directives or sources are added, list them here with topic and
status so this table stays the single index of what's been curated.

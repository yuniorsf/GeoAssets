# Cost optimization via intelligent tail-based sampling

**Status: `current`** — general observability engineering practice, not
gated by a language or runtime version.

**Source**: engineering directives provided by the user (observability
specialist), 2026-08-06. Distilled and paraphrased, not a reproduction.

## Storing 100% of successful traffic is wasteful

Retaining every trace for low-latency HTTP 200 responses is a financial and
storage waste — the overwhelming majority of that data is redundant once
you already have a representative baseline. The entropy/cost math favors
keeping the *anomalous* traces at full fidelity and discarding most of the
routine ones.

## Decide retention at the buffer, based on the whole trace

Implement adaptive sampling that decides whether to keep or discard a trace
only *after* it's finished (i.e. in a collector-side buffer, not at
trace-start time), and retain:

- **100%** of spans that end in an exception (`error = true`)
- **100%** of spans whose latency falls in the high percentile (> P95)
- a small, statistically representative fraction (e.g. **1%**) of normal
  transactions, to keep a baseline for comparison

This is *tail-based* sampling specifically because the keep/discard
decision requires seeing the trace's outcome (error? P95+ latency?) —
which isn't known at the moment the root span starts. It requires
buffering complete traces somewhere (typically an OpenTelemetry Collector
with the `tail_sampling` processor) before the sampling decision is made.

## Where this would apply in GeoAssets

**Resolved 2026-08-14 (XD01-31).** `ObservabilityServiceExtensions.WithTracing`
(`core/GeoAssets.Infrastructure.Observability/ObservabilityServiceExtensions.cs`)
previously configured head-based, probabilistic sampling
(`ParentBasedSampler(TraceIdRatioBasedSampler(...))`) behind a comment that
inaccurately claimed "tail-based sampling: always sample errors" —
`TraceIdRatioBasedSampler` cannot implement that policy, since the
keep/discard decision happens at trace-start, before the outcome (error?
P95+ latency?) is known.

The sampler is now `AlwaysOnSampler` — the SDK exports 100% of traces, and
the in-code comment says so plainly. Genuine tail-based sampling per this
directive still requires an OpenTelemetry Collector configured with the
`tail_sampling` processor (policies: `status_code` for errors, `latency`
for the P95+ bucket, `probabilistic` for the baseline 1%) placed between
the SDK and the OTLP backend (New Relic as of XD01-30) — that Collector
does not exist yet, so every exported trace is currently billed/stored
as-is by the backend. Deploying that Collector is infrastructure work, out
of scope for `GeoAssets.Infrastructure.Observability`.

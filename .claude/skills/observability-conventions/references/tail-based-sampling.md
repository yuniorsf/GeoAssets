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

`ObservabilityServiceExtensions.WithTracing`
(`core/GeoAssets.Infrastructure.Observability/ObservabilityServiceExtensions.cs:116-118`)
currently configures:

```csharp
tracing.SetSampler(new ParentBasedSampler(
    new TraceIdRatioBasedSampler(opts.Sampling.RatioForProduction)));
```

This is **head-based, probabilistic sampling** — the keep/discard decision
is made when the trace *starts*, based only on a hash of the `TraceId`,
before anything is known about whether the request will error or run slow.
It cannot implement "always keep errors and P95+ latency" — a trace that
turns out to fail was already discarded (or kept) by the coin flip at the
root span, independent of its outcome. The in-code comment above that call
(`"Tail-based sampling: always sample errors..."`) is aspirational, not an
accurate description of what `TraceIdRatioBasedSampler` does — worth fixing
that comment, or the sampler itself, so they agree.

Getting genuine tail-based sampling per this directive would mean routing
export through an OpenTelemetry Collector configured with the
`tail_sampling` processor (policies: `status_code` for errors,
`latency` for the P95+ bucket, `probabilistic` for the baseline 1%) placed
between the SDK and Azure Monitor, rather than deciding sampling in-process
via `SetSampler`. That's an infrastructure/deployment change, not a
`GeoAssets.Infrastructure.Observability` code change — the SDK-side sampler
would most likely be set to `AlwaysOnSampler` (export everything to the
Collector) and let the Collector make the retain/drop call.

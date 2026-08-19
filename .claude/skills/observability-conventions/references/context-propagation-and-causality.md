# Context invariance and causal propagation

**Status: `current`** — general distributed-tracing engineering practice, not
gated by a language or runtime version.

**Source**: engineering directives provided by the user (observability
specialist), 2026-08-06. Distilled and paraphrased, not a reproduction.

## The core rule

The foundation of distributed tracing is preserving the trace's Directed
Acyclic Graph (DAG) across every network and thread boundary. Break the
`TraceId` and you fragment the state space — the causal timeline can no
longer be reconstructed after the fact, no matter how much log/metric data
you kept.

## Deterministic propagation

Every secondary thread, async task (`Task` in C#, `Future` in Scala), or
queued message (RabbitMQ, Kafka, Azure Service Bus) must inherit the calling
execution's trace context. This isn't optional plumbing — an async hop that
doesn't carry the context forward silently starts a *new*, disconnected
trace, and the two halves of the same logical operation become unrelated in
the backend.

## Use the W3C standard, not a bespoke header

Propagate context via the standardized `traceparent` / `tracestate` HTTP
headers on every HTTP/RPC call. The payoff isn't just interop today — it's
that the propagation mechanism stays valid at the network level even if the
telemetry *backend* (Datadog, New Relic, Honeycomb, Azure Monitor) changes
later. A custom correlation header ties you to whatever code reads that
specific header; `traceparent` is read by every OpenTelemetry-instrumented
service regardless of vendor.

## Where this would apply in GeoAssets

- **Resolved 2026-08-14 (XD01-32), publish side only.** `KafkaOrderEventPublisher`
  and `ServiceBusOrderEventPublisher` now wrap each publish in a
  `GeoAssetsActivitySource`-issued `ActivityKind.Producer` span and inject
  its W3C `traceparent` (via `System.Diagnostics.DistributedContextPropagator`)
  into a `traceparent` Kafka header / Service Bus application property,
  additive alongside the existing `correlationId` header. The DAG no longer
  breaks at the point of publish.
  **Still open**: this repo has no Kafka/ServiceBus *consumer* implementation
  yet (publish-only), so nothing currently extracts `traceparent` on
  receive — whoever builds a consumer needs to read that header/property
  into an `ActivityContext` (e.g. via
  `DistributedContextPropagator.Current.Extract`) and start the consumer's
  `Activity` as a child of it, or the causal link this ticket added on the
  publish side still dead-ends at the topic.
- `ObservabilityServiceExtensions.AddGeoAssetsObservability`
  (`core/GeoAssets.Infrastructure.Observability/ObservabilityServiceExtensions.cs`)
  already registers ASP.NET Core and `HttpClient` instrumentation, which
  handles W3C propagation automatically for in-process HTTP/RPC calls — the
  gap is specifically at the async-messaging boundary, not the synchronous
  HTTP path.

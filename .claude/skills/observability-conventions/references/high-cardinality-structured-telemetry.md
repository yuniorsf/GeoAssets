# Preserving high cardinality and high density

**Status: `current`** — general observability engineering practice, not
gated by a language or runtime version.

**Source**: engineering directives provided by the user (observability
specialist), 2026-08-06. Distilled and paraphrased, not a reproduction.

## Aggregation destroys information

Traditional monitoring aggregates prematurely — e.g. counting how many
HTTP 500s occurred per minute — which discards exactly the detail needed to
answer "*whose* request failed, and what did they have in common?" after
the fact. Modern observability instead stores the raw, structured event and
lets aggregation happen at query time, not at write time.

## High-cardinality attributes are a feature, not a cost to avoid

Attach unique identifiers — `user_id`, `tenant_id`, `order_id`,
`cart_item_count` — as attributes/tags on the span or structured log
itself, not just as a low-cardinality bucket label. High cardinality is
what makes an attribute *useful* for root-causing a single anomalous
event, as opposed to only supporting coarse dashboards.

## Never concatenate strings into log messages

Don't build messages via string interpolation:

```csharp
// Wrong — the value is baked into an opaque string
logger.LogInformation($"User {id} failed");
```

Use structured, semantic logging instead, where the key stays a distinct,
indexable field:

```csharp
// Right — {UserId} is a named placeholder, not a concatenated value
logger.LogInformation("User failed to authenticate {UserId}", id);
```

This lets the backend's columnar storage index the key efficiently and
query on it directly (`user_id = 123`), instead of falling back to
full-text search over an opaque message string.

## Where this would apply in GeoAssets

- `KafkaOrderEventPublisher.PublishAsync`
  (`workflow/GeoAssets.Workflow.Messaging.Kafka/KafkaOrderEventPublisher.cs:72-84`)
  and `PostgresAssetProvider`
  (`providers/GeoAssets.Provider.PostgreSQL/Repositories/PostgresAssetProvider.cs:311`)
  already use `ILogger`'s message-template form (`{OrderId}`, `{Topic}`,
  etc.) rather than string interpolation — this convention is already
  established in the codebase and should be followed by any new logging
  call, not just these two call sites.
- `GeoAssetsMeter` and `GeoAssetsActivitySource`
  (`core/GeoAssets.Infrastructure.Observability/GeoAssetsMeter.cs`,
  `GeoAssetsActivitySource.cs`) are the natural place to attach
  high-cardinality span/metric tags (e.g. `order_id`, `asset_id`,
  `tenant_id` once multi-tenancy lands) via `Activity.SetTag` / instrument
  attributes — worth checking these call
  sites carry the domain identifier, not just a generic operation name,
  when instrumenting new code paths.

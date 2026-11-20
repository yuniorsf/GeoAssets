# GeoAssets.NeutralOperator.Server — local Docker

`GeoAssets.NeutralOperator.Server` (XD01-382, ADR-001 Phase 7) is the NeutralOperator vertical's own microservice on the
order-type chassis: the generic workflow engine over its **own** database (`geoassets_neutral_operator`), the
NeutralOperator module (shared-use dispatch, pole occupancy staging and commit, clandestine notices, tenant attachment
requests) and its `NeutralOperatorDbContext`. It never opens the kernel's database; it talks to `GeoAssets.Server` only
over its REST API:

| What | Kernel route | As |
|---|---|---|
| Order-type & feature-action descriptors (bootstrap) | `PUT /api/registry/verticals/neutral-operator/order-types` | `GeoAssets.ServiceCaller` |
| Permissions & role bundles (bootstrap) | `PUT /api/identity/verticals/neutral-operator/permissions` | `GeoAssets.ServiceCaller` |
| Target selection: preview / reserve, release, usages | `/api/targets/ledger/neutral-operator/{orderId}/…` (organization in the body / query) | `GeoAssets.ServiceCaller` |
| Features | `/api/geoassets/organizations/{organizationId}/features/…` (XD01-386) | `GeoAssets.ServiceCaller` |
| The caller's identity, permissions and grants | `GET /api/identity/me`, `GET /api/identity/organization-grants/me` | the caller's own token |

It serves on host port **8082** (the kernel is 8080, the web client 8081) the generic `/api/workflow` service-order,
order-type and target routes and the module's occupancy and shared-use routes — the same routes the kernel serves while it
still hosts the module.

## Running it

Start the kernel stack first (`deploy/server`, see its README): this stack joins its Docker network and reuses its
Postgres container with a separate database.

```bash
cd deploy/neutral-operator
cp .env.example .env    # POSTGRES_PASSWORD = the deploy/server one; ServiceAuth/AzureAdCiam when you have them
docker compose up --build
```

The one-shot `neutral-operator-db` service creates `geoassets_neutral_operator` when it does not exist. The network is
`server_default` (the default project name of `deploy/server`); set `KERNEL_NETWORK` otherwise.

## Health and readiness

- `GET /healthz` — liveness. **200** as long as the process runs; its body reports `degraded` while the bootstrap
  registration with the kernel is pending or failing (the `vertical-registration` check), `healthy` once it succeeded.
  A kernel outage therefore never restarts this container. The compose healthcheck probes it.
- `GET /readyz` — readiness. **503** until the kernel knows this vertical's order types and permissions, 200 after.

The registration retries with exponential backoff and jitter (1 s doubling to 60 s, `VerticalRegistration` section),
keeps retrying until it succeeds, then republishes every 5 minutes (the kernel's descriptor registry is in memory, so a
restarted kernel learns the vertical again on its own). Without `ServiceAuth` credentials the kernel answers 401: the
check stays Degraded, `/readyz` 503, and target reservations answer `503 targets.ledger-unavailable`.

## First-run database setup (manual, like the kernel's)

Apply, from the repo root and in this order, after the `neutral-operator-db` service has created the database:

```bash
# 1. This host's own workflow chain (ServiceOrderDbContext, migrations in apps/GeoAssets.NeutralOperator.Server:
#    InitialNeutralOperatorIsolation — tables, triggers and row-level security).
dotnet ef database update \
  --project apps/GeoAssets.NeutralOperator.Server \
  --startup-project apps/GeoAssets.NeutralOperator.Server \
  --context ServiceOrderDbContext \
  --connection "Host=localhost;Port=5433;Database=geoassets_neutral_operator;Username=postgres;Password=<POSTGRES_PASSWORD>"

# 2. The vertical's own chain (NeutralOperatorDbContext, schema neutral_operator) — after 1.
dotnet ef database update \
  --project verticals/NeutralOperator/GeoAssets.NeutralOperator.Persistence \
  --startup-project verticals/NeutralOperator/GeoAssets.NeutralOperator.Persistence \
  --context NeutralOperatorDbContext \
  --connection "Host=localhost;Port=5433;Database=geoassets_neutral_operator;Username=postgres;Password=<POSTGRES_PASSWORD>"
```

## Cut-over and rollback (kernel flag)

Until the cut-over the kernel keeps hosting the NeutralOperator module (its default `GeoAssets:Modules` is
`[ "postgres-spatial", "neutral-operator", "ip-billing-bridge" ]`), so both hosts can run side by side; this host's
registration then replaces the kernel's in-process `neutral-operator` descriptors with its own (the same feature action,
plus the target policies and terminal statuses the ledger needs).

- **Cut over**: start the kernel without the module — blank its entry with an environment variable
  (`GeoAssets__Modules__1=` on the `server` service of `deploy/server`; a blank entry is ignored) or list the modules
  without `neutral-operator`. The kernel then maps none of the module's routes (occupancy, shared-use dispatch, the GIS
  export `GET /api/geoassets/export/gis/v1`) and registers none of its services; clients reach the vertical here.
- **Roll back**: list `neutral-operator` again (remove the override) and restart the kernel — it serves the module
  in process exactly as before.

Orders already in the kernel's database are not moved by this ticket; the data move follows.

## The approval saga (XD01-383)

Approving a shared-use pole-work order writes the poles' occupancy here as **pending** (change sets `Pending`, one row in
`neutral_operator.occupancy_apply_sagas` with each pole's previous entries), keeps the order in `Completed` with the
attribute `occupancy.apply.state = awaiting-apply` (every other status change is refused meanwhile), and stages one
`AssetMutationBatchRequestedV1` for the kernel — the map (WMS/WFS) shows the new occupancy only once the kernel applied it.
The kernel answers `geoassets.assets.mutation-batch-applied.v1` (the order moves to `Approved`, the sets to `Committed`)
or `geoassets.assets.mutation-batch-rejected.v1` (the poles are restored, the sets become `Reverted`, the order stays in
`Completed` with the kernel's reason in its action log). Every message is deduplicated in the inbox; a batch is applied
once whatever is redelivered.

- **It needs a broker on this host** (`Workflow:Outbox:Transport` = `Kafka` or `ServiceBus`, with a consumer topic): in
  process this host reaches only itself, never the kernel. With `None` the batches stay pending in the outbox (nothing is
  lost), the approvals stay awaiting apply, and a startup warning says so. There is no REST fallback: only the kernel's
  consumer can tell a rejection from a retryable failure.
- **Timeout**: an approval still awaiting the kernel `Saga:ApplyTimeout` (default 15 min) after its batch was last staged
  is flagged — `geoassets.saga.apply.timeouts`, a structured warning, `geoassets.saga.apply.pending` /
  `geoassets.saga.apply.oldest_pending_age` gauges; alert rules in `deploy/otel/alerts/saga-apply-alerts.yaml` — and stays
  pending. Check the kernel's asset-mutation consumer, then retry:
  `POST /api/workflow/service-orders/{id}/occupancy-saga/retry` (whoever may approve the order; 202 with the saga) stages
  the same batch again.

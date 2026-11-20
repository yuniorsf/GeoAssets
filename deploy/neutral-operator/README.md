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

## Cut-over and rollback (kernel flag + gateway destination)

Since XD01-384 the cut-over is the default: the kernel's `GeoAssets:Modules` is `[ "postgres-spatial" ]` (XD01-385 retired `ip-billing-bridge`),
so it maps none of the module's routes (occupancy, shared-use dispatch, the GIS export `GET /api/geoassets/export/gis/v1`)
and registers none of its services, and clients reach the vertical through the gateway (`apps/GeoAssets.Gateway`):

| Client calls (gateway) | Gateway forwards to (`Destinations:NeutralOperator`) |
|---|---|
| `/api/v/neutral-operator/{**rest}` (query string kept) | `/api/{rest}` — e.g. `/api/v/neutral-operator/workflow/service-orders/{id}/shared-use/dispatch` → `/api/workflow/service-orders/{id}/shared-use/dispatch` |

The vertical's order-type descriptors (this host's and, while it hosts the module, the kernel's) publish
`ApiBaseRoute = /api/v/neutral-operator/workflow/service-orders`, and the Web/MAUI clients (`AddDescriptorRoutedWorkflowRest`)
reach each order type at its descriptor's route on the gateway's origin — they know no host of this vertical.

- **Production / cut over**: `Destinations:NeutralOperator:Shared` (and each silo) = this host; the kernel without the module
  (the default).
- **Roll back**: list `neutral-operator` again on the kernel (`GeoAssets__Modules__1=neutral-operator`) and restart it — it
  serves the module in process exactly as before — and point `Destinations:NeutralOperator:Shared` at the kernel. The module's
  routes are identical on both hosts, so the same `/api/v/neutral-operator/** → /api/**` rewrite reaches either.

**The local Docker stack keeps the kernel hosting the module** (`deploy/server`: `GeoAssets__Modules__1=neutral-operator` on
`server`, the gateway's `Destinations__NeutralOperator__Shared=http://server:8080`): it has no `GeoAssets.ServiceCaller`
credentials (this host's registration stays Degraded, ledger reservations answer 503) and no broker (the approval saga needs
one). To run the vertical here instead, set in `deploy/server/.env`

```bash
KERNEL_NEUTRAL_OPERATOR_MODULE=                       # blank: the kernel without the module
NEUTRAL_OPERATOR_DESTINATION=http://neutral-operator:8080
```

restart `deploy/server`, then start this stack (with `ServiceAuth__*` credentials and a broker in its `.env`).

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

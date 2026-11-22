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

## Token audiences (XD01-390)

This host validates the caller's token against its **own** API registration (`AzureAdCiam__ClientId` = the NeutralOperator
API's client id) and forwards the same token to the kernel's identity routes (`/api/identity/me`, `/api/identity/policies`,
`/api/identity/organization-grants/me`), so the kernel must accept that audience: set on the kernel
`AzureAdCiam__AdditionalAudiences__N=<NeutralOperator API client id>` (or its App ID URI, whichever the tokens carry as
`aud`; `deploy/server/.env` for the local stack). See `deploy/server/README.md` "Token audiences of the vertical hosts".

The Web and MAUI clients reach this host through the gateway at `/api/v/neutral-operator/` and request this API's scope for
exactly those routes (XD01-397) — every other request keeps the kernel's scope. To run CIAM model (b) end to end:

1. In Entra External ID (CIAM), create an app registration for the NeutralOperator API and expose an API on it
   (Application ID URI, e.g. `api://<NeutralOperator API client id>`, and an `access_as_user` scope).
2. Grant the Web (SPA) and MAUI (native) client registrations that scope (API permissions → *My APIs*; grant admin consent
   if the tenant requires it).
3. Set the clients' route scope for this vertical's routes — the entry `GeoAssetsServer:RouteScopes` already lists with a
   blank `Scope` (blank = ignored, so the request keeps the kernel's scope): in `apps/GeoAssets.Web/wwwroot/appsettings.json`
   (baked in at build time, like `GeoAssetsServer:ApiScope`) and `apps/GeoAssets.MAUI/appsettings.json`,
   `{ "PathPrefix": "/api/v/neutral-operator/", "Scope": "api://<NeutralOperator API client id>/access_as_user" }`. The
   clients ask consent for it at sign-in and acquire its token separately, one resource per token request.
4. Give the NeutralOperator API the kernel API's app roles (same `value`s) and the same user assignments, and add its
   client id to role sync's `RoleSync:TargetApplicationClientIds` (Role Sync service principal as an owner of the
   registration). The kernel reads the caller's roles from the forwarded token, so without them every permission check of
   this host denies — see `deploy/server/README.md` "Roles travel in the token".
5. Set this host's `AzureAdCiam__ClientId` to the NeutralOperator API's client id.
6. List its audience on the kernel: `AzureAdCiam__AdditionalAudiences__N=<NeutralOperator API client id>`.

Alternatively (model (a)) set `AzureAdCiam__ClientId` to the kernel's API audience, leave the clients' route scope blank,
and the kernel needs no extra audience.

A kernel that does not accept the audience makes every route that needs the caller's identity answer **502**
`kernel.token-rejected` (the detail carries the kernel's `error_description`) with one Error log naming the kernel route,
the token's `aud`/`iss` and the kernel's `error` — not 401 (the caller cannot fix it by signing in again) and not 500. An
unreachable kernel answers `503 kernel.unavailable`.

## Running it

Start the kernel stack first (`deploy/server`, see its README): this stack joins its Docker network and reuses its
Postgres container with a separate database.

```bash
cd deploy/neutral-operator
cp .env.example .env    # POSTGRES_PASSWORD = the deploy/server one; ServiceAuth/AzureAdCiam when you have them
docker compose up --build
```

Or start everything at once from the repo root: `docker compose -f deploy/docker-compose.yaml up --build -d` (it includes
`deploy/server`, this file and `deploy/web`; this file still reads its own `.env`).

The one-shot `neutral-operator-db` service waits up to 60 s for the kernel's Postgres, then creates
`geoassets_neutral_operator` when it does not exist. Both files declare the Compose project **`geoassets`**, so this stack
is on the kernel's network, `geoassets_default`, by being in the same project (the former `KERNEL_NETWORK` setting and
the external `server_default` network are gone): when you override the project name (`-p`), pass the same one to both. The
service is published on `127.0.0.1:8082` only.

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

Orders the kernel created while it hosted the module stay in the kernel's database until they are moved: see
[Moving the kernel's NeutralOperator orders](#moving-the-kernels-neutraloperator-orders-xd01-391).

## Moving the kernel's NeutralOperator orders (XD01-391)

While the kernel hosted the module, its NeutralOperator orders were written to the **kernel's** GeoAssets database.
`tools/GeoAssets.Tools.NeutralOperatorDataMove` moves them, once, to this host's database — re-runnable, in four verbs:

```bash
export GEOASSETS_KERNEL_DB="Host=…;Database=geoassets;Username=<table owner>;Password=…"
export GEOASSETS_NEUTRAL_OPERATOR_DB="Host=…;Database=geoassets_neutral_operator;Username=<table owner>;Password=…"
dotnet run --project tools/GeoAssets.Tools.NeutralOperatorDataMove -- plan            # dry run (the default verb)
dotnet run --project tools/GeoAssets.Tools.NeutralOperatorDataMove -- copy [--include-pending-outbox]
dotnet run --project tools/GeoAssets.Tools.NeutralOperatorDataMove -- verify
dotnet run --project tools/GeoAssets.Tools.NeutralOperatorDataMove -- remove --confirm
```

(`--source` / `--target` instead of the variables; `--batch-size <n>`, default 500.) The tool never prints a connection
string — only `host:port/database` — and exits **0** done · **1** usage error · **2** conflicts or differences found
(nothing overwritten) · **3** refused (a precondition failed; nothing changed) · **4** failed (database error or Ctrl+C;
rolled back).

**What moves.** Every order whose `OrderTypeId` is one of the module's order types (`NeutralOperatorModule.RegisterOrderTypes`
— the tool reads them from that code), with:

| Kernel table | How |
|---|---|
| `public."ServiceOrders"` | every column as stored — ids, `RowVersion`, timestamps, `OrganizationId`, attributes JSON; parents first |
| `public."OrderDispatches"`, `public."OrderActionLogs"` | per order, in the kernel's order; their integer identity is renumbered by this database (it is a local surrogate no route returns — both databases number from 1) |
| `public."ServiceOrderTargets"` (the orders' rows under the `kernel` vertical) | as stored — they become this host's local targets, exactly what `LedgerOrderTargetSelectionService` mirrors |
| `public."WorkflowOutboxMessages"` | only the moved orders' **undelivered** `AssetMutationBatchRequestedV1` / `OrderStateChangedV1`, and only with `--include-pending-outbox` |
| `neutral_operator.*` | the whole schema: pole occupancies and entries, occupancy change sets, approval sagas and their poles |

Not moved: order types, states, transitions, permissions and policies (this host builds its order types from the module's
code; a kernel-side order-type row for a NeutralOperator type is reported as a warning), the outbox's delivered history, the
inbox, the organization-grant mirror and cursor, and the kernel's own undelivered answers to a batch (they stay in its outbox
and reach this host over the broker).

**Semantics.** `copy` inserts each moved row this database lacks, in foreign-key order, one transaction per batch, keeping
every value (it compares and writes each column's exact text form). A row already here with identical content is skipped —
a second `copy` changes nothing. A row whose id (or any unique index) is already here with other content is a **conflict**:
reported, never overwritten, exit 2, and `copy` writes nothing at all while `plan` shows one. `verify` checks that every
moved row is here, identical (outbox messages: the message, not each relay's delivery state). `remove --confirm` runs
`verify` again inside one kernel transaction that locks the moved tables against writers, and deletes the kernel's rows
only if every one of them is here, identical — a row changed, or a NeutralOperator order created, after the copy refuses it
(exit 3, nothing deleted).

**The global target ledger stays in the kernel (ADR-001 D2).** In the kernel's database the moved orders' targets *are* the
ledger's entries of those orders, held under the in-process `kernel` vertical — and deleting their orders would cascade them
away. So `remove` does not delete them: in the same transaction it re-keys each one to `(neutral-operator, order id)` —
same target id, feature, status, version, release instant and organization, plus the order's type and its policy's
exclusivity, as a remote vertical's entry carries them — which is the key this host reserves, lists and releases its orders'
targets under. No other ledger entry is touched.

### Prerequisites

- **Both databases at their migration heads**: the kernel's GeoAssets database with the kernel's `ServiceOrderDbContext`
  chain and the `NeutralOperatorDbContext` chain; this database with both chains of [First-run database setup](#first-run-database-setup-manual-like-the-kernels).
  The tool checks the history tables and every moved table's columns against the EF models, and refuses otherwise.
- **Connect as the owner of the tables on both databases** (the role the migrations ran as) or a superuser. The workflow and
  occupancy tables have row-level security without `FORCE`, so their owner reads every row; `plan` reports — and every verb
  refuses — a role that row-level security would filter (and the tool's session runs with `row_security = off`, so such a
  read fails rather than returning fewer rows). `copy` disables this database's user triggers (the `ServiceOrders`
  row-version trigger would re-stamp `RowVersion`) inside each batch's own transaction, which only the owner can do.
- **A maintenance window: freeze writes** to NeutralOperator orders for the whole procedure — stop the gateway, or block
  `/api/v/neutral-operator/**` and the kernel's workflow routes at it. Each copy batch into a table with user triggers
  (`ServiceOrders`, `ServiceOrderTargets`) holds that table's exclusive lock until it commits.
- **A broker on this host** when an approval is awaiting the kernel's apply (`occupancy.apply.state = awaiting-apply`), so
  its staged batch is delivered from here (see [The approval saga](#the-approval-saga-xd01-383)).
- **A backup of both databases** (`pg_dump`) before `copy` and again before `remove`.

### Runbook

1. **Plan** — `plan`. Resolve every conflict and refusal it lists until it exits 0 (`outbox.pending`: decide on
   `--include-pending-outbox`; `order-types.not-moved`: re-create the override here if it is still wanted).
   *Rollback*: nothing to roll back.
2. **Copy** — `copy` (with `--include-pending-outbox` when `plan` reported pending messages: this host's relay delivers
   them; if the kernel's relay delivers its copy too, the consumer drops the second by its message id). Re-run it until it
   exits 0; a second run copies nothing. *Rollback*: the kernel is untouched and still authoritative; restore this
   database from its backup (or drop and re-create it) before trying again.
3. **Verify** — `verify` must exit 0. *Rollback*: as for copy.
4. **Remove** — take the second backup of the kernel's database, then `remove --confirm`, still inside the window and
   **before this host serves anything**. Keep this host stopped (or at least unreachable and not yet started over the
   copy) from step 2 until here: once it runs, it legitimately writes to the copied rows — its approval-saga sweeper stamps
   `TimedOutAt` on a pending saga older than `Saga:ApplyTimeout`, and every request changes orders — so `verify` would
   report differences and `remove` would refuse (exit 3), correctly, since it cannot tell those writes from rows the copy
   missed. `remove` refuses (exit 3, nothing deleted) if anything changed on either side since step 3.
   *Rollback*: there is no reverse move — restore the kernel's database from the backup taken in this step; this database
   still holds every row.
5. **Switch** — point the gateway's `Destinations:NeutralOperator:Shared` (and each silo) at this host and drop
   `neutral-operator` from the kernel's `GeoAssets:Modules`; start this host and restart the kernel. This host now serves
   the moved orders. *Rollback*: restore the kernel's NeutralOperator rows from the step-4 backup (or re-run this tool in
   the other direction by hand — it has no reverse verb), list the module on the kernel again and point the gateway back;
   anything this host wrote after the switch is then lost unless copied back.
6. End the window.

The order matters: `remove` must run while both databases are still exactly as `verify` saw them. If this host was started
over the copy before `remove` (or an approval saga already timed out there), restore this database from its step-2 backup,
re-run `copy` and `verify`, and then `remove` before starting it again.

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

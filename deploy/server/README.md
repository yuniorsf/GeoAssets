# GeoAssets.Server — local Docker

Runs `GeoAssets.Server` itself as a Docker container on your machine, backed
by a PostGIS container, exporting OTLP directly to New Relic (no Collector —
see `deploy/otel/README.md` "Configuring GeoAssets.Server directly against a
vendor" for why no Collector is needed for New Relic).

This is a local stand-in for the production secret-injection path this repo
doesn't have yet (no App Service/Container App/CI deploy job exists — see
XD01 for tracking). `NEW_RELIC_LICENSE_KEY` here comes from the same Key
Vault-backed file the local OTel Collector uses, not a hardcoded value.

## Running it

```bash
# 1. New Relic license key — pulled from Key Vault, shared with deploy/otel/
#    (optional: without deploy/otel/.env the stack still starts, exporting no telemetry to a vendor)
cd ../otel
az login   # if not already
./fetch-secrets.sh
cd ../server

# 2. Local Postgres password — not a vendor secret, no Key Vault entry
cp .env.example .env   # fill in POSTGRES_PASSWORD

# 3. The Postgres data volume — external, so no `docker compose down -v` can delete it. Once per machine:
docker volume create server_geoassets-postgres-data

# 4. Build and start
docker compose up --build
```

The stack is part of the Compose project **`geoassets`** (`name: geoassets` in `docker-compose.yaml`), on its network
`geoassets_default`; `deploy/neutral-operator` and `deploy/web` declare the same project. To run all of them as one, from
the repo root:

```bash
docker compose -f deploy/docker-compose.yaml up --build -d   # server + neutral-operator + web (include:)
docker compose -f deploy/docker-compose.yaml down            # never -v; the data volume is external anyway
```

Each included file keeps reading its own `.env` (`deploy/server/.env`, `deploy/neutral-operator/.env`). Starting one file
on its own (`docker compose up` here) works too and lands in the same project: Compose then lists the other files'
containers as "orphans" — harmless; don't pass `--remove-orphans` unless you mean to stop them. Every published port is
bound to `127.0.0.1` only: Postgres **5433**, the kernel **8080**, the gateway **8090** (and `deploy/web` **8081**,
`deploy/neutral-operator` **8082**). The kernel and the gateway have a compose healthcheck on `GET /healthz` (over bash's
`/dev/tcp`; the images ship no curl), and the gateway starts once the kernel is healthy. The containers run as the
image's unprivileged `app` user (uid 1654).

The PostgreSQL hosts' images (the kernel, `GeoAssets.NeutralOperator.Server`, `GeoAssets.FieldService.Server`) set
`PGGSSENCMODE=disable` (XD01-469): GeoAssets never reaches PostgreSQL over Kerberos and the images ship no
`libgssapi_krb5.so.2`, so Npgsql's default GSS encryption probe only logged `Cannot load library libgssapi_krb5.so.2` at
startup. It applies to every connection string the container uses, `.env` and Key Vault ones included; a connection
string's own `GSS Encryption Mode=…` still overrides it.

A machine that ran this stack before the `geoassets` project name keeps its data: the volume is still
`server_geoassets-postgres-data` (the old `server` project's), now declared `external`. Stop the old projects first
(`docker compose -p server down`, likewise `-p neutral-operator` and `-p web`, without `-v`) — their containers carry the
same `container_name`s.

**Distributor silos (XD01-447).** `deploy/distributor-silo` runs one more `GeoAssets.Server` per distribution company
(container `geoasset-server-<company>`, its own Compose project `geoassets-silo-<company>`) on this stack's network and
Postgres container, with its own database `geoassets_<company>` (`provision-database.sh <company>` creates it and applies
the migrations below). This stack's gateway reads their `X-Tenant-Domain` routes from the git-ignored
`deploy/distributor-silo/gateway-silos.env` when it exists, so a redeploy keeps them (XD01-454). See
`deploy/distributor-silo/README.md`.

## The gateway (XD01-384)

The stack also runs `GeoAssets.Gateway` (service `gateway`, host port **8090**, `GET /healthz`), the one origin the web
client (`deploy/web`, `GEOASSETS_GATEWAY_URL=http://localhost:8090`) talks to: every kernel API unchanged (`/api/**`,
`/wms`, `/wfs` → `server:8080`) and the NeutralOperator vertical at `/api/v/neutral-operator/**` (rewritten to `/api/**`).
It forwards the bearer token untouched (it authorizes nothing), answers CORS for `WEB_ORIGIN` (default
`http://localhost:8081`) and the dev origins, and rate-limits each caller (organization claim, else token subject, else
client IP) with two buckets (XD01-393): API calls (`GATEWAY_RATE_LIMIT_API_TOKENS`, default 500, refilled 10/s) and WMS/WFS
tiles (`GATEWAY_RATE_LIMIT_TILE_TOKENS`, default 1000, refilled 100/s) — see `docs/perf/gateway-rate-limits.md`; the former
`GATEWAY_RATE_LIMIT_TOKENS` is no longer read. The token's claims key a bucket only once its signature, issuer and lifetime validate
against the CIAM tenant (XD01-398): set `AzureAdCiam__Instance`/`AzureAdCiam__TenantId` in `.env` (the server reads the
same two); unset, the gateway only decodes the token and logs a startup warning. Here the kernel keeps hosting the NeutralOperator module (`GeoAssets__Modules__1`) and is
the gateway's NeutralOperator destination — see `deploy/neutral-operator/README.md` to switch to `NeutralOperator.Server`.

The kernel's default `GeoAssets:Modules` is `[ "postgres-spatial" ]` (XD01-385); an unknown module id — such as the retired
`ip-billing-bridge` — fails the startup with the list of known ids, so remove it from any older configuration. The município
grant sync/revoke endpoints are `GeoAssets.IpBilling.Server`'s (`POST /api/ip-billing/municipio-grants/sync|revoke`, through
the gateway's `/api/ip-billing/**` route, `IPBILLING_DESTINATION`); that host is not part of this stack. It needs
`GeoCoreServer__BaseUrl` = the kernel's **root** (e.g. `http://server:8080`, no `/api/geoassets` suffix) and its
`GeoAssets.ServiceCaller` credentials (`ServiceAuth__Enabled=true`, `ServiceAuth__Instance`, `ServiceAuth__TenantId`,
`ServiceAuth__ClientId`, `ServiceAuth__ClientSecret`, `ServiceAuth__Scope`; `ServiceAuth__Instance` is the kernel's CIAM
instance, `https://<tenant>.ciamlogin.com/`, and defaults to `AzureAdCiam__Instance` — XD01-452): it provisions the grants through the kernel's generic
`/api/identity/organization-grants` API and publishes its `ip-billing:*` permissions
(`PUT /api/identity/verticals/ip-billing/permissions`) — until it has, its `/readyz` answers 503. Município saves queue
their grant change in its `MunicipioGrantSyncRequests` outbox and a dispatcher sends it (`GrantSync__*`, XD01-395); a pruner
deletes finished requests (XD01-400): completed ones after `GrantSync__CompletedRetention` (default `30.00:00:00`), failed
ones only when `GrantSync__FailedRetention` is set (default empty = kept), `GrantSync__PruneBatchSize` (500) rows at a time
every `GrantSync__PruneInterval` (`01:00:00`, first run soon after start) — never a município's latest request (its
`grantSyncState`) nor an unfinished one; deletions are counted on `geoassets.ipbilling.grant_sync_pruned`.
`GrantSync__Enabled=false` stops both. Each batch walks the primary key and probes the `(MunicipioId, Id)` index, never a
sequential scan: at 5,570 municípios, a backlog batch took about 0.5 ms and a run with nothing due about 2 ms (XD01-405,
plans in `docs/perf/XD01-405-grant-sync-prune-plan.md`).

## PMTiles and COG tile sources (XD01-459)

The kernel image carries the GDAL plugin (`plugins/providers/GeoAssets.Provider.Gdal/`, module `gdal-cloud-native`,
about 150 MB of native GDAL for the image's RID; build with `--build-arg INCLUDE_GDAL_PLUGIN=false` to leave it out).
Nothing is served until a file is configured: mount it read-only into the `server` service and name it under an id
(a tile key segment), e.g. `GeoAssets__Gdal__PMTiles__basemap__Path=/data/basemap.pmtiles` and
`GeoAssets__Gdal__Cog__ortho__Path=/data/ortho.tif` (a missing file fails the startup). Each is served at
`/tiles/sources/{id}/{v}/{z}/{x}/{y}.mvt|.png` and listed in `/tiles/style.json` (`features:read`, through the gateway's
`/tiles/**` route and tile rate limit). They are deployment-wide: every caller with `features:read`, of any organization,
reads all of them, so never configure one organization's data here.

## Token audiences of the vertical hosts (XD01-390)

`GeoAssets.IpBilling.Server` and `GeoAssets.NeutralOperator.Server` validate the caller's bearer token against **their own**
API registration (`AzureAdCiam:ClientId` = that host's API) and then forward the same token to the kernel's caller-scoped
identity routes (`GET /api/identity/me`, `GET /api/identity/policies`, `GET /api/identity/organization-grants/me`). The
kernel therefore has to accept their audiences too (CIAM model (b): one app registration per API):

| Host | Its `AzureAdCiam:ClientId` | Kernel setting |
|---|---|---|
| `GeoAssets.Server` (kernel) | the kernel's API | — (its own `ClientId` and `api://{ClientId}` are always accepted) |
| `GeoAssets.IpBilling.Server` | the IP-billing API | `AzureAdCiam__AdditionalAudiences__0=<IpBilling API client id>` |
| `GeoAssets.NeutralOperator.Server` | the NeutralOperator API | `AzureAdCiam__AdditionalAudiences__1=<NeutralOperator API client id>` |

List whatever the tokens carry as `aud` — the API's client id for v2.0 access tokens, or its App ID URI
(`api://<client id>`) — in this stack's `.env` (passed to the `server` container) or the kernel's environment. Empty (the
default) keeps the kernel accepting only its own audience, exactly as before; only the audience set widens — issuer,
signature and lifetime are validated as before.

In Entra External ID (CIAM): expose an API (an app registration with an Application ID URI and an `access_as_user` scope)
per host, grant the Web and MAUI client registrations that scope, and have the clients request each API's scope for that
host's routes: the Web client's `GeoAssetsServer:ApiScope` (the kernel) and `GeoAssetsIpBillingServer:ApiScope`, and — for
a vertical reached through the gateway, e.g. `/api/v/neutral-operator/` — a `GeoAssetsServer:RouteScopes` entry
(`PathPrefix` + `Scope`, Web and MAUI, XD01-397): a request under that prefix carries a token for that scope instead of
the kernel's, consented at sign-in; a blank `Scope` (the shipped default) is ignored. Steps for NeutralOperator:
`deploy/neutral-operator/README.md` "Token audiences". Alternatively (model (a)) a vertical host can use the kernel's API
audience as its own `ClientId` and the kernel needs no extra audience.

**Roles travel in the token.** The kernel takes the caller's roles from the forwarded token's `roles` claim (XD01-19,
`GeoAuthorizationService`), not from the local role-assignment table — a role name then grants its permissions from the
local `AppRole`/`RolePermission` tables. A token issued for a vertical API carries only the app roles assigned on *that*
API, so every vertical API registration (model (b)) needs the kernel API's app roles, matched by `value` (e.g.
`Administrator`), and the same user assignments. Role sync (`RoleSync:*`, `RoleSyncAzureSetup.md`) keeps them in step when
the vertical API's client id is in `RoleSync:TargetApplicationClientIds` and the Role Sync service principal is an owner of
the registration (its `Application.ReadWrite.OwnedBy`, `RoleSyncAzureSetup.md` step 6); without role sync, assign them by
hand. **Symptom:** sign-in and the token audience work, but every permission check of that host denies (403) — the kernel
sees the caller with no roles.

**IP-billing API registration (XD01-343).** Until it exists, leave the Web client's `GeoAssetsIpBillingServer:ApiScope`
blank (the shipped default): the IP-billing client then requests the kernel's `GeoAssetsServer:ApiScope`, and sign-in asks
for nothing extra — so `GeoAssets.IpBilling.Server` must validate the kernel's audience (model (a): its
`AzureAdCiam:ClientId` = the kernel's API). To give it its own audience (model (b)):

1. In the CIAM tenant, create an app registration for the IP-billing API, set its Application ID URI
   (`api://<IpBilling API client id>`) and expose a delegated `access_as_user` scope.
2. On the Web client (SPA) registration — and MAUI's, once MAUI calls IP billing — add that scope under *API permissions*
   and grant admin consent.
3. Give it the kernel API's app roles (same `value`s) and the same user assignments, and add it to role sync's targets
   (*Roles travel in the token* above) — otherwise every `ip-billing:*` permission check denies.
4. Set `GeoAssets.IpBilling.Server`'s `AzureAdCiam:ClientId` to the new client id (user secrets locally, app settings /
   Key Vault when deployed) and list it in the kernel's `AzureAdCiam:AdditionalAudiences` (table above).
5. Set the Web client's `GeoAssetsIpBillingServer:ApiScope` to `api://<IpBilling API client id>/access_as_user`
   (`wwwroot/appsettings.Local.json` locally; it is baked into the WASM bundle like `GeoAssetsServer:ApiScope`). It is
   consented at sign-in (`AdditionalScopesToConsent`, never a second resource in `DefaultAccessTokenScopes`) and the IP-billing
   client acquires its token silently. A scope naming a registration that doesn't exist fails sign-in with `AADSTS500011`.

**Symptom of a missing audience**: the vertical host answers **502** with problem code `kernel.token-rejected` ("The
GeoAssets kernel rejected the caller's token.", the detail carries the kernel's `error_description`, e.g. *The audience
'…' is invalid*) and logs one Error naming the kernel route, the token's `aud` and `iss` and the kernel's `error` — add that
`aud` to the kernel's `AzureAdCiam:AdditionalAudiences` and restart the kernel. (Before XD01-390 IpBilling.Server answered
this as `503 kernel.unavailable` and NeutralOperator.Server as an unhandled 500.)

**What a vertical host answers for a failed kernel call** (`KernelFailureTranslation`, both vertical hosts):

| Kernel answer | Vertical host answers |
|---|---|
| 401 to the caller's forwarded token | 502 `kernel.token-rejected` |
| 400 to what the host sent | 400 `kernel.rejected` |
| 409 `organization-grant.conflict` (an organization-grant upsert raced by concurrent writes of its key, XD01-394) | 409 `kernel.conflict` — safe to repeat (XD01-401; before, 503) |
| unreachable, 5xx, or 401/403 to the host's own credentials | 503 `kernel.unavailable` |

## First-run database setup

`GeoAssetsDbContext` (assets/PostGIS data) migrates itself automatically at
startup, before the app starts serving requests — `GeoAssets.Server`'s
`IAssetProvider` singleton is pre-warmed eagerly via
`PostgresProviderFactory.CreateAsync` (`AddWarmedPostgresAssetProviderAsync`,
`providers/GeoAssets.Provider.PostgreSQL/PostgresServiceExtensions.cs`), not
lazily on first resolve (XD01-173).

That is fine for an empty or small database. On a populated one, a migration that rewrites `geo_entity` is different.
XD01-432's `geom_3857` and XD01-457's `geom_3857_centroid` hold an `ACCESS EXCLUSIVE` lock for about 1 min and 30 s
per million rows respectively, on native hardware. Under the startup's default 30 s command timeout, such a migration is retried seven times and
fails the startup after about 4.5 min, without applying anything. Apply those migrations by hand, with writes frozen,
**before** you roll the new image: see
[`docs/runbooks/geo-entity-table-rewrites.md`](../../docs/runbooks/geo-entity-table-rewrites.md) (XD01-467) for the
pre-checks, the window, the command, the verification queries and the rollback.

`ServiceOrderDbContext` (workflow tables) and `ProjectDbContext` (XD01-139
Project persistence) do **not** auto-migrate — apply each manually once,
from the repo root, after `postgres` is up:

```bash
dotnet ef database update \
  --project apps/GeoAssets.Server \
  --startup-project apps/GeoAssets.Server \
  --context ServiceOrderDbContext \
  --connection "Host=localhost;Port=5433;Database=geoassets;Username=postgres;Password=<POSTGRES_PASSWORD from .env>"

dotnet ef database update \
  --project apps/GeoAssets.Server \
  --startup-project apps/GeoAssets.Server \
  --context ProjectDbContext \
  --connection "Host=localhost;Port=5433;Database=geoassets;Username=postgres;Password=<POSTGRES_PASSWORD from .env>"
```

With the `neutral-operator` module enabled (this stack's default, `GeoAssets__Modules__1=neutral-operator`), also apply `NeutralOperatorDbContext` (XD01-380: pole
occupancies and occupancy change sets, schema `neutral_operator`) — **after** `ServiceOrderDbContext`,
whose tables its baseline adopts:

```bash
dotnet ef database update \
  --project verticals/NeutralOperator/GeoAssets.NeutralOperator.Persistence \
  --startup-project verticals/NeutralOperator/GeoAssets.NeutralOperator.Persistence \
  --context NeutralOperatorDbContext \
  --connection "Host=localhost;Port=5433;Database=geoassets;Username=postgres;Password=<POSTGRES_PASSWORD from .env>"
```

Since XD01-381 the `ServiceOrderDbContext` chain above also drops the FieldService vertical's tables
(`ContractorUsLedgerEntries`, `UsCatalogItems`, `field_contractor_us_postings`,
`field_icerp_paving_measurements`, `field_manhole_audits`) from this database — `GeoAssets.Server`
never used them. Its `DropFieldServiceTablesFromKernel` migration only drops empty tables: if any holds
a row it aborts with `Cannot drop <table> from the kernel database: it holds N row(s). Move them to
the FieldService database first.` and changes nothing.

`GeoAssets.FieldService.Server` (its own `FieldService` database, not part of this stack) applies, in
this order: its own `ServiceOrderDbContext` chain, `NeutralOperatorDbContext`, then
`FieldServiceDbContext` (XD01-381, schema `field_service`), whose baseline adopts the kernel-created
tables:

```bash
dotnet ef database update \
  --project apps/GeoAssets.FieldService.Server \
  --startup-project apps/GeoAssets.FieldService.Server \
  --context ServiceOrderDbContext \
  --connection "<FieldService connection string>"

dotnet ef database update \
  --project verticals/NeutralOperator/GeoAssets.NeutralOperator.Persistence \
  --startup-project verticals/NeutralOperator/GeoAssets.NeutralOperator.Persistence \
  --context NeutralOperatorDbContext \
  --connection "<FieldService connection string>"

dotnet ef database update \
  --project verticals/FieldService/GeoAssets.FieldService.Persistence \
  --startup-project verticals/FieldService/GeoAssets.FieldService.Persistence \
  --context FieldServiceDbContext \
  --connection "<FieldService connection string>"
```

### Duplicate active organization grants (XD01-394)

`GeoIdentityDbContext` (identity, roles, organization grants; migrations in `apps/GeoAssets.Server/Migrations`) is
applied the same way, by hand:

```bash
dotnet ef database update \
  --project apps/GeoAssets.Server \
  --startup-project apps/GeoAssets.Server \
  --context GeoIdentityDbContext \
  --connection "Host=localhost;Port=5433;Database=geoassets;Username=postgres;Password=<POSTGRES_PASSWORD from .env>"
```

Its `EnforceOneActiveOrganizationGrantPerKey` migration adds the unique index `UX_OrganizationGrants_ActiveKey`: at most
one grant flagged active (expired or not) per key `(GranteeOrganizationId, ResourceOrganizationId, ResourceType,
ScopeId)`, a null resource type or scope counting as one value (`NULLS NOT DISTINCT`). Before the XD01-394 fix two
concurrent upserts of one key could both insert, so a database may already hold duplicates. The migration checks first
and, if any key has several active grants, aborts with `Cannot enforce one active organization grant per key: N key(s)
have several active grants: (grantee …, resource …, resourceType …, scopeId …): grants <id>, <id>; …` (each key's grant
ids oldest first) and changes nothing. It never deactivates a grant itself.

To clean up, review the named grants. Then keep the oldest active grant of each key (earliest `GrantedAt`, then lowest
`Id`) and deactivate the rest. Deactivate rather than delete: grants are never hard-deleted. Record each deactivation on
the grant-sync outbox so the FieldService mirror (XD01-290) drops the grant too:

```sql
BEGIN;

WITH ranked AS (
    SELECT "Id",
           row_number() OVER (
               PARTITION BY "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId"
               ORDER BY "GrantedAt", "Id") AS position
      FROM "OrganizationGrants"
     WHERE "IsActive"
),
deactivated AS (
    UPDATE "OrganizationGrants" AS g
       SET "IsActive" = false
      FROM ranked AS r
     WHERE g."Id" = r."Id"
       AND r.position > 1
 RETURNING g."Id", g."GranteeOrganizationId", g."ResourceOrganizationId", g."ResourceType", g."ScopeId", g."ExpiresAt"
)
INSERT INTO "OrganizationGrantSyncOutbox"
       ("GrantId", "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId", "IsActive", "ExpiresAt", "RecordedAtUtc")
SELECT "Id", "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId", false, "ExpiresAt", now()
  FROM deactivated
RETURNING "GrantId", "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId";

COMMIT;
```

The surviving grant keeps its own actions, role and expiry. If one of the deactivated grants carried the ones you meant,
re-send that grant through `PUT /api/identity/organization-grants` after the migration: the upsert updates the
surviving grant in place. Then run the `database update` again.

Note the host-side port is **5433**, not 5432 — see the comment in `docker-compose.yaml`; this avoids colliding with any Postgres already running natively on your machine's 5432. The `server` container itself talks to `postgres` over the internal Docker network on the standard 5432, unaffected by this remap.

## What this doesn't solve

This is a local Docker stack, not a real deployment target. There's still no
CI/CD job, container registry push, or cloud host (App Service/Container
App/etc.) wired up anywhere in this repo — that remains an open gap if/when
GeoAssets.Server needs to run somewhere other than a developer's machine.

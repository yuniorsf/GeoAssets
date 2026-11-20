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
cd ../otel
az login   # if not already
./fetch-secrets.sh
cd ../server

# 2. Local Postgres password — not a vendor secret, no Key Vault entry
cp .env.example .env   # fill in POSTGRES_PASSWORD

# 3. Build and start
docker compose up --build
```

## The gateway (XD01-384)

The stack also runs `GeoAssets.Gateway` (service `gateway`, host port **8090**, `GET /healthz`), the one origin the web
client (`deploy/web`, `GEOASSETS_GATEWAY_URL=http://localhost:8090`) talks to: every kernel API unchanged (`/api/**`,
`/wms`, `/wfs` → `server:8080`) and the NeutralOperator vertical at `/api/v/neutral-operator/**` (rewritten to `/api/**`).
It forwards the bearer token untouched (it authorizes nothing), answers CORS for `WEB_ORIGIN` (default
`http://localhost:8081`) and the dev origins, and rate-limits per organization (`GATEWAY_RATE_LIMIT_TOKENS` per 10 s while
every caller shares one bucket). Here the kernel keeps hosting the NeutralOperator module (`GeoAssets__Modules__1`) and is
the gateway's NeutralOperator destination — see `deploy/neutral-operator/README.md` to switch to `NeutralOperator.Server`.

The kernel's default `GeoAssets:Modules` is `[ "postgres-spatial" ]` (XD01-385); an unknown module id — such as the retired
`ip-billing-bridge` — fails the startup with the list of known ids, so remove it from any older configuration. The município
grant sync/revoke endpoints are `GeoAssets.IpBilling.Server`'s (`POST /api/ip-billing/municipio-grants/sync|revoke`, through
the gateway's `/api/ip-billing/**` route, `IPBILLING_DESTINATION`); that host is not part of this stack. It needs
`GeoCoreServer__BaseUrl` = the kernel's **root** (e.g. `http://server:8080`, no `/api/geoassets` suffix) and its
`GeoAssets.ServiceCaller` credentials (`ServiceAuth__Enabled=true`, `ServiceAuth__TenantId`, `ServiceAuth__ClientId`,
`ServiceAuth__ClientSecret`, `ServiceAuth__Scope`): it provisions the grants through the kernel's generic
`/api/identity/organization-grants` API and publishes its `ip-billing:*` permissions
(`PUT /api/identity/verticals/ip-billing/permissions`) — until it has, its `/readyz` answers 503.

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
host's routes (the Web client's `GeoAssetsServer:ApiScope`, `GeoAssetsIpBillingServer:ApiScope`). Alternatively (model
(a)) a vertical host can use the kernel's API audience as its own `ClientId` and the kernel needs no extra audience.

**Symptom of a missing audience**: the vertical host answers **502** with problem code `kernel.token-rejected` ("The
GeoAssets kernel rejected the caller's token.", the detail carries the kernel's `error_description`, e.g. *The audience
'…' is invalid*) and logs one Error naming the kernel route, the token's `aud` and `iss` and the kernel's `error` — add that
`aud` to the kernel's `AzureAdCiam:AdditionalAudiences` and restart the kernel. (Before XD01-390 IpBilling.Server answered
this as `503 kernel.unavailable` and NeutralOperator.Server as an unhandled 500.)

## First-run database setup

`GeoAssetsDbContext` (assets/PostGIS data) migrates itself automatically at
startup, before the app starts serving requests — `GeoAssets.Server`'s
`IAssetProvider` singleton is pre-warmed eagerly via
`PostgresProviderFactory.CreateAsync` (`AddWarmedPostgresAssetProviderAsync`,
`providers/GeoAssets.Provider.PostgreSQL/PostgresServiceExtensions.cs`), not
lazily on first resolve (XD01-173).

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

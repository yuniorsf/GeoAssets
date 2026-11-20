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
every caller shares one bucket). Here the kernel keeps hosting the NeutralOperator module (`GeoAssets__Modules__2`) and is
the gateway's NeutralOperator destination — see `deploy/neutral-operator/README.md` to switch to `NeutralOperator.Server`.

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

With the `neutral-operator` module enabled (this stack's default, `GeoAssets__Modules__2=neutral-operator`), also apply `NeutralOperatorDbContext` (XD01-380: pole
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

Note the host-side port is **5433**, not 5432 — see the comment in `docker-compose.yaml`; this avoids colliding with any Postgres already running natively on your machine's 5432. The `server` container itself talks to `postgres` over the internal Docker network on the standard 5432, unaffected by this remap.

## What this doesn't solve

This is a local Docker stack, not a real deployment target. There's still no
CI/CD job, container registry push, or cloud host (App Service/Container
App/etc.) wired up anywhere in this repo — that remains an open gap if/when
GeoAssets.Server needs to run somewhere other than a developer's machine.

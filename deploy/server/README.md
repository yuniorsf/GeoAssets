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

## First-run database setup

`GeoAssetsDbContext` (assets/PostGIS data) migrates itself automatically the
first time the app resolves `IAssetProvider`
(`PostgresProviderFactory.Create`, `providers/GeoAssets.Provider.PostgreSQL/PostgresProviderFactory.cs:38`).

`ServiceOrderDbContext` (workflow tables) does **not** auto-migrate — apply
it manually once, from the repo root, after `postgres` is up:

```bash
dotnet ef database update \
  --project apps/GeoAssets.Server \
  --startup-project apps/GeoAssets.Server \
  --context ServiceOrderDbContext \
  --connection "Host=localhost;Port=5432;Database=geoassets;Username=postgres;Password=<POSTGRES_PASSWORD from .env>"
```

## What this doesn't solve

This is a local Docker stack, not a real deployment target. There's still no
CI/CD job, container registry push, or cloud host (App Service/Container
App/etc.) wired up anywhere in this repo — that remains an open gap if/when
GeoAssets.Server needs to run somewhere other than a developer's machine.

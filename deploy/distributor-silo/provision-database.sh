#!/usr/bin/env bash
# Provisions one company silo's database (XD01-447): creates geoassets_<company> in the running deploy/server Postgres
# container when it does not exist yet, then applies the kernel's four EF Core migration chains in the order the server
# tests migrate a fresh database (tests/GeoAssets.Server.Tests/Routing/ServerHostFixture.cs):
#
#   1. GeoAssetsDbContext     (assets / PostGIS)  — the silo would also migrate it at startup (catalog pool); applied here
#                                                   so the database is complete before the first start
#   2. GeoIdentityDbContext   (identity, roles, organizations, grants) — not migrated at startup; before 3: the
#                                                   ServiceOrders row-level-security policies select from "OrganizationGrants"
#   3. ServiceOrderDbContext  (workflow tables, outbox/inbox, target ledger) — not migrated at startup
#   4. ProjectDbContext       (projects) — not migrated at startup
#
# Re-runnable: an existing database is kept and `dotnet ef database update` applies only what is pending.
#
# Usage, from anywhere in the repo checkout:
#   deploy/distributor-silo/provision-database.sh <company>        e.g. coelba
#
# Environment (all optional):
#   SERVER_ENV_FILE     the deploy/server env file holding POSTGRES_PASSWORD   (default: deploy/server/.env)
#   POSTGRES_PASSWORD   used instead of reading SERVER_ENV_FILE
#   POSTGRES_CONTAINER  the deploy/server Postgres container                   (default: PostgresL)
#   POSTGRES_HOST_PORT  its published host port, for dotnet ef               (default: 5433)
#   MIGRATION_COMMAND_TIMEOUT  seconds one migration statement may run (default: 1800; 0 = no limit). Npgsql's default
#                       30 s is too short for a migration that rewrites geo_entity on a large company (XD01-449: the
#                       geom_3857 generated column of XD01-432 on COELBA's 784 k features).
#
# Requires docker and the dotnet-ef tool (`dotnet tool install --global dotnet-ef`). Prints no secret.
set -euo pipefail

usage() {
  echo "Usage: $0 <company>   (lowercase letters, digits and '_', starting with a letter; e.g. coelba)" >&2
  exit 1
}

[[ $# -eq 1 ]] || usage
company="$1"
[[ "$company" =~ ^[a-z][a-z0-9_]{0,39}$ ]] || usage

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
server_env_file="${SERVER_ENV_FILE:-$repo_root/deploy/server/.env}"
postgres_container="${POSTGRES_CONTAINER:-PostgresL}"
postgres_host_port="${POSTGRES_HOST_PORT:-5433}"
database="geoassets_${company}"

# The password: the environment's, else the last POSTGRES_PASSWORD= line of the deploy/server env file (surrounding
# quotes removed). Never printed.
password="${POSTGRES_PASSWORD:-}"
if [[ -z "$password" ]]; then
  [[ -r "$server_env_file" ]] || { echo "Cannot read $server_env_file (set SERVER_ENV_FILE or POSTGRES_PASSWORD)." >&2; exit 2; }
  password="$(sed -n 's/^[[:space:]]*POSTGRES_PASSWORD[[:space:]]*=[[:space:]]*//p' "$server_env_file" | tail -n 1)"
  password="${password%$'\r'}"
  if [[ "$password" == \"*\" || "$password" == \'*\' ]]; then
    password="${password:1:${#password}-2}"
  fi
fi
[[ -n "$password" ]] || { echo "POSTGRES_PASSWORD is empty in $server_env_file." >&2; exit 2; }
export PGPASSWORD="$password"

command -v docker >/dev/null || { echo "docker is not on the PATH." >&2; exit 2; }
dotnet ef --version >/dev/null 2>&1 || { echo "dotnet-ef is missing: dotnet tool install --global dotnet-ef" >&2; exit 2; }

if [[ "$(docker inspect -f '{{.State.Running}}' "$postgres_container" 2>/dev/null || true)" != "true" ]]; then
  echo "The Postgres container '$postgres_container' is not running: start deploy/server first (see deploy/server/README.md)." >&2
  exit 2
fi

# psql inside the container (PGPASSWORD passed by name, so its value never appears on a command line).
psql_in_container() {
  docker exec -e PGPASSWORD "$postgres_container" psql -v ON_ERROR_STOP=1 -U postgres -d postgres "$@"
}

attempt=0
until docker exec "$postgres_container" pg_isready -U postgres >/dev/null 2>&1; do
  attempt=$((attempt + 1))
  if [[ $attempt -ge 60 ]]; then
    echo "$postgres_container did not accept connections after 60 attempts." >&2
    exit 2
  fi
  sleep 1
done

if [[ "$(psql_in_container -tAc "SELECT 1 FROM pg_database WHERE datname = '${database}'")" == "1" ]]; then
  echo "Database ${database} already exists: keeping it."
else
  psql_in_container -qc "CREATE DATABASE \"${database}\""
  echo "Created database ${database}."
fi

# The silo database as ConnectionStrings:GeoAssets, which both design-time paths read: GeoAssetsDbContextFactory (the
# provider project) and GeoAssets.Server's own host (dotnet ef builds and starts its Program up to Build()) — the latter
# with Production settings and no module (the module list reader skips blank entries).
connection="Host=localhost;Port=${postgres_host_port};Database=${database};Username=postgres;Password=${password};Command Timeout=${MIGRATION_COMMAND_TIMEOUT:-1800}"
export ASPNETCORE_ENVIRONMENT=Production
export DOTNET_ENVIRONMENT=Production
export GeoAssets__Modules__0=
export GeoAssets__Modules__1=
export GeoAssets__Modules__2=
export ConnectionStrings__GeoAssets="$connection"

cd "$repo_root"
echo "Applying GeoAssetsDbContext to ${database}…"
dotnet ef database update \
  --project providers/GeoAssets.Provider.PostgreSQL \
  --startup-project providers/GeoAssets.Provider.PostgreSQL \
  --context GeoAssetsDbContext \
  --connection "$connection"

for context in GeoIdentityDbContext ServiceOrderDbContext ProjectDbContext; do
  echo "Applying ${context} to ${database}…"
  dotnet ef database update \
    --project apps/GeoAssets.Server \
    --startup-project apps/GeoAssets.Server \
    --context "$context" \
    --connection "$connection"
done

applied="$(docker exec -e PGPASSWORD "$postgres_container" psql -v ON_ERROR_STOP=1 -U postgres -d "$database" -tAc \
  'SELECT count(*) FROM "__EFMigrationsHistory"')"
echo "Done: ${database} has ${applied} applied migration(s). Start the silo: see deploy/distributor-silo/README.md."

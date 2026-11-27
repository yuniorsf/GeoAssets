# Distributor silos — one kernel per distribution company (XD01-447)

A repeatable way to run one `GeoAssets.Server` kernel **silo** per distribution company (COELBA, CEMAR, CEPISA, …) next
to the local `deploy/server` stack, and to load the company's network into it with
`tools/GeoAssets.Tools.NetworkImport` (epic XD01-441).

| Per company | Value (COELBA) |
|---|---|
| Env file (git-ignored) | `deploy/distributor-silo/coelba.env` |
| Compose project | `geoassets-silo-coelba` |
| Container | `geoasset-server-coelba` (on `geoassets_default`, port 8080) |
| Database | `geoassets_coelba` in the deploy/server Postgres (`PostgresL`) |
| Host port | `127.0.0.1:8180` |
| Tenant domain (gateway `X-Tenant-Domain`) | `coelba.local` |
| Modules | `postgres-spatial` only (no `neutral-operator`) |

A silo is the same image as the shared kernel with its own database. It reuses the deploy/server Postgres container and
joins that stack's network, `geoassets_default` (deploy/server declares Compose project `geoassets`), as an **external**
network. It creates no network or volume, and `down` never deletes the shared Postgres volume
`server_geoassets-postgres-data`.

Files:

| File | What |
|---|---|
| `docker-compose.yaml` | The silo service, parameterised by the env file (`COMPANY`, `TENANT_DOMAIN`, `SILO_PORT`, `POSTGRES_PASSWORD`, `AzureAdCiam__*`) |
| `.env.example` | Every variable; copy it to `<company>.env` |
| `provision-database.sh <company>` | Creates `geoassets_<company>` (idempotent) and applies the kernel's migration chains |
| `gateway-silos.env.example` | The silo routes of the deploy/server gateway, which reads `gateway-silos.env` on every `up` |
| `.gitignore` | Keeps `.env`, `*.env` (the company files and `gateway-silos.env`) out of git |

## Prerequisites

- The `deploy/server` stack running (`deploy/server/README.md`), so `PostgresL` is up and the `geoassets_default` network
  exists. Check with `docker network ls --filter name=geoassets_default` and `docker ps --filter name=PostgresL`.
- `deploy/server/.env` with `POSTGRES_PASSWORD` (the provisioning script reads it) and the kernel's CIAM settings
  (`AzureAdCiam__Instance`, `AzureAdCiam__TenantId`, `AzureAdCiam__ClientId`).
- The .NET 10 SDK and the EF Core tool: `dotnet tool install --global dotnet-ef`.
- `curl` and `jq`, for the organization call below.
- A CIAM app registration with the kernel API's `GeoAssets.ServiceCaller` app role, for the CLI (see [Credentials](#credentials-the-geoassetsservicecaller-registration)).
- Disk and memory: every silo is one more .NET process, capped at `SILO_MEMORY_LIMIT` (default `6g`; see
  [Measurements](#measurements-xd01-448-xd01-449)). An organization with more than 50,000 features is served from PostGIS,
  not cached whole (XD01-449): COELBA's METROPOLITANA (784 k features) runs in about 200 MiB. The database needs about
  11 KB per feature (8.5 GB for METROPOLITANA).

## Provision a company (COELBA)

Run every command from the repo root.

```bash
# 1. The company's env file. Fill in POSTGRES_PASSWORD and the AzureAdCiam__* values from deploy/server/.env.
cp deploy/distributor-silo/.env.example deploy/distributor-silo/coelba.env
#    COMPANY=coelba  TENANT_DOMAIN=coelba.local  SILO_PORT=8180 (the example's defaults)

# 2. The database and its migrations (deploy/server must be up).
deploy/distributor-silo/provision-database.sh coelba

# 3. Build and start the silo.
docker compose -f deploy/distributor-silo/docker-compose.yaml --env-file deploy/distributor-silo/coelba.env up --build -d

# 4. Wait for it to report healthy, then check it.
docker ps --filter name=geoasset-server-coelba --format '{{.Names}} {{.Status}}'
curl -fsS http://127.0.0.1:8180/healthz
```

### What `provision-database.sh` does

1. Reads `POSTGRES_PASSWORD` from `deploy/server/.env` (or from `SERVER_ENV_FILE`, or from the `POSTGRES_PASSWORD`
   environment variable). It never prints the password and passes it to `docker exec` by name, never as a value.
2. Waits for `PostgresL` (`pg_isready`). Creates `geoassets_<company>` with `docker exec PostgresL psql` if it does not
   exist yet.
3. Applies the four EF Core chains with the design-time settings GeoAssets.Server needs: `ASPNETCORE_ENVIRONMENT=Production`,
   `DOTNET_ENVIRONMENT=Production`, `GeoAssets__Modules__0=` / `__1=` / `__2=` (blank means no module), and
   `ConnectionStrings__GeoAssets` = the silo database on `localhost:5433`, with a 1,800 s command timeout
   (`MIGRATION_COMMAND_TIMEOUT`; Npgsql's 30 s default is too short for a migration that rewrites `geo_entity` on a large
   company — XD01-432's `geom_3857` took about 1 min 40 s on COELBA's 784 k features). It runs them in the order the server tests use
   on a fresh database (`tests/GeoAssets.Server.Tests/Routing/ServerHostFixture.cs`, lines 199–214):

```bash
# CONNECTION = Host=localhost;Port=5433;Database=geoassets_coelba;Username=postgres;Password=<POSTGRES_PASSWORD>
dotnet ef database update --project providers/GeoAssets.Provider.PostgreSQL --startup-project providers/GeoAssets.Provider.PostgreSQL --context GeoAssetsDbContext    --connection "$CONNECTION"
dotnet ef database update --project apps/GeoAssets.Server                  --startup-project apps/GeoAssets.Server                  --context GeoIdentityDbContext  --connection "$CONNECTION"
dotnet ef database update --project apps/GeoAssets.Server                  --startup-project apps/GeoAssets.Server                  --context ServiceOrderDbContext --connection "$CONNECTION"
dotnet ef database update --project apps/GeoAssets.Server                  --startup-project apps/GeoAssets.Server                  --context ProjectDbContext      --connection "$CONNECTION"
```

| Chain | Migrates at startup? | Why the script applies it |
|---|---|---|
| `GeoAssetsDbContext` (assets, PostGIS) | Yes. The catalog pool runs `MigrateAsync` (`PostgresProviderFactory.CreateAsync`). | So the database is complete before the first start. It is a no-op when the chain is already applied. |
| `GeoIdentityDbContext` (identity, organizations, grants) | No | `SeedGeoIdentityAsync` needs it at startup. It must run before `ServiceOrderDbContext`, because the ServiceOrders row-level-security policies select from `"OrganizationGrants"` (`apps/GeoAssets.Server/Migrations/20261031140000_AddServiceOrdersRowLevelSecurity.cs`). |
| `ServiceOrderDbContext` (workflow, outbox/inbox, target ledger) | No | `LoadRegistryFromDbAsync` reads it at startup. |
| `ProjectDbContext` (projects) | No | `SeedDefaultProjectsAsync` writes to it at startup. |

No `NeutralOperatorDbContext` or `FieldServiceDbContext` chain is needed, because a silo runs no vertical module. The
script can be re-run. After a ticket adds a kernel migration, run it again for every company. A silo started against a
database with a missing migration crashes at boot with `42703 column … does not exist`.

## Start, stop, logs

```bash
SILO="docker compose -f deploy/distributor-silo/docker-compose.yaml --env-file deploy/distributor-silo/coelba.env"
$SILO up --build -d       # build (one image for every silo: geoassets-server-silo:local) and start
$SILO stop                # stop, keep the container
$SILO start
$SILO logs -f kernel-silo
$SILO down                # remove the container; the database and the shared Postgres volume stay
```

**The project-name trick.** The project name is `geoassets-silo-${COMPANY}` (the file's `name:`), so every company is its
own Compose project. A second company's `up` never sees the first company's container as an orphan, and one company's
`down` touches nothing else: not another company, not the `geoassets` project's Postgres, kernel or gateway. `-p` overrides
the name if you need to (`docker compose -p geoassets-silo-coelba -f … --env-file …`). Always pass the same `-p` and env
file for one company. Never start a silo file under the `geoassets` project name (`-p geoassets`): its `down
--remove-orphans` would then stop the kernel stack.

The silo service is called `kernel-silo`, not `server`. On `geoassets_default` every service name is a DNS alias, and the
gateway reaches the shared kernel as `http://server:8080`. Address a silo only by its container name,
`geoasset-server-<company>`. The `kernel-silo` alias is ambiguous as soon as two silos run.

A silo has no `depends_on` on Postgres (that service belongs to another project). Start deploy/server first.

## Create the company organization

Every silo seeds the well-known organization `GeoAssets Default` (`00000000-0000-0000-0000-000000000001`) and the canonical
roles and permissions. Create the company's own organization with the kernel's identity API:

`POST /api/identity/organizations` (`apps/GeoAssets.Server/IdentityRestApiExtensions.cs`, `MapOrganizationsApi`).

- **Permission:** `organizations:edit`. A `GeoAssets.ServiceCaller` token satisfies every permission policy
  (`workflow/GeoAssets.Workflow.Hosting/GeoAuthorizationHandler.cs`), so the CLI's own credentials work. So does an
  Administrator user's token.
- **Body:** `OrganizationWriteDto`: `name`, `slug` (unique, at most 64 characters), `description`, `isActive`.
- **Answer:** `201 Created` with no body. `Location: /api/identity/organizations/{id}` carries the new id. The call also
  creates the organization's default project.

```bash
# A GeoAssets.ServiceCaller token: client credentials, the same flow and the same CIAM authority as the CLI.
TOKEN=$(curl -fsS "${GEOASSETS_SERVICEAUTH_INSTANCE%/}/$GEOASSETS_SERVICEAUTH_TENANT_ID/oauth2/v2.0/token" \
  --data-urlencode grant_type=client_credentials \
  --data-urlencode "client_id=$GEOASSETS_SERVICEAUTH_CLIENT_ID" \
  --data-urlencode "client_secret=$GEOASSETS_SERVICEAUTH_CLIENT_SECRET" \
  --data-urlencode "scope=$GEOASSETS_SERVICEAUTH_SCOPE" | jq -r .access_token)

ORG_ID=$(curl -fsS -o /dev/null -D - -X POST http://127.0.0.1:8180/api/identity/organizations \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"name":"COELBA","slug":"coelba","description":"Neoenergia Coelba — distribution network","isActive":true}' \
  | awk 'tolower($1) == "location:" { print $2 }' | tr -d '\r' | sed 's#.*/##')
echo "$ORG_ID"   # keep it; the CLI's --organization

# Later: list the silo's organizations.
curl -fsS http://127.0.0.1:8180/api/identity/organizations -H "Authorization: Bearer $TOKEN" | jq '.[] | {id, name, slug}'
```

Create the organization in the silo, not in the shared kernel. The silo's identity database is its own. Re-running the
`POST` with the same slug fails on the unique slug index, so look the id up with the `GET` above.

## Credentials: the `GeoAssets.ServiceCaller` registration

The CLI calls the kernel's bulk import (`POST /api/geoassets/organizations/{orgId}/feature-imports/{importId}/chunks|complete`)
and its organization-scoped feature reads. Both accept **only** the `GeoAssets.ServiceCaller` app role
(`FeatureImportRestApiExtensions`, `OrganizationScopedFeaturesRestApiExtensions`). A user token, even an administrator's,
gets 403. The silo validates tokens exactly like the shared kernel, because it has the same `AzureAdCiam__ClientId` (the
kernel API). A token for the kernel API therefore works on every silo and on the shared kernel. Accepted audiences are
the kernel API's client id and `api://<client id>` (`KernelAudiences`).

The CLI reads (`tools/GeoAssets.Tools.NetworkImport/AccessTokenSources.cs`):

| Variable | Value |
|---|---|
| `GEOASSETS_NETWORK_IMPORT_TOKEN` | A bearer token. When set, it is used instead of the five below. It expires after about an hour. |
| `GEOASSETS_SERVICEAUTH_INSTANCE` | The kernel's `AzureAdCiam__Instance`, e.g. `https://<tenant subdomain>.ciamlogin.com/`. The token is asked from `<instance><tenant id>`, never from `login.microsoftonline.com` (see *Symptoms*). |
| `GEOASSETS_SERVICEAUTH_TENANT_ID` | The CIAM tenant id |
| `GEOASSETS_SERVICEAUTH_CLIENT_ID` | The importer registration's client id |
| `GEOASSETS_SERVICEAUTH_CLIENT_SECRET` | Its client secret |
| `GEOASSETS_SERVICEAUTH_SCOPE` | `api://<kernel API client id>/.default`, or the kernel API's Application ID URI + `/.default` |

The registration steps follow, **as documentation only**. Run them yourself, as an Application Administrator of the CIAM
tenant (the same `az` conventions as `RoleSyncAzureSetup.md`). Never commit the secret. Keep it in a git-ignored env file,
for example `deploy/distributor-silo/network-import.env` (covered by `*.env`), and `source` it.

```bash
az login --tenant "<CIAM tenant id>" --allow-no-subscriptions

KERNEL_API_APP_ID="<kernel API client id>"            # deploy/server/.env AzureAdCiam__ClientId
KERNEL_API_SP_ID=$(az ad sp show --id "$KERNEL_API_APP_ID" --query id -o tsv)

# 1. Does the kernel API already define the app role? (NeutralOperator.Server / IpBilling.Server use it too.)
az ad app show --id "$KERNEL_API_APP_ID" --query "appRoles[?value=='GeoAssets.ServiceCaller'].{id:id,enabled:isEnabled}" -o table

# 1b. Only if it is missing. --app-roles REPLACES the whole list, so start from the current roles.
az ad app show --id "$KERNEL_API_APP_ID" --query appRoles > /tmp/kernel-app-roles.json
#    Append to the JSON array, with a fresh GUID (uuidgen):
#    { "allowedMemberTypes": ["Application"], "description": "Trusted internal service caller",
#      "displayName": "GeoAssets.ServiceCaller", "id": "<new GUID>", "isEnabled": true, "value": "GeoAssets.ServiceCaller" }
az ad app update --id "$KERNEL_API_APP_ID" --app-roles @/tmp/kernel-app-roles.json

SERVICE_CALLER_ROLE_ID=$(az ad app show --id "$KERNEL_API_APP_ID" \
  --query "appRoles[?value=='GeoAssets.ServiceCaller'].id | [0]" -o tsv)

# 2. The importer's own registration (client-credentials only: no redirect URI) and its service principal.
IMPORTER_APP_ID=$(az ad app create --display-name "GeoAssets Network Import (service caller)" \
  --sign-in-audience AzureADMyOrg --query appId -o tsv)
IMPORTER_SP_ID=$(az ad sp create --id "$IMPORTER_APP_ID" --query id -o tsv)

# 3. A client secret. Shown once; write it straight into the git-ignored env file, never to the terminal history.
printf 'GEOASSETS_SERVICEAUTH_CLIENT_SECRET=%s\n' \
  "$(az ad app credential reset --id "$IMPORTER_APP_ID" --append --display-name network-import --years 1 --query password -o tsv)" \
  >> deploy/distributor-silo/network-import.env

# 4. Assign the app role: this is the application permission, and it needs no separate consent.
az rest --method POST \
  --url "https://graph.microsoft.com/v1.0/servicePrincipals/$KERNEL_API_SP_ID/appRoleAssignedTo" \
  --body "{\"principalId\":\"$IMPORTER_SP_ID\",\"resourceId\":\"$KERNEL_API_SP_ID\",\"appRoleId\":\"$SERVICE_CALLER_ROLE_ID\"}"

# 5. The rest of the CLI's environment.
cat >> deploy/distributor-silo/network-import.env <<EOF
GEOASSETS_SERVICEAUTH_INSTANCE=<the kernel's AzureAdCiam__Instance, e.g. https://<tenant subdomain>.ciamlogin.com/>
GEOASSETS_SERVICEAUTH_TENANT_ID=<CIAM tenant id>
GEOASSETS_SERVICEAUTH_CLIENT_ID=$IMPORTER_APP_ID
GEOASSETS_SERVICEAUTH_SCOPE=api://$KERNEL_API_APP_ID/.default
EOF
```

Load the variables with `set -a; source deploy/distributor-silo/network-import.env; set +a`. If an existing
`GeoAssets.ServiceCaller` principal already exists (NeutralOperator.Server's `ServiceAuth__*`), it works as well. A
separate registration for the importer keeps its secret and its audit trail apart.

**Symptoms:** a `401` on every call means the silo's `AzureAdCiam__*` are unset or different from the kernel's, or the
token's scope names another API, or the token was asked from `login.microsoftonline.com`: the kernel validates the issuer of
its CIAM instance (`https://<tenant id>.ciamlogin.com/<tenant id>/v2.0`), so the silo's log shows `IDX40001: Issuer:
'https://login.microsoftonline.com/<tenant id>/v2.0', does not match` and the answer is `WWW-Authenticate: Bearer
error="invalid_token", error_description="The issuer '(null)' is invalid"`. A `403` on `feature-imports` means the token carries no `GeoAssets.ServiceCaller` in
`roles` (step 4 is missing, or a user token was used). The CLI exits with `3` and names the route and the status.

## Route the gateway to the silo

The gateway (`apps/GeoAssets.Gateway`) sends a request whose `X-Tenant-Domain` header names a configured silo to that silo:
`Destinations:GeoAssets:Silos:<tenant domain>` = its base address (`appsettings.json`; XD01-289). This covers
`/api/geoassets/**`, the kernel catch-all `/api/**`, `/wms` and `/wfs`. Any other request (no header, or an unlisted
domain) goes to the shared kernel, never to a silo. The deploy/server gateway reads the silos from the git-ignored
`deploy/distributor-silo/gateway-silos.env` (its `env_file`, optional):

```bash
cp deploy/distributor-silo/gateway-silos.env.example deploy/distributor-silo/gateway-silos.env
#   Destinations__GeoAssets__Silos__coelba.local=http://geoasset-server-coelba:8080   (one line per silo)

# Recreate only the gateway (kernel and Postgres untouched):
docker compose -f deploy/server/docker-compose.yaml up -d --no-deps gateway
# or, when you run the whole stack from deploy/docker-compose.yaml:
docker compose -f deploy/docker-compose.yaml up -d --no-deps gateway

# Check: the silo answers through the gateway.
curl -fsS -H 'X-Tenant-Domain: coelba.local' http://127.0.0.1:8090/api/identity/organizations -H "Authorization: Bearer $TOKEN" | jq length
```

Every `up` of deploy/server, or of `deploy/docker-compose.yaml`, reads the file again, so a redeploy keeps the silo routes
(XD01-454; before, they lived in an override file that every `up` had to pass, and a plain redeploy dropped them).
Compose recreates the gateway when the file changes. The repo `appsettings.json` keeps its placeholder silo
`acme.example.com`, which points at a host that does not exist. It answers 502 and is harmless. Without
`gateway-silos.env` the gateway has no other silo route.

**Rate limits and the CLI.** Through the gateway, the CLI's service-principal token gets one `Api` bucket (token subject,
since `OrganizationIdClaimType` is unset): 500 requests, refilled 10/s (`GATEWAY_RATE_LIMIT_API_TOKENS`,
`docs/perf/gateway-rate-limits.md`). The same principal shares that bucket across the shared kernel and every silo. The
import sends one request per chunk of at most 5000 features (gzip NDJSON, `FeatureImportLimits.MaxChunkSize`) and runs
them one after another, so COELBA METROPOLITANA's 160 chunks stay far below the limit. A `verify --sample` of several
hundred features can drain the bucket, though: the CLI gives a 429 only 3 attempts in all (back-off 1 s, then 2 s). The `complete`
call reloads the organization's catalog and can outlast a proxy timeout. **Prefer the silo's own port for imports**
(`--kernel-url http://127.0.0.1:8180`). It bypasses the gateway, its buckets and its timeouts, and needs no
`--tenant-domain`. Use the gateway (`--kernel-url http://127.0.0.1:8090 --tenant-domain coelba.local`) to test the
route itself.

## PROJ grid (SAD69 → SIRGAS 2000)

COELBA and CEMAR deliver in SAD69 / UTM. For an exact datum shift the CLI needs the IBGE grid `br_ibge_SAD69_003.tif`
(NTv2 converted to GeoTIFF by PROJ) in the directory given by `--proj-data`. Without it the transformer falls back to the
EPSG:15485 Helmert, about 5 m off, and `report.json` says `"positionAccuracy": "helmert"` instead of `"grid"`.

- If your PROJ install ships the IBGE grids, point `--proj-data` at its data directory. The Homebrew `proj` on the
  development Mac has them in `/opt/homebrew/share/proj` (`--proj-data /opt/homebrew/share/proj`).
- Otherwise get it from the PROJ-data package (`proj-data`, the `br_ibge` directory) or from `https://cdn.proj.org/br_ibge_SAD69_003.tif`,
  into a folder outside the repo checkout.
- **Licence:** IBGE (Instituto Brasileiro de Geografia e Estatística, ProGrid) under
  [Creative Commons Attribution Share-Alike 4.0](https://creativecommons.org/licenses/by-sa/4.0/), per PROJ-data's
  `br_ibge_README.txt`. Don't commit the grid to this repo. Credit IBGE wherever you redistribute the file.

## Load the network: inventory → plan → import → verify

The CLI (`dotnet run --project tools/GeoAssets.Tools.NetworkImport -c Release -- --help` prints the usage text) has
four verbs. `inventory` and `plan` are offline. `import` and `verify` call the silo. Exit codes: `0` ok, `1` usage, `2` input
error, `3` kernel failure.

```bash
NI="dotnet run --project tools/GeoAssets.Tools.NetworkImport -c Release --"
ROOT="<path to the delivery>/COELBA/Base Coelba Regional"   # one folder per region: CENTRO, METROPOLITANA, NORTE, …
OUT=~/geoassets-import/coelba                               # reports and the features file; outside the repo
PROJ_DATA=/opt/homebrew/share/proj
set -a; source deploy/distributor-silo/network-import.env; set +a

# 1. What the delivery holds; writes $OUT/inventory/inventory.json and data-request.md (send the latter to the company).
$NI inventory --root "$ROOT" --profile neoenergia-gse --out "$OUT/inventory"

# 2. A dry run into features.ndjson + report.json (counts per asset class, references by method, issues).
$NI plan --root "$ROOT" --profile neoenergia-gse --scope-prefix coelba --regions METROPOLITANA --include-geometry-only \
  --proj-data "$PROJ_DATA" --out "$OUT/plan-metro"

# 3. The import into the silo's organization. A re-run with the same --import-id and --checkpoint skips the chunks
#    already sent. --replace-scope deletes the scope's rows from other imports (a later, complete delivery).
$NI import --root "$ROOT" --profile neoenergia-gse --scope-prefix coelba --regions METROPOLITANA --include-geometry-only \
  --proj-data "$PROJ_DATA" --out "$OUT/import-metro" \
  --kernel-url http://127.0.0.1:8180 --organization "$ORG_ID" \
  --import-id coelba-metro-20261126 --checkpoint "$OUT/import-metro/checkpoint.json"

# 4. The first 50 (--sample) planned features exist in the silo with the same asset type.
$NI verify --kernel-url http://127.0.0.1:8180 --organization "$ORG_ID" \
  --features "$OUT/plan-metro/features.ndjson" --sample 50
```

To review a plan in QGIS or another GIS tool, add `--export flatgeobuf` (or `geoparquet`) to step 2: it also writes the
planned features as `features.fgb` (or `features.parquet`) next to `features.ndjson`, in EPSG:4326 with their `id`,
`name` and attributes (XD01-466).

`--include-geometry-only` also imports the layers `inventory` reports `geometryOnly` (a `.shp` without its `.dbf`).
COELBA's METROPOLITANA `REDE_BT` is one: without the flag its 236,659 LV segments are skipped and the import holds no LV
network at all (547,168 features instead of 783,827). Their ends are then resolved by snapping to the nearest pole, and
the features carry `import.completeness=geometry-only` until the delivery's `.dbf` arrives. Without `--proj-data` (or
with a PROJ install that lacks the IBGE grid) the CLI says `position accuracy helmert` at the end: about 4.9 m away from
the IBGE grid's reference transformation.

Through the gateway, add `--tenant-domain coelba.local` and use `--kernel-url http://127.0.0.1:8090`. Scopes are
`<scope-prefix>/<region>/<layer>` (for example `coelba/METROPOLITANA/REDE_MT`). Keep one `--scope-prefix` per company.
Import the other regions one `--regions` value at a time, each with its own `--import-id`.

## View the network: WMS (QGIS) and the map

Every imported feature belongs to the company's organization, and a caller sees only its organization's features plus
the shared ones. Entra CIAM tokens carry no organization claim, so the silo sets
`GeoAssets__Tenancy__OrganizationFromMembership=true` (XD01-471): a signed-in user's organization is the one their
identity record names. A user without one, and the import's service principal, see the shared features only, so their
WMS tiles stay empty.

Give a user access, once per silo:

1. The user signs in once against the silo (any request with their token, for example through the Web app or QGIS).
   That creates their identity record.
2. Their token carries an app role of the kernel API registration whose local role has `features:read`. The kernel
   reads roles from the token's `roles` claim and matches them by value to the seeded roles `ReadOnly`,
   `FieldTechnician`, `Supervisor` and `Administrator`. The registration defines only `Administrator` today; add a
   `ReadOnly` app role (value `ReadOnly`) to give read-only viewers access.
3. Assign them to the company's organization, with the service caller `$TOKEN` from *Create the company organization*:

```bash
USER_ID=$(curl -fsS "http://127.0.0.1:8180/api/identity/users/by-external-id/<user oid>" \
  -H "Authorization: Bearer $TOKEN" | jq -r .id)
curl -fsS -X PUT "http://127.0.0.1:8180/api/identity/users/$USER_ID" \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d "{\"displayName\":\"<name>\",\"isActive\":true,\"organizationId\":\"$ORG_ID\"}"
```

The PUT replaces the display name, the active flag and the organization together. A deactivated user gets no
organization.

In QGIS, add a WMS/WMTS connection:

| Setting | Value |
|---|---|
| URL | `http://127.0.0.1:8180/wms` (CEMAR `:8181`), or the gateway `http://127.0.0.1:8090/wms` with the HTTP header `X-Tenant-Domain: coelba.local` |
| Authentication | a user token for the kernel API (scope `api://<kernel API client id>/access_as_user`): method *OAuth2* (Authorization Code with PKCE, token URL `https://<tenant subdomain>.ciamlogin.com/<tenant id>/oauth2/v2.0/token`), or *API Header* (`Authorization: Bearer <token>`) for a token obtained elsewhere. OAuth2 needs a public-client (mobile and desktop) app registration with a loopback redirect such as `http://127.0.0.1:7070/`; the Web app's SPA registration cannot redeem a code outside a browser. |
| Layers | `geoassets:feature` (everything) or `geoassets:feature:<asset type id>` (one class), PNG, EPSG:4326, transparent |

Each asset type is drawn only from its zoom on, and a tile holds a bounded number of features, so zoom in to a city to
see poles and segments. `GetFeatureInfo` answers JSON.

In the Web app (XD01-473), add a provider **WMS (OGC)** in the provider pool:

| Field | Value |
|---|---|
| GeoAssets REST API URL | the gateway's root, `http://localhost:8090` (the layer requests `<url>/wms`) |
| OGC layer name | `geoassets:feature` |
| Tenant domain (silo, optional) | `coelba.local` (or `cemar.local`) |

Every tile request then carries `X-Tenant-Domain`, which the gateway routes to the silo, and the signed-in user's token,
which the Web app sends to the gateway's origin only. Use the root, not `/api/geoassets`: `/wms` counts against the
gateway's `tiles` rate limit, `/api/geoassets/wms` against the `Api` one, which a map of tiles exhausts. The layer is drawn
in the Leaflet render modes only (MapLibre mode skips it, like the kernel's own WMS).

## Add the next company (CEMAR, CEPISA)

No code or compose change is needed for the silo itself. Use another env file, another database, and one more gateway
line:

```bash
cp deploy/distributor-silo/.env.example deploy/distributor-silo/cemar.env
#   COMPANY=cemar  TENANT_DOMAIN=cemar.local  SILO_PORT=8181  (+ POSTGRES_PASSWORD, AzureAdCiam__* as for coelba)
deploy/distributor-silo/provision-database.sh cemar
docker compose -f deploy/distributor-silo/docker-compose.yaml --env-file deploy/distributor-silo/cemar.env up -d
echo 'Destinations__GeoAssets__Silos__cemar.local=http://geoasset-server-cemar:8080' >> deploy/distributor-silo/gateway-silos.env
docker compose -f deploy/server/docker-compose.yaml up -d --no-deps gateway
# then the organization (slug cemar) on http://127.0.0.1:8181, and the CLI with that port and organization id
```

| Company | `COMPANY` | `SILO_PORT` | Tenant domain | Profile (`--profile`) |
|---|---|---|---|---|
| COELBA (Neoenergia) | `coelba` | 8180 | `coelba.local` | `neoenergia-gse` |
| CEMAR (Equatorial Maranhão) | `cemar` | 8181 | `cemar.local` | `equatorial-qgis-municipal` (XD01-450; one region per municipality, e.g. `--regions shapes-Paco-do-Lumiar`) |
| CEPISA (Equatorial Piauí) | `cepisa` | 8182 | `cepisa.local` | Run `inventory` against an existing profile first. A delivery no profile fits needs a new profile in `plugins/ingestion/GeoAssets.Ingestion.Profiles` (a code change; the silo stays as is). |

`up` without `--build` reuses the image the first silo built (`geoassets-server-silo:local`). Rebuild with `--build`
after pulling kernel changes, then re-run `provision-database.sh` for every company before restarting the silos.

## Teardown

```bash
SILO="docker compose -f deploy/distributor-silo/docker-compose.yaml --env-file deploy/distributor-silo/coelba.env"
$SILO down                                     # the container only; never -v (a silo has no volume anyway)

# Remove its gateway line from deploy/distributor-silo/gateway-silos.env, then recreate the gateway:
docker compose -f deploy/server/docker-compose.yaml up -d --no-deps gateway

# Only to delete the company's data for good (irreversible). The shared volume and the other databases stay:
docker exec PostgresL psql -U postgres -d postgres -c 'DROP DATABASE "geoassets_coelba"'

# Once no silo is left: docker image rm geoassets-server-silo:local
```

Never run `docker compose down -v` on `deploy/server` and never `docker volume rm server_geoassets-postgres-data`. Every
silo's database lives in that volume, next to the shared kernel's.

## Measurements (XD01-448, XD01-449)

The scale gate ([`docs/perf/XD01-448-metropolitana-scale-gate.md`](../../docs/perf/XD01-448-metropolitana-scale-gate.md))
and the bounded organization catalog that fixed it
([`docs/perf/XD01-449-bounded-organization-catalog.md`](../../docs/perf/XD01-449-bounded-organization-catalog.md)). In short:

- `import` handles METROPOLITANA (783,827 features, 160 chunks) at about 1,100 features/s when inserting and 690/s when
  upserting, and resumes from its checkpoint. `verify` then passes (2,000 of 2,000 sampled).
- The silo starts in about 2 s. An organization with more than `GeoAssets__OrganizationCatalogs__FullCacheMaxFeatures`
  (default 50,000) features is served from PostGIS: its first read answers in a few seconds, later reads by id in 1–3 ms,
  and the silo stays around 200 MiB. Before XD01-449 the same read loaded the whole catalog and failed with
  `OutOfMemoryException`.
- A read that would return more than `GeoAssets__OrganizationCatalogs__EnumerationLimit` (default 10,000) features of
  such an organization (`GET /api/geoassets/features`, a topology walk, a selection strategy) answers
  `422 catalog.enumeration-limit`: narrow it with a bounding box, a page (`POST /api/geoassets/features/query`) or a closer
  seed. WFS GetFeature without BBOX returns at most 10,000 features per page (`COUNT`, `STARTINDEX`).
- The silo is capped at `SILO_MEMORY_LIMIT` (default `6g`) so the runtime sizes its GC to it: running out of memory fails
  a request instead of letting the Docker VM kill a process, which can be the shared Postgres.

# Local OpenTelemetry Collector (vendor-agnostic fan-out)

Local/dev Collector for XD01-33 (child of epic XD01-28). Instrument
application code strictly with the OpenTelemetry SDK, send OTLP to this
Collector, and let the Collector fan out to New Relic, Datadog, and Azure
Monitor via config only — swapping/adding a backend never touches app code.

This is **local/dev scaffolding only**, separate from `GeoAssets.Server`'s
normal path. `GeoAssets.Server` already emits OTLP directly to New Relic out
of the box (XD01-29/XD01-30 landed 2026-08-14) — see "Configuring
GeoAssets.Server directly against a vendor (no Collector)" below for that
path. This Collector exists for local multi-vendor fan-out and testing
(XD01-33), not because production needs one.

## Running it

```bash
cd deploy/otel
cp .env.example .env   # fill in the vendor credentials you want to test
docker compose up
```

### Pulling credentials from Key Vault instead

New Relic's ingest key is stored in the `geoassets-otel-kv` Key Vault
(GeoAssets subscription, `Develop` resource group). Instead of pasting it
into `.env` by hand, run:

```bash
az login   # if not already
./fetch-secrets.sh
```

This requires the `Key Vault Secrets User` (or `Secrets Officer`) role on
`geoassets-otel-kv`. It fills in `NEW_RELIC_LICENSE_KEY`,
`AZURE_MONITOR_CONNECTION_STRING`, `DD_API_KEY`, and `DD_SITE` — all four
vendor credentials, once their secrets exist in the vault (see below for
the Datadog and Azure Monitor one-time setup steps).

#### Creating the Azure Monitor connection string secret (one-time setup)

Run this once per environment, by whoever has `Contributor` on the target
resource group and `Key Vault Secrets Officer` on `geoassets-otel-kv`. It
only needs to be repeated if the Application Insights resource is
recreated or rotated.

1. **Find or create the Application Insights resource** the Collector
   should forward to. The existing resource for this environment is
   `geoassets-insights` (resource group `Develop`, region `centralus`),
   confirmed present:
   ```bash
   az login   # if not already

   # Check whether it already exists in the target resource group
   az monitor app-insights component show \
     --app geoassets-insights \
     --resource-group Develop \
     --query connectionString -o tsv

   # Only if it doesn't exist yet — create a new one (Log Analytics-based, recommended)
   az monitor app-insights component create \
     --app geoassets-insights \
     --resource-group Develop \
     --location centralus \
     --workspace <log-analytics-workspace-resource-id>
   ```
   The `create` command's output includes `connectionString` directly —
   or re-run the `show` command above afterward.

2. **Store the connection string as a Key Vault secret** (name matches the
   `NEW-RELIC-LICENSE-KEY` convention `fetch-secrets.sh` expects):
   ```bash
   az keyvault secret set \
     --vault-name geoassets-otel-kv \
     --name AZURE-MONITOR-CONNECTION-STRING \
     --value "<connectionString from step 1>"
   ```

3. **Verify** the secret is readable with your own identity:
   ```bash
   az keyvault secret show \
     --vault-name geoassets-otel-kv \
     --name AZURE-MONITOR-CONNECTION-STRING \
     --query value -o tsv
   ```

4. **Pull it into `.env`** — `./fetch-secrets.sh` now does this
   automatically alongside the New Relic key (see below).

Treat the connection string like any other secret: never paste it directly
into `otel-collector-config.yaml`, commit it, or log it — it grants write
access to the Application Insights resource's ingestion endpoint.

#### Adding the Datadog secrets (one-time setup)

Run this once per Datadog organization, by whoever holds an `Admin` (or
`API Keys` capability) role in Datadog and `Key Vault Secrets Officer` on
`geoassets-otel-kv`. It only needs to be repeated if the API key is
rotated or revoked.

1. **Obtain the Datadog API key.** This is a Datadog-side credential, not
   an Azure resource, so there's no `az` equivalent — create or copy it
   from the Datadog UI: **Organization Settings → API Keys**. Note the
   **site** your org is on too (shown in the URL, e.g. `datadoghq.com`,
   `datadoghq.eu`, `us3.datadoghq.com`).

2. **Store the API key as a Key Vault secret**:
   ```bash
   az login   # if not already

   az keyvault secret set \
     --vault-name geoassets-otel-kv \
     --name DD-API-KEY \
     --value "<API key from step 1>"
   ```

3. **Store the site alongside it**, so `fetch-secrets.sh` can pull both
   with the same pattern (not sensitive on its own, but kept in the vault
   for a uniform fetch flow):
   ```bash
   az keyvault secret set \
     --vault-name geoassets-otel-kv \
     --name DD-SITE \
     --value "<your Datadog site, e.g. datadoghq.com>"
   ```

4. **Verify** both are readable with your own identity:
   ```bash
   az keyvault secret show --vault-name geoassets-otel-kv --name DD-API-KEY --query value -o tsv
   az keyvault secret show --vault-name geoassets-otel-kv --name DD-SITE --query value -o tsv
   ```

5. **Pull them into `.env`** — `./fetch-secrets.sh` now does this
   automatically alongside New Relic and Azure Monitor.

Treat the API key like any other secret: never paste it directly into
`otel-collector-config.yaml`, commit it, or log it — it grants write
access to your Datadog org's intake API.

The collector listens on:
- `4317` — OTLP gRPC
- `4318` — OTLP HTTP

## Environment variables the .NET application needs

Inside the Docker Compose network, point the OTel .NET SDK's standard env
vars at the `otel-collector` service (not `localhost`):

```
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4318
OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf
OTEL_SERVICE_NAME=my-dotnet-service
```

`AddOtlpExporter()` reads these automatically — no endpoint needs to be
hardcoded in application code.

## .NET Program.cs — illustrative snippet

This is a generic, standalone example of wiring the official OpenTelemetry
.NET SDK with `AddOtlpExporter()` — useful as a reference if you're adding
OTLP to a *different* service that isn't `GeoAssets.Server`.
`GeoAssets.Server` itself doesn't need this snippet: `AddGeoAssetsObservability`
(from `GeoAssets.Infrastructure.Observability`, called in
`apps/GeoAssets.Server/Program.cs`) already does exactly this, driven by the
`Observability:Otlp:*` config section instead of raw `OTEL_EXPORTER_OTLP_*`
env vars — see "Configuring GeoAssets.Server directly against a vendor (no
Collector)" below for how to point it at a specific backend.

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: builder.Configuration["OTEL_SERVICE_NAME"] ?? "my-dotnet-service"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        // Reads OTEL_EXPORTER_OTLP_ENDPOINT / OTEL_EXPORTER_OTLP_PROTOCOL
        // from the environment — no endpoint hardcoded here.
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());

var app = builder.Build();
app.Run();
```

## Vendor exporters (Collector-side, config only)

| Vendor | Exporter | Credential env var(s) |
|---|---|---|
| New Relic | `otlphttp/newrelic` | `NEW_RELIC_LICENSE_KEY` |
| Datadog | `datadog` (native) | `DD_API_KEY`, `DD_SITE` |
| Azure Monitor | `azuremonitor` | `AZURE_MONITOR_CONNECTION_STRING` |

To stop sending to a vendor, remove it from `service.pipelines.*.exporters`
in `otel-collector-config.yaml` — no application redeploy required.

## Configuring GeoAssets.Server directly against a vendor (no Collector)

Everything above is about the local/dev Collector. In production,
`GeoAssets.Server` doesn't go through a Collector at all —
`AddGeoAssetsObservability`
(`core/GeoAssets.Infrastructure.Observability/ObservabilityServiceExtensions.cs`)
wires a plain `AddOtlpExporter()` for traces, metrics, and logs, configured
entirely through the `Observability:Otlp:*` section of `appsettings.json` (or
the `Observability__Otlp__Endpoint` / `OTEL_EXPORTER_OTLP_HEADERS`
environment-variable overrides, same `__` convention as
`ConnectionStrings__GeoAssets`). Swapping vendors is a config change, not a
code change — instrumentation call sites (`GeoAssetsActivitySource`,
`GeoAssetsMeter`) never know which backend is on the other end.

### New Relic (direct — no Collector or Agent needed)

This is the shipped default in `appsettings.json`:

| Setting | Value |
|---|---|
| `Observability:Otlp:Endpoint` | `https://otlp.nr-data.net:4317` (US, gRPC) or `https://otlp.nr-data.net:4318` (US, HTTP — also set `Protocol: HttpProtobuf`) |
| EU accounts | `https://otlp.eu01.nr-data.net:4317` / `:4318` |
| Header | `api-key=<your New Relic ingest license key>` |

Easiest setup: just set the `NEW_RELIC_LICENSE_KEY` environment variable.
`ObservabilityServiceExtensions.cs` auto-formats it into the `api-key` header
for you, so you don't need to hand-build `Observability:Otlp:Headers`.

New Relic accepts OTLP directly on that endpoint — no Agent or Collector
required.

### Datadog

Two supported paths — pick one:

1. **Via the Datadog Agent's OTLP receiver** — the proven path, and the same
   one the local Collector's `datadog` exporter in this directory stands in
   for. Run the Datadog Agent (v7.32+) with OTLP ingestion enabled
   (`otlp_config.receiver` in `datadog.yaml`, listening on `4317`/`4318`),
   then point `Observability:Otlp:Endpoint` at the Agent's host, e.g.
   `http://<agent-host>:4317`. The app itself sends no Datadog credential —
   the Agent authenticates to Datadog with its own `DD_API_KEY`.
2. **Direct/agentless OTLP intake.** Datadog has been rolling out OTLP
   ingestion that skips the Agent entirely, but the intake hostname and
   required headers vary by account site and are still evolving — check
   Datadog's current OTLP ingestion documentation for your site before
   wiring this up rather than assuming the New Relic-shaped URL applies here
   too.

Either path: still no code change in `GeoAssets.Server`, only
`Observability:Otlp:Endpoint`/`Headers`.

### Azure Application Insights / Azure Monitor

This one is different from the other two: **Azure Monitor doesn't expose a
plain OTLP ingest endpoint**, so you can't just repoint
`Observability:Otlp:Endpoint` at an Azure URL the way you can for New Relic.
Two real options:

1. **Through a Collector running the `azuremonitor` exporter** — exactly
   what `otel-collector-config.yaml` in this directory already does. Point
   `Observability:Otlp:Endpoint` at the Collector (e.g.
   `http://otel-collector:4317` inside the Compose network) and let the
   Collector translate OTLP into Application Insights' ingestion protocol
   using `AZURE_MONITOR_CONNECTION_STRING`. This is the only path proven in
   this repo today, and it requires a Collector running somewhere between
   the app and Azure — see the scope note below, production Collector
   deployment isn't solved yet.
2. **Re-adding the `Azure.Monitor.OpenTelemetry.AspNetCore` distro package**
   directly to `GeoAssets.Server` — this *is* a code change, and it's the
   exact vendor-SDK coupling XD01-30 removed to make the app
   vendor-agnostic. Only do this if you're deliberately opting back into an
   Azure-only setup and accepting that trade-off.

Unlike New Relic (and mostly Datadog), there's no agentless, Collector-free,
code-free path to Azure Monitor today.

## Scope note

Production Collector deployment (HA, scaling, secrets management) is out
of scope here — see the "Non-goals" section on XD01-28. This is local/dev
only.

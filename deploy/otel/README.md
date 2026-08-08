# Local OpenTelemetry Collector (vendor-agnostic fan-out)

Local/dev Collector for XD01-33 (child of epic XD01-28). Instrument
application code strictly with the OpenTelemetry SDK, send OTLP to this
Collector, and let the Collector fan out to New Relic, Datadog, and Azure
Monitor via config only — swapping/adding a backend never touches app code.

This is **local/dev scaffolding only**. Nothing in GeoAssets currently
emits OTLP (see XD01-29/XD01-30, both still "To Do") — until that lands,
this Collector has no real traffic to receive.

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
   should forward to:
   ```bash
   az login   # if not already

   # Check whether one already exists in the target resource group
   az monitor app-insights component show \
     --app geoassets-otel \
     --resource-group Develop \
     --query connectionString -o tsv

   # If it doesn't exist yet, create it (Log Analytics-based, recommended)
   az monitor app-insights component create \
     --app geoassets-otel \
     --resource-group Develop \
     --location eastus \
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
.NET SDK with `AddOtlpExporter()`. It is **not** applied to
`GeoAssets.Server` in this change — the real wiring of
`GeoAssets.Infrastructure.Observability` into `GeoAssets.Server` is tracked
separately in XD01-29 (project reference + `AddGeoAssetsObservability`
call site) and XD01-30 (replacing the `Azure.Monitor.OpenTelemetry.AspNetCore`
distro in `ObservabilityServiceExtensions.cs` with this same
`AddOtlpExporter()` approach).

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

## Scope note

Production Collector deployment (HA, scaling, secrets management) is out
of scope here — see the "Non-goals" section on XD01-28. This is local/dev
only.

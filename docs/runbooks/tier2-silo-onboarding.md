# Runbook: onboarding a new Tier-2 (dedicated-silo) client

Tier-2 clients get their own dedicated database and their own
`GeoAssets.FieldService.Server` deployment, instead of sharing the
multi-tenant pool. Per XD01-215's "Zero Code Branching Invariant", this is
the *exact same container image* as the shared tier — onboarding is purely
an infrastructure operation (Terraform + Helm), with zero source changes.

## Prerequisites

- A Kubernetes cluster already exists and your `kubeconfig` can reach it.
- A Postgres server already exists and you have administrator credentials
  on it (to create the tenant's dedicated role/database). It does not need
  to be a separate physical server per tenant — one server hosting one
  database per Tier-2 tenant is a valid topology.
- The `GeoAssets.FieldService.Server` image has been built
  (`apps/GeoAssets.FieldService.Server/Dockerfile`) and pushed somewhere
  your cluster can pull it from. **This repo has no CI/CD job or registry
  push wired up yet** (same gap as `deploy/server/README.md` already
  documents for the shared tier) — build and push it yourself for now.
- An Entra CIAM app registration exists for this tenant (`Instance`,
  `TenantId`, `ClientId`).
- `terraform` and `helm` CLIs installed locally.

## Steps

1. **Provision the silo** — write a `<tenant>.tfvars` for
   `deploy/terraform/tier2-silo` (see that module's README for the full
   variable list) and apply it:

   ```bash
   cd deploy/terraform/tier2-silo
   terraform init
   terraform plan  -var-file=<tenant>.tfvars
   terraform apply -var-file=<tenant>.tfvars
   ```

   This creates the tenant's Postgres role+database, a Kubernetes
   namespace, a connection-string Secret, and installs the
   `geoassets-fieldservice` Helm chart pointed at them.

2. **Apply database migrations.** The dedicated database starts empty.
   `GeoAssetsDbContext` does not auto-migrate on this host (see
   `apps/GeoAssets.FieldService.Server/Program.cs`'s own comments) — apply
   migrations the same way `deploy/server/README.md` documents for the
   shared tier's `ServiceOrderDbContext`/`ProjectDbContext`, against the
   new dedicated connection string:

   ```bash
   dotnet ef database update \
     --project apps/GeoAssets.Server --startup-project apps/GeoAssets.Server \
     --context GeoAssetsDbContext \
     --connection "<tenant's connection string>"

   dotnet ef database update \
     --project apps/GeoAssets.Server --startup-project apps/GeoAssets.Server \
     --context ServiceOrderDbContext \
     --connection "<tenant's connection string>"
   ```

3. **Verify the rollout:**

   ```bash
   kubectl rollout status deployment/<tenant>-fieldservice -n <tenant>-silo
   helm test <tenant>-fieldservice -n <tenant>-silo
   ```

   `helm test` runs the chart's own `/healthz` check
   (`templates/tests/test-connection.yaml`) from inside the cluster.

4. **Point the Gateway at the new silo.** `GatewayRouteCatalog`
   (`apps/GeoAssets.Gateway/GatewayRouteCatalog.cs`, XD01-213) routes a
   request to a tenant's silo cluster when it carries the
   `X-Tenant-Domain` header; wire this tenant's domain to
   `terraform output service_dns_name` from step 1 in the Gateway's own
   destination configuration (`Destinations:FieldService:Silo` —
   see `apps/GeoAssets.Gateway/appsettings.json`).

5. **Smoke-test end-to-end** with a real request through the Gateway,
   carrying this tenant's `X-Tenant-Domain` header and a bearer token from
   their Entra CIAM tenant, against one of the mapped module endpoints
   (e.g. `/api/fieldservice/contractor-us-ledger`).

## What was verified, and how

This runbook's steps 1–3 mirror exactly what was live-tested while building
this chart: a local `kind` cluster, a real Postgres+PostGIS pod standing in
for step 1's dedicated database, the real
`apps/GeoAssets.FieldService.Server/Dockerfile` image, a real
`helm install` with `existingSecretName` pointed at that database, a
rollout reaching `1/1 Ready`, a live `curl /healthz` returning
`{"status":"healthy"}`, and `helm test` succeeding. That live test is what
surfaced and proved the fix for a real bug: `/healthz` was rejected with
`401` by the `FallbackPolicy` every `AddGeoAssetsAuthentication`-wired host
sets (`RequireAuthenticatedUser()` on every endpoint without
`AllowAnonymous()`) — Kubernetes probes carry no bearer token, so the pod
could never become `Ready`. Fixed in
`core/GeoAssets.Infrastructure.Observability/ObservabilityApplicationExtensions.cs`
(now maps `/healthz` as a real anonymous-allowed endpoint when the host
supports endpoint routing, which every real host does); regression-tested
in `ObservabilityApplicationExtensionsTests.Healthz_RemainsReachable_EvenWhenFallbackPolicyRequiresAuthentication`.

Steps 1's database/role/Secret provisioning (the `postgresql`/`kubernetes`
Terraform resources themselves, as opposed to the chart they feed into)
and step 4's Gateway wiring were not live-exercised — this sandboxed
environment had no real multi-tenant Postgres admin endpoint or a second
tenant's CIAM registration to test against. They follow the same,
already-proven Terraform provider patterns (`terraform validate` passes)
but should be dry-run against a real staging cluster/Postgres server before
the first real client onboarding.

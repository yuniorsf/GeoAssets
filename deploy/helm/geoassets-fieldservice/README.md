# geoassets-fieldservice Helm chart

Deploys `GeoAssets.FieldService.Server`. The **same chart and the same
container image** produce either:

- the shared multi-tenant SaaS tier (default `values.yaml`), or
- a Tier-2 client's dedicated silo (`values.yaml` + `values-dedicated-silo.yaml`)

— only the values differ (`connectionString`/`existingSecretName`,
`multiTenancy.mode`, `multiTenancy.tenantName`). No image variant, build
flag, or source change distinguishes them (XD01-215's "Zero Code Branching
Invariant"). See `docs/runbooks/tier2-silo-onboarding.md` at the repo root
for the full onboarding procedure driven by
`deploy/terraform/tier2-silo`.

## Secrets

Never put a real connection string in a values file that gets committed.
Two ways to supply it:

1. **`existingSecretName`** (preferred for any real cluster) — point at a
   Secret with a `connectionString` key, provisioned by
   `deploy/terraform/tier2-silo` or your own secrets tooling (External
   Secrets Operator, the Key Vault CSI driver, etc.).
2. **`connectionString`** directly — only for local/dev smoke tests. The
   chart renders it into a plain `Secret` (`templates/secret.yaml`) itself.

## Local smoke test

```bash
helm lint .

helm template my-release . \
  --set image.repository=<repo> --set image.tag=<tag> \
  --set connectionString="Host=...;Port=5432;Database=geoassets_fieldservice;Username=postgres;Password=..."
```

Against a real cluster (this repo has no CI/CD job or container registry
push wired up yet — see "What this doesn't solve" below — so build and load
the image yourself first, e.g. with `kind load docker-image` for a local
`kind` cluster):

```bash
helm install my-release . -n <namespace> --create-namespace \
  --set image.repository=geoassets-fieldservice-server --set image.tag=test --set image.pullPolicy=Never \
  --set connectionString="..." \
  --set azureAdCiam.instance="https://<tenant>.ciamlogin.com/" \
  --set azureAdCiam.tenantId="<tenant-id>" \
  --set azureAdCiam.clientId="<client-id>"

kubectl rollout status deployment/my-release -n <namespace>
helm test my-release -n <namespace>
```

This exact sequence (real `kind` cluster, real Postgres+PostGIS pod, real
built image) was used to verify this chart live while building it — see
XD01-215's implementation notes for what that uncovered (a pre-existing bug
in `/healthz` being rejected by the FallbackPolicy any
`AddGeoAssetsAuthentication`-wired host sets, fixed in
`GeoAssets.Infrastructure.Observability.ObservabilityApplicationExtensions`,
not something specific to this chart).

## Pod security

The pod meets the Kubernetes Pod Security Standards `restricted` profile, so it can run in a namespace labelled
`pod-security.kubernetes.io/enforce: restricted`:

- **User:** it runs as the image's unprivileged `app` user (uid/gid 1654, `podSecurityContext`). The image itself sets
  `USER $APP_UID` as well.
- **Container:** privilege escalation is off, every capability is dropped and the root filesystem is read-only
  (`securityContext`). The only writable path is an `emptyDir` at `/tmp` (`tmpSizeLimit`, 64Mi by default).
- **Seccomp:** the `RuntimeDefault` profile.
- **Service account token:** not mounted, since the host never calls the Kubernetes API.

With the read-only filesystem, ASP.NET Core Data Protection keeps its keys in memory and logs a warning at start. That
is harmless for this bearer-token API: the keys were already lost on every restart, since they lived inside the
container. The `helm test` pod (`curlimages/curl`, uid 100) runs under the same restrictions.

## What this doesn't solve

Same caveat as `deploy/server/README.md`: there's still no CI/CD job,
container registry push, or cloud-hosted Kubernetes cluster wired up
anywhere in this repo. `image.repository`/`image.tag` are required values
with no default — you must point them at wherever you actually publish the
image. `deploy/terraform/tier2-silo` provisions the tenant's database and
installs this chart, but assumes a cluster and a Postgres server already
exist; it doesn't provision either of those itself (see that module's
README for why).

# Docker, OCI & Kubernetes / Helm Standards

## 1. Docker & OCI Containers

- **Multi-stage & BuildKit:**
  - Always use multi-stage builds (`AS build` -> `AS runtime`) so compilers, SDKs, and package managers never ship in the runtime image.
  - Enable BuildKit cache mounts (`RUN --mount=type=cache,...`) for package restoration and `--mount=type=secret` for private registries/NuGet/npm tokens (never `ARG`/`ENV` for secrets).
  - Include a strict `.dockerignore` excluding `.git`, `bin/`, `obj/`, `node_modules/`, `.env*`, and `*.tfstate*`.
- **Hardening & Runtime:**
  - Use minimal runtime bases (`distroless`, `chiseled` Ubuntu for .NET, `chainguard`, or slim/alpine) pinned by version tag and `@sha256` digest in production.
  - Run as an explicit non-root `USER` (e.g., `USER $APP_UID` on .NET 8+/10+ images or dedicated UID/GID `10001`).
  - Set `STOPSIGNAL SIGTERM`, expose unprivileged ports (`>= 1024`, e.g. `8080`), and bind gracefully to `0.0.0.0`.
  - Support multi-architecture (`linux/amd64`, `linux/arm64`) via `ARG TARGETARCH` when developers use Apple Silicon and clusters run `amd64` or `arm64`.
- **Compose (Local & Dev Stacks):**
  - Use Compose v2 syntax (no deprecated top-level `version:` key), explicit `healthcheck`s, `depends_on: { condition: service_healthy }`, resource limits where relevant, and `profiles` for optional dependencies.
- **Validation Gate:**
  - `hadolint <Dockerfile>` (if installed)
  - `docker buildx build --check .` (or dry syntax/build validation)
  - `docker compose -f <file> config -q`
  - `trivy image` / `docker scout cves` (when image is built)

---

## 2. Kubernetes & Helm Standards

- **Pod Security Standards (`restricted` baseline):**
  - Pod-level `securityContext`: `runAsNonRoot: true`, `seccompProfile: { type: RuntimeDefault }`.
  - Container-level `securityContext`: `allowPrivilegeEscalation: false`, `readOnlyRootFilesystem: true` (mount `emptyDir` for `/tmp` if needed), `capabilities: { drop: ["ALL"] }`.
- **Reliability & Scheduling:**
  - Always configure `resources.requests` and `resources.limits` (memory `requests == limits` or tight ratio to prevent OOM surprises; avoid overly restrictive CPU limits if latency-sensitive, or size carefully).
  - Configure all three probes appropriately: `startupProbe` (for slow cold starts), `readinessProbe` (traffic gating), `livenessProbe` (deadlock recovery only).
  - Production workloads (`replicaCount >= 2`): add `PodDisruptionBudget` (`minAvailable` or `maxUnavailable: 1`), `topologySpreadConstraints` across zones/nodes, and `HorizontalPodAutoscaler` (or KEDA for queue/event scaling).
  - Graceful shutdown: `terminationGracePeriodSeconds` + `preStop` sleep if needed so endpoints drain before SIGTERM closes sockets.
- **Networking & Secrets:**
  - Prefer **Kubernetes Gateway API** (`Gateway`, `HTTPRoute`) for new ingress architectures; maintain Ingress compatibility only where existing controllers require it.
  - Enforce `NetworkPolicy` (default deny ingress/egress per namespace, explicit allow rules for DNS, ingress/gateway, DB, and OTLP collector).
  - Never store raw secrets in Helm `values.yaml` committed to Git; reference `existingSecretName` synced via **External Secrets Operator** (Azure Key Vault / DO) or **Secrets Store CSI Driver**.
- **Helm & GitOps:**
  - Keep charts environment-agnostic (`values.yaml` + `values-<env>.yaml` overrides).
  - Use `helm lint`, `helm template ... | kubeconform -strict` (against target K8s version), and `trivy config` before any release.
  - Support Argo CD / Flux compatibility (avoid mutable hooks where pure declarative manifests work; pin chart/image versions).

---
name: iac-conventions
description: >-
  Modern Infrastructure as Code (IaC) conventions for Terraform (>= 1.6+),
  Docker/OCI containers, Kubernetes/Helm, Microsoft Azure, and DigitalOcean.
  Use when designing, writing, refactoring, or auditing Terraform modules,
  Dockerfiles, docker-compose files, Helm charts, or cloud/cluster manifests.
---

# IaC Conventions (Terraform, Docker, Kubernetes — Azure & DigitalOcean)

Authoritative engineering standards for `iac-specialist` and `iac-auditor`. Load only the reference file relevant to the task.

## References

| File | Scope |
| :--- | :--- |
| [terraform-azure-do.md](references/terraform-azure-do.md) | Terraform >= 1.6+ patterns, state/refactoring, testing, Azure (`azurerm`/`azapi`) & DigitalOcean (`digitalocean`) specifics |
| [docker-kubernetes.md](references/docker-kubernetes.md) | Docker BuildKit, OCI hardening, Kubernetes Pod Security (`restricted`), Gateway API, Helm v3+/v4 & GitOps |

## GeoAssets Repository Context

When working inside `GeoAssets`, ground every change in the existing deployment topology:

- **Terraform (`deploy/terraform/tier2-silo/`):** Provisions per-tenant PostgreSQL role/database, Kubernetes namespace, secret, and Helm release (`hashicorp/kubernetes`, `hashicorp/helm`, `hashicorp/random`, `cyrilgdn/postgresql`, `terraform >= 1.6`).
- **Helm (`deploy/helm/geoassets-fieldservice/`):** Enforces the **Zero Code Branching Invariant** — shared multi-tenant SaaS and Tier-2 dedicated silos deploy the exact same container image; environment differences live strictly in `values*.yaml` and `existingSecretName` (External Secrets Operator / Azure Key Vault CSI).
- **Dockerfiles (`apps/*/Dockerfile`):** 5 .NET 10 services (`FieldService.Server`, `Web`, `Server`, `NeutralOperator.Server`, `Gateway`) using multi-arch (`TARGETARCH`) builds from repo root context.
- **Local Compose (`deploy/{web,server,otel,neutral-operator}/docker-compose.yaml`):** Local/observability stacks including OpenTelemetry collector (`deploy/otel/`).

## Mandatory Safety Rules (Non-Negotiable)

1. **No unapproved mutations:** Never run `terraform apply`, `terraform destroy`, `kubectl apply`, `kubectl delete`, `helm install`, `helm upgrade`, `az ... create/delete`, or `doctl ... create/delete` without explicit user confirmation in the current turn.
2. **No plaintext secrets:** Never commit credentials, tokens, `.tfvars` with secrets, or inline K8s `Secret` values in Git. Use `ephemeral` resources / write-only args (where supported), External Secrets Operator, or cloud-native secret stores (Azure Key Vault / DO Spaces+Secrets).
3. **Immutable pinning:** Pin Terraform `required_version` and provider constraints (`~>`), pin base container images by tag + `@sha256` digest for production, and never use `:latest`.
4. **Adapt to available local tooling:** Run all installed validators (`terraform`, `helm`, `docker`, `kubectl`, `az`). If optional linters (`tflint`, `trivy`, `kubeconform`, `hadolint`, `doctl`) are not installed locally, state explicitly which checks were skipped rather than claiming full pass.

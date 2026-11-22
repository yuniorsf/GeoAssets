# Terraform, Azure & DigitalOcean Standards

## 1. Core Terraform (>= 1.6 / 1.9+) Standards

- **File & Module Layout:**
  - Every root stack and reusable module separates `versions.tf`, `providers.tf` (root only — modules never declare `provider {}` blocks), `variables.tf`, `main.tf`, `outputs.tf`, and `README.md`.
  - Every `variable` must declare `type`, `description`, and `validation {}` blocks for enums, CIDRs, naming conventions, and ranges. Mark secrets with `sensitive = true`.
  - Mark secret outputs with `sensitive = true` or avoid outputting secrets altogether.
- **Safe State & Refactoring:**
  - Always use remote state with locking enabled.
  - Never rename or move resources by deleting and recreating; use declarative `moved {}`, `removed {}`, and `import {}` blocks.
  - Use `lifecycle { prevent_destroy = true }` on stateful resources (databases, storage accounts, KMS/Key Vault keys, production clusters) and `create_before_destroy = true` for zero-downtime replacements.
- **Secrets & Modern Language Features:**
  - Prefer `ephemeral` resources and write-only attributes (Terraform >= 1.10/1.11+) when supported by the provider so secrets never persist in `terraform.tfstate`.
  - Use `check {}` blocks for continuous infrastructure assertions and `terraform test` (`tests/*.tftest.hcl`) with `mock_provider` for unit-testing module logic without cloud credentials.
- **Validation Gate:**
  1. `terraform fmt -check -recursive`
  2. `terraform init -backend=false` + `terraform validate`
  3. `tflint --recursive` (if installed)
  4. `trivy config .` (if installed)
  5. `terraform plan` (when backend/credentials are configured; never guess plan output)

---

## 2. Microsoft Azure (`azurerm` v4.x / `azapi` v2.x)

- **Authentication & State:**
  - Authenticate CI/CD via **OIDC Federated Credentials** (`use_oidc = true`) with User-Assigned Managed Identity or Service Principal — never long-lived client secrets.
  - Remote state in **Azure Storage (`azurerm` backend)** with `use_azuread_auth = true`, Blob versioning, soft-delete, TLS 1.2+, and private endpoint or restricted network access.
- **Identity & Access:**
  - Use **Managed Identities** (prefer User-Assigned for shared lifecycle across Terraform runs) and **Azure RBAC** everywhere; disable local admin accounts (`local_account_disabled = true` on AKS, ACR admin disabled, Azure Key Vault RBAC authorization `enable_rbac_authorization = true` over legacy access policies).
- **AKS (Azure Kubernetes Service) Best Practices:**
  - Use **Azure CNI Overlay** (with Cilium dataplane where supported) to avoid VNet IP exhaustion.
  - Enable **OIDC Issuer + Workload Identity** (`oidc_issuer_enabled = true`, `workload_identity_enabled = true`) so pods exchange K8s ServiceAccounts for Entra ID tokens without secrets.
  - Separate `system` node pool (tainted `CriticalAddonsOnly=true:NoSchedule`) from `user` node pools; enable Cluster Autoscaler or Karpenter/NAP, Availability Zones (`zones = ["1", "2", "3"]`), and **Azure Key Vault Provider for Secrets Store CSI Driver** or External Secrets Operator.
- **Networking & Data:**
  - Private Endpoints + Private DNS Zones for Azure Database for PostgreSQL Flexible Server, Key Vault, and ACR (Premium).
  - Enforce `public_network_access_enabled = false` in production; tag all resources consistently (`environment`, `owner`, `workload`, `managed_by = "terraform"`).
  - Use `azapi` alongside `azurerm` when a newly released Azure API feature is not yet exposed in `azurerm`.

---

## 3. DigitalOcean (`digitalocean/digitalocean` ~> 2.x)

- **Authentication & State:**
  - Authenticate via short-lived scoped API tokens or OIDC-brokered credentials in CI; never commit `DIGITALOCEAN_TOKEN` or `SPACES_ACCESS_KEY_ID`.
  - Remote state in **DigitalOcean Spaces** using Terraform's `s3` backend with:
    - `endpoints = { s3 = "https://<region>.digitaloceanspaces.com" }`
    - `skip_credentials_validation = true`, `skip_metadata_api_check = true`, `skip_region_validation = true`, `skip_requesting_account_id = true`
    - `use_lockfile = true` (Terraform >= 1.10 native S3 lockfile) so no DynamoDB table is needed.
- **DOKS (DigitalOcean Kubernetes) Best Practices:**
  - Enable `auto_upgrade = true` with a defined `maintenance_policy` window and `surge_upgrade = true` for zero-downtime node upgrades.
  - Separate node pools by workload profile (`system` vs `app` with taints/labels) and enable `auto_scale = true` with explicit `min_nodes` / `max_nodes`.
  - Use **VPC-native networking** (`vpc_uuid` explicitly attached to DOKS, Managed Databases, Droplets, and Load Balancers) and **Cloud Firewalls** restricting ingress strictly to LB/VPC CIDRs.
  - Disable `registry_integration` secret sprawl by scoping DOCR pull credentials cleanly; prefer **DO Load Balancer** annotations with TLS termination via Cert-Manager + Let's Encrypt or DO Certificates.
- **Managed Databases & Spaces:**
  - Always place Managed PostgreSQL inside the same `private_network_uuid` (VPC) and configure `digitalocean_database_firewall` allowing only the DOKS cluster (`type = "k8s"`, `value = digitalocean_kubernetes_cluster.this.id`) — never `0.0.0.0/0`.
  - Use `digitalocean_project` + `digitalocean_project_resources` and `tags` (`env:<env>`, `team:<team>`, `iac:terraform`) for cost attribution and blast-radius isolation.

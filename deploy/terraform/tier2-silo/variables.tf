variable "tenant_name" {
  description = "Short, DNS-safe identifier for the Tier-2 client (e.g. \"acme-utility\"). Used as the namespace name, the database/role name, and the Helm release name."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$", var.tenant_name))
    error_message = "tenant_name must be a valid DNS label (lowercase alphanumeric and hyphens, max 63 chars)."
  }
}

variable "namespace" {
  description = "Kubernetes namespace for this tenant's dedicated silo. Defaults to \"<tenant_name>-silo\"."
  type        = string
  default     = null
}

variable "chart_path" {
  description = "Path to the geoassets-fieldservice Helm chart (relative to the Terraform root module or absolute)."
  type        = string
  default     = "../../helm/geoassets-fieldservice"
}

variable "image_repository" {
  description = "Container image repository for GeoAssets.FieldService.Server — the exact same image used by the shared tier (\"Zero Code Branching Invariant\"). No registry/CI push pipeline exists yet in this repo; set this to wherever the image is actually published."
  type        = string
}

variable "image_tag" {
  description = "Container image tag/digest to deploy. Never \"latest\" — pin an explicit, immutable reference."
  type        = string
}

variable "replica_count" {
  description = "Number of GeoAssets.FieldService.Server replicas for this tenant."
  type        = number
  default     = 1
}

variable "azure_ad_ciam_instance" {
  description = "Entra CIAM instance URL for this tenant, e.g. https://<tenant>.ciamlogin.com/."
  type        = string
}

variable "azure_ad_ciam_tenant_id" {
  description = "Entra CIAM tenant ID for this tenant."
  type        = string
}

variable "azure_ad_ciam_client_id" {
  description = "Entra CIAM API client ID (audience) for this tenant's FieldService.Server registration."
  type        = string
}

variable "postgres_statement_timeout_ms" {
  description = "Optional XD01-214 statement_timeout backstop in milliseconds. Less critical on a dedicated silo (no other tenant shares the database), but still available."
  type        = number
  default     = null
}

# ── Dedicated database provisioning ──────────────────────────────────────────────────────────
# This module provisions the tenant's own database/role on an existing Postgres *server*
# (the "postgres_admin_*" variables below authenticate as that server's administrator) — it does
# not stand up the Postgres server itself. Pointing this at a shared Postgres *server* that hosts
# one database per Tier-2 tenant, vs. a genuinely separate server/instance per tenant, is an
# operational choice outside this module's scope.

variable "postgres_admin_host" {
  description = "Hostname of the Postgres server to provision the tenant's dedicated database on."
  type        = string
}

variable "postgres_admin_port" {
  description = "Port of the Postgres server above."
  type        = number
  default     = 5432
}

variable "postgres_admin_username" {
  description = "Administrator username on the Postgres server above, used only by Terraform to create the tenant's role/database."
  type        = string
}

variable "postgres_admin_password" {
  description = "Administrator password on the Postgres server above. Pass via TF_VAR_postgres_admin_password or a secrets-backed tfvars file — never commit it."
  type        = string
  sensitive   = true
}

variable "postgres_host_from_cluster" {
  description = "Hostname the FieldService.Server pod should use to reach the Postgres server above (may differ from postgres_admin_host — e.g. a cluster-internal DNS name or private endpoint)."
  type        = string
}

variable "postgres_port_from_cluster" {
  description = "Port the FieldService.Server pod should use to reach the Postgres server above."
  type        = number
  default     = 5432
}

# ── Kubernetes cluster targeting ─────────────────────────────────────────────────────────────

variable "kubeconfig_path" {
  description = "Path to the kubeconfig file for the cluster to provision this tenant's silo into."
  type        = string
  default     = "~/.kube/config"
}

variable "kube_context" {
  description = "kubeconfig context to use. Null uses the kubeconfig's current-context."
  type        = string
  default     = null
}

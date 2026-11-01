# deploy/terraform/tier2-silo

Provisions one Tier-2 client's dedicated silo: a dedicated Postgres
database+role on an existing Postgres server, a Kubernetes namespace and
connection-string Secret, and the `geoassets-fieldservice` Helm chart
pointed at them.

This module assumes a Kubernetes cluster and a Postgres server **already
exist** — it provisions a tenant's slice of them, not the cluster/server
themselves. There is no cloud provider block here on purpose: which cloud
(if any) hosts that cluster/server is an infrastructure decision this repo
hasn't made yet (no CI/CD job or cloud host is wired up anywhere — see
`deploy/server/README.md` and the Helm chart's own README).

## Usage

```hcl
module "acme_utility_silo" {
  source = "./deploy/terraform/tier2-silo"

  tenant_name      = "acme-utility"
  image_repository = "ghcr.io/yourorg/geoassets-fieldservice-server"
  image_tag        = "1.0.0"

  azure_ad_ciam_instance  = "https://acme-utility.ciamlogin.com/"
  azure_ad_ciam_tenant_id = "<entra-ciam-tenant-id>"
  azure_ad_ciam_client_id = "<entra-ciam-client-id>"

  postgres_admin_host        = "silo-postgres.internal"
  postgres_admin_username    = "postgres_admin"
  postgres_admin_password    = var.postgres_admin_password # TF_VAR_postgres_admin_password
  postgres_host_from_cluster = "silo-postgres.internal"     # or a cluster-internal/private-endpoint name

  kubeconfig_path = "~/.kube/config"
}

output "service_dns_name" {
  value = module.acme_utility_silo.service_dns_name
}
```

```bash
terraform init
terraform plan   -var-file=acme-utility.tfvars
terraform apply  -var-file=acme-utility.tfvars
```

Validated in this environment with `terraform fmt -check` and
`terraform validate` (both pass — see XD01-215). `terraform plan`/`apply`
were not run against a live Postgres server or Kubernetes cluster here —
the `postgresql` provider requires a live, reachable server even to plan
(it has to introspect existing roles/databases), which this sandboxed
environment doesn't have. What *was* verified live, end-to-end, against a
real local `kind` cluster and a real Postgres+PostGIS pod: the
`geoassets-fieldservice` Helm chart this module installs (see that chart's
own README) — i.e. everything downstream of "the database and Secret
already exist" was proven to work; provisioning the database/Secret
themselves via the `postgresql`/`kubernetes` Terraform resources above
follows standard, well-documented provider usage but could not be
live-exercised here.

## Teardown

```bash
terraform destroy -var-file=acme-utility.tfvars
```

Destroys the Helm release, namespace, Secret, database, and role. This
drops the tenant's data — confirm a backup/export has been taken first if
this is a real offboarding, not a test provision.

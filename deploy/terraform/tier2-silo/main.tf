locals {
  namespace = coalesce(var.namespace, "${var.tenant_name}-silo")
}

provider "postgresql" {
  host     = var.postgres_admin_host
  port     = var.postgres_admin_port
  username = var.postgres_admin_username
  password = var.postgres_admin_password
  sslmode  = "prefer"
}

provider "kubernetes" {
  config_path    = var.kubeconfig_path
  config_context = var.kube_context
}

provider "helm" {
  kubernetes {
    config_path    = var.kubeconfig_path
    config_context = var.kube_context
  }
}

resource "random_password" "tenant_db" {
  length  = 32
  special = false # Npgsql connection-string values don't need to tolerate ';'/'=' etc.
}

resource "postgresql_role" "tenant" {
  name     = "${var.tenant_name}_fieldservice"
  login    = true
  password = random_password.tenant_db.result
}

resource "postgresql_database" "tenant" {
  name              = "${var.tenant_name}_geoassets"
  owner             = postgresql_role.tenant.name
  template          = "template0"
  lc_collate        = "C"
  connection_limit  = -1
  allow_connections = true
}

resource "kubernetes_namespace" "silo" {
  metadata {
    name = local.namespace
    labels = {
      "multitenancy.geoassets.io/mode"   = "DedicatedSingleTenant"
      "multitenancy.geoassets.io/tenant" = var.tenant_name
    }
  }
}

resource "kubernetes_secret" "fieldservice_db" {
  metadata {
    name      = "${var.tenant_name}-fieldservice-db"
    namespace = kubernetes_namespace.silo.metadata[0].name
  }
  type = "Opaque"
  data = {
    connectionString = "Host=${var.postgres_host_from_cluster};Port=${var.postgres_port_from_cluster};Database=${postgresql_database.tenant.name};Username=${postgresql_role.tenant.name};Password=${random_password.tenant_db.result}"
  }
}

# Same chart, same image as the shared multi-tenant tier — only these values differ
# ("Zero Code Branching Invariant", XD01-215).
resource "helm_release" "fieldservice" {
  name      = "${var.tenant_name}-fieldservice"
  namespace = kubernetes_namespace.silo.metadata[0].name
  chart     = var.chart_path

  set {
    name  = "image.repository"
    value = var.image_repository
  }
  set {
    name  = "image.tag"
    value = var.image_tag
  }
  set {
    name  = "replicaCount"
    value = var.replica_count
  }
  set {
    name  = "multiTenancy.mode"
    value = "DedicatedSingleTenant"
  }
  set {
    name  = "multiTenancy.tenantName"
    value = var.tenant_name
  }
  set {
    name  = "existingSecretName"
    value = kubernetes_secret.fieldservice_db.metadata[0].name
  }
  set {
    name  = "azureAdCiam.instance"
    value = var.azure_ad_ciam_instance
  }
  set {
    name  = "azureAdCiam.tenantId"
    value = var.azure_ad_ciam_tenant_id
  }
  set {
    name  = "azureAdCiam.clientId"
    value = var.azure_ad_ciam_client_id
  }
  dynamic "set" {
    for_each = var.postgres_statement_timeout_ms != null ? [var.postgres_statement_timeout_ms] : []
    content {
      name  = "postgres.statementTimeoutMs"
      value = set.value
    }
  }
}

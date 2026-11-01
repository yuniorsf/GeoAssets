output "namespace" {
  description = "Kubernetes namespace the tenant's dedicated silo was provisioned into."
  value       = kubernetes_namespace.silo.metadata[0].name
}

output "database_name" {
  description = "Name of the tenant's dedicated Postgres database."
  value       = postgresql_database.tenant.name
}

output "database_role" {
  description = "Name of the tenant's dedicated Postgres role."
  value       = postgresql_role.tenant.name
}

output "connection_secret_name" {
  description = "Name of the Kubernetes Secret holding the tenant's connection string (consumed by the Helm chart's existingSecretName value)."
  value       = kubernetes_secret.fieldservice_db.metadata[0].name
}

output "helm_release_name" {
  description = "Name of the Helm release."
  value       = helm_release.fieldservice.name
}

output "service_dns_name" {
  description = "In-cluster DNS name for the tenant's FieldService.Server Service (reachable from other pods in the same cluster, e.g. the Gateway's silo destination for this tenant)."
  value       = "${helm_release.fieldservice.name}.${kubernetes_namespace.silo.metadata[0].name}.svc.cluster.local"
}

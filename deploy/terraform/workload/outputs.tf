output "lb_ip" {
  description = "External LB IP — set the domain A-records / verify DNS here."
  value       = module.edge.lb_ip
}

output "api_uri" {
  value = module.cloud_run_api.service_uri
}

output "registry_url" {
  value = module.registry.repository_url
}

output "sql_instance_connection_name" {
  description = "Cloud SQL connection name for the DB console tool's Auth Proxy."
  value       = module.data.instance_connection_name
}

output "redis_host" {
  value = module.data.redis_host
}

output "front_bucket" {
  value = module.front_site.bucket_name
}

output "lp_bucket" {
  value = module.lp_site.bucket_name
}

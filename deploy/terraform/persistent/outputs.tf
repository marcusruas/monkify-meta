output "dns_zone_name" {
  description = "Cloud DNS managed-zone name — consumed by the workload layer for record + cert attachment."
  value       = google_dns_managed_zone.primary.name
}

output "dns_name_servers" {
  description = "Nameservers to set at the domain registrar to delegate the zone to Cloud DNS."
  value       = google_dns_managed_zone.primary.name_servers
}

output "state_bucket" {
  description = "Remote-state bucket name."
  value       = google_storage_bucket.tf_state.name
}

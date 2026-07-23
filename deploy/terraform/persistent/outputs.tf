# Each output coalesces the protected and ephemeral variants — exactly one of the two
# exists for a given environment (see main.tf → protect_persistent).

output "dns_zone_name" {
  description = "Cloud DNS managed-zone name — consumed by the workload layer for record + cert attachment."
  value       = one(concat(google_dns_managed_zone.primary_protected[*].name, google_dns_managed_zone.primary_ephemeral[*].name))
}

output "dns_name_servers" {
  description = "Nameservers to set at the domain registrar (GoDaddy) to delegate the zone to Cloud DNS."
  value       = one(concat(google_dns_managed_zone.primary_protected[*].name_servers, google_dns_managed_zone.primary_ephemeral[*].name_servers))
}

output "state_bucket" {
  description = "Remote-state bucket name."
  value       = one(concat(google_storage_bucket.tf_state_protected[*].name, google_storage_bucket.tf_state_ephemeral[*].name))
}

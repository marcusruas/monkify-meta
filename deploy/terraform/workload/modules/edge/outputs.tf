output "lb_ip" {
  description = "External LB IP — the A-record target for all domains."
  value       = google_compute_global_address.lb_ip.address
}

output "security_policy_id" {
  value = google_compute_security_policy.armor.id
}

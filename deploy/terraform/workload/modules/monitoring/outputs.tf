output "dashboard_id" {
  description = "Cloud Monitoring dashboard resource id."
  value       = google_monitoring_dashboard.overview.id
}

output "notification_channel_id" {
  description = <<-EOT
    Empty when alert_email was not set. Alert policies are deliberately out of scope on this card,
    but when they are added they reuse this channel rather than creating a second one.
  EOT
  value       = local.enable_channel ? google_monitoring_notification_channel.ops[0].id : ""
}

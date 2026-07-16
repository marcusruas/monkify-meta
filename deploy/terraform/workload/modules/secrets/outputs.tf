output "secret_ids" {
  description = "Map of secret_id -> Secret Manager resource id."
  value       = { for id, s in google_secret_manager_secret.secret : id => s.id }
}

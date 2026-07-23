output "secret_ids" {
  description = "The set of Secret Manager secret ids this module grants accessor IAM on (created by the deploy tool)."
  value       = var.secret_ids
}

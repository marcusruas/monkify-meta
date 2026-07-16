output "repository_id" {
  value = google_artifact_registry_repository.images.repository_id
}

output "repository_url" {
  description = "Docker registry host/path prefix for images (…/<image>:<tag>)."
  value       = "${var.region}-docker.pkg.dev/${var.project_id}/${google_artifact_registry_repository.images.repository_id}"
}

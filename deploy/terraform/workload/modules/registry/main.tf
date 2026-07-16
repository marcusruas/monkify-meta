# Artifact Registry Docker repo for the API + Consumers images.
resource "google_artifact_registry_repository" "images" {
  project       = var.project_id
  location      = var.region
  repository_id = var.repository_id
  format        = "DOCKER"
  description   = "ChimpOut container images (api, consumers)."
  labels        = var.labels
}

# Least-privilege service account per component. Roles that a component needs
# beyond secret access (secret access is granted per-secret in the secrets module)
# are attached here: Cloud SQL client for DB connectivity.

resource "google_service_account" "api" {
  project      = var.project_id
  account_id   = "${var.name_prefix}-api"
  display_name = "SpaceStroker API (Cloud Run)"
}

resource "google_service_account" "consumers" {
  project      = var.project_id
  account_id   = "${var.name_prefix}-consumers"
  display_name = "SpaceStroker Consumers (Cloud Run)"
}

# Cloud SQL client — both hosts open a Postgres connection.
resource "google_project_iam_member" "api_sql_client" {
  project = var.project_id
  role    = "roles/cloudsql.client"
  member  = "serviceAccount:${google_service_account.api.email}"
}

resource "google_project_iam_member" "consumers_sql_client" {
  project = var.project_id
  role    = "roles/cloudsql.client"
  member  = "serviceAccount:${google_service_account.consumers.email}"
}

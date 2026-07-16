# Reusable static site: a public GCS website bucket behind a Cloud CDN backend bucket.
# Used for both the front SPA and the lp landing page. SPA history-mode routing is
# handled by not_found_page = index.html (unknown paths serve the SPA shell).

resource "google_storage_bucket" "site" {
  name                        = var.bucket_name
  project                     = var.project_id
  location                    = var.location
  uniform_bucket_level_access = true
  force_destroy               = true
  labels                      = var.labels

  website {
    main_page_suffix = "index.html"
    not_found_page   = "index.html"
  }
}

resource "google_storage_bucket_iam_member" "public_read" {
  bucket = google_storage_bucket.site.name
  role   = "roles/storage.objectViewer"
  member = "allUsers"
}

resource "google_compute_backend_bucket" "cdn" {
  name        = "${var.name_prefix}-backend"
  project     = var.project_id
  bucket_name = google_storage_bucket.site.name
  enable_cdn  = true

  # e.g. the front reproduces the retired nginx "Content-Security-Policy: frame-ancestors *".
  custom_response_headers = var.custom_response_headers
}

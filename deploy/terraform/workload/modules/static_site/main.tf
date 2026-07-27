# Reusable static site: a public GCS website bucket behind a Cloud CDN backend bucket.
# Used for both the front SPA and the lp landing page. NOTE: GCS serves not_found_page with an
# HTTP 404 and cannot override that status, so it is only used for real error pages (the lp's
# 404.html). The SPA's history-mode fallback is handled at the load balancer instead — see
# modules/edge, which rewrites the bucket's 404 into /index.html with a 200.

resource "google_storage_bucket" "site" {
  name                        = var.bucket_name
  project                     = var.project_id
  location                    = var.location
  uniform_bucket_level_access = true
  force_destroy               = true
  labels                      = var.labels

  website {
    main_page_suffix = "index.html"
    not_found_page   = var.not_found_page
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

output "bucket_name" {
  value = google_storage_bucket.site.name
}

output "backend_bucket_id" {
  value = google_compute_backend_bucket.cdn.id
}

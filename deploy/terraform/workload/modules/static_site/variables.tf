variable "project_id" {
  type = string
}

variable "name_prefix" {
  type        = string
  description = "Prefix for the backend-bucket name (e.g. spacestroker-front)."
}

variable "bucket_name" {
  type        = string
  description = "Globally-unique GCS bucket name serving the built static site."
}

variable "location" {
  type        = string
  description = "Bucket location (region or multi-region)."
  default     = "US"
}

variable "not_found_page" {
  type        = string
  description = <<-EOT
    Object served for unmatched paths (GCS website config). GCS ALWAYS returns HTTP 404 with it —
    the status cannot be overridden here — so this is only appropriate for a real error page.
    The front SPA leaves this null and gets its 200 fallback from the load balancer's
    custom_error_response_policy instead (see modules/edge). Null ⇒ GCS returns its own plain 404,
    which is what lets missing build assets surface as real 404s.
  EOT
  default     = null
}

variable "custom_response_headers" {
  type        = list(string)
  description = "CDN response headers, e.g. [\"Content-Security-Policy: frame-ancestors *\"]."
  default     = []
}

variable "labels" {
  type    = map(string)
  default = {}
}

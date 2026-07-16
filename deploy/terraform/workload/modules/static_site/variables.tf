variable "project_id" {
  type = string
}

variable "name_prefix" {
  type        = string
  description = "Prefix for the backend-bucket name (e.g. chimpout-front)."
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

variable "custom_response_headers" {
  type        = list(string)
  description = "CDN response headers, e.g. [\"Content-Security-Policy: frame-ancestors *\"]."
  default     = []
}

variable "labels" {
  type    = map(string)
  default = {}
}

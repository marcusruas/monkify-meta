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

variable "custom_response_headers" {
  type        = list(string)
  description = "CDN response headers, e.g. [\"Content-Security-Policy: frame-ancestors *\"]."
  default     = []
}

variable "edge_security_policy_id" {
  type        = string
  description = <<-EOT
    Cloud Armor EDGE security policy to attach to the backend bucket (type CLOUD_ARMOR_EDGE — a
    backend-tier policy is rejected here). Its purpose is observability: it puts the bucket on the
    Cloud Armor path so LB logs carry the client `regionCode`. Empty string ⇒ no policy attached.
  EOT
  default     = ""
}

variable "labels" {
  type    = map(string)
  default = {}
}

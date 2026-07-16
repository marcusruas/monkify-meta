variable "project_id" {
  type        = string
  description = "Existing GCP project id that hosts ChimpOut."
}

variable "region" {
  type        = string
  description = "Default region for regional resources (state bucket location)."
  default     = "us-central1"
}

variable "dns_zone_name" {
  type        = string
  description = "Cloud DNS managed-zone resource name (e.g. chimpout-zone)."
}

variable "dns_domain" {
  type        = string
  description = "Zone DNS name as an FQDN with a trailing dot, e.g. \"chimpout.com.\"."

  validation {
    condition     = endswith(var.dns_domain, ".")
    error_message = "dns_domain must be a fully-qualified name ending in a dot, e.g. \"chimpout.com.\"."
  }
}

variable "state_bucket_name" {
  type        = string
  description = "Globally-unique GCS bucket name that stores Terraform remote state for both layers."
}

variable "labels" {
  type        = map(string)
  description = "Resource labels applied to persistent resources."
  default     = {}
}

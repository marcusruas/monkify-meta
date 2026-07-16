variable "project_id" {
  type = string
}

variable "region" {
  type = string
}

variable "name_prefix" {
  type = string
}

variable "api_service_name" {
  type        = string
  description = "Cloud Run API service name for the serverless NEG."
}

variable "app_backend_bucket_id" {
  type        = string
  description = "Backend-bucket id for the front SPA."
}

variable "lp_backend_bucket_id" {
  type        = string
  description = "Backend-bucket id for the lp landing page (also the LB default service)."
}

variable "dns_zone_name" {
  type        = string
  description = "Cloud DNS managed-zone name from the persistent layer."
}

variable "domain_api" {
  type = string
}

variable "domain_app" {
  type = string
}

variable "domain_lp" {
  type = string
}

variable "domain_lp_www" {
  type = string
}

variable "rate_limit_count" {
  type    = number
  default = 100
}

variable "rate_limit_interval_sec" {
  type    = number
  default = 60
}

variable "project_id" {
  type = string
}

variable "region" {
  type = string
}

variable "name_prefix" {
  type = string
}

variable "network_id" {
  type        = string
  description = "VPC self-link/id for private IP + Redis authorized network."
}

variable "private_vpc_connection_id" {
  type        = string
  description = "Service-networking peering id — a dependency so private IPs allocate after peering."
}

variable "enable_deletion_protection" {
  type        = bool
  description = "Cloud SQL deletion protection. teardown flips this to false."
  default     = true
}

variable "db_instance_suffix" {
  type        = string
  description = "Optional Cloud SQL instance-name suffix (GCP reserves a deleted instance name ~7 days)."
  default     = ""
}

variable "db_tier" {
  type    = string
  default = "db-custom-1-3840"
}

variable "db_availability_type" {
  type    = string
  default = "ZONAL"
}

variable "max_connections" {
  type        = number
  description = "Cloud SQL max_connections (EF pool + Hangfire + MT transport)."
  default     = 200
}

variable "db_app_user" {
  type    = string
  default = "spacestroker_app"
}

variable "db_app_password" {
  type        = string
  description = "App DB user password (from secrets.<env>.json via the deploy tool). State lives in the locked GCS bucket."
  sensitive   = true
}

variable "redis_tier" {
  type    = string
  default = "BASIC"
}

variable "redis_memory_size_gb" {
  type    = number
  default = 1
}

variable "labels" {
  type    = map(string)
  default = {}
}

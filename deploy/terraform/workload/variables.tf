variable "project_id" {
  type = string
}

variable "region" {
  type    = string
  default = "us-central1"
}

variable "name_prefix" {
  type    = string
  default = "spacestroker"
}

variable "labels" {
  type    = map(string)
  default = {}
}

# --- Compute ---------------------------------------------------------------------
variable "api_image" {
  type        = string
  description = "Artifact Registry image for SpaceStroker.Api (…/api:<tag>)."
}

variable "consumers_image" {
  type        = string
  description = "Artifact Registry image for SpaceStroker.Consumers (…/consumers:<tag>)."
}

variable "api_min_instances" {
  type    = number
  default = 1
}

variable "api_max_instances" {
  type    = number
  default = 10
}

variable "consumer_count" {
  type        = number
  description = "Always-on Consumers instances (default 1; >1 is a documented app caveat)."
  default     = 1
}

# --- Data ------------------------------------------------------------------------
variable "enable_deletion_protection" {
  type        = bool
  description = "Cloud SQL deletion protection. teardown sets this false."
  default     = true
}

variable "db_tier" {
  type    = string
  default = "db-custom-2-7680"
}

variable "max_connections" {
  type    = number
  default = 200
}

variable "db_app_user" {
  type    = string
  default = "spacestroker_app"
}

variable "db_app_password" {
  type        = string
  description = "App DB user password (from secrets.<env>.json). State lives in the locked GCS bucket."
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

# --- Static sites ----------------------------------------------------------------
variable "front_bucket_name" {
  type        = string
  description = "Globally-unique GCS bucket for the front SPA."
}

variable "lp_bucket_name" {
  type        = string
  description = "Globally-unique GCS bucket for the lp landing page."
}

variable "bucket_location" {
  type    = string
  default = "US"
}

# --- DNS / domains ---------------------------------------------------------------
variable "dns_zone_name" {
  type        = string
  description = "Cloud DNS managed-zone name from the persistent layer output."
}

variable "domain_api" {
  type    = string
  default = "api.spacestroker.com"
}

variable "domain_app" {
  type    = string
  default = "app.spacestroker.com"
}

variable "domain_lp" {
  type    = string
  default = "spacestroker.com"
}

variable "domain_lp_www" {
  type    = string
  default = "www.spacestroker.com"
}

# --- App config (non-secret env injected into both Cloud Run services) ------------
variable "plain_env" {
  type        = map(string)
  description = "Extra non-secret env vars for both hosts (e.g. GeneralSettings__Token__ClusterUrl)."
  default     = {}
}

variable "project_id" {
  type = string
}

variable "region" {
  type    = string
  default = "us-central1"
}

variable "name_prefix" {
  type        = string
  description = <<-EOT
    Environment-scoped prefix for every project-global resource, always supplied by the deploy tool as
    spacestroker-<envCode> (spacestroker-dev, spacestroker-prod). Development and production share one
    GCP project, so this is what keeps them from colliding. The default is a bare fallback only.
  EOT
  default     = "spacestroker"
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

variable "db_instance_suffix" {
  type        = string
  description = <<-EOT
    Optional suffix for the Cloud SQL instance name. GCP reserves a deleted instance name for ~7 days,
    so redeploying an environment inside that window needs a new name — bump gcp.dbInstanceSuffix in
    secrets.<env>.json (e.g. "2") instead of waiting.
  EOT
  default     = ""
}

variable "db_tier" {
  type = string
  # 1 vCPU / 3.75 GiB. The machine is ~98% of the Cloud SQL bill (vCPU + RAM are billed per
  # hour whether or not anyone plays), so it is sized to the measured load, not to a guess:
  # over 45 days prod averaged 6% CPU (peak 8.5%), 47 peak Postgres backends against
  # max_connections = 200, and a 101 MB database on the 10 GiB disk. Resized 2026-09-21;
  # db-custom-2-7680 cost ~R$589/mo, this is ~R$299/mo.
  # NOTE: the deploy tool's EmitTfvars rewrites <env>.tfvars.json on every deploy and does NOT
  # emit db_tier, so this default is the ONLY place the live tier is pinned — an override in
  # production.tfvars.json would be clobbered on the next deploy.
  default = "db-custom-1-3840"
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

# --- Monitoring ------------------------------------------------------------------
variable "enable_dashboards" {
  type        = bool
  description = <<-EOT
    Create the monitoring module (dashboard, uptime checks, budget, Cloud Armor log-based metrics).
    DERIVED by the deploy tool from environment == "production" — deliberately NOT authored in
    secrets.<env>.json, so there is one less hand-set field to get wrong. Development and production
    share one GCP project, so a dev copy would only clutter the dashboard list; the module block
    carries `count`, which is what keeps development state free of any module.monitoring address at
    all rather than holding zero-instance resources.
  EOT
  default     = false
}

variable "billing_account_id" {
  type        = string
  description = <<-EOT
    Billing account for the spend budget. Empty ⇒ no budget and the apply still succeeds: a budget is
    created against the BILLING ACCOUNT (above the project) and needs roles/billing.costsManager
    granted there, which no project-level role provides.
  EOT
  default     = ""
}

variable "alert_email" {
  type        = string
  description = "Budget threshold mail destination. Empty ⇒ no notification channel, and therefore no budget."
  default     = ""
}

variable "budget_amount_usd" {
  type        = number
  description = "Monthly USD budget the 50/90/100% thresholds are measured against."
  default     = 100
}

# --- App config (non-secret env injected into both Cloud Run services) ------------
variable "plain_env" {
  type        = map(string)
  description = "Extra non-secret env vars for both hosts (e.g. GeneralSettings__Token__ClusterUrl)."
  default     = {}
}

variable "project_id" {
  type = string
}

variable "name_prefix" {
  type        = string
  description = <<-EOT
    Environment-scoped prefix, always spacestroker-<envCode>. EVERY metric filter in this module
    pins it: development and production share one GCP project, so an unscoped filter would report a
    dev deploy's error spike as a production incident.
  EOT
}

variable "billing_account_id" {
  type        = string
  description = <<-EOT
    Billing account for the spend budget. Empty ⇒ no budget, and the apply still succeeds — a budget
    is created against the BILLING ACCOUNT (above the project) and needs an IAM grant there that no
    project-level role provides, so a missing value is an ordinary state, not an error.
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
  description = "Monthly budget in USD that the 50/90/100% thresholds are measured against."
  default     = 100
}

variable "max_connections" {
  type        = number
  description = <<-EOT
    Cloud SQL max_connections, drawn as the threshold line on the connections chart. One instance
    serves both spacestroker and spacestroker_messaging, so exhaustion takes down bets and the
    message transport together — which is why this chart carries a line rather than just a curve.
  EOT
  default     = 200
}

variable "domain_api" {
  type        = string
  description = "API hostname for the uptime check (probes /health)."
}

variable "domain_app" {
  type        = string
  description = "SPA hostname for the uptime check (probes /)."
}

variable "labels" {
  type    = map(string)
  default = {}
}

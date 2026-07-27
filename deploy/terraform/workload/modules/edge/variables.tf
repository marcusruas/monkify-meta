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

# 600/60s, raised from 100/60s. 100 is below what a SINGLE normal player costs once a page load and the
# TokenBalance poll are counted, so two players sharing one VPN exit or a corporate NAT tripped it with
# no misbehaviour at all — the key is IP, so an exit address is one bucket. 600 leaves roughly 10 players
# per shared exit at 60 req/min each. Never overridden in any tfvars: this default IS production.
variable "rate_limit_count" {
  type    = number
  default = 600
}

variable "rate_limit_interval_sec" {
  type    = number
  default = 60
}

# Infrastructure paths (/health, /Hubs/*) get their own, much looser bucket than the general per-IP rule.
# NOT an exemption: a blanket `allow` would leave POST /Hubs/Games/negotiate with no ceiling anywhere,
# and the in-app limiter that would normally backstop it is under suspicion of bucketing every player on
# earth together (Trello #112). 3000/60s is ~5x the general rule — far above any real client, low enough
# to bound an abuser. A throttled health probe makes Cloud Run recycle the instance; a mass SignalR
# reconnect behind one NAT would blank an operator's realtime for everyone on that exit.
variable "infra_rate_limit_count" {
  type    = number
  default = 3000
}

variable "infra_rate_limit_interval_sec" {
  type    = number
  default = 60
}

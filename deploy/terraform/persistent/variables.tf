variable "project_id" {
  type        = string
  description = "Existing GCP project id that hosts SpaceStroker."
}

variable "region" {
  type        = string
  description = "Default region for regional resources (state bucket location)."
  default     = "us-central1"
}

variable "dns_zone_name" {
  type        = string
  description = "Cloud DNS managed-zone resource name (e.g. spacestroker-zone)."
}

variable "dns_domain" {
  type        = string
  description = "Zone DNS name as an FQDN with a trailing dot, e.g. \"spacestroker.com.\"."

  validation {
    condition     = endswith(var.dns_domain, ".")
    error_message = "dns_domain must be a fully-qualified name ending in a dot, e.g. \"spacestroker.com.\"."
  }
}

variable "state_bucket_name" {
  type        = string
  description = <<-EOT
    Globally-unique GCS bucket holding this ENVIRONMENT's remote state for both layers
    (prefixes persistent/ and workload/). Must match what the deploy tool derives:
    spacestroker-<envCode>-... -> "spacestroker-tfstate-<envCode>-<projectId>".
    One bucket per environment is mandatory: a total teardown deletes its own bucket.
  EOT
}

variable "protect_persistent" {
  type        = bool
  description = <<-EOT
    A plain `teardown` keeps the DNS zone + state bucket for EVERY environment; this flag only gates
    the explicit `--purge` wipe.
    true  -> production: the DNS zone + state bucket carry prevent_destroy and force_destroy = false,
             and `--purge` is blocked, so they can never be destroyed by the tool.
    false -> development / other non-production: the same resources are created WITHOUT
             prevent_destroy and with force_destroy = true, so a `teardown --purge` can remove them.
    No default on purpose — every tfvars file must state which it is.
  EOT
}

variable "labels" {
  type        = map(string)
  description = "Resource labels applied to persistent resources."
  default     = {}
}

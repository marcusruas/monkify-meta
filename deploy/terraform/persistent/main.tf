# ---------------------------------------------------------------------------
# PERSISTENT layer — resources that must SURVIVE `teardown`.
# The deploy tool's `teardown` runs `terraform destroy` on the WORKLOAD layer
# only; this layer is never destroyed, so the domain/DNS + state bucket live on.
# Every resource here is guarded with prevent_destroy as a second safety net.
# ---------------------------------------------------------------------------

# Remote-state bucket (versioned + retained). Holds state for BOTH layers under
# distinct prefixes (persistent/, workload/).
resource "google_storage_bucket" "tf_state" {
  name                        = var.state_bucket_name
  project                     = var.project_id
  location                    = var.region
  force_destroy               = false
  uniform_bucket_level_access = true
  labels                      = var.labels

  versioning {
    enabled = true
  }

  lifecycle {
    prevent_destroy = true
  }
}

# Public DNS zone for the ChimpOut domain. Domain REGISTRATION is out-of-band:
# after the first apply, repoint the registrar's nameservers to
# `google_dns_managed_zone.primary.name_servers` (see the dns_name_servers output).
resource "google_dns_managed_zone" "primary" {
  name        = var.dns_zone_name
  project     = var.project_id
  dns_name    = var.dns_domain
  description = "ChimpOut public DNS zone (persistent — survives teardown)."
  labels      = var.labels

  lifecycle {
    prevent_destroy = true
  }
}

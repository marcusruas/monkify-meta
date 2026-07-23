# ---------------------------------------------------------------------------
# PERSISTENT layer — the Cloud DNS zone + the remote-state bucket.
#
# PRODUCTION (protect_persistent = true): these SURVIVE `teardown`. The deploy
# tool destroys the WORKLOAD layer only, and both resources carry prevent_destroy
# as a second safety net, so the domain/DNS + state live on.
#
# DEVELOPMENT / other non-production (protect_persistent = false): `teardown` is
# TOTAL — it destroys this layer too, so nothing Terraform created survives except
# the registrar's domain registration. Those variants are created WITHOUT
# prevent_destroy and with force_destroy = true.
#
# Why two resources per thing instead of one conditional: lifecycle.prevent_destroy
# only accepts a literal (it cannot reference a variable), so the protected and
# ephemeral shapes are separate resources with mutually exclusive counts.
# ---------------------------------------------------------------------------

# Remote-state bucket (versioned + retained). Holds state for BOTH layers of THIS
# environment under distinct prefixes (persistent/, workload/). The bucket name is
# env-scoped (spacestroker-tfstate-<envCode>-<projectId>) so a development teardown
# deletes its own bucket and never production's.
resource "google_storage_bucket" "tf_state_protected" {
  count                       = var.protect_persistent ? 1 : 0
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

resource "google_storage_bucket" "tf_state_ephemeral" {
  count    = var.protect_persistent ? 0 : 1
  name     = var.state_bucket_name
  project  = var.project_id
  location = var.region
  # Required: at destroy time the bucket still holds this layer's (migrated-away)
  # state objects and their versions, which would otherwise block the delete.
  force_destroy               = true
  uniform_bucket_level_access = true
  labels                      = var.labels

  versioning {
    enabled = true
  }
}

# Public DNS zone for this environment. Domain REGISTRATION is out-of-band (GoDaddy):
# after the first apply, point the registrar (or the parent zone) at the
# dns_name_servers output. The deploy tool prints them when it bootstraps this layer.
resource "google_dns_managed_zone" "primary_protected" {
  count       = var.protect_persistent ? 1 : 0
  name        = var.dns_zone_name
  project     = var.project_id
  dns_name    = var.dns_domain
  description = "SpaceStroker public DNS zone (protected — survives teardown)."
  labels      = var.labels

  lifecycle {
    prevent_destroy = true
  }
}

resource "google_dns_managed_zone" "primary_ephemeral" {
  count       = var.protect_persistent ? 0 : 1
  name        = var.dns_zone_name
  project     = var.project_id
  dns_name    = var.dns_domain
  description = "SpaceStroker public DNS zone (ephemeral — destroyed by a total teardown)."
  labels      = var.labels
  # Drop any record sets the workload layer left behind instead of blocking the destroy.
  force_destroy = true
}

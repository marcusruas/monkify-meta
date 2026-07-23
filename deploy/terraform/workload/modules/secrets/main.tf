# Secret CONTAINERS and their VERSIONS are created by the deploy tool's SyncSecrets (which runs
# `gcloud secrets create` + `versions add` from secrets.<env>.json BEFORE this Terraform apply), so
# the versions already exist when Cloud Run mounts them. Terraform must NOT also create the containers
# — it would collide with the tool's ("Error 409: Secret ... already exists") and abort the apply.
# This module therefore only grants accessor IAM on those pre-existing secrets; teardown's
# SweepOrphanSecrets deletes the containers, so nothing is orphaned by Terraform not owning them.

# accessor_members is a map of secret_id -> list(service-account members) that may read it.
locals {
  accessor_pairs = flatten([
    for secret_id, members in var.accessor_members : [
      for member in members : {
        key       = "${secret_id}::${member}"
        secret_id = secret_id
        member    = member
      }
    ]
  ])
}

resource "google_secret_manager_secret_iam_member" "accessor" {
  for_each  = { for pair in local.accessor_pairs : pair.key => pair }
  project   = var.project_id
  secret_id = each.value.secret_id
  role      = "roles/secretmanager.secretAccessor"
  member    = each.value.member
}

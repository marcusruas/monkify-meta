# Secret Manager secret CONTAINERS only — no versions are committed or set here.
# The deploy tool syncs each secret's VALUE (a new version) from secrets.<env>.json
# at deploy time, so no secret material enters Terraform state or the repo.
# Each component's service account is granted accessor ONLY on the secrets it reads.

resource "google_secret_manager_secret" "secret" {
  for_each  = var.secret_ids
  project   = var.project_id
  secret_id = each.value

  replication {
    auto {}
  }

  labels = var.labels
}

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
  secret_id = google_secret_manager_secret.secret[each.value.secret_id].secret_id
  role      = "roles/secretmanager.secretAccessor"
  member    = each.value.member
}

terraform {
  required_version = ">= 1.6"

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 6.0"
    }
  }

  # Remote state for the PERSISTENT layer lives in the PER-ENVIRONMENT state bucket this layer
  # creates: spacestroker-tfstate-<envCode>-<projectId>, prefix "persistent".
  # Bootstrap (chicken-and-egg): the very first run uses local state. Terraform >=1.13 refuses to
  # `apply` while this `backend "gcs" {}` is declared-but-uninitialized (`-backend=false` alone is not
  # enough), so drop a temporary override that swaps the backend to local for the bootstrap apply:
  #   echo 'terraform { backend "local" {} }' > zzz_bootstrap_override.tf
  #   TF_DATA_DIR=.terraform-<envCode> terraform init -reconfigure
  #   TF_DATA_DIR=.terraform-<envCode> terraform apply -var-file=<environment>.tfvars
  # then remove the override and migrate the local state into the freshly created bucket:
  #   rm zzz_bootstrap_override.tf
  #   TF_DATA_DIR=.terraform-<envCode> terraform init -migrate-state -force-copy \
  #     -backend-config="bucket=<state_bucket>" -backend-config="prefix=persistent"
  # `spacestroker-deploy deploy` now does all of this automatically when the bucket is missing.
  backend "gcs" {}
}

provider "google" {
  project = var.project_id
  region  = var.region
}

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
  # Bootstrap (chicken-and-egg): the very first run uses local state
  #   TF_DATA_DIR=.terraform-<envCode> terraform init -reconfigure -backend=false
  #   TF_DATA_DIR=.terraform-<envCode> terraform apply -var-file=<environment>.tfvars
  # to create the bucket, then migrate:
  #   TF_DATA_DIR=.terraform-<envCode> terraform init -migrate-state -force-copy \
  #     -backend-config="bucket=<state_bucket>" -backend-config="prefix=persistent"
  # `spacestroker-deploy deploy` now does all of this automatically when the bucket is missing.
  backend "gcs" {}
}

provider "google" {
  project = var.project_id
  region  = var.region
}

terraform {
  required_version = ">= 1.6"

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 6.0"
    }
  }

  # Remote state for the PERSISTENT layer lives in the state bucket this layer creates.
  # Bootstrap (chicken-and-egg): the very first run uses local state
  #   terraform init -backend=false      # then: terraform apply -var-file=production.tfvars
  # to create the bucket, then migrate:
  #   terraform init -migrate-state -backend-config="bucket=<state_bucket>" -backend-config="prefix=persistent"
  backend "gcs" {}
}

provider "google" {
  project = var.project_id
  region  = var.region
}

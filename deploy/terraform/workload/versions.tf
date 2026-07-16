terraform {
  required_version = ">= 1.6"

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 6.0"
    }
  }

  # Remote state in the versioned state bucket (persistent layer), under the workload prefix.
  #   terraform init -backend-config="bucket=<state_bucket>" -backend-config="prefix=workload"
  backend "gcs" {}
}

provider "google" {
  project = var.project_id
  region  = var.region
}

terraform {
  required_version = ">= 1.6"

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 6.0"
    }
  }

  # Remote state in this ENVIRONMENT's versioned state bucket (created by the persistent layer),
  # under the workload prefix. The bucket differs per environment, so always init with -reconfigure
  # and a per-environment TF_DATA_DIR:
  #   TF_DATA_DIR=.terraform-<envCode> terraform init -reconfigure \
  #     -backend-config="bucket=spacestroker-tfstate-<envCode>-<projectId>" -backend-config="prefix=workload"
  backend "gcs" {}
}

provider "google" {
  project = var.project_id
  region  = var.region
}

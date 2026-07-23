# SpaceStroker.Api — public (LB-fronted), autoscaling Cloud Run service, min >= 1.
# Reads secrets via Secret Manager references (IAM at boot); plain config via env.

resource "google_cloud_run_v2_service" "api" {
  name                = "${var.name_prefix}-api"
  project             = var.project_id
  location            = var.region
  ingress             = "INGRESS_TRAFFIC_ALL"
  deletion_protection = false

  template {
    service_account = var.service_account_email

    scaling {
      min_instance_count = var.min_instances
      max_instance_count = var.max_instances
    }

    # Direct VPC egress: attach the instance straight to the subnet (no connector).
    vpc_access {
      egress = "PRIVATE_RANGES_ONLY"
      network_interfaces {
        network    = var.network_id
        subnetwork = var.subnet_id
      }
    }

    containers {
      image = var.image

      ports {
        container_port = 8080
      }

      resources {
        limits = {
          cpu    = var.cpu
          memory = var.memory
        }
        cpu_idle = true
      }

      dynamic "env" {
        for_each = var.plain_env
        content {
          name  = env.key
          value = env.value
        }
      }

      dynamic "env" {
        for_each = var.secret_env
        content {
          name = env.key
          value_source {
            secret_key_ref {
              secret  = env.value
              version = "latest"
            }
          }
        }
      }

      startup_probe {
        http_get {
          path = var.health_path
        }
        initial_delay_seconds = 10
        timeout_seconds       = 3
        period_seconds        = 10
        failure_threshold     = 30
      }

      liveness_probe {
        http_get {
          path = var.health_path
        }
        period_seconds = 30
      }
    }
  }
}

# Public: the external LB (and browsers through it) invoke the service; auth is enforced
# by the app's launch/admin token middleware, not by Cloud Run IAM.
resource "google_cloud_run_v2_service_iam_member" "public_invoker" {
  count    = var.allow_public_invoker ? 1 : 0
  project  = var.project_id
  location = var.region
  name     = google_cloud_run_v2_service.api.name
  role     = "roles/run.invoker"
  member   = "allUsers"
}

# SpaceStroker.Consumers — INTERNAL-only Cloud Run service (never on the public LB; it
# hosts /hangfire and owns EF migrations/seeding). CPU always-on (runs the game loop
# + Hangfire), instance count pinned to consumer_count (never scales to zero).

resource "google_cloud_run_v2_service" "consumers" {
  name                = "${var.name_prefix}-consumers"
  project             = var.project_id
  location            = var.region
  ingress             = "INGRESS_TRAFFIC_INTERNAL_ONLY"
  deletion_protection = false

  template {
    service_account = var.service_account_email

    scaling {
      min_instance_count = var.consumer_count
      max_instance_count = var.consumer_count
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
        # CPU always allocated — the background game loop + Hangfire run off-request.
        cpu_idle = false
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
        initial_delay_seconds = 15
        timeout_seconds       = 3
        period_seconds        = 10
        # Migrations + seeding run before the HTTP server listens; allow a long startup.
        failure_threshold = 60
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

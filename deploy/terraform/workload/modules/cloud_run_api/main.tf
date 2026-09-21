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
        # Instance-based billing (CPU always allocated). The API is NOT an idle service: the
        # SignalR terminal stream keeps a request open, so Cloud Run reports the instance in the
        # `active` state 92.6% of the time (7-day mean). Request-based billing charges active CPU
        # at R$0.000140887/vCPU-s vs R$0.000105665 always-allocated, and the break-even is ~72%
        # active — well below where this service runs, so `true` was paying the premium rate for
        # a discount it never qualified for (~R$68/mo). Always-allocated is also strictly better
        # for latency: no throttling of SignalR heartbeats or GC between requests.
        # Revisit if sustained traffic ever drops the active fraction below ~72%.
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

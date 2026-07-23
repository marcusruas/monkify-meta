# Private-IP-only data tier: one Cloud SQL Postgres 16 instance hosting BOTH the
# game DB (spacestroker) and the messaging DB (spacestroker_messaging), plus Memorystore
# Redis 7. No public IPs — reachable only over the VPC / private services peering.

resource "google_sql_database_instance" "postgres" {
  # GCP reserves a deleted instance name for ~7 days, so a same-week redeploy of a torn-down
  # environment needs a new name — that is what db_instance_suffix is for.
  name             = "${var.name_prefix}-pg${var.db_instance_suffix == "" ? "" : "-${var.db_instance_suffix}"}"
  project          = var.project_id
  region           = var.region
  database_version = "POSTGRES_16"

  # Overridden to false by the deploy tool's `teardown` so the workload can be destroyed.
  deletion_protection = var.enable_deletion_protection

  settings {
    # ENTERPRISE (not ENTERPRISE_PLUS) is required to use shared-core / custom machine tiers such as
    # db-custom-2-7680 and db-f1-micro. New POSTGRES_16 instances otherwise default to ENTERPRISE_PLUS,
    # which only accepts db-perf-optimized-N-* tiers and rejects our custom tier at create time.
    edition           = "ENTERPRISE"
    tier              = var.db_tier
    availability_type = var.db_availability_type
    disk_autoresize   = true
    user_labels       = var.labels

    ip_configuration {
      ipv4_enabled    = false
      private_network = var.network_id
    }

    # Sized for EF pool + Hangfire + the MassTransit SQL transport LISTEN/poll connections
    # sharing one instance (#64 handoff).
    database_flags {
      name  = "max_connections"
      value = tostring(var.max_connections)
    }

    backup_configuration {
      enabled = true
    }
  }

  # The private IP requires the service-networking peering to exist first.
  depends_on = [var.private_vpc_connection_id]
}

resource "google_sql_database" "game" {
  name     = "spacestroker"
  project  = var.project_id
  instance = google_sql_database_instance.postgres.name
}

# Messaging DB for the MassTransit Postgres SQL transport (#64). Pre-created here so
# terraform provisions "one instance, two DBs"; the Consumers MT migration hosted
# service is idempotent against an existing DB.
resource "google_sql_database" "messaging" {
  name     = "spacestroker_messaging"
  project  = var.project_id
  instance = google_sql_database_instance.postgres.name
}

# Application DB user reused for both databases (only Database= differs — #64).
resource "google_sql_user" "app" {
  name     = var.db_app_user
  project  = var.project_id
  instance = google_sql_database_instance.postgres.name
  password = var.db_app_password
}

resource "google_redis_instance" "cache" {
  name               = "${var.name_prefix}-redis"
  project            = var.project_id
  region             = var.region
  tier               = var.redis_tier
  memory_size_gb     = var.redis_memory_size_gb
  redis_version      = "REDIS_7_0"
  authorized_network = var.network_id
  connect_mode       = "PRIVATE_SERVICE_ACCESS"
  labels             = var.labels

  depends_on = [var.private_vpc_connection_id]
}

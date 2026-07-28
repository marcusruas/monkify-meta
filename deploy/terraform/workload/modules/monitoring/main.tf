# Production observability: one dashboard, two uptime checks, an email notification channel, a spend
# budget, and two Cloud Armor log-based metrics.
#
# The whole module is created only when var.enable_dashboards is true in the workload root, and that
# module block carries `count` — so a development plan holds NO module.monitoring address at all,
# rather than a set of zero-instance resources. Development and production share one GCP project, so
# that distinction is what keeps a dev teardown from ever reaching a production-facing object.
#
# NOTE: error/warning counts need NO log-based metric here. Once the app emits structured JSON with a
# real `severity` (monkify-backend, same card), the BUILT-IN logging.googleapis.com/log_entry_count
# metric is labelled by severity and the dashboard reads it directly. That backend change is what
# makes those panels possible at all — plain-text stdout lands at severity DEFAULT.

locals {
  # Both gates exist because each needs operator action outside Terraform's project-scoped reach: a
  # budget needs a billing-account IAM grant, and it must notify somewhere. Skipping beats failing
  # the whole apply for an operator who has not done the billing half yet.
  enable_channel = var.alert_email != ""
  enable_budget  = var.alert_email != "" && var.billing_account_id != ""
}

# --- Notification channel ---------------------------------------------------------
resource "google_monitoring_notification_channel" "ops" {
  count        = local.enable_channel ? 1 : 0
  project      = var.project_id
  display_name = "${var.name_prefix}-ops-email"
  type         = "email"

  labels = {
    email_address = var.alert_email
  }
}

# --- Uptime checks ----------------------------------------------------------------
# The only signal in this module that detects "the whole site is down" rather than "a metric looks
# odd". Everything else here charts a system that is already answering.
#
# period = 300s is deliberate. These originate OUTSIDE the VPC and traverse the public LB, so they
# spend the priority-800 Cloud Armor bucket (3000/60s, shared with /Hubs/*). Google probes from
# several regions per period; at five minutes that is a rounding error against the bucket. Tightening
# this without re-reading modules/edge/main.tf is how you make the monitoring the incident.
resource "google_monitoring_uptime_check_config" "api" {
  project      = var.project_id
  display_name = "${var.name_prefix}-uptime-api"
  timeout      = "10s"
  period       = "300s"

  http_check {
    path         = "/health"
    port         = 443
    use_ssl      = true
    validate_ssl = true
  }

  monitored_resource {
    type = "uptime_url"
    labels = {
      project_id = var.project_id
      host       = var.domain_api
    }
  }
}

resource "google_monitoring_uptime_check_config" "app" {
  project      = var.project_id
  display_name = "${var.name_prefix}-uptime-app"
  timeout      = "10s"
  period       = "300s"

  http_check {
    path         = "/"
    port         = 443
    use_ssl      = true
    validate_ssl = true
  }

  monitored_resource {
    type = "uptime_url"
    labels = {
      project_id = var.project_id
      host       = var.domain_app
    }
  }
}

# --- Cloud Armor log-based metrics ------------------------------------------------
# LB request logging is already enabled at sample_rate 1.0 (modules/edge/main.tf), so no edge change
# is needed. Env-scoping comes from url_map_name, which is "${name_prefix}-urlmap".
#
# Why a log-based metric AND the response-code chart on the dashboard: the LB's 429/403 count is free
# but CANNOT distinguish Cloud Armor's 429 from the in-app AspNetCoreRateLimit 429 (both are live on
# /rpc and /solana/*). The code-class chart gives volume; this gives attribution.
#
# Cardinality is bounded ON PURPOSE: only three rules exist (800/900/1000), so priority and
# configured_action are small closed sets. Never add a path or client-IP label here — that unbounded
# label is exactly what ruled out a per-endpoint request metric on this card.
resource "google_logging_metric" "armor_denied" {
  name    = "${var.name_prefix}-armor-denied"
  project = var.project_id
  filter  = <<-EOT
    resource.type="http_load_balancer"
    resource.labels.url_map_name="${var.name_prefix}-urlmap"
    jsonPayload.enforcedSecurityPolicy.outcome="DENY"
  EOT

  metric_descriptor {
    metric_kind = "DELTA"
    value_type  = "INT64"
    unit        = "1"

    labels {
      key         = "priority"
      value_type  = "STRING"
      description = "Armor rule priority that denied the request (800, 900 or 1000)."
    }
    labels {
      key         = "configured_action"
      value_type  = "STRING"
      description = "The rule's configured action (throttle, deny)."
    }
  }

  label_extractors = {
    "priority"          = "EXTRACT(jsonPayload.enforcedSecurityPolicy.priority)"
    "configured_action" = "EXTRACT(jsonPayload.enforcedSecurityPolicy.configuredAction)"
  }
}

# ⚠️ Reads previewSecurityPolicy, NOT enforcedSecurityPolicy. A rule at preview = true populates the
# OTHER field entirely, so pointing this at the enforced one yields a permanently-zero panel that
# reads as "the WAF never false-positives" — when the priority-900 OWASP rule sits in preview
# PRECISELY because it does, on Solana base58 bet payloads. A blocked bet is money never placed, so
# a silent zero here is the most expensive possible bug in this module.
resource "google_logging_metric" "armor_preview" {
  name    = "${var.name_prefix}-armor-preview"
  project = var.project_id
  filter  = <<-EOT
    resource.type="http_load_balancer"
    resource.labels.url_map_name="${var.name_prefix}-urlmap"
    jsonPayload.previewSecurityPolicy.outcome="DENY"
  EOT

  metric_descriptor {
    metric_kind = "DELTA"
    value_type  = "INT64"
    unit        = "1"

    labels {
      key         = "priority"
      value_type  = "STRING"
      description = "Preview rule priority that WOULD have denied the request."
    }
    labels {
      key         = "configured_action"
      value_type  = "STRING"
      description = "The action the preview rule would have taken."
    }
  }

  label_extractors = {
    "priority"          = "EXTRACT(jsonPayload.previewSecurityPolicy.priority)"
    "configured_action" = "EXTRACT(jsonPayload.previewSecurityPolicy.configuredAction)"
  }
}

# --- Budget -----------------------------------------------------------------------
# Cloud Monitoring cannot chart cost at all — there is no cost data source and no embed widget. So
# the dashboard LINKS to Billing → Reports (already the "biggest cost desc" view the card asked for)
# and this budget supplies the one thing Reports cannot: telling someone without being looked at.
#
# The budget covers the WHOLE PROJECT, development spend included. Cost attribution keys off LABELS,
# not names — name_prefix scopes resource names and does nothing for billing — and per-module label
# coverage is unverified. A partial label filter would UNDER-report spend, which is worse than a
# project-wide budget that over-reports.
resource "google_billing_budget" "spend" {
  count           = local.enable_budget ? 1 : 0
  billing_account = var.billing_account_id
  display_name    = "${var.name_prefix}-budget"

  budget_filter {
    projects = ["projects/${var.project_id}"]
  }

  amount {
    specified_amount {
      currency_code = "USD"
      units         = tostring(var.budget_amount_usd)
    }
  }

  dynamic "threshold_rules" {
    for_each = [0.5, 0.9, 1.0]
    content {
      threshold_percent = threshold_rules.value
    }
  }

  all_updates_rule {
    monitoring_notification_channels = [google_monitoring_notification_channel.ops[0].id]
    disable_default_iam_recipients   = true
  }
}

# --- Dashboard --------------------------------------------------------------------
# One dashboard, not three: a single bookmark and no tab-switching mid-incident. The layout lives in
# dashboard.json.tftpl rather than inline here — it is one large JSON document inside one Terraform
# resource, and keeping it in its own file is what makes a change to it reviewable in a diff.
#
# Every Cloud Run filter pins resource.labels.service_name to this environment's services, so no
# spacestroker-dev-* series can appear on the production dashboard.
resource "google_monitoring_dashboard" "overview" {
  project = var.project_id

  dashboard_json = templatefile("${path.module}/dashboard.json.tftpl", {
    name_prefix     = var.name_prefix
    project_id      = var.project_id
    max_connections = var.max_connections
  })
}

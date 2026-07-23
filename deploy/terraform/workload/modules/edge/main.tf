# External HTTPS load balancer → Cloud Armor (WAF + per-IP rate limit) → api (Cloud Run
# serverless NEG) / app + lp (CDN backend buckets). Google-managed TLS certs; Cloud DNS
# A-records point the domains at the LB IP. HTTP :80 redirects to HTTPS.

# --- Cloud Armor -----------------------------------------------------------------
resource "google_compute_security_policy" "armor" {
  name    = "${var.name_prefix}-armor"
  project = var.project_id

  # Per-IP rate limiting (complements the app's in-process IpRateLimiting).
  rule {
    action   = "rate_based_ban"
    priority = 1000
    match {
      versioned_expr = "SRC_IPS_V1"
      config {
        src_ip_ranges = ["*"]
      }
    }
    rate_limit_options {
      conform_action = "allow"
      exceed_action  = "deny(429)"
      enforce_on_key = "IP"
      rate_limit_threshold {
        count        = var.rate_limit_count
        interval_sec = var.rate_limit_interval_sec
      }
      ban_duration_sec = 300
    }
    description = "Per-IP rate limit."
  }

  # OWASP preconfigured WAF rules (SQLi / XSS) — tuned to the LEAST-aggressive sensitivity and run in
  # PREVIEW (log-only, NOT enforcing). SpaceStroker's API legitimately carries base58/base64 Solana
  # signatures, wallet addresses, and hex seeds in normal request bodies (e.g. POST /games/{id}/bets,
  # launch-token, …). The preconfigured SQLi/XSS signatures false-positive on those and return 403 —
  # even at sensitivity 1 — which blocks real play; because the 403 is emitted by Cloud Armor before
  # the app runs, it also carries no CORS headers, so browsers report it as a CORS error.
  # Preview keeps the rule EVALUATING + LOGGING (see log_config on the api backend below) so genuine
  # attacks stay visible. Only re-enable enforcement (preview = false) after tuning `opt_out_rule_ids`
  # for the specific signatures those logs show, so legitimate crypto payloads are not caught.
  rule {
    action   = "deny(403)"
    priority = 900
    preview  = true
    match {
      expr {
        expression = "evaluatePreconfiguredWaf('sqli-v33-stable', {'sensitivity': 1}) || evaluatePreconfiguredWaf('xss-v33-stable', {'sensitivity': 1})"
      }
    }
    description = "OWASP SQLi/XSS (preview / log-only — see comment)."
  }

  # Default allow (required lowest-priority rule).
  rule {
    action   = "allow"
    priority = 2147483647
    match {
      versioned_expr = "SRC_IPS_V1"
      config {
        src_ip_ranges = ["*"]
      }
    }
    description = "Default allow."
  }
}

# --- API backend (serverless NEG) ------------------------------------------------
resource "google_compute_region_network_endpoint_group" "api_neg" {
  name                  = "${var.name_prefix}-api-neg"
  project               = var.project_id
  region                = var.region
  network_endpoint_type = "SERVERLESS"
  cloud_run {
    service = var.api_service_name
  }
}

resource "google_compute_backend_service" "api" {
  name                  = "${var.name_prefix}-api-backend"
  project               = var.project_id
  protocol              = "HTTPS"
  load_balancing_scheme = "EXTERNAL_MANAGED"
  security_policy       = google_compute_security_policy.armor.id

  backend {
    group = google_compute_region_network_endpoint_group.api_neg.id
  }

  # Log LB + Cloud Armor decisions (including PREVIEW matches from the WAF rule above) so WAF
  # false-positives and real attacks are both visible in Cloud Logging.
  log_config {
    enable      = true
    sample_rate = 1.0
  }
}

# --- URL map (host routing) ------------------------------------------------------
resource "google_compute_url_map" "lb" {
  name            = "${var.name_prefix}-urlmap"
  project         = var.project_id
  default_service = var.lp_backend_bucket_id

  host_rule {
    hosts        = [var.domain_api]
    path_matcher = "api"
  }
  host_rule {
    hosts        = [var.domain_app]
    path_matcher = "app"
  }
  host_rule {
    hosts        = [var.domain_lp, var.domain_lp_www]
    path_matcher = "lp"
  }

  path_matcher {
    name            = "api"
    default_service = google_compute_backend_service.api.id
  }
  path_matcher {
    name            = "app"
    default_service = var.app_backend_bucket_id
  }
  path_matcher {
    name            = "lp"
    default_service = var.lp_backend_bucket_id
  }
}

# --- TLS + HTTPS front end -------------------------------------------------------
resource "google_compute_managed_ssl_certificate" "cert" {
  name    = "${var.name_prefix}-cert"
  project = var.project_id
  managed {
    domains = [var.domain_api, var.domain_app, var.domain_lp, var.domain_lp_www]
  }
}

resource "google_compute_target_https_proxy" "https" {
  name             = "${var.name_prefix}-https"
  project          = var.project_id
  url_map          = google_compute_url_map.lb.id
  ssl_certificates = [google_compute_managed_ssl_certificate.cert.id]
}

resource "google_compute_global_address" "lb_ip" {
  name    = "${var.name_prefix}-lb-ip"
  project = var.project_id
}

resource "google_compute_global_forwarding_rule" "https" {
  name                  = "${var.name_prefix}-https-fr"
  project               = var.project_id
  target                = google_compute_target_https_proxy.https.id
  ip_address            = google_compute_global_address.lb_ip.id
  port_range            = "443"
  load_balancing_scheme = "EXTERNAL_MANAGED"
}

# --- HTTP :80 -> HTTPS redirect --------------------------------------------------
resource "google_compute_url_map" "redirect" {
  name    = "${var.name_prefix}-redirect"
  project = var.project_id
  default_url_redirect {
    https_redirect         = true
    redirect_response_code = "MOVED_PERMANENTLY_DEFAULT"
    strip_query            = false
  }
}

resource "google_compute_target_http_proxy" "http" {
  name    = "${var.name_prefix}-http"
  project = var.project_id
  url_map = google_compute_url_map.redirect.id
}

resource "google_compute_global_forwarding_rule" "http" {
  name                  = "${var.name_prefix}-http-fr"
  project               = var.project_id
  target                = google_compute_target_http_proxy.http.id
  ip_address            = google_compute_global_address.lb_ip.id
  port_range            = "80"
  load_balancing_scheme = "EXTERNAL_MANAGED"
}

# --- DNS A records (in the persistent zone) --------------------------------------
resource "google_dns_record_set" "api" {
  name         = "${var.domain_api}."
  project      = var.project_id
  managed_zone = var.dns_zone_name
  type         = "A"
  ttl          = 300
  rrdatas      = [google_compute_global_address.lb_ip.address]
}

resource "google_dns_record_set" "app" {
  name         = "${var.domain_app}."
  project      = var.project_id
  managed_zone = var.dns_zone_name
  type         = "A"
  ttl          = 300
  rrdatas      = [google_compute_global_address.lb_ip.address]
}

resource "google_dns_record_set" "lp" {
  name         = "${var.domain_lp}."
  project      = var.project_id
  managed_zone = var.dns_zone_name
  type         = "A"
  ttl          = 300
  rrdatas      = [google_compute_global_address.lb_ip.address]
}

resource "google_dns_record_set" "lp_www" {
  name         = "${var.domain_lp_www}."
  project      = var.project_id
  managed_zone = var.dns_zone_name
  type         = "A"
  ttl          = 300
  rrdatas      = [google_compute_global_address.lb_ip.address]
}

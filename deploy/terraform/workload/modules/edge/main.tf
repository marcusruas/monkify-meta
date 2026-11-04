# External HTTPS load balancer → Cloud Armor (WAF + per-IP rate limit) → api (Cloud Run
# serverless NEG) / app + lp (CDN backend buckets). Google-managed TLS certs; Cloud DNS
# A-records point the domains at the LB IP. HTTP :80 redirects to HTTPS.

# --- Cloud Armor -----------------------------------------------------------------
resource "google_compute_security_policy" "armor" {
  name    = "${var.name_prefix}-armor"
  project = var.project_id

  # Infrastructure paths get a much looser per-IP bucket than player traffic. Deliberately a looser
  # THROTTLE and not an `allow` exemption — an exemption would leave POST /Hubs/Games/negotiate with no
  # ceiling anywhere, and the in-app limiter that would normally backstop it is under suspicion (#112).
  # See the variable comments in variables.tf and monkify-meta/specs/entry-path-recovery.md §7.
  #
  # Matches on `expr` (request path), not versioned_expr (source IP): the two forms are mutually
  # exclusive within a single match block.
  rule {
    action   = "throttle"
    priority = 800
    match {
      expr {
        expression = "request.path.startsWith('/Hubs/') || request.path == '/health'"
      }
    }
    rate_limit_options {
      conform_action = "allow"
      exceed_action  = "deny(429)"
      enforce_on_key = "IP"
      rate_limit_threshold {
        count        = var.infra_rate_limit_count
        interval_sec = var.infra_rate_limit_interval_sec
      }
    }
    description = "Looser per-IP throttle for /health + /Hubs/* (infrastructure paths)."
  }

  # ⚠️ A rule's `action` CANNOT be changed in place. The provider updates rules via patchRule, which
  # MERGES rather than replaces, so leftover fields from the old action survive and collide: #104's
  # rate_based_ban -> throttle change failed with "Ban threshold or duration can only be specified if
  # the action is 'rate_based_ban'" because banDurationSec: 300 outlived the patch. To change an action,
  # delete the live rule first (gcloud compute security-policies rules delete <priority>
  # --security-policy <policy>) or bump the rule's priority for an atomic remove+add. Adding a NEW rule
  # at a NEW priority — as the rule above does — is unaffected.

  # Per-IP rate limiting (complements the app's in-process IpRateLimiting).
  #
  # THROTTLE, not rate_based_ban. A ban denies EVERY request from the IP for its whole duration, and the
  # policy is attached to the api backend service only — the backend buckets have none. So a player who
  # briefly bursts (several tabs, a reload flurry) kept getting 200s for the static assets while every
  # API call 429'd: the shell rendered, the app looked broken rather than rate-limited, they reloaded,
  # and each reload RENEWED the ban. That is how a few seconds of excess became a multi-minute lockout
  # (measured 2026-07-26: 143 API requests, 17 loads across ~5 tabs, then 300 s of denial that kept
  # re-arming). Throttling denies only the requests ABOVE the rate, so the next interval is served
  # normally and the lockout cannot outlive its own cause.
  # See monkify-meta/specs/player-lockouts.md §7.
  rule {
    action   = "throttle"
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
    }
    description = "Per-IP throttle (excess denied; no ban)."
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

    # NOTE: deliberately NO default_custom_error_response_policy here. A path_rule that omits the
    # policy INHERITS the matcher default, so a matcher-level policy would silently apply to the
    # asset rule below and defeat the carve-out. Leaving matcher + url_map defaults unset is what
    # lets the asset rule fall through to the bucket's real 404.

    # Hashed build assets. Longest-path-first matching means these win over "/*" below, and with no
    # policy anywhere up the chain a missing chunk surfaces as a genuine 404 instead of HTML.
    path_rule {
      paths   = ["/js/*", "/css/*", "/img/*", "/media/*"]
      service = var.app_backend_bucket_id
    }

    # Everything else is an SPA history route: serve the shell with a 200 so monitors, unfurls and
    # crawlers see a real page. The bucket 404s (its not_found_page is unset — see static_site), and
    # this policy rewrites that 404 into /index.html + 200.
    path_rule {
      paths   = ["/*"]
      service = var.app_backend_bucket_id

      custom_error_response_policy {
        error_response_rule {
          match_response_codes   = ["404"]
          path                   = "/index.html"
          override_response_code = 200
        }
        # Must be a BackendBucket; nothing higher in the url_map sets one to inherit.
        error_service = var.app_backend_bucket_id
      }
    }
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
  # HTTP/3 (QUIC) pinned on rather than left to Google's default. It does nothing for distance, but a lost
  # packet on a lossy mobile link no longer stalls every request behind it, and a reconnecting client
  # resumes its session without a fresh handshake — the far-away (India) player path. In-place update.
  quic_override = "ENABLE"
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

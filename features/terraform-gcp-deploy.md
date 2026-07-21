# Feature — GCP deployment (Terraform + console tools)

> Status: **shipped.** Authoritative spec: `../specs/terraform-gcp-deploy.md`
> (merged via `feature/terraform-gcp-deploy` on 2026-07-17).

## What it does

Provides the production **GCP** deployment for all of SpaceStroker as Infrastructure-as-Code plus two local
**C# console tools**, all under `monkify-meta/deploy/`, driven by a single **unversioned local
`secrets.<env>.json`**. It also makes the two backend hosts **Cloud-Run-ready** (health probes, forwarded
headers, config-driven CORS) without changing any game behaviour. It is an **ops/infra** capability, not a
player-facing feature; the real `terraform apply` is an operator step (needs a registered domain + real
Solana/Fx secrets).

## Core rules

### Backend (Cloud-Run readiness — no game-behaviour change)
- **Health probes:** `SpaceStroker.Consumers` exposes `GET /health` (liveness, `Healthy`) via
  `MapHealthChecks`. `SpaceStroker.Api` already exposes `GET /health` via `HomeController` (`"Api running!"`) —
  it was **not** duplicated. Both are anonymous (no launch/admin token, not rate-limited).
- **Behind the LB:** `SpaceStroker.Api` calls `UseForwardedHeaders` (`X-Forwarded-Proto`/`For`) before rate
  limiting + `UseHttpsRedirection`, so it sees the real scheme/client behind the Cloud Run / LB TLS
  terminator (no redirect loop).
- **CORS is config-driven:** both hosts resolve allowed origins via
  `CorsOriginResolver.ResolveAllowedOrigins(config)` reading `GeneralSettings:Cors:AllowedOrigins`.
  Committed default is `https://app.spacestroker.com`; production can override via
  `GeneralSettings__Cors__AllowedOrigins__0`. In `Development` CORS still allows any origin.

### Topology (Terraform)
- **API → Cloud Run**, autoscaling, `min_instance_count ≥ 1`, public (LB-fronted, `allUsers` invoker).
- **Consumers → Cloud Run**, **internal ingress only** (hosts `/hangfire` + owns EF migrations/seeding),
  **CPU always-on** (`cpu_idle = false`), instance count pinned to `var.consumer_count` (default **1**;
  `> 1` is a documented app caveat). Released **before** the API.
- **front + lp → GCS website buckets + Cloud CDN** behind the LB. The front bucket reproduces the retired
  nginx header `Content-Security-Policy: frame-ancestors *` as a CDN response header; SPA history routing
  is served by `not_found_page = index.html`.
- **Cloud SQL for PostgreSQL 16**, one instance, two databases **`spacestroker`** + **`spacestroker_messaging`**
  (the MassTransit SQL transport DB), **private IP only**; **Memorystore Redis 7**, private only. Cloud
  Run reaches them via a **Serverless VPC Access connector**; egress via **Cloud NAT**.
- **Artifact Registry** holds the API + Consumers images.
- **External HTTPS LB → Cloud Armor** (per-IP rate limit + OWASP SQLi/XSS) → api / app / lp; **Google-managed
  TLS**; **Cloud DNS** A-records for `api.` / `app.` / apex / `www.` `spacestroker.com`.
- **Secret Manager** holds all secrets; Terraform creates the **containers only** (values synced by the
  deploy tool), and each component's **least-privilege service account** gets `secretAccessor` only on its
  secrets, plus `cloudsql.client`. No secret value ever enters Terraform state or a committed file.

### Two-layer split (teardown keeps the domain)
- **Persistent layer** — Cloud DNS zone + domain + remote-state bucket, guarded with `prevent_destroy`;
  teardown never touches it.
- **Workload layer** — everything else; `teardown` runs `terraform destroy` on this layer only (typed
  confirmation, warns about irreversible Cloud SQL data loss, overrides deletion-protection). Re-running
  `deploy` recreates the workload and re-attaches to the preserved DNS + managed certs.

### Console tools (`deploy/tools/`, standalone .NET 8, no reference to the backend solution)
- **`spacestroker-deploy`** — `deploy [api|consumers|front|lp]` (or all) + `teardown`. Reads
  `secrets.<env>.json`, syncs secrets → Secret Manager (values via stdin), emits `production.auto.tfvars.json`,
  builds/pushes images, `terraform apply`s, then builds the static sites (baking the `frontBuild` `VUE_APP_*`
  values) + syncs to GCS + invalidates the CDN. Never prints a secret value.
- **`spacestroker-db`** — read-write REPL over the game or messaging DB via the Cloud SQL Auth Proxy + IAM;
  `SELECT`/`WITH`/`SHOW`/`EXPLAIN` run immediately, `INSERT`/`UPDATE`/`DELETE`/DDL require typing `YES`.

### Front/lp production build
- `VUE_APP_*` are baked at build. The deploy tool generates the prod env (`api.spacestroker.com`,
  `mainnet-beta`, `VUE_APP_DEV_LAUNCH=false`, `VUE_APP_ALLOW_FREE_BETS=false`) from `secrets.<env>.json`;
  the committed `.env.example` documents these. `monkify-lp` (Astro static) has no repo change.

## Key code touchpoints

- **Deploy IaC + tools:** `monkify-meta/deploy/` — `terraform/persistent/`, `terraform/workload/` (root +
  `modules/{network,data,registry,iam,secrets,cloud_run_api,cloud_run_consumers,static_site,edge}`),
  `tools/SpaceStroker.Deploy/`, `tools/SpaceStroker.DbConsole/`, `secrets.example.json`, `README.md`.
- **Backend readiness:** `monkify-backend/src/SpaceStroker.Common/Cors/CorsOriginResolver.cs`,
  `SpaceStroker.Domain/Configs/Entities/CorsSettings.cs`, `SpaceStroker.Api/Program.cs` (forwarded headers + CORS),
  `SpaceStroker.Consumers/Program.cs` (`/health` + CORS + Main-operator seed URLs), `SpaceStroker.Common/spacestrokersettings.json` (`GeneralSettings:Cors`).
- **Front:** `monkify-front/.env.example` (production build documentation).
- **Not in this repo:** the real `terraform apply` (ops), domain registration, real Solana mainnet / Fx keys.

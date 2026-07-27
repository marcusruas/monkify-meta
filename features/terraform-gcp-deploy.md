# Feature — GCP deployment (Terraform + console tools)

> Status: **shipped.** Authoritative spec: `../specs/terraform-gcp-deploy.md`
> (merged via `feature/terraform-gcp-deploy` on 2026-07-17).
> Extended by `../specs/terraform-development-deploy.md` (2026-07-23): **environment scoping** so
> development and production coexist in one GCP project, and a **total development teardown**.
> Extended by `../specs/teardown-resume.md` (2026-07-27): teardown is **resumable** — targeted
> pre-destroy apply, destroy retries, leftover-aware exit codes and a `--purge` stop-guard.

## What it does

Provides the **GCP** deployment for all of SpaceStroker as Infrastructure-as-Code plus two local
**C# console tools**, all under `monkify-meta/deploy/`, driven by a single **unversioned local
`secrets.<env>.json`** per environment (`development`, `production`). It also makes the two backend hosts **Cloud-Run-ready** (health probes, forwarded
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
  nginx header `Content-Security-Policy: frame-ancestors *` as a CDN response header. **HTTP status codes
  are handled per site** (`static_site` takes a `not_found_page` variable; see
  `../specs/static-site-status-codes.md`): a GCS website config **always** returns 404 for
  `not_found_page` and cannot override that status, so the **front leaves it unset** and gets its SPA
  fallback from the **load balancer** instead — a `custom_error_response_policy` on the **`/*` path rule**
  of the `app` matcher rewrites the bucket's 404 into `/index.html` with `override_response_code = 200`,
  while `/js/* /css/* /img/* /media/*` route through a **more specific, policy-free** path rule so a
  missing hashed chunk still surfaces a real 404. The policy lives on the `/*` rule rather than the
  matcher default because a `path_rule` that omits a policy **inherits** the matcher default, which would
  apply it to the assets too. The **lp** sets `not_found_page = "404.html"` and **keeps** the 404 status —
  it is a one-pager with anchor-only nav, so there are no client-side routes to rescue.

  > ⚠️ **Committed 2026-07-27 but NOT yet applied**, and never `terraform validate`-ed (no terraform
  > binary in the authoring environment). Until the next `terraform apply` + CDN invalidation,
  > production still serves the old behaviour: every `app.spacestroker.com` deep link returns **404**
  > with the SPA shell, and `spacestroker.com/<unknown>` returns the **LP homepage** with a 404 instead
  > of the branded `404.html`. Remove this note once applied and verified.
- **Cloud SQL for PostgreSQL 16**, one instance, two databases **`spacestroker`** + **`spacestroker_messaging`**
  (the MassTransit SQL transport DB), **private IP only**; **Memorystore Redis 7**, private only. Cloud
  Run reaches them via a **Serverless VPC Access connector**; egress via **Cloud NAT**.
- **Artifact Registry** holds the API + Consumers images.
- **External HTTPS LB → Cloud Armor** (per-IP rate limit + OWASP SQLi/XSS) → api / app / lp; **Google-managed
  TLS**; **Cloud DNS** A-records for `api.` / `app.` / apex / `www.` `spacestroker.com`.
- **Secret Manager** holds all secrets; Terraform creates the **containers only** (values synced by the
  deploy tool), and each component's **least-privilege service account** gets `secretAccessor` only on its
  secrets, plus `cloudsql.client`. No secret value ever enters Terraform state or a committed file.

### Environment scoping (one GCP project, many environments)
- `environment` in `secrets.<env>.json` yields an **env code** (`development → dev`, `production → prod`,
  `staging → stg`, else the first 4 alphanumerics) and **every project-global id carries it**:
  `name_prefix = spacestroker-<envCode>` for Cloud Run, service accounts, VPC/connector/NAT, Cloud SQL,
  Memorystore, Artifact Registry, Secret Manager ids, the site buckets, LB/Armor/cert/IP and the DNS
  zone. Development and production share one GCP project, so this is what stops them colliding.
- The code is short by necessity: `spacestroker-development-consumers` (34) breaks the service-account
  cap of 30 and `spacestroker-development-vpcconn` (32) breaks the VPC-connector cap of 25.
- **One state bucket per environment** — `spacestroker-tfstate-<envCode>-<projectId>` (derived by the
  tool, **not** authored in `secrets.<env>.json`), prefixes `persistent`/`workload`, with a per-env
  `TF_DATA_DIR=.terraform-<envCode>` and `init -reconfigure`. **One data tier per environment** too
  (`spacestroker-dev-pg` + `-redis`), so a dev teardown can never reach production data.
- The tool **validates every derived id against its GCP cap before invoking Terraform**
  (`spacestroker-deploy validate` prints them all); the emitted vars file is `<environment>.tfvars.json`
  — deliberately not `*.auto.tfvars.json`, which Terraform would auto-load for every environment.

### Two-layer split — two teardown behaviours
- **Persistent layer** — Cloud DNS zone + remote-state bucket. `protect_persistent = true` (production)
  creates them with `prevent_destroy` + `force_destroy = false`, so teardown never touches them;
  `protect_persistent = false` (development) creates the *ephemeral* variants without `prevent_destroy`
  and with `force_destroy = true`. They are **separate resources with mutually exclusive counts**
  because `lifecycle.prevent_destroy` only accepts a literal.
- **Workload layer** — everything else; destroyed in both modes (typed confirmation, warns about
  irreversible Cloud SQL data loss, clears Cloud SQL deletion-protection).
- **`teardown --env production`** = workload only; re-running `deploy` re-attaches to the preserved DNS
  + managed certs.
- **`teardown --env <non-production>`** = **TOTAL**: workload → persistent (state migrated to a local
  backend first, since the bucket holds its own state) → sweep of leftover `spacestroker-<envCode>-*`
  Secret Manager secrets → removal of the locally generated files (`<env>.tfvars.json`,
  `.terraform-<envCode>/`, local state; never `secrets.<env>.json` or `*.tfvars`). Nothing
  Terraform-created survives — only the registrar (GoDaddy) domain registration. Guardrails: production
  is hard-blocked from this path, and a zone whose `dnsDomain` is the registrable apex
  (`spacestroker.com.`) is refused — only sub-zones like `dev.spacestroker.com.` may be destroyed.
- **`deploy` auto re-bootstraps** a missing persistent layer (local state → apply → migrate into the new
  bucket) and prints the new nameservers to set at GoDaddy, so a total teardown is round-trippable
  without raw Terraform. The tool never calls a registrar API; a dead NS delegation is only warned about.
  Cloud SQL reserves a deleted instance name for ~7 days — `gcp.dbInstanceSuffix` exists for redeploys
  inside that window.

### Teardown is resumable — re-running it never creates anything
- **The pre-destroy apply is targeted, and skipped when it has nothing to do.** Clearing Cloud SQL
  deletion-protection uses
  `terraform apply -target=module.data.google_sql_database_instance.postgres` — never a full apply.
  `deletion_protection` there is the **Terraform-level** guard (not
  `settings.deletion_protection_enabled`), so that apply is a **state-only** write: it creates and
  mutates nothing in GCP. It runs **only if** the instance is still in state
  (`terraform state list <addr>` probe); absent ⇒ skipped with a "resuming a partial teardown" line.
  A **full** apply here is what used to make a failed teardown un-resumable — it rebuilt everything
  the failed pass had destroyed.
- **The state probe fails closed.** A non-zero `terraform state list` aborts the run with a
  `DeployException`; a failed probe is never read as "empty state" (that would skip clearing
  deletion-protection on an intact stack, or report a teardown that never happened).
- **`destroy` retries itself** — up to **3 passes**, sleeping **60s** then **120s**, retrying on
  **any** non-zero exit (no stderr matching: `destroy` is idempotent, `ProcessRunner` doesn't
  redirect stderr, and GCP's wording drifts). The three known ordering failures — the messaging DB
  still holding connections, service-networking producers still attached, the subnet still held by
  the serverless egress address — each clear once the blocking resource is really gone.
- **What survives in state decides the exit code**, not Terraform's last exit code (a `destroy` can
  exit non-zero having removed everything that matters):
  - state empty ⇒ the normal completion messages, **exit 0**;
  - every leftover under **`module.network.`** (VPC + subnet + PSA range — all zero-cost, held by the
    Google-managed `serverless.googleapis.com/.../addressReservations/serverless-ipv4-*` reservation
    that releases **asynchronously** and cannot be forced) ⇒ **exit 0** plus a loud warning naming
    each address, stating all billable resources are gone, and giving the resume command;
  - anything else left (Cloud SQL, Redis, Cloud Run, a bucket, the registry, …) ⇒ `DeployException`
    listing the addresses ⇒ **exit 1**.
- **`--purge` stops rather than orphan.** If **any** workload state remains after the workload
  destroy — zero-cost leftovers included — the run stops **before** `DestroyPersistent` /
  `SweepOrphanSecrets` / `CleanLocalArtifacts` and exits non-zero, leaving the DNS zone, the state
  bucket and the secrets untouched. The state bucket is the only record of those resources;
  destroying it would put them beyond Terraform's reach.
- **Re-running the same `teardown` command is the supported resume path**, and every give-up path
  prints it (plus a raw `terraform destroy` escape hatch). Known unchanged limitation: after a
  `--purge` that already destroyed the state bucket, `teardown` still fails at `terraform init` —
  `EnsurePersistentLayer()` runs on `deploy`, not `teardown`.

### Console tools (`deploy/tools/`, standalone .NET 8, no reference to the backend solution)
- **`spacestroker-deploy`** — `deploy [api|consumers|front|lp]` (or all) + `teardown` + `validate`. Reads
  `secrets.<env>.json`, validates every derived id, ensures/bootstraps the persistent layer, syncs secrets
  → Secret Manager (values via a temp file, ids env-prefixed), emits `<environment>.tfvars.json`,
  builds/pushes images, `terraform apply`s, then builds the static sites (baking the `frontBuild` `VUE_APP_*`
  values) + syncs to GCS + invalidates the CDN. Never prints a secret value. `EnvNaming` is the single
  source of truth for names; `ProcessRunner` can capture stdout and set `TF_DATA_DIR` per invocation.
- **`spacestroker-db`** — read-write REPL over the game or messaging DB via the Cloud SQL Auth Proxy + IAM;
  `SELECT`/`WITH`/`SHOW`/`EXPLAIN` run immediately, `INSERT`/`UPDATE`/`DELETE`/DDL require typing `YES`.

### Front/lp production build
- `VUE_APP_*` are baked at build. The deploy tool generates the prod env (`api.spacestroker.com`,
  `mainnet-beta`, `VUE_APP_DEV_LAUNCH=false`, `VUE_APP_ALLOW_FREE_BETS=false`) from `secrets.<env>.json`;
  the committed `.env.example` documents these. `monkify-lp` (Astro static) has no repo change.

## Key code touchpoints

- **Deploy IaC + tools:** `monkify-meta/deploy/` — `terraform/persistent/`, `terraform/workload/` (root +
  `modules/{network,data,registry,iam,secrets,cloud_run_api,cloud_run_consumers,static_site,edge}`),
  `tools/SpaceStroker.Deploy/` (`EnvNaming.cs`, `Deployer.cs`, `ProcessRunner.cs`, `SecretsFile.cs`,
  `TeardownDecisions.cs`, `Program.cs`), `tools/SpaceStroker.Deploy.Tests/`,
  `tools/SpaceStroker.DbConsole/`, `secrets.example.json`, `.gitignore`, `README.md`.
- **Teardown decisions:** `deploy/tools/SpaceStroker.Deploy/TeardownDecisions.cs` — pure, I/O-free
  (`SqlInstanceAddress`, `ZeroCostPrefix`, `DestroyBackoff`/`DestroyAttempts`, `ParseStateList`,
  `Contains`, `IsZeroCostOnly`, and the resume-hint / zero-cost / billable-leftover / purge-blocked
  message builders), wired through `Deployer.DestroyWorkload(bool purge)` + its `RunDestroyWithRetries`
  and fail-closed `ReadState` helpers. Covered by `deploy/tools/SpaceStroker.Deploy.Tests`
  (xUnit + Shouldly, 16 tests, registered in `SpaceStroker.DeployTools.sln`) — the only automated
  tests in the deploy stack; `ProcessRunner` and the real `terraform`/`gcloud` calls stay untested.
- **Backend readiness:** `monkify-backend/src/SpaceStroker.Common/Cors/CorsOriginResolver.cs`,
  `SpaceStroker.Domain/Configs/Entities/CorsSettings.cs`, `SpaceStroker.Api/Program.cs` (forwarded headers + CORS),
  `SpaceStroker.Consumers/Program.cs` (`/health` + CORS + Main-operator seed URLs), `SpaceStroker.Common/spacestrokersettings.json` (`GeneralSettings:Cors`).
- **Front:** `monkify-front/.env.example` (production build documentation).
- **Not in this repo:** the real `terraform apply` (ops), domain registration, real Solana mainnet / Fx keys.

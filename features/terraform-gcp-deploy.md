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
- **front + lp → GCS website buckets + Cloud CDN** behind the LB. The front bucket carries **two** CDN
  response headers: `Content-Security-Policy: frame-ancestors *` (reproducing the retired nginx header so
  regulated operators can still embed) and **`X-Robots-Tag: noindex`**. The lp bucket deliberately carries
  **neither** — the landing page is the property that must stay indexable. The `noindex` header is the half
  of the crawler policy that actually **de-indexes** `app.spacestroker.com`: `robots.txt` only stops
  crawling, and blocking the crawl also stops Google ever reading a `noindex` *meta* tag, so a URL it
  already knows (the LP's "PLAY NOW" links straight there) could otherwise persist as a bare titleless
  link. It applies to every object from that bucket, `verify.html` and `embed-example.html` included.
  See `../specs/robots-txt.md`. **HTTP status codes
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
- **External HTTPS LB → Cloud Armor** (per-IP **throttle** + OWASP SQLi/XSS) → api / app / lp; **Google-managed
  TLS**; **Cloud DNS** A-records for `api.` / `app.` / apex / `www.` `spacestroker.com`.
  > **The per-IP rule throttles; it does not ban** (`modules/edge/main.tf`, priority 1000:
  > `action = "throttle"`, `enforce_on_key = "IP"`, **600 / 60 s**, **no `ban_duration_sec`**). It was
  > `rate_based_ban` at 100/60 s with a **300 s ban** until 2026-07-27, and that combination locked real
  > players out (Trello #104): the policy is attached to the **api backend service only** — the backend
  > *buckets* have none — so a banned player kept getting **200s for every static asset** while **every**
  > API call 429'd. The app rendered and looked broken rather than rate-limited, so they reloaded, and
  > each reload **re-armed** the 300 s ban. A few seconds of excess became a multi-minute outage that
  > outlived its own cause. Throttling denies only the requests *above* the rate, so the next interval is
  > served normally. 100/60 s was also simply too low: the key is **IP**, so a VPN exit or corporate NAT
  > is one bucket, and two ordinary players sharing one tripped it with no misbehaviour at all.
  > **Do not restore the ban or lower the count without re-reading that card** — and note Cloud Armor
  > enforces the rate across the **whole interval**, so interleaved 200s and 429s from one IP during a
  > burst are expected, not a partial outage.
  >
  > **Infrastructure paths have their own looser bucket** (priority **800**, added 2026-07-27 by Trello
  > #110): `request.path.startsWith('/Hubs/') || request.path == '/health'` throttles at
  > **3000 / 60 s** per IP (`infra_rate_limit_count`). Deliberately a *looser throttle*, **not** an
  > `allow` exemption — an exemption would leave `POST /Hubs/Games/negotiate` with **no ceiling
  > anywhere**, and the in-app limiter that would normally backstop it is under suspicion of bucketing
  > every player together (#112). It matters because a throttled health probe makes Cloud Run recycle
  > the instance, and a mass SignalR reconnect behind one NAT would blank realtime for everyone on that
  > exit. ⚠️ `/health`'s share of this is **hygiene, not an incident fix** — Cloud Run's own probes are
  > *internal* (`modules/cloud_run_api/main.tf:66,76`) and never traverse the LB, so the only thing ever
  > throttled there was an unidentified third-party poller. Note the rule matches on **`expr`**, not
  > `versioned_expr` — path and source-IP matching are mutually exclusive in one `match` block.
  >
  > ⚠️ **A rule's `action` cannot be changed in place.** The provider patches rules via `patchRule`,
  > which **merges** rather than replaces, so fields from the old action survive and collide — #104's
  > `rate_based_ban → throttle` change failed on a leftover `banDurationSec`. Delete the live rule
  > first, or bump its priority for an atomic remove+add. Adding a **new** rule at a **new** priority
  > (as #110 did) is unaffected. This is now recorded in `modules/edge/main.tf` itself.
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

### Static-site upload: Cache-Control tiers, asset retention, 24h prune
- **Three tiers, both sites**, decided per file by `StaticSiteCache.ForPath`: **HTML at any depth** →
  `no-cache`; **content-hashed assets** (front `js/ css/ img/ media/`, lp `_astro/`) →
  `public, max-age=31536000, immutable`; **everything else unhashed** (`fonts/`, favicons,
  `og-image.png`, `robots.txt`, `sitemap*.xml`) → an explicit `public, max-age=3600`. The `.html` test
  runs **first and at any depth** — Astro emits directory-style pages, so the lp's `/cookies` builds to
  `cookies/index.html`, which a first-path-segment rule would file as 1h. An unknown file **or
  directory** falls to 1h: unknown never means immutable.
- **Each tier is set twice** — at upload (`gsutil -h`) and again by a `setmeta` pass. Both are needed:
  `rsync` compares size/mtime/checksum, **not metadata**, so an object it skips as unchanged keeps its
  old header forever. The `setmeta` passes are what repair objects already in the buckets.
- **Ordering is load-bearing.** Assets upload **before** the HTML that names them, and every
  upload-time header is at least as conservative as that path's final value (the root pass uploads at
  `no-cache` even for files that end at 1h), so a window between passes can **under-cache but never
  over-cache**. A directory containing HTML is skipped by the recursive 1h pass for the same reason.
- **Hashed assets are retained, not deleted.** The asset passes drop `-d`, so a session still running
  the previous build can fetch a lazily-loaded chunk — `GameFairPanel.vue`'s provably-fair verify Web
  Worker is the live case. The root pass keeps `-d`; its single `-x` alternation (gsutil honours only
  one) shields the asset prefixes **and** `_builds/`, and drops `*.map` so sourcemaps are never uploaded.
- **Retained assets are pruned automatically after 24h.** Each deploy writes
  `gs://<bucket>/_builds/<utc-timestamp>.txt` listing what it published; assets no manifest inside the
  window still names are deleted, along with expired manifests. **Retirement time, not object age, is
  what is measured** — an image unchanged for eight months has an ancient creation date, so an
  age-based GCS lifecycle rule would delete it the instant it left a build. That is why manifests exist
  and why no lifecycle rule was added. The window comes from the **open-tab** horizon, not from
  `Cache-Control` (HTML revalidates every load): `useIdleDisconnect` abandons idle sessions after
  30-60 min, so 24h is ~20× that. One constant, `BuildManifest.Retention`.
- **The prune fails closed.** A non-zero manifest listing, manifest read, or asset listing prints why
  and deletes nothing; an empty keep-set prunes nothing rather than everything. Deleting the live build
  because a listing failed would be worse than the bug being fixed. Commands are batched under the
  Windows 8191-character cap.
- The `/*` CDN invalidation after upload is unchanged and still best-effort — it is what flushes edge
  copies cached under the *old* headers on the first deploy that carries this.

## Key code touchpoints

- **Deploy IaC + tools:** `monkify-meta/deploy/` — `terraform/persistent/`, `terraform/workload/` (root +
  `modules/{network,data,registry,iam,secrets,cloud_run_api,cloud_run_consumers,static_site,edge}`),
  `tools/SpaceStroker.Deploy/` (`EnvNaming.cs`, `Deployer.cs`, `ProcessRunner.cs`, `SecretsFile.cs`,
  `TeardownDecisions.cs`, `StaticSiteCache.cs`, `BuildManifest.cs`, `Program.cs`),
  `tools/SpaceStroker.Deploy.Tests/`,
  `tools/SpaceStroker.DbConsole/`, `secrets.example.json`, `.gitignore`, `README.md`.
- **Teardown decisions:** `deploy/tools/SpaceStroker.Deploy/TeardownDecisions.cs` — pure, I/O-free
  (`SqlInstanceAddress`, `ZeroCostPrefix`, `DestroyBackoff`/`DestroyAttempts`, `ParseStateList`,
  `Contains`, `IsZeroCostOnly`, and the resume-hint / zero-cost / billable-leftover / purge-blocked
  message builders), wired through `Deployer.DestroyWorkload(bool purge)` + its `RunDestroyWithRetries`
  and fail-closed `ReadState` helpers.
- **Static-site upload decisions:** `deploy/tools/SpaceStroker.Deploy/StaticSiteCache.cs` — pure
  (`StaticSite`, `UploadStep`/`UploadStepKind`, the three `*CacheControl` constants, `SourcemapPattern`,
  `HashedPrefixes`, `IsExcluded`, `ForPath`, `RootExclusionRegex`, `Plan`) — plus
  `BuildManifest.cs` for the prune (`Retention`, `NameFor`/`TimestampOf`,
  `RetainedManifests`/`ExpiredManifests`, `PruneTargets`, `ParseListing`,
  `Render`/`ParseManifestBodies`, `Batch`). Wired through `Deployer.UploadAndInvalidate(StaticSite, …)`,
  which reads the dist listing and executes each planned `gsutil` call, and its private
  `PruneRetainedAssets`.
- **Deploy-tool tests:** `deploy/tools/SpaceStroker.Deploy.Tests` (xUnit + Shouldly, **76 tests**,
  registered in `SpaceStroker.DeployTools.sln`) — `TeardownDecisionsTests` (16),
  `StaticSiteCacheTests` (43), `BuildManifestTests` (18) — the only automated tests in the deploy
  stack; `ProcessRunner` and the real `terraform`/`gcloud`/`gsutil` calls stay untested, so the upload
  and prune argument shapes are first exercised for real on the next deploy.
- **Backend readiness:** `monkify-backend/src/SpaceStroker.Common/Cors/CorsOriginResolver.cs`,
  `SpaceStroker.Domain/Configs/Entities/CorsSettings.cs`, `SpaceStroker.Api/Program.cs` (forwarded headers + CORS),
  `SpaceStroker.Consumers/Program.cs` (`/health` + CORS + Main-operator seed URLs), `SpaceStroker.Common/spacestrokersettings.json` (`GeneralSettings:Cors`).
- **Front:** `monkify-front/.env.example` (production build documentation).
- **Not in this repo:** the real `terraform apply` (ops), domain registration, real Solana mainnet / Fx keys.

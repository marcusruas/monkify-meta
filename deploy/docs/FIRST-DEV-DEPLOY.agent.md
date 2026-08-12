# First-time DEV deploy — agent runbook (precise)

Greenfield (`development`) deploy of SpaceStroker to GCP using `deploy/`. Target reader: an AI agent
executing on Windows, PowerShell primary + Bash available, workspace root `E:\Monkify`.

## Why this is not a single `deploy` command (root cause)

`Deployer.DeployAll()` order is: `PreFlight → SyncSecrets → EmitTfvars → TerraformApply(workload) →
DeployComponent(consumers, api, front, lp)`. Two greenfield invariants are violated by that order:

1. **`TerraformApply` creates `module.cloud_run_{api,consumers}` referencing
   `<region>-docker.pkg.dev/<proj>/spacestroker-dev/{api,consumers}:latest`, but images are only
   built/pushed later in `DeployComponent`.** Cloud Run v2 create fails on a missing image.
2. **Connection-string secrets are synced verbatim** (`SyncSecrets` → Secret Manager) and bound into
   Cloud Run as `ConnectionStrings__*`. In `secrets.development.json` the `Host=` is `REPLACE_ME`
   (passwords are already set by the user). The Cloud SQL private IP / Redis host are only known
   **after** `module.data` applies. A Cloud Run revision with a bad DB host never becomes Ready.

The tool never rewrites the connection host and never pushes before applying, so first-run is a
3-phase sequence: **partial apply to build data+registry → fix hosts + push images → full apply.**
`terraform apply` creates modules in dependency order (`network, registry, data, secrets, static
sites` before `cloud_run_*`), so Run 1's apply DOES create Cloud SQL + Redis + registry before it
fails at `cloud_run`. That partial success is Phase 1's deliverable.

## Facts baked in from the code (do not re-derive)

- Env code: `development → dev`; `name_prefix = spacestroker-dev`. (`EnvNaming`)
- SQL instance: `spacestroker-dev-pg`; Redis: `spacestroker-dev-redis`; registry repo:
  `spacestroker-dev`; state bucket (derived, do NOT author): `spacestroker-tfstate-dev-<projectId>`.
- DB user (terraform `db_app_user` default): `spacestroker_app`; password = `Password=` extracted
  from `ConnectionStrings:SpaceStroker`. **User has set real passwords already** — do not change them,
  and keep `:SpaceStroker` and `:Messaging` passwords identical (same DB user).
- **Memorystore AUTH is disabled** (`data/main.tf` `google_redis_instance` has no `auth_enabled`) →
  Redis connection string must be `<host>:6379` with **no** `,password=` segment.
- Cloud Run forces env via `plain_env = merge({ASPNETCORE_ENVIRONMENT="Production"}, var.plain_env)`;
  terraform `merge` right-wins, so `appConfig.ASPNETCORE_ENVIRONMENT="Development"` in the secrets
  file **overrides to Development** in the deployed services. Intended for dev; confirm with user.
- Secret Manager binding uses `version = "latest"`; a new `gcloud run services update`/apply revision
  is required to pick up a re-synced secret. Re-running `deploy` (Run 2) does this.
- TF working dir is per-env: always `TF_DATA_DIR=.terraform-dev`. Vars file (never `*.auto.`):
  `development.tfvars.json`, passed with `-var-file`.
- DB console instance-name bug is **fixed**: `SpaceStroker.DbConsole` now derives
  `spacestroker-<envCode>-pg[-<suffix>]` from `--env` + `gcp.dbInstanceSuffix`. It lives in
  `monkify-meta/tools/SpaceStroker.DbConsole` (moved out of `deploy/tools/` in card #167) and still
  reads `deploy/secrets.<env>.json`. Use `dotnet run -- --db game --env development`.

## Preconditions to verify before executing

- `gcloud terraform docker yarn npm dotnet` on PATH; docker daemon running.
- `gcp.projectId` in `secrets.development.json` is real, billing-enabled, ≤30 chars, exact
  (`gcloud projects list`). The value ends `-899` and may be truncated — confirm with user.
- Sibling repos present: `monkify-backend`, `monkify-front`, `monkify-lp` (all confirmed present).
- `deploy/terraform/persistent/development.tfvars` exists (it does) and its `project_id`,
  `dns_domain`, `state_bucket_name` match the secrets file.
- DNS: `spacestroker.com` is registered at GoDaddy; NS delegation for `dev.spacestroker.com` is done
  **last** (Phase 6), after the stack is up. Managed certs stay PROVISIONING until then — not an error.

## Secrets file remediation

Edit `deploy/secrets.development.json`:
- `ConnectionStrings:SpaceStroker` / `:Messaging`: passwords already set — leave them; set `Host=` in
  Phase 2 (SQL private IP), keep `Database=spacestroker` / `spacestroker_messaging` respectively.
- `ConnectionStrings:Redis`: in Phase 2 set to `<redisHost>:6379` (drop `,password=`).
- Confirm `ASPNETCORE_ENVIRONMENT=Development` is intended (dev endpoints/free-bets/Swagger ON).

## Execution

All `dotnet run` from `monkify-meta/deploy/SpaceStroker.Deploy`. `<PROJECT>` = real project id.

### Phase 0 — auth + APIs
```bash
gcloud auth login
gcloud auth application-default login
gcloud config set project <PROJECT>
gcloud auth configure-docker us-central1-docker.pkg.dev
gcloud services enable run.googleapis.com sqladmin.googleapis.com redis.googleapis.com \
  compute.googleapis.com servicenetworking.googleapis.com vpcaccess.googleapis.com \
  secretmanager.googleapis.com artifactregistry.googleapis.com dns.googleapis.com \
  cloudresourcemanager.googleapis.com \
  monitoring.googleapis.com billingbudgets.googleapis.com --project <PROJECT>
```

The last two serve the **production** monitoring stack. Development creates none of those resources
(`enable_dashboards` is false outside production), but enabling the APIs now costs nothing and
avoids a failed apply on the first production deploy.

### Phase 1 — validate, then partial apply (data + registry)
```bash
dotnet run -- validate --env development     # expect: all PASS; abort on any FAIL
dotnet run -- deploy   --env development      # EXPECTED to fail at the cloud_run step
```
Run 1 side effects (all wanted): persistent layer bootstrapped (state bucket + DNS zone; **capture
the printed nameservers for Phase 6**), secrets synced to Secret Manager, `development.tfvars.json`
emitted, Terraform created network / Cloud SQL (`spacestroker-dev-pg`) / Redis / Artifact Registry /
buckets. The terminating error at `cloud_run_*` (image not found) is the expected end of Phase 1.

Guard: if apply fails BEFORE `module.data` (e.g. an API not enabled, service-networking peering),
fix that cause and re-run — do not proceed until `spacestroker-dev-pg` exists.

### Phase 2 — read hosts, correct secrets
```bash
gcloud sql instances describe spacestroker-dev-pg --project <PROJECT> \
  --format="value(ipAddresses[0].ipAddress)"          # -> DB private IP
gcloud redis instances describe spacestroker-dev-redis --region us-central1 --project <PROJECT> \
  --format="value(host)"                               # -> Redis host
```
Edit `secrets.development.json`: put the SQL IP into both connection strings' `Host=`; set
`ConnectionStrings:Redis` = `<redisHost>:6379`.

### Phase 3 — build + push both images (registry now exists)
```bash
cd ../../../../monkify-backend/src   # -> E:\Monkify\monkify-backend\src
docker build -t us-central1-docker.pkg.dev/<PROJECT>/spacestroker-dev/consumers:latest -f SpaceStroker.Consumers/Dockerfile .
docker push  us-central1-docker.pkg.dev/<PROJECT>/spacestroker-dev/consumers:latest
docker build -t us-central1-docker.pkg.dev/<PROJECT>/spacestroker-dev/api:latest      -f SpaceStroker.Api/Dockerfile .
docker push  us-central1-docker.pkg.dev/<PROJECT>/spacestroker-dev/api:latest
```

### Phase 4 — Run 2: full deploy to completion
```bash
cd ../../monkify-meta/deploy/SpaceStroker.Deploy
dotnet run -- deploy --env development
```
Now: corrected secrets re-synced (new versions), `terraform apply` creates `cloud_run_*` (images
present, DB reachable) → Ready; `DeployComponent` rebuilds/pushes (idempotent) and updates the Cloud
Run revisions, then builds + `gsutil rsync`es front (yarn) and lp (npm) to their buckets and
invalidates CDN. Runs to `Deploy complete.` Consumers boots first and owns EF migrate + config-driven
seed — allow ~1 min before the API is fully functional.

### Phase 5 — verify (no DNS yet)
```bash
cd ../../terraform/workload
TF_DATA_DIR=.terraform-dev terraform output      # api_uri, lb_ip, redis_host, buckets
```
- `curl <api_uri>/health` → `Api running!` (API health via `HomeController`). This proves the stack.
- Consumers health is internal-ingress `/health` (not public); check
  `gcloud run services describe spacestroker-dev-consumers --region us-central1 --project <PROJECT>`.

### Phase 6 — delegate DNS at GoDaddy (last)
- In GoDaddy DNS for `spacestroker.com`, add the Run-1 nameservers as **NS records on host `dev`**
  (delegates `dev.spacestroker.com` to Cloud DNS).
- Wait for the managed cert to reach ACTIVE:
  `gcloud compute ssl-certificates list --project <PROJECT>`.
- Then `https://app.dev.spacestroker.com` serves the app.

## Teardown (destructive)
```bash
dotnet run -- teardown --env development             # workload only; DNS zone + state bucket KEPT
dotnet run -- teardown --env development --purge      # full wipe: ALSO DNS zone + state bucket + secrets
```
Default `teardown` (every environment) destroys the WORKLOAD only — Cloud SQL (+ data), Redis, Cloud
Run, LB, registry — and PRESERVES the persistent layer (DNS zone + state bucket), so the GoDaddy
delegation stays valid and a later `deploy` re-attaches. Type "development" to confirm. Redeploy
within ~7 days ⇒ bump `gcp.dbInstanceSuffix` (e.g. `"2"`) — Cloud SQL name reservation.

`--purge` (non-production only; blocked for production) additionally destroys the DNS zone + state
bucket, sweeps env secrets, and cleans local artifacts — only the GoDaddy domain registration
survives; then remove the dead `dev` NS records at GoDaddy (a later `deploy` recreates the zone with
DIFFERENT nameservers).

**Teardown is resumable — do NOT reach for raw Terraform.** Re-run
`dotnet run -- teardown --env development` verbatim; it never re-creates resources (the pre-destroy
apply is `-target`ed at the SQL instance and skipped when that instance is out of state), and `destroy`
already retries 3× (60s/120s) on the transient ordering failures. Read the exit code: `0` = done — a
`module.network.*`-only warning still counts as done, those are free and release asynchronously;
non-zero = the message names exactly what survived and why. Never destroy the state bucket while
workload state is non-empty; `--purge` already refuses to.

## Failure/branch reference
- `deploy` throws non-zero → tool aborts; state is preserved; safe to re-run after fixing cause.
- Cloud Run revision unhealthy at Phase 4 → almost always a bad DB host, or the `:SpaceStroker` and
  `:Messaging` connection strings carrying DIFFERENT passwords (both DBs share the one `spacestroker_app`
  user, so the passwords MUST match — a mismatch yields `28P01 password authentication failed` and the
  consumers container aborts the startup probe). Cloud Run mounts secrets at `version=latest`, so after
  fixing a secret, delete the failed service + `terraform state rm` it and re-deploy (SyncSecrets pushes
  the new version before the fresh create). Check Cloud Run logs before re-applying.
- `terraform output` empty after a FAILED apply → query resources directly
  (`terraform state show module.data.google_sql_database_instance.postgres`) or via `gcloud describe`.
- Do not author `gcp.stateBucket` (SecretsFile rejects it) and do not rename the DNS zone away from
  `spacestroker-dev-zone` without also editing `persistent/development.tfvars`.

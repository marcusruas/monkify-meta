# SpaceStroker — GCP deploy (`deploy/`)

Infrastructure-as-Code + two local console tools that stand up (and tear down) the production
SpaceStroker stack on GCP. **Manual deploys only** — no CI. Everything is driven by one **unversioned local
secrets file** (`secrets.<env>.json`), never committed.

See `monkify-meta/specs/terraform-gcp-deploy.md` for the full design and the acceptance criteria.

## Layout

```
deploy/
  secrets.example.json         # template — copy to secrets.<env>.json (git-ignored) and fill in
  terraform/
    persistent/                # LAYER 1 — DNS zone + state bucket (production: prevent_destroy; dev: destroyable)
    workload/                  # LAYER 2 — everything teardown may destroy
      modules/{network,data,registry,iam,secrets,cloud_run_api,cloud_run_consumers,static_site,edge}
  tools/
    SpaceStroker.Deploy/           # `spacestroker-deploy deploy [api|consumers|front|lp]` + `teardown` + `validate`
    SpaceStroker.DbConsole/        # `spacestroker-db --db game|messaging` (read-write REPL, write-confirm)
    SpaceStroker.DeployTools.sln
```

## Prerequisites (on PATH, authenticated)

- **gcloud** (`gcloud auth login` + `gcloud auth application-default login`) and **gsutil**
- **terraform** ≥ 1.6
- **docker** (build/push the API + Consumers images)
- **yarn** (front build) and **npm** (lp build)
- **cloud-sql-proxy** v2 (for the DB console)
- **.NET 8 SDK** (to build/run the console tools)

Out-of-band prerequisites: register **spacestroker.com** (then delegate its nameservers to Cloud DNS —
see bootstrap step 3); real **Solana mainnet** token + keys; a **FreeCurrencyAPI** production key.

## Environments (one GCP project, many environments)

`environment` in `secrets.<env>.json` is **load-bearing**. It yields an **env code** —
`development → dev`, `production → prod`, `staging → stg`, otherwise the first 4 alphanumerics — and
**every project-global GCP id carries it**, because development and production share one GCP project
and would otherwise collide on both names *and* Terraform state:

| Thing | Development | Production |
| --- | --- | --- |
| `name_prefix` | `spacestroker-dev` | `spacestroker-prod` |
| Cloud Run / service accounts | `spacestroker-dev-api`, `-consumers` | `spacestroker-prod-api`, `-consumers` |
| Cloud SQL / Memorystore | `spacestroker-dev-pg`, `-redis` | `spacestroker-prod-pg`, `-redis` |
| Artifact Registry repo | `spacestroker-dev` | `spacestroker-prod` |
| Secret Manager ids | `spacestroker-dev-ConnectionStrings-Redis`, … | `spacestroker-prod-…` |
| Site buckets | `spacestroker-dev-front-<projectId>`, `-lp-` | `spacestroker-prod-front-…` |
| **State bucket** | `spacestroker-tfstate-dev-<projectId>` | `spacestroker-tfstate-prod-<projectId>` |
| Terraform working dir | `TF_DATA_DIR=.terraform-dev` | `.terraform-prod` |
| Emitted vars file | `development.tfvars.json` | `production.tfvars.json` |
| `protect_persistent` | `false` (teardown destroys the zone + bucket) | `true` (teardown keeps them) |

Notes:

- The **state bucket is derived, not authored** — remove `gcp.stateBucket` from `secrets.<env>.json`
  (the tool errors if it is present). One bucket per environment is mandatory: a development teardown
  deletes its own bucket, and it must never be the one holding production state.
- The env code is deliberately **short**. `spacestroker-development-consumers` is 34 characters and
  service-account ids cap at **30**; `spacestroker-development-vpcconn` is 32 and Serverless VPC
  connector names cap at **25**. `spacestroker-dev-vpcconn` is 24 — one character of headroom.
- The emitted vars file is **not** `*.auto.tfvars.json`: Terraform auto-loads every such file in a
  directory, so two environments' files would both load and one would silently win. It is always
  passed explicitly with `-var-file`.

Check every derived id (and its GCP cap) before deploying anything:

```bash
cd deploy/tools/SpaceStroker.Deploy
dotnet run -- validate --env development
```

The same validation runs automatically before every `deploy` and `teardown`, so an over-long id fails
fast instead of half-applying a stack.

## One-time bootstrap (state bucket + persistent layer)

**Normally you don't do this by hand.** `spacestroker-deploy deploy --env <env>` detects a missing
state bucket, applies the persistent layer with local state, migrates the state into the new bucket
and prints the nameservers to set at the registrar. That is also what makes a **total teardown**
(below) round-trippable.

The manual sequence, if you want it:

```bash
cd deploy/terraform/persistent
cp production.tfvars.example development.tfvars   # project / domain / env-scoped zone + bucket,
                                                  # and protect_persistent = false for non-production
export TF_DATA_DIR=.terraform-dev                 # keep each environment's working dir separate

# 1. Create the state bucket + DNS zone with local state:
terraform init -reconfigure -backend=false
terraform apply -var-file=development.tfvars

# 2. Migrate the persistent state into the bucket it just created:
terraform init -migrate-state -force-copy \
  -backend-config="bucket=$(terraform output -raw state_bucket)" \
  -backend-config="prefix=persistent"

# 3. Delegate the zone: set these nameservers at GoDaddy (managed TLS won't validate until you do):
terraform output dns_name_servers
```

## Deploy

```bash
cd deploy/tools/SpaceStroker.Deploy
cp ../../secrets.example.json ../../secrets.production.json   # fill in real values (never commit)

dotnet run -- deploy                 # full workload: secrets → Secret Manager, tfvars, apply,
                                     # then release consumers → api → front → lp
dotnet run -- deploy api             # one component only (also: consumers | front | lp)
```

The tool: syncs each `secrets` entry to **Secret Manager** (values via stdin, never on the command line
or in Terraform state), writes the non-secret Terraform variables to `production.tfvars.json`
(git-ignored), builds/pushes the images to Artifact Registry, `terraform apply`s the workload, then
builds each static site (baking the `frontBuild` `VUE_APP_*` values) and syncs it to its GCS bucket +
invalidates the CDN. **Consumers is released before the API** (it owns EF migrations/seeding).

### Static-site caching

Each static site is uploaded in three tiers, so the entry point is always fresh and the hashed assets
are cached properly:

| What | `Cache-Control` |
| --- | --- |
| HTML at any depth (`index.html`, `404.html`, `verify.html`, `embed-example.html`, `cookies/index.html`) | `no-cache` |
| Content-hashed assets (front `js/ css/ img/ media/`, lp `_astro/`) | `public, max-age=31536000, immutable` |
| Everything else unhashed (`fonts/`, favicons, `og-image.png`, `robots.txt`, `sitemap*.xml`) | `public, max-age=3600` |

Each tier is set **twice**: once at upload (`gsutil -h`) and once by a `setmeta` pass. Both are
needed — `rsync` compares checksums, not metadata, so an object it skips as unchanged would otherwise
keep its old header forever. Sourcemaps (`*.map`) are not uploaded at all. The tiers live in
`StaticSiteCache.cs` and are covered by `SpaceStroker.Deploy.Tests`.

**Assets are retained, not deleted.** The asset passes deliberately drop `rsync -d`, so a session
still running the previous build can fetch a lazily-loaded chunk (e.g. the provably-fair verify Web
Worker) after a deploy. The root pass still uses `-d`, so top-level files the build dropped are pruned.

**Retained assets are pruned automatically, after 24 hours.** Each deploy writes a manifest of what it
published to `gs://<bucket>/_builds/<utc-timestamp>.txt`, then deletes any asset that no manifest from
the last **24 hours** still names, along with the expired manifests themselves.

The window is measured from when an asset was **retired**, not from when it was uploaded — an image
that sat unchanged for eight months has an ancient creation date, so an age-based GCS lifecycle rule
would have deleted it the instant it dropped out of a build. That is why the manifests exist.

24 hours comes from how long an **already-open tab** can still ask for an old chunk. It is not a
Cache-Control question: HTML is `no-cache`, so it revalidates on every load and can never name stale
chunks. The app already abandons idle sessions after 30-60 minutes (`useIdleDisconnect`), so 24h is
roughly 20× that ceiling and still covers a laptop closed overnight. Change it in one place:
`BuildManifest.Retention`.

The prune **fails closed** at every step — if the manifest listing cannot be read, a manifest cannot be
fetched, or an asset listing fails, it prints why and deletes nothing. `_builds/` is shielded from the
root pass's `-d`, or the manifests would be deleted by the very deploy that wrote them.

## Teardown — workload-only by default, `--purge` for a full wipe

```bash
cd deploy/tools/SpaceStroker.Deploy
dotnet run -- teardown --env development            # workload only — keeps the DNS zone + state bucket
dotnet run -- teardown --env development --purge     # full wipe — ALSO destroys the DNS zone + state bucket
```

Every teardown re-types the environment name to confirm. It then clears Cloud SQL deletion-protection
with a **targeted** `terraform apply` on the SQL instance alone — never a full-stack apply, and skipped
entirely when that instance is already gone. `destroy` runs up to **3 passes** (waiting 60s, then 120s)
because several GCP deletions only become possible once the resource blocking them is really gone.
This is what makes a teardown **resumable** — see "Resuming a failed teardown" below.

**Default (no `--purge`, every environment) — "drop the workload, keep the domain."** Destroys the
**workload layer only** (Cloud Run, Cloud SQL + **all its data**, Memorystore, LB, Cloud Armor,
VPC/NAT, Artifact Registry, buckets/CDN). The **persistent layer** (Cloud DNS zone + state bucket) is
preserved — it costs ~$0.20/month — so re-running `deploy` recreates the workload and re-attaches to
the same domains + managed certs, and the GoDaddy delegation stays valid. This is the cheap way to park
an environment between test sessions.

**`--purge` (non-production only) — TOTAL wipe.** After it finishes, **nothing Terraform created for
that environment remains in the project**; the only surviving asset is the **GoDaddy registration of
spacestroker.com**. In order:

1. **Workload layer** destroyed (as above). If **anything** is still in the workload state afterwards —
   even the zero-cost network leftovers — the run **stops here with a non-zero exit** and the persistent
   layer is left completely alone. That is deliberate: the state bucket is the only record of those
   resources, so destroying it would orphan them beyond Terraform's reach. Re-run the same `--purge`
   command once they release.
2. **Persistent layer** destroyed — the Cloud DNS zone *and* the state bucket. The bucket holds the
   very state that describes it, so the tool first migrates that state to a **local** backend
   (`init -migrate-state -force-copy -backend=false`), then destroys both with Terraform (the
   non-production bucket/zone are created with `force_destroy = true` and without `prevent_destroy`).
3. **Orphan Secret Manager secrets** swept — anything left matching `spacestroker-<envCode>-*`, plus
   every id derived from a key in `secrets.<env>.json` (the tool `gcloud secrets create`s each key, so
   a key that drifted out of Terraform's list would otherwise survive). Ids are printed; values never.
4. **Locally generated files** removed — `<environment>.tfvars.json`, both layers' `.terraform-<envCode>/`
   and the migrated local state. **Never** `secrets.<env>.json` or `*.tfvars` — those are yours.

Two guardrails make `--purge` safe to have in the same tool as production:

- **Production is hard-blocked** from `--purge` — `--env production` can only ever do the workload-only
  teardown, so its domain + state bucket are permanent.
- **The registrable apex is refused.** A zone whose `dnsDomain` has two or fewer labels
  (`spacestroker.com.`) is the domain itself and is never destroyed; only sub-zones such as
  `dev.spacestroker.com.` may be. A mis-set or copy-pasted secrets file therefore aborts before
  anything is destroyed.

After a `--purge`:

- The **NS delegation at GoDaddy** for the destroyed zone now points nowhere — remove it. A later
  `deploy` recreates the zone with **different** nameservers and prints them.
- `deploy --env <env>` afterwards re-bootstraps the persistent layer automatically; you never need to
  run raw Terraform to recover.

After **any** teardown, **Cloud SQL instance names stay reserved for ~7 days.** To redeploy inside that
window, bump `gcp.dbInstanceSuffix` in `secrets.<env>.json` (e.g. `"2"`) — the tool tells you this on
the way out.

### Resuming a failed teardown

**Re-run the same command.** `teardown` is resumable by design: it never runs a full-stack apply, so a
second run cannot re-create what the first one destroyed.

```bash
dotnet run -- teardown --env development          # or --purge, same as the run that failed
```

The exit code tells you what actually happened:

| Exit | Meaning |
| --- | --- |
| `0`, "Workload destroyed." | Nothing is left. Done. |
| `0`, "ALL BILLABLE RESOURCES ARE GONE" + a warning | Only the VPC / subnet / private-services range survive. They are **free**, and they are held by the Google-managed Direct VPC egress address (`serverless-ipv4-*`), which releases asynchronously — minutes to hours after Cloud Run is deleted. `gcloud compute addresses delete` cannot force it. Re-run later to finish. |
| non-zero, "still billing" | Something that costs money survived all 3 passes. The message lists exactly what. Fix the cause and re-run. |
| non-zero, "--purge STOPPED" | Workload state is not empty, so the persistent layer was left alone rather than orphaning those resources. Re-run `--purge` once the workload state clears. |
| non-zero, "cannot tell what is still deployed" | `terraform state list` itself failed, so the tool refused to guess. Check the workload backend/state; nothing was destroyed by that step. |

Nothing here requires raw Terraform. If you want it anyway, the messages print the exact
`terraform destroy` command as an escape hatch.

> **Migrating an environment deployed before env-scoping:** its resources carry the old un-prefixed
> names, so tear it down with the **old** tool first, delete the old `spacestroker-tfstate-<projectId>`
> bucket and `spacestroker-zone` by hand, then `deploy` fresh under the new naming (and re-delegate the
> new nameservers). No `terraform state mv` — dev data is disposable.

## DB console

```bash
cd deploy/tools/SpaceStroker.DbConsole
dotnet run -- --db game              # or: --db messaging
```

Starts the Cloud SQL Auth Proxy, connects over IAM, and opens a REPL. `SELECT`/`WITH`/`SHOW`/`EXPLAIN`
run immediately; `INSERT`/`UPDATE`/`DELETE`/DDL require typing `YES` first. `\q` to quit.

## Secrets to replace for production

Everything below is a **placeholder** in `secrets.example.json`; put real values only in your local
`secrets.<env>.json`.

| Where | Keys |
| --- | --- |
| **`gcp`** | `projectId`, `region`, `dnsZoneName`, `dnsDomain`, `domains.{api,app,lp,lpWww}`, `consumerCount`, `apiMinInstances`, `apiMaxInstances`, `enableDeletionProtection`, `dbInstanceSuffix` (optional) — **no `stateBucket`**, it is derived |
| **`secrets`** (→ Secret Manager) | `ConnectionStrings:SpaceStroker` (game DB pwd), `ConnectionStrings:Messaging` (messaging DB pwd, #64), `ConnectionStrings:Redis` (Memorystore AUTH), `GeneralSettings:Token:TokenOwnerPrivateKey`, `GeneralSettings:LaunchTokens:MainSigningPrivateKeyPem`, `GeneralSettings:SecretProtection:Key`, `GeneralSettings:Fx:ApiKey`, **`GeneralSettings:Token:ClusterUrl`** (#62 — the Solana RPC endpoint is a secret because the provider's api-key rides in its query string) |
| **`appConfig`** (non-secret env) | `ASPNETCORE_ENVIRONMENT=Production`, `GeneralSettings:Token:{FallbackClusterUrl(public mainnet),MintAddress,SenderAccount,TokenOwnerPublicKey,Decimals}`, `GeneralSettings:LaunchTokens:MainOperatorPublicKeyPem`, `GeneralSettings:Cors:AllowedOrigins:0=https://app.spacestroker.com` |
| **`frontBuild`** (baked into the SPA) | `VUE_APP_API_BASE_URL`, `VUE_APP_WEBSOCKET_BASE_URL`, `VUE_APP_SOLANA_NETWORK=mainnet-beta`, `VUE_APP_USDC_TOKEN_ADDRESS`, `VUE_APP_GAME_TOKEN_ADDRESS`, `VUE_APP_ALLOW_FREE_BETS=false`, `VUE_APP_DEV_LAUNCH=false`, `VUE_APP_IDLE_DISCONNECT_HIDDEN_MS=1800000`, `VUE_APP_IDLE_DISCONNECT_INACTIVE_MS=3600000`, **`VUE_APP_SOLANA_PROXY_URL=https://api.<env-host>/rpc`** (#99), `VUE_APP_SOLANA_CONFIRM_TIMEOUT_MS=45000`, `VUE_APP_SOLANA_CONFIRM_POLL_INTERVAL_MS=1000`, `VUE_APP_BALANCE_POLL_INTERVAL_MS=30000` |

> **Migrating an existing `secrets.<env>.json` for #99 (frontend Helius gateway).** Add the four
> `frontBuild` keys above. **`VUE_APP_SOLANA_PROXY_URL` is the load-bearing one:** it was never emitted
> before, so the deployed SPA had no proxy at all and talked directly to the public Solana RPC — which is
> unusable from a production browser. Point it at *that environment's* API host —
> `https://api.spacestroker.com/rpc` for production, `https://api.dev.spacestroker.com/rpc` for
> development. All four are **public build values**: they belong in `frontBuild`, never in `secrets`, and
> the browser must never receive the keyed provider URL (that stays server-side in
> `GeneralSettings:Token:ClusterUrl`). Leaving `VUE_APP_SOLANA_PROXY_URL` empty is a valid *local/devnet*
> setting (the SPA then uses the public endpoint directly, spending no provider credits) but is **not**
> viable for a deployed mainnet environment.

> **Migrating an existing `secrets.<env>.json` for #62 (Helius RPC).** `GeneralSettings:Token:ClusterUrl`
> **moved from `appConfig` to `secrets`** — `appConfig` entries become *plaintext* Cloud Run env vars, and
> the endpoint now carries an api-key. In your local file: move that key into the `secrets` block and set
> it to your Helius URL (`https://mainnet.helius-rpc.com/?api-key=…`, or `devnet.helius-rpc.com` for the
> development environment), then add `"GeneralSettings:Token:FallbackClusterUrl": "https://api.mainnet-beta.solana.com/"`
> to `appConfig` (public, keyless — it is the endpoint the backend falls back to after the primary's
> retries are exhausted; leave it blank to disable failover). Re-run the secret sync so the new Secret
> Manager entry exists before the next apply.

> The game-DB user needs **CREATE DATABASE** (or pre-create `spacestroker_messaging`) so the MassTransit
> Postgres transport can provision its DB at Consumers startup (#64). Both DBs are declared in the
> `data` module, so a default apply already creates `spacestroker` + `spacestroker_messaging`.

## Notes / caveats

- **`consumer_count > 1` is an app concern** (duplicate Hangfire jobs, EF-migration race, re-seeding).
  Default is **1**; raising it is validated separately.
- **No real `apply` is committed here** — the IaC is validated with `terraform fmt` + `validate`; run the
  real `apply`/`deploy` against your own GCP project.
- Secret **values** never enter Terraform state or committed files; only Secret Manager references do.

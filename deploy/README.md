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
or in Terraform state), writes the non-secret Terraform variables to `production.auto.tfvars.json`
(git-ignored), builds/pushes the images to Artifact Registry, `terraform apply`s the workload, then
builds each static site (baking the `frontBuild` `VUE_APP_*` values) and syncs it to its GCS bucket +
invalidates the CDN. **Consumers is released before the API** (it owns EF migrations/seeding).

## Teardown — two behaviours, chosen by the environment

```bash
cd deploy/tools/SpaceStroker.Deploy
dotnet run -- teardown --env production    # workload only — keeps the domain
dotnet run -- teardown --env development   # TOTAL — nothing Terraform created survives
```

Both re-type the environment name to confirm, and both override Cloud SQL deletion-protection so the
destroy can proceed.

**`--env production` — "drop everything, keep the domain."** Destroys the **workload layer only**
(Cloud Run, Cloud SQL + **all its data**, Memorystore, LB, Cloud Armor, VPC/NAT/connector, Artifact
Registry, buckets/CDN). The **persistent layer** (Cloud DNS zone + state bucket) is preserved, so
re-running `deploy` recreates the workload and re-attaches to the same domains + managed certs.

**Any other environment — TOTAL teardown.** After it finishes, **nothing Terraform created for that
environment remains in the project**; the only surviving asset is the **GoDaddy registration of
spacestroker.com**. In order:

1. **Workload layer** destroyed (as above). If this fails part-way the run stops here — the persistent
   layer is left alone and re-running teardown resumes safely.
2. **Persistent layer** destroyed — the Cloud DNS zone *and* the state bucket. The bucket holds the
   very state that describes it, so the tool first migrates that state to a **local** backend
   (`init -migrate-state -force-copy -backend=false`), then destroys both with Terraform (the
   non-production bucket/zone are created with `force_destroy = true` and without `prevent_destroy`).
3. **Orphan Secret Manager secrets** swept — anything left matching `spacestroker-<envCode>-*`, plus
   every id derived from a key in `secrets.<env>.json` (the tool `gcloud secrets create`s each key, so
   a key that drifted out of Terraform's list would otherwise survive). Ids are printed; values never.
4. **Locally generated files** removed — `<environment>.tfvars.json`, both layers' `.terraform-<envCode>/`
   and the migrated local state. **Never** `secrets.<env>.json` or `*.tfvars` — those are yours.

Two guardrails make this safe to have in the same tool as production:

- **Production is hard-blocked** from the total path — `--env production` can only ever do the
  workload-only teardown.
- **The registrable apex is refused.** A zone whose `dnsDomain` has two or fewer labels
  (`spacestroker.com.`) is the domain itself and is never destroyed; only sub-zones such as
  `dev.spacestroker.com.` may be. A mis-set or copy-pasted secrets file therefore aborts before
  anything is destroyed.

Afterwards:

- The **NS delegation at GoDaddy** for the destroyed zone now points nowhere — remove it. A later
  `deploy` recreates the zone with **different** nameservers and prints them.
- **Cloud SQL instance names stay reserved for ~7 days.** To redeploy inside that window, bump
  `gcp.dbInstanceSuffix` in `secrets.<env>.json` (e.g. `"2"`) — the tool tells you this on the way out.
- `deploy --env <env>` afterwards re-bootstraps the persistent layer automatically; you never need to
  run raw Terraform to recover.

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
| **`frontBuild`** (baked into the SPA) | `VUE_APP_API_BASE_URL`, `VUE_APP_WEBSOCKET_BASE_URL`, `VUE_APP_SOLANA_NETWORK=mainnet-beta`, `VUE_APP_USDC_TOKEN_ADDRESS`, `VUE_APP_GAME_TOKEN_ADDRESS`, `VUE_APP_ALLOW_FREE_BETS=false`, `VUE_APP_DEV_LAUNCH=false` |

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

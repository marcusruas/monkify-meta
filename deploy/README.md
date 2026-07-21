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
    persistent/                # LAYER 1 — DNS zone + domain + state bucket (prevent_destroy; teardown never touches)
    workload/                  # LAYER 2 — everything teardown may destroy
      modules/{network,data,registry,iam,secrets,cloud_run_api,cloud_run_consumers,static_site,edge}
  tools/
    SpaceStroker.Deploy/           # `spacestroker-deploy deploy [api|consumers|front|lp]` + `teardown`
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

## One-time bootstrap (state bucket + persistent layer)

The remote-state bucket must exist before either layer can use the `gcs` backend, and the persistent
layer creates that bucket — so bootstrap the persistent layer with **local** state first, then migrate.

```bash
cd deploy/terraform/persistent
cp production.tfvars.example production.tfvars   # fill in project / domain / state bucket

# 1. Create the state bucket + DNS zone with local state:
terraform init -backend=false
terraform apply -var-file=production.tfvars

# 2. Migrate the persistent state into the bucket it just created:
terraform init -migrate-state \
  -backend-config="bucket=$(terraform output -raw state_bucket)" \
  -backend-config="prefix=persistent"

# 3. Delegate the domain: point the registrar's nameservers at:
terraform output dns_name_servers
```

(The `spacestroker-deploy` tool creates the state bucket automatically on first `deploy` if it is missing.)

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

## Teardown ("drop everything, keep the domain")

```bash
cd deploy/tools/SpaceStroker.Deploy
dotnet run -- teardown               # re-type the environment name to confirm
```

Destroys the **workload layer only** (Cloud Run, Cloud SQL + **all its data**, Memorystore, LB, Cloud
Armor, VPC/NAT/connector, Artifact Registry, buckets/CDN). The **persistent layer** (Cloud DNS zone +
domain + state bucket) is preserved, so re-running `deploy` recreates the workload and re-attaches to the
same domains + managed certs. Cloud SQL deletion-protection is deliberately overridden for the destroy.

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
| **`gcp`** | `projectId`, `region`, `stateBucket`, `dnsZoneName`, `dnsDomain`, `domains.{api,app,lp,lpWww}`, `consumerCount`, `apiMinInstances`, `apiMaxInstances`, `enableDeletionProtection` |
| **`secrets`** (→ Secret Manager) | `ConnectionStrings:SpaceStroker` (game DB pwd), `ConnectionStrings:Messaging` (messaging DB pwd, #64), `ConnectionStrings:Redis` (Memorystore AUTH), `GeneralSettings:Token:TokenOwnerPrivateKey`, `GeneralSettings:LaunchTokens:MainSigningPrivateKeyPem`, `GeneralSettings:SecretProtection:Key`, `GeneralSettings:Fx:ApiKey` |
| **`appConfig`** (non-secret env) | `ASPNETCORE_ENVIRONMENT=Production`, `GeneralSettings:Token:{ClusterUrl(mainnet),MintAddress,SenderAccount,TokenOwnerPublicKey,Decimals}`, `GeneralSettings:LaunchTokens:MainOperatorPublicKeyPem`, `GeneralSettings:Cors:AllowedOrigins:0=https://app.spacestroker.com` |
| **`frontBuild`** (baked into the SPA) | `VUE_APP_API_BASE_URL`, `VUE_APP_WEBSOCKET_BASE_URL`, `VUE_APP_SOLANA_NETWORK=mainnet-beta`, `VUE_APP_USDC_TOKEN_ADDRESS`, `VUE_APP_GAME_TOKEN_ADDRESS`, `VUE_APP_ALLOW_FREE_BETS=false`, `VUE_APP_DEV_LAUNCH=false` |

> The game-DB user needs **CREATE DATABASE** (or pre-create `spacestroker_messaging`) so the MassTransit
> Postgres transport can provision its DB at Consumers startup (#64). Both DBs are declared in the
> `data` module, so a default apply already creates `spacestroker` + `spacestroker_messaging`.

## Notes / caveats

- **`consumer_count > 1` is an app concern** (duplicate Hangfire jobs, EF-migration race, re-seeding).
  Default is **1**; raising it is validated separately.
- **No real `apply` is committed here** — the IaC is validated with `terraform fmt` + `validate`; run the
  real `apply`/`deploy` against your own GCP project.
- Secret **values** never enter Terraform state or committed files; only Secret Manager references do.

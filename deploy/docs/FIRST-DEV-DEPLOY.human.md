# First-time DEV deploy — the friendly version

A walkthrough for standing up SpaceStroker on GCP for the **first time**, in the **development**
environment, using the tools in `deploy/`. It assumes you've never run any of this before and there
is nothing in GCP yet.

Read this whole page once before typing anything. It's long because it's your first time — after the
first deploy, day-to-day is just one command.

---

## The one thing to understand first

The deploy tool (`spacestroker-deploy`) was written assuming the app images and the database already
exist. On a **brand-new** project they don't, and that creates two "chicken-and-egg" problems:

1. **Cloud Run needs a container image that hasn't been built yet.**
2. **The app needs the database's address — but the database doesn't exist until we create it, and
   its address isn't known until then.**

So the very first deploy is **not** a single command. The mental model is:

> **Run the deploy once to create the plumbing → fill in the two addresses it just gave us + push
> the images → run the deploy again to finish.**

Two runs, with a small fix-up in the middle. That's the whole trick. Every deploy *after* the first
one is just `dotnet run -- deploy`.

---

## What you need installed (all on your PATH)

- **gcloud** and **gsutil** (Google Cloud SDK)
- **terraform** (version 1.6 or newer)
- **docker** — and it must be **running**
- **yarn** (builds the game front-end) and **npm** (builds the landing page)
- **.NET 8 SDK** (runs the two console tools)
- **cloud-sql-proxy v2** (only needed later, for the DB console — optional)

Quick check (any that error, install before continuing):

```bash
gcloud --version && terraform version && docker version && yarn --version && npm --version && dotnet --version
```

---

## One thing to confirm before you start

Your `secrets.development.json` points at project `project-ecdfb1b6-ed69-4e49-899`.

- Confirm that's a **real project you created**, that **billing is enabled** on it, and that the id
  is **exactly** right (auto-generated ids are cut off at 30 characters — that one ends in `-899`,
  which looks like it may have been truncated). Check with `gcloud projects list`.
- It should be a **dev-only** project. Don't share it with a future production deploy.

**About the domain:** `dev.spacestroker.com` is registered at GoDaddy — good. You **don't** delegate
the nameservers yet. We create everything first; the deploy prints a set of Google nameservers, and
**at the very end** you add those at GoDaddy. Until you do, the app still works via its raw Cloud Run
URL — only the pretty `https://app.dev.spacestroker.com` address (and its HTTPS certificate) waits on
the delegation. So DNS is the **last** step, not a blocker.

---

## Your secrets file — status

- ✅ **Database passwords: done** (you've added them). Just leave `Host=REPLACE_ME` in both
  connection strings for now — we fill those in after Run 1.
- ⚠️ **Redis has no password.** Memorystore is created **without** authentication in this stack, so
  after Run 1 the Redis line must be **just host and port**, with no `,password=` part:
  `<redis-host>:6379`.
- ℹ️ **The app runs in "Development" mode in the cloud.** Your file sets
  `ASPNETCORE_ENVIRONMENT: Development` and turns on `VUE_APP_DEV_LAUNCH` + `VUE_APP_ALLOW_FREE_BETS`,
  so the deployed app will have dev launch buttons, free bets, Swagger, and permissive CORS. For a
  dev environment that's usually what you want — just confirm it's intended.

You do **not** need to touch the Solana / admin-token keys — those are already real devnet
values.

---

## The deploy, step by step

Throughout, replace `<PROJECT>` with your real project id. Commands assume you start at the
workspace root (`E:\Monkify`).

### Stage 0 — Sign in and switch on the GCP services

```bash
gcloud auth login
gcloud auth application-default login
gcloud config set project <PROJECT>

# Let Docker push to Artifact Registry:
gcloud auth configure-docker us-central1-docker.pkg.dev

# Turn on every API this stack uses (safe to re-run):
gcloud services enable \
  run.googleapis.com sqladmin.googleapis.com redis.googleapis.com \
  compute.googleapis.com servicenetworking.googleapis.com vpcaccess.googleapis.com \
  secretmanager.googleapis.com artifactregistry.googleapis.com dns.googleapis.com \
  cloudresourcemanager.googleapis.com \
  monitoring.googleapis.com billingbudgets.googleapis.com --project <PROJECT>
```

> The last two are for the **production** monitoring stack (dashboard, uptime checks, budget). A
> development deploy never creates those resources, but enabling the APIs here is free and saves a
> failed apply the first time you deploy production.

### Stage 1 — Sanity-check the names

```bash
cd monkify-meta/deploy/tools/SpaceStroker.Deploy
dotnet run -- validate --env development
```

Every line should say **PASS**. If anything says FAIL, the project id or environment is too long —
stop and fix the secrets file before going further.

### Stage 2 — Run 1: create the plumbing (this one is *expected* to stop early)

```bash
# still in deploy/tools/SpaceStroker.Deploy
dotnet run -- deploy --env development
```

What happens, and why it's OK:

- It creates the Terraform **state bucket** and the **DNS zone**, and prints a set of **nameservers**
  — **copy those down**, you'll add them at GoDaddy in the last stage.
- It uploads your secrets to Secret Manager.
- It runs Terraform, which builds the network, the **database (Cloud SQL)**, **Redis**, the image
  registry, and the storage buckets. Cloud SQL takes ~10–15 minutes — be patient.
- Then it tries to start the app on Cloud Run, **fails because no image has been pushed yet**, and
  stops with an error.

**That error is the expected end of Run 1.** The database and registry now exist — that's all we
needed.

### Stage 3 — Fill in the two addresses

Get the database's private address and the Redis address:

```bash
gcloud sql instances describe spacestroker-dev-pg --project <PROJECT> \
  --format="value(ipAddresses[0].ipAddress)"
gcloud redis instances describe spacestroker-dev-redis --region us-central1 --project <PROJECT> \
  --format="value(host)"
```

Now edit `deploy/secrets.development.json`:

- `ConnectionStrings:SpaceStroker` → replace `Host=REPLACE_ME` with the **Cloud SQL IP**
  (keep `Database=spacestroker`, keep your password).
- `ConnectionStrings:Messaging` → same **Cloud SQL IP** (keep `Database=spacestroker_messaging`).
- `ConnectionStrings:Redis` → `<redis-host>:6379` (no password — see above).

### Stage 4 — Build and push the two app images

The registry exists now, so we can push. From the workspace root:

```bash
cd monkify-backend/src
docker build -t us-central1-docker.pkg.dev/<PROJECT>/spacestroker-dev/consumers:latest -f SpaceStroker.Consumers/Dockerfile .
docker push  us-central1-docker.pkg.dev/<PROJECT>/spacestroker-dev/consumers:latest
docker build -t us-central1-docker.pkg.dev/<PROJECT>/spacestroker-dev/api:latest      -f SpaceStroker.Api/Dockerfile .
docker push  us-central1-docker.pkg.dev/<PROJECT>/spacestroker-dev/api:latest
```

### Stage 5 — Run 2: finish the deploy

```bash
cd ../../monkify-meta/deploy/tools/SpaceStroker.Deploy
dotnet run -- deploy --env development
```

This time everything is in place: the corrected secrets get re-uploaded, Terraform creates the
Cloud Run services (images exist, database reachable), then the game front-end and landing page are
built and uploaded. It should run all the way to **"Deploy complete."**

> The "Consumers" service runs the database migrations and seeds the configured default game types
> (`GeneralSettings:Operator:DefaultGames`), so
> give it a minute after it starts before expecting the API to work.

### Stage 6 — Check it actually works (before touching DNS)

```bash
# From deploy/terraform/workload, ask Terraform where things are:
cd ../../terraform/workload
TF_DATA_DIR=.terraform-dev terraform output
```

- Grab `api_uri` (the raw Cloud Run URL) and open `<api_uri>/health` in a browser — you want
  `Api running!`. **This proves the whole stack works**, with no DNS needed.
- Grab `lb_ip` — that's the public IP the domain will point at.

### Stage 7 — Point the domain at it (GoDaddy)

Now, and only now, delegate the sub-domain:

1. Sign in to **GoDaddy** and open the DNS management for **spacestroker.com**.
2. Add the **nameservers Run 1 printed** as **NS records for the host `dev`** (i.e. delegating
   `dev.spacestroker.com` to Google's nameservers). Add one NS record per nameserver.
3. Wait for it to propagate, then for Google's managed certificate to go **ACTIVE** (anywhere from
   ~15 minutes to an hour). You can watch it with:
   ```bash
   gcloud compute ssl-certificates list --project <PROJECT>
   ```
4. Once the cert is ACTIVE, open **https://app.dev.spacestroker.com** — that's the app.

---

## When you want to tear it down

By default, teardown destroys the **workload** — Cloud SQL (and all its data), Redis, Cloud Run, the
load balancer, etc. — but **keeps the DNS zone and the Terraform state bucket** (they cost about
$0.20/month, so it's not worth destroying them). This is the cheap way to "park" the environment
between test sessions: tear down, and later `deploy` recreates everything and re-attaches to the same
domain delegation.

```bash
cd monkify-meta/deploy/tools/SpaceStroker.Deploy
dotnet run -- teardown --env development
```

It asks you to type `development` to confirm. Because the DNS zone is kept, your GoDaddy nameserver
delegation stays valid — no need to touch GoDaddy. If you redeploy within ~7 days, bump
`dbInstanceSuffix` in the secrets file (GCP holds the old database name for a week).

**Full wipe (rare):** if you want to destroy *everything*, including the DNS zone and state bucket,
add `--purge`:

```bash
dotnet run -- teardown --env development --purge
```

After a `--purge`, remove the now-dead `dev` NS records at GoDaddy; a later `deploy` recreates the
zone with **different** nameservers. (`--purge` is blocked for production.)

**If a teardown fails part-way, just run it again.** It will not re-create anything — the pre-destroy
apply is targeted at the Cloud SQL instance and is skipped when that instance is already gone, and
`destroy` retries 3 times on its own. If it finishes with a warning that only `module.network.*`
resources survive, that is a **success**: those are free (they're held by a Google-managed address that
releases on its own schedule), and re-running later tidies them up. Anything that costs money surviving
makes the command exit non-zero and tells you exactly what is left. See `deploy/README.md` →
"Resuming a failed teardown".

---

## Known gotchas (so they don't surprise you)

- **The first deploy fails on purpose** at the Cloud Run step. That's Stage 2 finishing, not a real
  failure. Every later deploy is a single clean `dotnet run -- deploy`.
- **Cloud SQL is slow to create** (~10–15 min) and slow to delete. Don't assume it's hung.
- **HTTPS certificate is "pending/provisioning" until you delegate DNS at GoDaddy.** Expected; that's
  why DNS is the last stage.
- **The DB console now works against dev.** After the deploy you can inspect the database with:
  ```bash
  cd monkify-meta/deploy/tools/SpaceStroker.DbConsole
  dotnet run -- --db game --env development      # or: --db messaging
  ```
  (It now targets `spacestroker-dev-pg` correctly — the earlier hardcoded-name bug is fixed.)
- **`secrets.development.json` is git-ignored** — it stays on your machine, never committed. Good.

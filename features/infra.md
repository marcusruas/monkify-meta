# Domain — Infrastructure (deploy, transport, RPC and the API edge)

> **What this covers.** How Space Stroker is deployed to GCP, how events move between the two hosts,
> how the browser reaches Solana, how the API's edge is rate-limited and how its errors reach the
> player. Ops/infra — **no player-facing game behaviour** lives here.
>
> Deep design: [`../specs/terraform-gcp-deploy.md`](../specs/terraform-gcp-deploy.md) ·
> [`../specs/terraform-development-deploy.md`](../specs/terraform-development-deploy.md) ·
> [`../specs/masstransit-postgres-transport.md`](../specs/masstransit-postgres-transport.md) ·
> [`../specs/frontend-helius-gateway.md`](../specs/frontend-helius-gateway.md) ·
> [`../specs/api-error-messages.md`](../specs/api-error-messages.md) ·
> [`../specs/rate-limiter-keying.md`](../specs/rate-limiter-keying.md) ·
> [`../specs/unified-websocket-connection.md`](../specs/unified-websocket-connection.md) ·
> [`../specs/gcp-dashboards.md`](../specs/gcp-dashboards.md) ·
> [`../specs/teardown-resume.md`](../specs/teardown-resume.md) ·
> [`../specs/entry-path-recovery.md`](../specs/entry-path-recovery.md) ·
> [`../specs/player-lockouts.md`](../specs/player-lockouts.md) ·
> [`../specs/robots-txt.md`](../specs/robots-txt.md) ·
> [`../specs/static-site-cache-control.md`](../specs/static-site-cache-control.md) ·
> [`../specs/static-site-status-codes.md`](../specs/static-site-status-codes.md).
>
> ⚠️ **Several of those specs shipped but were never `terraform apply`-ed.** See "Applied vs merged"
> at the bottom before assuming production matches the repo.

---

## 1. Deployment topology (GCP)

Terraform + two local C# console tools under `monkify-meta/deploy/`, driven by one **unversioned
`secrets.<env>.json`** per environment. The real `terraform apply` is an **operator step**. See
[`../conventions/backend.md`](../conventions/backend.md) → "Meta-repo console tools" for where the
tooling now lives (card #167 moved the DB console out to `tools/`).

- **API → Cloud Run**, autoscaling, `min_instance_count ≥ 1`, public behind the LB.
- **Consumers → Cloud Run**, **internal ingress only** — it hosts `/hangfire` and **owns EF migrations
  and seeding** — with **CPU always-on** (`cpu_idle = false`) and instance count pinned to
  `var.consumer_count` (default **1**; `> 1` is a documented app caveat). Released **before** the API.
- **front + lp → GCS website buckets + Cloud CDN** behind the LB.
- **Cloud SQL for PostgreSQL 16**, one instance, two databases **`spacestroker`** +
  **`spacestroker_messaging`**, **private IP only**. **Memorystore Redis 7**, private only. Cloud Run
  reaches both via a **Serverless VPC Access connector**; egress via **Cloud NAT**.
- **Artifact Registry** holds the API + Consumers images. **External HTTPS LB → Cloud Armor** →
  api / app / lp, **Google-managed TLS**, **Cloud DNS** for `api.` / `app.` / apex / `www.`.
- **Environment-scoped naming.** `name_prefix = spacestroker-<envCode>` (`development→dev`) because dev
  and prod **share one GCP project** and would otherwise collide on names *and* Terraform state. Per-env
  state bucket, Cloud SQL, Memorystore, Secret Manager ids, Artifact Registry repo, GCS buckets,
  `<env>.tfvars.json` and `TF_DATA_DIR`. ⚠️ The full word breaks the SA 30-char and VPC-connector
  25-char caps — hence the 4-char env code, with fail-fast id-cap validation (`validate`).
- **Terraform is split** **persistent** (DNS zone + state bucket, `prevent_destroy`) vs **workload**
  (everything `teardown` may destroy). ⚠️ `prevent_destroy` **cannot be variable-driven**, so the split
  is implemented as protected/ephemeral resource pairs on `protect_persistent`.

### Backend Cloud-Run readiness (no game-behaviour change)

- **Health probes:** `SpaceStroker.Consumers` exposes `GET /health` via `MapHealthChecks`.
  `SpaceStroker.Api` already had one via `HomeController` — it was **not** duplicated. Both anonymous
  and un-rate-limited.
- **Behind the LB:** the API calls `UseForwardedHeaders` **before** rate limiting and
  `UseHttpsRedirection`, so it sees the real scheme/client behind the TLS terminator and does not
  redirect-loop.
- **CORS is config-driven** via `GeneralSettings:Cors:AllowedOrigins` (committed default
  `https://app.spacestroker.com`; overridable per env). `Development` still allows any origin.

### Static-site serving

- The **front** bucket carries `Content-Security-Policy: frame-ancestors *` (reproducing the retired
  nginx header, so a site can still embed the app — see [`embedding.md`](embedding.md)) **and**
  `X-Robots-Tag: noindex`. The **lp** bucket carries **neither** — the landing page must stay indexable.
  ⚠️ The `noindex` **header**, not `robots.txt`, is what de-indexes `app.spacestroker.com`: `robots.txt`
  only stops crawling, and blocking the crawl also stops Google ever reading a `noindex` *meta* tag, so
  a URL it already knows (the LP links straight there) could persist as a bare titleless link.
- ⚠️ **HTTP status codes are per site.** A GCS website config **always** returns 404 for
  `not_found_page` and cannot override that status. So the **front leaves it unset** and gets its SPA
  fallback from the **load balancer**: a `custom_error_response_policy` on the **`/*` path rule** rewrites
  the bucket's 404 into `/index.html` with `override_response_code = 200`, while
  `/js/* /css/* /img/* /media/*` route through a **more specific, policy-free** rule so a missing hashed
  chunk still surfaces a real 404. ⚠️ **The policy must live on the `/*` rule, not the matcher default** —
  a `path_rule` that omits a policy **inherits** the matcher default, which would apply it to the assets
  too. The **lp** sets `not_found_page = "404.html"` and **keeps** the 404 status.

### Secret Manager — 2 versions per secret, and unchanged values are not re-pushed

Secret Manager bills **per enabled version per month** (first 6 free), **not per secret**, and access
operations are effectively free. So `deploy` **reads the stored `latest` and skips the `versions add`
when the value has not changed**, then destroys everything past the newest **2** versions;
`spacestroker-deploy prune-secrets` does the same standalone over the **live**
`spacestroker-<envCode>-*` list, which is what catches a key that drifted out of `secrets.<env>.json`
and would otherwise bill forever unnoticed.

Retention is **2** because Cloud Run mounts every secret with `version = "latest"` — even a
rolled-back revision re-resolves it, so only the newest is ever served, and the second exists purely
as a cloud-side rollback for a bad value (`secrets.<env>.json` is git-ignored and lives on one
workstation). ⚠️ **Destroying a version is irreversible.** Before this, 24 secrets had grown to 692
near-identical versions costing ~R$242/month; see
[`../specs/secret-version-retention.md`](../specs/secret-version-retention.md).

### Teardown

`teardown` destroys workload → persistent (zone + state bucket, via a **local state migration**) →
orphan-secret sweep → local generated-file cleanup, guarded by a **hard block on production** and a
refusal to touch the registrable apex zone. GoDaddy is only warned about, never called.
⚠️ **Re-running `teardown` after a partial failure re-creates what was destroyed** (there is a
pre-destroy `apply`) — resume with a raw `terraform destroy`.

## 2. Messaging transport

Game state-machine events ride the **MassTransit Postgres SQL transport** — **no message broker at
all**, one fewer always-on service to host, patch and secure, and the thing that unblocked GCP (which
has no managed RabbitMQ). Transport + provisioning only: **event contracts, consumers and the game flow
are unchanged**.

- **Separate messaging database.** Queues/topics/subscriptions/messages are tables under schema
  **`transport`** in **`spacestroker_messaging`** — a distinct DB on the **same** instance as the game DB.
  Game tables never live there.
- ⚠️ **Consumers provisions; the API only publishes.** Consumers runs
  `AddPostgresMigrationHostedService()` at startup, creating the DB and schema **before the bus
  connects** (mirroring how it owns EF migrations). The API registers `UsingPostgres()` **publisher-only**
  and stays stateless.
- **One reused credential** from `ConnectionStrings:Messaging` (only `Database=` differs). ⚠️ That user
  must have **`CREATE DATABASE`** on the instance.
- **Eight named endpoints kept**, one per consumer, plus `ConfigureEndpoints`.
- ⚠️ **Delivery is at-least-once** (competing consumers via `SKIP LOCKED` + `LISTEN/NOTIFY`).
  Reward/refund idempotency is **ledger-based** (`TransactionLog`): a redelivered `RewardGameEvent` for
  an already-rewarded game short-circuits and credits exactly once.
- **Hangfire is unchanged** and stays on the **game** DB. Redis (cache + SignalR backplane) unchanged.
- The publish abstraction is transport-neutral: **`IMessageBus` / `MessageBusPublisher`** (was
  `IRabbitMQService`).

## 3. Solana access from the browser

⚠️ **The SPA no longer talks to a public Solana RPC at all.** A production browser cannot use
`api.mainnet-beta.solana.com`, so every part of a paid bet that used to go there now goes through our
own backend. Three RPC round-trips per player were **deleted outright** rather than migrated, and the
balance poll was slowed and tied to the idle disconnect, so routing player traffic through a paid
provider does not cost credits in proportion to player count.

*(The earlier model — public RPC primary, `withFailover` retrying on HTTP 429 against a `/rpc` proxy
with a sticky cooldown — is **gone**. Today: one connection, no failover, no cooldown, and confirmation
by polling `getSignatureStatuses` rather than a WebSocket.)*

**`GET /solana/balance?wallet={pubkey}&mint={mint}`** → `{ wallet, mint, uiAmount, rawAmount, decimals }`.

- ⚠️ **`rawAmount` is a string** — base units can exceed `Number.MAX_SAFE_INTEGER`.
- **`GET /solana/balance/live`** is the same read with the cache bypassed. It is a separate **route**,
  not a query flag, ⚠️ because `AspNetCoreRateLimit` matches on `{verb}:{path}` and cannot budget a
  query-string value.
- ⚠️ **The `mint` must be the configured mint** — anything else is rejected **before** any upstream RPC
  call. This is what stops the endpoint becoming a free public balance API for every mint on Solana.
- **Every rejection returns one identical vague message** (`ErrorMessages.TokenNotAvailable`, 400):
  unknown mint, malformed wallet and missing parameter are indistinguishable. ⚠️ The query parameters
  are deliberately **nullable** — non-nullable ones made the framework answer *"The wallet field is
  required"*, leaking which field was wrong.
- **Decimals always come from the registered token**, never from the caller or the RPC response.
- ⚠️ **A wallet with no associated token account reads `0`, not an error, and is never retried** — it is
  a successful round-trip describing a business fact, so retrying would spend four extra calls plus a
  fallback on every first-time visitor.
- Both endpoints **write** the Redis cache; only the cached route **reads** it, so a `live` read also
  refreshes what the next background poll sees. A **failed** read is never cached.
- **Gating** is a server-side **Origin allow-list** (shared with `RpcController`) plus per-IP rate rules
  (`*:/solana/*` 120/min, `*:/solana/balance/live` 20/min). ⚠️ Stated plainly: **`Origin` is
  self-reported and forgeable**, so this is a practical barrier, not a guarantee — the registered-mint
  restriction and the rate limits are the substantive protection.

**Still current from the earlier RPC work:** `sendToken` is a **single transaction** — idempotent ATA
creation for sender **and** receiver bundled with the transfer, so a first-time bettor gets **one**
wallet approval instead of three — with an optional ComputeBudget **priority fee** and exact
integer/BigInt amount math.

## 4. The API edge

### Error messages reach the player

When the API rejects a request the player reads **the sentence the backend actually wrote**, not
`Request failed with status code 400`. The fix lives in the shared `mainAPI` response interceptor, so
all 23 call sites were corrected at once and a new endpoint cannot start out mute.

- **The envelope is the source.** Every rejection carries `{ data, messages: [{ type, value }] }`, and
  `utils/apiError.ts` → `apiErrorMessage(err)` is the **only** place in `src/` that reads it.
- ⚠️ **`type` is numeric** — `Informational 0 / Alert 1 / ValidationFailure 2 / Error 3`, with **no
  `JsonStringEnumConverter`** on the API. Two admin screens once filtered `type === 1` commented
  `/* ValidationFailure */` — that is **Alert**; they never matched. **Anything new that filters this
  field must use `2`.**
- **One friendly fallback** for anything unusable: *"Something went wrong — please try again."*
- ⚠️ **Status codes never reach the player.** `error.message` on an AxiosError is the axios status line;
  it goes to `console.error` and nowhere else. `displayableErrorMessage(err, fallback)` keeps a genuinely
  thrown sentence (a wallet that cannot sign) but never lets a status line through — **the presence of
  `response` is what tells the two apart**.
- **Identical toasts dedupe** — the message text *is* the `toastId`, so a fast retry refreshes one toast
  instead of stacking three. A counter or timestamp would defeat this.
- **`handled === true` still wins**, and so does the 200 ms `setTimeout` that lets a call site's `catch`
  run first. A site that reports a failure **itself** must set `err.handled = true` or the player reads
  the same sentence twice.

### Rate limiting — a money-path decision

- ⚠️ **`RealIpHeader` is pinned to `""` in `spacestrokersettings.json` and must stay pinned.**
  AspNetCoreRateLimit 4.0.2 **defaults it to `"X-Real-IP"`** and resolves the client from that header
  **in preference to** `Connection.RemoteIpAddress`. Google's ALB does not originate that header but
  forwards whatever the **client** sends — so a caller supplying its own `X-Real-IP` got a **fresh bucket
  per request** and walked through every rule. That was a **live production bypass**.
  `RateLimitSettingsTests` asserts the pinned value.
- ⚠️ **`POST /games/{id}/bets` is deliberately UNLIMITED app-side.** For `SolanaToken` the client
  transfers on-chain **first**, then posts the signature — so a rejected bet strands tokens with no `Bet`
  row, no auto-refund, and nothing for `POST /admin/bets/refund` to find. `RateLimitSettingsTests` asserts
  the rule list **exactly**, so a new catch-all fails the build instead of a player quietly losing money.
- ⚠️ A rule whose `Endpoint` has no `:` (e.g. `api/*`) matches **nothing** — the first segment is parsed
  as the *hostname*.
- **Cloud Armor throttles; it does not ban.** ⚠️ A 403 / "CORS blocked" on the deployed API is usually
  Armor's OWASP SQLi/XSS WAF false-positiving on Solana base58/hex bodies, **not** CORS. ⚠️ Changing a
  rule's *action* 400s because the provider merges via `patchRule` — delete the live rule or bump its
  priority.

## 5. Applied vs merged — read before trusting the repo

⚠️ Several infra changes are **on `main` but were never `terraform apply`-ed**, so production does not
match the repo:

| Change | Status |
| --- | --- |
| Static-site **status codes** (SPA fallback / lp 404) | merged 2026-07-27, **not applied**, never `terraform validate`-ed |
| Static-site **`Cache-Control`** | merged, **no deploy has run** |
| **`X-Robots-Tag`** header | merged, **not applied** |
| **Player-lockouts** Cloud Armor rule | merged, **Terraform not applied** |
| **Entry-path-recovery** Cloud Armor rule | merged, **not applied** |

Until applied, `app.spacestroker.com` deep links return **404** with the SPA shell, and
`spacestroker.com/<unknown>` returns the **LP homepage** with a 404 rather than the branded `404.html`.

## Key code touchpoints

**Backend (`monkify-backend`)** — `SpaceStroker.Api/Program.cs` (forwarded headers, CORS, rate-limit
middleware ordering), `SpaceStroker.Api/Controllers/RpcController.cs` + `SolanaController`,
`SpaceStroker.Consumers/Program.cs` (`AddPostgresMigrationHostedService`, the eight endpoints, Hangfire),
`SpaceStroker.Infrastructure/Services/Messaging/MessageBusPublisher.cs`,
`SpaceStroker.Common/spacestrokersettings.json` (`GeneralSettings:Cors`, `RealIpHeader`).

**Frontend (`monkify-front`)** — `src/hooks/useConnection.ts`, `src/utils/apiError.ts`,
`src/services/mainService.ts` (the `mainAPI` interceptor).

**Infra (`monkify-meta`)** — `deploy/terraform/{persistent,workload}`, `deploy/SpaceStroker.Deploy/`,
`tools/SpaceStroker.DbConsole/`, `deploy/README.md`, `deploy/docs/FIRST-DEV-DEPLOY.*.md`.

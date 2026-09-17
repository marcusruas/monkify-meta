# SpaceStroker — "Secret Manager versions pile up forever and bill for it" — Implementation Spec

> **Status: Shipped** — merged via `feature/prune-secret-versions` on **2026-09-17**.
> Repos affected: **`monkify-meta` only** (`deploy/SpaceStroker.Deploy/` + `deploy/README.md`).
> `monkify-backend` and `monkify-front` are untouched — no entity, handler, consumer, EF mapping,
> migration, endpoint, SignalR contract, composable or component.
> **Level: Minor change** — deploy-tooling cost bug. No backend integration test, no new
> `features/<slug>.md`; `conventions/backend.md` → "Meta-repo console tools" is corrected at ship time.
>
> ⚠️ **Written at ship time, not before implementation.** The work started from a billing question
> ("my secret manager cost 118.77 this month, it has 24 secrets, why?"), so the diagnosis came first
> and this spec records what was found and what shipped. The numbers below are measured, not
> estimated.
>
> **Purpose:** stop `spacestroker-deploy` from creating a Secret Manager version on every deploy
> whether or not the value changed, and trim the ~700 versions that had already accumulated.

---

## 1. Summary

Secret Manager bills the **"Secret version replica storage"** SKU **per enabled version per month** —
**R$0.352219615**, first 6 free — **not per secret**. `Deployer.SyncSecrets()` pushed a new version
for every key in `secrets.<env>.json` on every run and destroyed nothing, so versions only ever
accumulated.

Measured on 2026-09-17, project `project-ecdfb1b6-ed69-4e49-899`:

| | Value |
| --- | --- |
| Secrets | 24 (8 `spacestroker-dev-*`, 16 `spacestroker-prod-*`) |
| **Enabled versions** | **692** — none ever disabled or destroyed |
| Versions created | 329 in July · 231 in August · 132 in September (to the 17th) |
| Billed to 2026-09-17 | **R$118.77** — integrating live version-hours against the SKU rate reproduces the figure to within R$1 |
| Run rate | ~R$242/month, rising ~R$4–8 with **every** deploy, never falling |

**Access operations are not the lever.** The project made **1,776** Secret Manager API calls in 30
days against a **10,000/month** free tier — R$0.00. Versions were the entire bill.

Nothing could read the versions being paid for. Cloud Run mounts every secret with
`version = "latest"` (`terraform/workload/modules/cloud_run_api/main.tf`,
`.../cloud_run_consumers/main.tf`), which even a rolled-back revision re-resolves — so only the
newest enabled version is ever served, and the other ~660 were almost entirely byte-identical
duplicates of each other.

| Behaviour | Before | After this spec |
| --- | --- | --- |
| Value unchanged since the last deploy | New version pushed anyway | **No version created** — the secret is left alone |
| Old versions | Kept forever, all billing | **Destroyed past the newest 2** |
| Secrets that drifted out of `secrets.<env>.json` | Never touched again, bill forever | Swept by the new **`prune-secrets`** command |
| Steady-state cost | Unbounded, grows per deploy | **Flat** — 2 versions per secret |

## 2. Decisions

1. **Retention is 2, not 1** — the live value plus one rollback step. Keep-1 is cheaper (R$6.34 vs
   R$14.79/month at 24 secrets) but leaves `secrets.<env>.json` — git-ignored, on one workstation —
   as the only copy of a value a bad deploy overwrote. Keep-3 was rejected as paying for a case
   keep-2 already mostly covers.
2. **Skip the push when the value is unchanged** — this is the root cause, not the pruning. Without
   it the tool still writes 16 versions per deploy and the pruner merely destroys them again.
3. **Compare raw payload bytes, via `--out-file`** — not `--format="value(payload.data)"`, which
   **decodes and prints** the value and would put private keys in the deploy log. Bytes rather than
   text also make the comparison symmetric with how `versions add --data-file` wrote them, so no
   console encoding or line-ending rewrite can sit between the two.
4. **Comparison is exact, and fails toward pushing** — a wrong "changed" costs one version (the old
   behaviour); a wrong "unchanged" would silently leave production on a stale value. CRLF-vs-LF
   inside a PEM counts as a change, because it breaks key parsers.
5. **A disabled version never gets a retention slot** — it still bills (only *destroyed* stops the
   meter) while `latest` skips over it, so it can never be the value Cloud Run mounts.
6. **Pruning is non-fatal inside a deploy** — a permission error or API hiccup while trimming must
   not abort the release it is attached to. A failure costs storage, never availability.
7. **`prune-secrets` reads the live secret list from GCP**, not the keys in `secrets.<env>.json` — a
   key that drifts out of that file keeps billing forever and is exactly what the command is for.
8. **The 8 orphaned `spacestroker-dev-*` containers were deleted, not pruned.** No dev Cloud Run
   service exists; they were left behind by a teardown and billed ~R$67/month for an environment
   that does not run. Their values survive in `deploy/secrets.development.json` and
   `deploy --env development` recreates them.

## 3. Implementation

**`deploy/SpaceStroker.Deploy/SecretVersionPruning.cs`** (new) — a pure decision class, isolated
because every decision it makes is unrecoverable if wrong:

- `VersionsToKeep = 2`.
- `Parse(gcloudCsv)` — reads `--format="csv[no-heading](name,state)"` rows, strips `\r` (the tool runs
  on Windows, where every captured line is CRLF-terminated), and **throws** on a row it cannot parse
  rather than skipping it: a skipped row miscounts the retention window and destroys a version that
  should have been kept.
- `ToDestroy(versions, keep)` — the newest `keep` **enabled** versions are retained; everything else
  not already destroyed is returned **oldest first**. ⚠️ Ordering is **numeric**: gcloud prints
  version names as strings, and a text sort puts `"9"` above `"10"`, which would destroy the live
  value on any secret past version 9. Oldest-first means an interruption stops **short of** the
  retention window rather than inside it. `keep < 1` is refused.
- `IsAlreadyStored(value, storedPayload)` — byte comparison against `Encoding.UTF8.GetBytes(value)`.

**`deploy/SpaceStroker.Deploy/Deployer.cs`:**

- `SyncSecrets()` — reads the stored `latest` (`ReadLatestPayload`, to a temp file deleted in a
  `finally`), skips `versions add` when unchanged, then calls `PruneVersionsOf`. Prints
  `N new version(s), N unchanged, N old version(s) destroyed.`
- `PruneSecrets()` (new, public) — lists `spacestroker-<envCode>-*` from GCP and prunes each.
- `PruneVersionsOf(secretId, keep)` / `ListEnvironmentSecretIds()` / `ReadLatestPayload(secretId)` —
  private helpers.

**`deploy/SpaceStroker.Deploy/Program.cs`** — new `prune-secrets` command, added to the command
allow-list and the usage text.

## 4. Tests

`SpaceStroker.Deploy.Tests/SecretVersionPruningTests.cs` — **15 tests**, suite **91 → 106**. Every
test guards a way this could destroy the wrong version: numeric-vs-lexical ordering, oldest-first,
already-destroyed rows, the disabled-version rule, `keep < 1`, unparseable gcloud output, CRLF
tolerance, exact-match and non-ASCII round-tripping.

Also verified live against a throwaway `spacestroker-prob-*` environment holding **mock values only**
(created, exercised, deleted): first sync → v1; identical value → **no** new version; changed value →
v2; two more changes → retention trims to 2. This is what proved decision 3 — the first
implementation used `--format="value(payload.data)"`, and the probe showed it returns **decoded
plaintext**, not the base64 the API documents.

## 5. Rollout (done)

1. `prune-secrets --env production` — **468 versions destroyed across 16 secrets, 0 failures**.
2. The 8 `spacestroker-dev-*` containers deleted (192 versions).
3. Result: **692 → 32 enabled versions** (16 × 2). `/health` and `/games/active-types` returned 200
   after the sweep — expected, since the newest enabled version is never touched.
4. Cost: **~R$242/month → ~R$9.16/month** (26 billable × R$0.352219615), and flat from here.

## 6. Acceptance criteria

- [x] A deploy that changes no secret values creates **no** new versions.
- [x] A changed value still reaches Cloud Run on the next deploy.
- [x] No secret is left with more than 2 enabled versions after a deploy or a `prune-secrets` run.
- [x] The newest enabled version is never destroyed, on any code path.
- [x] No secret value is ever printed to stdout or a log.
- [x] `SpaceStroker.Deploy.Tests` covers the destroy-selection rules; whole suite green.

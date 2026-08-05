# Space Stroker — "Backfill Players in RemoveOperators" — Fix Spec

> **Status: fix implemented, verified locally, pending production deploy.**
> Repos affected: **`monkify-backend`** (one migration file — no C# behaviour change) and this doc in
> **`monkify-meta`**. **`monkify-front` needs no change** — the defect is entirely server-side.
> Branch / worktree: `feature/remove-operators-backfill` →
> `.worktrees/remove-operators-backfill/{monkify-backend,monkify-meta}`.
> Level: **production incident fix** (not a new feature — see §5 on the integration-test rule).

---

## 1. What happened

The 2026-08-05 production deploy of `main` failed. `spacestroker-prod-consumers` started, tried to
apply the 7 pending migrations, and `20260729190119_RemoveOperators` died on:

```
ALTER TABLE "GameBets" ADD CONSTRAINT "FK_GameBets_Players_PlayerId"
  FOREIGN KEY ("PlayerId") REFERENCES "Players" ("Id");
23503: insert or update on table "GameBets" violates foreign key constraint
```

Production was rolled back to the 2026-07-28 build (`api-00009-f2p`, `consumers-00006-x92`, front
commit `bcc7b5c`). See [[prod-deploy-config-namespace-incident]] in the agent memory for the rollback
recipe.

## 2. Root cause

`RemoveOperators` was authored **greenfield** — `INDEX.md` even labels it "Greenfield migration". Its
`Up()` does, in order:

1. `DropTable("OperatorPlayers")` — deletes every player row
2. `RenameColumn "OperatorPlayerId" -> "PlayerId"` on `GameBets` — the **old ids stay in the column**
3. `CreateTable("Players")` — brand new and **empty**
4. `AddForeignKey("FK_GameBets_Players_PlayerId")`

It never copies `OperatorPlayers` into `Players`. On an empty database there are no bets, so nothing
is orphaned and the FK holds. On production — which has real bet history — **every** row is orphaned
and step 4 cannot succeed.

**Nothing was lost.** Postgres DDL is transactional, so the failed migration rolled back whole; the
production schema stayed exactly where it was.

⚠️ **`ApplyMigrations` swallows its exception** (`Program.cs`, `catch { Log.Error(ex, "Failed to
ensure database is created.") }`). Startup continued past the failure and only died later on missing
columns, and the deploy tool still printed `Deploy complete.` and exited 0. **A green deploy does not
mean the migrations applied.**

## 3. The trap in the fix — the identity model narrowed

A naive `INSERT INTO "Players" SELECT ... FROM "OperatorPlayers"` is wrong:

| | Old `OperatorPlayers` | New `Players` |
| --- | --- | --- |
| Unique key | `(OperatorId, ExternalPlayerId)` | **`ExternalPlayerId` alone** (`IX_Players_ExternalPlayerId`) |

One wallet that played under **two operators** is two rows in the old model and may only ever be
**one** row in the new one. A straight copy violates the new unique index; de-duplicating without
repointing re-orphans the bets that referenced the discarded row.

## 4. The fix

All of it lives in `20260729190119_RemoveOperators.cs`. No model change, so the snapshot and the
Designer file are untouched, and no new migration is added.

1. **Stash before the drop.** Immediately before `DropTable("OperatorPlayers")`, copy the player rows
   into `"__RemoveOperators_PlayerBackup"`. A **real table, not a `TEMP` one** — temp tables are
   session-scoped and this has to survive however the migration's commands are dispatched.
2. **Replay after `Players` exists, before the FK.** Three statements:
   - `INSERT ... SELECT DISTINCT ON ("ExternalPlayerId") ... ORDER BY "ExternalPlayerId",
     "LastSeenAt" DESC, "Id"` — one row per wallet, **most recently seen wins**, original `Id`
     preserved so the majority of bets need no repointing at all.
   - `UPDATE "GameBets" ... SET "PlayerId" = p."Id"` for bets whose row lost the de-duplication.
   - A safety net that mints a `'migrated:<uuid>'` placeholder `Player` for any bet still pointing
     nowhere, so financial history is **preserved rather than deleted**. It cannot match while the old
     FK held; it exists because this migration is the one place that could silently destroy bet
     history.
   - `DROP TABLE "__RemoveOperators_PlayerBackup"`.

**Editing an already-shipped migration is deliberate and safe here.** Every other environment has it
recorded in `__EFMigrationsHistory` and will never re-run it; production has **not** applied it. Only
un-migrated databases see the new behaviour.

## 5. Verification

No unit test covers migration SQL and there is no harness that replays a migration against a
populated legacy schema, so this was verified by **reproducing the production failure locally** —
which is stronger evidence than a green suite:

1. Fresh Postgres, `dotnet ef database update 20260724030110_AddBetExclusiveChoice` (the exact
   production schema point).
2. Seed production-shaped data, **including the same `ExternalPlayerId` under two operators**, a
   free bet with a `NULL` player, and two `TransactionLogs` rows.
3. `dotnet ef database update` → **reproduced `23503` byte-for-byte**, same constraint name.
4. Apply the fix, reset, re-seed, re-run → **succeeds** through
   `20260805015728_AddQueryCoveringIndexes`.

Post-migration assertions, all green: `players=2` (duplicate collapsed to the most recent,
`AnnNew`); all 4 bets retained with **0 orphaned**; the bet that referenced the discarded duplicate
now resolves to `wallet-1`; the free bet keeps `NULL`; both transaction logs intact; scratch table
gone.

**Integration test: WAIVED.** `constraints.md` §3 mandates a happy-path integration test for every
**new feature**. This is a **bug fix to a migration** with no new behaviour and no endpoint; the
Testcontainers suite builds its schema by running migrations on an empty database, which is precisely
the case that never fails. The reproduction above is the meaningful test.

## 6. Deploy notes

- Take an on-demand backup first — **PITR is OFF** on `spacestroker-prod-pg`
  (`gcloud sql backups create --instance=spacestroker-prod-pg`).
- **Push both images before the Terraform apply.** `DeployAll` applies Terraform *first*, which with
  tag `:latest` would create a revision running **old code under new config**.
- The config namespace moved (`GeneralSettings:Token:*` → `GeneralSettings:Operator:Token:*`), so an
  image-only component deploy is **not** sufficient — the apply is what rewires the env.
- ⚠️ The apply **revokes IAM on the retired secrets**, which blocks any later revision rollback until
  the bindings are re-granted to both service accounts.
- The LP is already on the new build, so `spacestroker.com/terms/` currently states the **1%** house
  cut while the rolled-back backend still charges **0.5%**. This deploy closes that gap.

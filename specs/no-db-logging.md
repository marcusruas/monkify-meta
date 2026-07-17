# ChimpOut — "Make log not stored in DB" Feature — Implementation Spec

> **Status: Shipped** — merged to `main` on **2026-07-17** via `feature/no-db-logging`
> (`monkify-backend` + `monkify-meta`). 366 unit + 35 integration tests green (no unit tests changed,
> no new integration test — QOL/infra exemption). Distilled summary lives in `INDEX.md` → "Feature
> currently in flight" (last shipped) and `conventions/backend.md` (logging); no dedicated
> `features/` doc (QOL/infra change).
>
> **Original status: Ready for implementation** — refined via Trello card
> [Make log not stored in DB](https://trello.com/c/ZlNLBc5C) (2026-07-17).
> Repos affected: `monkify-backend`, and this doc in `monkify-meta` (**no `monkify-front`**).
>
> **Purpose:** Stop persisting the application's Serilog output into Postgres tables. Today both
> hosts write every log event to a per-host table in the game database via a Serilog PostgreSQL sink,
> which bloats `chimpout` and duplicates what the deploy platform already captures (Cloud Run →
> Cloud Logging in prod, the terminal locally). This is a backend-only infra/config cleanup — for
> ops/devs, not players; no game, bet, payment, or realtime behaviour changes.

---

## 1. Summary

Serilog is configured once in `ChimpOut.Infrastructure.DependencyInjection.AddLogs(builder, tableName)`
with two sinks: **Console** and **PostgreSQL**. The PostgreSQL sink auto-creates and writes to a
per-host table in the `ChimpOut` game database (`ChimpOutApiLogs` for the API host,
`ChimpOutConsumerLogs` for the Consumers host). We **remove the PostgreSQL sink** so logging is
**Console-only**, do a **full cleanup** of the now-dead code/package/parameter, and **drop the two
existing log tables** via an EF migration (applied by Consumers at startup).

| Aspect | Before | After |
| --- | --- | --- |
| Log sinks | Console **+** PostgreSQL (per-host table) | **Console only** (stdout) |
| Log storage | Rows in `ChimpOutApiLogs` / `ChimpOutConsumerLogs` in `chimpout` DB | None in DB; captured by Cloud Run/Cloud Logging (prod) or terminal (local) |
| `AddLogs` signature | `AddLogs(this IHostApplicationBuilder builder, string tableName)` | `AddLogs(this IHostApplicationBuilder builder)` |
| DB connection read for logging | Reads `ConnectionStrings:ChimpOut` inside `AddLogs` | Not read (logging never touches the DB) |
| `Serilog.Sinks.PostgreSQL` package | Referenced | Removed |
| Log levels | Information (non-prod) / Warning (prod) | **Unchanged** |
| Existing log tables | Present in provisioned DBs | Dropped by migration `DropLogTables` |

Log **content**, **levels**, and **format** are unchanged — only the *destination* changes (DB sink
removed).

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo / branch-per-repo:** work lives in `.worktrees/no-db-logging/<repo>`; hub stays
  on `main` (never `git checkout -b` in the hub). Branch `feature/no-db-logging` in `monkify-backend`
  and `monkify-meta` is the merge/PR ref.
- **Unit tests (§3):** update/add in `ChimpOut.Tests` and report changed tests at end of
  implementation. `AddLogs` lives in a `[ExcludeFromCodeCoverage]` class and has **no** existing unit
  test, so no unit-test change is expected — but the full suite must stay green. Run via
  `scripts/run_tests.bat` (not the full `dotnet test` — `ProductionSessions` is a slow Monte Carlo
  suite).
- **EF mapping (§4):** inline in `ChimpOutDbContext.OnModelCreating`; no `IEntityTypeConfiguration`.
  (N/A here — the log tables were never in the EF model; the migration is raw SQL and adds nothing to
  the model.)
- **Migrations (§4):** under `ChimpOut.Infrastructure/Migrations/`; applied **only** by
  `ChimpOut.Consumers` at startup (`ApplyMigrations` → `dbContext.Database.Migrate()`,
  `ChimpOut.Consumers/Program.cs:189-203`). Never hand-edit `ChimpOutDbContextModelSnapshot.cs`;
  generate the migration via `dotnet ef migrations add`.
- **No integration test required (§3 "Integration tests"):** this is an infra/QOL cleanup with **no
  game-flow change**, so the mandatory-happy-path-integration-test rule does not apply.
- **Secrets/config (§1):** mock-only values in committed files (no config change is needed here).
- **Hosts (§8):** API stays stateless and does not migrate; Consumers owns migrations. Unchanged.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **Console only.** Keep only the Serilog `WriteTo.Console()` sink; remove `WriteTo.PostgreSQL(...)`. Serilog itself stays. |
| 2 | **Log levels unchanged.** Keep `MinimumLevel.Information()` in non-production and `MinimumLevel.Warning()` in production. |
| 3 | **Full code cleanup.** Remove the `Serilog.Sinks.PostgreSQL` PackageReference, the `using Serilog.Sinks.PostgreSQL;`, the `logsConnectionString` read, and the `tableName` parameter of `AddLogs`; update both call sites to `builder.AddLogs()`. |
| 4 | **Drop existing tables via EF migration.** New migration `DropLogTables` runs raw SQL `DROP TABLE IF EXISTS "ChimpOutApiLogs"` and `DROP TABLE IF EXISTS "ChimpOutConsumerLogs"`. `Down()` is a **no-op** (tables aren't reproducible from the model and the sink no longer exists to recreate them). `IF EXISTS` keeps it safe on a fresh prod DB and idempotent on re-run. |
| 5 | **Empty model diff.** Because the log tables were never in the EF model, `dotnet ef migrations add DropLogTables` produces an empty `Up`/`Down`; the DROP SQL is added **by hand**, and `ChimpOutDbContextModelSnapshot.cs` does **not** change. |
| 6 | **Scope.** Backend-only (`monkify-backend`). No frontend, no config, no game/bet/payment/realtime behaviour change. |

---

## 4. Data model

### 4.1 Entity / column changes

None. No entity, DTO, enum, or column is added or changed. The log tables
(`ChimpOutApiLogs`, `ChimpOutConsumerLogs`) were created at runtime by the Serilog PostgreSQL sink
(`needAutoCreateTable: true`) and were **never** mapped in `ChimpOutDbContext`.

### 4.2 EF configuration (inline)

None. `OnModelCreating` is untouched (the log tables are not in the model).

### 4.3 Migration

- **Name:** `DropLogTables`
- **Generate (from the backend worktree):**

  ```bash
  cd E:/Monkify/.worktrees/no-db-logging/monkify-backend/src
  dotnet ef migrations add DropLogTables \
    --project ChimpOut.Infrastructure \
    --startup-project ChimpOut.Consumers \
    --output-dir Migrations
  ```

  (Match how the existing `InitialCreate` migration was generated — startup project is
  `ChimpOut.Consumers`, which owns the DbContext at design time.)
- **Hand-edit `Up()`** to add the raw SQL (the generated `Up`/`Down` are empty because nothing in the
  model changed):

  ```csharp
  public partial class DropLogTables : Migration
  {
      /// <inheritdoc />
      protected override void Up(MigrationBuilder migrationBuilder)
      {
          // The app's Serilog logs are no longer stored in the database (DB sink removed).
          // These tables were created at runtime by Serilog.Sinks.PostgreSQL (needAutoCreateTable),
          // never by the EF model, so drop them with raw SQL. IF EXISTS => safe on a fresh DB and
          // idempotent on re-run.
          migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""ChimpOutApiLogs"";");
          migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""ChimpOutConsumerLogs"";");
      }

      /// <inheritdoc />
      protected override void Down(MigrationBuilder migrationBuilder)
      {
          // No-op: the log tables were sink-managed and are not reproducible from the model, and the
          // sink that created them has been removed. Nothing to recreate on rollback.
      }
  }
  ```

- **Do not** hand-edit `ChimpOutDbContextModelSnapshot.cs` — it must remain unchanged (verify the
  `dotnet ef migrations add` run left it untouched; if the tool touched it, revert that file so the
  snapshot diff is empty).

### 4.4 ER diagram

N/A — no model change.

---

## 5. Configuration

None. No `GeneralSettings`, `chimpoutsettings.json`, env var, or seed-data change. The
`ConnectionStrings:ChimpOut` value stays (it is still used by EF/Hangfire/game DB access) — it is
simply no longer read inside `AddLogs`.

---

## 6. Backend behaviour

### 6.1 `AddLogs` — remove the PostgreSQL sink (`ChimpOut.Infrastructure/DependencyInjection.cs`)

Current (lines ~119-144):

```csharp
public static void AddLogs(this IHostApplicationBuilder builder, string tableName)
{
    var logBuilder = new LoggerConfiguration();

    if (!builder.Environment.IsProduction())
        logBuilder = logBuilder.MinimumLevel.Information();
    else
        logBuilder = logBuilder.MinimumLevel.Warning();

    // Console sink
    logBuilder = logBuilder.WriteTo.Console();

    // PostgreSQL sink (basic overload with auto-create)
    var logsConnectionString = builder.Configuration.GetConnectionString("ChimpOut");
    if (!string.IsNullOrWhiteSpace(logsConnectionString))
    {
        logBuilder = logBuilder.WriteTo.PostgreSQL(
            connectionString: logsConnectionString,
            tableName: tableName,
            needAutoCreateTable: true
        );
    }

    Log.Logger = logBuilder.CreateLogger();
    builder.Logging.AddSerilog(Log.Logger);
}
```

Target:

```csharp
public static void AddLogs(this IHostApplicationBuilder builder)
{
    var logBuilder = new LoggerConfiguration();

    if (!builder.Environment.IsProduction())
        logBuilder = logBuilder.MinimumLevel.Information();
    else
        logBuilder = logBuilder.MinimumLevel.Warning();

    // Console sink only — the app no longer persists logs to the database.
    logBuilder = logBuilder.WriteTo.Console();

    Log.Logger = logBuilder.CreateLogger();
    builder.Logging.AddSerilog(Log.Logger);
}
```

Also remove the now-unused `using Serilog.Sinks.PostgreSQL;` at the top of the file (line ~34). Keep
`using Serilog;`.

### 6.2 Call sites

- `ChimpOut.Api/Program.cs:74` — `builder.AddLogs("ChimpOutApiLogs");` → `builder.AddLogs();`
- `ChimpOut.Consumers/Program.cs:38` — `builder.AddLogs("ChimpOutConsumerLogs");` → `builder.AddLogs();`

### 6.3 Package reference

- `ChimpOut.Infrastructure/ChimpOut.Infrastructure.csproj:38` — remove
  `<PackageReference Include="Serilog.Sinks.PostgreSQL" Version="2.3.0" />`.
- Keep `Serilog.Extensions.Logging` and `Serilog.Sinks.Console`. (`Serilog` core is referenced via
  `ChimpOut.Common`; leave it.)

### 6.4 Migration applied by Consumers

No code change to `ApplyMigrations` — the new `DropLogTables` migration is picked up automatically by
the existing `dbContext.Database.Migrate()` at Consumers startup
(`ChimpOut.Consumers/Program.cs:189-203`). The API host does not migrate (unchanged).

### 6.5 Integration test harness — no change

`ChimpOut.Tests.Integration/Harness/IntegrationTestFixture.cs:115-122` **replaces** the global
`Log.Logger` with an in-memory + console logger **after** both hosts are built (i.e. after `AddLogs`
has run). It never depended on the PostgreSQL sink, so it keeps working unchanged. Do not modify it.

---

## 7. Frontend behaviour

None. `monkify-front` is not touched (no API/DTO/SignalR/UI/env change).

---

## 8. Flow diagrams

Not needed — a single-path config change with no new flow. (Before: log event → Console + Postgres
table. After: log event → Console only.)

---

## 9. Out of scope

- Any change to log **content**, **levels**, or structured-logging **format**.
- Adding a **file sink** or any external log-aggregation sink (Console/stdout is the target;
  Cloud Run captures it in prod).
- Making sinks **configurable via settings** (YAGNI for this card).
- Frontend logging.
- Removing `Serilog` itself or the Console sink.

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/ChimpOut.Infrastructure/DependencyInjection.cs` | Remove the PostgreSQL sink block + `logsConnectionString` read; drop the `tableName` parameter from `AddLogs`; remove `using Serilog.Sinks.PostgreSQL;`. Console sink + level logic stay. |
| 2 | `src/ChimpOut.Api/Program.cs` | Line 74: `builder.AddLogs("ChimpOutApiLogs");` → `builder.AddLogs();`. |
| 3 | `src/ChimpOut.Consumers/Program.cs` | Line 38: `builder.AddLogs("ChimpOutConsumerLogs");` → `builder.AddLogs();`. |
| 4 | `src/ChimpOut.Infrastructure/ChimpOut.Infrastructure.csproj` | Remove the `Serilog.Sinks.PostgreSQL` PackageReference (line 38). |
| 5 | `src/ChimpOut.Infrastructure/Migrations/<timestamp>_DropLogTables.cs` (+ `.Designer.cs`) | **New** migration: raw-SQL `DROP TABLE IF EXISTS` for both log tables; no-op `Down`. Generated via `dotnet ef migrations add DropLogTables`, `Up()` hand-edited. |
| 6 | `src/ChimpOut.Infrastructure/Migrations/ChimpOutDbContextModelSnapshot.cs` | **Must stay unchanged** — verify no diff after generating the migration (revert if the tool touched it). |

### 10.2 Frontend (`monkify-front`)

None.

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/no-db-logging.md` | This spec (already created on the branch). |
| 2 | `INDEX.md` | Add the spec row (status **Ready**) + update "Feature currently in flight". |

---

## 11. Unit tests

Project: `monkify-backend/src/ChimpOut.Tests`.

| Test file | Test cases | Expected |
| --- | --- | --- |
| — | No new/changed unit test expected. `AddLogs` is in a `[ExcludeFromCodeCoverage]` class with no existing coverage; changing its sinks/signature does not break any test (grep confirms no test references `AddLogs`). | Full `ChimpOut.Tests` suite stays green. |

**Verification instead of a new test:** run `scripts/run_tests.bat` and confirm green; run
`scripts/run_integration_tests.bat` to confirm the harness (which swaps `Log.Logger` post-build) is
unaffected. Report at the end that **no unit tests changed** (or list any that did).

> Rationale for no new test: there is no meaningful, stable unit under test for "Serilog wrote to the
> console" (asserting sink registration on a static `Log.Logger` is brittle and low value). The
> migration's effect is covered by Manual QA §13 and the acceptance criteria.

---

## 12. Acceptance criteria

Mapped 1:1 from the Trello **Acceptance Criteria** checklist:

1. Given the API or Consumers host is running, when it emits a log at or above the active minimum
   level, then the log line appears on the Console (stdout) and no row is written to any database
   table.
2. Given the Serilog configuration in `AddLogs`, when the app starts in any environment, then no
   Serilog PostgreSQL sink is registered and the `ChimpOut` connection string is not read for logging
   purposes.
3. Given the code cleanup, when reviewing the solution, then the `Serilog.Sinks.PostgreSQL`
   PackageReference is gone, `AddLogs` takes no `tableName` parameter, and both call sites
   (`ChimpOut.Api` and `ChimpOut.Consumers`) call `builder.AddLogs()` with no argument.
4. Given a database that still has the `ChimpOutApiLogs` and `ChimpOutConsumerLogs` tables, when
   `ChimpOut.Consumers` runs the new migration at startup, then both tables are dropped and the two
   hosts start cleanly.
5. Given a database where the log tables were never created (fresh prod DB or already-cleaned env),
   when the migration runs, then it completes without error (`DROP TABLE IF EXISTS` is a no-op) and is
   safe to re-run.
6. Given the change is backend-logging-only, when `scripts/run_tests.bat` is run, then the full
   unit-test suite is green and the report states which unit tests changed (expected: none).
7. Given the integration test harness swaps `Log.Logger` for an in-memory sink after host build, when
   the existing `ChimpOut.Tests.Integration` suite runs, then it stays green with no changes needed to
   the harness.

---

## 13. Manual QA (optional)

1. From `.worktrees/no-db-logging/monkify-backend/src`, apply the change and start the local stack
   (or run Consumers against a local Postgres).
2. **Existing tables dropped:** on a DB that already had the log tables, confirm after Consumers
   startup that `ChimpOutApiLogs` and `ChimpOutConsumerLogs` no longer exist
   (`\dt` in `psql`, or `SELECT to_regclass('"ChimpOutApiLogs"');` returns `NULL`).
3. **No new rows / tables:** exercise the API (place a bet / hit an endpoint), then confirm the log
   tables are **not** re-created and no log rows land in the DB. Logs appear in the **console**.
4. **Fresh DB:** drop the DB, start Consumers, confirm the `DropLogTables` migration runs without
   error (no-op) and startup succeeds.
5. Run `scripts/run_tests.bat` and `scripts/run_integration_tests.bat` — both green.

---

## 14. Trello / pipeline

- **Source card:** [Make log not stored in DB (#68)](https://trello.com/c/ZlNLBc5C) — list
  **Specs created**; the **Implementation** section on the card lists the branch + spec + worktree.
- **Next:** `/monkify-implement-feature Make log not stored in DB` to build it, then
  `/monkify-ship-feature Make log not stored in DB` to sync the KB and merge `feature/no-db-logging`
  → `main` (reads branch/spec from the card).

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-07-17 | Initial spec from the refined card #68 (refined earlier the same day). |

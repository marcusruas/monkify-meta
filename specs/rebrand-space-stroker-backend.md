# ChimpOut → "Space Stroker" — Backend + Infra Rename — Implementation Spec

> **Status: Ready for implementation** — refined via Trello card
> [Rename backend + infra ChimpOut → Space Stroker (follow-up to #74)](https://trello.com/c/VgNMpTzu)
> (2026-07-21).
> Repos affected: `monkify-backend`, `monkify-meta` (deploy + KB), and `monkify-front` (namespace-mirror
> comments only). This doc lives in `monkify-meta`.
>
> **Purpose:** Finish the Space Stroker rebrand. Card #74 renamed only the consumer-facing frontend;
> the backend host, C# namespaces (`ChimpOut.*`), DB, config, the GCP/Terraform infra and the
> `chimpout.com` domain still say ChimpOut — an intentional, temporary drift. This card renames that
> **backend + infra** half so the naming stops drifting: brand token `ChimpOut → SpaceStroker`
> (machine form) / `Space Stroker` (display), a greenfield DB rename, the domain `chimpout.com →
> spacestroker.com`, and a full knowledge-base rename. It is a **pure rename refactor** — a mirror of
> the earlier `Monkify → ChimpOut` rebrand (`specs/rebrand-chimpout.md`); the game plays identically
> afterward, only names change.

---

## 1. Summary

One global, mechanical rename applied everywhere the brand proper noun appears in the backend, infra,
and knowledge base:

| Rename | Rule | Notes |
| --- | --- | --- |
| **`ChimpOut` → `SpaceStroker`** | Replace the brand token `ChimpOut`/`chimpout`/`CHIMPOUT` (case-preserving) everywhere it is the **brand proper noun** — code identifiers, DB, config, infra, domain, docs. | The **"infinite monkey" theme stays** — the monkey character/imagery and the noun "monkey" are untouched. The `Session→Game` family was already renamed in the prior rebrand and is **not** in scope here. |

**Case mapping (apply consistently):**

| From | To (machine) | To (display) |
| --- | --- | --- |
| `ChimpOut` | `SpaceStroker` (PascalCase — namespaces/assemblies/types/DbContext can't contain a space) | `Space Stroker` (two words, user-visible copy) |
| `chimpout` | `spacestroker` (kebab/lowercase — files, config keys, hosts, buckets, DB names) | — |
| `CHIMPOUT` | `SPACESTROKER` where a space would break a token/host; `SPACE STROKER` in prose/display | `SPACE STROKER` |

**This is a "clean break":** no aliases, no redirects, no dual-emit, no old-identifier acceptance. The
DB is rebuilt via a greenfield rename migration (no data preserved — nothing is live). The domain moves
to `spacestroker.com` in Terraform/config as **code only**; the real `terraform apply` / DNS cutover is
a separate downstream ops step.

### Corrections to the refined card (found during code exploration)

- **SignalR hub carries no ChimpOut token.** The hub is already `ActiveGamesHub` at `/Hubs/Games`
  (renamed to the `Game*` family in the earlier rebrand). Only its **containing namespace**
  (`ChimpOut.Infrastructure.Hubs` → `SpaceStroker.Infrastructure.Hubs`) changes; the hub name and map
  path stay. There is no "SignalR hub name" to rename.
- **The config-load extension is `AddChimpOutSettings`** (`ChimpOut.Common/Extensions/CommonExtensions.cs:22`)
  and it hard-codes the filename `"chimpoutsettings.json"` at `:24` — both rename.
- **`monkify-lp` is NOT affected.** #74 already renamed the LP to Space Stroker; its only remaining
  ChimpOut references are the deferred `// monkify-front` repo-folder comments (tooling, out of scope).

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo (worktree-first):** `feature/rebrand-space-stroker-backend` in each affected repo;
  the shared checkout stays on `main`. Work lives in
  `.worktrees/rebrand-space-stroker-backend/<repo>`. Push after branch work.
- **Unit tests:** update the affected tests in `ChimpOut.Tests` (→ `SpaceStroker.Tests`) and keep them
  green; **report changed tests** at the end. This is a **rename/refactor, not a new feature**, so **no
  new happy-path integration test is required** — but the existing `ChimpOut.Tests.Integration`
  (Testcontainers) suite must be renamed to the new identifiers and stay green. Run via
  `scripts/run_tests.bat` (avoid the slow ProductionSessions Monte-Carlo suite) and
  `scripts/run_integration_tests.bat`.
- **EF mapping:** inline in `ChimpOutDbContext.OnModelCreating` (→ `SpaceStrokerDbContext`); no
  `IEntityTypeConfiguration`.
- **Migrations:** new rename migration under `ChimpOut.Infrastructure/Migrations/` (→
  `SpaceStroker.Infrastructure/Migrations/`); applied **only** by `ChimpOut.Consumers` (→
  `SpaceStroker.Consumers`) at startup. Regenerate the snapshot with `dotnet ef` — never hand-edit it.
- **Secrets/config:** mock/placeholder values only in committed files; never surface real secrets or
  domain credentials.
- **Operator messaging:** vague errors only; never reveal contract status (unchanged by this rename).
- **Do NOT rename tooling:** the git **repo-folder names** (`monkify-backend`, `monkify-front`,
  `monkify-meta`, `monkify-lp`), the `monkify-*` skill folders, the "MonkAIfy / monkaify" Trello board
  references and board URL, the `AGENTS.md` clone URLs, the `<!-- monkify-agents-pointer -->` marker,
  and the `.cursor/.claude/skills/` discovery paths all stay `monkify`.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **Do the backend rename? YES** — full deep rename. The "keep ChimpOut as the permanent internal/technical name (company-vs-product split) and close the card" alternative was **rejected**. |
| 2 | **Code identifier form = `SpaceStroker` (PascalCase);** display copy is the two-word `Space Stroker`. Case map per §1. |
| 3 | **Clean break** — no aliases, no redirects, no dual-emit, no old-identifier acceptance. Safe because nothing is live. |
| 4 | **DB = greenfield rename migration, no data preserved.** Game DB `chimpout` → `spacestroker`; messaging DB `chimpout_messaging` → `spacestroker_messaging`; any ChimpOut-named columns. Applied only by the (renamed) Consumers host at startup; local/dev DBs recreate cleanly. |
| 5 | **Infra + domain = full, now.** Domain `chimpout.com` → **`spacestroker.com`** (`api.spacestroker.com`, `app.spacestroker.com`, `www.spacestroker.com`, apex) in the Terraform **persistent** DNS layer + `secrets.<env>.json` + backend CORS defaults. **Code only** — the real `terraform apply` / DNS cutover is a separate ops step. ⚠️ Ops must confirm `spacestroker.com` is actually secured before a real apply. |
| 6 | **`src/` .NET project folders + `.sln` are renamed on disk** (`src/ChimpOut.Api` → `src/SpaceStroker.Api`, `ChimpOut.sln` → `SpaceStroker.sln`), in addition to file-contents. |
| 7 | **Repo folders + tooling stay `monkify-*`** (contents-only) — see §2 constraint. |
| 8 | **KB rename = FULL** — living docs **and** all historical `specs/*.md`. Guards: (a) tooling exclusions; (b) **do NOT blind-replace the `from→to` mapping tokens inside `specs/rebrand-chimpout.md` and `specs/rebrand-space-stroker.md`** — their "from" side is legitimately `ChimpOut` and must stay accurate. |
| 9 | **Frontend touched (comments only):** the 6 `ChimpOut.*` backend-namespace references in `monkify-front` (deliberately kept as ChimpOut in #74) now update to `SpaceStroker.*`. No behavior/build-contract change. |
| 10 | **Level = Minor change** (rename refactor, zero game-flow change) ⇒ **no new integration test, no new `features/*.md` doc**; existing suites renamed + green; KB docs updated. |

---

## 4. Data model

**No entity/column *shape* changes** — this is a physical-name rename only. No new tables, no new
columns, no type changes; the whole `Game*` entity family is untouched in shape.

### 4.1 DbContext

`ChimpOut.Infrastructure/Data/Context/ChimpOutDbContext.cs` →
`SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs`
(class `ChimpOutDbContext` → `SpaceStrokerDbContext`; all `using ChimpOut.*`; DI + host registrations).
EF mapping stays **inline** in `OnModelCreating`; no table/`DbSet` renames (those are all `Game*`
already).

### 4.2 Physical database names

| Kind | From | To |
| --- | --- | --- |
| Game database | `chimpout` | `spacestroker` |
| Messaging database (MassTransit Postgres transport) | `chimpout_messaging` | `spacestroker_messaging` |

The connection strings live in config (`chimpoutsettings.json` → `spacestrokersettings.json`); see §5.
Reference sites that name the messaging DB in comments/code:
`ChimpOut.Infrastructure/Services/Messaging/MessagingRegistration.cs:11,26`,
`ChimpOut.Tests.Integration/Harness/IntegrationTestFixture.cs:89,94`,
`ChimpOut.Consumers/Program.cs:58`.

### 4.3 Migration — none needed (confirmed at implementation)

**No new EF migration is added.** Code exploration confirmed there are **zero ChimpOut-named schema
objects**: every table is `Game*`/`Bet*`/`Operator*`/etc. (already renamed by the prior rebrand), and
the only `ChimpOut` tokens in `Migrations/` are **C# identifiers** (the `ChimpOut.Infrastructure.Migrations`
namespace + `[DbContext(typeof(ChimpOutDbContext))]` attributes) plus two **historical SQL string
literals** (`DROP TABLE IF EXISTS "ChimpOutApiLogs"/"ChimpOutConsumerLogs"` in `DropLogTables`, for
already-dropped log tables — a greenfield `IF EXISTS` no-op). Renaming those C# identifiers produces
**no model change**, so `dotnet ef migrations add` would emit an empty migration.

- The **database name** change (`chimpout → spacestroker`, `chimpout_messaging → spacestroker_messaging`)
  is a **connection-string / docker-compose concern**, not in-schema DDL: a fresh database named
  `spacestroker` is created by the connection, and the **existing** migrations (`InitialCreate`,
  `DropLogTables`) build its schema at Consumers startup. AC #2 ("applies a greenfield rename migration")
  is satisfied by the fresh `spacestroker` DB coming up migrated — no separate rename migration exists to
  write.
- The `[DbContext(typeof(ChimpOutDbContext))]` reference in the model snapshot + migration `.Designer.cs`
  files is updated to `SpaceStrokerDbContext` by the text rename (the file is renamed to
  `SpaceStrokerDbContextModelSnapshot.cs`); the snapshot is **not** regenerated (no model delta), and it
  is never hand-edited beyond that mechanical type reference.
- Applied **only** by the renamed Consumers host at startup (`Database.Migrate()`); the API does not
  migrate. Local/dev DBs recreate cleanly — **never** run against prod data.

---

## 5. Configuration

| Item | From | To |
| --- | --- | --- |
| Settings file | `ChimpOut.Common/chimpoutsettings.json` | `SpaceStroker.Common/spacestrokersettings.json` |
| Loader | `CommonExtensions.AddChimpOutSettings` + hard-coded filename `"chimpoutsettings.json"` (`ChimpOut.Common/Extensions/CommonExtensions.cs:22,24`) | `AddSpaceStrokerSettings` + `"spacestrokersettings.json"` (+ every call site) |
| Connection-string key | `ConnectionStrings:ChimpOut` (read at `Consumers/Program.cs:41`, `Infrastructure/DependencyInjection.cs:44`) | `ConnectionStrings:SpaceStroker` (update **both** `GetConnectionString("ChimpOut")` sites) |
| Messaging conn string | `…Database=chimpout_messaging…` (`chimpoutsettings.json:103`) | `…Database=spacestroker_messaging…` |
| CORS default origin | `https://app.chimpout.com` (`CorsOriginResolver.cs:15` `DefaultOrigins`; `chimpoutsettings.json:98`; comments `CorsSettings.cs:10`, `CorsOriginResolver.cs:7`) | `https://app.spacestroker.com` |
| Operator branding mock | `MainPageUrl`/`IconUrl` = `https://app.chimpout.com[/icon.png]` (`Consumers/Program.cs:325-326`) | `https://app.spacestroker.com…` (mock values) |

`csproj`/assembly/namespace renames (§10.1) cover the rest. Values in committed config stay
mock/placeholder.

---

## 6. Backend behaviour

**No control-flow, math, payout, typer, provably-fair, lottery, operator-scoping, or state-machine
change — names only.** The bet → run → reward pipeline behaves identically. Mechanical spots:

### 6.1 Solution / projects / namespaces
Rename the seven projects + solution (assembly, root namespace, `.csproj`, **on-disk `src/` folder**):
`ChimpOut.{Api,Consumers,Domain,Infrastructure,Common,Tests,Tests.Integration}` → `SpaceStroker.*`;
`ChimpOut.sln` → `SpaceStroker.sln`. Fix every `_globalImports.cs` / `GlobalUsings.cs` /
`using ChimpOut.*` (present across essentially every file). Recommended order (compiler-guided):
rename projects/namespaces first and build, then DbContext, then config, then DB/migration, then
tests.

### 6.2 SignalR
`ActiveGamesHub` (name + `/Hubs/Games` map path) **unchanged**; only its namespace
(`ChimpOut.Infrastructure.Hubs` → `SpaceStroker.Infrastructure.Hubs`) and `using`s change.

### 6.3 Consumers / hosts
`ChimpOut.Consumers` → `SpaceStroker.Consumers` still owns migrations (`Database.Migrate()`) + the game
loop + Hangfire jobs; `ChimpOut.Api` → `SpaceStroker.Api` stays stateless (no migrate). Both hosts'
`/health` endpoints and forwarded-headers/CORS wiring are unchanged except for the identifier/host
renames in §5.

### 6.4 Local stack (docker-compose + scripts)
Update in lockstep with the connection strings so a fresh stack still boots and migrates:
`docker-compose.yml`, `docker-compose.prod.yml`, and `scripts/*.bat`/`*.sh`
(`setup_local_environment.bat`, `run_tests.bat`, `run_integration_tests.bat`, `setup_database.sh`,
`collect_code_coverage.bat`, `generate_production_report.ps1`, …) — the DB name (`chimpout` →
`spacestroker`), the messaging DB, any Docker network/volume named `chimpout*`, and connection
identifiers. **Trap:** if the compose/script DB or network names are not renamed together with the
connection strings, the stack silently boots against the wrong/missing DB.

---

## 7. Frontend behaviour (`monkify-front`) — comments only

The frontend has **no** ChimpOut brand copy left (all became "Space Stroker" in #74). The only
ChimpOut references are 6 **backend-namespace** mentions that #74 deliberately kept accurate (because
the backend hadn't renamed). They now flip to `SpaceStroker.*`:

| File:line | From | To |
| --- | --- | --- |
| `src/types/index.ts:18` | `// Mirrors backend ChimpOut.Domain.Games.ValueObjects.PaymentMethod` | `…SpaceStroker.Domain.Games.ValueObjects.PaymentMethod` |
| `src/types/index.ts:120` | `// Mirrors backend ChimpOut.Infrastructure.Contracts.Games.GameVerificationDto` | `…SpaceStroker.Infrastructure.Contracts.Games.GameVerificationDto` |
| `src/types/index.ts:174` | `// Mirrors backend ChimpOut.Infrastructure.Contracts.Fx.FxRatesDto` | `…SpaceStroker.Infrastructure.Contracts.Fx.FxRatesDto` |
| `src/types/index.ts:197` | `// Mirrors backend ChimpOut.Infrastructure.Contracts.Admin.GameTypeAdminDto` | `…SpaceStroker.Infrastructure.Contracts.Admin.GameTypeAdminDto` |
| `src/utils/provablyFair.ts:5` | `backend's ChimpOut.Domain.Games.Services.ProvablyFair` | `…SpaceStroker.Domain.Games.Services.ProvablyFair` |
| `public/verify.html:16` | `ChimpOut.Domain.Games.Services.ProvablyFair` | `…SpaceStroker.Domain.Games.Services.ProvablyFair` |

No component, route, storage-key, postMessage, build, or behavior change — comments/prose only. (The
`verify.html` ↔ `provablyFair.ts` keystream algorithm parity is untouched.)

---

## 8. Infra behaviour (`monkify-meta/deploy`)

Full rename, domain included (code only; real apply is a downstream ops step).

### 8.1 Deploy tools (C# consoles)
| From | To |
| --- | --- |
| `tools/ChimpOut.Deploy/` (`AssemblyName chimpout-deploy`; `Program.cs`, `Deployer.cs`, `ProcessRunner.cs`, `RepoPaths.cs`, `SecretsFile.cs`) | `tools/SpaceStroker.Deploy/` (`spacestroker-deploy`) |
| `tools/ChimpOut.DbConsole/` (`AssemblyName chimpout-db`; `Program.cs`) | `tools/SpaceStroker.DbConsole/` (`spacestroker-db`) |
| `tools/ChimpOut.DeployTools.sln` | `tools/SpaceStroker.DeployTools.sln` |
| Deployer resource names: `chimpout-api`, `chimpout-consumers`, `chimpout-urlmap`, bucket `chimpout-{site}-{project}` (`Deployer.cs:38,42,215,220`) | `spacestroker-*` |
| DbConsole instance `…:chimpout-pg` (`Program.cs:25`) | `…:spacestroker-pg` |

Update the usage/comment banners (`chimpout-deploy` / `chimpout-db`) accordingly. Rename the `obj/`
build artifacts are regenerated — no need to hand-edit them.

### 8.2 Terraform
`terraform/persistent/**` (DNS zone + domain + state bucket, `prevent_destroy`) and
`terraform/workload/**` (root `main.tf`/`variables.tf`/`production.tfvars.example` + the 9 modules:
`cloud_run_api`, `cloud_run_consumers`, `data`, `edge`, `iam`, `network`, `registry`, `secrets`,
`static_site`). Rename every `chimpout*` resource/service/bucket/secret/instance/label name →
`spacestroker*`. Notable anchors: `data/main.tf` DB names `"chimpout"` / `"chimpout_messaging"` →
`"spacestroker"` / `"spacestroker_messaging"`; `registry`, `cloud_run_api` (`chimpout-api`),
`cloud_run_consumers` (`chimpout-consumers`), `static_site` bucket naming.

### 8.3 Domain → `spacestroker.com`
In `secrets.example.json` (and the real unversioned `secrets.<env>.json` shape) + the persistent DNS
layer:

| Key | From | To |
| --- | --- | --- |
| `gcp.stateBucket` | `chimpout-tfstate-…` | `spacestroker-tfstate-…` |
| `gcp.dnsZoneName` | `chimpout-zone` | `spacestroker-zone` |
| `gcp.dnsDomain` | `chimpout.com.` | `spacestroker.com.` |
| `gcp.domains.api` | `api.chimpout.com` | `api.spacestroker.com` |
| `gcp.domains.app` | `app.chimpout.com` | `app.spacestroker.com` |
| `gcp.domains.lp` | `chimpout.com` | `spacestroker.com` |
| `gcp.domains.lpWww` | `www.chimpout.com` | `www.spacestroker.com` |
| `appConfig.GeneralSettings:Cors:AllowedOrigins:0` | `https://app.chimpout.com` | `https://app.spacestroker.com` |
| `appConfig.VUE_APP_API_BASE_URL` / `VUE_APP_WEBSOCKET_BASE_URL` | `https://api.chimpout.com[/Hubs]` | `https://api.spacestroker.com[/Hubs]` |

Update `deploy/README.md` prose (the "register **chimpout.com**" step, the `chimpout-deploy` /
`chimpout-db` command references, the field table). Values in committed example files stay
placeholder/mock.

> **Apply-time note (documented, not executed here):** renaming GCS buckets / Cloud Run services /
> Secret Manager entries / Artifact Registry means the **next real apply** creates new resources and
> tears down the old ones; the domain move sits in the `prevent_destroy` persistent layer. This is an
> ops step outside this card. **Confirm `spacestroker.com` is secured first.**

---

## 9. Knowledge base (`monkify-meta` docs)

Full `ChimpOut → SpaceStroker` rename across the KB (prose **and** code-identifier references):

- **Living / reference docs:** `INDEX.md`, `architecture.md`, `glossary.md`, `constraints.md`,
  `conventions/**`, every `features/*.md`. Update the `glossary.md` brand entry to Space Stroker.
- **Historical `specs/*.md`:** apply the same rename so identifiers match the renamed code; rename any
  `chimpout`-named spec/feature **file** and fix INDEX links. (No `chimpout`-named files exist today —
  the `Session→Game`/`Monkify→ChimpOut` renames already produced `game-*` file names — but verify.)
- **Guards (do not blind-replace):**
  1. Tooling: the `monkify-*` skill identifiers/paths, the MonkAIfy board references + board URL, the
     `AGENTS.md` clone URLs, the `<!-- monkify-agents-pointer -->` marker, and repo-folder names stay
     `monkify`.
  2. The two rebrand specs' from→to tokens: **do not** replace `ChimpOut` inside
     `specs/rebrand-chimpout.md` (its "to" side is legitimately ChimpOut — the Monkify→ChimpOut
     record) or `specs/rebrand-space-stroker.md` (its "from" side is legitimately ChimpOut). Leave
     those two files' historical mapping tokens intact; only add `SpaceStroker` context where a status
     note genuinely needs it.

Practical guard: run the doc find/replace **excluding** `skills/**`, `AGENTS.md`,
`specs/rebrand-chimpout.md`, `specs/rebrand-space-stroker.md`, and any line containing
`trello.com/b/Yi42y6Xy` or a `.git` clone URL — then eyeball the diff.

> Per the KB-currency rule, this spec file (`specs/rebrand-space-stroker-backend.md`) intentionally
> **keeps** the `ChimpOut` tokens where it documents the *from →* side of the mapping — do not
> blind-replace inside it either.

---

## 10. File checklist

> This is a **rename refactor**, so the checklist is by **category** (the grep gates in §12 are the
> completeness check), with concrete anchors from code exploration.

### 10.1 Backend (`monkify-backend`)

| # | Target | Change |
| --- | --- | --- |
| 1 | `src/ChimpOut.sln` + 7 `ChimpOut.*.csproj` + project **folders** | Rename to `SpaceStroker.*` (folder, assembly, root namespace); `.sln` → `SpaceStroker.sln` |
| 2 | `ChimpOut.Infrastructure/Data/Context/ChimpOutDbContext.cs` + snapshot | → `SpaceStrokerDbContext` / `SpaceStrokerDbContextModelSnapshot` (regenerate snapshot via `dotnet ef`); `DbSet`/table names unchanged (`Game*`) |
| 3 | Every `_globalImports.cs` / `GlobalUsings.cs` + all `using ChimpOut.*` | → `SpaceStroker.*` |
| 4 | `ChimpOut.Common/Extensions/CommonExtensions.cs:22,24` | `AddChimpOutSettings` → `AddSpaceStrokerSettings`; filename `"chimpoutsettings.json"` → `"spacestrokersettings.json"` (+ call sites) |
| 5 | `ChimpOut.Common/chimpoutsettings.json` | Rename file → `spacestrokersettings.json`; `Database=chimpout_messaging` → `spacestroker_messaging` (`:103`); CORS `app.chimpout.com` → `app.spacestroker.com` (`:98`) |
| 6 | `ChimpOut.Consumers/Program.cs:41`, `Infrastructure/DependencyInjection.cs:44` | `GetConnectionString("ChimpOut")` → `"SpaceStroker"` (both sites); mock branding `app.chimpout.com` (`Program.cs:325-326`) → `app.spacestroker.com`; comment `:58` |
| 7 | `ChimpOut.Common/Cors/CorsOriginResolver.cs:7,15`, `Domain/Configs/Entities/CorsSettings.cs:10` | `DefaultOrigins` + comments `app.chimpout.com` → `app.spacestroker.com` |
| 8 | `Infrastructure/Services/Messaging/MessagingRegistration.cs:11,26`, `Hubs/ActiveSessions…` → **N/A** (hub already `ActiveGamesHub`) | Messaging DB comments → `spacestroker_messaging`; hub namespace only |
| 9 | `Infrastructure/Migrations/` + snapshot | Add `RenameChimpOutToSpaceStroker` migration; regenerate snapshot |
| 10 | `docker-compose.yml`, `docker-compose.prod.yml`, `scripts/*.bat`/`*.sh`/`*.ps1` | DB/network/messaging identifiers `chimpout*` → `spacestroker*` (lockstep with conn strings) |
| 11 | `ChimpOut.Tests/**`, `ChimpOut.Tests.Integration/**` | Rename projects/types/files + `using`s → `SpaceStroker.*`; update `app.chimpout.com` asserts (`CorsOriginResolverTests`, `LaunchControllerTests`) + messaging-DB refs (`IntegrationTestFixture:89,94`); keep both suites green (§11) |

### 10.2 Frontend (`monkify-front`)

| # | Target | Change |
| --- | --- | --- |
| 1 | `src/types/index.ts:18,120,174,197` | `// Mirrors backend ChimpOut.*` → `SpaceStroker.*` (4×) |
| 2 | `src/utils/provablyFair.ts:5` | `ChimpOut.Domain.Games.Services.ProvablyFair` → `SpaceStroker.…` |
| 3 | `public/verify.html:16` | `ChimpOut.Domain.Games.Services.ProvablyFair` → `SpaceStroker.…` |

### 10.3 Meta — deploy (`monkify-meta/deploy`)

| # | Target | Change |
| --- | --- | --- |
| 1 | `tools/ChimpOut.Deploy/**`, `tools/ChimpOut.DbConsole/**`, `tools/ChimpOut.DeployTools.sln` | Rename folders/`.csproj`/`.sln`/namespaces/`AssemblyName` → `SpaceStroker.*` / `spacestroker-*`; resource names in `Deployer.cs`/`DbConsole` (§8.1) |
| 2 | `terraform/persistent/**` + `terraform/workload/**` (root + 9 modules) | `chimpout*` resource/bucket/service/secret/instance names → `spacestroker*` (§8.2) |
| 3 | `secrets.example.json`, `terraform/**/production.tfvars.example` | Domain + bucket + zone → `spacestroker.com` / `spacestroker-*` (§8.3); mock values |
| 4 | `deploy/README.md` | Brand prose, `chimpout.com` registration step, `chimpout-deploy`/`chimpout-db` command refs, field table |

### 10.4 Meta — KB (`monkify-meta`)

| # | Target | Change |
| --- | --- | --- |
| 1 | `INDEX.md`, `architecture.md`, `glossary.md`, `constraints.md`, `conventions/**`, `features/**` | Full rename (prose + identifiers); tooling-guarded (§9) |
| 2 | `specs/**` | Full rename incl. any `chimpout`-named file + INDEX links; **guard** `specs/rebrand-chimpout.md` + `specs/rebrand-space-stroker.md` from→to tokens (§9) |
| 3 | `specs/rebrand-space-stroker-backend.md`, `INDEX.md` "in flight" | This spec (this run) + the flight entry |

---

## 11. Unit / integration tests

Projects: `monkify-backend/src/ChimpOut.Tests` (+ `ChimpOut.Tests.Integration`) → `SpaceStroker.*`.

| Test area | Change | Expected |
| --- | --- | --- |
| All `ChimpOut.*`-named test projects/types/files + `using`s | Rename → `SpaceStroker.*` | Compile + pass |
| `CorsOriginResolverTests` (`app.chimpout.com`/`www.chimpout.com` asserts, `:16-49`) | Update expected origins → `app.spacestroker.com` / `www.spacestroker.com` | Pass |
| `LaunchControllerTests` (`app.chimpout.com` branding asserts, `:49-50`) | Update expected URLs → `app.spacestroker.com` | Pass |
| `ChimpOut.Tests.Integration` (`GameFlowTests`, `GameChatTests`, `LotteryPotStackingFlowTests`, `RefundSafeguardsTests`, `IntegrationTestFixture`, …) | Rename identifiers; messaging DB `chimpout_messaging` → `spacestroker_messaging` (`:89,94`) | Green |

Run via `scripts/run_tests.bat` + `scripts/run_integration_tests.bat`. **No NEW integration test is
required** (rename/refactor, not a new feature). **Report the changed test files at the end** per
constraints §3.

---

## 12. Verification gates (grep-to-zero)

After each repo's rename (the definition of done for the mechanical pass):

- **Backend:** `rg -in "chimpout" monkify-backend/src` returns **nothing**; `dotnet build` clean; the
  renamed Consumers host boots + migrates a fresh `spacestroker` DB; both test suites green; a fresh
  `scripts/setup_local_environment.bat` stack boots and plays a round.
- **Frontend:** `rg -in "chimpout" monkify-front/src monkify-front/public` returns **nothing** (the 6
  namespace comments now read `SpaceStroker.*`); `yarn build` + `yarn lint` clean (no behavior change).
- **Meta — deploy:** `rg -in "chimpout" monkify-meta/deploy` returns **nothing**; the deploy tools
  build (`SpaceStroker.DeployTools.sln`).
- **Meta — KB:** `rg -n "ChimpOut|chimpout" monkify-meta -g '!skills/**' -g '!AGENTS.md' -g '!deploy/**'`
  returns **nothing** in living docs + spec prose **except** the two guarded rebrand specs' from→to
  tokens, this spec's from→to tokens, the `trello.com/b/Yi42y6Xy` board lines, and `.git` clone URLs.
- **Global smell test:** a case-insensitive `chimp\W?out` sweep across all four surfaces returns only
  the intentional exclusions (tooling `monkify-*`, MonkAIfy board, `.git` clone URLs, the guarded
  rebrand-spec tokens).

---

## 13. Manual QA

1. `scripts/setup_local_environment.bat` → fresh stack; confirm the renamed Consumers host migrates a
   clean **`spacestroker`** DB (+ **`spacestroker_messaging`**) and the API/consumers boot.
2. Open the SPA → place a bet, watch the typer run over SignalR (`/Hubs/Games`), confirm a reward pays
   out — behaviour identical to before (names only).
3. Confirm nothing user-visible changed in the front (already Space Stroker from #74); the 6 mirror
   comments now read `SpaceStroker.*`.
4. `dotnet build` the deploy tools solution (`SpaceStroker.DeployTools.sln`); `terraform validate` the
   persistent + workload layers (no apply).
5. Grep gates in §12 return clean.

---

## 14. Trello / pipeline

- **Source card:** [Rename backend + infra ChimpOut → Space Stroker #75](https://trello.com/c/VgNMpTzu)
  — list **Specs created**; the **Implementation** section on the card lists the worktree, branch, and
  spec.
- **Level:** `Minor change` (rename/refactor) — no new integration test, no new `features/` doc.
- **Next:** `/monkify-implement-feature Rename backend + infra ChimpOut → Space Stroker` to build it;
  then `/monkify-ship-feature …` to sync the KB and merge
  `feature/rebrand-space-stroker-backend` → `main` in each affected repo.
- **Downstream coordination:** after this ships, the `chimpout.com` → `spacestroker.com` DNS cutover +
  real `terraform apply` are a separate ops task (confirm the domain is secured first).

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-07-21 | Initial spec from refined card #75 (backend + infra + KB ChimpOut → Space Stroker rename). Grounded in a full-repo touchpoint inventory; corrects the card's SignalR-hub assumption and confirms `monkify-lp` is out of scope. |

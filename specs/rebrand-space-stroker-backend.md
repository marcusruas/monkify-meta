# SpaceStroker → "Space Stroker" — Backend + Infra Rename — Implementation Spec

> **Status: Shipped (2026-07-21)** — `feature/rebrand-space-stroker-backend` merged to `main` in
> `monkify-backend` + `monkify-front` + `monkify-meta`. Refined via Trello card
> [Rename backend + infra → Space Stroker (follow-up to #74)](https://trello.com/c/VgNMpTzu)
> (2026-07-21). Verified: `dotnet build` clean, 386 unit tests green, local Docker stack healthy
> (game DB `spacestroker` + `spacestroker_messaging`, migrations applied at Consumers startup).
>
> **Editorial note:** the backend/infra half was renamed to **Space Stroker** (machine token
> `SpaceStroker`), completing the frontend rebrand from #74. Per request, the KB was fully scrubbed of
> the product's former brand name, so this spec's from→to mapping now reads self-referentially — the
> substance is the rename to `SpaceStroker`; the historical prior name has been removed everywhere.
> Repos affected: `monkify-backend`, `monkify-meta` (deploy + KB), and `monkify-front` (namespace-mirror
> comments only). This doc lives in `monkify-meta`.
>
> **Purpose:** Finish the Space Stroker rebrand. Card #74 renamed only the consumer-facing frontend;
> the backend host, C# namespaces (`SpaceStroker.*`), DB, config, the GCP/Terraform infra and the
> `spacestroker.com` domain still say SpaceStroker — an intentional, temporary drift. This card renames that
> **backend + infra** half so the naming stops drifting: brand token `SpaceStroker → SpaceStroker`
> (machine form) / `Space Stroker` (display), a greenfield DB rename, the domain `spacestroker.com →
> spacestroker.com`, and a full knowledge-base rename. It is a **pure rename refactor** — a mirror of
> the earlier `Monkify → SpaceStroker` rebrand (`specs/rebrand-spacestroker.md`); the game plays identically
> afterward, only names change.

---

## 1. Summary

One global, mechanical rename applied everywhere the brand proper noun appears in the backend, infra,
and knowledge base:

| Rename | Rule | Notes |
| --- | --- | --- |
| **`SpaceStroker` → `SpaceStroker`** | Replace the brand token `SpaceStroker`/`spacestroker`/`SPACESTROKER` (case-preserving) everywhere it is the **brand proper noun** — code identifiers, DB, config, infra, domain, docs. | The **"infinite monkey" theme stays** — the monkey character/imagery and the noun "monkey" are untouched. The `Session→Game` family was already renamed in the prior rebrand and is **not** in scope here. |

**Case mapping (apply consistently):**

| From | To (machine) | To (display) |
| --- | --- | --- |
| `SpaceStroker` | `SpaceStroker` (PascalCase — namespaces/assemblies/types/DbContext can't contain a space) | `Space Stroker` (two words, user-visible copy) |
| `spacestroker` | `spacestroker` (kebab/lowercase — files, config keys, hosts, buckets, DB names) | — |
| `SPACESTROKER` | `SPACESTROKER` where a space would break a token/host; `SPACE STROKER` in prose/display | `SPACE STROKER` |

**This is a "clean break":** no aliases, no redirects, no dual-emit, no old-identifier acceptance. The
DB is rebuilt via a greenfield rename migration (no data preserved — nothing is live). The domain moves
to `spacestroker.com` in Terraform/config as **code only**; the real `terraform apply` / DNS cutover is
a separate downstream ops step.

### Corrections to the refined card (found during code exploration)

- **SignalR hub carries no SpaceStroker token.** The hub is already `ActiveGamesHub` at `/Hubs/Games`
  (renamed to the `Game*` family in the earlier rebrand). Only its **containing namespace**
  (`SpaceStroker.Infrastructure.Hubs` → `SpaceStroker.Infrastructure.Hubs`) changes; the hub name and map
  path stay. There is no "SignalR hub name" to rename.
- **The config-load extension is `AddSpaceStrokerSettings`** (`SpaceStroker.Common/Extensions/CommonExtensions.cs:22`)
  and it hard-codes the filename `"spacestrokersettings.json"` at `:24` — both rename.
- **`monkify-lp` is NOT affected.** #74 already renamed the LP to Space Stroker; its only remaining
  SpaceStroker references are the deferred `// monkify-front` repo-folder comments (tooling, out of scope).

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo (worktree-first):** `feature/rebrand-space-stroker-backend` in each affected repo;
  the shared checkout stays on `main`. Work lives in
  `.worktrees/rebrand-space-stroker-backend/<repo>`. Push after branch work.
- **Unit tests:** update the affected tests in `SpaceStroker.Tests` (→ `SpaceStroker.Tests`) and keep them
  green; **report changed tests** at the end. This is a **rename/refactor, not a new feature**, so **no
  new happy-path integration test is required** — but the existing `SpaceStroker.Tests.Integration`
  (Testcontainers) suite must be renamed to the new identifiers and stay green. Run via
  `scripts/run_tests.bat` (avoid the slow ProductionSessions Monte-Carlo suite) and
  `scripts/run_integration_tests.bat`.
- **EF mapping:** inline in `SpaceStrokerDbContext.OnModelCreating` (→ `SpaceStrokerDbContext`); no
  `IEntityTypeConfiguration`.
- **Migrations:** new rename migration under `SpaceStroker.Infrastructure/Migrations/` (→
  `SpaceStroker.Infrastructure/Migrations/`); applied **only** by `SpaceStroker.Consumers` (→
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
| 1 | **Do the backend rename? YES** — full deep rename. The "keep SpaceStroker as the permanent internal/technical name (company-vs-product split) and close the card" alternative was **rejected**. |
| 2 | **Code identifier form = `SpaceStroker` (PascalCase);** display copy is the two-word `Space Stroker`. Case map per §1. |
| 3 | **Clean break** — no aliases, no redirects, no dual-emit, no old-identifier acceptance. Safe because nothing is live. |
| 4 | **DB = greenfield rename migration, no data preserved.** Game DB `spacestroker` → `spacestroker`; messaging DB `spacestroker_messaging` → `spacestroker_messaging`; any SpaceStroker-named columns. Applied only by the (renamed) Consumers host at startup; local/dev DBs recreate cleanly. |
| 5 | **Infra + domain = full, now.** Domain `spacestroker.com` → **`spacestroker.com`** (`api.spacestroker.com`, `app.spacestroker.com`, `www.spacestroker.com`, apex) in the Terraform **persistent** DNS layer + `secrets.<env>.json` + backend CORS defaults. **Code only** — the real `terraform apply` / DNS cutover is a separate ops step. ⚠️ Ops must confirm `spacestroker.com` is actually secured before a real apply. |
| 6 | **`src/` .NET project folders + `.sln` are renamed on disk** (`src/SpaceStroker.Api` → `src/SpaceStroker.Api`, `SpaceStroker.sln` → `SpaceStroker.sln`), in addition to file-contents. |
| 7 | **Repo folders + tooling stay `monkify-*`** (contents-only) — see §2 constraint. |
| 8 | **KB rename = FULL** — living docs **and** all historical `specs/*.md`. Guards: (a) tooling exclusions; (b) **do NOT blind-replace the `from→to` mapping tokens inside `specs/rebrand-spacestroker.md` and `specs/rebrand-space-stroker.md`** — their "from" side is legitimately `SpaceStroker` and must stay accurate. |
| 9 | **Frontend touched (comments only):** the 6 `SpaceStroker.*` backend-namespace references in `monkify-front` (deliberately kept as SpaceStroker in #74) now update to `SpaceStroker.*`. No behavior/build-contract change. |
| 10 | **Level = Minor change** (rename refactor, zero game-flow change) ⇒ **no new integration test, no new `features/*.md` doc**; existing suites renamed + green; KB docs updated. |

---

## 4. Data model

**No entity/column *shape* changes** — this is a physical-name rename only. No new tables, no new
columns, no type changes; the whole `Game*` entity family is untouched in shape.

### 4.1 DbContext

`SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` →
`SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs`
(class `SpaceStrokerDbContext` → `SpaceStrokerDbContext`; all `using SpaceStroker.*`; DI + host registrations).
EF mapping stays **inline** in `OnModelCreating`; no table/`DbSet` renames (those are all `Game*`
already).

### 4.2 Physical database names

| Kind | From | To |
| --- | --- | --- |
| Game database | `spacestroker` | `spacestroker` |
| Messaging database (MassTransit Postgres transport) | `spacestroker_messaging` | `spacestroker_messaging` |

The connection strings live in config (`spacestrokersettings.json` → `spacestrokersettings.json`); see §5.
Reference sites that name the messaging DB in comments/code:
`SpaceStroker.Infrastructure/Services/Messaging/MessagingRegistration.cs:11,26`,
`SpaceStroker.Tests.Integration/Harness/IntegrationTestFixture.cs:89,94`,
`SpaceStroker.Consumers/Program.cs:58`.

### 4.3 Migration — none needed (confirmed at implementation)

**No new EF migration is added.** Code exploration confirmed there are **zero SpaceStroker-named schema
objects**: every table is `Game*`/`Bet*`/`Operator*`/etc. (already renamed by the prior rebrand), and
the only `SpaceStroker` tokens in `Migrations/` are **C# identifiers** (the `SpaceStroker.Infrastructure.Migrations`
namespace + `[DbContext(typeof(SpaceStrokerDbContext))]` attributes) plus two **historical SQL string
literals** (`DROP TABLE IF EXISTS "SpaceStrokerApiLogs"/"SpaceStrokerConsumerLogs"` in `DropLogTables`, for
already-dropped log tables — a greenfield `IF EXISTS` no-op). Renaming those C# identifiers produces
**no model change**, so `dotnet ef migrations add` would emit an empty migration.

- The **database name** change (`spacestroker → spacestroker`, `spacestroker_messaging → spacestroker_messaging`)
  is a **connection-string / docker-compose concern**, not in-schema DDL: a fresh database named
  `spacestroker` is created by the connection, and the **existing** migrations (`InitialCreate`,
  `DropLogTables`) build its schema at Consumers startup. AC #2 ("applies a greenfield rename migration")
  is satisfied by the fresh `spacestroker` DB coming up migrated — no separate rename migration exists to
  write.
- The `[DbContext(typeof(SpaceStrokerDbContext))]` reference in the model snapshot + migration `.Designer.cs`
  files is updated to `SpaceStrokerDbContext` by the text rename (the file is renamed to
  `SpaceStrokerDbContextModelSnapshot.cs`); the snapshot is **not** regenerated (no model delta), and it
  is never hand-edited beyond that mechanical type reference.
- Applied **only** by the renamed Consumers host at startup (`Database.Migrate()`); the API does not
  migrate. Local/dev DBs recreate cleanly — **never** run against prod data.

---

## 5. Configuration

| Item | From | To |
| --- | --- | --- |
| Settings file | `SpaceStroker.Common/spacestrokersettings.json` | `SpaceStroker.Common/spacestrokersettings.json` |
| Loader | `CommonExtensions.AddSpaceStrokerSettings` + hard-coded filename `"spacestrokersettings.json"` (`SpaceStroker.Common/Extensions/CommonExtensions.cs:22,24`) | `AddSpaceStrokerSettings` + `"spacestrokersettings.json"` (+ every call site) |
| Connection-string key | `ConnectionStrings:SpaceStroker` (read at `Consumers/Program.cs:41`, `Infrastructure/DependencyInjection.cs:44`) | `ConnectionStrings:SpaceStroker` (update **both** `GetConnectionString("SpaceStroker")` sites) |
| Messaging conn string | `…Database=spacestroker_messaging…` (`spacestrokersettings.json:103`) | `…Database=spacestroker_messaging…` |
| CORS default origin | `https://app.spacestroker.com` (`CorsOriginResolver.cs:15` `DefaultOrigins`; `spacestrokersettings.json:98`; comments `CorsSettings.cs:10`, `CorsOriginResolver.cs:7`) | `https://app.spacestroker.com` |
| Operator branding mock | `MainPageUrl`/`IconUrl` = `https://app.spacestroker.com[/icon.png]` (`Consumers/Program.cs:325-326`) | `https://app.spacestroker.com…` (mock values) |

`csproj`/assembly/namespace renames (§10.1) cover the rest. Values in committed config stay
mock/placeholder.

---

## 6. Backend behaviour

**No control-flow, math, payout, typer, provably-fair, lottery, operator-scoping, or state-machine
change — names only.** The bet → run → reward pipeline behaves identically. Mechanical spots:

### 6.1 Solution / projects / namespaces
Rename the seven projects + solution (assembly, root namespace, `.csproj`, **on-disk `src/` folder**):
`SpaceStroker.{Api,Consumers,Domain,Infrastructure,Common,Tests,Tests.Integration}` → `SpaceStroker.*`;
`SpaceStroker.sln` → `SpaceStroker.sln`. Fix every `_globalImports.cs` / `GlobalUsings.cs` /
`using SpaceStroker.*` (present across essentially every file). Recommended order (compiler-guided):
rename projects/namespaces first and build, then DbContext, then config, then DB/migration, then
tests.

### 6.2 SignalR
`ActiveGamesHub` (name + `/Hubs/Games` map path) **unchanged**; only its namespace
(`SpaceStroker.Infrastructure.Hubs` → `SpaceStroker.Infrastructure.Hubs`) and `using`s change.

### 6.3 Consumers / hosts
`SpaceStroker.Consumers` → `SpaceStroker.Consumers` still owns migrations (`Database.Migrate()`) + the game
loop + Hangfire jobs; `SpaceStroker.Api` → `SpaceStroker.Api` stays stateless (no migrate). Both hosts'
`/health` endpoints and forwarded-headers/CORS wiring are unchanged except for the identifier/host
renames in §5.

### 6.4 Local stack (docker-compose + scripts)
Update in lockstep with the connection strings so a fresh stack still boots and migrates:
`docker-compose.yml`, `docker-compose.prod.yml`, and `scripts/*.bat`/`*.sh`
(`setup_local_environment.bat`, `run_tests.bat`, `run_integration_tests.bat`, `setup_database.sh`,
`collect_code_coverage.bat`, `generate_production_report.ps1`, …) — the DB name (`spacestroker` →
`spacestroker`), the messaging DB, any Docker network/volume named `spacestroker*`, and connection
identifiers. **Trap:** if the compose/script DB or network names are not renamed together with the
connection strings, the stack silently boots against the wrong/missing DB.

---

## 7. Frontend behaviour (`monkify-front`) — comments only

The frontend has **no** SpaceStroker brand copy left (all became "Space Stroker" in #74). The only
SpaceStroker references are 6 **backend-namespace** mentions that #74 deliberately kept accurate (because
the backend hadn't renamed). They now flip to `SpaceStroker.*`:

| File:line | From | To |
| --- | --- | --- |
| `src/types/index.ts:18` | `// Mirrors backend SpaceStroker.Domain.Games.ValueObjects.PaymentMethod` | `…SpaceStroker.Domain.Games.ValueObjects.PaymentMethod` |
| `src/types/index.ts:120` | `// Mirrors backend SpaceStroker.Infrastructure.Contracts.Games.GameVerificationDto` | `…SpaceStroker.Infrastructure.Contracts.Games.GameVerificationDto` |
| `src/types/index.ts:174` | `// Mirrors backend SpaceStroker.Infrastructure.Contracts.Fx.FxRatesDto` | `…SpaceStroker.Infrastructure.Contracts.Fx.FxRatesDto` |
| `src/types/index.ts:197` | `// Mirrors backend SpaceStroker.Infrastructure.Contracts.Admin.GameTypeAdminDto` | `…SpaceStroker.Infrastructure.Contracts.Admin.GameTypeAdminDto` |
| `src/utils/provablyFair.ts:5` | `backend's SpaceStroker.Domain.Games.Services.ProvablyFair` | `…SpaceStroker.Domain.Games.Services.ProvablyFair` |
| `public/verify.html:16` | `SpaceStroker.Domain.Games.Services.ProvablyFair` | `…SpaceStroker.Domain.Games.Services.ProvablyFair` |

No component, route, storage-key, postMessage, build, or behavior change — comments/prose only. (The
`verify.html` ↔ `provablyFair.ts` keystream algorithm parity is untouched.)

---

## 8. Infra behaviour (`monkify-meta/deploy`)

Full rename, domain included (code only; real apply is a downstream ops step).

### 8.1 Deploy tools (C# consoles)
| From | To |
| --- | --- |
| `tools/SpaceStroker.Deploy/` (`AssemblyName spacestroker-deploy`; `Program.cs`, `Deployer.cs`, `ProcessRunner.cs`, `RepoPaths.cs`, `SecretsFile.cs`) | `tools/SpaceStroker.Deploy/` (`spacestroker-deploy`) |
| `tools/SpaceStroker.DbConsole/` (`AssemblyName spacestroker-db`; `Program.cs`) | `tools/SpaceStroker.DbConsole/` (`spacestroker-db`) |
| `tools/SpaceStroker.DeployTools.sln` | `tools/SpaceStroker.DeployTools.sln` |
| Deployer resource names: `spacestroker-api`, `spacestroker-consumers`, `spacestroker-urlmap`, bucket `spacestroker-{site}-{project}` (`Deployer.cs:38,42,215,220`) | `spacestroker-*` |
| DbConsole instance `…:spacestroker-pg` (`Program.cs:25`) | `…:spacestroker-pg` |

Update the usage/comment banners (`spacestroker-deploy` / `spacestroker-db`) accordingly. Rename the `obj/`
build artifacts are regenerated — no need to hand-edit them.

### 8.2 Terraform
`terraform/persistent/**` (DNS zone + domain + state bucket, `prevent_destroy`) and
`terraform/workload/**` (root `main.tf`/`variables.tf`/`production.tfvars.example` + the 9 modules:
`cloud_run_api`, `cloud_run_consumers`, `data`, `edge`, `iam`, `network`, `registry`, `secrets`,
`static_site`). Rename every `spacestroker*` resource/service/bucket/secret/instance/label name →
`spacestroker*`. Notable anchors: `data/main.tf` DB names `"spacestroker"` / `"spacestroker_messaging"` →
`"spacestroker"` / `"spacestroker_messaging"`; `registry`, `cloud_run_api` (`spacestroker-api`),
`cloud_run_consumers` (`spacestroker-consumers`), `static_site` bucket naming.

### 8.3 Domain → `spacestroker.com`
In `secrets.example.json` (and the real unversioned `secrets.<env>.json` shape) + the persistent DNS
layer:

| Key | From | To |
| --- | --- | --- |
| `gcp.stateBucket` | `spacestroker-tfstate-…` | `spacestroker-tfstate-…` |
| `gcp.dnsZoneName` | `spacestroker-zone` | `spacestroker-zone` |
| `gcp.dnsDomain` | `spacestroker.com.` | `spacestroker.com.` |
| `gcp.domains.api` | `api.spacestroker.com` | `api.spacestroker.com` |
| `gcp.domains.app` | `app.spacestroker.com` | `app.spacestroker.com` |
| `gcp.domains.lp` | `spacestroker.com` | `spacestroker.com` |
| `gcp.domains.lpWww` | `www.spacestroker.com` | `www.spacestroker.com` |
| `appConfig.GeneralSettings:Cors:AllowedOrigins:0` | `https://app.spacestroker.com` | `https://app.spacestroker.com` |
| `appConfig.VUE_APP_API_BASE_URL` / `VUE_APP_WEBSOCKET_BASE_URL` | `https://api.spacestroker.com[/Hubs]` | `https://api.spacestroker.com[/Hubs]` |

Update `deploy/README.md` prose (the "register **spacestroker.com**" step, the `spacestroker-deploy` /
`spacestroker-db` command references, the field table). Values in committed example files stay
placeholder/mock.

> **Apply-time note (documented, not executed here):** renaming GCS buckets / Cloud Run services /
> Secret Manager entries / Artifact Registry means the **next real apply** creates new resources and
> tears down the old ones; the domain move sits in the `prevent_destroy` persistent layer. This is an
> ops step outside this card. **Confirm `spacestroker.com` is secured first.**

---

## 9. Knowledge base (`monkify-meta` docs)

Full `SpaceStroker → SpaceStroker` rename across the KB (prose **and** code-identifier references):

- **Living / reference docs:** `INDEX.md`, `architecture.md`, `glossary.md`, `constraints.md`,
  `conventions/**`, every `features/*.md`. Update the `glossary.md` brand entry to Space Stroker.
- **Historical `specs/*.md`:** apply the same rename so identifiers match the renamed code; rename any
  `spacestroker`-named spec/feature **file** and fix INDEX links. (No `spacestroker`-named files exist today —
  the `Session→Game`/`Monkify→SpaceStroker` renames already produced `game-*` file names — but verify.)
- **Guards (do not blind-replace):**
  1. Tooling: the `monkify-*` skill identifiers/paths, the MonkAIfy board references + board URL, the
     `AGENTS.md` clone URLs, the `<!-- monkify-agents-pointer -->` marker, and repo-folder names stay
     `monkify`.
  2. The two rebrand specs' from→to tokens: **do not** replace `SpaceStroker` inside
     `specs/rebrand-spacestroker.md` (its "to" side is legitimately SpaceStroker — the Monkify→SpaceStroker
     record) or `specs/rebrand-space-stroker.md` (its "from" side is legitimately SpaceStroker). Leave
     those two files' historical mapping tokens intact; only add `SpaceStroker` context where a status
     note genuinely needs it.

Practical guard: run the doc find/replace **excluding** `skills/**`, `AGENTS.md`,
`specs/rebrand-spacestroker.md`, `specs/rebrand-space-stroker.md`, and any line containing
`trello.com/b/Yi42y6Xy` or a `.git` clone URL — then eyeball the diff.

> Per the KB-currency rule, this spec file (`specs/rebrand-space-stroker-backend.md`) intentionally
> **keeps** the `SpaceStroker` tokens where it documents the *from →* side of the mapping — do not
> blind-replace inside it either.

---

## 10. File checklist

> This is a **rename refactor**, so the checklist is by **category** (the grep gates in §12 are the
> completeness check), with concrete anchors from code exploration.

### 10.1 Backend (`monkify-backend`)

| # | Target | Change |
| --- | --- | --- |
| 1 | `src/SpaceStroker.sln` + 7 `SpaceStroker.*.csproj` + project **folders** | Rename to `SpaceStroker.*` (folder, assembly, root namespace); `.sln` → `SpaceStroker.sln` |
| 2 | `SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` + snapshot | → `SpaceStrokerDbContext` / `SpaceStrokerDbContextModelSnapshot` (regenerate snapshot via `dotnet ef`); `DbSet`/table names unchanged (`Game*`) |
| 3 | Every `_globalImports.cs` / `GlobalUsings.cs` + all `using SpaceStroker.*` | → `SpaceStroker.*` |
| 4 | `SpaceStroker.Common/Extensions/CommonExtensions.cs:22,24` | `AddSpaceStrokerSettings` → `AddSpaceStrokerSettings`; filename `"spacestrokersettings.json"` → `"spacestrokersettings.json"` (+ call sites) |
| 5 | `SpaceStroker.Common/spacestrokersettings.json` | Rename file → `spacestrokersettings.json`; `Database=spacestroker_messaging` → `spacestroker_messaging` (`:103`); CORS `app.spacestroker.com` → `app.spacestroker.com` (`:98`) |
| 6 | `SpaceStroker.Consumers/Program.cs:41`, `Infrastructure/DependencyInjection.cs:44` | `GetConnectionString("SpaceStroker")` → `"SpaceStroker"` (both sites); mock branding `app.spacestroker.com` (`Program.cs:325-326`) → `app.spacestroker.com`; comment `:58` |
| 7 | `SpaceStroker.Common/Cors/CorsOriginResolver.cs:7,15`, `Domain/Configs/Entities/CorsSettings.cs:10` | `DefaultOrigins` + comments `app.spacestroker.com` → `app.spacestroker.com` |
| 8 | `Infrastructure/Services/Messaging/MessagingRegistration.cs:11,26`, `Hubs/ActiveSessions…` → **N/A** (hub already `ActiveGamesHub`) | Messaging DB comments → `spacestroker_messaging`; hub namespace only |
| 9 | `Infrastructure/Migrations/` + snapshot | Add `RenameSpaceStrokerToSpaceStroker` migration; regenerate snapshot |
| 10 | `docker-compose.yml`, `docker-compose.prod.yml`, `scripts/*.bat`/`*.sh`/`*.ps1` | DB/network/messaging identifiers `spacestroker*` → `spacestroker*` (lockstep with conn strings) |
| 11 | `SpaceStroker.Tests/**`, `SpaceStroker.Tests.Integration/**` | Rename projects/types/files + `using`s → `SpaceStroker.*`; update `app.spacestroker.com` asserts (`CorsOriginResolverTests`, `LaunchControllerTests`) + messaging-DB refs (`IntegrationTestFixture:89,94`); keep both suites green (§11) |

### 10.2 Frontend (`monkify-front`)

| # | Target | Change |
| --- | --- | --- |
| 1 | `src/types/index.ts:18,120,174,197` | `// Mirrors backend SpaceStroker.*` → `SpaceStroker.*` (4×) |
| 2 | `src/utils/provablyFair.ts:5` | `SpaceStroker.Domain.Games.Services.ProvablyFair` → `SpaceStroker.…` |
| 3 | `public/verify.html:16` | `SpaceStroker.Domain.Games.Services.ProvablyFair` → `SpaceStroker.…` |

### 10.3 Meta — deploy (`monkify-meta/deploy`)

| # | Target | Change |
| --- | --- | --- |
| 1 | `tools/SpaceStroker.Deploy/**`, `tools/SpaceStroker.DbConsole/**`, `tools/SpaceStroker.DeployTools.sln` | Rename folders/`.csproj`/`.sln`/namespaces/`AssemblyName` → `SpaceStroker.*` / `spacestroker-*`; resource names in `Deployer.cs`/`DbConsole` (§8.1) |
| 2 | `terraform/persistent/**` + `terraform/workload/**` (root + 9 modules) | `spacestroker*` resource/bucket/service/secret/instance names → `spacestroker*` (§8.2) |
| 3 | `secrets.example.json`, `terraform/**/production.tfvars.example` | Domain + bucket + zone → `spacestroker.com` / `spacestroker-*` (§8.3); mock values |
| 4 | `deploy/README.md` | Brand prose, `spacestroker.com` registration step, `spacestroker-deploy`/`spacestroker-db` command refs, field table |

### 10.4 Meta — KB (`monkify-meta`)

| # | Target | Change |
| --- | --- | --- |
| 1 | `INDEX.md`, `architecture.md`, `glossary.md`, `constraints.md`, `conventions/**`, `features/**` | Full rename (prose + identifiers); tooling-guarded (§9) |
| 2 | `specs/**` | Full rename incl. any `spacestroker`-named file + INDEX links; **guard** `specs/rebrand-spacestroker.md` + `specs/rebrand-space-stroker.md` from→to tokens (§9) |
| 3 | `specs/rebrand-space-stroker-backend.md`, `INDEX.md` "in flight" | This spec (this run) + the flight entry |

---

## 11. Unit / integration tests

Projects: `monkify-backend/src/SpaceStroker.Tests` (+ `SpaceStroker.Tests.Integration`) → `SpaceStroker.*`.

| Test area | Change | Expected |
| --- | --- | --- |
| All `SpaceStroker.*`-named test projects/types/files + `using`s | Rename → `SpaceStroker.*` | Compile + pass |
| `CorsOriginResolverTests` (`app.spacestroker.com`/`www.spacestroker.com` asserts, `:16-49`) | Update expected origins → `app.spacestroker.com` / `www.spacestroker.com` | Pass |
| `LaunchControllerTests` (`app.spacestroker.com` branding asserts, `:49-50`) | Update expected URLs → `app.spacestroker.com` | Pass |
| `SpaceStroker.Tests.Integration` (`GameFlowTests`, `GameChatTests`, `LotteryPotStackingFlowTests`, `RefundSafeguardsTests`, `IntegrationTestFixture`, …) | Rename identifiers; messaging DB `spacestroker_messaging` → `spacestroker_messaging` (`:89,94`) | Green |

Run via `scripts/run_tests.bat` + `scripts/run_integration_tests.bat`. **No NEW integration test is
required** (rename/refactor, not a new feature). **Report the changed test files at the end** per
constraints §3.

---

## 12. Verification gates (grep-to-zero)

After each repo's rename (the definition of done for the mechanical pass):

- **Backend:** `rg -in "spacestroker" monkify-backend/src` returns **nothing**; `dotnet build` clean; the
  renamed Consumers host boots + migrates a fresh `spacestroker` DB; both test suites green; a fresh
  `scripts/setup_local_environment.bat` stack boots and plays a round.
- **Frontend:** `rg -in "spacestroker" monkify-front/src monkify-front/public` returns **nothing** (the 6
  namespace comments now read `SpaceStroker.*`); `yarn build` + `yarn lint` clean (no behavior change).
- **Meta — deploy:** `rg -in "spacestroker" monkify-meta/deploy` returns **nothing**; the deploy tools
  build (`SpaceStroker.DeployTools.sln`).
- **Meta — KB:** `rg -n "SpaceStroker|spacestroker" monkify-meta -g '!skills/**' -g '!AGENTS.md' -g '!deploy/**'`
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

- **Source card:** [Rename backend + infra SpaceStroker → Space Stroker #75](https://trello.com/c/VgNMpTzu)
  — list **Specs created**; the **Implementation** section on the card lists the worktree, branch, and
  spec.
- **Level:** `Minor change` (rename/refactor) — no new integration test, no new `features/` doc.
- **Next:** `/monkify-implement-feature Rename backend + infra SpaceStroker → Space Stroker` to build it;
  then `/monkify-ship-feature …` to sync the KB and merge
  `feature/rebrand-space-stroker-backend` → `main` in each affected repo.
- **Downstream coordination:** after this ships, the `spacestroker.com` → `spacestroker.com` DNS cutover +
  real `terraform apply` are a separate ops task (confirm the domain is secured first).

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-07-21 | Initial spec from refined card #75 (backend + infra + KB SpaceStroker → Space Stroker rename). Grounded in a full-repo touchpoint inventory; corrects the card's SignalR-hub assumption and confirms `monkify-lp` is out of scope. |

# Monkify — "Rebranding (new name): Monkify → SpaceStroker, Session → Game" — Implementation Spec

> **Status: Shipped (2026-07-16)** — `feature/rebrand-spacestroker` merged to `main` (lp: `master`) in all repos. Refined via Trello card
> [Rebranding (new name)](https://trello.com/c/UBbiaz0d/49-rebranding-new-name) (2026-07-16).
> Repos affected: `monkify-backend`, `monkify-front`, `monkify-lp`, and this doc in `monkify-meta`.
>
> **Purpose:** Apply a company rebrand across the whole product by renaming two tokens **deeply**
> (code identifiers, DB schema, SignalR, routes, config, and docs): the brand proper noun
> **`Monkify` → `SpaceStroker`**, and the domain term **`Session` → `Game`**. This is a rename
> refactor, not a behaviour change — the game plays identically afterward; only names change.

---

## 1. Summary

Two global, mechanical renames applied everywhere:

| Rename | Rule | Notes |
| --- | --- | --- |
| **`Monkify` → `SpaceStroker`** | Replace the literal brand token `Monkify`/`monkify`/`MONKIFY` (case-preserving) everywhere it is the **brand proper noun**. | The **"infinite monkey" theme stays** — the monkey character/imagery and the common noun **"monkey"** are untouched. Only the token `Monkify` changes. |
| **`Session` → `Game`** | Pure mechanical: every `Session` token → `Game` across the whole `Session*` family (entities, DTOs, services, consumers, controllers, hub, routes, DB, config section, frontend types/composables). | A **`Game` = one live round**; its definition = **`GameParameters`**. |

Case mapping (apply consistently): `Monkify`→`SpaceStroker`, `monkify`→`spacestroker`, `MONKIFY`→`SPACESTROKER`;
`Session`→`Game`, `session`→`game`, `Sessions`→`Games`, `sessions`→`games`.

**This is a "clean break":** no `/monkify`→`/spacestroker` redirects, no dual SignalR channels, no
accepting old identifiers. The DB is rebuilt via a greenfield rename migration (no data preserved).

### Corrections to the refined card (found during code exploration)

The refined card listed a few touchpoints that do not exist as assumed; the spec supersedes them:

- **JWT `iss` is NOT the brand.** `LaunchTokenService` sets `issuer: operatorId.ToString()` (a GUID)
  and validates against the operator GUID. There is **no `"Monkify"` issuer string to rename.** The
  `launch-token` route / `LaunchController` names do **not** contain `Monkify` or `Session`, so they
  stay.
- **The backend has no `/monkify/...` route prefix.** All `/monkify/...` paths are **frontend routing
  only** (`monkify-front/src/routes.ts`). Backend controller routes use `sessions`, `admin`, etc.
- **EF has no `ToTable(...)`.** Table names equal the `DbSet<>` property names, so renaming the
  DbSets renames the tables. Note the Bet table is `SessionBets` (`DbSet<Bet> SessionBets`) → `GameBets`.

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Branch-per-repo (worktree-first):** `feature/rebrand-spacestroker` in each affected git repo; the
  shared checkout stays on `main` (lp on `master`). Work lives in
  `.worktrees/rebrand-spacestroker/<repo>`. Push after branch work.
- **Unit tests:** update the affected tests in `Monkify.Tests` and keep them green; **report changed
  tests** at the end of implementation. This is a **rebrand/refactor, not a new feature**, so **no new
  happy-path integration test is required** — but the existing `Monkify.Tests.Integration`
  (Testcontainers) suite must be renamed to the new identifiers and stay green.
- **EF mapping:** inline in `SpaceStrokerDbContext.OnModelCreating` (renamed from `MonkifyDbContext`); no
  `IEntityTypeConfiguration`.
- **Migrations:** new rename migration under `Monkify.Infrastructure/Migrations/`; applied **only** by
  `SpaceStroker.Consumers` at startup. Regenerate the snapshot with `dotnet ef` — never hand-edit it.
- **Secrets/config:** mock-only values in committed files; never surface real secrets.
- **Operator messaging:** vague errors only; never reveal contract status. (Only the brand word in
  copy changes; keep messages vague.)
- **Do NOT rename tooling** (see §7.3): the `monkify-*` skill folder names, the "MonkAIfy/monkaify"
  Trello board references, `AGENTS.md` clone URLs, the `<!-- monkify-agents-pointer -->` marker, and
  the `.cursor/.claude/skills/` discovery paths all stay `monkify`.
- **Contents-only:** repo/folder/git-remote/clone-URL names stay `monkify-*`.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | Rename depth = **deep/everything** in code + DB + SignalR + routes + config + docs. Not copy-only. |
| 2 | `Monkify → SpaceStroker` = **brand proper noun only**; keep the monkey character/imagery and the common noun "monkey". |
| 3 | `Session → Game` = **pure mechanical** across the whole `Session*` family. A `Game` is one round; `GameParameters` is its definition. |
| 4 | Repos in scope: `monkify-backend`, `monkify-front`, `monkify-meta`, `monkify-lp` — **contents-only** (folder/remote names unchanged). |
| 5 | **Product-only** — internal tooling (MonkAIfy board, `monkify-*` skills, workspace automation) is untouched. |
| 6 | **DB = greenfield rename migration**, no data preserved (pre-launch). |
| 7 | **Clean break** — no aliases/redirects, no old-identifier acceptance, no dual SignalR. |
| 8 | **Token/on-chain branding OUT OF SCOPE** → the Solana token symbol/name is decided by cards #62/#63. On the LP, the ticker `$MONKIFY` and the domain `app.monkify.net` are **deferred** (see §9.3). |
| 9 | JWT `iss` (operator GUID) and the `launch-token`/`admin`/`players`/`operators`/`fx`/`dev` routes are **unaffected** (they carry neither `Monkify` nor `Session`). |

---

## 4. Canonical rename mapping (the core reference)

### 4.1 Backend identifiers

| From | To |
| --- | --- |
| `Monkify.Api` / `Monkify.Consumers` / `Monkify.Domain` / `Monkify.Infrastructure` / `Monkify.Common` / `Monkify.Tests` / `Monkify.Tests.Integration` | `SpaceStroker.*` (assembly, root namespace, `.csproj`, folder under `src/`, `Monkify.sln` → `SpaceStroker.sln`) |
| `MonkifyDbContext` / `MonkifyDbContextModelSnapshot` | `SpaceStrokerDbContext` / `SpaceStrokerDbContextModelSnapshot` |
| `MonkifyTyper` (and any `Monkify*` identifier) | `SpaceStrokerTyper` / `SpaceStroker*` |
| namespace `Monkify.Domain.Sessions` | `SpaceStroker.Domain.Games` |
| `Session` (round entity) | `Game` |
| `SessionParameters` | `GameParameters` |
| `SessionStatus`, `SessionStatusLog`, `SessionCharacterType` | `GameStatus`, `GameStatusLog`, `GameCharacterType` |
| `SessionChatMessage`, `SessionSettings` | `GameChatMessage`, `GameSettings` |
| `SessionService`/`ISessionService`, `SessionStatsService`/`ISessionStatsService`, `SessionTypeStats` | `GameService`/`IGameService`, `GameStatsService`/`IGameStatsService`, `GameTypeStats` |
| `SessionParametersValidation` | `GameParametersValidation` |
| `SessionsController` | `GamesController` |
| `ActiveSessionsHub` (methods `JoinSession`/`LeaveSession`) | `ActiveGamesHub` (`JoinGame`/`LeaveGame`); `SendChatMessage` unchanged |
| `RunSessionConsumer`/`RunSessionEvent`, `RewardSessionConsumer`/`RewardSessionEvent` | `RunGameConsumer`/`RunGameEvent`, `RewardGameConsumer`/`RewardGameEvent` |
| `SessionCreatedEvent`, `SessionStatusUpdatedEvent`, `SessionParametersCreated{Consumer,Event}`, `SessionTypeDeactivated{Consumer,Event}` | `GameCreatedEvent`, `GameStatusUpdatedEvent`, `GameParametersCreated{Consumer,Event}`, `GameTypeDeactivated{Consumer,Event}` |
| DTOs: `ActiveSessionDto`, `SessionParametersDto`, `SessionTypeStatsDto`, `SessionVerificationDto`, `SessionTypeAdminDto`, `SessionTypeRequestBody`, `DeactivateSessionTypeBody` | `ActiveGameDto`, `GameParametersDto`, `GameTypeStatsDto`, `GameVerificationDto`, `GameTypeAdminDto`, `GameTypeRequestBody`, `DeactivateGameTypeBody` |
| Handlers: `CreateSessionType`, `DeactivateSessionType`, `GetOperatorSessionTypes`, `GetActiveSessionForParameter`, `GetSessionTypeStats`, `GetSessionVerification` | `CreateGameType`, `DeactivateGameType`, `GetOperatorGameTypes`, `GetActiveGameForParameter`, `GetGameTypeStats`, `GetGameVerification` |
| `DefaultSessionParameters` (+ its local `Game(...)` factory already so-named) | `DefaultGameParameters` |

### 4.2 Backend DB / config / transport

| Kind | From | To |
| --- | --- | --- |
| DbSet/table | `Sessions`, `SessionStatusLogs`, `SessionBets`, `SessionParameters`, `SessionChatMessages` | `Games`, `GameStatusLogs`, `GameBets`, `GameParameters`, `GameChatMessages` |
| FK column | `SessionId` | `GameId` |
| Config file | `monkifysettings.json` (source at `Monkify.Common/`) | `spacestrokersettings.json` (+ every `AddJsonFile("monkifysettings.json")` call site) |
| Config key | `ConnectionStrings:Monkify` (read at `Consumers/Program.cs:38`, `Infrastructure/DependencyInjection.cs:45,132`) | `ConnectionStrings:SpaceStroker` (update all 3 `GetConnectionString("Monkify")` call sites) |
| Config section | `GeneralSettings:Sessions` + keys `TimeUntilSessionStarts`, `DelayBetweenSessions`, `ActiveSessionsEndpoint`(`"SessionReceived"`), `SessionStatusEndpoint`, `SessionBetsEndpoint`, `SessionTerminalEndpoint`, `SessionSpeedEndpoint`, `SessionChatEndpoint`; `Workers:CreateSessionsInterval`, `Workers:RewardSessionsInterval` | `GeneralSettings:Games` + `TimeUntilGameStarts`, `DelayBetweenGames`, `ActiveGamesEndpoint`(`"GameReceived"`), `GameStatusEndpoint`, … ; `Workers:CreateGamesInterval`, `RewardGamesInterval`. The `{0}/Status`, `{0}/Bets`, `{0}/Terminal`, `{0}/DisplaySpeed`, `{0}/ChatMessage` **templates are unchanged** (only the C# var feeding `{0}` is `game.Id`). |
| SignalR hub map | `MapHub<ActiveSessionsHub>("/Hubs/Sessions")` (`Api/Program.cs:141`) | `MapHub<ActiveGamesHub>("/Hubs/Games")` |
| API routes | `[Route("sessions")]` + `active-types`, `{sessionTypeId}`, `{parametersId}/stats`, `{id}/verification`, `{id}/bets`; admin `session-types`, `session-types/{id}/deactivate` | `[Route("games")]` + same sub-paths (`{sessionTypeId}`→`{gameTypeId}`); admin `game-types`, `game-types/{id}/deactivate` |
| `.resx` keys | `SessionNotValidForBets`, `SessionWithNoBets`, `SessionWithoutWinners` (+ update `.Designer` refs & call sites) | `GameNotValidForBets`, `GameWithNoBets`, `GameWithoutWinners` |
| `.resx` values | 8 messages containing "session" (see §6.5) | user-visible "session" → "game" (the type) / "round" (a single run), keeping the messages vague |
| Brand mock values | `SupportEmail: "monkify@gmail.com"` | `spacestroker@gmail.com` (pure brand copy). Infra values `Database=monkify`, RabbitMQ `edson:monkifyedson270799` are **optional infra renames — only if kept in sync with `docker-compose.yml` + `scripts/`** (else the local stack breaks); default: leave the infra identifiers, rename only brand copy. |

### 4.3 Frontend identifiers / keys

| Kind | From | To |
| --- | --- | --- |
| package/name | `package.json` `"name": "Monkify"` | `"SpaceStroker"` |
| titles | `<title>Monkify</title>` (index.html), `Monkify — Embed Example`, `Monkify — Provably Fair Verifier`, `MONKIFY` (verify.html:87) | `SpaceStroker …` / `SPACESTROKER` |
| storage keys | `monkify.clientSeed`, `monkify.clientSeed.nonce`, `monkify.adminToken`, `monkify.launchToken`, `monkify.autoLaunch`, `monkify.audio` | `space-stroker.*` |
| toast classes | `monkify-toast`, `monkify-toast__body` (`main.ts:48-49`, + CSS) | `spacestroker-toast`, `spacestroker-toast__body` |
| postMessage | `EMBED_SOURCE='monkify'` (`utils/embed.ts`), `WALLET_BRIDGE_SOURCE='monkify-wallet'` (`utils/walletBridge.ts`), `window.open(url,'monkify-wallet',…)` (`useEmbeddedWallet.ts:31`) | `'space-stroker'`, `'space-stroker-wallet'` (keep the window name === bridge source) |
| env var | `VUE_APP_NEW_SESSION_DELAY` (used `GameConsole.vue:26`) | `VUE_APP_NEW_GAME_DELAY` |
| composables/types/pages | `useSession.ts`, `useSessionChat.ts`, `types/session.ts`, `SessionSelectPage.vue`, admin `SessionTypeForm/List/Manager.vue`, and `session`-named vars in `Game*` components | `useGame.ts`, `useGameChat.ts`, `types/game.ts`, `GameSelectPage.vue`, `GameTypeForm/List/Manager.vue`, `game` vars |
| routes | `/monkify/game`, `/monkify/game/sessions`, `/monkify/game/play`, `/monkify/admin`, `/monkify/wallet-bridge`, hashes `/monkify/game#launch_token=`, `/monkify/admin#admin_token=` | `/space-stroker/…` (see §8 route/name collision resolution) |

### 4.4 Frontend `Game*` component shell — keep names

`GamePage`, `GameTerminal`, `GameConsole`, `GameHeader`, `GameSidePanel`, `GameMonkey`, `GameAudio`,
`GameChatPanel`, `GameStatsPanel`, `GameTyper`, `GameMobileTabs`, `GameTypes`, `GameTypeCard`,
`GameBackground` are the **app shell** and their file/component names stay. Only their **internal
`session`→`game`** variables/types/imports change (e.g. `useSession`→`useGame`,
`ActiveSessionDto`→`ActiveGameDto`). Watch for local collisions where a `session` variable would
become `game` next to an existing `game`.

---

## 5. Recommended execution order (per repo, inside its worktree)

Do renames **top-down** so the compiler/tests guide you:

1. **Backend**
   1. Rename project folders/`.csproj`/`.sln` and root namespaces `Monkify.* → SpaceStroker.*` (IDE/solution-wide). Build.
   2. Rename `MonkifyDbContext`/snapshot/`MonkifyTyper` and the whole `Session* → Game*` family (types, files, folders). Build.
   3. Config: rename `monkifysettings.json` → `spacestrokersettings.json`, the `AddJsonFile` call sites, `ConnectionStrings:Monkify` key (+3 call sites), and the `GeneralSettings:Sessions`→`Games` section (+ its POCO `SessionSettings`→`GameSettings`).
   4. DB: rename DbSets (→ table renames) + FK `SessionId`→`GameId`; add the rename migration; regenerate the snapshot.
   5. SignalR hub + map path; controller routes; `.resx` keys + values; then fix unit + integration tests.
   6. Grep gate (§10): zero `Monkify`/`Session` identifiers remain in `src/` (see exceptions).
2. **Frontend** — types → composables → services → components → routes → titles/keys/postMessage; then `yarn build` + `yarn lint`.
3. **monkify-meta** — living docs first, then specs prose (tooling-exclusion guard, §7.3).
4. **monkify-lp** — brand strings (ticker/domain deferred, §9.3).

---

## 6. Backend behaviour (unchanged — names only)

No control flow, math, payout, typer, or state-machine logic changes. The bet→run→reward pipeline,
provably-fair commit-reveal, lottery carry, operator scoping, and payment-method strategy all behave
identically; only their type/route/channel/config names change per §4. Specific mechanical spots:

- **6.1 SignalR emit sites** — `GameService` (was `SessionService`) emits at the same points; the
  channel suffixes are unchanged (`{gameId}/Status|Bets|Terminal|DisplaySpeed|ChatMessage`), fed from
  the renamed `GeneralSettings:Games.*` templates. `ActiveGamesHub.SendChatMessage` uses
  `GameChatEndpoint` → `{gameId}/ChatMessage`.
- **6.2 Consumers** — `RunGameConsumer`, `RewardGameConsumer`, `GameParametersCreatedConsumer`,
  `GameTypeDeactivatedConsumer` keep their MassTransit wiring; only class/event names change.
- **6.3 Migration** — greenfield rename migration renames tables `Sessions→Games`,
  `SessionStatusLogs→GameStatusLogs`, `SessionBets→GameBets`, `SessionParameters→GameParameters`,
  `SessionChatMessages→GameChatMessages`, and FK `SessionId→GameId`. Applied only by
  `SpaceStroker.Consumers` at startup. Local/dev DBs recreate cleanly (no data preserved).
- **6.4 Seed data** — `DefaultGameParameters` (was `DefaultSessionParameters`) keeps its `Game(...)`
  factory and the seeded default types; only the type name changes.
- **6.5 `.resx` copy** (`ErrorMessages.resx`) — rewrite these 8 values so no user-visible "session"
  remains, using "game" for the type and "round" for a single run, staying vague:
  `BetCannotReceiveReward`, `ParameterHasNoActiveSessions`, `ParameterNotFound`,
  `SessionNotValidForBets`, `SessionWithNoBets`, `SessionWithoutWinners`, `TyperStartedWithoutBets`,
  `TyperStartedWithoutServerSeed`. The 3 `Session*` **keys** are also renamed (§4.2) with their
  `.Designer.cs` and call sites updated.

---

## 7. monkify-meta (knowledge base)

### 7.1 Living / reference docs — full rename
`INDEX.md`, `architecture.md`, `glossary.md`, `constraints.md`, `conventions/backend.md`,
`conventions/frontend.md`, and every `features/*.md`: apply both renames to **prose and code-identifier
references** so the KB reflects the new reality (e.g. `SessionService`→`GameService`,
`Monkify.Domain`→`SpaceStroker.Domain`, "a session"→"a game/round"). `glossary.md` holds the canonical
term definitions — update the `Session`/`Monkify` entries and add `Game`/`SpaceStroker`.

### 7.2 Historical `specs/*.md` — full rename, tooling-guarded
Apply the same global find/replace to the ~26 spec files so identifiers match the renamed code
(≈2,147 `Monkify` across 75 files, ≈2,503 `Session` across 72 files repo-wide). These are
mechanical; the high-density files to budget for are `specs/operator-admin-page.md`,
`specs/lottery-pot-stacking.md`, `specs/session-type-stats.md` (→ `game-type-stats.md`),
`specs/payment-methods-refactor.md`, `specs/provably-fair.md`, `specs/session-chat.md`
(→ `game-chat.md`). Rename the **file names** that contain `session` too, and fix INDEX links.
> This spec file itself (`specs/rebrand-spacestroker.md`) intentionally **keeps** the old `Monkify`/
> `Session` tokens where it documents the *from →* side of the mapping — do not blind-replace inside it.

### 7.3 TOOLING — MUST NOT rename (guard every find/replace)
Exclude and leave verbatim:
- The `skills/` tree and the skill identifiers `monkify-refine-tasks`, `monkify-create-spec`,
  `monkify-implement-feature`, `monkify-ship-feature` (and the `/monkify-*` slash-command mentions).
- The **MonkAIfy / monkaify** Trello board references (`skills/*/SKILL.md`, `INDEX.md:139`,
  `AGENTS.md`) and the board URL `https://trello.com/b/Yi42y6Xy/monkaify`.
- `AGENTS.md` clone URLs (`github.com/marcusruas/monkify-backend.git`,
  `github.com/VitorIsaacSM/monkify-front.git`), the `<!-- monkify-agents-pointer -->` marker, and the
  `.cursor/skills/` `.claude/skills/` discovery paths.
- All repo-folder names (`monkify-backend`, `monkify-front`, `monkify-meta`, `monkify-lp`).

Practical guard: run the doc find/replace **excluding** `skills/**`, `AGENTS.md`, and any line
containing `trello.com/b/Yi42y6Xy` or a `.git` clone URL, then eyeball the diff.

---

## 8. Route / name collision resolution (frontend)

Concrete answers to the two card wrinkles:

- **URL de-dup.** `/monkify/game` is the product **area** (brand) → `/space-stroker/game`. Mechanically
  renaming the lobby segment `/monkify/game/sessions` would yield the ugly `/space-stroker/game/games`.
  **Resolution:** lobby at **`/space-stroker/game`** (redirect base) and round at
  **`/space-stroker/game/play`**; drop the `/sessions` sub-segment. Update the `/`→`/space-stroker/game`
  redirect and the hash-entry paths (`/space-stroker/game#launch_token=`, `/space-stroker/admin#admin_token=`).
- **Route `name` collision.** `routes.ts` currently has `name: 'Session'` (lobby) and `name: 'Game'`
  (play). Under `Session→Game` both want `Game`. **Resolution:** lobby route `name: 'GameLobby'`
  (path `/space-stroker/game`), play route keeps `name: 'Game'` (path `/space-stroker/game/play`). Update every
  `router.push({ name: 'Session' })` call site accordingly.

---

## 9. Per-repo notes

### 9.1 monkify-front
Frontend-only mechanical rename per §4.3/§4.4/§8. `EMBED_SOURCE`/`WALLET_BRIDGE_SOURCE` change is a
**breaking change to the embed + wallet-bridge contract** (a `<iframe>` casino host listening for
`source:'monkify'` and the wallet-bridge popup name must both move to `'space-stroker'` together) — this is
accepted under the clean-break decision; update `EMBEDDING.md` + `public/embed-example.html` to match.

### 9.2 monkify-lp (Astro + Vue)
12 brand occurrences in 4 files: `src/pages/index.astro`, `src/layouts/Layout.astro`,
`src/components/Footer.vue`, `src/components/SpaceRoadmap.vue`. Rename the brand **name**
(`MONKIFY`/`Monkify` → `SPACESTROKER`/`SpaceStroker`; title "MONKIFY — The space chimp coin", footer
copyright, "What is MONKIFY?"/hero copy). `package.json` `"name": "monkify-lp"` is the repo-folder
name → **leave** (contents-only). Coin art `monk-coin*.png` is monkey imagery → **keep**.

### 9.3 monkify-lp — deferred (token + domain)
- **`$MONKIFY` ticker (×5):** the on-chain token symbol is **out of scope** (cards #62/#63). **Do not
  guess** a new ticker here — leave `$MONKIFY` as-is **or** replace with a neutral placeholder
  pending #63; flag for coordination. (Recommended: leave `$MONKIFY` and add a follow-up note, so the
  LP never advertises an unminted symbol.)
- **`app.monkify.net` domain (×3 hrefs):** the production domain is an **ops/DNS task** (like the repo
  rename, out of scope). Leave the real href `https://app.monkify.net` so links stay live, until a
  real SpaceStroker domain exists. Do **not** point them at a dead placeholder.

---

## 10. Verification gates (grep-to-zero)

After each repo's rename, these must hold (the definition of done for the mechanical pass):

- **Backend:** `rg -n "Monkify" src/` returns **nothing** except the renamed folder is gone; `rg -n "\bSession"`
  in `src/` returns nothing (identifiers). Exceptions: none expected — the backend has no tooling
  strings. `dotnet build` clean; `SpaceStroker.Consumers` boots and migrates a fresh DB; both test suites green.
- **Frontend:** `rg -in "monkify" src/ public/` returns **nothing**; `rg -n "useSession|ActiveSessionDto|/monkify/"` returns nothing. `yarn build` + `yarn lint` clean.
- **monkify-meta:** `rg -n "Monkify|Session" -g '!skills/**' -g '!AGENTS.md'` (then manually clear the
  `trello.com/b/Yi42y6Xy` and `.git` URL lines) returns **nothing** in living docs + specs prose;
  the tooling paths (§7.3) still read `monkify`.
- **monkify-lp:** `rg -n "Monkify|monkify" src/` returns only the deferred `$MONKIFY` ticker and
  `app.monkify.net` hrefs (§9.3) — everything else reads SpaceStroker.

---

## 11. File checklist

> This is a **rename refactor**, so the checklist is by **category** (the full file lists are in §4
> and the inventory) rather than a 300-line enumeration. The grep gates in §10 are the completeness check.

### 11.1 Backend (`monkify-backend`)

| # | Target | Change |
| --- | --- | --- |
| 1 | `src/Monkify.sln` + 7 `Monkify.*.csproj` + project folders | Rename to `SpaceStroker.*` (folders, assembly, root namespace) |
| 2 | `Monkify.Infrastructure/Data/Context/MonkifyDbContext.cs` | → `SpaceStrokerDbContext`; rename DbSets (`Sessions→Games`, `SessionBets→GameBets`, `SessionStatusLogs→GameStatusLogs`, `SessionParameters→GameParameters`, `SessionChatMessages→GameChatMessages`); update `OnModelCreating` `Entity<>` generics |
| 3 | `Monkify.Domain/Sessions/**` (entities, VOs, services) | Rename folder → `Games/`; `Session*`→`Game*` types; `MonkifyTyper`→`SpaceStrokerTyper` |
| 4 | `Monkify.Infrastructure/Consumers/{RunSession,RewardSession,SessionParametersCreated,SessionTypeDeactivated}/**` | Rename folders + `*Consumer`/`*Event` classes to `*Game*` |
| 5 | `Monkify.Infrastructure/Contracts/{Sessions,Admin}/**` | Rename DTO/event files + types per §4.1 |
| 6 | `Monkify.Infrastructure/Handlers/{Admin,Sessions}/**` | Rename handler folders/types per §4.1 |
| 7 | `Monkify.Infrastructure/Services/Sessions/**`, `Hubs/ActiveSessionsHub.cs` | → `Services/Games/**` (`GameService`, `GameStatsService`, `GameTypeStats`); `ActiveGamesHub` (`JoinGame`/`LeaveGame`) |
| 8 | `Monkify.Api/Controllers/SessionsController.cs`, `Api/Program.cs` | → `GamesController` `[Route("games")]`; `MapHub<ActiveGamesHub>("/Hubs/Games")` |
| 9 | `Monkify.Consumers/DefaultSessionParameters.cs`, `Consumers/Program.cs`, `Infrastructure/DependencyInjection.cs` | → `DefaultGameParameters`; `GetConnectionString("Monkify")`→`"SpaceStroker"` (3 sites) |
| 10 | `Monkify.Common/monkifysettings.json` (+ `AddJsonFile` sites) | Rename file → `spacestrokersettings.json`; `ConnectionStrings:Monkify`→`SpaceStroker`; `GeneralSettings:Sessions`→`Games` (+ keys); `SupportEmail` brand |
| 11 | `Monkify.Domain/Configs/Entities/SessionSettings.cs` | → `GameSettings` (POCO bound to `GeneralSettings:Games`) |
| 12 | `Monkify.Common/Resources/ErrorMessages.resx` (+ `.Designer.cs`) | Rewrite 8 "session" values → game/round; rename 3 `Session*` keys (§6.5) |
| 13 | `Monkify.Infrastructure/Migrations/` + `MonkifyDbContextModelSnapshot.cs` | Add rename migration; regenerate snapshot (`→ SpaceStrokerDbContextModelSnapshot`) |
| 14 | `Monkify.Tests/**`, `Monkify.Tests.Integration/**` | Rename all `Session*`/`Monkify*` test types/files; keep green (§12) |
| 15 | `docker-compose.yml`, `scripts/*.bat` | Update only if infra identifiers (DB/broker names) are renamed — keep in sync or leave (§4.2) |

### 11.2 Frontend (`monkify-front`)

| # | Target | Change |
| --- | --- | --- |
| 1 | `src/hooks/useSession.ts`, `useSessionChat.ts` | → `useGame.ts`, `useGameChat.ts`; internal `session`→`game` |
| 2 | `src/types/session.ts`, `src/types/index.ts` | → `types/game.ts`; update `Monkify.Domain` mirror comments; `ActiveSessionDto`→`ActiveGameDto`, etc. |
| 3 | `src/pages/SessionSelectPage.vue` | → `GameSelectPage.vue` |
| 4 | `src/components/admin/SessionType{Form,List,Manager}.vue` | → `GameType{Form,List,Manager}.vue` |
| 5 | `src/components/game/**`, `GameHeader.vue`, pages | `session`→`game` vars/imports only; component names stay (§4.4) |
| 6 | `src/routes.ts` | `/monkify/…`→`/space-stroker/…`; de-dup lobby/round paths + route names (§8) |
| 7 | `src/utils/embed.ts`, `utils/walletBridge.ts`, `hooks/useEmbeddedWallet.ts` | `'monkify'`/`'monkify-wallet'`→`'space-stroker'`/`'space-stroker-wallet'` |
| 8 | `src/hooks/useClientSeed.ts`, `utils/adminToken.ts`, `utils/launchToken.ts`, `components/GameHeader.vue` | storage keys `monkify.*`→`space-stroker.*` |
| 9 | `src/main.ts` (+ CSS) | toast classes `monkify-toast*`→`spacestroker-toast*` |
| 10 | `package.json`, `public/index.html`, `public/embed-example.html`, `public/verify.html`, `EMBEDDING.md`, `.env.example` | brand titles/name/`MONKIFY`; `VUE_APP_NEW_SESSION_DELAY`→`VUE_APP_NEW_GAME_DELAY` |

### 11.3 Meta (`monkify-meta`)

| # | Target | Change |
| --- | --- | --- |
| 1 | `INDEX.md`, `architecture.md`, `glossary.md`, `constraints.md`, `conventions/**`, `features/**` | Full rename (prose + identifiers); tooling-guarded (§7.3) |
| 2 | `specs/**` | Full rename incl. `session`-named files; fix INDEX links; guard this spec's from→ tokens (§7.2) |
| 3 | `specs/rebrand-spacestroker.md`, `INDEX.md` "in flight" | This spec + INDEX row/flight entry (this run) |

### 11.4 Landing page (`monkify-lp`)

| # | Target | Change |
| --- | --- | --- |
| 1 | `src/pages/index.astro`, `layouts/Layout.astro`, `components/Footer.vue`, `components/SpaceRoadmap.vue` | Brand name → SpaceStroker; **keep** `$MONKIFY` ticker + `app.monkify.net` (§9.3) |

---

## 12. Unit / integration tests

Project: `monkify-backend/src/Monkify.Tests` (+ `Monkify.Tests.Integration`).

| Test area | Change | Expected |
| --- | --- | --- |
| All `Session*`/`Monkify*`-named test files (e.g. `SessionTests`, `SessionServiceTests`, `SessionParametersValidationTests`, `SessionServiceParametersTests`, `SessionStatsServiceTests`, `SessionsControllerOperatorTests`, `DefaultSessionParametersTests`, background consumer tests) | Rename types/files to `Game*`; update references to renamed identifiers | Compile + pass |
| Tests asserting `.resx` message text (contain "session") | Update expected strings to the new game/round copy | Pass |
| `Monkify.Tests.Integration` (`SessionFlowTests`, `SessionChatTests`, `SessionStatsFlowTests`, `LotteryPotStackingFlowTests`, etc.) | Rename to `GameFlowTests`, `GameChatTests`, … ; update routes (`/games/…`), hub path (`/Hubs/Games`), channel usage | Both suites green |

Run via the repo scripts (`scripts/run_tests.bat`, `scripts/run_integration_tests.bat`). **No new
integration test is required** (rebrand/refactor, not a new feature). **Report the changed test files
at the end of implementation** per constraints §3.

---

## 13. Acceptance criteria

From the Trello **Acceptance Criteria** checklist (the 11 deep-rename items added 2026-07-16; the 8
older "copy-only" items are from an abandoned refinement and are **not** part of this spec):

1. Given the four .NET projects are built, when assemblies/namespaces are inspected, then root namespaces are `SpaceStroker.*` and no `Monkify` identifier remains in `monkify-backend` code, config, or file names.
2. Given the domain model, when types are inspected, then the round entity is `Game` and the whole former `Session*` family is `Game*` (`GameParameters`, `GameStatus`, `GameType`, `GameStatusLog`, `GameCharacterType`, `GameChatMessage`, `GameSettings`, `GameService`, `GamesController`, `ActiveGamesHub`) with no `Session` identifier left.
3. Given a clean DB, when the Consumers host starts, then it applies a new EF rename migration, the schema uses `Game*` names (`Games`, `GameParameters`, `GameStatusLogs`, `GameBets`, `GameChatMessages`; FK `GameId`), and the app boots + migrates without error.
4. Given the API, when routes are called, then `/games/{id}/bets`, `/games/{parametersId}/stats`, `/games/{id}/verification` respond and no `/sessions*` route exists (clean break).
5. Given a live round, when a client subscribes over SignalR, then the hub is `ActiveGamesHub` at `/Hubs/Games` and channels are `{gameId}/Terminal|Bets|ChatMessage`; the old `ActiveSessionsHub`/`/Hubs/Sessions` no longer exists.
6. Given the frontend, when any page renders, then no user-visible text or `<title>` reads "Monkify" (reads "SpaceStroker"), routes are `/space-stroker/...`, storage keys are `space-stroker.*`, and postMessage envelopes use `source:'space-stroker'` / `'space-stroker-wallet'`.
7. Given the game screen after the rename, when a round plays end-to-end (bet → run → reward) on the local stack, then behaviour is identical to before (names only).
8. Given the "infinite monkey" theme, when UI/imagery/copy are reviewed, then the monkey character/imagery and the common noun "monkey" are unchanged — only "Monkify" became "SpaceStroker".
9. Given `monkify-meta`, when docs are read, then product-brand prose reads "SpaceStroker" and product terminology reads "Game", while the `skills/monkify-*` identifiers, the "MonkAIfy" board references, `AGENTS.md` clone URLs, and repo-folder names are unchanged.
10. Given `monkify-lp`, when reviewed, then visible brand strings read "SpaceStroker".
11. Given the full `Monkify.Tests` + `Monkify.Tests.Integration` suites, when run after the rename, then every test is updated to the new identifiers and both suites are green, with changed tests reported (no NEW integration test required).

---

## 14. Manual QA

1. `scripts/setup_local_environment.bat` → fresh stack; confirm `SpaceStroker.Consumers` migrates a clean DB (tables `Games`, `GameBets`, …).
2. Open the SPA at `/space-stroker/game` → lobby lists game types; open a game → `/space-stroker/game/play`; place a bet, watch the typer run over SignalR (`/Hubs/Games`), and confirm a reward pays out.
3. Confirm tab title, chat, stats, provably-fair verifier (`verify.html`), and admin portal (`/space-stroker/admin`) all read "SpaceStroker"/"Game" with the monkey character still present.
4. Grep gates in §10 return clean.

---

## 15. Trello / pipeline

- **Source card:** [Rebranding (new name) #49](https://trello.com/c/UBbiaz0d) — list **Specs created**; the **Implementation** section on the card lists the worktree, branch, and spec.
- **Next:** `/monkify-implement-feature Rebranding (new name)` to build it; then `/monkify-ship-feature Rebranding (new name)` to sync the KB and merge `feature/rebrand-spacestroker` → `main` (lp → `master`).
- **Cleanup owed (from refinement):** delete the 8 stale copy-only acceptance-criteria items on the card, and the abandoned `feature/rebranding-spacestroker` branches on origin (backend/front/meta/lp).

---

## 16. Revision log

| Date | Change |
| --- | --- |
| 2026-07-16 | Initial spec from the refined card (deep rename). Grounded in a full-repo touchpoint inventory; corrects the card's JWT-issuer / backend-route / EF-`ToTable` assumptions. |

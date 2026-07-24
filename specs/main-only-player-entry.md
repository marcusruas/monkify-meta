# SpaceStroker — "Main-only operator + player-only entry + token-gated admin" — Implementation Spec

> **Status: Shipped** (2026-07-24) — merged via `feature/main-only-player-entry` → `main`.
> Repos affected: `monkify-backend`, `monkify-front`, and this doc in `monkify-meta`.
>
> **Purpose:** Consolidate to a **single operator ("Main")** everywhere, make the public entry
> `/space-stroker/game` **always drop the visitor into the lobby as a player**, and make the
> operator **admin portal reachable only by presenting a valid admin token** (no server-side token
> generator). This removes the "two people, same link, different game type" problem caused by the
> non-prod **DEV Cassino** operator and the dev launch picker (see the diagnosis below).
>
> This **partially reverses** `specs/main-production-operator.md` decision #2 (which deliberately kept
> DEV Cassino non-prod-gated): DEV Cassino is now removed in **all** environments.

---

## 0. Why (the diagnosis)

The Consumers host seeds **two** operators: `Main` (all envs) and `DEV Cassino` (non-prod only). In
the dev build (`VUE_APP_DEV_LAUNCH=true`) the entry `/space-stroker/game` shows the **dev launch
picker** (`DevLandingPage.vue`), which mints a **DEV Cassino** token via `POST /dev/launch-token`.
The player launch token lives in per-tab `sessionStorage` and is **stripped from the URL** after
launch, so sharing your address-bar link hands the friend **no token** — they land on the picker (or
auto-mint) and can end up on a **different operator / game-type set** than you. Two operators + a
manual picker = divergent "session types" from one link.

Fix: one operator (Main), one uniform player entry, admin behind a token only.

---

## 1. Summary

| Area | Change |
| --- | --- |
| **Backend — seeding** | Seed **only `Main`** in **every** environment. **Remove all DEV Cassino seeding** (operator + token + its default game types). Main's 3 default types get **`AllowFreeBet = true` in non-production, `false` in production**, so local/dev keep the free-bet path without a funded wallet while prod stays paid-only. |
| **Backend — dev endpoints** | **Remove `POST /dev/admin-token`** entirely (no server-side admin-token minting — only the operator's offline private key may mint one). **Repoint `POST /dev/launch-token`** from `DEV Cassino` → `Main` (still non-prod-gated) so local test skills keep working against the surviving operator. |
| **Frontend — entry** | `/space-stroker/game` is **always** the lobby (`GameSelectPage`) and auto-mints a **Main** player token. Remove the `devLaunchEnabled` route branch, the `/space-stroker/game/lobby` route, and `DevLandingPage.vue`. Retire the `VUE_APP_DEV_LAUNCH` flag / `devLaunchEnabled`. |
| **Frontend — admin** | Admin token is **captured at `/space-stroker/game`** (URL fragment `#admin_token=<jwt>`): store it, strip the fragment, redirect to the `Admin` route. The `Admin` route + `AdminPage` stay registered in **all** builds. **Remove `DevAdminButton.vue`** (the dev icon that called `/dev/admin-token`). |
| **Data / tooling** | The already-deployed **dev DB** still holds DEV Cassino — purged **once by hand** via `SpaceStroker.DbConsole` (no cleanup code shipped; prod never seeded it). Drop `VUE_APP_DEV_LAUNCH` from the deploy tool `frontBuild` + `.env*` files. |

Net effect: **every environment → only Main exists**; entry always lands a **player** in the lobby;
admin is reachable **only** by an operator holding Main's private key. **No schema/migration.**

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo / hub stays on `main`:** `feature/main-only-player-entry` in each affected repo;
  all work in `.worktrees/main-only-player-entry/<repo>`. Never `git checkout -b` in a hub.
- **Unit tests:** update/add in `SpaceStroker.Tests`; **report changed tests** at the end.
- **Integration test:** the Testcontainers harness seeds its **own** `IT *` operators (distinct names,
  by design), so it is unaffected by removing DEV Cassino. Add one happy-path assertion (see §7) that
  after Consumers startup **DEV Cassino does not exist** and **Main + its 3 types do**.
- **EF mapping:** inline in `SpaceStrokerDbContext.OnModelCreating`; no `IEntityTypeConfiguration`.
  **No schema change** (seeding/config only) → **no migration**.
- **Secrets/config:** **mock-only** values in committed files; never print/commit real credentials.
- **Host split:** seeding stays in `SpaceStroker.Consumers` at startup; the API stays stateless and
  does not migrate.
- **Operator messaging:** vague errors only; never reveal contract status.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **Only `Main` is seeded, in all environments.** All DEV Cassino seeding is **removed** (operator, its `OperatorToken`, and its default game types). `EnsureDevOperator` / `EnsureDevOperatorToken` and the `DEV Cassino` game-parameter loop are deleted. Main keeps its fixed GUID `22222222-2222-4222-8222-222222222222`, its registered token, and its 3 default types (`Cosmic Sprint` / `Beast Mode` / `Space Jackpot`). |
| 2 | **Main free bets by environment (Q1).** Main's 3 default types are seeded with `AllowFreeBet = !IsProduction()` — **ON** in Development/local (free-bet path testable on Main without a funded wallet), **OFF** in Production (paid-only, unchanged). Payment method stays SolanaToken. |
| 3 | **No server-side admin token (Q3).** `POST /dev/admin-token` is **removed**. The admin portal is reachable **only** by presenting a valid RS256 admin token (`scope=admin`) minted **offline** by the operator with **Main's private signing key** (validated against Main's `LaunchPublicKey` / `MainOperatorPublicKeyPem`, as today). No new endpoint; no code change to admin-token *validation*. |
| 4 | **Admin entry via the game page (Q3).** At `/space-stroker/game` boot, if the URL fragment carries `admin_token`, the app stores it (`setAdminToken`), strips the fragment, and redirects to the `Admin` route. `useAdmin` reads the stored token (fragment→storage fallback). Works identically in dev/prod/local. |
| 5 | **`/dev/launch-token` → Main (Q — flagged & approved).** The dev player-token endpoint is repointed from `DEV Cassino` to `Main` (still `404` in production), so local automation/skills (`fill-crypto-clash`, local paid-bet flows) keep minting against the surviving operator instead of 404-ing. |
| 6 | **Existing dev data (Q2).** No cleanup code ships. The current dev DB's `DEV Cassino` operator + its game types/games are **purged once by hand** via `SpaceStroker.DbConsole`. Production never seeded DEV Cassino, so nothing to clean there. |
| 7 | **`VUE_APP_DEV_LAUNCH` retired.** With entry uniform and admin token-gated, the flag / `devLaunchEnabled` export is dead. Removed from `featureFlags.ts`, `routes.ts`, the deploy tool `frontBuild`, and `.env*`. `VUE_APP_ALLOW_FREE_BETS` is unaffected. |

---

## 4. Backend touchpoints (`monkify-backend`)

- `SpaceStroker.Consumers/Program.cs`
  - `CreateDefaultOperatorAsync`: drop the DEV Cassino block (both the `IsProduction()` early-return
    guard and the `EnsureDevOperator` + `EnsureDevOperatorToken` calls). Keep Main seeding.
  - Delete `EnsureDevOperator`, `EnsureDevOperatorToken`.
  - `CreateDefaultGameParametersAsync`: delete the `devOperators` (`Name == "DEV Cassino"`) loop; keep
    the Main block. In the Main block, set `AllowFreeBet = !app.Environment.IsProduction()` on each
    seeded parameter.
- `SpaceStroker.Api/Controllers/DevController.cs`
  - Remove the `admin-token` action.
  - Change `DevOperatorName` usage: `launch-token` resolves **`Main`** instead of `DEV Cassino`.
- Config: `DevOperatorPublicKeyPem` / `DevSigningPrivateKeyPem` become unused for admin minting; leave
  the config keys (mock values) unless trivially removable without churn.

## 5. Frontend touchpoints (`monkify-front`)

- `src/routes.ts` — collapse the `devLaunchEnabled` ternary: `/space-stroker/game` → `GameSelectPage`
  (name `GameLobby`) always; drop the `/space-stroker/game/lobby` entry; drop the `DevLandingPage`
  import/route.
- `src/pages/DevLandingPage.vue` — **deleted**.
- `src/constants/featureFlags.ts` — remove `devLaunchEnabled` (+ the `VUE_APP_DEV_LAUNCH` comment).
- `src/App.vue` (entry boot, `onMounted`) — before the player `loadOperator()` path, if
  `route.name === 'GameLobby'` and the fragment carries `admin_token`: `setAdminToken`, strip
  fragment, `router.replace({ name: 'Admin' })`, and skip the player boot.
- `src/hooks/useAdmin.ts` — ensure `ensureAdminToken` falls back to the **stored** token (not only the
  fragment) so the post-redirect admin page resolves. (Fragment still wins when present.)
- `src/components/header/DevAdminButton.vue` — **deleted**; remove its usage in `GameHeader.vue`.

## 6. Deploy / tooling touchpoints (`monkify-meta/deploy`)

- Remove `VUE_APP_DEV_LAUNCH` from `secrets.example.json` / `secrets.development.json` /
  `secrets.production.json` `frontBuild` blocks and any `.env*` docs. (Non-secret; safe to edit.)

---

## 7. Tests

- **Unit (`SpaceStroker.Tests`):**
  - `DevControllerTests` — drop the admin-token cases; update launch-token to expect **Main**.
  - `DefaultGameParametersTests` / `OperatorServiceTests` / any test asserting `DEV Cassino` seeding —
    update to the Main-only reality.
  - Report every changed/added test at the end (constraints §3).
- **Integration (`SpaceStroker.Tests.Integration`):** add one assertion to the existing suite that,
  after the real Consumers host starts, **no operator named `DEV Cassino` exists** and **`Main` plus
  its 3 default game types do**. (Harness `IT *` operators are independent — no collision.)

## 8. Out of scope / non-goals

- No change to admin-token **validation** or the admin API endpoints (create/list/deactivate game
  types) — they remain the shipped operator feature, now reachable only via a token.
- No schema/migration. No change to the game lifecycle, payouts, or typer.
- No automated deletion of existing DEV Cassino data (handled manually via the DB console).
- `monkify-lp` is untouched.

## 9. Rollout notes

- After merge + deploy, the dev DB retains DEV Cassino until the manual `SpaceStroker.DbConsole` purge;
  do that as an explicit ops step (delete its games/bets before the operator to satisfy FKs).
- Front build bakes the retired flag out; re-deploy the front so the picker/admin-button bundle is gone.

# Feature — Main-only operator + player-only entry + token-gated admin

> Status: **shipped** (2026-07-24, `feature/main-only-player-entry` → `main`).
> Authoritative spec: `../specs/main-only-player-entry.md`.
> Repos: `monkify-backend`, `monkify-front`, `monkify-meta`. **Level: Feature** (happy-path
> integration test added). **Partially reverses** `main-production-operator.md` decision #2.

## What it does

Consolidates the game to a **single operator, "Main", in every environment**, makes the public
entry `/space-stroker/game` **always land the visitor in the lobby as a player**, and makes the
operator **admin portal reachable only by presenting a valid admin token** (no server-side
generator). This removes the "two people open the same link, get different game types" problem that
came from the non-prod **DEV Cassino** operator plus the dev launch picker.

## Core behaviour

### Backend (`SpaceStroker.Consumers`, `SpaceStroker.Api`)
- **Only `Main` is seeded, in all environments.** All DEV Cassino seeding is removed (operator,
  token, and its default game types). `EnsureDevOperator`/`EnsureDevOperatorToken` and the DEV Cassino
  game-parameter loop are gone.
- **Main free bets by environment.** Main's 3 default types (`Cosmic Sprint` / `Beast Mode` /
  `Space Jackpot`) are seeded `AllowFreeBet = !IsProduction()` — **ON** in Development/local (free-bet
  path testable without a funded wallet), **OFF** in Production. A **startup self-heal** re-aligns
  pre-existing rows via **`ExecuteUpdate`** (the `SpaceStrokerDbContext` is `NoTracking` by default, so a
  load-mutate-`SaveChanges` would silently persist nothing) and busts the `ActiveParameters:{op}` cache.
- **No server-side admin token.** `POST /dev/admin-token` is **removed** — only the operator, holding
  Main's private signing key, can mint an admin token (validated against Main's `LaunchPublicKey`).
- **`POST /dev/launch-token` → Main.** Repointed from DEV Cassino to Main (still non-production-gated,
  signed with `MainSigningPrivateKeyPem`) so local automation keeps minting against the surviving
  operator.

### Frontend (`monkify-front`)
- `/space-stroker/game` is **always** the lobby (`GameSelectPage`) and auto-mints a **Main** player
  token. The `DevLandingPage` picker, the `/space-stroker/game/lobby` route, and the
  `VUE_APP_DEV_LAUNCH` flag are **retired**.
- **Admin entry moved to the game page:** loading `/space-stroker/game#admin_token=<jwt>` captures the
  token (App.vue → `useAdmin`), strips the fragment, and redirects to `/space-stroker/admin`. The
  `Admin` route + `AdminPage` stay registered in all builds; the RS256 admin token is the only key.
  The dev `DevAdminButton` header icon is **removed**.

## Net effect

Every environment → only Main exists; entry always lands a player in the lobby; a shared link always
shows the same games; admin is reachable only by an operator holding Main's private key.
**No schema/migration.**

## Notes / rollout

- Pre-existing deployed DBs keep any stale **DEV Cassino** row (seeding is create-only and no cleanup
  code ships — see spec §3 #6); it is unreachable by users once the frontend is deployed. Purge it by
  hand via `SpaceStroker.DbConsole` from an in-VPC host when convenient.
- Existing Main types seeded free-bets-off before this feature are corrected on the next Consumers
  startup by the self-heal.

## Key touchpoints

- **Backend:** `SpaceStroker.Consumers/Program.cs` (`CreateDefaultOperatorAsync`,
  `CreateDefaultGameParametersAsync` self-heal), `SpaceStroker.Api/Controllers/DevController.cs`.
- **Frontend:** `src/routes.ts`, `src/constants/featureFlags.ts`, `src/App.vue`, `src/hooks/useAdmin.ts`;
  deleted `src/pages/DevLandingPage.vue`, `src/components/header/DevAdminButton.vue`.
- **Tests:** `SpaceStroker.Tests/UnitTests/Controllers/DevControllerTests.cs` (rewritten),
  `SpaceStroker.Tests.Integration/OperatorSeedingTests.cs` (new — asserts Main-only + free-bet-on).

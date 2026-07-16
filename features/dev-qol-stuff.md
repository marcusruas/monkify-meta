# Feature — Dev QOL (launch UX + integration tests)

> Status: **shipped.** Authoritative spec: `../specs/dev-qol-stuff.md`
> (merged via `feature/dev-qol-stuff` on 2026-06-30).

## What it does

A bundle of developer/QA quality-of-life changes: the launch flow no longer carries `operatorId` in
the URL (the operator comes from the launch token), the dev launch is now two explicit buttons
instead of a silent auto-mint, and the backend gains an end-to-end **game-lifecycle integration
test suite** (Testcontainers). No player- or operator-facing behavior changes beyond the launch URL
shape.

## Core rules

- **Operator identity comes from the token, not the URL.** Operator-scoped frontend routes are
  param-free (`/chimpout/game/games`, `/chimpout/game/play`). The frontend decodes the operator id
  from the launch token's `iss` claim (`getOperatorIdFromToken`) and calls `GET /operators/{id}` with
  it. The production casino launch URL is `…/chimpout/game/games#launch_token=<jwt>`. Full launch
  details live in `features/launch-token-validation.md`.
- **Dev launch endpoint returns `{ token }` only.** `POST /dev/launch-token?paymentModel=Crypto|Regulated`
  no longer returns `operatorId` (recoverable from `iss`); the minted dev token is long-lived
  (effectively non-expiring) for local convenience. Still 404 in production.
- **Explicit dev entry, no silent auto-mint.** `/chimpout/game` is a dev-only landing
  (`DevLandingPage.vue`) with **Crypto** and **Regulated** buttons; each mints a token and enters the
  game-select page. The old silent `mintDevToken` fallback in `useLaunch` is removed.
- **`VUE_APP_DEV_LAUNCH` gates the dev landing** (`featureFlags.ts` — `devLaunchEnabled =
  VUE_APP_DEV_LAUNCH !== 'false'`, default **on**, mirroring `VUE_APP_ALLOW_FREE_BETS`). Production
  (`.env.production`) sets it `false` → `/chimpout/game` redirects to `/error` and the buttons never
  ship. Gating is **not** on `NODE_ENV`, because the local docker environment builds the SPA in
  production mode; `scripts/setup_local_environment.bat` materialises `VUE_APP_DEV_LAUNCH=true` into
  the generated `.env.production.local` so the buttons appear there (the api/consumers containers run
  `ASPNETCORE_ENVIRONMENT=Development`, so `/dev/launch-token` is available).
- **Game-flow integration tests exist and must stay current.** `ChimpOut.Tests.Integration`
  (Testcontainers: PostgreSQL + RabbitMQ + Redis) boots the **API** and **Consumers** hosts via
  `WebApplicationFactory` and runs representative default game types end-to-end
  (bet → run → reward) with Solana/operator-wallet mocked. It asserts each game reaches
  `RewardForWinnersCompleted`, winners are `Rewarded`, the `TransactionLog` rows are correct (none for
  free bets; a `Credit` for paid crypto via the Solana double; `Debit` + `Credit` for paid regulated),
  and **no error-level logs** are emitted. It is excluded from the fast suite (`scripts/run_tests.bat`,
  which scopes to `~UnitTests`) and runs via `scripts/run_integration_tests.bat`. **Any change to the
  game lifecycle/flow must update this suite** (see `conventions/backend.md`, `constraints.md §3`).

## Key code touchpoints

**Backend (`monkify-backend`)**

- `ChimpOut.Api/Controllers/DevController.cs` — `MintLaunchToken` returns `{ token }`, non-expiring TTL.
- `ChimpOut.Tests/UnitTests/Controllers/DevControllerTests.cs` — assert token `iss` + no `operatorId`.
- `ChimpOut.Tests.Integration/` — `Harness/` (fixture, `TestDoubles`, `InMemoryLogSink`) +
  `GameFlowTests.cs`; `scripts/run_integration_tests.bat`; project added to `ChimpOut.sln`;
  `Program.cs` in Api + Consumers expose `public partial class Program` for the test factory;
  Api/Consumers `Dockerfile` copy the integration csproj before `dotnet restore`.
- `scripts/setup_local_environment.bat` — recreate local env; sets `VUE_APP_DEV_LAUNCH=true`.

**Frontend (`monkify-front`)**

- `src/utils/launchToken.ts` (`getOperatorIdFromToken`), `src/routes.ts` (param-free routes + dev
  landing gated by `devLaunchEnabled`), `src/pages/DevLandingPage.vue`, `src/hooks/useLaunch.ts`
  (auto-mint removed), `src/hooks/useOperator.ts` (operator from token), `src/constants/featureFlags.ts`
  (`devLaunchEnabled`), `GameHeader.vue` / `GamePage.vue` / `game-selection/GameTypes.vue` (param-free
  navigation), `.env.example` (documents `VUE_APP_DEV_LAUNCH`).

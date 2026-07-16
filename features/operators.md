# Feature — Operators (multi-tenancy)

> Status: **shipped.** Authoritative spec: `../specs/operator-feature.md`
> (merged via `feature/operators` on 2026-06-26).
>
> **Superseded (2026-06-30) by `features/launch-token-validation.md`:** the `x-operator-id` header is
> **removed**. Operator (and player) identity now comes from a signed **launch token**
> (`X-Launch-Token`, validated in `ChimpOut.Api` middleware). The multi-tenancy/contract/branding rules
> below still hold; only the transport changed — read the launch-token doc for the current request flow.
> Operators also gained `PaymentModel` (`Regulated` | `Crypto`) and `LaunchPublicKey`.

## What it does

Introduces **Operators** (casino/betting platforms, e.g. BetMGM, BetFury). Every `GameParameters`
row **must** have an `OperatorId`. Players arrive via a signed launch token (the operator is the
token's `iss` — game routes are **param-free**: `/chimpout/game/games`, `/chimpout/game/play`); the
frontend sends a signed **`X-Launch-Token`** on every operator-scoped REST call (was `x-operator-id`).
There is **no house / operator-less mode**.

## Core rules

- **Scoping:** valid `X-Launch-Token` (active operator) → only that operator's data. Missing/invalid/
  expired token → generic **HTTP 401** (vague; reason logged server-side only). Invalid/inactive
  operator → SPA redirects **`/error`** (never reveal contract status). The operator id is the token
  `iss`, not a header.
- **Required operator:** `GameParameters.OperatorId` is non-nullable. Each operator owns **multiple**
  game types (not shared across operators).
- **Contract window:** `ContractStartDate` … optional `ContractEndDate` (null = open-ended). Expiry is
  timestamp-precise; future `ContractStartDate` is treated the same as expired for UX/API.
- **Mid-game expiry:** an in-progress game finishes (typer, payouts); new bets and new games
  for that operator are blocked.
- **Bets:** `POST /games/{id}/bets` requires a valid launch token; the token's operator must match
  the game's `OperatorId` (mismatch/inactive → vague rejection). Player identity comes from the
  token (regulated) or wallet (crypto) — see `features/launch-token-validation.md`.
- **Branding:** `GET /operators/{id}` only (no list); now token-guarded and also returns
  `paymentModel`. Returns name, icon, main page URL, optional color → frontend sets
  `--color-secondary` at runtime.
- **Empty lists:** `GET /games/active-types` and game history return **200 with `[]`** when empty.
- **SignalR:** game id in the hub URL is sufficient — no token on websocket connections.
- **Payments:** SOL only.
- **Non-prod seed:** when no operators exist, `ChimpOut.Consumers` seeds **DEV Cassino** and assigns
  default `GameParameters` to it. Local play: the dev launch buttons at `/chimpout/game` (dev-only;
  see `features/dev-qol-stuff.md`).

## Key code touchpoints

**Backend (`monkify-backend`)**

- Entity: `ChimpOut.Domain/Games/Entities/Operator.cs`; required FK on `GameParameters`.
- Migrations: `AddOperators`, `MakeOperatorIdRequired` in `ChimpOut.Infrastructure/Migrations/`.
- Identity: `LaunchTokenValidationMiddleware` (`ChimpOut.Api/Middleware/`) validates `X-Launch-Token`
  and populates `ILaunchContext`; controllers read operator/player from it (the old
  `GamesController.RequireOperatorId` / `x-operator-id` binding is removed). See
  `features/launch-token-validation.md`.
- Handlers: `GetActiveParameters`, `GetActiveGameForParameter`, `GetGameHistory`,
  `RegisterBet` — all carry required `OperatorId`; `GetOperatorById` under
  `Handlers/Operators/GetOperatorById/`.
- Controller: `OperatorsController` — `GET /operators/{id}` only.
- Services: operator-scoped `GameService.GetAllActiveGameParameters(Guid)`; internal
  `GetAllActiveGameParametersForAllOperators()` for consumers; cache keys
  `ActiveParameters:{operatorId}` and `Operator:{operatorId}` in `CacheHelper`.
- Seeding: `ChimpOut.Consumers/Program.cs` — `CreateDefaultOperatorAsync`, `CreateDefaultGameParametersAsync`,
  `OpenNewGames` (skips inactive operators).

**Frontend (`monkify-front`)**

- Routes: `src/routes.ts` — param-free `/chimpout/game/games`, `/chimpout/game/play`, `/error`;
  `/chimpout/game` is the dev launch landing (prod → `/error`). Operator id comes from the token `iss`
  (`src/utils/launchToken.ts` `getOperatorIdFromToken`), not the route.
- Composable: `src/hooks/useOperator.ts` — loads branding via `getOperator`, applies theme, redirects
  on failure.
- Interceptor: `src/services/mainService.ts` — attaches `X-Launch-Token` (from `useLaunch` /
  `src/utils/launchToken.ts`); 401 → `/error`. (Was `x-operator-id` from the route param.)
- Route helper: `src/utils/operatorRoute.ts` — `isValidOperatorGuid` (validates the operator id
  decoded from the token before the branding fetch).
- Types: `OperatorBranding` (now incl. `paymentModel`) in `src/types/index.ts`.

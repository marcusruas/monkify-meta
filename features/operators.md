# Feature — Operators (multi-tenancy)

> Status: **shipped.** Authoritative spec: `../specs/operator-feature.md`
> (merged via `feature/operators` on 2026-06-26).
>
> **Superseded (2026-06-30) by `features/launch-token-validation.md`:** the `x-operator-id` header is
> **removed**. Operator (and player) identity now comes from a signed **launch token**
> (`X-Launch-Token`, validated in `Monkify.Api` middleware). The multi-tenancy/contract/branding rules
> below still hold; only the transport changed — read the launch-token doc for the current request flow.
> Operators also gained `PaymentModel` (`Regulated` | `Crypto`) and `LaunchPublicKey`.

## What it does

Introduces **Operators** (casino/betting platforms, e.g. BetMGM, BetFury). Every `SessionParameters`
row **must** have an `OperatorId`. Players arrive at **`/monkify/game/<operatorId>`** (and
`/monkify/game/<operatorId>/play` for the live game); the frontend sends a signed
**`X-Launch-Token`** on every operator-scoped REST call (was `x-operator-id`). There is **no house /
operator-less mode**.

## Core rules

- **Scoping:** valid `X-Launch-Token` (active operator) → only that operator's data. Missing/invalid/
  expired token → generic **HTTP 401** (vague; reason logged server-side only). Invalid/inactive
  operator → SPA redirects **`/error`** (never reveal contract status). The operator id is the token
  `iss`, not a header.
- **Required operator:** `SessionParameters.OperatorId` is non-nullable. Each operator owns **multiple**
  session types (not shared across operators).
- **Contract window:** `ContractStartDate` … optional `ContractEndDate` (null = open-ended). Expiry is
  timestamp-precise; future `ContractStartDate` is treated the same as expired for UX/API.
- **Mid-session expiry:** an in-progress session finishes (typer, payouts); new bets and new sessions
  for that operator are blocked.
- **Bets:** `POST /sessions/{id}/bets` requires a valid launch token; the token's operator must match
  the session's `OperatorId` (mismatch/inactive → vague rejection). Player identity comes from the
  token (regulated) or wallet (crypto) — see `features/launch-token-validation.md`.
- **Branding:** `GET /operators/{id}` only (no list); now token-guarded and also returns
  `paymentModel`. Returns name, icon, main page URL, optional color → frontend sets
  `--color-secondary` at runtime.
- **Empty lists:** `GET /sessions/active-types` and session history return **200 with `[]`** when empty.
- **SignalR:** session id in the hub URL is sufficient — no token on websocket connections.
- **Payments:** SOL only.
- **Non-prod seed:** when no operators exist, `Monkify.Consumers` seeds **DEV Cassino** and assigns
  default `SessionParameters` to it. Local play: `/monkify/game/<dev-operator-guid>`.

## Key code touchpoints

**Backend (`monkify-backend`)**

- Entity: `Monkify.Domain/Sessions/Entities/Operator.cs`; required FK on `SessionParameters`.
- Migrations: `AddOperators`, `MakeOperatorIdRequired` in `Monkify.Infrastructure/Migrations/`.
- Identity: `LaunchTokenValidationMiddleware` (`Monkify.Api/Middleware/`) validates `X-Launch-Token`
  and populates `ILaunchContext`; controllers read operator/player from it (the old
  `SessionsController.RequireOperatorId` / `x-operator-id` binding is removed). See
  `features/launch-token-validation.md`.
- Handlers: `GetActiveParameters`, `GetActiveSessionForParameter`, `GetSessionHistory`,
  `RegisterBet` — all carry required `OperatorId`; `GetOperatorById` under
  `Handlers/Operators/GetOperatorById/`.
- Controller: `OperatorsController` — `GET /operators/{id}` only.
- Services: operator-scoped `SessionService.GetAllActiveSessionParameters(Guid)`; internal
  `GetAllActiveSessionParametersForAllOperators()` for consumers; cache keys
  `ActiveParameters:{operatorId}` and `Operator:{operatorId}` in `CacheHelper`.
- Seeding: `Monkify.Consumers/Program.cs` — `CreateDefaultOperatorAsync`, `CreateDefaultSessionParametersAsync`,
  `OpenNewSessions` (skips inactive operators).

**Frontend (`monkify-front`)**

- Routes: `src/routes.ts` — `/monkify/game/:operatorId`, `/monkify/game/:operatorId/play`, `/error`;
  `/` and bare `/monkify/game` redirect to `/error`.
- Composable: `src/hooks/useOperator.ts` — loads branding via `getOperator`, applies theme, redirects
  on failure.
- Interceptor: `src/services/mainService.ts` — attaches `X-Launch-Token` (from `useLaunch` /
  `src/utils/launchToken.ts`); 401 → `/error`. (Was `x-operator-id` from the route param.)
- Route helper: `src/utils/operatorRoute.ts` — GUID validation and path parsing (still used for the
  branding fetch path).
- Types: `OperatorBranding` (now incl. `paymentModel`) in `src/types/index.ts`.

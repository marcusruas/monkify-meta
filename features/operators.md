# Feature — Operators (multi-tenancy)

> Status: **shipped.** Authoritative spec: `../specs/operator-feature.md`
> (merged via `feature/operators` on 2026-06-26).

## What it does

Introduces **Operators** (casino/betting platforms, e.g. BetMGM, BetFury). Every `SessionParameters`
row **must** have an `OperatorId`. Players arrive at **`/monkify/game/<operatorId>`** (and
`/monkify/game/<operatorId>/play` for the live game); the frontend sends **`x-operator-id`** on every
operator-scoped REST call. There is **no house / operator-less mode**.

## Core rules

- **Scoping:** valid `x-operator-id` + active contract → only that operator's data. Missing or
  empty header → **HTTP 400** with a generic message. Invalid/inactive operator in the URL → redirect
  **`/error`** (vague; never reveal contract status).
- **Required operator:** `SessionParameters.OperatorId` is non-nullable. Each operator owns **multiple**
  session types (not shared across operators).
- **Contract window:** `ContractStartDate` … optional `ContractEndDate` (null = open-ended). Expiry is
  timestamp-precise; future `ContractStartDate` is treated the same as expired for UX/API.
- **Mid-session expiry:** an in-progress session finishes (typer, payouts); new bets and new sessions
  for that operator are blocked.
- **Bets:** `POST /sessions/{id}/bets` requires `x-operator-id` matching the session's `OperatorId`;
  mismatch or inactive operator → refund with vague error.
- **Branding:** `GET /operators/{id}` only (no list). Returns name, icon, main page URL, optional
  color → frontend sets `--color-secondary` at runtime.
- **Empty lists:** `GET /sessions/active-types` and session history return **200 with `[]`** when empty.
- **SignalR:** session id in the hub URL is sufficient — no `x-operator-id` on websocket connections.
- **Payments:** SOL only.
- **Non-prod seed:** when no operators exist, `Monkify.Consumers` seeds **DEV Cassino** and assigns
  default `SessionParameters` to it. Local play: `/monkify/game/<dev-operator-guid>`.

## Key code touchpoints

**Backend (`monkify-backend`)**

- Entity: `Monkify.Domain/Sessions/Entities/Operator.cs`; required FK on `SessionParameters`.
- Migrations: `AddOperators`, `MakeOperatorIdRequired` in `Monkify.Infrastructure/Migrations/`.
- Header validation: `SessionsController.RequireOperatorId` — `[FromHeader(Name = "x-operator-id")]`
  on all session GETs and bet POST.
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
- Interceptor: `src/services/mainService.ts` — attaches `x-operator-id` from route param.
- Route helper: `src/utils/operatorRoute.ts` — GUID validation and path parsing.
- Types: `OperatorBranding` in `src/types/index.ts`.

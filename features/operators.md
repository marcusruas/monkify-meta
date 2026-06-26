# Feature — Operators (multi-tenancy)

> Status: **distilled from spec.** Authoritative spec: `../specs/operator-feature.md`
> (branch `feature/operators`). Revised 2026-06-25 (*Enhance operator feature*).

## What it does

Introduces **Operators** (casino/betting platforms, e.g. BetMGM, BetFury). Every `SessionParameters`
row **must** have an `OperatorId`. Players arrive at **`/monkify/game/<operatorId>`**; the frontend sends
**`x-operator-id`** on every operator-scoped API call. There is **no house / operator-less mode**.

## Core rules

- **Scoping:** valid `x-operator-id` + active contract → only that operator's data. Missing header → **HTTP 400**.
  Invalid/inactive operator in URL → redirect **`/error`** (vague; never reveal contract status).
- **Required operator:** no sessions without an operator; `OperatorId` is non-nullable on `SessionParameters`.
  Each operator owns **multiple** session types (not shared).
- **Expiry:** timestamp-precise; future `ContractStartDate` = not active (same UX as expired).
- **Mid-session expiry:** active session finishes; block new bets and new sessions.
- **Bets:** `POST /sessions/{id}/bets` requires `x-operator-id` matching the session's `OperatorId`.
- **Branding:** `GET /operators/{id}` only (no list). Color → secondary theme token.
- **SignalR:** session id only — no operator header on hubs.
- **Payments:** SOL only.

## Key code touchpoints

- Entity: `Operator.cs`; required FK on `SessionParameters`.
- Header: `[FromHeader(Name = "x-operator-id")]` on session GETs and bet POST.
- `GetOperatorById` handler + `OperatorsController` (`GET /operators/{id}`).
- Operator-aware `SessionService` / cache (`ActiveParameters:{operatorId}`).
- Seeding: DEV Cassino (non-prod) in `Monkify.Consumers/Program.cs`.
- Frontend: route `/monkify/game/:operatorId`, `useOperator`, axios interceptor, `getOperator(id)`.

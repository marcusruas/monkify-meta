# Feature — Operators (multi-tenancy)

> Status: **stub / distilled.** Authoritative spec: `../specs/operator-feature.md`
> (branch `feature/operators`). This page is the short version; trust the spec + live code for details.

## What it does

Introduces **Operators** (casino/betting platforms, e.g. BetMGM, BetFury). `SessionParameters`
gains an optional `OperatorId`. Players arrive from an operator's site carrying an `operator-id`,
which the frontend persists and sends as the **`operator-id` request header** on every API call.
The backend partitions all read/write traffic by operator.

## Core rules

- **Scoping:** header present + parseable Guid → only that operator's data, **and only if the
  contract is active (started, not expired)**. Header absent/invalid → only house data
  (`OperatorId == null`).
- **Expired/inactive operators go dark:** no new sessions, no sessions/bets shown, no new bets — and
  the user only ever sees a **vague** "not found / unavailable" message. **Never reveal contract
  status.** (See `constraints.md` §7.)
- **Expiry is timestamp-precise:** `Operator.ContractHasExpired(utcNow)` = `ContractEndDate < utcNow`.
  A future `ContractStartDate` means not-yet-active.
- **Branding:** `GET /operators` returns branding (name, iconUrl, mainPageUrl, color) for active
  operators only. The frontend matches the current `operator-id` and applies the operator color as
  the **secondary** theme color.

## Key code touchpoints (live)

- Entity: `Monkify.Domain/Sessions/Entities/Operator.cs`; FK on `SessionParameters`.
- DbContext inline config + indexes; `Operators` DbSet.
- Header binding `[FromHeader(Name = "operator-id")]` on `SessionsController` GETs.
- Operator-aware filtering + cache key in `SessionService` / `CacheHelper`.
- `OperatorsController` + `Handlers/Operators/GetOperators`.
- Seeding: `CreateDefaultOperatorAsync` ("DEV Cassino", non-prod only) in `Monkify.Consumers/Program.cs`.
- Frontend: `src/hooks/useOperator.ts`, the axios interceptor header, `getOperators`, theming.

<!-- TODO: extract the full enforcement table and decisions (§7, §13) from the spec into here. -->

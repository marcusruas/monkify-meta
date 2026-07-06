# Implementation checklist (per repo)

Work through this while implementing from the plan (Step 6). Skip a row only when it genuinely does
not apply — say so in the report, don't silently omit. **Source of truth priority:** spec > Trello
card (refinement intent) > existing code patterns. When the spec and existing behavior conflict and
the spec doesn't resolve it, **stop and ask the user** (INDEX.md → "Implementing from a spec").

## Backend (`monkify-backend`)

- [ ] **Domain entities** — added/changed under `Monkify.Domain`; match the spec's data model.
- [ ] **EF mapping is inline** in `MonkifyDbContext.OnModelCreating` — no `IEntityTypeConfiguration`
      classes (per `constraints.md` §4).
- [ ] **Migration** — created with `dotnet ef migrations add <Name>` under
      `Monkify.Infrastructure/Migrations`; **never** hand-edit `MonkifyDbContextModelSnapshot.cs`.
      Migrations are applied **only** by `Monkify.Consumers` at startup — the API does not migrate.
- [ ] **MediatR handlers** — request/handler pairs follow the existing style; validation lives where
      the codebase already puts it.
- [ ] **MassTransit consumers** — session-flow / money / typer work stays in `Monkify.Consumers`
      (the API stays stateless).
- [ ] **API controllers + DTOs** (`Monkify.Api`) — routes, headers, and payloads match the spec's
      API surface. Reuse existing auth/token/header patterns (e.g. launch token, admin token).
- [ ] **SignalR contracts** — hub method + event names/shapes match the spec; front expectations align.
- [ ] **Operator scoping / multi-tenancy** — new behavior is operator-scoped where the spec requires;
      expired/inactive operators surface only a **vague** message (`constraints.md` §6).
- [ ] **Secrets/config** — only mock/placeholder values in `appsettings*.json` / `monkifysettings.json`
      (`constraints.md` §1).

## Frontend (`monkify-front`)

- [ ] **Composables** (`src/hooks`) — state/logic changes match the spec.
- [ ] **Services** (`src/services`) — axios calls hit the exact routes/headers the backend exposes.
- [ ] **Types** (`src/types`) — DTO/SignalR types match the backend contract.
- [ ] **Components** — UI matches the spec; copy respects vague-operator messaging where relevant.
- [ ] **Env vars** use `VUE_APP_*` (not `VITE_*`) unless the repo has migrated.

## Tests (mandatory — see `constraints.md` §3 and "Integration tests")

- [ ] **Unit tests** (`Monkify.Tests`, xUnit + Shouldly + Moq + Bogus, in-memory EF) — added for new
      behavior, updated for changed behavior; each acceptance criterion has a mapped assertion.
- [ ] **Happy-path integration test** (`Monkify.Tests.Integration`, Testcontainers) — drives the
      feature end to end against real Postgres/RabbitMQ/Redis, following `SessionFlowTests`. Required
      for every **new feature** (exempt: bug fixes / QOL / minor changes).
- [ ] **Complex-flow integration test** — a dedicated test for any particularly complex flow beyond
      the happy path (multi-step transition, refund/rollback, fail-closed branch).
- [ ] **Run + report** — `scripts/run_tests.bat` (unit) and `scripts/run_integration_tests.bat`
      (integration) are green; report exactly which tests changed/were added.

## Cross-feature sanity (before hand-off)

- [ ] No regression to sessions/bets, operators, payments/payouts, player identity, free-bet mode,
      chat, or admin flows the feature touches.
- [ ] Shared entities, headers, and public API shape stay consistent with what other `features/*.md`
      describe.
- [ ] Scope matches the card's **Out of scope** — nothing extra snuck in.

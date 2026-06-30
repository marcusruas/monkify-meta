# Monkify — Project Knowledge Base (INDEX)

> **Read this file first on every task.** It is the map of the Monkify knowledge base.
> It tells you where things live, what each document is for, and the rules you must never break.

Monkify is a real-time, multiplayer betting game where a simulated "monkey" types random
characters until a player's chosen word/sequence appears. Players bet on a sequence; when the
typer produces a matching sequence, the matching bets win and are paid out.

## How to use this knowledge base

1. **Always start here.** Then read `constraints.md` before writing or running anything.
2. Read `architecture.md` to understand the systems, repos, and data/message flow.
3. For domain vocabulary, read `glossary.md`.
4. For "how we write code here", read the relevant file in `conventions/`.
5. For a specific feature, read the relevant file in `features/`.
6. The authoritative, deep design docs are in `specs/`. The `features/` files are distilled
  summaries that point back into `specs/` and the live code.

## Implementing from a spec

When you are **implementing** a feature from a spec in `specs/` (not just reading for context):

1. Read the **target spec** end to end, plus its matching `features/` summary if one exists.
2. Read **every other doc in `features/`** — and skim related shipped specs — so you know what
   already exists in production (sessions, operators, payment flows, player identity, free-bet mode,
   etc.). The spec you are building is one piece of a shared system, not an isolated module.
3. While designing and coding, actively look for **cross-feature interactions**: shared entities
   and tables, request headers, payment and payout paths, session/bet lifecycle, operator scoping,
   frontend state and composables, migrations, SignalR contracts, and public API shape.
4. If you find a **conflict, ambiguity, or gap** between the spec you are implementing and
   **existing behavior documented elsewhere** — and the target spec does **not** already resolve
   it — **stop and ask the user**. Do not guess, silently pick one behavior, or paper over the
   mismatch with a local workaround.
5. When you ask, be concrete: name the existing feature, describe the conflicting behavior, quote
   or summarize what the spec says (or omits), and offer one or two sensible resolution options if
   you have them. Wait for an answer before proceeding on that point.

Treat unresolved cross-feature conflicts as **blockers**, not implementation details you can defer.

## Keeping the knowledge base current

Every time a feature gets merged into `main`, this repository's knowledge base **must be updated**
so it reflects the application's current business logic and behavior. That includes (as relevant)
`features/`, `conventions/`, `glossary.md`, `architecture.md`, and any other docs that describe
how the system works — not only `specs/`. Stale docs here mislead agents and humans alike; treat
knowledge-base updates as part of shipping the feature, not optional follow-up work.

## Repositories (in this workspace, `e:\Monkify`)


| Folder             | What it is                                                               | Covered here       |
| ------------------ | ------------------------------------------------------------------------ | ------------------ |
| `monkify-backend`  | .NET 8 backend: API, background consumers, domain, infrastructure, tests | Yes (primary)      |
| `monkify-front`    | Vue 3 + TypeScript frontend (the game UI)                                | Yes (primary)      |
| `monkify-deployer` | Deployment assets/tooling                                                | Not yet documented |
| `monkify-lp`       | Landing page                                                             | Not yet documented |
| `monkify-meta`     | **This knowledge base**                                                  | —                  |


> Note: when the user says "the backend" they mean `monkify-backend`; "the front" / "the UI"
> means `monkify-front`. (The repo is `monkify-backend`, not "monkify-back".)

## Map of this knowledge base


| File                        | Purpose                                                                                  | Status               |
| --------------------------- | ---------------------------------------------------------------------------------------- | -------------------- |
| `INDEX.md`                  | This map. Read first.                                                                    | Drafted              |
| `architecture.md`           | Repos, services, RabbitMQ, PostgreSQL, Redis, SignalR, Solana, EF, payment processors    | Drafted              |
| `constraints.md`            | Non-negotiable rules: secrets, branches per repo, unit tests, migrations                 | Drafted              |
| `glossary.md`               | Session, Bet, Operator, Typer, SessionParameters, etc.                                   | Drafted              |
| `conventions/backend.md`    | EF inline in DbContext, migrations in Consumers, MediatR handlers, MassTransit consumers | TODO (stub)          |
| `conventions/frontend.md`   | Vue 3, composables (`src/hooks`), types in `src/types`, axios service layer              | TODO (stub)          |
| `features/sessions.md`      | Session lifecycle, the typer, rewards                                                    | Drafted              |
| `features/operators.md`     | Multi-tenant operators; distilled from `specs/operator-feature.md`                       | Shipped              |
| `features/free-bet-mode.md` | Dev/QA free bets without Solana payment; distilled from `specs/free-bet-mode.md`         | Shipped              |
| `features/operator-player-identity.md` | Unified `OperatorPlayer` model; remove `Bet.Wallet`; distilled from `specs/operator-player-identity.md` | Shipped              |
| `features/typer-pacing-scalability.md` | Wall-clock typer pacing, fallback winner, duration bounds; distilled from `specs/typer-pacing-scalability.md` | Shipped              |
| `features/launch-token-validation.md` | Signed casino launch flow (`X-Launch-Token`); `Operator.PaymentModel`; replaces `x-operator-id` | Shipped              |
| `features/operator-wallet-api.md` | Regulated (Model A) operator wallet debit/credit/refund; unified `TransactionLog`; commission removed | Shipped              |
| `specs/operator-feature.md` | Full implementation spec for Operators                                                   | Shipped              |
| `specs/free-bet-mode.md`    | Full implementation spec for dev-only Free Bet Mode                                      | Shipped              |
| `specs/operator-player-identity.md` | Operator-scoped unified player model (`OperatorPlayer`, remove `Bet.Wallet`)     | Shipped              |
| `specs/typer-pacing-scalability.md` | Typer wall-clock pacing — consistent duration across configs and servers           | Shipped              |
| `specs/launch-token-validation.md` | Signed casino launch flow — RS256 launch token, `Operator.PaymentModel`, prefs endpoint | Shipped              |
| `specs/operator-wallet-api.md` | Model A (Regulated) operator wallet debit/credit/refund adapters; unify `TransactionLog`, drop `RefundLog`, remove commission | Shipped              |
| `specs/dev-qol-stuff.md`    | Dev QOL: operatorId out of game URL (decode token `iss`), dev payment-model launch buttons, Testcontainers session integration tests | Ready                |
| `skills/`                   | Agent Skills that power the Monkify task pipeline (see below)                            | Drafted              |


## Skills (the task pipeline)

Reusable Agent Skills live in `skills/`. They are the source of truth, version-controlled here.
Because this repo's `skills/` folder is **not** an auto-discovery path, AGENTS.md installs them into
the Cursor- and Claude-discovered locations (`.cursor/skills/` and `.claude/skills/`) on first use —
see "Skills — ensure installed" in `AGENTS.md`.


| Skill                         | Invoke                  | Purpose                                                                                                                                                                                                                                      |
| ----------------------------- | ----------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `skills/monkify-refine-tasks` | `/monkify-refine-tasks` | Pick a card from the Trello **monkaify** board and refine it (Monkify-wide, both apps) into a clear, unambiguous, spec-ready task; write it back and move it to **Refined**. Needs the `trello` MCP + Superpowers.                           |
| `skills/monkify-create-spec`  | `/monkify-create-spec`  | From a **Refined** card: ingest card + chat context, explore code, create `feature/<slug>` branches, write `specs/<slug>.md`, update INDEX, move card to **Specs created**. Needs Trello MCP.                                              |
| `skills/monkify-ship-feature` | `/monkify-ship-feature` | Merge a feature branch into `main` (each affected repo), verify backend tests, sync the knowledge base from the linked Trello card (**branch + spec read from card Implementation section**), spec, and merged code; move card to **Implemented**. |


## Feature currently in flight

- **DEV QOL Stuff** — spec `specs/dev-qol-stuff.md` (Ready for implementation). Trello:
  https://trello.com/c/dHExBJHg #28. Branch `feature/dev-qol-stuff` in `monkify-backend`,
  `monkify-front`, and `monkify-meta`. Three things: (A) remove `operatorId` from the game URL — the
  frontend decodes the operator from the launch token's `iss`; the dev mint endpoint returns `{ token }`
  only; (B) replace the silent dev auto-mint with two dev-only Crypto/Regulated launch buttons on a
  token-less `/monkify/game` landing; (C) new `Monkify.Tests.Integration` project (Testcontainers:
  Postgres/RabbitMQ/Redis) running representative session types end-to-end with free + paid (Solana
  mocked) bets. On ship, the launch-URL example in `features/launch-token-validation.md` must drop the
  `/<operatorId>` segment.
  (Last shipped: **Operator wallet API (Model A / Regulated)** — `feature/operator-wallet-api`,
  2026-06-30; per-operator wallet debit/credit/refund adapters + mock, unified `TransactionLog`
  (`RefundLog` dropped), commission removed. Trello #19.)
  Backlog follow-ups: Monkify commission per bet → https://trello.com/c/BXgxJcwP #26;
  currency conversion / FX rendering → https://trello.com/c/ZyJViMKI #25.

## The 60-second mental model

- **Backend** (`monkify-backend`) is a layered .NET 8 solution split into a web **API**
(`Monkify.Api`) and a background worker host (`Monkify.Consumers`). They share `Monkify.Domain`,
`Monkify.Infrastructure`, and `Monkify.Common`.
- A **bet** is placed via `POST /sessions/{id}/bets`. The handler validates the on-chain Solana
payment signature, persists the bet, and publishes a `BetPlacedEvent`.
- **Sessions** progress through a state machine driven by **RabbitMQ events** (via MassTransit)
consumed in `Monkify.Consumers`: `BetPlaced → RunSession → RewardSession`, with
`RefundInvalidBet` for failures.
- The **typer** (`MonkifyTyper`) runs the game: it emits random characters over **SignalR** until
a bet's choice appears; winners are then rewarded via **Solana** SPL token transfers.
- **PostgreSQL** (EF Core / Npgsql) is the store. **Redis** caches active parameters and backs
SignalR. **Hangfire** (on PostgreSQL) schedules lottery sessions.
- **Frontend** (`monkify-front`) is Vue 3 + TypeScript: composables in `src/hooks`, an axios
service layer in `src/services`, and SignalR clients to stream the live typer + bets.

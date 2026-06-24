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

## Repositories (in this workspace, `e:\Monkify`)

| Folder | What it is | Covered here |
| --- | --- | --- |
| `monkify-backend` | .NET 8 backend: API, background consumers, domain, infrastructure, tests | Yes (primary) |
| `monkify-front` | Vue 3 + TypeScript frontend (the game UI) | Yes (primary) |
| `monkify-deployer` | Deployment assets/tooling | Not yet documented |
| `monkify-lp` | Landing page | Not yet documented |
| `monkify-meta` | **This knowledge base** | — |

> Note: when the user says "the backend" they mean `monkify-backend`; "the front" / "the UI"
> means `monkify-front`. (The repo is `monkify-backend`, not "monkify-back".)

## Map of this knowledge base

| File | Purpose | Status |
| --- | --- | --- |
| `INDEX.md` | This map. Read first. | Drafted |
| `architecture.md` | Repos, services, RabbitMQ, PostgreSQL, Redis, SignalR, Solana, EF, payment processors | Drafted |
| `constraints.md` | Non-negotiable rules: secrets, branches per repo, unit tests, migrations | Drafted |
| `glossary.md` | Session, Bet, Operator, Typer, SessionParameters, PaymentMethod, etc. | TODO (stub) |
| `conventions/backend.md` | EF inline in DbContext, migrations in Consumers, MediatR handlers, MassTransit consumers | TODO (stub) |
| `conventions/frontend.md` | Vue 3, composables (`src/hooks`), types in `src/types`, axios service layer | TODO (stub) |
| `features/sessions.md` | Session lifecycle, the typer, rewards | TODO (stub) |
| `features/operators.md` | Multi-tenant operators; distilled from `specs/operator-feature.md` | TODO (stub) |
| `features/payment-methods.md` | Multiple payment methods; distilled from `specs/payment-methods-feature.md` | TODO (stub) |
| `specs/operator-feature.md` | Full implementation spec for Operators | Authoritative source |
| `specs/payment-methods-feature.md` | Full implementation spec for Multiple Payment Methods | Authoritative source |

## The 60-second mental model

- **Backend** (`monkify-backend`) is a layered .NET 8 solution split into a web **API**
  (`Monkify.Api`) and a background worker host (`Monkify.Consumers`). They share `Monkify.Domain`,
  `Monkify.Infrastructure`, and `Monkify.Common`.
- A **bet** is placed via `POST /sessions/{id}/bets`. The handler validates the inbound payment
  (per payment method), persists the bet + payment + ledger row, and publishes a `BetPlacedEvent`.
- **Sessions** progress through a state machine driven by **RabbitMQ events** (via MassTransit)
  consumed in `Monkify.Consumers`: `BetPlaced → RunSession → RewardSession`, with
  `RefundInvalidBet` for failures.
- The **typer** (`MonkifyTyper`) runs the game: it emits random characters over **SignalR** until
  a bet's choice appears; winners are then rewarded by a **payment processor** (Solana / PayPal /
  Stripe / etc.).
- **PostgreSQL** (EF Core / Npgsql) is the store. **Redis** caches active parameters and backs
  SignalR. **Hangfire** (on PostgreSQL) schedules lottery sessions.
- **Frontend** (`monkify-front`) is Vue 3 + TypeScript: composables in `src/hooks`, an axios
  service layer in `src/services`, and SignalR clients to stream the live typer + bets.

## Two features currently in flight

Both are already reflected in the live code (entities, handlers, processors, frontend types):

- **Operators** — multi-tenant scoping by an `operator-id` request header. See
  `specs/operator-feature.md` (branch `feature/operators`).
- **Multiple Payment Methods** — N payment methods per game, a payment-processor strategy, and an
  auditable money ledger. See `specs/payment-methods-feature.md` (branch `feature/payment-methods`).

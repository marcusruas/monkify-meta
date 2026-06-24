# Monkify refinement checklist

Drive the refinement dialogue with this. For the card at hand, work top to bottom and ask the user
about anything the card does not already answer. The point is to surface impact and ambiguity across
**all** Monkify features and **both** apps (`monkify-backend`, `monkify-front`) before a spec exists.

> Read the live `features/` docs in `monkify-meta` for the current truth of each feature. This list
> is the *prompt* for questions, not a substitute for those docs.

## 1. Scope & intent

- [ ] What problem does this card solve, in one sentence? Who is it for (player, operator, ops)?
- [ ] Is it a new feature, a change to existing behavior, or a bug fix?
- [ ] What is explicitly **out of scope**?

## 2. Feature-by-feature impact (ask which apply)

For each area: does this card change it, depend on it, or break an assumption it relies on?

- [ ] **Sessions & the typer** — session lifecycle/state machine, `MonkifyTyper`, character stream,
      win detection, session parameters, scheduled/lottery sessions.
- [ ] **Bets** — placing a bet (`POST /sessions/{id}/bets`), validation, choice handling, bet status.
- [ ] **Payments & payment methods** — the `PaymentMethod` set, per-method processing/strategy,
      inbound payment proof, free-coin vs paid flows, currency display.
- [ ] **Operators / multi-tenancy** — `operator-id` header scoping, header present vs absent,
      contract window (active / future-start / expired), operator branding/theming.
- [ ] **Rewards & refunds** — payout math, refund of invalid bets, the money/transaction ledger,
      commission.
- [ ] **Realtime** — SignalR streaming of typer output, bets, status, display speed.
- [ ] **Cross-cutting** — auth/headers, rate limiting, caching (Redis), scheduling (Hangfire).

## 3. Backend impact (`monkify-backend`)

- [ ] Domain entities / value objects / enums added or changed?
- [ ] MediatR handlers and/or MassTransit consumers affected?
- [ ] EF mapping (inline in `MonkifyDbContext.OnModelCreating`) and a **migration** needed?
      (Migrations apply only via `Monkify.Consumers`.)
- [ ] API surface: new/changed endpoints, request/response shapes, headers?
- [ ] Which host owns new work — stateless `Monkify.Api` vs background `Monkify.Consumers`?
- [ ] Unit tests in `Monkify.Tests` that must be updated/added.

## 4. Frontend impact (`monkify-front`)

- [ ] Composables (`src/hooks`), services (`src/services`), and types (`src/types`) affected?
- [ ] New/changed API calls and the headers they must send (e.g. `operator-id`)?
- [ ] Live UI behavior (typer/bets streaming), theming, currency/display formatting?
- [ ] Empty/error/loading states and the exact user-facing copy (keep operator errors vague).

## 5. Edge cases, failure modes, concurrency

- [ ] What happens mid-session (changes applied while a session is running)?
- [ ] Operator contract expired / not yet started / no operator header at all?
- [ ] Payment fails, is duplicated, or is refunded — what is the user/state outcome?
- [ ] Free-coin vs paid divergence?
- [ ] Race conditions / double-submit / retries / idempotency.
- [ ] Backward compatibility with existing sessions, bets, and data.

## 6. Constraints to confirm (from `constraints.md`)

- [ ] Secrets/config: only mock values; never reveal real secrets.
- [ ] Branch-per-repo for cross-repo features (`monkify-backend` + `monkify-front`).
- [ ] Operator messaging stays vague ("not found / unavailable"); never reveal contract status.
- [ ] Backend behavior changes require updated/added unit tests, reported at the end.

## 7. Exit bar

- [ ] Every "how should it behave when…" question has a concrete, agreed answer.
- [ ] Acceptance criteria are testable (Given / When / Then).
- [ ] A spec-writer could produce an implementation spec from this card **without** asking anything.

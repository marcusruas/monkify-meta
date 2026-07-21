# Feature — Refund safeguards

> Status: **shipped.** Authoritative spec: `../specs/refund-safeguards.md`
> (merged via `feature/refund-safeguards` on 2026-07-20).

## What it does

Makes `BetStatus.NeedsRefunding` / `Refunded` a **real, self-clearing bet lifecycle** and gives
operators a way to recover stuck money. Two safeguards: (1) a background **pending-refund sweep** runs
every 12 hours and refunds every bet sitting in `NeedsRefunding`; (2) an operator-admin **"refund by
payment signature"** action marks a specific bet for refund. Before this, `NeedsRefunding`/`Refunded`
were defined but never set — refunds were fire-and-forget (a failed refund only logged an error and
the money was never returned), and there was no operator-facing way to force a refund.

## Core rules

- **Self-clearing refund lifecycle.** `RefundInvalidBetConsumer` sets `Bet.Status = Refunded` on any
  **successful** `Refund()` — this applies to **every** refund routed through the consumer, including
  the three pre-existing inline triggers (under-subscribed lottery, game-type deactivation, invalid
  SolanaToken bet), which previously left the bet `Made`. On a **failed** refund the status is left
  untouched, so a swept `NeedsRefunding` bet is retried on the next sweep; an inline-trigger bet that
  fails stays `Made` (logged only, as before). The flip is done via a minimal tracked re-query
  (`MarkRefunded`), because `SpaceStrokerDbContext` is NoTracking by default.
- **12-hour sweep.** `PendingRefundSweepJob` is a Hangfire recurring job in `SpaceStroker.Consumers`
  (cron `0 */12 * * *`, id `pending-refund-sweep`). It loads **all** bets with
  `Status == NeedsRefunding` (all operators, **any** game state) and publishes one existing
  `RefundInvalidBetEvent` per bet (`Wallet = bet.GetWalletAddress()`, `Value = bet.Amount`, reason
  "Pending-refund safeguard sweep."). The sweep itself changes no status — it only publishes; the
  consumer does the money movement + status flip. Failed refunds retry every 12h indefinitely.
- **Admin refund by payment signature.** `POST /admin/bets/refund` (operator admin portal, under the
  `X-Admin-Token` guard) takes `{ paymentSignature }` and marks the matching bet `NeedsRefunding`. It
  is the **only** producer of `NeedsRefunding` — the money moves later via the sweep (single executor;
  a manual refund may sit up to ~12h). It does **not** refund immediately.
- **Lookup + operator scoping.** The bet is located by `Bet.ExternalTransactionReference ==
  paymentSignature` (the on-chain signature for `SolanaToken`, the wallet debit reference for
  `Standard`), **scoped to the admin's operator** (`Game.Parameters.OperatorId ==
  IAdminContext.OperatorId`). An unknown signature **or** a bet belonging to another operator both
  return a vague "No bet found for that signature." (never reveals another operator's data).
- **Guards (money-safety).** The admin action rejects a bet that is already `Rewarded` or `Refunded`
  ("This bet has already been rewarded or refunded and cannot be refunded.") — refunding a `Rewarded`
  bet would double-pay, because the refund amount (`CalculateRemainingBetPayout`) nets out prior
  **refund** movements only, not reward credits. It also rejects a **free bet** (`Amount == 0`, "Free
  bets have no payment to refund.") since the consumer skips amount-0 without clearing the status.
  There is **no game-state restriction** — any `GameStatus` is allowed.
- **Idempotent.** Re-marking a bet that is already `NeedsRefunding` is a success no-op. Ledger-based
  refund idempotency still applies: if a `RefundInvalidBetEvent` is delivered twice,
  `CalculateRemainingBetPayout` returns `BetHasAlreadyBeenRefunded` (from the `Refund` ledger rows) so
  no money moves twice.
- **Refund amount.** A `NeedsRewarding` winner whose payout failed and is then marked for refund gets
  its **stake** back (the remaining stake minus prior refunds), not winnings.
- **Host split.** The sweep + all money movement run in `SpaceStroker.Consumers`; the admin endpoint only
  reads and flips a status column (no money, no Solana/wallet call, no key decrypt), so it stays in the
  stateless `SpaceStroker.Api`.
- **No schema change.** `BetStatus.NeedsRefunding`/`Refunded` already existed and
  `Bet.ExternalTransactionReference` was already indexed — no entity/mapping change, no migration.

## Key code touchpoints

**Backend (`monkify-backend`)**

- `SpaceStroker.Infrastructure/Jobs/PendingRefundSweepJob.cs` — the 12h sweep (reads `NeedsRefunding`,
  publishes `RefundInvalidBetEvent`).
- `SpaceStroker.Consumers/Program.cs` — `AddScoped<PendingRefundSweepJob>()` +
  `recurringJobs.AddOrUpdate<PendingRefundSweepJob>("pending-refund-sweep", j => j.Run(), "0 */12 * * *")`.
- `SpaceStroker.Infrastructure/Consumers/RefundInvalidBet/RefundInvalidBetConsumer.cs` — sets
  `Refunded` on success via `MarkRefunded` (minimal tracked re-query); leaves status on failure.
- `SpaceStroker.Infrastructure/Handlers/Admin/RefundBet/` — `RefundBetRequest` + `RefundBetHandler`
  (lookup by `ExternalTransactionReference`, operator-scoped, guards, set `NeedsRefunding`).
- `SpaceStroker.Infrastructure/Contracts/Admin/RefundBetBySignatureBody.cs` — `{ PaymentSignature }`.
- `SpaceStroker.Api/Controllers/AdminController.cs` — `POST /admin/bets/refund` (+ `bets/refund` OPTIONS
  preflight).
- `SpaceStroker.Domain/Bets/Services/BetDomainService.cs` — `CalculateRefundForInvalidBet` /
  `CalculateRemainingBetPayout` (unchanged; the refund amount math).

**Frontend (`monkify-front`)**

- `src/components/admin/BetRefundManager.vue` — the "Refund a Bet" panel (payment-signature input +
  Refund button, toast feedback).
- `src/components/admin/GameTypeManager.vue` — new `refunds` tab mounting `BetRefundManager`.
- `src/services/adminService.ts` — `refundBet(paymentSignature)` → `POST /admin/bets/refund` on the
  `X-Admin-Token` `adminAPI` instance.

**Tests**

- Unit (`SpaceStroker.Tests`): `PendingRefundSweepJobTests`, `RefundBetHandlerTests`, and
  `RefundInvalidBetConsumerTests` (the `Refunded`-on-success flip + failure-keeps-`NeedsRefunding`).
- Integration (`SpaceStroker.Tests.Integration`): `RefundSafeguardsTests` (sweep happy-path,
  admin-mark → sweep → refund, guards) + harness helpers `SetBetStatusAsync` /
  `RunPendingRefundSweepAsync`.

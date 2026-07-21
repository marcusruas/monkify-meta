# ChimpOut — "Refund safeguards" Feature — Implementation Spec

> **Status: Shipped** — merged via `feature/refund-safeguards` on 2026-07-20 (386 unit + 39
> integration tests green). Distilled summary: `../features/refund-safeguards.md`. Originally refined
> via Trello card [Refund safeguards](https://trello.com/c/MpfBtpE4/71-refund-safeguards) (2026-07-20).
> Repos affected: `monkify-backend`, `monkify-front`, and this doc in `monkify-meta`.
>
> **Purpose:** Make `NeedsRefunding` a real, self-clearing bet lifecycle state and give operators a way
> to recover stuck money. Two safeguards: (1) a background job that periodically refunds every bet
> sitting in `NeedsRefunding`, and (2) an operator-admin action that marks a specific bet (found by its
> payment signature) for refund. Together they turn today's fire-and-forget inline refund into something
> that retries on failure and can be triggered manually.

---

## 1. Summary

Today `BetStatus.NeedsRefunding` and `BetStatus.Refunded` exist
(`ChimpOut.Domain/Bets/ValueObjects/BetStatus.cs`) but are **never set in production**. Refunds are
done inline/immediately: three triggers publish `RefundInvalidBetEvent`
(`ChimpOut.Infrastructure.Consumers.RefundBet`) → `RefundInvalidBetConsumer` calls the payment
strategy's `Refund()`. If that refund **fails, the consumer only logs an error**
(`RefundInvalidBetConsumer.cs:52-53`) — the money is never returned and there is no retry. And there is
no operator-facing way to force a refund of a specific bet.

This card adds:

| Part | What | Host |
| --- | --- | --- |
| **1. Sweep** | A Hangfire recurring job every 12h that loads all `NeedsRefunding` bets and publishes one `RefundInvalidBetEvent` per bet. | `ChimpOut.Consumers` |
| **2. Self-clearing** | `RefundInvalidBetConsumer` sets `Bet.Status = Refunded` on any **successful** refund (all refunds, inline triggers included); on failure it leaves the status untouched so a swept bet retries next cycle. | `ChimpOut.Consumers` |
| **3. Admin refund by signature** | `POST /admin/bets/refund { paymentSignature }` marks a bet (found by `ExternalTransactionReference`, operator-scoped) as `NeedsRefunding`. Money moves later via the sweep. | `ChimpOut.Api` + `monkify-front` |

**Scope shape (deliberate):** the sweep is the **single executor** of refunds it drives; the admin
action is the **sole new producer** of `NeedsRefunding`. The three existing inline triggers are
otherwise unchanged (they keep publishing the same event; they simply also end `Refunded` now).

### Flow at a glance

```
Admin clicks Refund (payment signature)
   → POST /admin/bets/refund  → Bet.Status = NeedsRefunding   (no money yet)
        ... up to 12h ...
PendingRefundSweepJob (every 12h)
   → for each NeedsRefunding bet: publish RefundInvalidBetEvent
        → RefundInvalidBetConsumer.Refund()
             success → Bet.Status = Refunded   (sweep won't re-pick)
             failure → stays NeedsRefunding     (retried next sweep)
```

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo:** `feature/refund-safeguards` in `monkify-backend`, `monkify-front`,
  `monkify-meta`; hub stays on `main`. (Worktrees already created — see the card **Implementation**
  block.)
- **Unit tests:** update/add in `ChimpOut.Tests` (run via `scripts/run_tests.bat` — **not** the full
  `dotnet test`, which includes the slow `ProductionSessions` Monte-Carlo suite); report changed tests
  at the end of implementation.
- **Integration tests (Feature — mandatory):** at least one happy-path test in
  `ChimpOut.Tests.Integration` (Testcontainers), **plus** a dedicated test for the complex flow
  (admin-mark → sweep → refund). Run via `scripts/run_integration_tests.bat`.
- **EF mapping:** inline in `ChimpOutDbContext.OnModelCreating`; no `IEntityTypeConfiguration`.
  **No schema change is needed here** (see §4).
- **Migrations:** applied only by `ChimpOut.Consumers`. **None expected** for this feature.
- **Host split:** the API stays stateless (marks status only, no money movement); all money/refund and
  the sweep run in `ChimpOut.Consumers`.
- **Admin messaging:** authenticated `/admin` endpoints may return **specific** field-level messages
  (the vague-operator-messaging rule applies only to the launch/contract path, not the admin path). The
  cross-operator/not-found case is still answered vaguely ("no bet found for that signature") so one
  operator can't probe another's data.
- **Secrets/config:** mock-only values in committed files.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | The 12h sweep reads **only** bets in `NeedsRefunding` and publishes a refund event per bet. It does **not** change the three existing inline-refund triggers. |
| 2 | The **admin action is the sole new mechanism** that sets `NeedsRefunding`. |
| 3 | On a **successful** `Refund()`, `RefundInvalidBetConsumer` sets `Bet.Status = Refunded`. This applies to **every** refund through the consumer, including the three existing inline triggers (which today leave the bet `Made` → they now end `Refunded`). One consistent rule; the sweep self-clears. |
| 4 | On refund **failure**, the consumer leaves the status untouched → a swept `NeedsRefunding` bet is naturally **retried** on the next sweep; existing inline-trigger bets stay `Made` (unchanged from today, still only logged). |
| 5 | Admin refund lookup is by **`ExternalTransactionReference == paymentSignature`** (method-agnostic — on-chain signature for SolanaToken, wallet debit reference for Standard), **scoped to the admin's operator** (`Game.Parameters.OperatorId == IAdminContext.OperatorId`). Must resolve to **exactly one** bet. |
| 6 | Admin refund **guards:** reject if the bet is `Rewarded` or `Refunded` (money-safety — the refund calc does not net out reward credits, so refunding a `Rewarded` bet would double-pay); reject a **free bet** (`Amount == 0`). **No game-state restriction** — any `GameStatus` is allowed. |
| 7 | Admin refund is **idempotent**: a bet already `NeedsRefunding` returns success as a no-op. |
| 8 | Admin action marks status **only**; the money moves via the 12h sweep (single executor; a manual refund may sit up to ~12h). No immediate publish. |
| 9 | Failed refunds retry **indefinitely** every 12h (logged each time). Escalation to `NeedsManualAnalysis` after N tries is **out of scope**. |
| 10 | Refund amount is the existing idempotent `BetDomainService.CalculateRefundForInvalidBet` (full remaining stake minus prior refunds). A `NeedsRewarding` winner whose payout failed gets its **stake** back, not winnings. |

---

## 4. Data model

**No schema change, no migration.** Everything needed already exists:

- `BetStatus` enum already has `NeedsRefunding` (value 3) and `Refunded` (value 4) —
  `ChimpOut.Domain/Bets/ValueObjects/BetStatus.cs:9-17`. This feature simply starts **using** them.
- `Bet.ExternalTransactionReference` (`ChimpOut.Domain/Bets/Entities/Bet.cs:65`) is the payment
  signature; it is already **indexed** and length-capped (100) in
  `ChimpOutDbContext.OnModelCreating` (`ChimpOutDbContext.cs:185`, `:176`) — so the admin lookup by
  signature is index-backed.
- `Bet.Status` (`Bet.cs:69`) is already a mapped column; flipping it needs no mapping change.

If the implementer confirms the model snapshot has no pending diff after the code changes (it should
not — no entity/mapping change), **no `dotnet ef migrations add` is required**. Do not add an empty
migration.

---

## 5. Configuration

No new config. The sweep cadence is a **fixed cron** in code (`0 */12 * * *`), matching how the
existing `game-chat-purge` job hardcodes `Cron.Daily()` — there is no per-job settings entry today and
we do not add one. (If a future card wants it configurable, add `GeneralSettings` then; out of scope
here.)

---

## 6. Backend behaviour

### 6.1 New Hangfire job — `PendingRefundSweepJob`

New file `ChimpOut.Infrastructure/Jobs/PendingRefundSweepJob.cs`, mirroring
`ChimpOut.Infrastructure/Jobs/ChatMessagePurgeJob.cs` (a plain class with a single async method,
resolved by Hangfire's DI activator).

```csharp
namespace ChimpOut.Infrastructure.Jobs
{
    /// <summary>
    /// Publishes a refund event for every bet stuck in NeedsRefunding. Scheduled as a 12-hourly
    /// Hangfire recurring job in ChimpOut.Consumers. The RefundInvalidBetConsumer does the actual
    /// money movement and clears the status to Refunded on success; a bet that fails to refund stays
    /// NeedsRefunding and is retried on the next run.
    /// </summary>
    public class PendingRefundSweepJob
    {
        private readonly ChimpOutDbContext _context;
        private readonly IMessageBus _messageBus;

        public PendingRefundSweepJob(ChimpOutDbContext context, IMessageBus messageBus) { ... }

        public async Task Run()
        {
            // NoTracking read (default) — we only publish; the consumer loads + mutates the bet.
            // Include OperatorPlayer so GetWalletAddress() resolves the refund destination.
            var pending = await _context.GameBets
                .Include(x => x.OperatorPlayer)
                .Where(x => x.Status == BetStatus.NeedsRefunding)
                .ToListAsync();

            foreach (var bet in pending)
            {
                await _messageBus.PublishEventAsync(new RefundInvalidBetEvent(
                    bet.Id,
                    bet.GetWalletAddress(),      // BetExtensions.GetWalletAddress()
                    bet.Amount,
                    "Pending-refund safeguard sweep."));
            }

            Log.Information("Pending-refund sweep published {Count} refund event(s).", pending.Count);
        }
    }
}
```

Notes:
- `IMessageBus` is the publish abstraction (`ChimpOut.Infrastructure.Services.Messaging`), already used
  by `DeactivateGameTypeHandler`.
- Free bets (`Amount == 0`) should never be in `NeedsRefunding` (the admin action rejects them, §6.3);
  even if one slips in, `RefundInvalidBetConsumer` skips `Value == 0` — see §6.2 for the caveat and
  why the producer never marks free bets.
- The `Where` is a simple status filter across all operators/games — no operator scoping (system job).

### 6.2 Changed consumer — `RefundInvalidBetConsumer` sets `Refunded` on success

File `ChimpOut.Infrastructure/Consumers/RefundInvalidBet/RefundInvalidBetConsumer.cs`.

Today it loads the bet `AsNoTracking()` (`:66`) and never persists a status change. Change:

1. Load the bet **tracked** (drop `AsNoTracking()` from `LoadBet`, or load a tracked entity for the
   mutation) so `SaveChanges` can persist the flip.
2. After `Refund()` returns:
   - `result.IsSuccess == true` → set `bet.Status = BetStatus.Refunded` and
     `await dbContext.SaveChangesAsync()`.
   - `result.IsSuccess == false` → keep the existing `Log.Error(...)` and **do not** change status.

```csharp
var result = await paymentResolver.Resolve(bet.Game.Parameters)
    .Refund(bet, refundAmount.Value, message.Reason, CancellationToken.None);

if (result.IsSuccess)
{
    bet.Status = BetStatus.Refunded;
    await dbContext.SaveChangesAsync();
}
else
{
    Log.Error("Refund failed for bet {BetId}. Reason: {Reason}. Error: {Error}",
        bet.Id, message.Reason, result.Error);
}
```

Consequences (intended):
- The three inline triggers (`BetPlacedConsumer` under-subscribed lottery, `GameTypeDeactivatedConsumer`,
  `RegisterBetHandler` invalid crypto bet) now leave their refunded bets at `Refunded` instead of `Made`.
  This is a behaviour change asserted by updated unit/integration tests (§11, §12).
- Idempotency is unchanged: `BetDomainService.CalculateRefundForInvalidBet` →
  `CalculateRemainingBetPayout` still returns `BetHasAlreadyBeenRefunded` (early `return` at
  `RefundInvalidBetConsumer.cs:44-48`) when the ledger already has a full `Refund`, so a duplicate event
  moves no money. When that early-return fires, the status flip is skipped — acceptable, because a
  bet that was already fully refunded is either already `Refunded` (inline path set it) or will be
  cleared on the delivery that actually moved the money.

**Free-bet caveat:** the consumer's `IsFreeBet` guard returns early (`:23-27`) **without** setting
`Refunded`. If a free bet were ever marked `NeedsRefunding`, the sweep would re-publish it forever. The
admin action therefore **rejects free bets** up front (§6.3), and no other producer marks free bets.
No consumer change for free bets.

### 6.3 New admin endpoint — refund by payment signature

**Controller** `ChimpOut.Api/Controllers/AdminController.cs` — add a route (guarded by the existing
`AdminTokenValidationMiddleware`, which covers any `/admin/*` path, operator = token `iss`):

```csharp
[HttpPost("bets/refund")]
public async Task<IActionResult> RefundBet([FromBody] RefundBetBySignatureBody body)
{
    return await ProcessRequest(new RefundBetRequest(_adminContext.OperatorId, body.PaymentSignature));
}
```

Also add `bets/refund` to the `[HttpOptions(...)]` CORS-preflight list at the top of the controller
(the admin API is called cross-origin from the SPA).

**Contract** `ChimpOut.Infrastructure/Contracts/Admin/RefundBetBySignatureBody.cs`:

```csharp
public class RefundBetBySignatureBody
{
    public string PaymentSignature { get; set; } = string.Empty;
}
```

**Request + handler** under `ChimpOut.Infrastructure/Handlers/Admin/RefundBet/`
(`RefundBetRequest : IRequest<Guid?>`, `RefundBetHandler : BaseRequestHandler<RefundBetRequest, Guid?>`),
mirroring `DeactivateGameTypeHandler` (tracked query, `Messaging.AddValidationFailureMessage(...)` +
`throw new ValidationFailureException()` for rejects, return the id on success):

Logic:
1. Trim the signature; if empty → validation message "No bet found for that signature." + throw.
2. Load the bet **tracked**, joined to its game/parameters, matching `ExternalTransactionReference ==
   paymentSignature` **and** `Game.Parameters.OperatorId == request.OperatorId`:

   ```csharp
   var bet = await Context.GameBets
       .AsTracking()
       .Include(x => x.Game).ThenInclude(g => g.Parameters)
       .FirstOrDefaultAsync(x => x.ExternalTransactionReference == signature
                              && x.Game.Parameters.OperatorId == request.OperatorId,
                              cancellationToken);
   ```
   - `null` → "No bet found for that signature." + throw. (Covers unknown signature **and**
     cross-operator — never reveals another operator's bet.)
   - If more than one matches for this operator (should not happen — signature reuse is guarded at
     placement in `RegisterBetHandler.ValidateSolanaPayment`), `FirstOrDefaultAsync` deterministically
     takes one; acceptable. (Optional: assert single with a defensive count and a generic message.)
3. Guards:
   - `bet.Status is BetStatus.Rewarded or BetStatus.Refunded` → "This bet has already been
     rewarded or refunded and cannot be refunded." + throw.
   - `bet.Amount == 0` (free bet) → "Free bets have no payment to refund." + throw.
   - `bet.Status == BetStatus.NeedsRefunding` → **no-op success**: return `bet.Id` (idempotent).
4. Otherwise set `bet.Status = BetStatus.NeedsRefunding`, `SaveChanges`, return `bet.Id`. **No event
   published** (money moves via the sweep).

**Which host:** the endpoint lives in `ChimpOut.Api` and only reads + flips a status column — no money
movement, no Solana/wallet call, no decrypt — so the stateless-API rule holds.

### 6.4 Job registration (`ChimpOut.Consumers/Program.cs`)

1. Register the job scoped, next to the existing chat-purge registration (`Program.cs:53`):
   ```csharp
   builder.Services.AddScoped<PendingRefundSweepJob>();
   ```
2. Schedule it in `ScheduleRecurringJobs` (`Program.cs:180-187`), next to `game-chat-purge`:
   ```csharp
   // Every 12 hours: refund bets stuck in NeedsRefunding (no built-in twelve-hourly Cron helper).
   recurringJobs.AddOrUpdate<PendingRefundSweepJob>(
       "pending-refund-sweep", job => job.Run(), "0 */12 * * *");
   ```

Hangfire (Postgres storage) is already configured (`Program.cs:42-50`); no infra change.

---

## 7. Frontend behaviour

A new **"Refund a bet"** section in the operator admin portal, alongside the existing Game Types and
Registered Tokens tabs.

- **`services/adminService.ts`** — add a call on the existing `adminAPI` instance (which already
  attaches `X-Admin-Token` and routes 401 → `/error`):
  ```ts
  const refundBet = async (paymentSignature: string) => {
    const { data } = await adminAPI.post<DefaultResponse<string>>('/admin/bets/refund', {
      paymentSignature,
    });
    return data;
  };
  ```
  Export it from the `adminService` default object.
- **`components/admin/BetRefundManager.vue`** (new) — a panel with a single payment-signature text
  input and a **Refund** button. On submit call `adminService.refundBet(signature.trim())`;
  `toast.success('Bet marked for refund. It will be refunded on the next sweep.')` on success, and on
  error surface the backend validation message via the existing
  `firstValidationMessage(err?.response?.data?.messages)` helper pattern (see
  `OperatorTokenManager.vue:49-51,68-77`) with a fallback toast. Reuse the arcade-cabinet styling /
  `.admin-input` class already used by `OperatorTokenManager.vue`.
- **`components/admin/GameTypeManager.vue`** — extend the `Section` tab set from `'types' | 'tokens'`
  to include `'refunds'`, add the third nav button ("Refund a bet"), and render `<BetRefundManager />`
  when `section === 'refunds'` (mirror how `<OperatorTokenManager />` is mounted at `:190`).
- **No new types needed** beyond the inline string body; `DefaultResponse<string>` already exists in
  `src/types`. (Add a small `BetRefundBody`/response type only if you prefer typed symmetry — optional.)
- **Env vars:** none new; uses `VUE_APP_API_BASE_URL` via `adminAPI` already.

User-facing copy (admin only, specific is fine):
- Success: "Bet marked for refund. It will be refunded on the next sweep."
- Not found: backend "No bet found for that signature."
- Already settled: backend "This bet has already been rewarded or refunded and cannot be refunded."
- Free bet: backend "Free bets have no payment to refund."

---

## 8. Flow diagram

```mermaid
sequenceDiagram
    participant Admin as Operator admin (SPA)
    participant Api as ChimpOut.Api
    participant DB as Postgres
    participant Job as PendingRefundSweepJob (12h)
    participant Bus as MassTransit
    participant Con as RefundInvalidBetConsumer
    participant Pay as IPaymentMethodService

    Admin->>Api: POST /admin/bets/refund { paymentSignature }
    Api->>DB: find bet by ExternalTransactionReference (operator-scoped)
    alt not found / Rewarded / Refunded / free
        Api-->>Admin: 400 validation message (no change)
    else eligible
        Api->>DB: Bet.Status = NeedsRefunding
        Api-->>Admin: 200 "marked for refund"
    end

    Note over Job: every 12h
    Job->>DB: read all NeedsRefunding bets
    loop each bet
        Job->>Bus: publish RefundInvalidBetEvent
        Bus->>Con: consume
        Con->>Pay: Refund(bet, amount, reason)
        alt success
            Con->>DB: Bet.Status = Refunded (+ ledger Refund row)
        else failure
            Con->>Con: log error; status stays NeedsRefunding (retried next sweep)
        end
    end
```

---

## 9. Out of scope

- Changing the three existing inline-refund triggers' behaviour (other than the shared
  `Refunded`-on-success flip in §6.2).
- Auto-refunding reward-processing failures (`ErrorWhenProcessingRewards` / `NeedsManualAnalysis`) —
  nothing new marks those `NeedsRefunding`.
- Firing the admin refund immediately (kept sweep-only, up to ~12h).
- Retry caps / manual-analysis escalation for repeatedly-failing refunds.
- Any schema/migration change or making the cron configurable.

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/ChimpOut.Infrastructure/Jobs/PendingRefundSweepJob.cs` | **New.** Reads `NeedsRefunding` bets, publishes `RefundInvalidBetEvent` each. |
| 2 | `src/ChimpOut.Infrastructure/Consumers/RefundInvalidBet/RefundInvalidBetConsumer.cs` | Load bet tracked; on `IsSuccess` set `Status = Refunded` + `SaveChanges`; failure branch unchanged. |
| 3 | `src/ChimpOut.Infrastructure/Contracts/Admin/RefundBetBySignatureBody.cs` | **New.** `{ string PaymentSignature }`. |
| 4 | `src/ChimpOut.Infrastructure/Handlers/Admin/RefundBet/RefundBetRequest.cs` | **New.** `IRequest<Guid?>` carrying `OperatorId` + `PaymentSignature`. |
| 5 | `src/ChimpOut.Infrastructure/Handlers/Admin/RefundBet/RefundBetHandler.cs` | **New.** Find by `ExternalTransactionReference` (operator-scoped), guards, set `NeedsRefunding`. |
| 6 | `src/ChimpOut.Api/Controllers/AdminController.cs` | Add `POST bets/refund` + add `bets/refund` to the `[HttpOptions]` preflight list. |
| 7 | `src/ChimpOut.Consumers/Program.cs` | `AddScoped<PendingRefundSweepJob>()` + `AddOrUpdate<PendingRefundSweepJob>("pending-refund-sweep", j => j.Run(), "0 */12 * * *")`. |
| 8 | `src/ChimpOut.Common/Resources/ErrorMessages.resx` (+ `.Designer.cs`) | **Optional** — add the three admin messages here if the codebase prefers `ErrorMessages` constants over inline strings; otherwise inline in the handler (match surrounding style — `DeactivateGameTypeHandler` uses inline strings). |

### 10.2 Frontend (`monkify-front`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/services/adminService.ts` | Add `refundBet(paymentSignature)`; export from default object. |
| 2 | `src/components/admin/BetRefundManager.vue` | **New.** Signature input + Refund button + toast feedback. |
| 3 | `src/components/admin/GameTypeManager.vue` | Add a `'refunds'` tab/nav button; mount `<BetRefundManager />`. |

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/refund-safeguards.md` | This spec (new). |
| 2 | `INDEX.md` | Add spec row (status Ready) + update "Feature currently in flight". |
| 3 | `features/refund-safeguards.md` | **Created at ship** by `/monkify-ship-feature` (Feature-level artifact). |

---

## 11. Unit tests

Project: `monkify-backend/src/ChimpOut.Tests` (run via `scripts/run_tests.bat`).

| Test file | Test cases | Expected |
| --- | --- | --- |
| `RefundInvalidBetConsumerTests` (existing) | Successful refund now flips status | Bet ends `Refunded`; ledger `Refund` row written. |
| `RefundInvalidBetConsumerTests` (existing) | Failed refund | Bet status **unchanged** (stays whatever it was — `NeedsRefunding` for swept, `Made` for inline); error logged. |
| `RefundInvalidBetConsumerTests` (existing) | Already-refunded / free-bet early returns | No status flip, no exception (idempotent / skip). |
| `BetPlacedHandlerTests`, `GameTypeDeactivatedConsumerTests`, `RegisterBetHandlerTests` (existing) | The three inline-trigger paths | Assertions updated so a refunded bet ends `Refunded` (was `Made`). |
| `RefundBetHandlerTests` (**new**) | Eligible bet marked | `Status == NeedsRefunding`, returns the bet id. |
| `RefundBetHandlerTests` (**new**) | Unknown signature | Validation message "No bet found…"; no change. |
| `RefundBetHandlerTests` (**new**) | Cross-operator signature | Treated as not found (message + no change). |
| `RefundBetHandlerTests` (**new**) | Bet is `Rewarded` / `Refunded` | Rejected; status unchanged; no double-pay. |
| `RefundBetHandlerTests` (**new**) | Free bet (`Amount == 0`) | Rejected with the free-bet message. |
| `RefundBetHandlerTests` (**new**) | Already `NeedsRefunding` | Idempotent success (still `NeedsRefunding`, returns id). |
| `PendingRefundSweepJobTests` (**new**) | Mixed bets | Publishes exactly one `RefundInvalidBetEvent` per `NeedsRefunding` bet (mock `IMessageBus`), none for others; event carries wallet/amount. |

Use the existing `Shared/BetTestFactory` for bet fixtures and `Moq` for `IMessageBus` /
`IPaymentMethodResolver`, matching the current test style (xUnit + Shouldly + Moq + in-memory EF).

---

## 12. Acceptance criteria

Mapped from the Trello **Acceptance Criteria** checklist:

1. Given a paid bet in `NeedsRefunding`, when the 12h sweep runs, then a `RefundInvalidBetEvent` is
   published for it and the bet is refunded (a `TransactionLog` `Refund` row appears).
2. Given a `NeedsRefunding` bet, when its refund succeeds, then `Bet.Status` becomes `Refunded` so the
   next sweep does not re-pick it.
3. Given a `NeedsRefunding` bet whose refund attempt fails, when the sweep processes it, then the bet
   stays `NeedsRefunding` and is retried on the next 12h run.
4. Given any refund routed through `RefundInvalidBetConsumer` (including the three inline triggers),
   when `Refund()` succeeds, then the bet `Status` is set to `Refunded`.
5. Given a valid admin token and a payment signature matching a refundable bet for that operator, when
   the admin calls `POST /admin/bets/refund`, then the bet `Status` is set to `NeedsRefunding` and a
   success message is returned.
6. Given a payment signature whose bet is already `Rewarded` or `Refunded`, when the admin calls the
   refund endpoint, then it is rejected (bet not marked) with a clear message and no double-payout.
7. Given a payment signature for a free bet (`Amount 0`), when the admin calls the refund endpoint,
   then it is rejected with a clear message and the bet is not marked `NeedsRefunding`.
8. Given a payment signature that does not exist, or matches a bet belonging to a different operator,
   when the admin calls the refund endpoint, then the response is a vague "no bet found for that
   signature" and nothing is marked.
9. Given a bet already in `NeedsRefunding`, when the admin calls the refund endpoint again for it, then
   it is a success no-op (idempotent, still `NeedsRefunding`).
10. Given the sweep job is registered, when the Consumers host starts, then a Hangfire recurring job
    runs every 12 hours (cron `0 */12 * * *`) and is visible in the `/hangfire` dashboard.
11. Given the operator admin page, when an admin opens it, then a "Refund a bet" panel with a
    payment-signature input and a Refund button is available and shows success/error feedback.
12. Given the Testcontainers suite, when the complex-flow integration test runs (place paid bet → admin
    marks by signature → `NeedsRefunding` → sweep → refunded), then the bet ends `Refunded` with a
    `Refund` ledger row.

### Integration tests (`ChimpOut.Tests.Integration`)

Model on `OperatorWalletRefundTests` (Standard/regulated path — `FakeOperatorWalletResolver` makes the
refund succeed; simplest). The harness boots **both** hosts and exposes `CreateConsumersScope()`,
`CreateAdminClient(MintAdminToken(op))`, `MintToken`, `WaitForWaitingBetsGameAsync`, `PlaceBetAsync`,
`GetBetsAsync`, `GetTransactionLogsAsync`, `WaitUntilAsync`.

- **Add a harness helper** `SetBetStatusAsync(Guid betId, BetStatus status)` to `IntegrationTestFixture`
  (mirror `SetCarriedExtraAsync` — `AsTracking().First` + save) so a test can seed a `NeedsRefunding`
  bet directly.
- **Happy-path sweep test** (new `RefundSafeguardsTests`): create a Standard type, wait for a waiting
  game, place a paid bet, read its bet id, `SetBetStatusAsync(betId, NeedsRefunding)`, resolve
  `PendingRefundSweepJob` from `CreateConsumersScope()` and `await job.Run()` (drives the real event →
  consumer without waiting for cron), then `WaitUntilAsync` for a `TransactionLogType.Refund` row **and**
  assert the bet becomes `Refunded` (re-read via `GetBetsAsync`). Satisfies AC 1–3, 12.
- **Complex-flow test** (admin-mark → sweep → refund): same setup, but instead of `SetBetStatusAsync`,
  call the admin endpoint — `CreateAdminClient(MintAdminToken(RegulatedOperatorId)).PostAsJsonAsync(
  "/admin/bets/refund", new { paymentSignature = bet.ExternalTransactionReference })`, assert 200 and
  the bet is `NeedsRefunding`, then run the sweep job and assert `Refunded` + `Refund` ledger row.
  Satisfies AC 5, 12.
- **Guard test(s):** admin refund of an already-`Rewarded` bet (drive a full paid game to
  `RewardForWinnersCompleted` via the existing flow, then POST the signature) returns 400 with the
  "already rewarded or refunded" message and does not mark it; a bogus signature returns "no bet
  found". (AC 6, 8.)

---

## 13. Manual QA (local Docker stack)

1. Bring up the local stack; open `/chimpout/admin` with a dev admin token (header dev button →
   `POST /dev/admin-token`).
2. Place a paid bet on a Standard game type; grab its payment signature (bet
   `ExternalTransactionReference`, e.g. via the DB console or `/games/{id}/Bets`).
3. In the new **Refund a bet** panel, paste the signature → Refund → expect "marked for refund".
   Confirm the bet row is `NeedsRefunding` in the DB.
4. Trigger the sweep without waiting 12h: run `pending-refund-sweep` from the `/hangfire` dashboard
   ("Recurring jobs" → Trigger now). Confirm a `TransactionLog` `Refund` row appears and the bet flips
   to `Refunded`.
5. Re-run the panel with: an unknown signature (→ "no bet found"), the now-`Refunded` signature (→
   "already rewarded or refunded"), and a signature from a different operator's bet (→ "no bet found").

---

## 14. Trello / pipeline

- **Source card:** [Refund safeguards](https://trello.com/c/MpfBtpE4/71-refund-safeguards) — list
  **Specs created**; the **Implementation** section on the card lists worktree + branch + spec + Level.
- **Next:** `/monkify-implement-feature Refund safeguards` to build it (with the mandatory happy-path +
  complex-flow integration tests), then `/monkify-ship-feature Refund safeguards` to sync the KB
  (creating `features/refund-safeguards.md`) and merge `feature/refund-safeguards` → `main`.

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-07-20 | Initial spec from refined card #71 (refinement done in the same session; decisions §3 authoritative). |

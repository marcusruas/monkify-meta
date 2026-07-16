# Feature — Operator wallet API (Model A / regulated debit-credit-refund)

> Status: **shipped.** Authoritative spec: `../specs/operator-wallet-api.md`
> (merged via `feature/operator-wallet-api` on 2026-06-30).
>
> ⚠️ **Reframed by `features/payment-methods.md` (2026-07-08).** This off-chain wallet path is intact,
> but it is no longer selected by `Operator.PaymentModel == Regulated` (that enum is gone). It is now the
> **`Standard` payment method** on a game type, reached through the unified `IPaymentMethodService`
> resolver (`StandardPaymentMethodService` → `IOperatorWalletResolver`). Read every "regulated operator"
> below as "**Standard** game type". The debit/credit/refund mechanics, USD amounts, unified
> `TransactionLog`, and no-commission rules are unchanged — but `TransactionLog` now also carries a
> `PaymentMethod` and its `Signature` column was dropped in favour of `ExternalTransactionReference`.

## What it does

For **regulated operators** (`Operator.PaymentModel == Regulated`, "Model A"), ChimpOut holds **no
player float**: it instructs the operator's own wallet API to **debit** the stake when a bet is
placed, **credit** winnings when a game is rewarded, and **refund** cancelled/invalid bets.
**Crypto operators** (`PaymentModel == Crypto`, "Model B") are unchanged — they still pay on-chain via
Solana. This feature also carries two app-wide changes that touch both payment models: the money
ledger is unified onto `TransactionLog`, and the ChimpOut commission is removed (winners receive the
full pot).

## Core rules

- **Per-operator adapters.** Regulated payouts go through `IOperatorWalletService` (operations
  **Debit / Credit / Refund**). Each operator names a **provider** in config; `IOperatorWalletResolver`
  maps the provider name to a registered adapter. The repo ships **one** adapter — `MockOperatorWalletService`
  (provider `"mock"`) for dev + tests; real casino adapters are added per onboarding.
- **Config & secrets.** Per-operator wallet config (`Provider`, `BaseUrl`, `ApiKey`, `TimeoutSeconds`)
  lives under `GeneralSettings.OperatorWallets.Operators[<operatorId>]` in `chimpoutsettings.json`,
  **mock values only**. A regulated operator with **no/unknown provider** configured raises a
  configuration error — the wallet call is never silently skipped.
- **Amounts are USD.** Debit/credit/refund send the canonical **USD decimal** amount with
  `currency: "USD"`. FX / display-currency conversion is out of scope.
- **Debit — at bet time, last step, fail-closed.** In `RegisterBetHandler` (the API host), all local
  validations run **first** (choice length, duplicates, game accepting bets, operator active); the
  debit is the **last** step before persisting. So an invalid bet is rejected **without** a debit (no
  registration-time refund). A debit failure surfaces a **specific** player-facing message — insufficient
  funds / player blocked / duplicate / "please try again" (timeout) — via `ApiResult.messages`. **These
  player wallet errors are specific** (the vague-operator-status rule does not apply). On **timeout** the
  bet is rejected with no synchronous retry; the bet-id idempotency key prevents a double-debit on a
  later retry.
- **Credit — at reward time.** `RewardGameConsumer` pays regulated winners via the adapter's `Credit`
  (crypto winners still use `ISolanaService.TransferTokensForBet`). The Solana blockhash pre-check runs
  only for crypto payouts. Free-bet winners get no wallet call (unchanged).
- **Refund — post-acceptance only.** `RefundInvalidBetConsumer` refunds regulated bets via the adapter's
  `Refund` (with a reason); crypto bets still refund on-chain. Regulated refunds cover lottery
  under-subscription, abrupt game end, and other post-acceptance cancellations. Free bets (amount 0)
  are skipped.
- **Failure handling reuses the existing pattern.** A failed credit leaves the bet un-`Rewarded` so the
  game resolves to `ErrorWhenProcessingRewards` (and the bet may become `NeedsManualAnalysis`); a
  failed refund is logged for manual handling. Idempotency keys make a later retry safe.
- **Idempotency & audit.** Keys are the **bet id** (+ operation type) — regulated bets carry no
  `PaymentSignature`. Each successful operation writes a `TransactionLog` row carrying the operator's
  returned **`ExternalTransactionReference`**.
- **Unified ledger.** `TransactionLog` now records **all** money movements via a `TransactionLogType`
  discriminator (`Credit | Debit | Refund`) plus nullable `Reason` and `ExternalTransactionReference`;
  `Signature` is nullable (crypto-only). **`RefundLog` is removed** — refund audit rows live in
  `TransactionLog` (type `Refund`). `BetDomainService` idempotency math reads the ledger **by type**:
  reward subtraction uses `Credit` rows, the already-refunded guard uses `Refund` rows; `Debit` rows are
  never subtracted.
- **No commission.** `BetDomainService.SetPotAmount` no longer applies a commission: the pot is the full
  sum of stakes (plus `LotteryParameters.LotteryPot` for lottery) and the whole pot is paid to winners.
  This applies to **both** payment models. (A ChimpOut commission-per-bet feature is tracked separately for
  later — see Trello card #26.)
- **Free bets & crypto unchanged.** Amount-0 free bets make no wallet calls under any model; crypto
  operators are unchanged apart from the unified ledger and commission removal.

## Key code touchpoints

**Backend (`monkify-backend`)**

- Adapter: `ChimpOut.Infrastructure/Services/OperatorWallets/` — `IOperatorWalletService`,
  `OperatorWalletResult`, `OperatorWalletError` (`InsufficientFunds | PlayerBlocked | Duplicate | Timeout
  | Unavailable`), `IOperatorWalletResolver` + `OperatorWalletResolver`, `MockOperatorWalletService`.
  Registered in `ChimpOut.Infrastructure/DependencyInjection.cs`.
- Config: `ChimpOut.Domain/Configs/Entities/OperatorWalletSettings.cs`; `GeneralSettings.OperatorWallets`;
  `ChimpOut.Common/chimpoutsettings.json` → `GeneralSettings.OperatorWallets` (mock).
- Debit: `ChimpOut.Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs`
  (`DebitOperatorWallet`, `MapWalletErrorToMessage`); messages in `ChimpOut.Common/Resources/ErrorMessages`
  (`WalletInsufficientFunds`, `WalletPlayerBlocked`, `WalletDuplicateBet`, `WalletTemporarilyUnavailable`).
- Credit: `ChimpOut.Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs`
  (`CreditThroughOperatorWallet`).
- Refund: `ChimpOut.Infrastructure/Consumers/RefundInvalidBet/RefundInvalidBetConsumer.cs`
  (`RefundThroughOperatorWallet`); `RefundInvalidBetEvent` now carries `BetId` (publishers:
  `RegisterBetHandler`, `BetPlacedConsumer`). Crypto refund: `SolanaService.RefundTokens` writes a
  `Refund`-typed `TransactionLog` (the `RefundLog` table is gone).
- Ledger: `ChimpOut.Domain/Bets/Entities/TransactionLog.cs`,
  `ChimpOut.Domain/Bets/ValueObjects/TransactionLogType.cs`; inline mapping in
  `ChimpOutDbContext.OnModelCreating`; migration
  `ChimpOut.Infrastructure/Migrations/20260630183419_AddOperatorWalletLedger.cs` (adds columns, backfills
  existing rows as `Credit`, migrates `RefundLog` → `TransactionLog`, drops `Refunds`).
- Money math: `ChimpOut.Domain/Bets/Services/BetDomainService.cs` (no commission; type-aware credit/refund
  sums). Config removed: `TokenSettings.CommisionPercentage`.
- Host split: debit in `ChimpOut.Api` (synchronous, at bet time); credit/refund in `ChimpOut.Consumers`.

**Frontend (`monkify-front`)**

- **None.** Regulated bets already post `{ amount, choice, seed }` with no wallet/signature; debit-failure
  reasons render through the existing `ApiResult.messages` → toast path.

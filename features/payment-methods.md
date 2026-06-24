# Feature — Multiple Payment Methods

> Status: **stub / distilled.** Authoritative spec: `../specs/payment-methods-feature.md`
> (branch `feature/payment-methods`). Short version below; trust the spec + live code for details.

## What it does

Lets a game session accept **multiple payment forms** (crypto, PayPal, Venmo, online banking,
cards, and a free coin) instead of only a single Solana SPL token, and re-models payments/audit so
every money movement (inbound stake, reward credit, refund) is fully auditable in **USD**.

## Data model (live)

- `PaymentMethod` enum: `SOL=1, USDT=2, PayPal=3, Venmo=4, Fiat=5, MastercardVisa=6, FreeCoin=7`
  (with verbatim `[Description]` labels — note USDT's label is "CryptoCurrency (USDC)").
- `SessionParameters *—* PaymentMethod` via `SessionParameterPaymentMethod` join entity.
- `Bet.PaymentSignature` removed; inbound proof now lives on `BetPayment`.
- `BetPayment` (1 inbound stake + outbound reward/refund payments): `Amount` (USD), `PaymentMethod`,
  `Direction` (Inbound/Outbound), `ExternalReference`, and method-specific JSONB `Details`
  (`BetPaymentDetails`).
- `TransactionLog` is the **audit ledger**: `BetPaymentId` (NOT NULL), `Amount`, `TransactionType`
  (`Inbound`/`Credit`/`Refund`), `RefundReason`. The old `RefundLog`/`Refunds` table is removed.
- **Reward/refund math counts only `TransactionType.Credit`** (`BetDomainService.CalculateCreditsForBet`).

## Processor strategy (live)

- `IPaymentMethodProcessor` (`ValidateInboundPayment`, `Credit`, `Refund`), resolved via **keyed DI**
  + `IPaymentMethodProcessorFactory`.
- Implementations: Solana, Usdt, FreeCoin, PayPal (PayPal Server SDK), Venmo (Braintree),
  OnlineBanking (Stripe ACH), Card (Stripe). SDKs wrapped behind `Services/Payments/Providers`.
- Payouts come from prefunded per-provider "house" floats; refunds reverse the original charge;
  card/bank winners with no payout destination → `NeedsManualAnalysis`.

## Flow (live)

- Inbound: `RegisterBetHandler` builds a `BetPayment(Inbound)`, runs an external-reference-reuse
  check, validates via the processor, persists `Bet + BetPayment + TransactionLog(Inbound)`, and
  validates the chosen method is allowed for the parameter.
- Outbound: `RewardSessionConsumer` → `processor.Credit`; refund consumers → `processor.Refund`.

## Frontend (live)

- `src/types/index.ts`: `PaymentMethod` enum, `PaymentMethodOption`, `ActiveSession.paymentMethod[]`,
  `BodySessionBet` (`paymentMethod`, `externalReference`, `paymentDetails`).
- `useSession.bet()` branches by method (SOL/USDT on-chain; FreeCoin no charge; PayPal/Venmo/Stripe
  stubbed pending live keys). `useCurrency` for multi-currency display (settled in USD).

<!-- TODO: extract the full file-by-file list, decisions (§9), and test impacts from the spec. -->

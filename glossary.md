# Monkify — Glossary

> Status: **stub** — to be fully drafted. Quick definitions of the core domain vocabulary.
> Source of truth: `monkify-backend/src/Monkify.Domain`. See `architecture.md` for how they fit.

- **Session** — one round of the game for a given game type. Has a status lifecycle
  (`WaitingBets → SessionStarting → InProgress → Ended → RewardForWinners…`). Entity: `Session`.
- **SessionParameters** — a "game type" / configuration (name, required amount, allowed characters,
  min players, choice length, lottery params, payment methods, operator). Entity: `SessionParameters`.
- **Bet** — a player's wager on a `Session`: a `Choice` (the sequence they bet on), an `Amount`
  (USD), an optional crypto `Wallet`, and seed. Entity: `Bet`.
- **Typer / MonkifyTyper** — the domain service that "types" random characters until a bet's choice
  appears; determines winners. `Monkify.Domain/Sessions/Services/MonkifyTyper.cs`.
- **Operator** — a casino/betting platform tenant. `SessionParameters.OperatorId` (nullable = house).
  Scoped via the `operator-id` header. Entity: `Operator`.
- **PaymentMethod** — enum of supported methods (SOL, USDT, PayPal, Venmo, Fiat, MastercardVisa,
  FreeCoin). Each has an `IPaymentMethodProcessor`.
- **BetPayment** — a single money movement for a bet (Inbound stake / Outbound reward|refund) with
  method-specific JSONB `Details`. Entity: `BetPayment`.
- **TransactionLog** — the audit ledger row per movement (`Inbound` / `Credit` / `Refund`).
- **PresetChoice** — predefined winnable choices for a game type.
- **LotteryParameters** — config for lottery-style sessions (frequency, pot); scheduled via Hangfire.

<!-- TODO: expand each term with fields, invariants, and links to code. -->

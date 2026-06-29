# Monkify — Glossary

> Domain vocabulary for agents. Source of truth: `monkify-backend/src/Monkify.Domain`.
> See `architecture.md` for how terms fit together; `features/operators.md` for operator rules.

- **Session** — one round of the game for a given game type. Has a status lifecycle
  (`WaitingBets → SessionStarting → InProgress → Ended → RewardForWinners…`). Entity: `Session`.
- **SessionParameters** — a "game type" / configuration (name, required amount, allowed characters,
  min players, choice length, lottery params, **required** `OperatorId`). Entity: `SessionParameters`.
- **Bet** — a player's wager on a `Session`: a `Choice` (the sequence they bet on), an `Amount`
  (USD), optional `OperatorPlayerId` FK (NULL for anonymous free bets), `PaymentSignature`
  (on-chain tx proof for paid bets), and seed. Entity: `Bet`. On-chain payout/refund address is
  resolved from linked `OperatorPlayer.ExternalPlayerId`, not stored on the bet row.
- **OperatorPlayer** — operator-scoped player identity. Fields include `ExternalPlayerId` (Solana
  wallet pubkey or casino player id), `IsCrypto`, optional `DisplayName`, `Currency` (defaults to
  USD), `LastSeenAt`. Unique on `(OperatorId, ExternalPlayerId)`. No PII. Entity: `OperatorPlayer`.
  See `features/operator-player-identity.md`.
- **Free bet** — dev/QA-only bet with `Amount = 0` and empty `PaymentSignature`, allowed only when
  backend `AllowFreeBets`, session `AllowFreeBet`, and (for UI) the frontend flag are all enabled.
  No Solana payment, payout, or refund. Detected via `BetExtensions.IsFreeBet`. See
  `features/free-bet-mode.md`.
- **AllowFreeBet** — bool on `SessionParameters` (DB default `false`). Per session type; exposed as
  `allowFreeBet` on active-types API responses.
- **AllowFreeBets** — bool on `GeneralSettings.Sessions` in `monkifysettings.json`. Global backend
  gate; must be `true` for the API to accept free bets.
- **Typer / MonkifyTyper** — the domain service that "types" random characters until a bet's choice
  appears; determines winners. `Monkify.Domain/Sessions/Services/MonkifyTyper.cs`.
- **Operator** — a casino/betting platform tenant (e.g. BetMGM). Every `SessionParameters` row
  belongs to exactly one operator (`OperatorId` required — no house mode). Scoped on REST via the
  **`x-operator-id`** request header; players enter at `/monkify/game/<operatorId>`. Contract
  fields: `ContractStartDate`, optional `ContractEndDate` (null = never expires). Entity: `Operator`.
- **ContractIsActive** — domain helper on `Operator`: true when `ContractStartDate <= UtcNow` and
  contract has not expired. Inactive operators return empty data or redirect to `/error` (vague).
- **TransactionLog** — reward credit rows per bet (used for idempotency in reward/refund math).
- **RefundLog** — audit row for refunded invalid bets.
- **PresetChoice** — predefined winnable choices for a game type.
- **LotteryParameters** — config for lottery-style sessions (frequency, pot); scheduled via Hangfire.

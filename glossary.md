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
  wallet pubkey or casino player id), optional `DisplayName`, `Currency` (defaults to USD),
  `LastSeenAt`. Unique on `(OperatorId, ExternalPlayerId)`. No PII. (`IsCrypto` was removed — crypto-ness
  is now `Operator.PaymentModel`.) Entity: `OperatorPlayer`. See `features/operator-player-identity.md`.
- **Free bet** — dev/QA-only bet with `Amount = 0` and empty `PaymentSignature`, allowed only when
  backend `AllowFreeBets`, session `AllowFreeBet`, and (for UI) the frontend flag are all enabled.
  No Solana payment, payout, or refund. Detected via `BetExtensions.IsFreeBet`. See
  `features/free-bet-mode.md`.
- **AllowFreeBet** — bool on `SessionParameters` (DB default `false`). Per session type; exposed as
  `allowFreeBet` on active-types API responses.
- **AllowFreeBets** — bool on `GeneralSettings.Sessions` in `monkifysettings.json`. Global backend
  gate; must be `true` for the API to accept free bets.
- **Typer / MonkifyTyper** — the domain service that "types" random characters until a bet's choice
  appears; determines winners. Uses Stopwatch-scheduled wall-clock pacing and optional seed-based
  fallback at the character cap. `Monkify.Domain/Sessions/Services/MonkifyTyper.cs`. See
  `features/typer-pacing-scalability.md`.
- **DisplaySpeed** — SignalR event `{ typingSpeed }` (1–3) ramping during a session run. Driven by
  elapsed-time fractions of the typer's target mean duration; the frontend maps tiers to refresh
  cadence in `GameTyper.vue`.
- **Fallback winner** — when the typer reaches `MaxTyperCharacters` without a natural sliding-window
  match, the winner is drawn with `new Random(SessionSeed)` over ordinally-sorted choices. Same seed
  + bets → same winner on any host (`WinnerDecidedByFallback`).
- **MaxTyperCharacters** — config cap (default 5,000,000) on characters typed before fallback
  winner selection; also bounds the pacing horizon for huge search spaces.
- **Operator** — a casino/betting platform tenant (e.g. BetMGM). Every `SessionParameters` row
  belongs to exactly one operator (`OperatorId` required — no house mode). Scoped on REST via the
  signed **launch token** (`X-Launch-Token`; the operator id is the token `iss`); players enter at
  `/monkify/game/sessions` (the operator is read from the token, not the URL). Fields:
  `ContractStartDate`, optional `ContractEndDate` (null = never
  expires), **`PaymentModel`**, **`LaunchPublicKey`** (+ optional `LaunchKeyId`). Entity: `Operator`.
- **Launch token** — a casino-signed **RS256 JWT** that proves operator + player identity; required
  (`X-Launch-Token` header) on every operator-scoped REST call and validated by middleware in
  `Monkify.Api` against the operator's `LaunchPublicKey`. Claims: `iss`=operatorId, `sub`=player id,
  `exp`/`iat`/`jti`. Failures return a vague 401. See `features/launch-token-validation.md`.
- **PaymentModel** — enum on `Operator` (`Regulated` | `Crypto`). Crypto operators connect a wallet and
  pay on-chain; regulated operators settle off-chain and identify the player by the token `sub`.
  Replaces the removed `OperatorPlayer.IsCrypto`. Existing operators backfilled to `Crypto`.
- **ContractIsActive** — domain helper on `Operator`: true when `ContractStartDate <= UtcNow` and
  contract has not expired. Inactive operators return empty data or redirect to `/error` (vague). The
  active-operator lookup is cached via `IOperatorService.GetActiveOperatorById`.
- **TransactionLog** — the **unified money-movement ledger** per bet. Each row has a `TransactionLogType`
  (`Credit | Debit | Refund`), `Amount`, nullable `Signature` (Solana, crypto only), nullable
  `ExternalTransactionReference` (operator wallet ref, regulated only), and nullable `Reason`. Used for
  reward/refund idempotency: reward subtraction reads `Credit` rows, the already-refunded guard reads
  `Refund` rows. (The separate `RefundLog` table was removed and folded in here.) See
  `features/operator-wallet-api.md`.
- **TransactionLogType** — enum on `TransactionLog` (`Credit = 0`, `Debit = 1`, `Refund = 2`); `Credit` is
  the default so legacy reward rows keep their meaning.
- **Operator wallet API** — for regulated (Model A) operators, the operator's own wallet that Monkify
  instructs to **debit** stakes, **credit** winnings, and **refund** cancelled bets (Monkify holds no
  float). Integrated via a per-operator adapter (`IOperatorWalletService`) selected by a named provider in
  config (`IOperatorWalletResolver`). Crypto (Model B) operators are unaffected (Solana). See
  `features/operator-wallet-api.md`.
- **Pre-bet check / RG hook** — an **opt-in**, per-operator responsible-gaming check Monkify calls in
  `RegisterBetHandler` after local validation and **before** the wallet debit, so the operator can
  **allow or deny** a bet (self-exclusion, limits, cooling-off, KYC, blocked). Per-operator adapter
  (`IPreBetCheckService`) selected by a named provider in `GeneralSettings.OperatorRgChecks`
  (`IPreBetCheckResolver`, which returns **null → skip** when unconfigured — the default). Applies to
  **both** payment models. A denial returns a **specific** player message (`PreBetCheckDenyReason`:
  `SelfExcluded | LimitReached | CoolingOff | KycRequired | Blocked | Other`); a timeout/error is
  **fail-closed** (rejected, no debit). Skipped for anonymous free bets. Log-only, no DB row. See
  `features/rg-pre-bet-hook.md`.
- **PresetChoice** — predefined winnable choices for a game type.
- **LotteryParameters** — config for lottery-style sessions (frequency, pot); scheduled via Hangfire.
- **Display name** — optional `OperatorPlayer.DisplayName` (1–20 chars, trimmed). Set via the
  customization modal (`PATCH /players/preferences`); rendered on bet DTOs and recent-bets SignalR
  events for self and others; falls back to truncated `externalPlayerId`. See
  `features/player-customization-modal.md`.
- **Preferred display currency** — `OperatorPlayer.Currency` (default `USD`). Regulated operators may
  choose from the backend definitive fiat set (33 codes); crypto operators are restricted to `USD`
  (selector hidden in UI). Affects **rendering only** — backend always stores and transmits USD;
  client multiplies by cached USD→currency rates from `GET /fx/rates`. See
  `features/player-customization-modal.md`.
- **FX rates / display conversion** — token-gated `GET /fx/rates` returns the definitive currency
  list with USD-based rates (Redis cache, 6h TTL; `IExchangeRateProvider` + FreeCurrencyAPI or mock).
  Frontend `useDisplayCurrency.formatUsd` applies conversion for regulated operators only; settlement
  and operator-wallet amounts stay USD.

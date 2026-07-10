# Monkify — Glossary

> Domain vocabulary for agents. Source of truth: `monkify-backend/src/Monkify.Domain`.
> See `architecture.md` for how terms fit together; `features/operators.md` for operator rules.

- **Session** — one round of the game for a given game type. Has a status lifecycle
  (`WaitingBets → SessionStarting → InProgress → Ended → RewardForWinners…`). Entity: `Session`.
- **SessionParameters** — a "game type" / configuration (name, required amount, allowed characters,
  min players, choice length, lottery params, **required** `OperatorId`, a **`PaymentMethod`**, and a
  nullable **`OperatorTokenId`** set only for `SolanaToken`). Entity: `SessionParameters`.
- **Bet** — a player's wager on a `Session`: a `Choice` (the sequence they bet on), an `Amount`
  (Standard = USD; SolanaToken = token-native), optional `OperatorPlayerId` FK (NULL for anonymous free
  bets), a **`PaymentMethod`** (copied from the session type), a nullable **`ExternalTransactionReference`**
  (SolanaToken → the on-chain signature; Standard → the wallet debit ref), a provably-fair **`Seed`**
  (a 64-char `HMAC-SHA256(clientSeed, Nonce)` hex — the raw client seed never reaches the server), and a
  per-bet **`Nonce`**. Entity: `Bet`. The old `PaymentSignature` column is **removed**. On-chain payout/refund address is resolved from linked
  `OperatorPlayer.ExternalPlayerId`, not stored on the bet row.
- **OperatorPlayer** — operator-scoped player identity. Fields include `ExternalPlayerId` (Solana
  wallet pubkey or casino player id), optional `DisplayName`, `Currency` (defaults to USD),
  `LastSeenAt`. Unique on `(OperatorId, ExternalPlayerId)`. No PII. (`IsCrypto` was removed; crypto-ness
  is now the session type's `PaymentMethod`, not an operator property.) Entity: `OperatorPlayer`. See
  `features/operator-player-identity.md`.
- **Free bet** — dev/QA-only bet with `Amount = 0`, allowed only when backend `AllowFreeBets`, session
  `AllowFreeBet`, and (for UI) the frontend flag are all enabled. No debit/credit/refund under **either**
  payment method; a free `SolanaToken` session also skips wallet-connect. Detected via
  `BetExtensions.IsFreeBet` (now `Amount == 0`; the old empty-`PaymentSignature` check is gone). See
  `features/free-bet-mode.md`.
- **AllowFreeBet** — bool on `SessionParameters` (DB default `false`). Per session type; exposed as
  `allowFreeBet` on active-types API responses.
- **AllowFreeBets** — bool on `GeneralSettings.Sessions` in `monkifysettings.json`. Global backend
  gate; must be `true` for the API to accept free bets.
- **Typer / MonkifyTyper** — the domain service that "types" characters until a bet's choice appears;
  determines winners. Reads the session's committed **`ServerSeed`** and drives character selection from
  a **portable HMAC-SHA256 keystream** (rejection-sampled; no `System.Random`), with Stopwatch-scheduled
  wall-clock pacing and an HMAC-keystream fallback at the character cap.
  `Monkify.Domain/Sessions/Services/MonkifyTyper.cs`. See `features/provably-fair.md` and
  `features/typer-pacing-scalability.md`.
- **DisplaySpeed** — SignalR event `{ typingSpeed }` (1–3) ramping during a session run. Driven by
  elapsed-time fractions of the typer's target mean duration; the frontend maps tiers to refresh
  cadence in `GameTyper.vue`.
- **Fallback winner** — when the typer reaches `MaxTyperCharacters` without a natural sliding-window
  match, the winner is drawn from a dedicated `HMAC-SHA256(ServerSeed, prefix + "|fallback")` label over
  ordinally-sorted choices. Same `ServerSeed` + bets → same winner in any language
  (`WinnerDecidedByFallback`). Because the winning sequence was never
  naturally typed, `SessionService.RunSession` then emits it as a **final `<sessionId>/Terminal`
  batch** so the terminal ends with the word "typed" (display-only; see `features/characters-away.md`).
- **MaxTyperCharacters** — config cap (default 5,000,000) on characters typed before fallback
  winner selection; also bounds the pacing horizon for huge search spaces.
- **ServerSeed** — the per-session server entropy for provably fairness: 32 CSPRNG bytes stored as a
  64-char lowercase-hex string on `Session.ServerSeed`, generated + committed **atomically on the first
  bet** (first-writer-wins; never regenerated) and **revealed only after the round Ends** (via the
  verification endpoint). Keys the HMAC-SHA256 keystream that drives the typer. Replaces the removed
  `int? Session.Seed`. See `features/provably-fair.md`.
- **ServerSeedHash** — `SHA256(UTF8(ServerSeed))` (lowercase hex) on `Session.ServerSeedHash`, the
  **commitment** published as soon as the seed exists (during betting, on `ActiveSessionDto` and the
  verification endpoint). After the reveal, `SHA256(ServerSeed) == ServerSeedHash` proves the seed was
  not grinded.
- **Client seed** — the player's own provably-fair entropy: a persistent value in browser localStorage
  (`monkify.clientSeed`), viewable/settable/rotatable in the header gear modal. Combined with a per-bet
  **`Nonce`** to derive `Bet.Seed = HMAC-SHA256(clientSeed, String(nonce))`. The raw client seed
  **never** leaves the browser; only the derived `Bet.Seed` reaches the server. Rotating/setting resets
  the nonce to 0. Frontend `useClientSeed`.
- **Bet nonce** — an incrementing per-bet counter (`Bet.Nonce`, `long`) under a client seed; makes each
  derived `Bet.Seed` unique and lets a bettor prove `Bet.Seed == HMAC(clientSeed, nonce)` without
  exposing the client seed.
- **Provably-fair verification** — reproducing a round's outcome from published inputs. Exposed by the
  reveal-gated `GET /sessions/{id}/verification` (operator-scoped): pre-Ended returns `serverSeedHash` +
  ordered bet seeds/choices/nonces + `characterPool`/`queueLength`/`maxCharacters`; post-Ended adds
  `serverSeed` + `winningChoice` + `winnerDecidedByFallback` (the raw `clientSeed` is never returned).
  Anyone replays the HMAC keystream to reproduce the winner; a standalone offline `public/verify.html`
  does it without contacting Monkify. There is **no in-terminal recompute** — the game terminal only
  displays the commitment (during betting) and the revealed seed (after Ended). See
  `features/provably-fair.md`.
- **Characters Away / near-miss** — a player-facing, display-only reveal: while a round runs, a
  bottom-docked terminal row per **own** bet choice shows how close the monkey has come to typing it
  (`> N AWAY!`, `away = L − k` where `k` = longest stream-suffix that is a prefix of the choice — a
  KMP tail-match). Rows climb, **reset and re-climb after a 2 s stall**, and **type out the full word**
  on a win. Computed entirely client-side from the existing `Terminal` stream (`utils/nearMiss.ts` +
  `hooks/useNearMiss.ts`, rendered in `GameTerminal.vue` beside the in-game `> POT:` line). No effect
  on outcomes, payouts, pacing, or the seed. See `features/characters-away.md`.
- **Operator** — a casino/betting platform tenant (e.g. BetMGM). Every `SessionParameters` row
  belongs to exactly one operator (`OperatorId` required — no house mode). Scoped on REST via the
  signed **launch token** (`X-Launch-Token`; the operator id is the token `iss`); players enter at
  `/monkify/game/sessions` (the operator is read from the token, not the URL). Fields:
  `ContractStartDate`, optional `ContractEndDate` (null = never
  expires), **`LaunchPublicKey`** (+ optional `LaunchKeyId`), and a collection of registered
  **`OperatorToken`s**. The old operator-level `PaymentModel` is **removed** — payment is a per-session
  concern (see **PaymentMethod**). Entity: `Operator`.
- **Launch token** — a casino-signed **RS256 JWT** that proves operator + player identity; required
  (`X-Launch-Token` header) on every operator-scoped REST call and validated by middleware in
  `Monkify.Api` against the operator's `LaunchPublicKey`. Claims: `iss`=operatorId, `sub`=player id,
  `exp`/`iat`/`jti`. Failures return a vague 401. See `features/launch-token-validation.md`.
- **Admin token** — an operator-signed **RS256 JWT** with `scope = "admin"`, `iss = operatorId`,
  **no `sub`**, sent as **`X-Admin-Token`** on `/admin/*` REST calls. Validated by
  `AdminTokenValidationMiddleware` against the same `LaunchPublicKey`; **not** contract-gated. Admin
  and launch tokens are not interchangeable. Entry:
  `/monkify/admin#admin_token=<jwt>`. See `features/operator-admin-page.md`.
- **IAdminContext** — scoped request identity set by admin middleware; carries `OperatorId` only
  (no player). Used by admin handlers; kept separate from `ILaunchContext`.
- **PaymentMethod** — enum on **`SessionParameters`** (`Standard = 0`, `SolanaToken = 1`) that decides
  how a session type settles. **Standard** = off-chain via the operator's wallet adapter, always USD,
  player = launch-token `sub`, no wallet. **SolanaToken** = on-chain in a per-operator registered
  `OperatorToken`, token-native units, player = connected wallet + signature. Replaces the removed
  operator-level `Operator.PaymentModel`. Dispatched by `IPaymentMethodResolver`. See
  `features/payment-methods.md`.
- **OperatorToken** — a per-operator registered SPL token backing `SolanaToken` sessions. Fields:
  `Label` (display symbol), `MintAddress`, `SenderAccount`, `TokenOwnerPublicKey`,
  `TokenOwnerPrivateKey` (**encrypted at rest**, never returned/logged), `Decimals`, `Active`. Managed
  in the admin portal (register / list / deactivate — no edit). Entity: `OperatorToken`.
- **SolanaTokenDescriptor** — an immutable record (`MintAddress`, `SenderAccount`, `TokenOwnerPublicKey`,
  `TokenOwnerPrivateKey`, `Decimals`) passed to `ISolanaService` per call so it settles against the
  session's token (private key already decrypted by the caller — Consumers only). Replaces per-bet reads
  of the global `GeneralSettings.Token`.
- **IPaymentMethodService / resolver** — the unified settlement strategy (`Debit`/`Credit`/`Refund` →
  `PaymentResult`) with `StandardPaymentMethodService` (→ `IOperatorWalletService`) and
  `SolanaTokenPaymentMethodService` (→ `ISolanaService` + a `SolanaTokenDescriptor`).
  `IPaymentMethodResolver.Resolve(parameters)` keys on the session's `PaymentMethod`;
  `GetAvailableMethods(operatorId)` returns Standard iff a wallet provider is configured and SolanaToken
  iff ≥1 active `OperatorToken`. See `features/payment-methods.md`.
- **CryptoTransactionLog** — on-chain detail for a `SolanaToken` movement, always tied to a `Bet`:
  `Signature`, `Wallet`, `MintAddress`, `SenderAccount`, token-native `Amount`, `RawTokenAmount`,
  `Decimals`, `Reason`. `TransactionLogId` is **null** for the bet-**placement** record ("what the user
  paid") and **set** for a Credit/Refund movement. Entity: `CryptoTransactionLog`.
- **ISecretProtector** — encryption-at-rest for `OperatorToken.TokenOwnerPrivateKey`. Shipped impl
  `AesSecretProtector` = **AES-256-GCM**, key derived (SHA-256) from the
  `GeneralSettings.SecretProtection.Key` passphrase shared by both hosts; base64
  `[nonce][tag][ciphertext]`. The API `Protect`s on register; Consumers `Unprotect`s only to build a
  `SolanaTokenDescriptor` for a transfer. *(Deviates from the spec's Data-Protection plan.)*
- **ContractIsActive** — domain helper on `Operator`: true when `ContractStartDate <= UtcNow` and
  contract has not expired. Inactive operators return empty data or redirect to `/error` (vague). The
  active-operator lookup is cached via `IOperatorService.GetActiveOperatorById`.
- **TransactionLog** — the **unified money-movement ledger** per bet. Each row has a `TransactionLogType`
  (`Credit | Debit | Refund`), `Amount`, a **`PaymentMethod`**, a nullable `ExternalTransactionReference`
  (the operator wallet ref for `Standard`; the on-chain signature for `SolanaToken`), and nullable
  `Reason`. The old `Signature` column is **removed** (on-chain specifics now live in
  `CryptoTransactionLog`). Used for reward/refund idempotency: reward subtraction reads `Credit` rows,
  the already-refunded guard reads `Refund` rows. (The separate `RefundLog` table was folded in here.)
  See `features/payment-methods.md`.
- **TransactionLogType** — enum on `TransactionLog` (`Credit = 0`, `Debit = 1`, `Refund = 2`); `Credit` is
  the default so legacy reward rows keep their meaning.
- **Operator wallet API** — the off-chain settlement path for **`Standard`** session types: the
  operator's own wallet that Monkify instructs to **debit** stakes, **credit** winnings, and **refund**
  cancelled bets in USD (Monkify holds no float). Integrated via a per-operator adapter
  (`IOperatorWalletService`) selected by a named provider in config (`IOperatorWalletResolver`), reached
  through `StandardPaymentMethodService`. `SolanaToken` sessions settle on-chain instead. See
  `features/operator-wallet-api.md` + `features/payment-methods.md`.
- **Pre-bet check / RG hook** — an **opt-in**, per-operator responsible-gaming check Monkify calls in
  `RegisterBetHandler` after local validation and **before** the wallet debit, so the operator can
  **allow or deny** a bet (self-exclusion, limits, cooling-off, KYC, blocked). Per-operator adapter
  (`IPreBetCheckService`) selected by a named provider in `GeneralSettings.OperatorRgChecks`
  (`IPreBetCheckResolver`, which returns **null → skip** when unconfigured — the default). Applies to
  **both** payment methods (it runs before settlement, whichever strategy applies). A denial returns a
  **specific** player message (`PreBetCheckDenyReason`: `SelfExcluded | LimitReached | CoolingOff |
  KycRequired | Blocked | Other`); a timeout/error is **fail-closed** (rejected, no settlement). Skipped
  for anonymous free bets. Log-only, no DB row. See `features/rg-pre-bet-hook.md`.
- **PresetChoice** — predefined winnable choices for a game type.
- **LotteryParameters** — config for lottery-style sessions (frequency, pot); scheduled via Hangfire.
- **Pot** — the amount winners split, shown as the `> POT:` line in the game terminal for **every**
  session type (free-type race, preset race, lottery). Client-side, display-only: races = `Σ bets`,
  lottery = `lotteryParameters.lotteryPot` base `+ Σ bets`; seeded on load, accumulated live from the
  `/Bets` SignalR stream, reset/reseeded on rollover; free bets (`amount = 0`) add nothing. Rendered
  via `formatAmount` (SolanaToken → the token symbol/decimals; Standard → USD or preferred display
  currency). Settlement math is separate — `BetDomainService.SetPotAmount` remains the payout source of
  truth. See `features/session-pot-display.md`.
- **Display name** — optional `OperatorPlayer.DisplayName` (1–20 chars, trimmed). Set via the
  customization modal (`PATCH /players/preferences`); rendered on bet DTOs and recent-bets SignalR
  events for self and others; falls back to truncated `externalPlayerId`. See
  `features/player-customization-modal.md`.
- **Preferred display currency** — `OperatorPlayer.Currency` (default `USD`). Offered to **all**
  operators (chosen from the backend definitive fiat set, 33 codes), but affects **rendering of
  `Standard` sessions only** — `SolanaToken` sessions always render token-native, regardless of the
  choice. Rendering-only — backend always stores and transmits USD; client multiplies by cached
  USD→currency rates from `GET /fx/rates`. See `features/player-customization-modal.md`.
- **FX rates / display conversion** — token-gated `GET /fx/rates` returns the definitive currency
  list with USD-based rates (Redis cache, 6h TTL; `IExchangeRateProvider` + FreeCurrencyAPI or mock).
  Frontend `useDisplayCurrency.formatUsd` applies conversion for **`Standard`** sessions only;
  settlement and operator-wallet amounts stay USD.
- **Session chat message** — a plain-text line broadcast within one session instance over
  `ActiveSessionsHub.SendChatMessage`; attributed by client-resolved **display name**; persisted to
  `SessionChatMessage` for audit/retention only (not replayed). See `features/session-chat.md`.

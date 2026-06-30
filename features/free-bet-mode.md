# Feature — Free bet mode

> Status: **shipped.** Authoritative spec: `../specs/free-bet-mode.md`
> (merged via `feature/free-bet-mode` on 2026-06-26).

## What it does

Dev/QA-only path for placing bets **without** a Solana wallet or on-chain payment, while exercising
the full session lifecycle (typer, lottery min-player checks, rewards). Production must reject free
bets at the API and hide the UI toggle.

## Core rules

- **Dual guard:** backend `GeneralSettings.Sessions.AllowFreeBets` (default `false` in production
  deploy config) **and** frontend `allowFreeBets` (see below). Both must allow, **and** the session
  type must have `SessionParameters.AllowFreeBet = true`, before a free bet is accepted.
- **Detection:** a free bet is inferred at runtime — `amount == 0` **and** empty/missing
  `paymentSignature`. No `isFreeBet` request field. Mixed signals (`amount == 0` with a signature, or
  `amount > 0` with empty signature) are **invalid**, not free bets.
- **Wallet (register-bet body):** optional for free bets. Required for paid bets (unchanged). When
  provided, backend upserts `OperatorPlayer` and sets `Bet.OperatorPlayerId`; anonymous free bets
  omit wallet and keep `OperatorPlayerId` NULL.
- **UI toggle:** when the frontend flag is on and `allowFreeBet` is true on the session type, the
  game shows **"Free bet mode"** (off by default). Toggle on skips wallet connect and `sendToken`
  even if a wallet is connected.
- **Frontend flag:** `allowFreeBets` is **enabled by default**; set `VUE_APP_ALLOW_FREE_BETS=false`
  in production builds to hide the toggle.
- **Pot:** free bets use `Amount = 0` and are excluded from pot sum (`SetPotAmount` sums amounts only).
  Mixed free + paid sessions: paid bets fund the pot; free bets still participate in the typer.
- **Rewards:** free-bet winners are marked `Rewarded` with an info log only — no Solana transfer.
  Blockhash pre-check runs only when at least one **paid** winner exists.
- **Refunds:** invalid free bets, lottery under-subscription, and duplicate free bets do **not**
  publish `RefundInvalidBetEvent` or append `RefundWarning`. `RefundInvalidBetConsumer` skips when
  `Value == 0`.
- **Duplicates:** when `OperatorPlayerId` set — `(sessionId, operatorPlayerId, choice)`. Anonymous
  free (NULL FK) — `(sessionId, choice)` only.
- **Min players (free bets count individually):** in `BetPlacedConsumer` every **free** bet counts as
  an **individual player** (distinct key = `Bet.Id`), even when the bets resolve to the **same**
  `OperatorPlayer` (e.g. a regulated operator's token `sub`, or one wallet placing several free bets).
  Paid/identified bets still de-duplicate by `OperatorPlayerId`. So one user can satisfy
  `MinimumNumberOfPlayers` on their own with several free bets.
- **Session types:** all types (race + lottery). Dev seed sets `AllowFreeBet = true` on all six
  default session types; DB default for new rows remains `false`.
- **Operators:** unchanged — `x-operator-id` still required; operator must match session.

## Key code touchpoints

**Backend (`monkify-backend`)**

- Entity: `SessionParameters.AllowFreeBet`; config: `SessionSettings.AllowFreeBets` in
  `monkifysettings.json` → `GeneralSettings.Sessions`.
- Helper: `Monkify.Domain/Bets/Services/BetExtensions.cs` — `IsFreeBetRequest`, `IsFreeBet`.
- Handler: `RegisterBetHandler` — gate, skip payment validation, alternate duplicate keys, no refund
  event for invalid free bets.
- Domain: `BetDomainService.ChoiceIsValidForSession(..., isFreeBet)` — amount rules for free vs paid.
- Consumers: `BetPlacedConsumer` (free bets counted individually for min-players; skip lottery refund
  events), `RewardSessionConsumer` (skip Solana for free winners),
  `RefundInvalidBetConsumer` (skip when amount 0).
- DTO: `SessionParametersDto.AllowFreeBet` on `GET /sessions/active-types`.
- Migration: `AddAllowFreeBetToSessionParameters`. Seed: `DefaultSessionParameters.cs`.

**Frontend (`monkify-front`)**

- Flag: `src/constants/featureFlags.ts` — `allowFreeBets`.
- Composable: `src/hooks/useSession.ts` — `freeBetMode`, `lastOwnBetChoice`, branched `bet()`.
- UI: `GameTerminal.vue` (toggle, status labels), `GameConsole.vue` (wallet gate bypass),
  `GameMonkey.vue` (bet animation queue for free bets).
- Types: `ActiveSession.allowFreeBet` in `src/types/index.ts`.

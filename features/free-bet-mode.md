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
- **Wallet:** optional for free bets (`Wallet` may be `""`). Required for paid bets (unchanged).
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
- **Duplicates:** paid bets — `(sessionId, wallet, choice)`. Free bet without wallet —
  `(sessionId, choice)` only. Free bet with wallet connected — `(sessionId, wallet, choice)`.
- **Lottery min players:** each free bet without a wallet counts as a distinct player
  (`free:{betId}` identity in `BetPlacedConsumer`); empty wallets no longer collapse to one player.
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
- Consumers: `BetPlacedConsumer` (player count + skip lottery refund events),
  `RewardSessionConsumer` (skip Solana for free winners),
  `RefundInvalidBetConsumer` (skip when amount 0).
- DTO: `SessionParametersDto.AllowFreeBet` on `GET /sessions/active-types`.
- Migration: `AddAllowFreeBetToSessionParameters`. Seed: `DefaultSessionParameters.cs`.

**Frontend (`monkify-front`)**

- Flag: `src/constants/featureFlags.ts` — `allowFreeBets`.
- Composable: `src/hooks/useSession.ts` — `freeBetMode`, `lastOwnBetChoice`, branched `bet()`.
- UI: `GameTerminal.vue` (toggle, status labels), `GameConsole.vue` (wallet gate bypass),
  `GameMonkey.vue` (bet animation queue for free bets).
- Types: `ActiveSession.allowFreeBet` in `src/types/index.ts`.

# Feature — OperatorPlayer identity (unified player model)

> Status: **shipped.** Authoritative spec: `../specs/operator-player-identity.md`
> (merged via `feature/operator-player-identity` on 2026-06-29).

## What it does

Introduces **operator-scoped player identity** for crypto-native and regulated B2B deployments.
Crypto wallet pubkeys and casino external player ids both live on `OperatorPlayer.ExternalPlayerId`,
distinguished by `IsCrypto`. Identified bets link via `Bet.OperatorPlayerId`; anonymous free bets
(with no wallet) keep a NULL FK.

## Core rules

- **Unified identity:** one `OperatorPlayer` row per `(OperatorId, ExternalPlayerId)`; unique index
  enforces no duplicates. No PII columns on `OperatorPlayer`.
- **`Bet.Wallet` removed:** persisted player identity is only via `OperatorPlayerId` (nullable).
  `RefundLog.Wallet` remains for on-chain refund audit rows.
- **Crypto path:** register-bet body still sends `wallet`; backend upserts
  `OperatorPlayer(ExternalPlayerId=wallet, IsCrypto=true)` and sets `Bet.OperatorPlayerId`.
- **Regulated path:** body sends `externalPlayerId` (and optional `displayName`); upsert with
  `IsCrypto=false`. Currency defaults to **USD** on create/backfill — not accepted on register-bet.
- **Anonymous free bet:** no wallet and no `externalPlayerId` → no `OperatorPlayer` row;
  `Bet.OperatorPlayerId` stays NULL.
- **Paid bet without identity:** rejected (unchanged).
- **Duplicate bets:** when `OperatorPlayerId` set → `(SessionId, OperatorPlayerId, Choice)`;
  when NULL (anonymous free) → `(SessionId, Choice)` only.
- **Min-player count:** distinct key = `OperatorPlayerId` when set; when NULL use `Bet.Id` per bet
  (`BetPlacedConsumer`). **Exception — free bets always count individually** (`Bet.Id`), even when they
  resolve to the same `OperatorPlayer`, so a single tester can start a session (see `free-bet-mode.md`).
- **Solana validate/reward/refund:** resolve payout address from `OperatorPlayer.ExternalPlayerId`
  via `Bet.GetWalletAddress()` — not from a bet column.
- **API/DTOs:** bet responses and SignalR events expose `externalPlayerId` (nullable). Register-bet
  still accepts `wallet` for crypto UX.
- **Migration:** `AddOperatorPlayerIdentity` backfills crypto wallets into `OperatorPlayers` before
  dropping `SessionBets.Wallet`.

## Key code touchpoints

**Backend (`monkify-backend`)**

- Entity: `Monkify.Domain/Sessions/Entities/OperatorPlayer.cs`.
- Bet: nullable `OperatorPlayerId` FK; helper `BetExtensions.GetWalletAddress(Bet)`.
- DbContext + migration: `20260629141822_AddOperatorPlayerIdentity`.
- Handler: `RegisterBetHandler` — `UpsertOperatorPlayerAsync`, identity resolution, duplicate keys.
- Consumers: `BetPlacedConsumer`, `RewardSessionConsumer` (include `OperatorPlayer` on bets).
- Services: `SessionService`, `SolanaService`; DTO/event: `BetDto`, `BetNotificationEvent`.
- History filter: `GetSessionHistoryHandler` joins `OperatorPlayer.ExternalPlayerId`.
- Tests: `RegisterBetHandlerTests`, `BetPlacedHandlerTests`, `BetTestFactory`, `SolanaServiceTests`.

**Frontend (`monkify-front`)**

- Types: `AppliedBet.externalPlayerId` in `src/types/index.ts`.
- Composable: `useSession.ts` — maps SignalR `externalPlayerId`; register-bet still sends `wallet`.
- UI: `GameTerminal.vue`, `GameMonkey.vue` — player count, "YOU", winners use `externalPlayerId`.

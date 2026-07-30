# Feature — Player identity (the wallet is the player)

> Status: **shipped.** Authoritative spec: `../specs/operator-player-identity.md`
> (merged via `feature/operator-player-identity` on 2026-06-29).
>
> **Rewritten (2026-07-30) by `features/remove-operators.md`:** the entity was `OperatorPlayer`, keyed
> `(OperatorId, ExternalPlayerId)`, and identity could arrive either as a launch-token `sub` or as a
> wallet depending on the game's payment method. Operators, launch tokens and the `Standard` method are
> gone: the entity is **`Player`**, the wallet that signed the transfer is the only identity, and
> `Bet.OperatorPlayerId` is now `Bet.PlayerId`. `Player.Currency` was dropped with the `Standard`
> method. *(Renamed from `operator-player-identity.md`.)*

## What it does

Gives every paid bet a stable player row keyed on the **connected wallet**, so display names, own-bet
highlighting, "mine only" history filtering, per-round bet limits and self-exclusion all have one
thing to hang off. Anonymous free bets deliberately have none.

## Core rules

- **One `Player` row per wallet.** `ExternalPlayerId` (the wallet address) carries a unique index on
  its own. No PII columns. The field is named `ExternalPlayerId` rather than `Wallet` because that is
  what every DTO, SignalR event and history filter already calls it.
- **The wallet is the only identity source.** A paid bet sends `wallet` in the register-bet body and
  the handler upserts the `Player`, setting `Bet.PlayerId`.
- **Anonymous free bet:** no wallet ⇒ no `Player` row, `Bet.PlayerId` stays NULL.
- **Paid bet without identity:** rejected.
- **No `Bet.Wallet` column.** On-chain payout and refund addresses resolve from
  `Player.ExternalPlayerId` via `Bet.GetWalletAddress()`.
- **Duplicate bets:** when `PlayerId` is set → `(GameId, PlayerId, Choice)`; when NULL (anonymous
  free) → `(GameId, Choice)` only.
- **Min-player count:** distinct key = `PlayerId` when set; when NULL, `Bet.Id` per bet
  (`BetPlacedConsumer`). **Exception — free bets always count individually** (`Bet.Id`), even when
  they resolve to the same `Player`, so a single tester can start a game (see `free-bet-mode.md`).
- **`DisplayName` is written by the prefs endpoint, and that write is signature-gated.** A wallet
  address is public, so `PATCH /players/preferences` requires an ed25519 signature over a
  purpose-scoped single-use nonce. A display name may also ride along on a register-bet body, which
  is safe because that request already proves wallet control by carrying a confirmed on-chain
  transfer from it. See `features/player-customization-modal.md`.
- **Self-exclusion lives on this row** (`SelfExcludedAt` + `SelfExcludedUntil`) and is evaluated per
  bet, so an expiry needs no scheduled job. See `features/legal-docs-self-exclusion.md`.
- **API/DTOs:** bet responses and SignalR events expose `externalPlayerId` (nullable).

## Key code touchpoints

**Backend (`monkify-backend`)**

- Entity: `SpaceStroker.Domain/Games/Entities/Player.cs`.
- Bet: nullable `PlayerId` FK; helper `BetExtensions.GetWalletAddress(Bet)`.
- Handler: `RegisterBetHandler` — player upsert, identity resolution, duplicate keys.
- Service: `GameService.InsertBet` detaches `bet.Game` and `bet.Player` before `AddAsync` so EF does
  not re-insert a persisted graph, and **restores both** — the caller writes the placement ledger row
  from `bet.Player` right after, so restoring only `Game` blanked the `Wallet` on every placement row.
- Consumers: `BetPlacedConsumer`, `RewardGameConsumer` (include `Player` on bets).
- History filter: `GetGameHistoryHandler` joins `Player.ExternalPlayerId` for "mine only".

**Frontend (`monkify-front`)**

- Types: `AppliedBet.externalPlayerId` in `src/types/index.ts`.
- Composable: `useGame.ts` — maps SignalR `externalPlayerId`; register-bet sends `wallet`.
- UI: `GameTerminal.vue`, `GameMonkey.vue` — player count, "YOU", winners use `externalPlayerId`.

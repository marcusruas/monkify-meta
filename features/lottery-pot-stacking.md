# Feature — Lottery Pot Stacking

> Status: **shipped.** Authoritative spec: `../specs/lottery-pot-stacking.md`
> (merged via `feature/lottery-pot-stacking` on 2026-07-14).

## What it does

Lottery pots now behave like a real jackpot. When a lottery round pays out **no one**, the un-won
money **stacks** into the next round of that same lottery instead of vanishing, so the pot grows until
someone finally wins. When an operator **deactivates** a lottery that is still holding un-won money,
they must nominate **another eligible lottery to transfer the pot to** — it is never destroyed. A
running "carried jackpot" is stored per game and shown to players in the `> POT:` line.

## Core rules

- **The jackpot lives on the game.** `Game.CarriedExtra` (`decimal`, `numeric(18,9)`, default
  `0`) holds the un-won money carried in from prior rounds, **on top of** the config base
  `LotteryParameters.LotteryPot`. The effective pot is `PotAmount = Σ bets + LotteryPot + CarriedExtra`
  (`BetDomainService.SetPotAmount`). `CarriedExtra` is `0` for every non-lottery game and for a
  fresh lottery or the round right after a paid win.
- **A lottery pays no one in two ways.** (a) A **preset** lottery draws a winning choice **nobody bet
  on** (`SpaceStrokerTyper` registers every preset with count 0, so the natural/fallback draw can land on an
  un-backed preset → `NumberOfWinners == 0`). (b) **Under-subscription** — fewer than
  `MinimumNumberOfPlayers` by draw time, so the draw never runs and paid stakes are refunded. A
  **free-type** lottery always has a winner when it draws.
- **Carry rule (single source of truth: `LotteryPotCarry.NextCarriedExtra(Game)`)**, applied once
  when the successor game is created:
  - **Preset no-winner draw** → successor `CarriedExtra = prev.CarriedExtra + Σ predecessor stakes`.
    The losing stakes are **not** refunded — they join the jackpot (as they already would in a normal
    preset round).
  - **Under-subscription** (`NotEnoughPlayersToStart`) or abrupt close (`GameEndedAbruptely`) →
    successor `CarriedExtra = prev.CarriedExtra` **unchanged** (stakes refunded as before; an idle
    round does not grow the jackpot).
  - **Paid win** (`NumberOfWinners > 0`, including a win backed only by a free bet) → successor
    `CarriedExtra = 0` (reset to the config base).
- **Where the carry is decided.** `RunGameConsumer` computes it after a draw; `BetPlacedConsumer`
  passes it on the under-subscription rollover; the Consumers **startup backfill** inherits it from a
  type's most recent game so a restart mid-jackpot does not reset the pot. Non-lottery types always
  carry `0`. The carry is applied exactly once per predecessor (one successor per rollover).
- **Applies to both payment methods.** `Standard` (off-chain USD) and `SolanaToken` (on-chain)
  lotteries both stack.
- **Deactivation transfers an un-won pot.** `POST /admin/game-types/{id}/deactivate` accepts an
  optional body `{ targetLotteryId }`. When the lottery's current game holds `CarriedExtra > 0`:
  - A `TargetLotteryId` is **required**; omitting it returns an admin validation error and the lottery
    stays active.
  - The target must be **eligible**: same operator, `Active`, a lottery, the **same `PaymentMethod`**,
    and — for `SolanaToken` — the **same `OperatorTokenId`** (same mint/currency), and not the source
    itself. No eligible target → deactivation is **blocked** with a clear admin error; the pot is
    neither destroyed nor refunded.
  - On success the source's `CarriedExtra` is added to the **target's current bet-accepting game**
    `CarriedExtra` (the source game is zeroed), then the type is soft-deactivated
    (`Active = false`, `GameTypeDeactivatedEvent`) as before. The config base is untouched — the
    target keeps its own base.
  - When `CarriedExtra == 0`, deactivation is unchanged from before (no target needed).
- **Admin visibility.** `GET /admin/game-types` exposes each lottery's current `CarriedExtra` (from
  its live game) so the portal can prompt for a transfer and filter eligible targets.
- **Player visibility.** The active-game DTO carries `CarriedExtra`; the `> POT:` line seeds from
  `LotteryPot + CarriedExtra + Σ live bets`, so the growing jackpot is visible before, during, and
  after a round (settlement math is unchanged — display only).
- **Testable in dev.** A seeded, dev-only 1-minute **preset** lottery `6L Minute Menagerie` (Standard,
  Letters, length 6, 8 animals, base pot 4, min 2, free bets on) lets the stack be exercised roughly
  every minute — a preset lottery is the only shape that can produce the no-winner jackpot.

## Key code touchpoints

**Backend (`monkify-backend`)**

- Entity/EF/migration: `SpaceStroker.Domain/Games/Entities/Game.cs` (`CarriedExtra`),
  `SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` (inline mapping `HasPrecision(18,9)`),
  `SpaceStroker.Infrastructure/Migrations/*_AddGameCarriedExtra.*`.
- Carry rule: `SpaceStroker.Domain/Games/Services/LotteryPotCarry.cs`.
- Pot math: `SpaceStroker.Domain/Bets/Services/BetDomainService.cs` (`SetPotAmount`).
- Rollover wiring: `SpaceStroker.Infrastructure/Services/Games/GameService.cs`
  (`CreateGame(parameters, carriedExtra)`, `GetMostRecentGame`),
  `SpaceStroker.Infrastructure/Consumers/RunGame/RunGameConsumer.cs`,
  `SpaceStroker.Infrastructure/Consumers/BetPlaced/BetPlacedConsumer.cs`, `SpaceStroker.Consumers/Program.cs`
  (startup backfill).
- Admin transfer: `SpaceStroker.Api/Controllers/AdminController.cs`,
  `SpaceStroker.Infrastructure/Handlers/Admin/DeactivateGameType/{DeactivateGameTypeRequest,DeactivateGameTypeHandler}.cs`,
  `SpaceStroker.Infrastructure/Contracts/Admin/DeactivateGameTypeBody.cs`,
  `SpaceStroker.Infrastructure/Handlers/Admin/GetOperatorGameTypes/GetOperatorGameTypesHandler.cs`,
  `SpaceStroker.Infrastructure/Contracts/Admin/GameTypeAdminDto.cs`.
- Player DTO: `SpaceStroker.Infrastructure/Contracts/Games/ActiveGameDto.cs`.
- Seed: `SpaceStroker.Consumers/DefaultGameParameters.cs` (`PresetLottery` factory + `6L Minute Menagerie`).
- Tests: `SpaceStroker.Tests/UnitTests/Domain/Games/LotteryPotCarryTests.cs`, extended
  `BetDomainServiceTests`/`CreateGameTests`/`AdminGameTypeHandlerTests`;
  `SpaceStroker.Tests.Integration/LotteryPotStackingFlowTests.cs` (preset no-winner stack + deactivation transfer).

**Frontend (`monkify-front`)**

- `src/types/index.ts` — `GameInstance.carriedExtra`, `GameTypeAdmin.carriedExtra`.
- `src/hooks/useGame.ts` — `> POT:` seed includes `carriedExtra`.
- `src/services/adminService.ts` — `deactivateGameType(id, targetLotteryId?)`.
- `src/components/admin/GameTypeManager.vue` — eligible-target prompt + backend error surfacing on deactivate.

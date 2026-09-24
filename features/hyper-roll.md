# Feature — Hyper Roll

> Status: **shipped.** Authoritative spec: [`../specs/hyper-roll.md`](../specs/hyper-roll.md), with the
> solo-round redesign in [`../specs/hyper-roll-solo-rounds.md`](../specs/hyper-roll-solo-rounds.md)
> (its decision 35) and the teaching surfaces in [`../specs/hyper-roll-howto.md`](../specs/hyper-roll-howto.md).
> Code reached `main` in `monkify-backend`, `monkify-front` and `monkify-meta` on **2026-09-18** via
> `feature/hyper-roll-handoff` (live in prod the same day); `monkify-lp` and this doc merged from
> `feature/hyper-roll` on **2026-09-22**. Trello [#183](https://trello.com/c/XtPzB6wZ/183-hyper-roll-game).
>
> ⚠️ **Board modes (2026-09-24, #193):** the type now offers three boards — AVERAGE (5 words), SUPER (6)
> and HYPER (8, the board described below). Ladders, the per-mode multiplier map, the 74-word pool,
> `GameBoardModes`, `Game.BoardMode` and the mode keys are in [`hyper-roll-modes.md`](hyper-roll-modes.md);
> where this doc says "the board" or "the ladder", read **the HYPER mode**.

## What it does

Hyper Roll is a **player-vs-house** game that replaced Beast Mode. The player gets a board of **5, 6 or 8
words** (the AVERAGE / SUPER / HYPER mode they picked) and picks one. They stake any amount of SOL between the type's minimum and maximum, and pull the lever.
Edson types as usual, then a **provably-fair weighted draw** picks the winning word. If it is the player's word, the
**treasury pays `stake × that word's multiplier`** (4× to 10×). There is no pot, no split and no house cut:
the house's margin is built into the odds, and every word on a board pays back that mode's RTP
(**91.50 / 91.14 / 91.84%**).
Every round belongs to **one player** (solo rounds).

## Core rules

### The money model

- **Payout = `stake × multiplier`, no commission.** `CalculateHouseEdgeReward` skips the pot division and
  the house cut; `GameParametersDto.CommissionPercentage` is **0** for a house-edge type. The STATS badge
  reads **`91.84% RTP`** where the other types show `N% HOUSE CUT`.
- **Each mode's board follows a fixed ladder of `length:multiplier:weight` slots** (`GameBoardModes.Ladder`;
  `GameParameters.BoardLadder` is only a legacy HYPER mirror). The HYPER ladder, as multiplier:weight:
  `10:91837, 10:91837, 4:229591, 6:153061, 8:114796, 8:114796, 9:102041, 9:102041` (Σ = 1,000,000).
  A slot's chance is `weight ÷ 1,000,000`, so `10×` lands 9.18% of the time and `4×` lands 22.96%. Every slot returns
  91.836–91.837%, so `RtpPercent = 91.84` is **the** payback, not a ceiling, and the copy never says
  "up to". The house keeps **8.16%** of turnover. (Spec decision 37, the fourth odds revision; the
  model allows unequal paybacks, but the shipped board does not use it.)
- **Validation:** a ladder is valid when `|maxReturnPercent − RtpPercent| ≤ 0.25`
  (`HouseEdgeRules.Validate`). Multiplier cap **10×**, floor 1.01×.
- **Stake bounds:** `RequiredAmount` is the **minimum** (**0.002 SOL** since the `hyper-roll-min-stake`
  change; launched at 0.01) and `MaximumStake` the maximum (**0.1 SOL**, sized against a ~20 SOL
  treasury: max stake ≈ treasury ÷ 200). Both are **create-only**: `HouseEdgeReconciler` never touches
  them, so moving a bound on an existing DB is a guarded hand-run SQL (`deploy/sql/hyper-roll-min-stake.sql`)
  plus an `ActiveParameters` cache drop. Out of bounds → vague `InvalidAmount`, paid stake refunded.
- **Bankroll = the treasury** (`Operator.Token.TokenOwnerPublicKey`), the same wallet every type bets into.
  There is no separate keypair and no new secret.
- **Partners** are still paid `turnover × 2% × SharePercentage` on Hyper Roll traffic, although the margin
  here is the 8.16% RTP gap and there is no 2% cut. Accepted; see
  [`partner-player-roster.md`](partner-player-roster.md).

### The word pool and the board

- The pool is the reworked **`PresetChoice`** table (`Multiplier`, `Active`), seeded from
  `DefaultGames[Hyper Roll].HouseEdge.Words`. It has **74 lowercase words, lengths 4–10**
  (10/10/10/10/16/9/9 words). **The ladder slot's length picks the word and the slot sets the
  multiplier**, so the same word pays differently per mode; `PresetChoice.Multiplier` is a legacy HYPER
  mirror (the table below is HYPER's map: 6 letters → 4×, 7 → 6×, 8 → 8×, 9 → 9×, 10 → 10×). A config-binding
  unit test enforces the length rule on the shipped pool. At runtime `ValidateMode` checks length within
  4–10, charset, uniqueness and that every mode's length tiers can be filled.
- **`HouseEdgeReconciler` self-heals** the words, ladder, `RtpPercent` and `ChoiceRequiredLength` (10)
  on existing databases at Consumers startup. Odds revisions therefore ship as settings-only changes with no
  migration. Adding or removing a word takes a config change plus a Consumers redeploy; there is no admin CRUD.
- **The board is one word drawn per ladder slot of the chosen mode**, using ordinary randomness, with no repeated word inside a
  board. It is persisted in **`GameBoardChoices`** (word, multiplier, **weight**). Only the winner draw is
  provably fair. Which word fills a slot never changes that slot's weight or EV, so the board draw has nothing
  to exploit.
- A type whose active pool cannot fill the ladder **never opens a round** (logged at Error).

### Solo rounds (decision 35)

- **One player per round.** `POST /games/{gameTypeId}/boards` opens an anonymous board (a `WaitingBets`
  round with `BoardExpiresAt`). The first and only bet binds `Game.OwnerPlayerId`, and any second bet on
  that round is refused (paid → refunded).
- **Reroll in place:** `POST /games/rounds/{roundId}/reroll` replaces the 8 board rows and rotates the
  server seed. It is allowed only while the round has no bet. `GET /games/rounds/{roundId}` restores a board
  or re-attaches to a running round after a reload.
- **The bet starts the round immediately** (`HouseEdgeTimeUntilGameStarts = 0`). Hyper Roll has **no automatic
  rollover**: the next board comes from a lever pull or a page reload.
- **Unbet boards expire** after `HouseEdgeBoardTtlMinutes` (30) idle, and each read, pre-check or reroll resets
  the timer. The Hangfire job `house-edge-board-cleanup` deletes them every
  `HouseEdgeBoardCleanupIntervalMinutes` (5).
- Opening a board broadcasts nothing (no `GameReceived`, no `GameOpenedEvent`). Chat for house-edge types
  is per game type. Completed rounds stay in the public feed and history. Per-type stats say
  "biggest win" rather than "biggest pot", and the lobby card has no live pill.
- `MinimumNumberOfPlayers = 1`; the **house seed bet exits silently** for house-edge types (S13), because a
  Warning every round would flood prod logs. **Orbit never auto-bets it** (`AllowOrbit = false`), but a
  player can pay a manual Hyper Roll bet from their Orbit balance (see below). Hyper Roll has no practice-mode
  bet, but `/game/practice?mode=hyper-roll` runs a front-only demo (see `hyper-roll-howto.md`).

### Manual Orbit-paid bets (#194)

- A player can pay a Hyper Roll bet from their Orbit balance: the SPA opens the board as usual
  (`POST /games/{typeId}/boards`, with the chosen mode) and sends the returned **round id** as `gameId`,
  the board word and a variable stake to `POST /orbit/wagers`. The mode is the board's, so the
  multipliers and RTP of AVERAGE / SUPER / HYPER apply unchanged.
- The API pre-checks the stake bounds (`RequiredAmount ≤ stake ≤ MaximumStake`, 9 dp), the maintenance
  switch, and board freshness: a board that is owned, already bet, or **expires within 10 s** is refused
  before any money moves, because the asynchronous placement takes seconds.
- The placement then runs `RegisterHouseEdgeBet` unchanged (coverage lock, solo-round validation, claim).
  The Orbit claim is linked inside the coverage lock's transaction. A coverage refusal after the custody
  transfer is returned on-chain to the Orbit account (case B), never credited ledger-only.
- **Payout:** a winning Orbit-paid bet is credited `stake × multiplier` from the treasury to the **Orbit
  account** (`CreditWin` + a `WinCredit` row). `AddHouseEdgePayout`'s wallet is only the display value on
  the broadcast result.
- `hyperRollBetInFlight` wraps the Orbit branch and its wallet fallback exactly as it wraps a wallet bet.
  See [`orbit-wallet.md`](orbit-wallet.md).

### The draw

- **Natural matching is disabled** for house-edge types. Every round types exactly `MaxCharacters`, and
  the winner is always the fallback, so `winnerDecidedByFallback` is always true. The draw stays count-triggered,
  never wall-clock.
- The winner is **`ProvablyFair.WeightedFallbackIndex`**: a uniform draw over the board's **published integer
  weights**, using 32-bit rejection sampling (no modulo bias) on the existing
  `HMAC-SHA256(serverSeed, prefix + "|fallback")` block. The **KEEP-IN-SYNC triad** is `ProvablyFair.cs`,
  `src/utils/provablyFair.ts` and `public/verify.html`, pinned by a known-answer vector.
- Weights are **published before betting** (`BoardChoiceDto.Weight`) and in
  `GET /games/{id}/verification`. Verifiers always read the stored weight, never one recomputed from the
  multiplier, and reject any weight that is not a positive integer.

### Protecting the treasury

- **Maintenance switch:** `GeneralSettings:Games:HouseEdgeEnabled` (missing = `true`). It is plain config
  read per request, never cached, and flipping it is an API + Consumers redeploy. While it is off, the type
  stays in the lobby with a **MAINTENANCE** badge (`GameParametersDto.UnderMaintenance`), and only **new bets**
  are refused. Open rounds still settle.
- **Coverage check before accepting a bet:** a paid bet is coverable only when
  `treasuryBalance − feeReserve ≥ exposure`. `exposure` is the sum, over **every** unsettled house-edge
  round, of that round's worst-case word payout, counting the new bet. `feeReserve` is 0.001 SOL per
  payout transfer. The balance is a fresh, uncached read, and **a failed read counts as not coverable (fail
  closed)**. The sequence runs under **one global PostgreSQL advisory lock** shared by every house-edge round, so two
  concurrent bets cannot both pass.
- The SPA calls **`POST /games/{gameTypeId}/availability`** (`{ choice, amount }` → `{ available }`,
  read-only, rate limited 60/min, deliberately not under `/bets`) **before the wallet signs**.
  `RegisterBetHandler` repeats the same rule **after the payment lands** as the backstop and refunds via
  `RefundInvalidBet`. Both paths use one message for both causes: **"Mode is under maintenance. Please
  try again later."** It never reveals whether the switch or the treasury refused the bet.
- **Payout-time check (last line of defence):** `RewardGameConsumer` keeps a running treasury balance
  per winner, in bet order, with the 0.001 SOL reserve. It pays every winner it can cover. An uncoverable
  winner gets `NeedsManualAnalysis` with no transfer attempted, and the round gets
  `ErrorWhenProcessingRewards`, so a human resolves it. A failed read there (`null`) does not block; the
  transfer fails on-chain the normal way. Credits stay idempotent against prior `Credit` rows on redelivery.
- **Payouts on the wire** are non-uniform, so they go in `result.payouts: [{ wallet, amount }]` on
  `RewardForWinnersCompleted`. `netRewardPerWinner` stays null for house-edge rounds, because that field is
  uniform by construction and unit-pinned.

### Frontend

- The console shows the 8 official key sprites recoloured into a rarity ladder: `4×` grey, `6×` green,
  `8×` blue, `9×` purple, `10×` **gold** with a sparkle. The terminal table is `KEY WORD MULT PAYS`, and the
  header reads `YOU vs THE HOUSE` + stake. The stake plate has `−`/`+` keys (0.01 grid), **MIN / MAX** keys
  and the three round **mode keys** (the old `.01 / .02 / .05` chips are gone — see
  [`hyper-roll-modes.md`](hyper-roll-modes.md)). The stake field opens at the minimum.
- `> POT:` and the near-miss `N AWAY!` row are **hidden**. `> PAID:` shows only the viewer's own payout.
  A win plays a banner scaled by multiplier (NICE WIN! → MAX HIT!), and a loss prints `NO WIN THIS ROUND.`
- The bet flow checks bounds and the `+0.001 SOL` fee headroom, then `/availability`, then asks for the signature.
- The lobby card reads **"from <min> SOL"**. Art resolves by **shape** (`gameArt.ts`), never by name or id.
  `/how-to-play` has a Hyper Roll mode card and its own `HOW HYPER ROLL WORKS` rail, and the game screen has a
  `HOW TO PLAY [?]` key (`hyper-roll-howto.md`).

### Beast Mode

- **Beast Mode is deactivated** (`Active = false`) by `deploy/sql/beast-mode-deactivation.sql`, run
  by hand in prod on 2026-09-18, and removed from `DefaultGames`. Its settled history and stats stay reachable.
  Hyper Roll is a fresh `GameParameters` row, not a renamed Beast Mode.
- The legacy pari-mutuel preset path (`IsHouseEdge = false`, "the list IS the choices") still exists for the
  test catalogues and the admin portal. Every rule above is gated on **`GameParameters.IsHouseEdge`**, so the
  fixed-price types stay byte-identical.

## Key code touchpoints

**Backend (`monkify-backend/src`)**
- `SpaceStroker.Domain/Games/Entities/GameParameters.cs`: `IsHouseEdge`, `MaximumStake`, `RtpPercent`,
  `BoardLadder`. `PresetChoice.cs`: `Multiplier`, `Active`. `Game.cs`: `BoardChoices`, `OwnerPlayerId`,
  `BoardExpiresAt`. `GameBoardChoice.cs`.
- `SpaceStroker.Domain/Games/Services/HouseEdgeRules.cs` (ladder parse, `Validate`, `ValidateBoard`,
  `TreasuryFeeReservePerTransfer`), `HouseEdgeBoard.cs` (`Draw`), `HouseEdgeCoverage.cs`,
  `ValueObjects/HouseEdgeSlot.cs`, `ProvablyFair.cs` (`WeightedFallbackIndex`), `SpaceStrokerTyper.cs`.
- `SpaceStroker.Domain/Bets/Services/BetDomainService.cs`: `HouseEdgeChoiceIsValid`,
  `CalculateHouseEdgeReward`.
- `SpaceStroker.Infrastructure/Handlers/Games/`: `OpenHouseEdgeBoard`, `RerollHouseEdgeBoard`,
  `GetHouseEdgeRound`, `CheckBetAvailability`, `RegisterBet` (backstop), `GetGameVerification`.
- `SpaceStroker.Infrastructure/Services/Games/HouseEdgeCoverageService.cs` (advisory lock + coverage),
  `HouseEdgeReconciler.cs` (startup self-heal), `GameService.cs`.
- `SpaceStroker.Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs` (running-balance payout check,
  `payouts[]`), `Services/HouseSeed/HouseSeedBetService.cs` (early exit),
  `Jobs/HouseEdgeBoardCleanupJob.cs` (scheduled in `SpaceStroker.Consumers/Program.cs`).
- `SpaceStroker.Api/Controllers/GamesController.cs`: `POST {gameTypeId}/boards`, `GET rounds/{roundId}`,
  `POST rounds/{roundId}/reroll`, `POST {gameTypeId}/availability`.
- Config: `SpaceStroker.Common/spacestrokersettings.json` → `DefaultGames[Hyper Roll].HouseEdge`
  and `GeneralSettings:Games:HouseEdge*`.
- Tests: `SpaceStroker.Tests` (`HouseEdgeRulesTests`, `HouseEdgeBoardTests`, `HouseEdgeCoverageTests`,
  `HouseEdgeCoverageServiceTests`, `HouseEdgeBoardCleanupJobTests`, `HouseEdgeEnabledSettingTests`,
  `BetDomainServiceTests`). `SpaceStroker.Tests.Integration`: `HyperRollFlowTests` (happy path +
  insufficient treasury), `HyperRollSoloRoundsTests`, `HyperRollMaintenanceFlowTests`,
  `HyperRollMisconfigurationTests`, `HyperRollBoardWeightGuardTests`.

**Frontend (`monkify-front/src`)**
- `components/game/GameConsole.vue`, `GameTerminal.vue`, `HyperRollModeDial.vue`, `HyperRollWinFx.vue`,
  `GameStatsPanel.vue`, `GameFairPanel.vue`. `components/game-selection/GameTypeCard.vue`.
- `hooks/useGame.ts` (board open/reroll/restore, availability pre-check, stake), `hooks/useLiveGames.ts`,
  `hooks/usePracticeHyperRoll.ts`.
- `utils/houseEdge.ts`, `utils/provablyFair.ts` + `public/verify.html` (KEEP-IN-SYNC),
  `utils/gameArt.ts` / `gameArchetype.ts`, `services/mainService.ts`, `types/index.ts`.
- `components/guide/GuideModeCards.vue`, `components/guide/sections/GuideHyperRoll.vue`.

**Landing page (`monkify-lp`)**: `src/components/GameModes.vue` (Hyper Roll card, still on the Beast Mode
art), `SpaceRoadmap.vue`, `src/pages/index.astro`, `src/pages/terms.astro` (house-edge clause: up to 10×,
no house cut, 91.84%).

**Ops (`monkify-meta/deploy/sql`)**: `beast-mode-deactivation.sql`, `hyper-roll-min-stake.sql`. Rollout order
(fund the treasury, Beast Mode SQL, Consumers then API, verify `/games/active-types`) is `specs/hyper-roll.md` §15.

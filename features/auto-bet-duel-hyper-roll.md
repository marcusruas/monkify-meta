# Feature — Orbit auto-bet for Monkey Duel & Hyper Roll

> Status: **shipped.** Authoritative spec: [`../specs/auto-bet-duel-hyper-roll.md`](../specs/auto-bet-duel-hyper-roll.md)
> (merged via `feature/auto-bet-duel-hyper-roll` on 2026-10-06). Trello
> [#200](https://trello.com/c/i6thj637). Modal design: mockup **A2 · game tiles**
> (<https://claude.ai/artifact/Uzzm7DYGqqV9GHeYkE3yj7>), trimmed by the owner during QA (see "The modal").
> Orbit itself (custody, the shared-round engine, stop rules) is in [`orbit.md`](orbit.md).

## What it does

Orbit's auto-bet used to play only **Cosmic Sprint**, because it rode the shared round rhythm
(`GameOpenedEvent` → planner → one bet per round). A player can now point auto-bet at **Monkey Duel
(VS HOUSE)** or **Hyper Roll** instead, **one game at a time**, with a stake they set; for Hyper Roll they
also pick a board mode and a multiplier (or RANDOM). Both are **solo** games with no shared round and no
rollover, so a **second engine loop** opens the player's own round, bets it, waits for settlement, pauses
a gap and goes again. The game screen follows each auto round live, and the lever stays locked.

## Core rules

### Which games, and one at a time

- **Solo type** = `GameParameters.IsHouseEdge || IsDuel` (`OrbitSoloTypes.IsSolo`, one spelling for the
  rule). `AllowOrbit` is now **`true`** for Hyper Roll and `Monkey Duel (CoinFlip)` in `DefaultGames`,
  healed onto existing DBs at Consumers startup. `GET /orbit` → `eligibleGameTypes` therefore lists
  **Cosmic Sprint, Hyper Roll and Monkey Duel**. Space Jackpot stays `AllowOrbit: false`.
- **One game at a time** is structural: one `OrbitEnrollment` row per player, one `GameParametersId`.
- **Monkey Duel auto = VS HOUSE only.** The loop opens `DuelKind.House` rounds; Orbit never posts or joins
  OPEN DUELS.
- ⚠️ **The shared-round engine never touches a solo round.** With `AllowOrbit` on, the planner would have
  fanned every enrolled player into a human's Hyper Roll board. Four guards: `PlanPlacements` returns
  early, `IsOrbitEligible` refuses, `PlaceOrbitBetConsumer` skips (`"solo rounds are played by the solo
  loop"`), and `BetPlacedConsumer` does not re-plan a solo round. A human's Hyper Roll bet never pulls Orbit
  players in.

### Settings (`PATCH /orbit/settings`)

- New PATCH fields, null = leave alone: `stake`/`clearStake`, `boardMode`/`clearBoardMode`,
  `multiplier`/`clearMultiplier` (clear multiplier = RANDOM). Every refusal is the vague
  `OrbitRequestInvalid`.
- **Stake**: solo types only, `> 0`, at most **9 dp**, `RequiredAmount ≤ stake ≤ GameParameters.MaximumStake`
  (the duel's ceiling is seeded from `Duel.MaximumStake`). A null ceiling **fails closed**. Null stake ⇒ the
  type's `RequiredAmount`. Never clamped or resized silently.
- **Board mode**: Hyper Roll only, matched case-insensitively against `GameBoardModes`, stored as the row's
  name. Null ⇒ the default mode (lowest `Position`).
- **Multiplier**: Hyper Roll only, must be on the **effective** mode's ladder. Switching mode with a stored
  multiplier the new ladder lacks resets it to RANDOM rather than refusing.
- ⚠️ **Locked while Enabled:** game type, stake, mode and multiplier. A patch that would *change* one of them
  on an enabled enrolment is refused; re-sending the stored value is not a change. `enabled: false` in the
  same patch counts first, so "pause + switch" works in one call. The **word, auto-pick and cash-out rules
  stay editable** while running.
- **Type change**: resets stake, mode and multiplier (values are per type) unless the same patch sets them.
  Leaving Cosmic Sprint **releases its `OrbitChoiceClaim`**; the word preference (`Choice`) is kept, so a
  Sprint word carries over to the duel.
- ⚠️ **No word claim is ever taken on a solo type.** A standing claim on Monkey Duel would block that word in
  everyone's duels. The duel still validates the word (6 letters); Hyper Roll ignores `Choice` entirely
  (it is not measured against its 10-letter board shape).
- **A running duel needs a playable word** (`Choice` or `AutoPickChoice`), judged on the result of the
  patch, so clearing the word on a running duel is refused too. Hyper Roll needs none.
- **Enable** on a solo type (paused → enabled only) clears `WaitingForHouseSince` and `NextSoloRoundAt`, then
  publishes `OrbitSoloRoundDueEvent` instead of `GameOpenedEvent`. A repeat `enabled: true` while running
  does not reset the gap. **Pause** clears the waiting flag and leaves `SoloRoundId`: the round in flight
  settles normally.

### The solo loop (`OrbitSoloAutoBetService.RunOnce`)

Triggered by `OrbitSoloRoundDueEvent(playerId)` on its **own receive endpoint**
(`OrbitSoloRoundConsumer`). Every tick is idempotent; duplicate events (settings, deposit, settlement,
schedule, sweep, startup) are expected and absorbed by the lease. One tick opens at most one round and
places at most one stake.

1. **Eligible?** Enabled, solo type, `Active`, `AllowOrbit`, not `ComingSoon`, not self-excluded. Otherwise
   no-op.
2. **In flight?** While `SoloRoundId` is set, the round is in flight for every status in
   `HouseEdgeCoverage.UnsettledStatuses`, i.e. everything before rewards complete. An ended-but-unsettled
   round still blocks, so a duplicate tick can never stake before stop-loss/threshold ran
   (`OrbitSoloRoundState`). A terminal round is cleared only after a **grace of gap + 60 s** (the settlement
   hook is the fast path). A deleted round is finished at once. ⚠️ **A round in `ErrorWhenProcessingRewards`
   holds the loop forever**, until an operator resolves the payout.
3. **Gap**: if `NextSoloRoundAt` is in the future, reschedule for the remainder and return.
4. **Static refusals** (no lease, no write): a stake outside the type's current bounds (e.g. after a config
   change), a duel with no word and no auto-pick, or a stored duel word the type rejects. These are logged
   and the loop idles until a settings change or deposit re-triggers it.
5. **Lease**: a conditional `ExecuteUpdate` on `SoloLeaseUntil` (`SoloLeaseSeconds`, 60), taken **before** any
   round exists. Its value is a **token**: only the tick that holds it may record a round or release it.
   0 rows ⇒ another tick owns the player.
6. **Funds**: both the **settled** and the **spendable** balance must cover the stake (a manual Orbit wager
   riding elsewhere lowers the spendable one). Short ⇒ release, clear waiting, **no reschedule**. The shipped
   NEEDS MONEY path owns this, and a deposit wakes the loop. **Stop-loss floor**: settled balance
   **strictly below** the floor ⇒ release and run `EvaluateFloor` (the shipped cash-out), no reschedule.
   Orbit low on SOL fees ⇒ release and retry after the gap.
7. **House pre-check, no money yet**: `HouseEdgeEnabled` off, or `IHouseEdgeCoverageService` cannot cover
   `stake × worst multiplier` (Hyper Roll: the effective mode's top multiplier; duel:
   `DuelHouseRules.PayoutMultiplier`) ⇒ **waiting for the house**. Also waiting: a type with no board modes,
   or an open call that returns no round.
8. **Open**: `OpenSoloBoard(params, mode)` (a fresh board every round, no reroll) or
   `OpenDuelRoom(params, DuelKind.House)`. **Record** `SoloRoundId` (lease-token conditional) and push
   `OrbitAutoRound { gameTypeId, roundId, waitingForHouse: false }` **before** placing, so the screen can
   attach while the transfer runs.
9. **Choice**: Hyper Roll → `OrbitSlotPicker` (below). Duel → `Choice`, else
   `OrbitChoiceGenerator.DeriveChoice` when auto-pick is on.
10. **Re-check, then place.** Enabled and self-exclusion are re-read. Then the **unchanged** `OrbitStakePlacer`
    runs (custody → treasury, `RegisterBetRequest { FundedByOrbit, OrbitAutoPlaced }`, memo
    `orbit-solo:{roundId:N}`). The loop itself never writes a ledger row.
11. **Outcome**: `Placed` ⇒ clear waiting; settlement drives the next tick. `InsufficientFunds` ⇒ clear the
    round, no reschedule. `TransferFailed` ⇒ clear the round, gap, reschedule. `RegistrationRefused` (case B,
    money returns via `OrbitStakeReconciliationJob`) ⇒ clear the round, enter waiting. `InDoubt` **and
    `AlreadyClaimed`** ⇒ **hold** `SoloRoundId`; settlement or the sweep moves the loop on. Any exception ⇒
    log Error, drop the lease, hand back a round the placer never touched (keep one it did), retry after the
    gap. A transfer is never retried inside a tick.

- **Waiting for the house** keeps the enrolment **Enabled**, moves no money, sets `WaitingForHouseSince`
  (first value kept across retries), pushes `OrbitAutoRound { roundId: null, waitingForHouse: true }` and
  retries after the gap. No auto-pause. The copy never says which of the treasury, the maintenance switch or
  coverage caused it.

### Hyper Roll slot pick (`OrbitSlotPicker`)

- Candidates = the round's slots carrying the chosen multiplier. RANDOM, or a multiplier the board does not
  carry, means **all slots**.
- One candidate ⇒ it. Several (RANDOM, or a multiplier on two slots such as HYPER 10×) ⇒ a **uniform** pick
  from `HMAC-SHA256(serverSeed, "orbit-slot|{gameId:N}|{playerId:N}|{counter}")`, 32-bit rejection sampling
  (no modulo bias). Uniform rather than weighted, because every slot pays back the same RTP. The pick is
  reproducible offline once the seed is revealed.

### Settlement, refunds, liveness

- `RewardGameConsumer` calls `OnAutoRoundSettled` **after** `EvaluateOrbitFloors` on both its paths, and
  `RefundInvalidBetConsumer` calls it for a refunded `OrbitAutoPlaced` bet (duel give-up, backstop refusal).
  It clears `SoloRoundId` (only when it matches), sets `NextSoloRoundAt = now + gap` and schedules the next
  tick. A failure there never fails the settlement or refund; the sweep recovers it.
- Threshold, floor, stop-loss, the Orbit win toast and Telegram apply **unchanged** (the bet is
  `OrbitAutoPlaced`). `EvaluateZeroOut` compares the balance with the **effective stake** for a solo
  enrolment.
- A **deposit** on a solo enrolment publishes `OrbitSoloRoundDueEvent` instead of the shared re-plan.
- **Consumers startup** publishes a due event for every Enabled solo enrolment (`KickOrbitSoloLoops`).
- **Hangfire `orbit-solo-autobet-sweep`** (every minute, `OrbitSoloAutoBetSweepJob`) reads only and
  publishes a due event for each due player: nothing in flight and gap elapsed, or the in-flight round gone or
  finished (same `OrbitSoloRoundState` rule). It never shortens the gap. It logs at **Error** (first window,
  then hourly) a loop held by an errored payout, or by a round still unsettled 10 min after creation.

### Pace: known limitation

- Config: `Operator.Token.Orbit.SoloRoundGapSeconds` = **5** (committed), `SoloLeaseSeconds` = 60 (code default).
- ⚠️ **The real round-to-round time is ~10–30 s, not 5 s.** It adds up the 5 s gap, Hangfire's Postgres
  schedule polling (~15 s by default; the scheduled tick fires on the next poll), the on-chain custody →
  treasury transfer, and for a duel the 2 s start countdown, on top of the round itself. The owner chose not
  to change the backend for this at ship (2026-10-06). The lever would be a non-Hangfire delay (e.g. a
  MassTransit scheduled message) or a shorter Hangfire poll interval.

### Read side and realtime

- `GET /orbit` (`OrbitDto`) adds `stake`, `boardMode`, `multiplier`, `waitingForHouse` (Enabled and
  `WaitingForHouseSince` set), `currentAutoRoundId` (`SoloRoundId` while that game is in
  `ActiveGamesStatus`) and **`autoStakeInFlight`** (any `OrbitAutoPlaced` bet of the player in an unsettled
  round, any type). Each `eligibleGameTypes` entry adds `isHouseEdge`, `isDuel`, `maximumStake`
  (solo only) and `modes[]` (`name, wordCount, rtpPercent, slots[{multiplier, weight}]`, Hyper Roll only, by
  `Position`).
- **`OrbitAutoRound`** (`GameSettings.OrbitAutoRoundEndpoint`) is pushed from Consumers to the private
  `orbit/{playerId}` group as a camelCase JSON string `{ gameTypeId, roundId, waitingForHouse }`. It is not
  sent to Telegram.
- **`GET /games/rounds/{roundId}`** also serves **House duel** rounds (same `ActiveGameDto`), and a read of
  an unbet House duel shell keeps it alive like a Hyper Roll board. OpenRoom duels and races keep the vague
  `GameNotValidForBets`.

### Frontend

- **The modal** (`OrbitAutoBetSection`, title **LET ORBIT PLAY**, state in `useOrbitAutoBet` owned by
  `OrbitModal`): a row of **game tiles** (`OrbitGameTiles`, glyphs PVP / VS / 10×, art by shape, the
  one-line description only as a tooltip). Then per game: Cosmic Sprint = word + random. Monkey Duel = the
  same word field + random + the **stake plate**. Hyper Roll = `OrbitMultiplierPicker` (mode tiles, the
  mode's distinct multipliers as `boardTier`-coloured chips + **RND**, one odds line
  `Pays N× · wins ~1 in X · R% RTP`, nothing for RND) + the stake plate. `OrbitStakePlate` is
  `−` / LCD / `+` / MIN / MAX on a 0.01 grid, and the LCD also takes a **typed** amount (≤ 9 dp, comma or
  dot, clamped). It PATCHes debounced. The owner removed the per-tile subtitles, the duel VS strip, the
  "same word as Cosmic Sprint" line and the RND sentence in QA so the section fits without scrolling.
- **Locks**: tiles, mode, multiplier and stake are disabled while Orbit is **enabled** (running, or enabled
  but unfunded), with the tooltip *"Pause auto-bet to switch games or change its setup."* The word and rules
  stay live. Tiles also lock while a settings save is in flight. A word saved before any tile press carries
  the selected tile's type.
- **Waiting**: *"Waiting for the house…"* under the status line (tag stays RUNNING) and in the game
  terminal.
- **Funds**: START blockers, `needsFirstDeposit` and `isPausedForFunds` compare with the **effective stake**
  (`effectiveStakeFor`). ⚠️ **The client auto-park never fires while `autoStakeInFlight`**: an all-in stake
  reads 0.00 until settlement, and the tab once parked a loop seconds before a 3× win landed.
- **Game screen** (`useGame`): `orbitAutoPlaysSoloGame` (Orbit enabled on the solo type on screen). The page
  never opens a board or looks up a duel of its own then. It attaches to `currentAutoRoundId` on load (after
  waiting for the boot Orbit read) and to every `OrbitAutoRound` push through a serialized
  `attachRound(roundId)` (latest wins). Rollover is held so a result is not wiped. Attached auto bets are
  marked `viaOrbit` (a Hyper Roll win says it went to Orbit). Idle lines read the Orbit lock line instead of
  "PULL THE LEVER". The duel and Hyper Roll stake displays quote **Orbit's** stake (Hyper Roll's LCD reads
  **AUTO**, `OrbitBetLock compact`).
- **Duel lock**: `DuelConsole` now honours `orbitAutoPlaysThisGame` like `GameConsole` (`OrbitBetLock`, *"Orbit
  is auto-playing this game — pause it to bet manually."*).
- **No redirect** into a solo auto-play type (`useOrbitRedirect`): those rounds open every ~10–30 s and would
  keep yanking a player off whatever they play by hand. Shared races keep the redirect.
- Everything stays behind `orbitEnabled`. How-to-play and LP were out of scope (`GuideOrbit.vue` is already
  game-agnostic).

### Rode along: one result standard across modes (QA, `8dfc5bc`)

Distilled into [`monkey-duel.md`](monkey-duel.md), [`hyper-roll.md`](hyper-roll.md) and
[`old-game-modes-ux.md`](old-game-modes-ux.md): the duel races in Sprint's `RaceLanes` (your word only) and
ends on Sprint's `RaceResultFx` card. The VS splash shows the drawn house word. The duel's win/lose sting
now plays. Hyper Roll keeps **no lanes** and uses the loss card without "N away". Every mode's typer
highlights the winning word.

## Deploy / ops notes

- Migration `20261005210302_OrbitSoloAutoBet`: additive, 7 nullable `OrbitEnrollments` columns + index
  `(Enabled, SoloRoundId)`. Deploy Consumers → API → **front in the same window** (the `AllowOrbit` flip is
  live when the new Consumers starts, and an old front would list the two types without following them).
  Prove it with `GET /orbit` listing three `eligibleGameTypes`.
- **Rollback** of Consumers leaves `orbit-solo-autobet-sweep` and scheduled `OrbitSoloRoundDueEvent` jobs
  pointing at missing code: `RecurringJob.RemoveIfExists("orbit-solo-autobet-sweep")` or the Hangfire
  dashboard.
- A player held by an errored payout stays parked until an operator resolves that round (the sweep's Error log).

## Key code touchpoints

**Backend (`monkify-backend/src`)**
- Domain: `Orbit/Entities/OrbitEnrollment.cs` (`Stake`, `BoardMode`, `Multiplier`, `SoloRoundId`,
  `SoloLeaseUntil`, `NextSoloRoundAt`, `WaitingForHouseSince`), `Orbit/Services/OrbitSoloTypes.cs`,
  `OrbitSlotPicker.cs`, `OrbitSoloRoundState.cs`, `Configs/Entities/OrbitSettings.cs`
  (`SoloRoundGapSeconds`, `SoloLeaseSeconds`), `Configs/Entities/GameSettings.cs` (`OrbitAutoRoundEndpoint`).
- Infrastructure: `Services/Orbit/OrbitSoloAutoBetService.cs` (+ `IOrbitSoloAutoBetService`,
  `OrbitAutoRoundPush`), `IOrbitSoloLoopStore` / `OrbitSoloLoopStore` (every enrolment write, `ExecuteUpdate`),
  `IOrbitSoloScheduler` / `HangfireOrbitSoloScheduler`, `Consumers/OrbitSoloRound/*`,
  `Jobs/OrbitSoloAutoBetSweepJob.cs`, `Handlers/Orbit/UpdateOrbitSettings/*`, `Handlers/Orbit/GetOrbit/*`,
  `Handlers/Games/GetHouseEdgeRound/GetHouseEdgeRoundHandler.cs`, guards in `OrbitBetPlacementService`,
  `PlaceOrbitBetConsumer`, `BetPlacedConsumer`, hooks in `RewardGameConsumer`, `RefundInvalidBetConsumer`,
  `OrbitDepositCreditedConsumer`, `OrbitBetSettlementService` (zero-out), `SpaceStrokerDbContext`, migration
  `20261005210302_OrbitSoloAutoBet`.
- Consumers: `Program.cs` (endpoint, `KickOrbitSoloLoops`, `orbit-solo-autobet-sweep`). Config:
  `SpaceStroker.Common/spacestrokersettings.json`.

**Frontend (`monkify-front/src`)** — `types/orbit.ts`, `hooks/useOrbit.ts`, `hooks/useOrbitAutoBet.ts`,
`hooks/useOrbitNotifications.ts`, `hooks/useGameHub.ts`, `hooks/useGame.ts` (`attachRound`,
`orbitAutoPlaysSoloGame`), `hooks/useOrbitRedirect.ts`, `components/orbit/sections/OrbitAutoBetSection.vue`,
`OrbitGameTiles.vue`, `OrbitStakePlate.vue`, `OrbitMultiplierPicker.vue`, `OrbitBetLock.vue`, `OrbitModal.vue`,
`components/game/DuelConsole.vue`, `GameConsole.vue`, `GameTerminal.vue`, `utils/gameArt.ts`.

**Tests**: integration `SpaceStroker.Tests.Integration/OrbitSoloAutoBetFlowTests.cs`
(`HyperRoll_AutoBet_PlaysRoundsBackToBack`, `MonkeyDuel_AutoBet_DuelsTheHouse`,
`SoloAutoBet_HouseRefuses_WaitsWithoutMovingMoney`, `HumanHyperRollBet_DoesNotPullOrbitPlayersIn`,
`SoloAutoBet_RidingStakeElsewhere_OpensNoUnpayableRound`,
`SoloAutoBet_BelowTheCashOutFloor_StakesNothing_AndSendsTheBalanceHome`) and `OrbitSoloLoopStoreSqlTests.cs`
(lease exclusivity/token, scoped clears, waiting, sweep selection against real SQL). Unit:
`OrbitSoloAutoBetServiceTests`, `OrbitSoloAutoBetSweepJobTests`, `OrbitSlotPickerTests`,
`OrbitSoloRoundStateTests`, `OrbitSoloTypesTests`, `UpdateOrbitSettingsTests`, `GetOrbitSoloSetupTests`,
`GetHouseEdgeRoundHandlerTests`, `RewardGameConsumerTests`, `RefundInvalidBetConsumerTests`,
`PlaceOrbitBetConsumerTests`, `BetPlacedHandlerTests`, `OrbitBetPlacementServiceTests`,
`OrbitBetSettlementServiceTests`, `OrbitSettingsBindingTests`, `DefaultGameSeedingTests`.

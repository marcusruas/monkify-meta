# Feature — Orbit bet count & stake steps

> Status: **shipped.** Authoritative spec: [`../specs/new-options-for-orbit.md`](../specs/new-options-for-orbit.md)
> (merged via `feature/new-options-for-orbit` on 2026-10-16). Trello [#208](https://trello.com/c/CvQ6kWlQ).
> Builds on [`orbit.md`](orbit.md) and [`auto-bet-duel-hyper-roll.md`](auto-bet-duel-hyper-roll.md).

## What it does

Orbit auto-bet gets the Stake-style autobet controls: a **number of bets per run** (all three auto-bet games:
Cosmic Sprint, Monkey Duel VS HOUSE, Hyper Roll) and **on win / on loss stake steps** (back to base, or raise
by X% — martingale-style) on the two solo games. Reaching the count **pauses** Orbit with the money still in
custody. In the modal the LET ORBIT PLAY rules become two tabs (mockup **C4c**): **WHEN TO CASH OUT** and
**BETS & STAKE** (**HOW MANY BETS** on Cosmic Sprint).

## Core rules

### The run

- **A run = one START.** The paused → enabled transition (`UpdateOrbitSettingsHandler`) sets
  `RunStartedAt = now`, `RunBetsPlaced = 0`, `RunNextStake = null`. A repeat `enabled: true` while running
  resets nothing. A manual pause ends the run; the next START is a fresh run. No "remaining" survives a pause.
- **Changing the bet limit on a paused enrolment also resets `RunBetsPlaced`.** The counter counts even with
  no limit, so without this a hand-paused run given a limit at or below its count would read `runFinished`
  for a run that never ran.
- **Empty / 0 = no limit** — exactly the pre-#208 behaviour. Bound `1..10_000` (`OrbitRunRules.MaxBetLimit`).
- **Whichever stop fires first wins.** Threshold / floor / stop-loss keep cashing out as before; the count
  only pauses (`Enabled = false`). A cash-out mid-run leaves `Enabled` as it is, so the run continues until
  the count or the money runs out. This is how the card's "number of bets overrides the cash-out
  thresholds" is read.
- Like the cash-out rules, the run watches **auto bets only** (`Bet.OrbitAutoPlaced`). Manual Orbit-paid
  bets never count and never move the stake.

### Counting — never more than N

- ⚠️ **The slot is taken in the stake claim, nowhere else.** `OrbitStakePlacer.ClaimAndDebit` (auto
  placements only) does a conditional `RunBetsPlaced + 1 WHERE Enabled AND (BetLimit IS NULL OR
  RunBetsPlaced < BetLimit)` **last** in the claim transaction; 0 rows ⇒ rollback, `StakeOutcome.RunLimitReached`
  (no debit, no transfer, no bet). That is the hard guarantee that Cosmic Sprint's planner — which signs
  round N+1 before round N settles — can never place an (N+1)th bet. The `Enabled` predicate also refuses a
  placement racing a pause.
- **A stake that never became a bet gives its slot back** (`IOrbitRunService.ReleaseSlot`): a refunded auto
  bet (`RefundInvalidBetConsumer`), a compensated `TransferFailed`, a `RegistrationRefused`. The decrement is
  conditional on `RunBetsPlaced > 0 AND RunStartedAt <= claimedAt`, so a late refund never decrements a
  newer run. `InDoubt` / `AlreadyClaimed` keep their slot (the stake may be live). "Waiting for the house"
  claims nothing.
- Up-front gates so nothing is opened for a full run: the shared planner (`OrbitBetPlacementService.ResolveCandidates`)
  leaves a player at the limit out of its candidates; the solo loop (`OrbitSoloAutoBetService.RunOnce`)
  checks `OrbitRunRules.ReachedLimit` **before** taking the lease or opening a round, and evaluates the run
  end instead. `PlaceOrbitBetConsumer` maps `RunLimitReached` like a pre-claim skip (no re-plan).

### Stake steps (Monkey Duel + Hyper Roll only)

- **Base stake** = `Stake ?? RequiredAmount`; the stored `Stake` is never overwritten. The running next stake
  is `RunNextStake` (null = base).
- **Effective stake** (`OrbitRunStake.Effective`): Cosmic Sprint ⇒ `RequiredAmount`; solo ⇒
  `RunNextStake ?? Stake ?? RequiredAmount`, **clamped to `[RequiredAmount, MaximumStake]`**. Used by the
  solo loop's funds check, house-coverage pre-check and placement, by `EvaluateZeroOut`, and by `GET /orbit`'s
  `nextStake`.
- ⚠️ **Only the stepped stake is clamped.** The solo loop still applies the #200 static refusal to the
  **base** stake: a base outside the type's current bounds (config change) or a null ceiling idles the loop,
  never silently resized. Without the clamp on the stepped stake, a compounded stake above `MaximumStake`
  would trip that refusal and idle the loop.
- **Next stake** (`OrbitRunStake.Next`), only while `StakeStepsEnabled`: **win** (`BetStatus.Rewarded`) or
  **loss** (settled still `Made` — there is no Lost status) picks the on-win / on-loss %. Null or 0 ⇒ back to
  base; else `settledStake × (1 + X/100)`, **truncated to 9 dp** (lamports), then clamped. A **refund leaves
  the stake unchanged**.
- "Back to base" is stored as a **null %** — no mode enum. X is `0..1000` with ≤ 2 dp; no negative %.
- ⚠️ **Steps run before the stop rules.** `RewardGameConsumer` calls `IOrbitRunService.ApplySettledRound`
  **before** `EvaluateOrbitFloors` on both exits (no winners / normal), because zero-out compares the balance
  with the stake and must see the **next** one. Only the player's **latest** auto bet of the run steps
  `RunNextStake`, so a redelivered older settlement cannot overwrite it; the write is guarded by
  `RunStartedAt <= bet.CreatedDate`.
- **Balance short of the next stake** ⇒ the existing NEEDS MONEY path: no bet, no reschedule, money stays,
  **run state kept**. A deposit's `OrbitSoloRoundDueEvent` resumes the same run (counter and next stake
  intact).

### Run end

- `IOrbitRunService.EvaluateRunEnd(playerId, settlingGameId?)` pauses when the enrolment is `Enabled`, has
  reached its limit, and **nothing of this run is in flight**: no open stake claim (a `BetDebit` neither
  linked to a bet nor compensated — the transfer takes seconds before the bet row exists) and no
  non-refunded `OrbitAutoPlaced` bet in an unsettled round. Both checks are **scoped to the current run**
  (`CreatedDate >= RunStartedAt`) — an earlier run's stuck round must not hold every later run at
  "Bet N / N" forever.
- ⚠️ **The settling round is excluded from the in-flight check.** The reward path calls this while the round
  is still `RewardForWinnersInProgress`; without the exclusion a run would never pause on its Nth bet.
- The pause is a conditional `ExecuteUpdate` (`Enabled = false`, `WaitingForHouseSince = null`) **matching
  `RunStartedAt`**, so a stale evaluation never pauses a run the player just restarted. On success it
  releases the Cosmic Sprint `OrbitChoiceClaim` (as a player pause does) and pushes `OrbitRunFinished`.
  `SoloRoundId` is left alone.
- Call sites: `ApplySettledRound` (settlement), `RefundInvalidBetConsumer` (after `ReleaseSlot`), the solo
  loop's limit check, and a **self-heal in `OrbitPlacementPlannerConsumer`** on every `GameOpenedEvent` for
  Enabled enrolments of the opened type at their limit — Cosmic Sprint has no other retry for a missed run
  end. Every failure is logged at Error and **never fails settlement or a refund**.
- `EvaluateZeroOut` returns early for a non-Enabled enrolment, so a just-finished run never gets the
  "out of funds" toast / Telegram.

### Settings, read side, realtime

- `PATCH /orbit/settings` adds `betLimit` / `clearBetLimit` (0 ⇒ clear), `stakeStepsEnabled`,
  `onWinIncreasePercent` / `clearOnWinIncrease`, `onLossIncreasePercent` / `clearOnLossIncrease`
  (null = leave alone; a clear flag wins over a value). Out of range, > 2 dp, or turning steps on / setting a
  % when the **target** type is Cosmic Sprint ⇒ the vague `OrbitRequestInvalid`.
- ⚠️ **Locked while Enabled** (joins game / stake / mode / multiplier): bet limit, CHANGE STAKE, both
  percentages. Re-sending the stored value is not a change; `enabled: false` in the same patch counts first.
  Cash-out rules and the word stay editable. A type change keeps the run settings (steps are ignored on
  Cosmic Sprint).
- `GET /orbit` (`OrbitDto`) adds `betLimit`, `stakeStepsEnabled`, `onWinIncreasePercent`,
  `onLossIncreasePercent`, `runBetsPlaced`, `runFinished` (`!Enabled && ReachedLimit`) and `nextStake`
  (solo enrolled type only). The streamer view returns their defaults. `orbitBetsPlaced` keeps its all-time
  meaning.
- **`OrbitRunFinished`** (`GameSettings.OrbitRunFinishedEndpoint`) — private `orbit/{playerId}` group,
  camelCase `{ gameTypeId, gameTypeName, betsPlaced }`. **Never Telegram** (a pause moves no money).
- An old front against the new backend behaves as before: every new field is optional and absent ⇒ no
  limit, fixed stake.

### Frontend

- `OrbitAutoBetSection`: the AUTO CASHOUT block is two tabs (`OrbitRulesTabs`, `role="tablist"`). **WHEN TO
  CASH OUT** = today's two fields as **BALANCE OVER** / **BALANCE UNDER** (the "auto-bets only" note lives in
  the info tip). **BETS & STAKE** = **PLAY [n] BETS** (empty shows `no limit`); on solo types a **CHANGE
  STAKE** switch and, when on, **AFTER A WIN** / **AFTER A LOSS** rows (`OrbitStakeStepRow`: segmented
  **BACK TO BASE | RAISE BY** + %). Cosmic Sprint's tab is **HOW MANY BETS** with the count only.
- The **hidden tab shows a dot** when it holds an active rule. Default tab WHEN TO CASH OUT; the last tab is
  remembered in `localStorage` `space-stroker.orbit.rules-tab` (try/catch).
- **Read-back sentence** (`utils/orbitRunReadback.ts`) — ⚠️ **shortened in QA to one line** so the pinned
  modal does not scroll. Steps on: *"20 bets, then pause. Loss: doubles · Win: back to 0.010."* (no limit ⇒
  *"No limit. Loss: … · Win: …."*; 100% ⇒ "doubles", other X ⇒ "+X%"). Steps off or Cosmic Sprint:
  *"Orbit plays N bets, then pauses."* / *"Orbit plays until you pause or a cash-out fires."*, plus *"Same
  stake every bet."* on a solo type.
- Status lines: running with a limit ⇒ *"Bet X / N"* (+ *" · next stake …"* with steps on); steps on with no
  limit ⇒ *"Next stake …"*; NEEDS MONEY on a stepped run keeps the line as *"… · needs {stake} to go on"*;
  finished ⇒ *"Finished N/N bets — paused"* (tag PAUSED). Toast on `OrbitRunFinished`: *"Orbit finished N
  bets on {game} and paused. Your money stays in Orbit."*
- ⚠️ **START never reads RESUME when a run is tracked** (a bet limit or stake steps set): a START always
  begins a fresh run, so RESUME would promise the old count and stake. Only plain auto-bet keeps RESUME.
- Inputs save on the setup cadence (600 ms debounce from DOM `@input`, never a `watch`), flush on modal
  close and fold into the START PATCH; client validation mirrors the server. Locked with `setupLocked` and
  the tooltip *"Pause auto-bet to switch games or change its setup."*
- `effectiveStakeFor` returns `nextStake` while enabled on that solo type, so START blockers,
  `needsFirstDeposit` and `isPausedForFunds` judge the **next** stake. ⚠️ **The client auto-park skips a solo
  stake-steps run** (owner option B): parking would send a real pause and the next START would reset the run,
  breaking "a deposit resumes the run". That run stays Enabled in NEEDS MONEY, and its toast reads *"Orbit is
  waiting — your balance no longer covers the next bet. Add funds and it carries on by itself."*

## Deploy / ops notes

- Migration `20261014162202_AddOrbitRunLimits`: additive, 7 `OrbitEnrollments` columns (`BetLimit`,
  `StakeStepsEnabled` default false, `OnWinIncreasePercent` / `OnLossIncreasePercent` numeric(6,2),
  `RunStartedAt`, `RunBetsPlaced` default 0, `RunNextStake` numeric(18,9)). Every existing enrolment behaves
  as before. One non-secret config key `GameSettings.OrbitRunFinishedEndpoint` (committed default
  `OrbitRunFinished`; no deploy-secrets binding).
- Deploy Consumers (migration) → API → **front in the same window**. Prove it with `GET /orbit` returning
  `runBetsPlaced` / `nextStake`. Consumers rollback is safe: the columns are ignored and nothing new is
  scheduled.

## Key code touchpoints

**Backend (`monkify-backend/src`)**
- Domain: `Orbit/Entities/OrbitEnrollment.cs` (run columns), `Orbit/Services/OrbitRunStake.cs`
  (`Effective`, `Next`, clamp), `Orbit/Services/OrbitRunRules.cs` (`ReachedLimit`, bounds),
  `Configs/Entities/GameSettings.cs` (`OrbitRunFinishedEndpoint`).
- Infrastructure: `Services/Orbit/IOrbitRunService.cs` + `OrbitRunService.cs` (`ApplySettledRound`,
  `EvaluateRunEnd`, `ReleaseSlot`, push), `IOrbitRunStore.cs` + `OrbitRunStore.cs` (every run-state
  `ExecuteUpdate`), `OrbitStakePlacer.cs` (claim slot, `StakeOutcome.RunLimitReached`, release on failure),
  `OrbitSoloAutoBetService.cs`, `OrbitBetPlacementService.cs`, `OrbitBetSettlementService.cs` (zero-out),
  `Consumers/RewardGame/RewardGameConsumer.cs`, `Consumers/RefundInvalidBet/RefundInvalidBetConsumer.cs`,
  `Consumers/OrbitBetPlacement/PlaceOrbitBetConsumer.cs`, `Consumers/OrbitPlacementPlanning/OrbitPlacementPlannerConsumer.cs`
  (self-heal), `Handlers/Orbit/UpdateOrbitSettings/*`, `Handlers/Orbit/GetOrbit/*`, `SpaceStrokerDbContext.cs`,
  `DependencyInjection.cs`, migration `20261014162202_AddOrbitRunLimits`.

**Frontend (`monkify-front/src`)** — `types/orbit.ts`, `hooks/useOrbitAutoBet.ts`, `hooks/useOrbit.ts`
(`effectiveStakeFor`, `baseStakeFor`), `hooks/useOrbitNotifications.ts`, `hooks/useGameHub.ts`,
`utils/orbitRunReadback.ts`, `components/orbit/sections/OrbitAutoBetSection.vue`,
`components/orbit/OrbitRulesTabs.vue`, `components/orbit/OrbitStakeStepRow.vue`.

**Tests**: integration `OrbitSoloAutoBetFlowTests.HyperRoll_AutoBet_BetLimitWithLossSteps_PausesAfterN`
(happy path) and `OrbitAutoBetFlowTests.CosmicSprint_AutoBet_BetLimit_NeverPlacesMoreThanN` (complex flow),
`OrbitRunStoreSqlTests` (slot / next-stake / run-end SQL + placer rollback on Postgres). Unit:
`OrbitRunStakeTests`, `OrbitRunRulesTests`, `OrbitRunServiceTests`, `UpdateOrbitSettingsTests`,
`GetOrbitSoloSetupTests`, `OrbitStakePlacerTests`, `OrbitBetPlacementServiceTests`, `PlaceOrbitBetConsumerTests`,
`OrbitSoloAutoBetServiceTests`, `OrbitBetSettlementServiceTests`, `RewardGameConsumerTests`,
`RefundInvalidBetConsumerTests`, `OrbitPlacementPlannerConsumerTests`, `OrbitSettingsBindingTests`.

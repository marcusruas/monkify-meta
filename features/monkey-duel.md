# Feature — Monkey Duel

> Status: **shipped.** Authoritative spec: [`../specs/monkey-duel.md`](../specs/monkey-duel.md)
> (merged via `feature/monkey-duel` on 2026-09-25). Trello
> [#184](https://trello.com/c/O7OXxNM0/184-coinflip-like-game). Design: the round-6 artifact
> <https://claude.ai/artifact/JufhzgZu8FYtm1qfdqSRCU> (⚠️ its 5% copy and payout formula are wrong — see
> Money below).

## What it does

A **1v1 race**: two 6-letter words, one typer stream, the first word typed takes the 2-stake pot less
the ordinary commission. One machine, two CRT tabs — **VS HOUSE** (the default landing: duel the house
on the spot, nobody to wait for) and **OPEN DUELS** (a public board of player-posted rooms you can take,
post to, or cancel). It is the **second** player-vs-house shape beside Hyper Roll, deliberately: Hyper
Roll is a weighted draw against a board, the duel is a genuine race. **Hyper Roll is untouched.**

It ships **live** (`ComingSoon: false`) with an **8-word house pool** — see "Launch gate" below.

## Core rules

### The type

- **One `GameParameters` row** ("Monkey Duel") marked **`IsDuel = true`**. The duel **kind** is per
  round: `Game.DuelKind` = `House (1)` | `OpenRoom (2)`, fixed when the round opens. It serialises as an
  **integer** (there is no `JsonStringEnumConverter`) — the front declares `enum DuelKind` and compares
  against it.
- Letters, `ChoiceRequiredLength = 6`, duplicates accepted, `MinimumNumberOfPlayers = 2`,
  `AllowOrbit = true` since #200 (2026-10-06; was `false`), no `LotteryParameters`.
- ⚠️ **`IsHouseEdge` stays `false`.** That flag selects the treasury × multiplier payout with no
  commission; a duel is pari-mutuel.
- ⚠️ **`PresetChoices` stays empty.** Everywhere in the codebase "has `PresetChoices`" means "preset
  race" (bet validation, `ExclusiveChoice`, one-bet-per-preset-round, the typer's competing set, the SPA
  archetype). The house word pool therefore lives in its **own `DuelWordPool` table** (`Word`, `Active`,
  unique on `(ParametersId, Word)`), so to every shipped path a duel type looks exactly like a free-text
  race.
- Pool words come from config `DefaultGames[].Duel.Words`. `DuelWordRules.Normalize` trims, dedups and
  validates them (length, alphabet, duplicates) in **both** writers — the create-time seeder and the
  startup `DuelWordPoolReconciler`, which inserts missing words and **deactivates** (never deletes)
  removed ones, before `OpenNewGames`. A rejected word is logged `DUEL POOL — skipping…` and never used.
- Stake bounds: `RequiredAmount` = floor (0.002 SOL), `Duel.MaximumStake` = ceiling (0.5 SOL). A null
  `MaximumStake` fails closed (`InvalidAmount`).

### Opening, betting, joining

- **A round is opened by an explicit call**, never at startup and never by rollover:
  `POST /games/{gameTypeId}/duels` `{ kind }`. It commits `ServerSeed` + hash **in the same
  `SaveChanges` as the row**, and for `House` snapshots the active pool (ordinal-sorted, comma-joined)
  into `Game.DuelPoolSnapshot`. A `ComingSoon`/inactive/non-duel type, an empty pool, or (for `House`) an
  uncovered/unconfigured HouseSeed wallet → the vague refusal. No `GameCreatedEvent`/`GameOpenedEvent`.
- **The empty shell gets the board TTL** (`BoardExpiresAt`): `HouseEdgeBoardCleanupJob` now also sweeps
  duel rounds (`IsHouseEdge || DuelKind != null`), but deletes **only unowned, unbet** shells. A room
  with a bet on it **never expires** (decision 15).
- **The stake lives on the round** (`Game.Stake`). The **creator's** bet binds `OwnerPlayerId` +
  `Stake` (within floor/ceiling at SOL precision); the **joiner** must match `Stake` **exactly**
  (`InvalidAmount` + refund otherwise). Free bets keep the shipped rule (amount 0).
- All of it runs in `RegisterDuelBet` under a **duel advisory lock** (distinct key from Hyper Roll's):
  replay check → round still `WaitingBets`, a duel, not full → **bind** (creator) or **claim**
  (joiner: `UPDATE … SET ClaimedByPlayerId WHERE ClaimedByPlayerId IS NULL AND OwnerPlayerId <> @p`) →
  HouseSeed coverage (House rounds, creator's paid bet) → insert → commit → notify.
- **Join race: first write wins.** 0 rows on the claim → `RefundInvalidBet(ChoiceAlreadyTaken)` — the
  existing vague "already taken" plus refund notice. The joiner's stake is **re-checked under the
  lock** (a joiner that read `Stake == null` before the creator's bind committed cannot claim with a
  mismatched stake).
- A creator cannot join their own room (`OwnerPlayerId <> @p`). An **anonymous** bet (free, no wallet)
  is refused up front — it would otherwise bind NULL over NULL and leave the room unclaimable.
- **Both sides always hold different words** via the shipped free-text exclusivity + unique
  `(GameId, ExclusiveChoice)` index — no new rule.
- A player may keep **several rooms posted at once**. `GET /games/{type}?wallet=` re-attaches a
  reloading player to their own round, returning a **matched** duel ahead of rooms still waiting.

### The house side (VS HOUSE)

> ⚠️ **Stale, flagged at the #200 ship (2026-10-06):** the code no longer has `DuelHouseBetConsumer` /
> `DuelHouseBetService` / `DuelCreatorBetPlacedEvent`. The house word is drawn **inside
> `RegisterBetHandler.RegisterDuelBet`** at bet time (`DuelHouseWordGenerator.DrawAvailableWord`) and the
> treasury is the house, per [`../specs/duel-treasury-house.md`](../specs/duel-treasury-house.md). Read the
> bullets below as history until this section is re-distilled from that spec.

- After a **creator** bet commits on a `House` round, `RegisterBetHandler` publishes
  `DuelCreatorBetPlacedEvent`; `DuelHouseBetConsumer` (Consumers, **its own receive endpoint**) runs
  `DuelHouseBetService`, modelled on `HouseSeedBetService`: an **ordinary** paid bet from the
  **HouseSeed** wallet (never the treasury, never `FundedByOrbit`), transfer memo **`duel:{gameId:N}`**
  (load-bearing: stops two rounds' identical transfers sharing a signature), registered through the
  normal `RegisterBetRequest` — which is what claims the round.
- **The house word is drawn, never chosen:** `DuelHouseWordGenerator.DeriveWord(serverSeed, gameId,
  attempt, poolSnapshot)` over the ordinal-sorted snapshot, prefix `duel|{gameId}|{attempt}`, attempts
  0…9, skipping the creator's word.
- **Free path (non-production only):** a 0-amount creator bet gets a 0-amount house bet with no
  transfer and no HouseSeed requirement — how the local stack QAs the house tab.
- ⚠️ **Every give-up path terminates the round and refunds the creator** (conditional UPDATE to
  `NotEnoughPlayersToStart` + `RefundInvalidBetEvent`) — nothing else would ever return that stake
  (no automatic `Made → NeedsRefunding`, no redelivery). The one quiet return is "no bound stake yet".
  A throw after the transfer logs **`HOUSE DUEL ORPHAN PAYMENT`**; the transfer is never re-sent.
- **Coverage** is its own rule, `DuelHouseCoverage` (not `HouseEdgeCoverage` — the treasury takes two
  stakes and pays ≤ 1.98S, so it carries no duel exposure):
  `spendable = balance − MinimumSolBalance − feeReserve × (pending + 1)`;
  `maxStake = clamp(spendable − Σ pending house stakes, 0, MaximumStake)`. A **null balance read is
  not coverable** (fail closed). One function behind both the `/availability` quote and the
  `RegisterDuelBet` backstop; the backstop excludes the round being decided from its own pending set.
- **Coverage clamps the ceiling; it does not close the tab.** Only when the wallet cannot cover the
  **minimum** (or is unconfigured, or unreadable) does VS HOUSE go unavailable and the machine land on
  OPEN DUELS.

### Start, run, reward

- A duel starts **as soon as both bets are in** — no `MinimumWaitPeriodForBets` hold — after
  `DuelTimeUntilGameStarts` (**2 s** since 2026-09-29, was 3 s — the VS splash follows the server's
  `startDate`, so it needs no frontend constant). A one-bet room past the window just stays open.
- No rollover, no startup open, no startup re-plan, **never seeded by the house seed bet** (on a duel
  the seeded bet *is* the second player), never planned by Orbit's shared-round engine, no lottery.
- **Orbit auto-bet (#200, 2026-10-06): VS HOUSE only.** The type is in Orbit's `eligibleGameTypes`. An
  enrolled player's **solo loop** opens a `DuelKind.House` round via `OpenDuelRoom`, bets the enrolment word
  (the same `Choice` as Cosmic Sprint, or seed-derived with auto-pick) at the enrolment `Stake`
  (`RequiredAmount..MaximumStake`) through the unchanged Orbit placer, and goes again after settlement + the
  gap. Orbit never posts or joins OPEN DUELS, and **never takes an `OrbitChoiceClaim` on the duel** (it would
  block that word in everyone's duels). See [`auto-bet-duel-hyper-roll.md`](auto-bet-duel-hyper-roll.md).
  `GET /games/rounds/{roundId}` now serves **House** duel rounds too (OpenRoom stays refused), and reading
  an unbet House shell keeps it alive.
- The typer races the two words over the union of their characters, but on its **own pacing window**
  (`GeneralSettings:Games:DuelTarget*`, 2026-09-29): mean target **4–6 s**, p95 **8 s** ⇒ a 5 s target and
  a 7.2 s cap. A duel usually ends at a natural win BEFORE the cap, so it **averages ~3.5–3.8 s**
  (measured 3.52 s over 10 local rounds, range 0.2–7.3 s) — not 5. ⚠️ The cap/mean ratio (1.44) is kept
  at or above the race's 1.35 on purpose: a tighter cap would push more rounds to the fallback. Expect a
  high **fallback** share (~40–43% in 2-bet free-text rounds, `typer-refactoring.md`).
- **The word carries over.** After a bet lands, `DuelConsole` stores the word per wallet
  (`utils/duelWord.ts`, `localStorage` key `space-stroker.duel-word.<wallet>`) and pre-fills it on the
  next round. **Orbit's word wins** when the player is enrolled with one. It fills only an empty field or
  one it filled itself — never over the player's typing — and a field the player clears stays clear.
  Device-local by design; the backend knows nothing about it.
- **Payout is the unchanged pari-mutuel path:** winner gets `S × (2 − rate)` = **1.98 S** at the live
  2%, one `Credit` `TransactionLog` row. Our edge on a house duel is ~**1% of the pot** with full 50/50
  variance.

### Cancelling a room

- `POST /games/rounds/{roundId}/duel-cancel` `{ wallet, nonce, signature }` — a **wallet proof**
  (`WalletNoncePurpose.DuelCancel = "duel-cancel"`, via `WalletProof.TryConsume`). Conditional UPDATE
  to `NotEnoughPlayersToStart` only while unclaimed and owned by that wallet, then a full refund through
  `RefundInvalidBetEvent`. Every failure (bad proof, unknown round, not the owner, already claimed)
  returns the **same** vague `GameNotValidForBets`. Cancel vs join in flight: whoever writes first wins.

### Board + realtime

- `GET /games/{gameTypeId}/duels?pageNumber&pageSize` (≤ 30) lists `OpenRoom` rounds that are
  `WaitingBets`, unclaimed and **owned** (an unbet shell is not a room), newest first, as `DuelRoomDto`
  (`gameId, word, stake, displayName, wallet, createdDate`). Words are visible — the joiner's advantage
  is accepted (decision 11).
- Realtime on the existing `ActiveGamesHub`: `JoinDuelBoard(gameTypeId)` / `LeaveDuelBoard` → group
  `duels:{gameTypeId}`, event `{gameTypeId}/DuelBoard` carrying `DuelRoomEvent { change:
  opened|taken|cancelled, room, gameId }`. Keyed by **type**, so one join survives every room; one
  connection per tab; no polling. A `House` round never appears or broadcasts.
- Rate-limit rules exist for `post:/games/*/duels`, `get:/games/*/duels`,
  `post:/games/rounds/*/duel-cancel` (none contains `/bets`).

### Contracts

- `GameParametersDto.isDuel`; `commissionPercentage` stays the **real** rate for a duel.
- `ActiveGameDto` + `duelKind`, `stake`, `ownerWallet`, `isFull`.
- `BetAvailabilityDto.maxStake` (null = no clamp); `CheckBetAvailabilityRequestBody.GameId` is optional
  so the SPA can quote the ceiling before a round exists. The duel quote also refuses stakes outside the
  type's floor/ceiling (it would otherwise say yes to a bet then refused as `InvalidAmount`).
- `GameVerificationDto` + `isDuel`, `duelKind`, `duelPoolSnapshot`, `houseWord`. ⚠️ `houseWord` is
  resolved from the round's own **`ClaimedByPlayerId`**, not by matching the current HouseSeed public
  key — so rotating the HouseSeed wallet never blanks a settled round's house word.

### Provably fair

- Offline reproduction: from the revealed `serverSeed`, `gameId`, `duelPoolSnapshot` and the creator's
  word, take attempt 0, 1, … and return the first derived word ≠ the creator's word. KEEP-IN-SYNC across
  `DuelHouseWordGenerator.cs` / `src/utils/provablyFair.ts` / `public/verify.html`.
- ⚠️ The verifier **fails closed** on a House duel: the branch is entered on kind alone, and a missing
  pool snapshot / house word / creator word yields `houseWordOk = false` (reported "not published",
  distinct from "mismatch") — never `VERIFIED`.

### Frontend

- Archetype `'duel'` (`utils/gameArchetype.ts`) / `'monkey-duel'` (`utils/gameArt.ts`), each checked
  **first**. The lobby card is a composition (two Edsons, red/blue split, big VS) drawn by
  `GameTypeCard` — there is no poster file by design. The lobby's live-round snapshot **skips** duel
  types (`GET /games/{type}` has no shared round and would toast a 400).
- The machine: `DuelTabs` (VS HOUSE / OPEN DUELS N) · `DuelCard` (two corners either side of VS, red =
  you, blue = rival) · `DuelBoard` (room grid, `+ OPEN YOUR OWN DUEL` tile first) · `DuelPlate` (pure
  view: word LCD + `?`, − / stake LCD / +, slide switch, UP/DOWN, SORT/OK/CLEAR; since 2026-10-02 it
  mounts on the shared `ConsolePlate`, which owns the plate, the lever and the row grid, so its keys and
  rows match every other game's — see `frontend-ux.md` → "The game console") + `DuelConsole`
  (live logic) · `DuelVsSplash`. **OK arms everything on the board**: JOIN a room, OPEN from the `+`
  tile, CANCEL your own. Lever tag `READY → PULL → PLACING… → LIVE`; the result lever is **END — no
  rematch**.
- **The race and result use Cosmic Sprint's standard** (since 2026-10-06, #200 QA `8dfc5bc`): during the
  round the screen shows `RaceLanes` with **your word only** (the rival's word is never raced on screen),
  the typer stream filling the rest, and on the result the lanes give way to the outcome lines. The result
  moment is Sprint's **`RaceResultFx`** card (`YOU WON!` + the duel payout `S × (2 − rate)` / `NOT THIS
  TIME` + `-stake`), shown to the duel's own player. ⚠️ The old dedicated **DUEL WON / DUEL LOST banner and
  the near-miss dock are gone.** The winning word lights up yellow at the end of the stream
  (`.winning-hit`), as in every mode.
- **The VS splash shows the drawn house word.** `RegisterBetHandler` draws it at bet time, after the screen
  loaded the round, so once the round leaves `WaitingBets` the front re-reads `GET /games/rounds/{id}` once
  (`refreshDuelHouseWord`) and the splash reads `stroke VS nebula` instead of `??????`. A failed read is
  silent and the splash keeps `??????`.
- **Your own bet is re-derived from the round** on every load (`getBets()` after the pull never echoed it
  through the Bets stream), so the **victory / lose sting now plays in duels** (it never did before), and the
  lane appears. A duel round with no stake yet takes its stake from the first bet.
- **While Orbit auto-plays the duel** (#200): `DuelConsole` locks the lever / OK with `OrbitBetLock` and the
  *"Orbit is auto-playing this game — pause it to bet manually."* line (it ignored the lock before), the
  stake LCD and POT quote **Orbit's** stake, the screen attaches to each auto round, and idle lines read the
  Orbit lock line.
- `useDuel.ts` holds tab, board, cursor, sort, the clamped ceiling and the open/join/cancel wallet
  actions; `useGameHub.ts` joins the board channel; `useGame.ts` has the duel round-loading branch and
  `isDuelPlacing`.
- Copy reads the **live** `commissionPercentage` (2%) and the corrected formula `S × (2 − rate)` —
  never the artifact's 5% / `2S × (1 − rate)`.
- **HOW TO PLAY** is a practice duel against the house at **`/game/practice?mode=duel`**
  (`usePracticeDuel.ts`), running on the live `DuelPlate` / `DuelCard` / `DuelVsSplash` with Edson's
  four-beat tutorial.

### Launch gate (Monte Carlo)

- `UnitTests/Domain/ProductionGames/DuelEdgeTests.cs` (run by `run_production_session_tests.bat`,
  excluded from `run_tests.bat`) measures win rates, not pacing: naive-word edge, the best player
  counter-word vs the pool (with Bonferroni correction over every candidate searched), the PVP joiner
  advantage (**~51.9%** measured over 2,400 independent gate rounds; design estimate ~53%), and a pool-size
  sweep at 6 / 12 / 24.
- Finding (`task-18` report, fix round 3): **no counter-word at any swept pool size beats 50% with
  significance** (every Bonferroni p = 1.0), and the three sizes do not differ significantly.
- ⚠️ **Drift:** the pool that shipped on 2026-09-25 is **8 words** — `banana cosmos meteor rocket planet
  launch nebula saturn` — which is neither a swept size nor drawn wholly from the Monte Carlo's master
  list; `banana` has only 3 distinct letters. The owner took the type live with it
  (`e0099b6`). Re-running `DuelEdgeTests` against the live pool is the open follow-up.

## Key code touchpoints

**Backend (`monkify-backend`)**

- Domain: `Games/Entities/GameParameters.cs` (`IsDuel`, `DuelWordPool`), `Games/Entities/DuelWordPool.cs`,
  `Games/Entities/Game.cs` (`DuelKind`, `Stake`, `ClaimedByPlayerId`, `DuelPoolSnapshot`),
  `Games/ValueObjects/DuelKind.cs`, `Games/Services/DuelHouseWordGenerator.cs`,
  `Games/Services/DuelHouseCoverage.cs`, `Games/Services/DuelWordRules.cs`,
  `Bets/Services/BetDomainService.cs` (duel amount branch), `Configs/Entities/GameSettings.cs`
  (`DuelTimeUntilGameStarts`, `SecondsUntilGameStarts(isHouseEdge, isDuel)`, `DuelBoardEndpoint`).
- Infrastructure: `Handlers/Games/OpenDuelRoom`, `GetDuelRooms`, `CancelDuelRoom`,
  `Handlers/Games/RegisterBet/RegisterBetHandler.cs` (`RegisterDuelBet`),
  `Handlers/Games/CheckBetAvailability`, `Services/Duel/DuelHouseBetService.cs`,
  `Services/Games/DuelHouseCoverageService.cs`, `Services/Games/DuelWordPoolReconciler.cs`,
  `Consumers/DuelHouseBet/*`, `Hubs/ActiveGamesHub.cs`, `Jobs/HouseEdgeBoardCleanupJob.cs`,
  migration `20260924215529_MonkeyDuel`.
- Api: `Controllers/GamesController.cs` — `POST {gameTypeId}/duels`, `GET {gameTypeId}/duels`,
  `POST rounds/{roundId}/duel-cancel`. Consumers: `Program.cs` (seed, reconcile, skip in
  `OpenNewGames` + startup re-plan). Config: `spacestrokersettings.json`.

**Frontend (`monkify-front`)** — `src/types/index.ts`, `src/services/mainService.ts`
(`openDuelRoom`, `getDuelRooms`, `cancelDuelRoom`), `src/hooks/useDuel.ts`, `useGameHub.ts`,
`useGame.ts` (`refreshDuelHouseWord`, own-bet/lane re-seed, `attachRound` for Orbit auto duels), `usePracticeDuel.ts`,
`src/components/game/Duel{Tabs,Card,Board,Plate,Console,VsSplash}.vue`, the shared `RaceLanes.vue` /
`RaceResultFx.vue` / `GameTyper.vue` / `GameTerminal.vue`,
`src/utils/gameArchetype.ts`, `src/utils/gameArt.ts`, `src/utils/provablyFair.ts`, `public/verify.html`.

**Tests** — unit: `BetDomainServiceTests`, `DuelHouseWordGeneratorTests` (golden vector),
`DuelHouseCoverageTests`, `DuelWordRulesTests`, `DuelHouseBetServiceTests`, `RegisterBetHandlerTests`,
`CheckBetAvailabilityHandlerTests`, `CancelDuelRoomHandlerTests`, `HouseEdgeBoardCleanupJobTests`,
`RateLimitSettingsTests`, `OrbitSettingsBindingTests` (pins "Monkey Duel" live in the committed file).
Integration: `MonkeyDuelFlowTests` (house + PVP happy paths, HouseSeed rotation regression),
`MonkeyDuelJoinRaceTests`, `MonkeyDuelCancelTests`, `MonkeyDuelCoverageTests`,
`MonkeyDuelConditionalUpdateTests`, `DefaultGameSeedingTests`.

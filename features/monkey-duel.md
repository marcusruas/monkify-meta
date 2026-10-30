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
  into `Game.DuelPoolSnapshot`. A `ComingSoon`/inactive/non-duel type, an empty pool, or (for `House`) the
  `HouseEdgeEnabled` switch off or a **treasury** that cannot cover the minimum stake (the shared Hyper Roll
  check; skipped for a session-verified streamer, see below) → the vague refusal. No
  `GameCreatedEvent`/`GameOpenedEvent`.
- **The empty shell gets the board TTL** (`BoardExpiresAt`): `HouseEdgeBoardCleanupJob` now also sweeps
  duel rounds (`IsHouseEdge || DuelKind != null`), but deletes **only unowned, unbet** shells. A room
  with a bet on it **never expires** (decision 15).
- **The stake lives on the round** (`Game.Stake`). The **creator's** bet binds `OwnerPlayerId` +
  `Stake` (within floor/ceiling at SOL precision); the **joiner** must match `Stake` **exactly**
  (`InvalidAmount` + refund otherwise). Free bets keep the shipped rule (amount 0).
- All of it runs in `RegisterDuelBet` under the **duel advisory lock** (`DuelLockService`, key `MONKDUEL`,
  distinct from Hyper Roll's): replay check → (paid House bet: the shared **treasury lock**, taken before the
  bind — lock order is always duel → treasury) → round still `WaitingBets`, a duel, not full → **bind**
  (creator) or **claim** (joiner: `UPDATE … SET ClaimedByPlayerId WHERE ClaimedByPlayerId IS NULL AND
  OwnerPlayerId <> @p`) → House creator: the house side (below) → insert → commit → notify.
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

> **The treasury is the house** ([`../specs/duel-treasury-house.md`](../specs/duel-treasury-house.md),
> re-distilled at the #201 ship, 2026-10-07). There is **no house bet row**: `DuelHouseBetService`,
> `DuelHouseBetConsumer`, `DuelCreatorBetPlacedEvent`, `DuelHouseCoverage` and `DuelHouseCoverageService`
> are gone, and House duels no longer read the HouseSeed wallet (Cosmic Sprint's house seed bet keeps it).

- **The house side is decided at the creator's bet**, under the duel lock, in
  `RegisterBetHandler.DrawHouseSideUnderLock`: `HouseEdgeEnabled` false → refused with the maintenance
  sentence (paid: refunded; applies to free bets too) → paid bet: the **shared treasury check**
  (`HouseEdgeCoverageService`, the same budget, lock and maintenance switch as Hyper Roll), fail-closed —
  not coverable or no decision → refunded with the maintenance sentence → the **house word** is drawn and
  written to **`Game.HouseWord`** by a conditional UPDATE (`HouseWord IS NULL AND DuelKind = House`), in
  the same transaction as the bind and the bet. No drawable word → `GameNotValidForBets` + refund; the bind
  rolls back, leaving an unbound shell for the cleanup job.
- **The house word is drawn, never chosen:** `DuelHouseWordGenerator.DrawAvailableWord` →
  `DeriveWord(serverSeed, gameId, attempt, poolSnapshot)` over the ordinal-sorted snapshot, prefix
  `duel|{gameId}|{attempt}`, attempts 0…9 (`MaxWordAttempts = 10`), skipping the creator's word.
- **The round is full as soon as the creator's bet lands** (`Game.HasDrawnHouseWord`): `BetPlacedConsumer`
  needs one player, as for Hyper Roll, and the typer races the creator's word against `HouseWord` (added
  with count 0, Hyper Roll's board-word pattern — a house win has `NumberOfWinners = 0`).
- **Exposure:** an unsettled House duel holding a paid bet counts in `HouseEdgeCoverage` at its worst case
  `Stake × (2 − rate)` plus a fee reserve, so a Hyper Roll round and a House duel share one budget. The VS
  HOUSE quote is `min(MaximumStake, (balance − feeReserve − exposure) / (2 − rate))`, truncated to 9 dp
  (`DuelHouseRules.MaxStakeFromHeadroom`), `0` on an unreadable balance. **Coverage clamps the ceiling;
  it does not close the tab** — only a treasury that cannot cover the **minimum** (or is unreadable) makes
  VS HOUSE unavailable and lands the machine on OPEN DUELS.
- **Free path (non-production only):** a 0-amount creator bet skips the money check and the treasury lock
  and is never exposure; the house word is still drawn — how the local stack QAs the house tab.
- **Streamers (#201, [`streamer-wallets.md`](streamer-wallets.md)):** a registered streamer plays VS HOUSE
  with **play money** through `POST /orbit/wagers` — no treasury lock, no coverage pricing, no treasury
  read at settlement; the house word is drawn as usual and a win `S × (2 − rate)` is credited to the play
  ledger. A session-verified streamer's quote is the type's `MaximumStake` and its House room skips the
  coverage refusal (maintenance still applies). OPEN DUELS are refused for a streamer and hidden in its UI.

### Start, run, reward

- A duel starts **as soon as both bets are in** — no `MinimumWaitPeriodForBets` hold — after
  `DuelTimeUntilGameStarts` (**2 s** since 2026-09-29, was 3 s — the VS splash follows the server's
  `startDate`, so it needs no frontend constant). A one-bet room past the window just stays open.
- **A House duel starts on the pull** (2026-10-22, shipped with #209): `DuelHouseTimeUntilGameStarts` = **0**
  (committed; null or negative falls back to `DuelTimeUntilGameStarts`). `GameSettings.SecondsUntilGameStarts`
  takes `isDuelHouse`, which `GameService` passes as `game.DuelKind == House`. House rounds therefore skip the
  VS splash; open PvP rooms keep the 2 s countdown.
- **House duels are one pull, like Hyper Roll** (front, `DuelConsole.vue`): the lever on a finished House
  result closes it and places the next House bet with the remembered word (or, with an empty field, a word
  picked at the pull) and the same stake (tag `PLAY x`, "PULL THE LEVER TO PLAY AGAIN."); only a half-typed
  word makes it just END. PvP results keep END. Since #212 **the console unlocks as soon as the round is
  over**: on the result screen the word, stake and board keys work (touching one waves the result off), and
  a pull starts a new round. It stays locked only while a round or a bet is in flight.
- No rollover, no startup open, no startup re-plan, **never seeded by the house seed bet**
  (`HouseSeedBetService` skips `IsDuel` types), never planned by Orbit's shared-round engine, no lottery.
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
- **Quick play: an empty word field is playable** (owner, 2026-10-29, shipped with #212). On a first visit the
  field is **empty** (never pre-filled) and the screen prompts *"TYPE A 6-LETTER WORD, OR JUST PULL AND WE'LL
  PICK ONE FOR YOU."* Pulling with an empty field picks a themed real word at that moment
  (`utils/duelQuickWord.ts` `pickDuelWord(length, avoid)`, 12 six-letter words, disjoint from the house pool
  and self-non-overlapping; on JOIN it avoids the room's word). The picked word is a normal bet choice, is
  **not remembered**, stays in the field while its round runs and is cleared once the round is over. Only a
  half-typed word holds the lever back. Front-only; the practice duel and the guide (`TYPE A WORD, OR DON'T`)
  say the same.
- **A typed word carries over.** After a bet lands with a word the player typed, `DuelConsole` stores it per
  wallet (`utils/duelWord.ts`, `localStorage` key `space-stroker.duel-word.<wallet>`) and pre-fills it on the
  next round. **Orbit's word wins** when the player is enrolled with one. It fills only an empty field or
  one it filled itself — never over the player's typing — and a field the player clears stays clear.
  Device-local by design; the backend knows nothing about it.
- **The stake carries over too** (#201 decision 12, all players, 2026-10-07). The last **paid** stake
  (wallet, Orbit or streamer play; never a free bet) is stored per wallet and type (`utils/lastStake.ts`,
  key `space-stroker.last-stake.<gameTypeId>.<wallet>`) and restored on entering the duel, after every bet,
  on a late wallet connect and when the house ceiling arrives — only while the stake LCD still holds the
  type minimum or what it filled itself, clamped to `[min, current tab ceiling]`. Paused while Orbit
  auto-plays the duel.
- **Payout:** the winner gets `S × (2 − rate)` = **1.98 S** at the live 2%, one `Credit` `TransactionLog`
  row. OpenRoom: the unchanged pari-mutuel path over the two stakes. House: no dedicated reward function —
  `BetDomainService.SetPotAmount` pads the pot with `Game.Stake` when `HasDrawnHouseWord`, the shared
  commission formula on that 2S pot yields the same figure, and the **treasury** pays it (to Orbit custody
  when `FundedByOrbit`; to the play ledger for a streamer). House word first → nobody is paid. Our edge on a
  house duel is ~**1% of the pot** with full 50/50 variance.

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
- `ActiveGameDto` + `duelKind`, `stake`, `ownerWallet`, `houseWord`, `isFull` (`ClaimedByPlayerId != null
  || HouseWord != null`).
- `BetAvailabilityDto.maxStake` (null = no clamp); `CheckBetAvailabilityRequestBody.GameId` is optional
  so the SPA can quote the ceiling before a round exists. The duel quote also refuses stakes outside the
  type's floor/ceiling (it would otherwise say yes to a bet then refused as `InvalidAmount`). Since #201 the
  body also takes an optional `wallet`, honoured only with a matching `X-Orbit-Session` (streamer quote).
- `GameVerificationDto` + `isDuel`, `duelKind`, `duelPoolSnapshot`, `houseWord` (= `Game.HouseWord`).

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
  you, blue = rival) · `DuelBoard` (room grid, `+ OPEN YOUR OWN DUEL` tile first; since Profile V2 each
  open room card shows its owner's Edson icon as a 34 px tile spanning the card's three lines,
  [`profile-v2.md`](profile-v2.md)) · `DuelPlate` (pure
  view: word LCD + `?`, − / stake LCD / +, slide switch, UP/DOWN, SORT/OK/CLEAR; since 2026-10-02 it
  mounts on the shared `ConsolePlate`, which owns the plate, the lever and the row grid, so its keys and
  rows match every other game's — see `frontend-ux.md` → "The game console") + `DuelConsole`
  (live logic) · `DuelVsSplash`. **OK arms everything on the board**: JOIN a room, OPEN from the `+`
  tile, CANCEL your own. Lever tag `READY → PULL → PLACING… → LIVE`; the result lever is **END — no
  rematch**.
- **The race and result use Cosmic Sprint's standard** (since 2026-10-06, #200 QA `8dfc5bc`; revised with
  #212, 2026-10-29). A House pull runs **reveal beat → race → result**: a short word-vs-word reveal (your
  word against `??????`, the house word typing itself in once drawn; skipped for a round found already
  running, instant under reduced motion), then `RaceLanes` races **both words** (yours as `YOU`, theirs as
  `HOUSE` / `RIVAL`; the house lane eases in once drawn), the typer stream filling the rest, and on the
  result the lanes give way to plain outcome lines: *"EDSON TYPED YOUR WORD: X."* / *"EDSON TYPED THE
  HOUSE'S WORD: X."* + *"NOT YOURS (Y)."*, and when the character cap hit first (`winnerWasTyped` false,
  ~40% of rounds) *"NEITHER WORD CAME UP BEFORE THE LIMIT."* + *"YOU WON THE FAIR DRAW."* / *"THE FAIR DRAW
  WENT TO THE HOUSE."* ⚠️ Never claim a word was typed when the fair draw decided. The result
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
  `Games/Services/DuelHouseRules.cs`, `Games/Services/DuelWordRules.cs`, `Game.HouseWord` /
  `HasDrawnHouseWord`,
  `Bets/Services/BetDomainService.cs` (duel amount branch), `Configs/Entities/GameSettings.cs`
  (`DuelTimeUntilGameStarts`, `SecondsUntilGameStarts(isHouseEdge, isDuel)`, `DuelBoardEndpoint`).
- Infrastructure: `Handlers/Games/OpenDuelRoom`, `GetDuelRooms`, `CancelDuelRoom`,
  `Handlers/Games/RegisterBet/RegisterBetHandler.cs` (`RegisterDuelBet`, `DrawHouseSideUnderLock`),
  `Handlers/Games/CheckBetAvailability`, `Services/Games/DuelLockService.cs`,
  `Services/Games/HouseEdgeCoverageService.cs` (shared exposure), `Services/Games/GameService.cs`
  (`TrySetDuelHouseWord`), `Services/Games/DuelWordPoolReconciler.cs`, `Hubs/ActiveGamesHub.cs`,
  `Jobs/HouseEdgeBoardCleanupJob.cs`, migration `20260924215529_MonkeyDuel`.
- Api: `Controllers/GamesController.cs` — `POST {gameTypeId}/duels`, `GET {gameTypeId}/duels`,
  `POST rounds/{roundId}/duel-cancel`. Consumers: `Program.cs` (seed, reconcile, skip in
  `OpenNewGames` + startup re-plan). Config: `spacestrokersettings.json`.

**Frontend (`monkify-front`)** — `src/types/index.ts`, `src/services/mainService.ts`
(`openDuelRoom`, `getDuelRooms`, `cancelDuelRoom`), `src/hooks/useDuel.ts`, `useGameHub.ts`,
`useGame.ts` (`refreshDuelHouseWord`, own-bet/lane re-seed, `attachRound` for Orbit auto duels), `usePracticeDuel.ts`,
`src/components/game/Duel{Tabs,Card,Board,Plate,Console,VsSplash}.vue`, the shared `RaceLanes.vue` /
`RaceResultFx.vue` / `GameTyper.vue` / `GameTerminal.vue`, `src/utils/duelWord.ts`, `src/utils/lastStake.ts`,
`src/utils/gameArchetype.ts`, `src/utils/gameArt.ts`, `src/utils/provablyFair.ts`, `public/verify.html`.

**Tests** — unit: `BetDomainServiceTests`, `DuelHouseWordGeneratorTests` (golden vector),
`DuelHouseRulesTests`, `DuelLockServiceTests`, `DuelWordRulesTests`, `RegisterBetHandlerTests`,
`CheckBetAvailabilityHandlerTests`, `CancelDuelRoomHandlerTests`, `HouseEdgeBoardCleanupJobTests`,
`RateLimitSettingsTests`, `OrbitSettingsBindingTests` (pins "Monkey Duel" live in the committed file).
Integration: `MonkeyDuelFlowTests` (house win paid by the treasury, house win pays nobody, verification,
drawn-word attempt advance, PVP happy path), `StreamerWalletsFlowTests` (House duel in play money),
`MonkeyDuelJoinRaceTests`, `MonkeyDuelCancelTests`, `MonkeyDuelCoverageTests`,
`MonkeyDuelConditionalUpdateTests`, `DefaultGameSeedingTests`.

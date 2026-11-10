# Feature — Monkey Duel quick flip (VS HOUSE as a coin flip)

> Status: **shipped.** Authoritative spec: [`../specs/duel-quick-flip.md`](../specs/duel-quick-flip.md)
> (§0 = the round-3 design that shipped; merged via `feature/duel-quick-flip` on 2026-11-10). Trello
> [#219](https://trello.com/c/eQ4Dzxj8). Design: Claude Design project `a7ae74b1…` (option 1c / playable 2c,
> then turn `t3` `Quick Flip Round 3.dc.html`). Builds on [`monkey-duel.md`](monkey-duel.md), which stays the
> reference for the type, open rooms, money, coverage and Orbit.

## What it does

**VS HOUSE on Monkey Duel is a SolPump-style coin flip: set a stake, pull the lever, watch the $STROKE coin
flip and land, in under a second.** Nothing to type, nothing to pick, nothing to read. Heads is **PLAYER**
(you), tails is **STROKE** (the house); those are the only two words on the screen. The server settles the
round from the committed seed the instant the bet lands, so the coin is the whole round. Open rooms (OPEN
DUELS) are untouched: still two typed words and Edson's stream.

## Core rules

### Settlement (backend)

- **A House duel with a drawn house word is settled from the seed at construction of the typer, with nothing
  typed.** `SpaceStrokerTyper.IsHouseDuel` (`IsDuel && Game.HasDrawnHouseWord`) runs `SettleFromTheSeed()`
  right after pacing: `MaxCharacters = 0`, no per-character schedule, and `DecideFallbackWinner()` — the same
  fallback draw a capped round reaches, `ProvablyFair.FallbackIndex(serverSeed, prefix, 2)` over the
  **ordinal-sorted** `{creator word, house word}` with the one-bet prefix (`gameId` + the creator's seed). A
  50/50 by construction.
- `GameService.RunGame` therefore never enters its typing loop for a House duel and **emits no terminal
  batch** (not even the "type the winner" tail a capped round gets): nothing was typed, nothing is shown as
  typed. The round goes `GameStarting → InProgress → Ended` within one consumer hop; the Ended frame carries
  `result.firstChoiceTyped` as before.
- **Persisted like any run:** `WinningChoice`, `CharactersTyped = 0`, `CharacterPool` (the two words'
  letters), `MaxCharacters = 0`. **The verifier is unchanged:** `GetGameVerificationHandler` derives
  `WinnerDecidedByFallback = CharactersTyped >= MaxCharacters` (0 ≥ 0 ⇒ true), and `verify.html` /
  `provablyFair.ts` replay zero characters and draw the fallback — plus the independent house-word check.
- **Open rooms race exactly as before** (`DuelTarget*` pacing, natural match or fallback at the cap). A
  legacy House row without a drawn word (never happens for new rounds) also still types: `HasDrawnHouseWord`
  is the single gate, shared with the pot padding and the choice set.
- **Round length** is now only the message hops (BetPlaced → RunGame → Ended broadcast): the typer part is
  0 ms. Everything money-side is untouched — `RegisterDuelBet`, the duel lock, the house word draw, the shared
  treasury coverage, `S × (2 − rate)`, Orbit auto-play, streamer play money.
- **A settled House duel is served by id**: `GET /games/rounds/{roundId}` returns a House round in any ended
  status too (winner and house word included), because the front's post-pull lookup routinely finds the round
  already over. Open rooms stay refused there; the by-wallet active-round lookup is unchanged.
- **Shared exposure** still counts an unsettled House duel, but the window is now milliseconds:
  `MonkeyDuelCoverageTests.SharedExposure…` no longer observes a House duel in flight (the duel side of the
  book is pinned by `HouseEdgeCoverageServiceTests`).

### The bet (frontend)

- **VS HOUSE always bets the literal word `player`** (`DUEL_HOUSE_PLAYER_WORD` in `utils/duelQuickWord.ts`:
  six letters, outside the house pool and the quick list). A type whose word is not six letters falls back to
  `pickDuelWord`. The house word is still drawn and published on the fairness page; **the cabinet never shows
  either word.** Open rooms still type or pick (`pickDuelWord`), and the typed-word carry-over
  (`utils/duelWord.ts`) is board-only.
- **Stake keys ½ · 2× · MAX** on the house face (owner's call): clamped to `[RequiredAmount, ceiling]` on the
  0.001 SOL grid through `useDuel.setStake` → `clampDuelStake`; **MAX = the live coverage quote**
  (`BetAvailabilityDto.maxStake`, can sit below 0.5 SOL on a thin treasury; a streamer's quote is
  `MaximumStake`). Row 2 keeps − / stake LCD / + and `lastStake`. Keys are inert while Orbit locks the
  console or a bet is in flight.
- **Lever tag** `FLIP <stake>` → `PLACING…` → `LIVE` → `FLIP <stake>`; the lever on a House result closes it
  and flips again with the same stake. Board tags unchanged.
- **LAST 6** (device-local, `localStorage` `space-stroker.duel-last.<gameTypeId>.<wallet>`, `anon` without a
  wallet; `utils/duelLastResults.ts`): heads/tails only, newest left, every House round this console saw
  settle (wallet, Orbit auto, streamer, local free bets), capped at 6, survives reload, empty and silent when
  storage is blocked.

### The screen

- **`DuelFlipArena.vue`** (pure view) is the whole VS HOUSE screen between the header and the prompt:
  PLAYER (HEADS · WINS payout) | `DuelCoin` + a 1 s clock bar | STROKE (TAILS · KEEPS stake). Once the coin is
  down the winning side goes hazard yellow and the other dims. `DuelCard` (the old YOU / rival corners) stays
  for the board and other callers.
- **`DuelCoin.vue`** is a real 3D flip: heads `monk-coin-main.png` and tails `monk-coin-tails.png`
  (generated by `scripts/coin-tails.mjs`) back to back in one `preserve-3d` disc; `spinning` rotates it
  (0.28 s per turn), `landing` eases it onto the reported face, `landed` holds it. The spin GIF
  (`monk-coin-spin.gif`) is only used by the PvP `DuelVsSplash`. ⚠️ Never `monk-coin.png` (retired art).
- **Timing** (`src/constants/duelCoin.ts`): spin ≥ **600 ms**, land **300 ms**, clock **1000 ms** — a flip
  reads done in ~0.9 s on a prompt server (owner: "at most 1 second"). **The face is the server's
  verdict** (`duelIWon` = `winnerChoice === my bet`), never a timer: the coin lands only once the Ended frame
  is in **and** the minimum spin has passed; a late frame keeps it in the air.
- A House round is "settled on screen" when `status > InProgress && winnerChoice` (no `finishedTyping`: there
  is no stream). The verdict line and `RaceResultFx` wait for `landed`.
- **Copy, one line per state** (owner): idle `> PULL THE LEVER: FLIP <stake>, WIN <payout>`; placing
  `> THE ROUND SEED DECIDES THE FLIP.`; header `> FLIP · PLAYER vs STROKE` + POT; result
  `HEADS. PLAYER WINS. +<payout>` / `TAILS. STROKE TAKES IT. -<stake>` then `> PULL THE LEVER TO FLIP AGAIN.`
  **No proof strip, no "Edson is typing", no outcome sentences, no pot/cut line, no server seed on the
  cabinet** (the fairness page has it). The result card (`RaceResultFx`) shows no bet word on a House round.
- **Audio:** the flip plays a short bell "plim" (`assets/audios/coin-flip.wav`, synthesized, ~0.3 s) as the
  coin leaves the hand; the win / loss sting plays **when the coin lands**, not on the Ended frame — `useGame`
  stays quiet for House duels and `GameTerminal` fires it on `landed`, bettors only, once per round, never on
  a reload onto an old result, active tab only. No new audio on open rooms.
- **The post-pull attach** (`DuelConsole` → `useGame.getBets`): the by-wallet active-round lookup comes back
  empty once the round is already settled, so `getBets` falls back to the round the pull opened
  (`useDuel.lastHouseRoundId` → `GET /games/rounds/{id}`), **never wipes an already-settled House round** on
  an empty read, and seeds `winnerChoice` off the round when the Ended frame was missed. A round attached
  already settled **still plays its flip** (it is the round this console pulled); only a reload onto an old
  result lands at once.
- **The mode intro** glides PLAYER in from the left and STROKE from the right on the first round of a
  session; it no longer touches the coin (a centre slam hid most of the first flip).
- Reduced motion: a neutral `?` disc while in the air, an instant face, no clock animation; `light-graphics`
  drops the glows.

### Practice, guide, LP

- **Practice duel** (`/game/practice?mode=duel`): same arena and plate, timings `{0, 0, 600, 300}` (no VS
  splash: bubble 2 holds the clock over the idle coin), the coin lands at `Ended` and the result shows at
  `RewardForWinnersCompleted`, house label `STROKE`, four reworded bubbles, the flip plim, always heads.
- **Guide** (`GuideCoinflip.vue`): intro "One coin or two words…", step 2 **NOTHING TO TYPE** with the coin
  art re-cut from the live cabinet (`coinflip-coin.png`), steps 4–5 and the tip name PLAYER / STROKE and the
  round seed; the fairness page replays the draw.
- **LP** blurb: *"Flip the $STROKE coin against the house on the spot: heads is PLAYER, tails is STROKE,
  settled from the round seed in two seconds. Or take another player's open duel, where the first word Edson
  types takes both stakes."* Terms untouched (payout unchanged).

## Key code touchpoints

**Backend (`monkify-backend`)** — `Domain/Games/Services/SpaceStrokerTyper.cs` (`IsHouseDuel`,
`SettleFromTheSeed`), `Infrastructure/Services/Games/GameService.cs` (`RunGame`: no tail batch for a House
duel), `Infrastructure/Handlers/Games/GetHouseEdgeRound/GetHouseEdgeRoundHandler.cs` (settled House duel
servable). Tests: `SpaceStrokerTyperTests` (`HouseDuel_SettlesFromTheSeedAtConstruction_NothingIsTyped`,
`HouseDuel_IsAFairCoin_AcrossSeeds`, `OpenRoomDuel_StillRacesItsStream`, adjusted prefix / pacing / no-word
tests), `GetHouseEdgeRoundHandlerTests.SettledHouseDuelRound_IsReturned_WithItsWinner`, integration
`MonkeyDuelFlowTests.HouseDuel_SettlesFromTheSeedAtThePull_NothingIsTyped` (the mandatory happy path),
`MonkeyDuelCoverageTests` (shared-exposure step rewritten).

**Frontend (`monkify-front`)** — `src/constants/duelCoin.ts`, `src/components/game/DuelFlipArena.vue`,
`DuelCoin.vue`, `GameTerminal.vue` (House idle / race / result blocks, coin phases, sting on landed),
`DuelConsole.vue` (fixed word, quick-stake keys, LAST 6, post-pull attach by id), `DuelPlate.vue` (tab-aware
wells), `DuelCard.vue` (`#centre` slot), `src/hooks/useDuel.ts` (`lastHouseRoundId`), `src/hooks/useGame.ts`
(`loadDuelRound` fallback, settled-round guard, winner seed), `src/utils/duelQuickWord.ts`
(`DUEL_HOUSE_PLAYER_WORD`), `src/utils/duelLastResults.ts`, `src/assets/game/duel/monk-coin-tails.png` +
`scripts/coin-tails.mjs`, `src/assets/game/duel/label-last.png`, `src/assets/audios/coin-flip.wav`,
practice (`usePracticeDuel.ts`, `constants/practiceGame.ts`, `PracticeDuelScreen.vue`,
`PracticeDuelConsole.vue`), `components/guide/sections/GuideCoinflip.vue` + `assets/guide/coinflip-*.png`,
`scripts/smoke-duel-coin.mjs` (Playwright smoke: faces match the verdict, flip ≤ 1.4 s, no typed copy).

**LP (`monkify-lp`)** — `src/components/GameModes.vue` (duel blurb).

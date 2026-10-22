# Feature — Hyper Roll Word Lines (5×3 grid, 10 paylines, 98% RTP)

> Status: **shipped.** Authoritative spec: [`../specs/hyper-roll-word-lines.md`](../specs/hyper-roll-word-lines.md)
> (merged via `feature/hyper-roll-word-lines` on 2026-10-22, all four repos). Trello
> [#209](https://trello.com/c/0BtXXjVp). It **reworks Hyper Roll in place**: same game type and id, so
> lobby, history, stats, chat and Orbit carry over. The shared house-edge machinery it still runs on
> (treasury bankroll, coverage lock, maintenance switch, stake bounds, solo rounds, Orbit-paid and streamer
> bets) is in [`hyper-roll.md`](hyper-roll.md). The word-board rules there and the AVERAGE / SUPER / HYPER
> modes in [`hyper-roll-modes.md`](hyper-roll-modes.md) now only apply to **legacy rounds**.

## What it does

The player sets a stake and pulls the lever. There is no word to pick, no mode and no reroll. One
provably-fair weighted draw picks **one result row** from a published 13-row table, and the server builds a
**5×3 letter grid** that shows exactly that result on up to **10 paylines**. **Every spin pays**, from 0.3×
(the "bad word") to **10×** (full board). The table pays back **98%**.

## Core rules

### The outcome table (the published paytable)

| Key | Result | Pay | Weight | Chance |
|---|---|---|---|---|
| `bad` | No word on any line or reel; the middle row shows `#@%!&` | 0.3× | 436,574 | 43.66% |
| `double` | Two 5-letter words on two straight rows (replaces the sum) | 6× | 4,633 | 1 in 216 |
| `down` | 3-letter word top→bottom on one reel | 0.5× | 70,000 | 7.00% |
| `echo` | The same 4-letter word on 2 lines, both doubled | 5.6× | 9,266 | 0.93% |
| `edson` | EDSON on a line (replaces the sum) | 8× | 2,317 | 1 in 432 |
| `five` | One 5-letter word | 2.5× | 65,635 | 6.56% |
| `four` | One 4-letter word | 1.4× | 100,382 | 10.04% |
| `full` | Three 5-letter words, one per row (replaces the sum) | 10× | 1,544 | 1 in 648 |
| `spacefive` | 5-letter space word (×2) | 5× | 16,988 | 1.70% |
| `spacefour` | 4-letter space word (×2) | 2.8× | 27,026 | 2.70% |
| `three` | One 3-letter word | 0.5× | 200,000 | 20.00% |
| `twolines` | A 5-letter word + a 3-letter word on two lines | 3× | 27,026 | 2.70% |
| `wild` | 4-letter word with one `?` | 1.4× | 38,609 | 3.86% |

- Σ weights = 1,000,000; Σ weight × pay = **98.0002%**. 29.3% of spins pay **above** the stake.
  The table is **locked**: changing a pay or a weight changes the RTP and goes back to the owner.
- Config: `DefaultGames["Hyper Roll"].HouseEdge.Outcomes` (`{ Key, Pay, Weight }`) → stored on
  `GameParameters.OutcomeTable` as `key:pay:weight;…` by `HouseEdgeReconciler` at Consumers startup
  (self-heals, like the old ladder). `HouseEdgeRules.ValidateOutcomeTable` fails closed: an unknown,
  duplicated or missing key, a pay outside 0.1–10, a weight ≤ 0, Σ ≠ 1,000,000, RTP ≥ 100 or more than
  0.25 pt off `RtpPercent` (98) ⇒ the type **opens no round** (logged at Error). ⚠️ A mis-weighted table is
  a money bug.
- **Worst single spin = stake × 10** (1 SOL at the 0.1 SOL max). Coverage, `/availability` and the Orbit
  loop all price a Word Lines bet at 10×.
- House margin ≈ 2% minus payout fees (~0.2 pt at the 0.002 SOL minimum) minus partner share. ⚠️ A partner
  deal at ≥ 90% `SharePercentage` makes referred Hyper Roll traffic lose money (ops note, no code guard).

### A round

- **Open-then-bet stays.** `POST /games/{typeId}/boards` opens a solo `WaitingBets` round with
  `Game.BoardMode = "LINES"` (`HouseEdgeRules.WordLinesMode`), commits the server-seed hash and attaches the
  **13 outcome rows as the round's `GameBoardChoices`** (`Choice = key`, `Multiplier = pay`, `Weight`). The
  `mode` argument is ignored. **Reroll is gone**: the route, handler and `RerollSoloBoard` are deleted, so a
  stale client gets a 404.
- **The bet's choice is always `spin`** (`HouseEdgeRules.SpinChoice`). `/bets`, `/availability` and
  `/orbit/wagers` accept a missing or any choice for a `LINES` round and store `spin`. Non-house-edge types
  still require a choice.
- **The draw** is the unchanged `ProvablyFair.WeightedFallbackIndex` over the 13 rows sorted ordinally by
  key. The typer emits **no** final key batch for `LINES` rounds (the key is an internal id), uses the full
  A–Z alphabet as its character pool, and sets `NumberOfWinners` = the bet count.
- **A round types for ~2 s** (`GeneralSettings:Games:HouseEdgeTarget*` = 2 / 2 / 2 s, committed + code
  defaults). Judge it by real round times after a deploy; a CPU-starved Consumers stretches it silently.
- **The grid is built at Ended** (`GameService.UpdateGameStatusCore`) by `WordLinesGrid.Build` and persisted
  with the result in one save: `Game.WordLinesGrid` (15 chars, row-major, top row first, A–Z and `?`) and
  `Game.WordLinesHits` (JSON `[{ kind: line|down, index, word, shown, space }]`). Both are null on legacy
  rounds. The grid is presentation, like the old board draw: only the row is provably fair.
- **Every bet of a `LINES` round is paid.** `RunGameConsumer` marks every non-voided bet `NeedsRewarding`,
  and `CalculateHouseEdgeReward` takes the multiplier from the **drawn row**, not from the bet's choice:
  reward = `stake × pay − AlreadyCreditedAmount`, no commission. `RewardGameConsumer`'s running-balance
  treasury check is unchanged. Wallet bets are paid on-chain (even 0.3×), Orbit-paid bets get a ledger
  `CreditWin`, streamer bets a play-ledger credit.
- ⚠️ **Small native payouts:** a 0.3× bad word at the 0.002 SOL minimum is 600,000 lamports, under the
  890,880-lamport rent-exempt minimum. The native-SOL transfer guard now refuses a sub-minimum transfer only
  when the **destination's balance + amount** would still be under the minimum, or when that balance read
  fails (fail closed, logged Error). See [`sol-settlement.md`](sol-settlement.md).
- **"Won" means payout > stake** for `LINES` rounds: history, player/global/per-type stats, the front's
  `myWon`, Orbit's win notification and Orbit's #208 win/loss step rule. `TotalPaidOut` still counts every
  credit. A 0.3×/0.5× result is never called a win.

### The grid generator (`SpaceStroker.Domain/Games/Services/WordLinesGrid.cs`)

- Pure, `Random` injected. 10 paylines read from reel 1 left to right:
  `1: middle row, 2: top, 3: bottom, 4: V, 5: inverted V, 6–7: diagonals with a flat, 8–10: zigzags`
  (coordinates in the spec §6.6 and `utils/wordLines.ts`). A line word is the first 3–5 cells of a line,
  the longest valid word on a line wins it, a down word is a reel read top→bottom, `?` matches any letter.
- Per key it places the row's words, fills every other cell from `FillerLetters` (`QXZJVK`, letters that
  cannot spell words), then **scans every line and reel**: the hits must equal exactly the intended ones,
  and no playable, space, blocked word or `edson` may appear anywhere else. On failure it retries up to
  **50** times, then uses a hard-coded **deterministic fallback grid** for that key (logged at Warning). The
  row and the payout never change.
- **Repetition guard:** words that appeared in the owner's hits in their last **20** `LINES` rounds
  (`RepeatGuardRounds`, Orbit auto rounds included, one query) are excluded. If that would empty a length
  tier, the guard is ignored for that pick.
- **Word pool:** `HouseEdge.Words` → `PresetChoices`, with `Multiplier` reused as the **space flag** (2 = space
  word, pays ×2; 1 = normal). Shipped: 193 three-letter, 196 four-letter and 180 five-letter words, plus 10 +
  10 space words (the old 4/5-letter Hyper Roll words: mars luna star moon nova warp void apes halo beam /
  pluto venus orbit comet solar titan probe rover flare atlas). The old 6–10-letter words are
  `Active = false` (kept for old rounds). `HouseEdge.BlockedWords` (64 entries, config only, never synced to
  the DB) can never show on any line or reel. `ValidateWordLinesPool` fails closed on a word outside 3–5
  a–z letters, a bad space flag, fewer than 50 normal words per tier or 5 space words per length, a
  playable ∩ blocked overlap, or a word containing `edson`.

### Cutover (legacy rounds)

- At Consumers startup the reconciler writes the outcome table, deletes Hyper Roll's `GameBoardModes` rows,
  deactivates the 6–10-letter words and **sweeps unbet, unowned, non-`LINES` `WaitingBets` boards** (under
  the coverage advisory lock, count logged at Warning).
- A legacy round that already has a bet settles under the **old word rules** (`BoardMode` AVERAGE / SUPER /
  HYPER / null). History and verify render old rounds as word boards and new rounds as grids.
- Legacy columns and tables (`GameBoardModes`, `GameParameters.BoardLadder`, enrollment `BoardMode` /
  `Multiplier`, `HouseEdgeBoard`) stay for now (expand–migrate–contract; dropping them is a follow-up card).
  `OrbitSlotPicker` and `OrbitMultiplierPicker.vue` are deleted.
- ⚠️ **Deploy window:** once the new Consumers reconcile, the still-running old API cannot open a Hyper Roll
  board until the new API revision is live (no money path is affected). Deploy Consumers → API → front back
  to back. `ActiveParameters:v3` → **`:v4`** (the DTO gained `Outcomes`).

### Orbit

- Auto-bet, manual Orbit-paid bets and streamer play bets all play Word Lines **at the stake only**. The
  solo loop opens `OpenSoloBoard(parameters, null)`, bets `spin` and prices coverage at the table's max pay
  (10×). An enrollment's old `BoardMode` / `Multiplier` are ignored (no backfill); the settings handler
  accepts and ignores them, and `GetOrbit` returns no modes for Hyper Roll. The setup UI shows only the stake
  plate. See [`auto-bet-duel-hyper-roll.md`](auto-bet-duel-hyper-roll.md).

### API surface

- `GameParametersDto` (Hyper Roll): `RtpPercent` 98, **`Outcomes: [{ key, pay, weight }]`** in draw order,
  `Modes` and `PresetChoices` empty.
- `ActiveGameDto` / `GET /games/rounds/{id}`: the 13 rows as `BoardChoices`, plus `WordLinesGrid` and
  `WordLinesHits` once the round has ended (a reload restores a settled spin).
- `GameResult` on Ended: `OutcomeKey`, `OutcomePay`, `Grid`, `Hits` (null for legacy rounds).
- `GET /games/{id}/verification`: `BoardMode = LINES`, the rows with weights, the grid and hits.
- History rows: `OutcomeKey`, `OutcomePay` (copied by `ClonePublic`).

### Frontend

- **Game screen:** `WordLinesGrid.vue` replaces the word table. While the round runs the 5 reels scroll
  letters with blur and CRT flicker; when the grid arrives they land left → right (70 ms apart, 100 ms
  bounce). Then the paid words are **traced** square by square along their paylines (`TRACE_PAUSE` 233 ms,
  `TRACE_LETTER` 80 ms per square, `TRACE_WORD_GAP` 200 ms, `TRACE_END` 233 ms — 50% faster than the first
  cut, owner 2026-10-22) with SVG polylines and line-number badges. On `bad` the middle row flips to a red
  `#@%!&`. The terminal prints one line per hit (`↓ REEL n` for a down word) and the result line.
- **Console:** `WordLinesPlate.vue` (engraved **PAYS** LCD — idle `-.-×`, scrambling while spinning, the paid
  total coloured by tier afterwards — plus 10 payline lamps) replaces the 8 word keys. `HyperRollModeDial`
  shows `10 LN` / `10×` readouts with HELP and MAX, no mode keys. The stake keys are **HALF / ×2 / MAX**
  around the LCD (bounds 0.002–0.1 SOL, 0.01 grid, last-stake memory unchanged). **One pull**: the lever
  on a finished spin opens the next round and bets it at the same stake (`SPIN <stake>`).
- **Result banner** (`HyperRollWinFx.vue`, `variant` prop; tiers by pay `< 1` gray, `< 2` green, `< 5` blue,
  `< 8` purple, else gold):
  - `win` (pay > 1): "YOU WON!", count-up, coins; screen shake from 5×; max-win sting and gold ring from 8×.
    Held 2 s.
  - `under` (pay < 1, a word): "ALMOST THERE!", the amount back. `bad`: "SO CLOSE!" + Edson's curse (garbled blips, no bleep).
    No coins, no shake, no count-up; held 1.3 s. `RaceResultFx` is not used for `LINES` rounds.
- **Sounds** (`utils/wordLinesSfx.ts`, `src/assets/audios/word-lines/`, base volume 0.1): spin loop, reel
  stops rotating through 3 samples, a trace-tick ladder (8 steps, clamped), reveal boom, tiered win stings,
  ALMOST THERE, and `edson-swore.wav` (also the loss sting of every other mode, see [`frontend-ux.md`](frontend-ux.md)). Hyper Roll plays no phone beep on the bet: the lever-pull sound marks it.
- **Practice** (`/game/practice?mode=hyper-roll`, `usePracticeHyperRoll.ts`, `practiceGame.ts`): front-only,
  draws the outcome with `Math.random()` over the same 13 weights (it no longer always wins) and plays a
  stored grid per key. `PRACTICE_HYPER_ROLL_TIMINGS.runMs` = 1600 (grid at ~1.2 s, Ended at 1.6 s).
- **Verify / fair panel:** `provablyFair.ts` + `verify.html` (algorithm unchanged, V4 known-answer vector
  over the 13 rows in comments) show "RESULT: <label> (<pay>×)" and "WEIGHTED DRAW - WORD LINES", and
  `WordLinesStaticGrid.vue` renders the stored grid. History shows the outcome label + pay and `WON x` /
  `BACK x`. The STATS badge reads `98% RTP`. Orbit tile subtitle: "5×3 grid · 10 lines".
- **Guide / lobby / LP:** `HOW HYPER ROLL WORKS` is rewritten (set stake, pull, read the grid, special words,
  every spin pays) with `GuideWordLinesArt.vue`; the lobby tag reads "5×3 GRID · 10 LINES" and `0.3X–10X`. The
  LP mode card, index copy and terms clause (13 results, 0.3×–10×, 98% RTP, published weights) are in
  [`lp.md`](lp.md).

## Key code touchpoints

**Backend (`monkify-backend/src`)**
- `SpaceStroker.Domain/Games/Services/WordLinesGrid.cs` (generator, `Scan`, fallbacks, hit model),
  `HouseEdgeRules.cs` (constants, `Parse/FormatOutcomeTable`, `ValidateOutcomeTable`,
  `ValidateWordLinesPool`), `ValueObjects/HouseEdgeOutcome.cs`, `SpaceStrokerTyper.cs`.
- Entities: `GameParameters.OutcomeTable`, `Game.WordLinesGrid` / `WordLinesHits`; migration
  `20261015063234_HyperRollWordLines`.
- `SpaceStroker.Infrastructure/Services/Games/GameService.cs` (open with 13 rows, Ended arm grid + guard
  query), `HouseEdgeReconciler.cs` (table, modes delete, cutover sweep), `BetPreChecks.cs`.
- `Domain/Bets/Services/BetDomainService.cs` (`spin`, reward from the drawn row),
  `Consumers/RunGame/RunGameConsumer.cs`, `Consumers/RewardGame/RewardGameConsumer.cs`,
  `Services/Solana/SolanaService.cs` (sub-rent-exempt native guard).
- `Handlers/Games/RegisterBet`, `CheckBetAvailability`, `GetGameVerification`; `Handlers/Orbit/PlaceOrbitWager`,
  `UpdateOrbitSettings`, `GetOrbit`; `Consumers/OrbitBetPlacement/PlaceManualOrbitBetConsumer.cs`;
  `Services/Orbit/OrbitSoloAutoBetService.cs`, `OrbitBetSettlementService.cs`, `OrbitRunService.cs`.
- History/stats: `GameHistoryService.cs`, `PlayerStatsService.cs`, `GlobalStatsService.cs`, `GameStatsService.cs`.
- Config: `SpaceStroker.Common/spacestrokersettings.json` → `DefaultGames[Hyper Roll].HouseEdge`
  (`Outcomes`, `Words`, `BlockedWords`, `FillerLetters`, `RepeatGuardRounds`).
- Tests: `WordLinesGridTests`, `WordLinesOddsTests`, `HouseEdgeRulesTests`, `ProvablyFairTests` (V4),
  `SolanaServiceTests`, plus the reworked handler/consumer/Orbit tests. Integration:
  `HyperRollWordLinesFlowTests` (happy path per steered row incl. `bad` = 0.3×, and the cutover),
  `HouseEdgeWordLinesReconcileTests`, and the retargeted `HyperRoll*` / Orbit suites.

**Frontend (`monkify-front/src`)**
- `components/game/WordLinesGrid.vue`, `WordLinesPlate.vue`, `WordLinesStaticGrid.vue`, `GameConsole.vue`,
  `GameTerminal.vue`, `HyperRollWinFx.vue`, `HyperRollModeDial.vue`, `GameFairPanel.vue`.
- `utils/wordLines.ts` (geometry, labels, tiers, banner kind), `utils/wordLinesSfx.ts`,
  `hooks/useWordLinesReel.ts`, `hooks/useGame.ts`, `hooks/usePracticeHyperRoll.ts`, `constants/practiceGame.ts`.
- `utils/provablyFair.ts` + `public/verify.html` (KEEP-IN-SYNC), `pages/GameHistoryPage.vue`,
  `components/orbit/sections/OrbitAutoBetSection.vue`, `hooks/useOrbitAutoBet.ts`.
- `components/guide/sections/GuideHyperRoll.vue`, `GuideWordLinesArt.vue`, `GuideModeCards.vue`,
  `components/game-selection/GameTypeCard.vue`.

**Landing page (`monkify-lp`)**: `src/components/GameModes.vue`, `src/pages/index.astro`, `src/pages/terms.astro`.

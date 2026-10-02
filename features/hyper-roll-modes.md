# Feature — Hyper Roll game modes (AVERAGE / SUPER / HYPER)

> Status: **shipped.** Authoritative spec: [`../specs/hyper-roll-modes.md`](../specs/hyper-roll-modes.md)
> (merged via `feature/hyper-roll-modes` on 2026-09-24). Extends [`hyper-roll.md`](hyper-roll.md).
> Trello [#193](https://trello.com/c/Uyo3eq6g).

## What it does

Hyper Roll offers **three selectable boards** on one game type. **AVERAGE** has 5 words, **SUPER** has 6, and
**HYPER** has 8. HYPER is exactly the board that shipped before modes. A board with more words is harder to
hit and pays bigger multipliers. The player picks a mode with three round **mode keys** on the console's
stake plate. Picking a mode redraws the board on the spot, and the mode is remembered per browser.

## Core rules

### The three boards (odds locked by the owner, 2026-09-22)

A mode's ladder is a list of `length:multiplier:weight` slots. Each ladder's weights sum to 1,000,000, and
every slot pays back its mode's RTP flat.

| Mode | Words | Lengths | Multipliers | RTP | Win | Top prize at 0.1 SOL |
|---|---|---|---|---|---|---|
| **AVERAGE** (default) | 5 | 4–8 | 3 / 4 / 5 / 6 / 7× | **91.50%** | 1 in 5.0 | 0.70 SOL |
| **SUPER** | 6 | 5–9 | 3 / 4 / 6 / 8 / 9 / 9× | **91.14%** | 1 in 6.0 | 0.90 SOL |
| **HYPER** | 8 | 6–10 | 4 / 6 / 8 / 8 / 9 / 9 / 10 / 10× | **91.84%** | 1 in 8.0 | 1.00 SOL |

- ⚠️ **A word's multiplier depends on the mode.** A 6-letter word pays 5× on AVERAGE but 4× on SUPER and
  HYPER, and an 8-letter word pays 7× on AVERAGE but 8× on the other two. The slot carries the word
  **length**, so `HouseEdgeBoard.Draw` fills each slot from the active pool **by length**. The multiplier
  comes from the slot, never from the word.
- Changing any multiplier, slot count or length tier changes a mode's RTP, so the owner must approve it.
- **No mode loses money for the house.** Every slot pays back under 100%. A sharp player gets the best odds on
  HYPER, so the effective house edge is still **8.16%**.
- **All modes share one stake range:** min `RequiredAmount` 0.002 SOL and max `MaximumStake` 0.1 SOL. Only the
  top prize differs between modes.

### Data and config

- **`GameBoardModes`** has one row per (type, mode): `Name`, `RtpPercent`, `Ladder`, and `Position`, which is
  the display order. The lowest `Position` (AVERAGE) is the default mode. A unique index covers
  `(ParametersId, Name)`.
- The modes are seeded from `DefaultGames[Hyper Roll].HouseEdge.Modes[]` in `spacestrokersettings.json`.
  `HouseEdgeReconciler` heals them at Consumers startup like the word pool: it inserts missing modes,
  updates changed RTP/ladder/position values, and **deletes** modes that are no longer in config. It still
  never touches the stake bounds.
- **`Game.BoardMode`** (`string?`) stores the name of the mode the round was drawn from. It is **only a label**
  used by history, stats and the fairness panel. The round's own `GameBoardChoices` carry every multiplier and
  weight that the draw and verification read. Rounds that existed before modes were backfilled to `HYPER`,
  and non-house-edge rounds keep it null.
- **Legacy HYPER mirror (expand–migrate–contract):** the reconciler still writes
  `GameParameters.BoardLadder`/`RtpPercent` and `PresetChoice.Multiplier`, but the new code reads none of
  them. They exist so that an old API revision still works during a deploy. Removing them is a follow-up.
- **Word pool: 74 words, lengths 4–10** (`WordMinLength` changed from 6 to 4). The new 4-letter tier is mars,
  luna, star, moon, nova, warp, void, apes, halo, beam. The new 5-letter tier is pluto, venus, orbit, comet,
  solar, titan, probe, rover, flare, atlas.
- **Validation fails closed per mode.** `HouseEdgeRules.ValidateMode` checks each mode's ladder shape,
  multipliers, weights, RTP tolerance against the **mode's** `RtpPercent`, and that the pool has enough active
  words of each length the mode needs. A type is valid only when every mode passes. An invalid mode never
  opens a board and is logged at Error. Consumers never throws on this at startup.

### API

- `POST /games/{gameTypeId}/boards` and `POST /games/rounds/{roundId}/reroll` accept an optional body
  `{ "mode": "SUPER" }`. The match is case-insensitive.
- **A missing or unknown mode falls back to the default mode (AVERAGE)** and is never refused. This way a
  stale `localStorage` value cannot turn into a refused paid bet. All other refusals keep their existing
  vague messages.
- **A reroll with a mode** keeps the round id, replaces the board rows, **rotates the server seed**, and writes
  `BoardMode` with an explicit `IsModified`, because the DbContext is NoTracking. The request is still refused
  once the round has a bet.
- DTOs: `GameParametersDto.Modes[]` has `{ name, wordCount, rtpPercent, topMultiplier }`, ordered by
  position. `wordCount` and `topMultiplier` are derived from the ladder. The scalar `rtpPercent` is the
  **maximum** across modes. `ActiveGameDto`, `GameVerificationDto` and `GameHistoryDto` each gain
  `boardMode`, and `GameHistoryDto.ClonePublic()` copies it.
- The cache key changed to **`ActiveParameters:v3`** (from `:v2`) because the DTO shape changed.

### What did not change

Coverage did not change: `HouseEdgeCoverage.WorstCase` prices each unsettled round at the best multiplier on
**its own** board, so AVERAGE and SUPER over-reserve and never under-reserve. The weighted provably-fair draw,
the seed protocol, `verify.html`, the typer, rewards, refunds, the maintenance switch and the house seed bet
exit are also unchanged.

### Frontend

- **Mode keys.** The stake plate's bottom row has **MIN / MAX**, a seam, then three small round mode keys
  and an LCD that names the selected board, for example `HYPER 10x`. The lit key takes the tier colour of
  its mode's ceiling. The keys are locked once a bet is placed and while the round runs. They are **not**
  locked during a reroll, because `rerollBoard` already refuses a second reroll while one is in flight. This
  replaced the owner-rejected rotary dial, but the component is still named `HyperRollModeDial.vue`.
- **The `.01/.02/.05/MAX` chips are gone** (`HyperRollStakeChips.vue` was deleted). The −/+ keys step on a
  0.01 grid, and MIN/MAX jump to the bounds.
- **The chosen mode is remembered per browser** in `localStorage['hyperRoll:mode:<typeId>']`. A first visit
  opens on AVERAGE. Pressing a mode key redraws through the reroll path, and the **lever reroll always sends
  the round's current mode**, so it never drops back to the default.
- The key row just gets shorter: keys keep their size.
- ⚠️ **A word's colour is its RANK ON ITS OWN BOARD since 2026-10-02** (`houseEdge.boardTier`), not the
  fixed multiplier ranges it shipped with. Those ranges were drawn for HYPER alone, so on AVERAGE 3× and
  4× were both gray and 5× and 6× both green — two different payouts in one colour. Every board has
  exactly **five distinct multipliers**, so ranking them runs the whole ladder once per board:

  | Board | Gray | Green | Blue | Purple | Gold |
  |---|---|---|---|---|---|
  | AVERAGE | 3× | 4× | 5× | 6× | 7× |
  | SUPER | 3× | 4× | 6× | 8× | 9×, 9× |
  | HYPER | 4× | 6× | 8×, 8× | 9×, 9× | 10×, 10× |

  Two keys share a colour only when they pay the same. It drives the key art and the terminal's MULT
  boxes (live and practice), and the win banner's flash colour.
  - ⚠️ **The colour is relative to the board**: gold is 7× on AVERAGE, 9× on SUPER and 10× on HYPER, and
    every board's top word now wears the gold key and its sparkle.
  - ⚠️ **What a win FEELS like still follows the absolute multiplier** — `winHeadline`, the coin count, the
    screen shake and the 10× gold triple ring all keep `tierName`'s thresholds. A 7× on AVERAGE flashes
    gold but says BIG HIT.
  - ⚠️ **The mode keys and their LCD keep `tierName`** (AVERAGE blue, SUPER purple, HYPER gold) so the
    three boards still light differently. Known side effect: `SUPER 9×` reads purple while the 9× keys on
    that board are gold.
  - ⚠️ **Rejected the same day:** an eight-colour, one-per-multiplier ladder (silver / two greens / two
    blues / purple / red / gold). Built, shown to the owner, dropped — eight unrelated hues stopped
    reading as a progression. Do not re-propose it.
  - A board without exactly five distinct multipliers is spread across the ladder end to end; changing a
    ladder's slot count therefore changes its colours — re-check this table when a mode's ladder moves.
  - ⚠️ The how-to-play guide's static Hyper Roll pictures (`assets/guide/hyper-roll-*.png`) were drawn
    under the old ranges and were **not** regenerated.
- The mode also appears in the stats panel (one RTP per mode instead of a single `91.84% RTP` badge), the
  fairness panel, the games history rows, the terminal key hint, and the how-to-play mode card.
- **Practice** (`/game/practice?mode=hyper-roll`) has all three modes offline. Its ladders and the new words
  are copied by hand into `constants/practiceGame.ts`.

### Rollout

Migration `HyperRollModes` creates `GameBoardModes`, adds `Games.BoardMode`, and backfills `HYPER`. It is
additive only. ⚠️ **There is a known window:** Consumers inserts the 4- and 5-letter words, and the
still-running old API revision rejects them, so for a few minutes it cannot **open** a new Hyper Roll board.
No money path is touched and no running round is affected. Deploy Consumers and API back to back. See spec §12
for the zero-downtime alternative.

## Key code touchpoints

**Backend (`monkify-backend/src`)**
- `SpaceStroker.Domain/Games/Entities/GameBoardMode.cs` (new), `Game.cs` (`BoardMode`), `GameParameters.cs`
  (`BoardModes`), `ValueObjects/HouseEdgeSlot.cs` (`Length`).
- `SpaceStroker.Domain/Games/Services/HouseEdgeRules.cs` (`ValidateMode`, three-part `ParseLadder`/`FormatLadder`,
  `WordMinLength = 4`), `HouseEdgeBoard.cs` (draw by length).
- `SpaceStroker.Infrastructure/Services/Games/GameService.cs` (`DrawHouseEdgeBoard` resolves the mode,
  `OpenSoloBoard`/`RerollSoloBoard`), `HouseEdgeReconciler.cs` (modes pass), `GameHistoryService.cs`.
- `SpaceStroker.Infrastructure/Contracts/Games/` (`GameParametersDto`, `ActiveGameDto`, `GameHistoryDto`,
  `GameVerificationDto`), `Handlers/Games/GetGameVerification/`, `Services/Cache/CacheHelper.cs` (`:v3`),
  `Data/Context/SpaceStrokerDbContext.cs`, `Migrations/20260922212125_HyperRollModes.cs`.
- `SpaceStroker.Api/Controllers/GamesController.cs` (`BoardModeBody` on `boards` and `reroll`).
- `SpaceStroker.Consumers/Program.cs` (seeds `BoardModes`). Config: `SpaceStroker.Common/spacestrokersettings.json`
  → `HouseEdge.Modes`, plus the 20 new words.
- Tests: `SpaceStroker.Tests` (`GameServiceBoardModeTests`, `HouseEdgeRulesTests`, `HouseEdgeBoardTests`,
  config-binding tests). `SpaceStroker.Tests.Integration`: **`HyperRollModeFlowTests`** (happy path, one per
  mode), `DefaultGameSeedingTests`, `HyperRollMisconfigurationTests`.

**Frontend (`monkify-front/src`)**
- `components/game/HyperRollModeDial.vue` (mode keys + MIN/MAX), `GameConsole.vue`, `GameStatsPanel.vue`,
  `GameFairPanel.vue`, `GameTerminal.vue`; `components/practice/PracticeConsole.vue`;
  `components/guide/GuideModeCards.vue`; `pages/GameHistoryPage.vue`.
- `hooks/useGame.ts` (mode storage, open/reroll with mode), `hooks/usePracticeHyperRoll.ts`,
  `hooks/usePracticeGame.ts`, `constants/practiceGame.ts`, `services/mainService.ts`, `types/index.ts`.
- Sprites: `assets/game/mode/key-*.png`, `assets/game/stake/key-min.png`, `label-min.png`.

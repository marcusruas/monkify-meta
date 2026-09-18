# SpaceStroker — "Hyper Roll: how to play" — Implementation Spec

> **Status: Shipped to `main` with Hyper Roll (#183) on 2026-09-18, via `feature/hyper-roll-handoff`.**
> ⚠️ **The break-glass box (§2.2) was replaced before shipping** by a `HOW TO PLAY [?]` key in the
> screen's corner (owner, 2026-09-18) — see §8, which also records the lobby-card changes made then.
> Originally implemented on `feature/hyper-roll-howto` (2026-09-17) — designed with the
> owner on 2026-09-17 from five mockups per point in Claude Design
> ([project](https://claude.ai/design/p/ea8f9d0f-a5fa-4355-891b-7a064bb5affe)); the owner chose **1a**
> and **2a**. Extends [`hyper-roll.md`](hyper-roll.md) and
> [`practice-mode.md`](practice-mode.md); the guide half extends
> [`how-to-play.md`](how-to-play.md) / [`how-to-play-changes.md`](how-to-play-changes.md).
> Repos affected: `monkify-front` and this doc in `monkify-meta`. **No backend change at all.**
> Branch / worktree: `feature/hyper-roll-howto` → `.worktrees/hyper-roll-howto/<repo>`, branched off
> `feature/hyper-roll` (not off `main`) because it teaches a game that has not shipped yet.
> Change level: **QOL / UI** — no backend behaviour, no money path, no migration, so the mandatory
> happy-path integration test rule does not apply (`constraints.md` → "Integration tests": new
> **features** only). The gates are `yarn lint` and `yarn build`, both green.
>
> **Purpose:** the app could not teach Hyper Roll. `/how-to-play` had one round narrative written for a
> race, and the only guided round in the product (`/game/practice`) was hard-wired to Cosmic Sprint's
> shape. A player looking at the Hyper Roll cabinet had nowhere to go from it.

---

## 1. Summary

| | Before | After |
|---|---|---|
| Guide order | `how-the-game-works`, `what-you-win`, `the-modes`, `orbit`, … | **`the-modes`**, `how-the-game-works`, **`hyper-roll`**, `what-you-win`, `orbit`, … |
| Round tutorial | One rail, six steps, written for a race | **Two rails**: four race steps and five Hyper Roll steps |
| Hyper Roll art | None | Four crops of the live cabinet in `assets/guide/` |
| Help from the game screen | None | A **`HOW TO PLAY [?]` key** in the screen's bottom-right corner (§8.1; replaced the break-glass box) |
| Practice round | Always a 6-letter race | `?mode=hyper-roll` runs a **solo board round** |
| Practice engine | `usePracticeGame` | `usePracticeGame` (race) + **`usePracticeHyperRoll`** |

---

## 2. The two decisions

### 2.1 Mockup 1a — two panels on the guide's own rail

Chosen over a mode-switch tab (1b), a side-by-side comparison (1c), an illustrated strip (1d) and a
cabinet walkthrough (1e).

- The existing `HOW THE GAME WORKS` section becomes **`HOW A RACE ROUND WORKS`**, covering Cosmic
  Sprint and Space Jackpot only.
- A new **`HOW HYPER ROLL WORKS`** section follows it.
- **Both sit below `THE MODES`**, which is now the page's first section.
- No new components: both sections render the shipped `GuideRail` / `GuideTip`.

**Why the tables lead now.** The old order opened on the round narrative and reached the tables third.
With two different games to teach, a reader who meets a rail before the tables cannot tell which of the
two they are reading. The accepted cost: `THE MODES` is the only section that depends on the catalogue
fetch, so the page's first paint can be a loading block. `GuideModeCards` owns all three of its states
and every other section renders fully whatever the network is doing.

**The `how-the-game-works` id is unchanged.** Section ids are a cross-surface contract
(`constants/guideSections.ts`). Nothing in `monkify-front` or `monkify-lp` links this one today —
checked, unlike `#orbit`, `#token` and `#feedbacker`, which are linked — but a shared or bookmarked
link is not worth breaking for a heading change.

**What the race section lost.** The `CONNECT A WALLET` and `PICK A MODE` steps are gone. Neither was
race-specific, and both tutorials now sit under the tables, so `PICK A MODE` had become a step telling
the reader to scroll up. The wallet fact survives as one clause in the race lead ("Connect a wallet and
you are in: it is the only account there is") and in `WHAT YOU WIN` → `GETTING PAID`. The closing
`GuideTip` moved to the Hyper Roll section, which keeps it the single consumer of
`.arcade-frame--black` (`GuideTip`'s own rule: at most one per section, and the page has exactly one).

### 2.2 Mockup 2a — break the glass, on the apron the console already has (⚰️ REPLACED, see §8.1)

> ⚰️ **Superseded 2026-09-18, before shipping.** The owner removed the box entirely and asked for the
> help affordance to sit in a corner of the display instead. Kept below as the record of what was
> built and why; none of it ships. The glass assets were deleted.

Chosen over centred (2b), right (2c) and the broken-state variant (2d); 2e measured the band.

A little alarm box is bolted to the **left of the console's apron**, with `BREAK GLASS` engraved on the
plate beside it. **Hover lifts the pane** and shows a manual behind it. **A click breaks it** and routes
to `/game/practice?mode=<mode>`, leaving the round.

> ⚠️ **The console is NOT redesigned for it** (owner's explicit instruction: *"I don't want to redesign
> the console for the glass break. Put it in the space it has."*). The apron is `terminal.png`'s own
> dark base band — sprite rows 300-409 of 420, flat colour across the full width — and it was already
> empty. Nothing on the console moved, resized or changed.

**Geometry** (`GameConsole.vue`, `.break-glass`): a share of the console box, exactly like every other
plate on that face. The apron starts at `300 / 420 = 71.43%` and is `26.19%` tall; the box art is 9 of
the band's 11 art-pixel rows, so its top is `71.43% + (11 - 9) / 2 / 42 = 73.81%`. Width is `14 / 97 =
14.4%` of the box, with `aspect-ratio: 14 / 9` so the art never stretches with the console.

**When it is hidden.**

| Case | Why |
|---|---|
| A live bet (`boardHasBet` / `iHaveBetThisRound`) | Leaving does not cancel a bet the server already has, so offering an exit there offers to walk away from a live stake. It returns with the next board. This is also why the click needs no confirm step. |
| `mobile` and `wide` faces | Both **crop the base band off** (`wideSpriteCrop`), so there is no apron to bolt it to. The phone reaches the manual from the header menu, as it always has. |
| Embedded | `main.ts` redirects `/game/practice` out of a frame, so the click would land on the lobby. |

**The 220ms beat before the route change** is what makes the break readable: without it the navigation
eats the broken frame and the box just vanishes, which reads as a mis-click.

---

## 3. The practice page becomes mode-aware

`/game/practice?mode=hyper-roll`. Anything unrecognised, and a bare `/game/practice` (what the three
CTAs on `/` still link to), runs the race.

> ⚠️ **A MODE, NOT A GAME TYPE ID.** The owner's original suggestion was to pass the game type through.
> The codebase's own rule is that modes match by **shape** and never by id (`utils/gameArchetype.ts`),
> because the GUIDs are regenerated on every teardown and reseed — a shared link carrying one would
> land on a page that cannot resolve it. `GameConsole` derives `hyper-roll` from `isHouseEdge` and
> `race` otherwise.

The mode is read **once, at setup**. The store, the tutorial and both components are built around it,
so changing it under a mounted page would mean disposing and rebuilding all three; a player switches
mode by following a link, which is a fresh mount.

### 3.1 A second engine, not a branch

`createPracticeHyperRollGame` (`hooks/usePracticeHyperRoll.ts`) sits beside the race's
`createPracticeRaceGame`, and `createPracticeGame(mode)` dispatches. They share `GameStatus`, the
character stream and `practicePhaseAt`, and almost nothing else: no opponent, no pot, no shared
countdown, no near-miss row, and a bet that carries a stake as well as a word.

Both satisfy one widened `PracticeGameStore`, so the page and the two components hold **one `mode`
branch each**. The race store's half of that shape is inert (empty board, `''` stake, four actions that
refuse), which is cheaper than two shapes and a cast at every call site.

⚠️ **Import direction.** `usePracticeGame` imports the Hyper Roll factory to dispatch, so
`usePracticeHyperRoll` takes **types only** from it (`import type`), and `practicePhaseAt` moved into
`constants/practiceGame.ts` where both can reach it. That is what keeps the two modules acyclic.

⚠️ **The isolation rule holds in full** (`practice-mode.md` §7.2): nothing under `hooks/usePractice*`
or `components/practice/` imports `useGame`, `useNearMiss`, `useGameHub`, `useGameChat`,
`useIdleDisconnect`, `mainService` or `solana-wallets-vue`. Practice still makes no request and opens
no socket. Two things are reused from the live game and both are **pure**:

- `utils/houseEdge` (`payoutFor`, `tierName`, `tierColor`, `tierRim`, `winHeadline`,
  `formatMultiplier`) and `utils/money` (`formatPayout`, `toLamportGrid`) — display helpers, so the
  figure practice prints and the figure the game prints cannot drift.
- `HyperRollWinFx.vue` and `HyperRollStakeChips.vue`. The win FX was already pure props.
  **`HyperRollStakeChips` was refactored for this**: it used to read `useGame` directly and now takes
  `maxStake` + `disabled` and emits `pick` / `max`, with `GameConsole` supplying the wiring. If it ever
  reads a store again, practice has to stop importing it and the sprite row has to be copied, which is
  the duplication the refactor avoided.

### 3.2 The round

| | Value | Why |
|---|---|---|
| Board | 8 slots, ladder `10, 10, 4, 6, 8, 8, 9, 9`, shuffled | Decision 37's live board. Words are drawn from a copy of the backend's curated pool, so a practice board is indistinguishable from a live one. |
| Stake | `0.01` to `0.1`, step `0.01`, **visor opens at the minimum** | The live table's bounds, and the 2026-09-17 owner call that the visor opens filled in. |
| `opponentBetMs` | `0` | A solo round has no second player, so the clock starts at the bet. |
| `countdownMs` | `2500` | The real round starts at once; a bet that cut straight to a wall of characters reads as a glitch, so this stands in for the beat the cabinet takes to open. |
| `runMs` | `9000` (the race's is 15000) | No near-miss row to read and no other player to root against. |
| Outcome | **The player always wins** | The same fixed outcome `#142` gave the race round, and for the same reason: a tutorial round has to be interpretable. |

The lever does the live cabinet's three jobs, with the same tags: `REROLL` while nothing is locked,
`PLAY <stake>` once a word is, `NEW BOARD` on the result.

### 3.3 Edson's script

Four beats per mode, same shape. Hyper Roll's hold boundary is **`board-locked`** — its equivalent of
the race's `before-countdown`, since the beat the second bubble talks over is the bet itself — engaged
the same way, *before* the phase is committed.

`SHOW ME` is **one move shorter** on Hyper Roll: there is nothing to type, so Edson presses a key
(keeping whatever the player already lit) and pulls. The stake is left alone deliberately, because the
visor already opens at the minimum and demonstrating a step the player does not have to take would
teach the wrong thing. The pull still goes through the console's real `onLever()`, so the demonstrated
move and the real move cannot diverge.

### 3.4 What the screen prints

Copied in **shape** from `GameTerminal.vue`, never imported — the same knowing duplication
`practice-mode.md` §7.8 already accepts for the race. The header line, the `KEY | WORD | MULT | PAYS`
board, the waiting lines and the result block all match the live screen.

⚠️ **The result states a payout that did not happen** (`> +0.09 SOL SENT TO YOUR WALLET.`), exactly as
the race's `> PAID TO:` does. The permanent `> PRACTICE ROUND. NOTHING IS CHARGED, NOTHING IS PAID.`
banner is what answers every money line on this screen (owner's call, 2026-08-02: practice must read
like the real game). Do not reword the round copy to hedge.

**One deliberate omission:** the live board's **reel-in** (each row scrambling and clacking into place)
is not reproduced. It is 40 lines of timer for a flourish, and a bubble is usually up when a practice
board changes.

---

## 4. The art

Every picture ships as a real file; nothing in the mockups was a sketch.

| File | What it is |
|---|---|
| `assets/guide/hyper-roll-board.png` | The CRT's board table, cropped from a live round |
| `assets/guide/hyper-roll-keys.png` | The keypad plate, tier colours and all |
| `assets/guide/hyper-roll-visor.png` | The stake plate: visor, chips and MAX |
| `assets/guide/hyper-roll-lever.png` | The lever plate with its `REROLL` tag |

The four guide crops were cut from a screenshot of the running cabinet at 2× nearest-neighbour, so the
pixel grid survives. **If the console art or the board layout changes, they have to be re-cut** — that
is the price of teaching with the game's own screen rather than a drawing of it.

The glass-box art (`assets/game/glass-box*.png`, `label-break-glass.png`) was deleted with the box
(§8.1).

---

## 5. Copy rules

The owner's rule for this card: **no litotes and no em dashes** in anything a player reads. Say what a
thing does, not what it fails to do ("the house takes no cut", never "the house does not take much of a
cut"). It applies to both guide sections and Edson's Hyper Roll script. The
older guide rules still bind: never "animal", never a numeric choice cap, and **the word is "word"**.

No RTP figure is written into the Hyper Roll section. It has moved three times in two weeks (decisions
29, 36, 37); the section says every word pays back the same rate and sends the reader to the STATS
panel, which reads it live. `GuideModeCards` still prints the live number, which comes off the
catalogue.

---

## 6. Files

**`monkify-front`**

| File | Change |
|---|---|
| `constants/guideSections.ts` | `hyper-roll` id, the new order, the retitled race section |
| `components/guide/sections/GuideHowTheGameWorks.vue` | Rewritten as the race tutorial (4 steps) |
| `components/guide/sections/GuideHyperRoll.vue` | **New** — 5 steps plus the page's tip |
| `components/guide/sections/GuideTheModes.vue` | Leads the page now, so it introduces Edson and the two kinds of table |
| `pages/HowToPlayPage.vue` | Stale "six sections" comment |
| `components/game/GameConsole.vue` | The chips wiring (the break-glass box was removed again, §8.1) |
| `components/game/GameTerminal.vue` | The `HOW TO PLAY [?]` key (§8.1) |
| `components/game/HyperRollStakeChips.vue` | Presentational: props in, events out |
| `components/practice/PracticeConsole.vue` | A second face: stake visor, chips, eight live board keys, lever tag |
| `components/practice/PracticeTerminal.vue` | The board table, the solo lines, the result, the win FX |
| `constants/practiceGame.ts` | The mode, the board pool, the Hyper Roll config, timings, script and lines; `practicePhaseAt` moved in |
| `hooks/usePracticeGame.ts` | Widened store, renamed race factory, `createPracticeGame(mode)` dispatcher |
| `hooks/usePracticeHyperRoll.ts` | **New** — the solo engine |
| `hooks/usePracticeTutorial.ts` | Per-mode script, the `board-locked` boundary, the shorter demo |
| `pages/PracticePage.vue` | Reads `?mode=` |
| `routes.ts` | Documents the query and the new entry point |
| `assets/guide/hyper-roll-*.png` | **New** art |

**`monkify-meta`**: this spec, plus the INDEX row.

---

## 7. Open questions for the owner

All three were about the break-glass box and were closed when it was replaced (§8.1): nothing breaks
any more, the key shows on the phone too, and a race table's key still opens the race practice round.

---

## 8. Owner changes before shipping (2026-09-18)

Made on `feature/hyper-roll-handoff` and shipped with it. Front only, except the catalogue copy in §8.4.

### 8.1 `HOW TO PLAY [?]` replaces the break-glass box

`GameTerminal.vue`. A green `HOW TO PLAY` label beside a `[?]` key (`--color-phosphor`, the terminal's
own green), pinned to the **screen's** bottom-right corner. On desktop the terminal container IS
`bg.png` (1300 × 1320), whose black screen runs x 70–1219, y 90–809, so the key sits at
`right: calc(6.15% + 18px)`, `bottom: calc(38.64% + 16px)`; on the phone the container is the screen,
so `10px` from its corner. A click plays the button sound and routes to
`/game/practice?mode=<hyper-roll|race>` (a SHAPE, never a gameTypeId), with no break animation.

Shown only while **the round is taking bets** (`status === WaitingBets`, or no instance yet) **and the
player has no bet in it** (`iHaveBetThisRound`, or any bet on a Hyper Roll board, which is one
player's). So it vanishes on a bet and when the round starts, and returns with the next round. Never
when embedded (`main.ts` redirects `/game/practice` out of a frame). Unlike the box it shows on every
face, phone included.

### 8.2 The Hyper Roll lobby card

- **Own archetype.** `utils/gameArt.ts` gains `'hyper-roll'` (`isHouseEdge`), so it no longer borrows
  Beast Mode's art and cyan accent. Art: `assets/types/hyperroll.gif` + a first-frame
  `hyperroll-poster.png` (640 × 640, transparent, art only from row 160).
- **Fit `zoom-bottom`**: `height: 150%`, `bottom: -12.5%` — pushes the empty top quarter out of the
  frame so Edson's hair meets the top edge; only the bottom ~8% (torso) is cropped. Also used by the
  guide's mode card.
- **Accent pair** `--color-hyper: #2f86ff` / `--color-hyper-alt: #c24dff` (`colors.css`, tailwind
  `hyper` / `hyper-alt`) — the cabinet's blue and purple, which are also the blue and purple
  multiplier tiers. Name in blue; bevel and hover run blue → purple; a faint (9% / 7%) blue → purple
  tint behind the transparent sprite. The guide's MULTIPLIER card uses `text-hyper` too.
- **Mode badges**, top-right of the art, Hyper Roll only: `1P · VS HOUSE` and `4X–10X`, in the same
  HUD plate as the TICKET/FROM chips (black scrim, hairline border, VT323); only the text is coloured
  (lightened tints of the accent pair). A plain `X`, because VT323 draws `×` raised. The range is a
  constant — `ActiveGame` carries no ladder — so keep it in step with the word-length ladder.
- **Tag** is now `PICK 1 OF 8 · SET YOUR STAKE` (the 10× moved onto the badge).

### 8.3 Which games show, and in what order

`utils/gameCatalogue.ts → shownGameTypes()`, used by the lobby picker (`GameTypes.vue`) and the guide's
mode cards (`GuideModeCards.vue`). **Gated types (`comingSoon`) are hidden** — today that is only Beast
Mode — instead of rendering a COMING SOON card; front only, so the backend flag still brings a type
back. **Hyper Roll (`isHouseEdge`) always goes last**; everything else keeps the catalogue's order
(stable sort). Result: Cosmic Sprint → Space Jackpot → Hyper Roll.

### 8.4 Catalogue description (backend copy)

`spacestrokersettings.json`: `"Pick a word, set your stake - land it and the house pays up to 10x."`
— two lines on the card, like the others. ⚠️ **The seeder never patches an existing row's
`Description`**, so a database that already holds a Hyper Roll row keeps the old three-line text until
it is updated by hand (and the `ActiveParameters:v2` Redis key is dropped or expires).

### 8.5 Bet rockets fit their word

`TorpedoSpawner.vue`. The bettor's word is boxed to the torpedo's fuselage (x 105–360 of the 590 × 170
sprite) and shrunk from 16px to fit; below 10px the rocket grows instead. A 10-letter word lands at
~10.8px; a 20-character choice grows the rocket to ~149px tall.

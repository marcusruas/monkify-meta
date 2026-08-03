# Space Stroker — "Trim the home hero" — Implementation Spec

> **Status: SHIPPED (2026-08-03)** — merged to `main` via `feature/home-hero-trim` in `monkify-front`
> and `monkify-meta`. Distilled into `features/home-dashboard.md` → "The hero band"; the rules this
> card reversed carry a post-ship note in `specs/home-dashboard.md` §7.11.5. **§14 records how the
> shipped result differs from the §7 design pass** — read it before treating §7 as current.
> Follow-up: [#145 — replace the temp sprite + wallet-connected animation](https://trello.com/c/6T053xnq).
>
> Refined via Trello card
> [trim down hero card in home page](https://trello.com/c/U1Sdsk6Y) (#138, refined 2026-08-03).
> Repos affected: `monkify-front`, and this doc + `features/home-dashboard.md` in `monkify-meta`.
> **`monkify-backend` is untouched.**
>
> **Purpose:** the front door at `/` spends its whole first viewport on decoration. `HomeHero.vue`
> is a ~530 px tall band whose largest element is a 533 KB saucer GIF, and `+ PICK YOUR GAME +` —
> the only thing on the page a visitor can act on — sits at or below the fold at 1366×768. This
> collapses the hero into a **short, frameless band** (≤ 50 % of today's height, both states) with a
> small mascot, so the picker clears the fold.

---

## 1. Summary

One component, both of its states, plus the two stylesheet loose ends the change exposes.

| | Today | After |
| --- | --- | --- |
| Section padding (desktop) | `pt-20 pb-24` (176 px) | `pt-8 pb-10` (72 px) |
| Section padding (mobile) | `pt-10 pb-14` (96 px) | `pt-6 pb-8` (56 px) |
| Art | `edson-ship.gif` 1850×1160, `md:max-w-[620px]` ⇒ **~355 px tall**, drives the band | `guide/game-modes.gif` 570×520, `md:h-[140px]` ⇒ **140 px tall**, never drives the band |
| Art on `html.light-graphics` | still-frame `edson-ship.png` swap via `usePerfMode` | **the GIF, everywhere** (scoped exception, decision 9) |
| Art below `sm` (640 px) | stacks under the copy at `max-w-[340px]` | **not rendered** (§7.5.2) |
| Cold copy | headline · sub · buttons · fine print | headline · sub · buttons |
| Connected copy | eyebrow · name · live line · buttons · streak block | `WELCOME BACK` · name · live line · buttons |
| Frame | none | **none** (unchanged — AC6) |
| Rendered height @ 1366×768 | ~531 px (both states) | **≤ 265 px**, target ~220–250 px |

Everything else on `/` — the picker, the panels below it, `HomePage.vue`'s wrapper — is untouched.

### 1.1 What this reverses, on purpose

This is a **deliberate shipped decision being traded away**, not a regression, and the code comments
must say so:

- `specs/home-dashboard.md` §7.11.5 / **decision 29** *raised* the hero padding to
  `pt-10 pb-14 md:pt-20 md:pb-24` to give the fold "real vertical room".
- **Decision 23** made the saucer large on purpose — *"the saucer is the page's one piece of
  character art and it should dominate the fold, not sit politely beside the text"* — and
  **decision 28** enlarged it further to `md:max-w-[620px]` on the larger flex basis.

Both are reversed here. The reason is reach, not taste: the fold's most valuable pixels were spent
on an element the player cannot click.

**Not reversed:** decisions 22–23's *frameless* rule (AC6), decision 29's connected-state content
model (name-not-address, live line, no zero-streak), and decision 34's one-handler entry rule.

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo (§2 / `AGENTS.md` rule 6):** `.worktrees/home-hero-trim/monkify-front` and
  `.worktrees/home-hero-trim/monkify-meta`, branch `feature/home-hero-trim` in both, cut from
  `origin/main`. Both hubs stay on `main` — never `git checkout -b` there. Push to `origin` right
  after branch work (rule 5). **Both worktrees and branches already exist** (created 2026-08-03 and
  registered on the card).
- **No integration test, no unit test (§3).** Level is **Minor change, frontend-only**: no entity,
  migration, handler, consumer, endpoint, DTO or SignalR contract changes, so the "every new feature
  ships an integration test" mandate does not apply. `monkify-front` has no test runner.
- **Gates:** `yarn lint` **and** `yarn build` must both pass, plus the manual QA in §13.
- ⚠️ **`yarn lint` needs uniform CRLF.** `monkify-front` is checked out with `core.autocrlf=true`;
  editing with an LF-writing tool produces `Insert ␍` on every line of the file. Fix with `unix2dos`
  before committing, and verify with `od -c <file> | head`.
- **EF / migrations / secrets / operator messaging (§1, §4, §6):** not applicable — nothing in this
  change touches config, the database, or a user-facing error path.
- **KB sync at ship (`INDEX.md` → "Keeping the knowledge base current"):** `features/home-dashboard.md`
  states hero rules this card overturns. §10.3 lists the exact lines.

---

## 3. Resolved product decisions

Decisions **1–12** are the card's, carried verbatim in substance. **13–20** are this spec's design
pass, which the card explicitly delegated (*"The banner's content is OPEN… Two-column vs
inline-mascot is the design pass's call"*).

| # | Decision |
| --- | --- |
| 1 | **Both states are trimmed** — cold and connected. They share the padding and the art column; fixing one leaves the other tall. |
| 2 | **The hero stays FRAMELESS.** *"Squish to a rectangle"* means a short wide band, **not** wrapping it in `.arcade-frame` / `.arcade-panel`. `home-dashboard` decisions 22–23 survive: the hero is the page's title card, so a border there is a border around everything. |
| 3 | **Reach bar (functional):** at a **1366×768** browser window, `+ PICK YOUR GAME +` **and the top edge of the mode cards** are visible without scrolling, in **both** states. |
| 4 | **Soft mobile bar:** on **390×844** portrait, the picker heading is reachable with at most one short scroll. Deliberately soft — a phone fold cannot hold a hero and a card. |
| 5 | **Hard height bar:** the hero `<section>`'s rendered height at 1366×768 is **≤ 50 %** of the pre-change height, **per state**. ⚠️ The baseline **must be measured and recorded before any edit** (§7.1) — the AC is unverifiable afterwards. |
| 6 | **The band's content is OPEN.** Add, remove or update at will; only decisions 3–5 and the traps in §7.6 are pinned. §7.3–7.4 exercise that latitude and are the design of record, but a later reviewer may move within it without re-refining the card. |
| 7 | **Art is `@/assets/guide/game-modes.gif`** (44.8 KB, **570×520**, Edson holding the animated sign that cycles the mode labels). This resolves the card's `/img/game-modes`: there is **no `public/img/`** in `monkify-front`; the sprite is the one `GuideWhereToStart.vue` renders for **PICK A MODE** on `/how-to-play`. |
| 8 | **Art size capped, placement free:** never more than **~200 px tall on desktop**, and **never the element that drives the band's height**. |
| 9 | **The GIF loops everywhere — no poster still, no `usePerfMode` swap.** ⚠️ This **knowingly bends `specs/light-graphics-mode.md` §7.10**, which bans infinite animation under `html.light-graphics`. Accepted because `/how-to-play` **already** renders this exact sprite raw, with no perf-mode wiring (`GuideArt.vue` → `GuideRail`), on every phone — and the file is 44.8 KB, an eighth of the asset the rule was written about. **A decision, not an oversight.** |
| 10 | **`edson-ship.gif` / `edson-ship.png` stay in the repo, unused.** `HomeHero` is their only importer (verified — see §7.6.1); webpack emits nothing for an unimported asset, so the cost is repo-only, and the card's *"temp"* framing means the swap-back must stay a one-line revert. |
| 11 | **The streak block is dropped.** `HomePlayerStats` ("YOUR RECORD") renders **best** streak, not current, so `currentStreak` leaves the app entirely — accepted. `GET /players/stats` keeps returning it, so it can return anywhere later. |
| 12 | **Level: Minor change, frontend-only** ⇒ no integration test, no unit test (`constraints.md` §3). |
| 13 | **The band is a two-column row, not an inline mascot.** The copy reads left, a small mascot anchors right. An inline mascot inside the copy flow would have to sit beside the headline — and the headline is exactly the element the Press Start 2P advance trap (§7.6.4) makes fragile. Two columns keep the copy column's width a single number the headline can be checked against. |
| 14 | **The art column is `shrink-0` and sized by HEIGHT, not by a flex basis.** Today both columns are `flex-1 basis-[…]`, so the art's rendered width — and therefore its height — is a function of the viewport, which is what let it reach 355 px. Pinning `h-[…] w-auto` makes the art's height a constant the band's budget can be written against, and makes AC8 (*"not the element that determines the band's height"*) true by construction rather than by measurement luck. |
| 15 | **The art is not rendered below `sm` (640 px).** Beside the copy at 390 px it leaves the headline a ~245 px column, where `FIRST WORD OUT` at 19 px needs ~266 px and breaks to **three lines** — the exact failure AC11 pins. Stacking it instead adds the height this card exists to remove. A phone gets a pure copy band; Edson is still on every mode card directly below it. |
| 16 | **The cold state loses its static fine print** (*"Every round is seed-locked before it starts."*). It is a trust claim, not a mechanic, and the page already makes it twice below the fold (`HomeHowItWorks`, the Provably Fair section). Restoring it is one `<p>` if the owner disagrees — but it costs ~40 px of the ~265 px budget. |
| 17 | **The connected state's `Welcome back` eyebrow drops to the shared `.eyebrow` treatment** (Press Start 2P 9 px amber, the page's own section-marker idiom) instead of VT323 20/23 px. The greeting is a label; the **name** is the thing being said. Saves ~16 px and makes the band's top line rhyme with the picker header below it. |
| 18 | **The live line stays**, unchanged in behaviour: same `useLiveGames` snapshot, same actionability priority, same quiet fallback, **no spinner and no error state**. It is the one dynamic thing in the band and the actual reason a returning player opened the page (`home-dashboard` decision 29). Dropping it would leave the connected band saying only who you are — the exact complaint decision 29 was filed for. |
| 19 | **Both CTAs stay, and the primary keeps routing through `useGamePicker.chooseGame`.** The hero is the second entry point into a game (`home-dashboard` decision 34); a hand-rolled `router.push` here lands the player in a game the app never selected and that never fired `game_type_selected`. |
| 20 | **The scoped glow is retained, renamed and re-tuned.** `.hero-ship` → `.hero-art`, with the blur radii scaled to the smaller sprite (46 px → 26 px). It stays a flat two-layer `drop-shadow` — no `filter: blur`, no `backdrop-filter`, no animation — so `html.light-graphics` renders it unchanged (AC10). |

---

## 4. Data model

**Nothing.** No entity, no column, no `OnModelCreating` change, no migration.

## 5. Configuration

**Nothing.** No `GeneralSettings` key, no `VUE_APP_*` env var, no seed data.

## 6. Backend behaviour (`monkify-backend`)

**Nothing changes.** For the avoidance of doubt:

- `GET /players/stats` keeps returning `currentStreak`; the frontend simply stops rendering it in the
  hero. `HomePlayerStats` still consumes the same payload (`bestStreak` and the rest), so the request
  itself is unaffected — see §7.6.2.
- `GET /games/{gameTypeId}` (the `useLiveGames` fan-out) is unchanged; the live line reads the same
  singleton snapshot `GameTypes.vue` already fetched and costs no request of its own.
- No new endpoint, no DTO field, no SignalR contract, no cache key, no rate-limit rule.

---

## 7. Frontend behaviour (`monkify-front`)

### 7.1 FIRST: record the baseline (AC1) — before touching a single line

AC2 compares against a number that stops existing the moment the component is edited. Do this on a
clean `feature/home-hero-trim` worktree, **before** the first edit, and paste the output into the
commit body / the Trello card so it is auditable.

1. Serve the worktree (see `local-dev` notes: copy the hub's `.env.local` **and** `yarn.lock` into
   the worktree first, and pick a port you have verified is free — another worktree's dev server
   squatting on 8081 is a known way to measure the wrong app).
2. Set the **viewport** to exactly **1366×768** (devtools device emulation, or
   `mcp__chrome-devtools__resize_page`). Use the *viewport*, not the OS window — a real 1366×768
   window has ~110 px of browser chrome and would make the numbers unreproducible. §7.7 keeps
   headroom for that case explicitly.
3. On `/`, in each state, evaluate:

   ```js
   JSON.stringify(
     [...document.querySelectorAll('#main-container section')].slice(0, 2).map((el) => {
       const r = el.getBoundingClientRect();
       return { h: Math.round(r.height), top: Math.round(r.top) };
     }).concat([
       { pickerHeading: Math.round(
           document.querySelector('#main-container h2').getBoundingClientRect().top) },
       { firstCardTop: Math.round(
           document.querySelector('.pixelated-card').getBoundingClientRect().top) },
     ])
   );
   ```

   The **first** `section` is the hero; the second is the picker wrapper. `h2` is
   `+ PICK YOUR GAME +`; `.pixelated-card` is the first mode card.
4. Do it **twice**:
   - **Cold** — no wallet connected.
   - **Connected, tallest case** — a wallet connected **with a live round on the floor** (so the
     live line renders a real game) **and `currentStreak >= 1`** (so the streak block renders).
     A quiet, streak-less connected page is *not* the baseline; measuring it inflates the pass rate
     of AC2 for free.
5. Record: `heroHeightCold`, `heroHeightConnected`, `pickerHeadingTop`, `firstCardTop`, per state.

**Expected order of magnitude** (arithmetic, not a substitute for measuring): the art column renders
~566 px wide at this viewport, and `edson-ship.gif` is 1850×1160, so the image is ~355 px tall and
taller than either copy column — the band is therefore ~176 px of padding + ~355 px of art ≈ **530 px
in both states**. If the measurement lands far from that, stop and work out why before continuing:
the budget in §7.7 is derived from it.

### 7.2 `HomeHero.vue` — the band

Frameless, one flex row, `items-center`. The art column is fixed-height and `shrink-0`; the copy
column takes the rest.

```
┌─ <section>  (no border, no background) ────────────────────────────────────────┐
│  pt-6 pb-8   md:pt-8 md:pb-10                                                  │
│                                                                                │
│   ┌── copy (min-w-0 flex-1) ─────────────────┐        ┌── art (shrink-0) ──┐   │
│   │  headline / greeting+name                │        │  game-modes.gif    │   │
│   │  sub / live line                         │        │  h-[110px]         │   │
│   │  [ CTA ] [ Practice ]                    │        │  md:h-[140px]      │   │
│   └──────────────────────────────────────────┘        │  hidden below sm   │   │
│                                                       └────────────────────┘   │
└────────────────────────────────────────────────────────────────────────────────┘
```

Section element:

```html
<section class="flex flex-wrap items-center gap-6 pt-6 pb-8 md:gap-10 md:pt-8 md:pb-10">
```

Copy column:

```html
<div class="grid min-w-0 flex-1 gap-3 md:gap-4">
```

`flex-1` with **no `basis-[…]`** — the art no longer competes for width, so the copy column is
"everything the art does not take", which is what makes the headline check in §7.6.4 a single
arithmetic statement instead of a per-breakpoint table.

### 7.3 Cold state (no wallet)

Three blocks, down from four.

| Block | Markup | Change |
| --- | --- | --- |
| Headline | `h1.press-start-2p-regular` `text-[19px] md:text-[22px] lg:text-[26px]`, `leading-[1.35]`, `text-shadow: 3px 3px 0 #000`, `FIRST WORD OUT<br /><span class="text-cyan">TAKES THE POT.</span>` | `lg` ceiling 32 px → **26 px**; leading 1.45 → 1.35. Copy **unchanged** |
| Sub | `p.vt323-regular` `max-w-[38ch]` `text-[19px] md:text-[21px]` `leading-[1.3]` `text-[#b8c5d9]` — *"Everyone calls six characters. Edson types until one lands."* | Copy unchanged; 22 → 21 px, leading 1.35 → 1.3 |
| Buttons | `<wallet-buttons variant="ghost" size="md" />` + `Practice` (`btn-arcade btn-arcade--ghost-cyan`, `@click="emit('practice')"`), in `flex flex-wrap items-center gap-3` | gap 4 → 3. Otherwise **unchanged** — this is the real wallet cluster, which already resolves the desktop-extension vs mobile-deeplink split; a lookalike would have to reimplement it |
| ~~Fine print~~ | — | **Removed** (decision 16) |

⚠️ **`WalletButtons`' `variant="ghost" size="md"` pair is load-bearing** and stays: a filled Connect
beside an outlined Practice reads as two unrelated controls rather than two ways in
(`home-dashboard` decision 28).

### 7.4 Connected state

Four blocks, down from five.

| Block | Markup | Change |
| --- | --- | --- |
| Greeting | `span.eyebrow.eyebrow--mute` → `Welcome back` | was VT323 `text-[20px] md:text-[23px]`; now the shared 9 px `.eyebrow` (decision 17) |
| Name | `span.press-start-2p-regular` `text-[15px] md:text-[20px]` `leading-[1.35]`, `text-shadow: 3px 3px 0 #000`, `{{ playerLabel }}` | 16/24 px → 15/20 px. `playerLabel = displayName \|\| shortAddress` **unchanged** (AC13) |
| Live line | `p.vt323-regular` `text-[19px] md:text-[21px]` `leading-[1.3]` `text-[#b8c5d9]`, with the amber `>` prefix span | 20/24 px → 19/21 px. Logic **unchanged** (AC14) |
| Buttons | `{{ primaryLabel }}` (`btn-arcade btn-arcade--primary`, `@click="onPrimary"`) + `Practice`, `gap-3` | gap 4 → 3. `onPrimary` **unchanged** (AC15) |
| ~~Streak~~ | — | **Removed** (decision 11) — with `usePlayerStats`, `stats`, `streak`, `SEGMENTS` and `segments` (§7.6.2) |

The greeting + name stay one `grid gap-1` sub-block so the label sits tight under its eyebrow.

`liveLine`, `featured`, `primaryLabel` and `onPrimary` are **copied through untouched**. Their
guarantees are ACs in their own right and none of them is a height problem:

- Priority `WaitingBets && players > 0` → `GameStarting` → `InProgress` (actionability, not
  progress).
- No loading state, no error state: the quiet line is true while the snapshot is in flight, when it
  failed, and when the floor is empty (AC14).
- Amounts through `formatGameAmount`, never raw.
- `onPrimary` → `chooseGame(live.game)` when something is live, else `emit('play')` → `HomePage`'s
  `scrollToModes` (AC15, AC16).

### 7.5 The art

#### 7.5.1 The element

```html
<div class="hidden shrink-0 justify-center sm:flex">
  <img
    :src="modeSprite"
    alt="Edson the chimp holding a sign that cycles the game modes"
    draggable="false"
    class="hero-art pixelated block h-[110px] w-auto md:h-[140px]"
  />
</div>
```

- `import modeSprite from '@/assets/guide/game-modes.gif';` — a **static import**, no `computed`,
  no `usePerfMode` branch (decision 9 / AC9).
- **`h-[…] w-auto`, never a width cap.** 570×520 ⇒ at 140 px tall the sprite is ~153 px wide; at
  110 px, ~121 px. Well inside the ~200 px ceiling (decision 8 / AC8), and — because the copy column
  is always taller (§7.7) — never the element that sets the band's height.
- **`pixelated` is kept**: `/how-to-play` renders this same sprite with `pixelated` (`GuideArt.vue`
  line 58), and the app's character art is pixel art throughout.
- ⚠️ **Do NOT copy the guide's `object-contain scale-[0.78] -translate-y-[4%]`.** Those numbers exist
  because `GuideRail` drops the sprite into a fixed **4:3, 172 px-wide** slot and has to fit a
  square-ish canvas into it. Here the image renders at its natural aspect ratio, so the scale and the
  nudge would only shrink and offset it for no reason.
- **No `loading="lazy"`** — and in general never inside a clipped frame; see `home-dashboard`
  decision 44 (Chrome folds ancestor `clip-path` into the intersection check and the image never
  loads). The hero draws no clip, but this is above-the-fold art either way.

#### 7.5.2 Below `sm` (640 px) the art is not rendered

`hidden … sm:flex`. Rationale is decision 15 and it is arithmetic, not preference: at 390 px the
container is `px-3.5` ⇒ 362 px of content; a 110 px-tall sprite is 121 px wide, plus `gap-6` (24 px),
leaving the copy column **~217 px**. `FIRST WORD OUT` is 14 characters and Press Start 2P advances
~1 em per character, so at 19 px it needs **~266 px** — it would break to three lines and fail AC11.

Two rejected alternatives, recorded so they are not re-litigated:

- **Shrink the mobile headline to 17 px** (~238 px needed vs ~217 px available) — still fails, and at
  16 px the headline is smaller than the sub beneath it.
- **Stack the art above/below the copy on mobile** — adds ~130 px to the state that already has the
  least room, which is the opposite of this card.

#### 7.5.3 The glow

```css
.hero-art {
  filter: drop-shadow(0 0 26px color-mix(in oklab, var(--color-cyan) 26%, transparent))
    drop-shadow(0 8px 16px color-mix(in oklab, black 55%, transparent));
}
```

Renamed from `.hero-ship` and re-tuned (46 px / 14 px+26 px radii were sized for a 566 px image; on a
153 px sprite they read as fog). Still **two flat `drop-shadow`s** — no `filter: blur`, no
`backdrop-filter`, no `mix-blend-mode`, no animation — so `html.light-graphics` renders it unchanged
and AC10 holds by construction.

### 7.6 Removals, orphans and traps

#### 7.6.1 The perf-mode swap (AC18)

Deleting the swap orphans **three** symbols; `yarn lint` fails on `no-unused-vars` if any survives:

```ts
import { usePerfMode } from '@/hooks/usePerfMode';   // delete
import shipGif from '@/assets/edson-ship.gif';        // delete
import shipStill from '@/assets/edson-ship.png';      // delete
const { lightMode } = usePerfMode();                  // delete
const shipSrc = computed(() => (lightMode.value ? shipStill : shipGif));  // delete
```

`edson-ship.gif` / `.png` **stay on disk** (decision 10). Verified: `HomeHero.vue` lines 11–12 are
their only references anywhere in `monkify-front`, so after this change nothing imports them and
webpack emits neither file — a repo-only cost, and the swap-back is a two-line revert.

`usePerfMode` itself has other consumers and is **not** deleted.

#### 7.6.2 The streak block (AC18) — a second orphan set the card did not enumerate

Dropping the streak orphans another four symbols in the same file:

```ts
import { usePlayerStats } from '@/hooks/usePlayerStats';  // delete
const { stats } = usePlayerStats();                        // delete
const streak = computed(() => stats.value?.currentStreak ?? 0);  // delete
const SEGMENTS = 7;                                        // delete
const segments = computed(() => …);                        // delete
```

**This does not change any request.** `usePlayerStats` is a module singleton that binds its wallet
watcher on **first use**, and `HomePlayerStats.vue` (rendered `v-if="connected"` by `HomePage`) still
calls it. So:

- **Connected:** `GET /players/stats` still fires exactly once, from `HomePlayerStats`.
- **Cold:** the hook's `load(null)` returns before touching `mainService` (`usePlayerStats.ts:28-32`,
  *"cold state: the endpoint is never called at all"*), and `HomePlayerStats` is not even mounted.
  **AC12 holds and is not at risk from this change** — but verify it in the Network tab anyway, since
  it is the one AC that a careless "just make the hero fetch its own data" refactor would break.

⚠️ Also check `computed` is still imported and still used after both deletions — `playerLabel`,
`shortAddress`, `featured`, `liveLine` and `primaryLabel` keep it alive, so it stays. Do not remove
it reflexively with the others.

#### 7.6.3 `useLiveGames` and `usePlayerPreferences` stay

- `useLiveGames()` feeds the surviving live line. It also increments the composable's `consumers`
  counter on mount, but `GameTypes.vue` mounts it on the same page, so the focus-refresh behaviour is
  identical either way.
- `usePlayerPreferences()` supplies **both** `displayName` (AC13) and `formatGameAmount` (the live
  line's pot). Neither goes away.
- `useMobileWallet().effectivePublicKey` stays — `shortAddress` needs it, and it is the only correct
  definition of "connected" (never `useWallet().connected`, which is blind to the mobile deeplink
  session a phone actually uses).

#### 7.6.4 The Press Start 2P line-break trap (AC11)

The face advances ~1 em per character, so a headline of *n* characters needs ≈ `n × font-size` of
column width. `FIRST WORD OUT` is 14 characters:

| Breakpoint | Headline size | Needs | Copy column gets | Verdict |
| --- | --- | --- | --- | --- |
| 360–639 px (art hidden) | 19 px | ~266 px | ~332 px @390 / ~302 px @360 | ✅ |
| 640–767 px | 19 px | ~266 px | ~467 px @640 | ✅ |
| 768–1023 px | 22 px | ~308 px | ~470 px @768 | ✅ |
| ≥ 1024 px | 26 px | ~364 px | ~900 px @1366 | ✅ |

The margin is comfortable at every step **because the art no longer takes 566 px of the row** — which
is precisely what made 38 px fail against a ~486 px column in the original build. **If a later change
re-widens the art or re-introduces a `basis-[…]` on either column, this table must be recomputed.**
Leave it as a comment above the `h1`, as the current file does.

#### 7.6.5 `arcade.css` — the `.hero-mascot` housekeeping item is already done, but a new one appears

⚠️ **The card's trap is stale.** `home-dashboard` §7.11.6 flagged that `arcade.css`'s light-graphics
kill block still named `.hero-mascot`, a class no component rendered. **It was already repointed** —
`arcade.css:1105-1112` now targets `.character-img` / `.pixelated-card:hover .character-img`, and its
comment explains the history. There is **no dead `.hero-mascot` selector left to delete**; AC19's
second clause is satisfied by inspection, and the implementer should say so rather than inventing an
edit.

**What *does* need editing is that block's comment**, which names a class this change deletes:

```css
/* …
   NOT the starting page's saucer: `.hero-ship`'s glow is a flat drop-shadow with no blurred backdrop
   and no animation, and it is the one piece of character art on the front door — it stays. (This rule
   used to name `.hero-mascot`, a class no component has rendered since the art became the ship.) */
```

Rewrite it to name **`.hero-art`** and the mode sprite. Leaving it is exactly the failure §7.11.6
warned about — a comment that looks like it protects the hero and refers to a class that no longer
exists. This is the whole of AC19's first clause, and the **only** `arcade.css` edit this card needs.

#### 7.6.6 `HomePage.vue`'s wrapper is load-bearing (AC17)

```html
<div class="relative z-[2] mx-auto w-full max-w-[1180px] px-3.5 pb-20 md:px-10 md:pb-28">
```

- `relative z-[2]` exists because `App.vue`'s two starfields are `fixed` with `z: auto` and come
  earlier in the DOM, so unpositioned content paints **under** them. Every other page is saved by an
  `.arcade-panel` (`position: relative`); the frameless hero is not.
- `pb-20 md:pb-28` clears `App.vue`'s **fixed** seam vignette, which fades the bottom ~12 % of the
  viewport at every scroll position.

**Do not touch either.** `HomePage.vue` is expected to need **no change at all** — the `@play` /
`@practice` contract is unchanged (both emits survive, so both handlers survive, AC16), and the
hero↔picker rhythm is handled inside the hero's own `pb-*`. If it turns out to need one, it is a
padding tweak and nothing else.

### 7.7 The height budget — why the numbers in §7.2–7.4 are what they are

At the 1366 px viewport, with the container at `max-w-[1180px]` and `md:px-10`, the content row is
1100 px wide. The art is 153 px, `gap-10` is 40 px, so the copy column is ~907 px.

**Cold column:**

| Element | Height |
| --- | --- |
| `h1`, 2 lines @ 26 px × 1.35 | 70 px |
| `gap-4` | 16 px |
| sub, 1 line @ 21 px × 1.3 | 27 px |
| `gap-4` | 16 px |
| buttons (`--ctl-h-lg` = 48 px) | 48 px |
| **copy total** | **~177 px** |

**Connected column:**

| Element | Height |
| --- | --- |
| eyebrow @ 9 px × 1.4 | 13 px |
| `gap-1` | 4 px |
| name @ 20 px × 1.35 | 27 px |
| `gap-4` | 16 px |
| live line, 1 line @ 21 px × 1.3 | 27 px |
| `gap-4` | 16 px |
| buttons | 48 px |
| **copy total** | **~151 px** |

Art is 140 px — **below both**, so the copy column drives the band in either state (AC8 ✅).

| | Padding | Content | **Band** | vs ~531 px baseline |
| --- | --- | --- | --- | --- |
| Cold | 72 px | 177 px | **~249 px** | **47 %** ✅ |
| Connected | 72 px | 151 px | **~223 px** | **42 %** ✅ |

**Fold reach at 1366×768** (viewport, per §7.1): sticky header 64 px + band ~249 px = 313 px; the
picker header block is ~79 px (`eyebrow` 13 + `gap-2.5` 10 + `h2` @22 px × 1.4 ≈ 31 + `md:mb-6` 24),
so the first card's top edge lands at **~392 px** — roughly **375 px of headroom** inside a 768 px
viewport.

That headroom is the point: a **real** maximized 1366×768 window has only ~640–660 px of viewport
after browser chrome, and the card's AC is phrased as "browser window". At ~392 px the cards clear
that too, with ~250 px to spare. **If the measured result leaves less than ~100 px of slack at a
768 px viewport, the design is too tall — trim the padding before trimming content.**

⚠️ These are *derived* numbers. Line boxes, font metrics and the wallet cluster's real rendered
height will move them by a few px, and a two-line live line (a long game name) adds ~27 px to the
connected state. Verify with the §7.1 script after the edit; the ACs are measured, not calculated.

---

## 8. Out of scope

- **The `/game` lobby.** It renders `GameTypes` **non-`bare`** with its own `GAME SELECTION` banner
  and no hero. It is the URL `EMBEDDING.md` points integrators at — untouched (AC20).
- **The picker cards themselves** (`GameTypeCard.vue`, `gameArt.ts`) and the picker header — settled
  in `home-dashboard` §7.12 / decisions 37–47.
- **Section order below the hero** and the sections themselves (`HomeHowItWorks`, `HomeFeatureCards`,
  `HomePlayerStats`, `HomeFloor`, `HomeFooter`).
- **Replacing `game-modes.gif` with purpose-drawn hero art.** The card says *"temp"*; a real asset is
  a later card.
- **`/how-to-play`'s use of the same sprite** — unchanged.
- **The general light-graphics policy.** Decision 9 is a scoped exception for one 44.8 KB sprite, not
  a repeal of `specs/light-graphics-mode.md` §7.10.
- **Deleting `edson-ship.gif` / `.png`** — decision 10.
- **Backend, DB, SignalR, endpoints, analytics events, rate limits.**

---

## 9. File checklist

### 9.1 Backend (`monkify-backend`)

**None.** No worktree, no branch, no commit in this repo.

### 9.2 Frontend (`monkify-front`) — branch `feature/home-hero-trim`

| # | File | Change |
| --- | --- | --- |
| 1 | `src/components/home/HomeHero.vue` | The whole card. Section padding `pt-10 pb-14 md:pt-20 md:pb-24` → `pt-6 pb-8 md:pt-8 md:pb-10`; drop both `basis-[…]`; art → `game-modes.gif` at `h-[110px] md:h-[140px] w-auto`, `shrink-0`, `hidden … sm:flex`; delete the `usePerfMode` swap + both `edson-ship` imports; delete the streak block + `usePlayerStats`/`stats`/`streak`/`SEGMENTS`/`segments`; drop the cold fine print; retype the headline/sub/greeting/name/live line per §7.3–7.4; `.hero-ship` → `.hero-art` with re-tuned radii. **Update the file's header comment**: it currently documents the ship, the large art and the perf-mode swap as the design — all three are reversed here (§1.1) |
| 2 | `src/assets/styles/arcade.css` | **Comment only** (≈ lines 1105–1108): the light-graphics character-art block names `.hero-ship`; repoint it to `.hero-art` + the mode sprite. The selector list itself is already correct (§7.6.5) — do not add a `.hero-mascot` rule |
| 3 | `src/pages/HomePage.vue` | **Expected: no change.** Listed only so the implementer confirms the emits and the `relative z-[2]` / `pb-*` wrapper are untouched (AC16, AC17) |

Not edited, confirmed by inspection: `useLiveGames.ts`, `useGamePicker.ts`, `usePlayerStats.ts`,
`usePerfMode.ts`, `GameTypes.vue`, `GameTypeCard.vue`, `src/assets/edson-ship.*`.

### 9.3 Meta (`monkify-meta`) — branch `feature/home-hero-trim`

| # | File | Change | When |
| --- | --- | --- | --- |
| 1 | `specs/home-hero-trim.md` | This spec | create-spec (done) |
| 2 | `INDEX.md` | Row for this spec + "Feature currently in flight" | create-spec (done) |
| 3 | `features/home-dashboard.md` | **At ship.** Rewrite the hero rules this card overturns: the *"the hero states what is happening"* bullet keeps its live line + name-vs-address rules but loses the streak clause; add the trimmed band (frameless, short, mode-sprite art, no perf-mode swap) and the **scoped `light-graphics-mode.md` §7.10 exception**. The `Key code touchpoints` list is unchanged (same files) | ship |
| 4 | `specs/home-dashboard.md` | **At ship.** Add a *Post-ship note* to §7.11.5 / decisions 23, 28, 29 recording that the padding raise and the dominating saucer were reversed by this card, with a pointer here — do **not** rewrite the historical decisions | ship |
| 5 | `INDEX.md` | **At ship.** Status Ready → Shipped | ship |

---

## 10. Tests

**No unit tests and no integration tests** (`constraints.md` §3 — Minor change, frontend-only; no
backend behaviour changes and `monkify-front` has no test runner). Nothing in `SpaceStroker.Tests` or
`SpaceStroker.Tests.Integration` is touched, and there is nothing to report at the end of
implementation under `AGENTS.md` rule 3.

Verification is the §11 acceptance criteria, gated by:

```bash
cd .worktrees/home-hero-trim/monkify-front
yarn lint      # must be clean — the two orphan sets in §7.6.1/§7.6.2 fail here
yarn build     # must succeed
```

⚠️ Run `unix2dos` on every edited file before `yarn lint` if any tool wrote LF line endings, or
prettier reports `Insert ␍` on every line.

---

## 11. Acceptance criteria

Copied 1:1 from the Trello checklist (21 items).

1. **AC1** — Given the current build, when work starts, then the hero `<section>`'s rendered height is
   measured and recorded for **both** states at 1366×768 (cold, and connected-with-a-live-round-and-streak),
   **before any edit**, so AC2 is verifiable. *(§7.1)*
2. **AC2** — Given a 1366×768 browser window, when `/` renders in either state, then the hero
   section's height is at most **50 %** of the AC1 baseline for that same state. *(§7.7)*
3. **AC3** — Given a 1366×768 browser window and no wallet connected, when `/` renders, then
   `+ PICK YOUR GAME +` and the **top edge of the mode cards** are both visible without scrolling.
4. **AC4** — Given a 1366×768 browser window and a connected player in the **tallest** connected case
   (a live round line plus a streak of at least 1, if both survive the redesign), when `/` renders,
   then `+ PICK YOUR GAME +` and the top edge of the mode cards are both visible without scrolling.
   *(The streak does not survive — decision 11 — so the tallest connected case here is a live round
   line, ideally one whose game name wraps it to two lines.)*
5. **AC5** — Given a 390×844 portrait phone, when `/` renders in either state, then
   `+ PICK YOUR GAME +` is reached with at most one short scroll (soft bar).
6. **AC6** — Given `/` in either state, when the hero renders, then it draws **no** border, panel or
   frame of its own — not wrapped in `.arcade-frame` or `.arcade-panel`, and still the only unframed
   section on the page.
7. **AC7** — Given the hero renders art, when the page loads, then the image is
   `@/assets/guide/game-modes.gif` and **neither `edson-ship.gif` nor `edson-ship.png` is imported by
   any component**.
8. **AC8** — Given a 1366×768 desktop window, when the hero art renders, then it is at most ~200 px
   tall and is **not** the element that determines the band's height (removing it changes the band's
   height by less than its own height). *(§7.7: art 140 px vs copy 151–177 px)*
9. **AC9** — Given `html.light-graphics` is active (any phone, or hardware acceleration off), when the
   hero renders, then `game-modes.gif` plays as the **animated GIF** — no still-frame swap, no
   `usePerfMode` branch on the hero art (accepted, scoped exception to `specs/light-graphics-mode.md`
   §7.10, matching `/how-to-play`). *(Verify on desktop with HW acceleration off — below `sm` the art
   is not rendered at all, §7.5.2.)*
10. **AC10** — Given `html.light-graphics` is active, when the hero renders, then **no** element in it
    uses `backdrop-filter`, `blur()`, `mix-blend-mode` or an infinite CSS animation; any glow on the
    art stays a flat `drop-shadow`.
11. **AC11** — Given the cold state at **every breakpoint from 360 px to 1920 px**, when the headline
    renders, then it never breaks onto **three or more** lines. *(§7.6.4)*
12. **AC12** — Given no wallet is connected, when `/` renders, then **no request to
    `GET /players/stats`** is issued — the cold state stays wholly static (verified in the Network tab).
13. **AC13** — Given a connected player, when `/` renders, then the hero shows their `displayName`,
    falling back to the **short** address when they never set one — never an empty label and never a
    raw full public key.
14. **AC14** — Given the redesigned hero keeps the live round line, when the `useLiveGames` snapshot is
    in flight, has failed, or the floor is genuinely empty, then the **same quiet line** renders in all
    three cases — no spinner, no error state, and never a figure rendered as `0`.
15. **AC15** — Given the hero exposes any control that enters a game, when it is clicked, then it routes
    through **`useGamePicker.chooseGame`** — the game is selected, `game_type_selected` fires **once**,
    the route changes and `updateNewGame(true)` runs — identical to clicking a mode card.
16. **AC16** — Given the hero keeps a Practice control, when it is used, then it still reaches
    `/game/practice` via `HomePage`'s `startPractice`; and if any emit (`play` / `practice`) is
    removed, then its `HomePage` handler is removed with it and no dead handler remains.
17. **AC17** — Given the hero is scrolled through at any position on `/`, when the page paints, then the
    hero renders **above** `App.vue`'s fixed starfields and clear of the fixed seam vignette —
    `HomePage.vue`'s `relative z-[2]` and bottom padding are unchanged.
18. **AC18** — Given the perf-mode swap is removed from the hero, when `yarn lint` runs, then it passes
    with **no** unused-import or unused-variable error (`usePerfMode`, `shipStill`, `shipSrc` are
    **gone**, not merely unreferenced), and `yarn build` succeeds. *(Extend to the streak's orphan set —
    §7.6.2.)*
19. **AC19** — Given the hero's art element is renamed or restructured, when the styles are reviewed,
    then **no scoped rule targets a class the component no longer renders**, and `arcade.css`'s dead
    `.hero-mascot` selector is either repointed or deleted. *(§7.6.5 — the selector was already
    repointed to `.character-img`; what this change owes is the **comment** that still names
    `.hero-ship`.)*
20. **AC20** — Given `/game` is loaded, when the lobby renders, then it is **unchanged** — the
    non-`bare` `GAME SELECTION` banner and its card grid, with no hero.
21. **AC21** — Given the change is merged, when `monkify-meta` is read, then `features/home-dashboard.md`
    no longer describes the hero rules this card overturns (the raised padding, the dominating saucer,
    the light-graphics still-frame swap) and records the new band plus the scoped §7.10 exception.

---

## 12. Manual QA

`monkify-front` alone is enough for everything except the connected states, which need a wallet and
(for AC4/AC14) a live round — i.e. the local Docker stack.

1. **Baseline first (AC1).** Before editing: serve the worktree, viewport 1366×768, run the §7.1
   script in both states, record the numbers.
2. **Cold, desktop (AC2, AC3, AC6, AC11, AC12).** Reload `/` with no wallet. Re-run the script; check
   the ratio. Confirm the picker heading **and** the first card's top edge are on screen. Confirm no
   border around the band. Network tab: **no** `/players/stats`. Sweep the width 360 → 1920 and watch
   the headline stay at two lines.
3. **Connected, desktop (AC2, AC4, AC13, AC14, AC15, AC16).** Connect a wallet with a display name;
   then repeat with one that has none (expect the short address). With a round in `WaitingBets` and
   players in it, confirm the live line and the `JOIN {NAME}` CTA; click it and confirm it lands on
   `/game/play?type=…` with the game selected (the round it names, not another). With nothing live,
   confirm the quiet line and that `PLAY AGAIN` scrolls to the picker. Click `Practice` → `/game/practice`.
4. **Phone (AC5, AC11).** 390×844 portrait: the picker heading within one short scroll, no art in the
   band, headline at two lines. Rotate to landscape and re-check.
5. **Light graphics (AC9, AC10).** Desktop Chrome with hardware acceleration **off** (or force
   `html.light-graphics`): the sprite still **animates**; devtools computed styles show no
   `backdrop-filter` / `blur()` / `mix-blend-mode` / infinite animation anywhere in the band.
6. **Lobby untouched (AC20).** `/game` renders the `GAME SELECTION` banner and the grid, no hero.
7. **Gates.** `yarn lint` clean, `yarn build` succeeds (AC18).

---

## 13. Trello / pipeline

- **Source card:** [#138 — trim down hero card in home page](https://trello.com/c/U1Sdsk6Y) — moved
  to **Specs created**; the `## Implementation` block on the card carries branch, spec path,
  worktree and repos.
- **Worktrees:** `.worktrees/home-hero-trim/monkify-front`, `.worktrees/home-hero-trim/monkify-meta`
  — branch `feature/home-hero-trim`, both pushed to `origin`.
- **Next:** `/monkify-implement-feature trim down hero card in home page`, then
  `/monkify-ship-feature trim down hero card in home page` (which syncs §9.3 items 3–5 and merges).

---

## 14. Implementation notes — what shipped differs from §7

Recorded at implementation time (2026-08-03). Everything here is **in addition to or instead of** the
design pass in §7.2–7.7; the ACs in §11 all still hold and were re-measured after each change.

### 14.1 Measured baseline (AC1)

At a **1367×768 viewport** on `feature/home-hero-trim` before any edit:

| | Value |
| --- | --- |
| `heroHeightCold` | **531 px** |
| `heroHeightConnected` | **531 px** (derived: 176 px padding + `max(296 px copy, 355 px art)`) |
| Cold copy column / connected copy column / art | 296 px / 296 px / **355 px** |
| `pickerHeadingTop` / `firstCardTop` | 617 px / 672 px |

The art drove the band in **both** states, which is what makes the connected derivation sound (a real
wallet connection cannot be automated). §7.1's ~531 px estimate was exact. **AC2 target: ≤ 265 px.**

### 14.2 `max-w-[38ch]` → `max-w-[62ch]` on the cold sub (§7.3 corrected)

§7.3 kept the sub's `max-w-[38ch]` while §7.7's budget counted that line as **one line**. Both cannot be
true: 38ch wraps the 57-character sentence in the now-907 px column, which measured **277 px = 52.2 %
and failed AC2**. The cap was written when the column was ~486 px wide; at 907 px it no longer protects
a measure, it just buys a wrap. At 62ch the band is **250 px**, i.e. §7.7's predicted ~249 px.

### 14.3 Page-wide vertical rhythm (owner request, mid-implementation)

The 24 px inter-section gap read as one continuous stack on a wide monitor (the panels are capped at
`max-w-[1180px]` and never grow, so the gap is the only dimension that can breathe). `HomePage.vue`'s
stack is now `gap-6 md:gap-10 xl:gap-12 2xl:gap-16` and the picker's inner gap `gap-5 md:gap-6`.

The hero is frameless, so **its `pb-*` IS the gap to the picker** and has to match that scale — but its
total padding is capped by AC2 (the copy column alone is 178 px). It is therefore **bottom-weighted, not
symmetric**: `pt-6 pb-10 md:pb-12 2xl:pb-16` — 24/48 px, the *same 72 px total* as a symmetric
`pt-8 pb-10`, just spent where the page needs it. ⚠️ `pt-8 pb-14` is 88 px and **fails AC2**.

### 14.4 The connected state is now TWO states (owner request, mid-implementation)

Split on `isNewPlayer` = a **settled** `stats.roundsPlayed === 0`. Decision 11's removal of
`usePlayerStats` from the hero is **reversed** — the streak is still gone, but `roundsPlayed` /
`mostPlayedTypeName` now drive the band. No new request: it is a module singleton `HomePlayerStats`
already loads (verified: exactly one `GET /players/stats` when connected, **zero when cold — AC12 holds**).

| | Round live | Floor quiet |
| --- | --- | --- |
| **New (0 rounds)** | `NEW HERE` · `[JOIN {NAME}] [TRY IT FREE]` | `NEW HERE` · `[TRY IT FREE] [HOW IT WORKS]` |
| **Returning** | `WELCOME BACK` · `[JOIN {NAME}] [GAME HISTORY]` | `WELCOME BACK` · `[PLAY {most-played}] [GAME HISTORY]` |

- **Primary is decided by the floor, secondary by the player.** `TRY IT FREE` is the newcomer's
  constant (primary when there is nothing to join, secondary when there is, so `HOW IT WORKS` only
  appears when it is not needed above); `GAME HISTORY` is the regular's constant.
- **`Play again` and the `play` emit are GONE**, and with them `HomePage`'s `scrollToModes`, the
  `modesSection` ref and its `ref` import (AC16). That button's only job was scrolling to mode cards
  ~48 px below it. **Nothing in this band may be a scroll-to-what-you-can-already-see.**
- An **empty `primaryLabel` renders no button at all** — the only honest state for a regular whose
  floor is quiet and whose favourite was deactivated.
- ⚠️ **An UNKNOWN `stats` resolves to the RETURNING shape**, never the new one: `stats` is null while
  the read is in flight *and* if it fails, and greeting a regular with `NEW HERE` for a frame is the
  worse lie. Only a settled zero flips it.
- ⚠️ **`usualGame` matches against `gameTypes` (the `/games/active-types` catalogue), NOT
  `liveByType`.** They look interchangeable and are not: `useLiveGames` deliberately drops any type
  whose live-instance call **failed**, so keying off it made a transient backend hiccup look identical
  to a deactivated game and silently ate the CTA. Caught in QA by faking that failure.
- `HomeHero` now emits `practice` / `history` / `how-to-play`; `HomePage` owns all three destinations
  and their `trackHomeLinkClick` links, as it already did for `practice`.
- The **cold** state's `Practice` was relabelled **`TRY IT FREE`** too — same action, and the page's own
  section for it (`HomeFeatureCards`) is already titled TRY IT FREE.

### 14.5 Mobile: centred band + a sentence-boundary line break (owner request)

- The copy column is **`text-center sm:text-left`** with `justify-center sm:justify-start` on both
  button rows and `mx-auto sm:mx-0` on the capped sub. The breakpoint is **not** a taste call: `sm` is
  where the art returns (§7.5.2), so below it the copy is the only thing in the row and full-width,
  and from `sm` up it is a real left column that must align with the picker header and cards below.
- The cold sub carries **`<br class="lg:hidden" />`** on the full stop. Unassisted it broke mid-clause
  (*"…characters. Edson types / until one lands."*). `lg` is where the column finally holds both
  sentences on one line (measured: 1 line at 1024, 2 below). ⚠️ The source newline **after** the `<br />`
  is load-bearing — at `lg`+ the `<br />` is `display:none` and that newline is the only whitespace left
  between "characters." and "Edson".

### 14.6 Final measurements (AC2–AC5, re-verified)

| Viewport | Cold | Connected |
| --- | --- | --- |
| 1366×768 | **249–250 px (47 %)** | **223 px (42 %)**; worst case (2-line live line) 250 px |
| 390×844 | 237 px | 258 px |

`pickerHeadingTop` 336 / `firstCardTop` 391 at 1366×768 — both clear a real maximized window
(~640 px of viewport) with ~250 px to spare. Headline is 2 lines at 360/390/502/640/768/1024/1366/1920.
`yarn lint` clean, `yarn build` green. **`edson-ship.gif`/`.png` are no longer emitted to `dist`** — the
predicted repo-only cost is real (−533 KB / −26 KB off the wire).

### 14.7 Not verified by the implementer

AC13 with a wallet that **has** a display name (the short-address fallback is verified; the
`displayName || shortAddress` line is untouched), and AC9 with Chrome hardware acceleration genuinely
off (verified by forcing `html.light-graphics` instead).

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-08-03 | Initial spec from the refined card (#138, refined 2026-08-03) + code exploration. Design pass pinned as decisions 13–20. Two card traps corrected against the live tree: `arcade.css`'s `.hero-mascot` selector is **already** repointed (only its comment needs the edit, §7.6.5), and dropping the streak orphans a **second** import set the card did not list (§7.6.2). |
| 2026-08-03 | Implemented. Added §14 — the measured AC1 baseline, the `38ch`→`62ch` correction that AC2 forced, and four owner-requested extensions taken mid-implementation (page-wide section rhythm, the new-vs-returning connected split, mobile centring, the sentence-boundary break). Decision 11 partially reversed: `usePlayerStats` is back in the hero for `roundsPlayed`, the streak stays gone. |

# Space Stroker — "App home changes" — Implementation Spec

> **Status: Ready for implementation** — refined via Trello card
> [App home changes](https://trello.com/c/eS4SpiZ6) (#143, refined 2026-08-03).
> Repos affected: `monkify-front`, and this doc + `features/home-dashboard.md` in `monkify-meta`.
> **`monkify-backend` is untouched.**
>
> ⚠️ **Release train.** This card does **not** merge to `main`. Branch `feature/app-home-changes` was
> cut off **`release/ui-ux-changes`** and ships into it, alongside #142 (practice mode changes) and
> #144 (how-to-play changes). `monkify-ship-feature` reads this from the card's `**Release branch:**`
> field.
>
> **Purpose:** a polish pass on the front door at `/`. Five copy/layout corrections the owner listed
> after living with the page — the hero's quiet line, the picker cards' `PLAYERS` chip, the
> how-it-works section's title/beats/button, and a `TRY IT FREE` panel that repeats a CTA the hero
> already carries — plus taking two below-the-fold requests off the page's critical path.

---

## 1. Summary

Six changes in seven files. Nothing here adds a capability; every item corrects something already on
the page.

| # | Change | Where | Kind |
| --- | --- | --- | --- |
| 1 | Hero quiet line → "Place a bet and get ready for the next round." | `HomeHero.vue` | copy |
| 2 | `PLAYERS` chip removed from race/preset picker cards (`/` **and** `/game`) | `GameTypeCard.vue` | UI |
| 3 | Section title → `WANNA LEARN HOW TO PLAY?`, eyebrow dropped, three beat headings + button relabelled | `HomeHowItWorks.vue` | copy |
| 4 | `TRY IT FREE` panel deleted; `PROVABLY FAIR` full width; the `practice` analytics event re-homed | `HomeFeatureCards.vue`, `HomePage.vue` | UI + analytics |
| 5 | `THE FLOOR`'s two reads deferred until the section enters the viewport | `HomeFloor.vue` | requests |
| 6 | The boot catalogue response is reused instead of re-requested | `App.vue`, `GameTypes.vue` | requests |

### 1.1 What this deliberately does **not** do

The card asked to stop calling `GET /games/{gameTypeId}` per game type. **That was considered and
rejected by the owner during refinement.** `useLiveGames` is the single source of the picker's live
layer *and* the hero's live line; §7.7 states the rule and §9 keeps the file off the checklist.

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree per repo** (§2): `.worktrees/app-home-changes/monkify-front` and
  `.worktrees/app-home-changes/monkify-meta`, branch `feature/app-home-changes` in both. The hubs
  stay on `main`; both branches were cut off **`release/ui-ux-changes`**, not `main`.
- **Push after branch work** (`AGENTS.md` rule 5) — `git push -u origin HEAD` on first push.
- **Unit tests** (§3): no backend change ⇒ no `SpaceStroker.Tests` work. `monkify-front` has **no test
  runner**; the gates are `yarn lint` + `yarn build` + manual QA.
- **Integration tests** (§3, "Scope of new feature"): this is a **Minor change** — no new
  `SpaceStroker.Tests.Integration` test, and no dedicated `features/<slug>.md` doc. **The existing
  `features/home-dashboard.md` must still be corrected** (§9.3).
- **User-facing messaging** (§6): the picker never narrates a failure it cannot explain, and never
  renders a live figure as `0`. Both survive here — see §7.2 and §7.5.
- **Secrets/config** (§1): nothing touched.
- ⚠️ **`yarn lint` needs uniform CRLF.** LF-writing tools produce `Insert ␍` across an entire file;
  normalise with `unix2dos` before committing.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | The hero's quiet fallback becomes **"Place a bet and get ready for the next round."** — with the trailing period its three sibling live lines all carry. |
| 2 | Only the **`PLAYERS`** chip is removed. `TICKET`, the lottery `POT` chip and the `FILLING`/`STARTING`/`LIVE NOW` status pill all stay. |
| 3 | The chip removal applies to **both** `/` and `/game`. `GameTypes.vue`/`GameTypeCard.vue` are shared verbatim so the two surfaces can never drift; **no prop-gated split**. |
| 4 | Section title → **`WANNA LEARN HOW TO PLAY?`**; the **`Three beats` eyebrow is dropped**. `HomeSection`'s `eyebrow` prop is already optional — no component change. |
| 5 | The three beat **headings** change (`Place a bet` / `Edson starts (key)stroking` / `First bet typed wins`); the **sub-copy under each stays byte-for-byte**. |
| 6 | The section's button reads **`Learn more`**. Destination and analytics are unchanged. |
| 7 | The **`TRY IT FREE` panel is deleted** and `PROVABLY FAIR` goes full width — post-#138 the cold hero's own second CTA is labelled `Try it free`, so the panel repeats it. |
| 8 | The **`home_link_click` / `practice` event survives** the deletion: the track call moves into `HomePage`'s practice handler, which every practice entry point already routes through. |
| 9 | **The per-type `GET /games/{gameTypeId}` fan-out stays.** Rejected the card's removal request — it feeds the POT chip, the status pill, the picker eyebrow and the hero's live line + Join/Watch CTA. |
| 10 | **`THE FLOOR`'s two reads are deferred** until the section enters the viewport. Its `loading` / `failed` / retry states survive unchanged. |
| 11 | **The walletless `GET /players/preferences` call stays** — it also carries the self-exclusion flags the pre-bet guards read. |
| 12 | The **boot catalogue response is reused** by `GameTypes` instead of re-requested. ⚠️ Corrected mid-spec: this is a **cleanup, not a request saving** — `getGamesTypes` is already `singleFlight`-wrapped (#104), so the two concurrent call sites already collapse into one HTTP request. See §7.6. |
| 13 | **Level: Minor change**, frontend-only. No integration test, no new `features/` doc. |
| 14 | Branch base and ship target are **`release/ui-ux-changes`**, not `main`. #138 landed on `main` (`993261e`) before this branch was cut, so the release branch already contains the trimmed hero — no merge of #138 was needed. |

---

## 4. Data model

**None.** No entity, no column, no EF mapping, no migration.

---

## 5. Configuration

**None.** No `GeneralSettings` key, no `VUE_APP_*` variable, no seed data.

---

## 6. Backend behaviour (`monkify-backend`)

**None.** `GET /games/active-types`, `GET /games/{gameTypeId}`, `GET /games/stats` and
`GET /games/history` are consumed exactly as they are today — only **when** the frontend calls two of
them changes. No controller, handler, DTO, consumer or SignalR contract is touched.

---

## 7. Frontend behaviour (`monkify-front`)

All line numbers are against `release/ui-ux-changes` at `993261e`.

### 7.1 `HomeHero.vue` — the quiet line (AC1)

One string. `liveLine` (currently line 142):

```ts
// before
if (!live) return "Floor's quiet. Be the one who starts a round.";
// after
if (!live) return 'Place a bet and get ready for the next round.';
```

Everything around it is untouched: the `featured` priority order, the three live variants, the
`primaryLabel` / `onPrimary` fallbacks and the two connected shapes are all #138's and stay.

⚠️ **This string renders in the CONNECTED state only.** The trimmed hero's cold state has no live
line, so a first-time visitor never sees either wording. Do not "fix" that by adding the line to the
cold state — the cold band is deliberately static (`specs/home-hero-trim.md` §7.3).

⚠️ **Keep the comment above `liveLine` true.** It says the quiet line is honest while the snapshot is
in flight, when it failed and when the floor really is empty — that reasoning still holds for the new
wording, and it is the reason there is no spinner here.

### 7.2 `GameTypeCard.vue` — drop the `PLAYERS` chip (AC2, AC3)

`liveChip` (currently lines 155–175) keeps the lottery arm and returns `null` for everything else:

```ts
const liveChip = computed(() => {
  const live = props.live;
  if (!live) return null;
  // A race or a preset shows no live chip at all since #143: "N players" read as a scoreboard on a
  // card whose job is to say what the game IS. The FILLING pill above still says a round is alive.
  if (archetypeOf(props.game) !== 'lottery') return null;
  return {
    label: 'Pot',
    text: money(live.pot, props.game),
    coin: showCoin.value,
    dot: false,
    hot: live.pot > 0,
  };
});
```

Consequences to honour:

- **The template does not change.** `v-if="liveChip"` already omits the whole chip, so a race card
  renders one chip (`TICKET`) and reserves no space for a second. The `.chips` flex row is
  `gap: 5px` with no fixed width — nothing collapses.
- **`live.players` is still read**, by `status` (`FILLING` requires `players > 0`), so no import,
  computed or util is orphaned. `utils/players.ts` and `countPlayers` stay.
- **The `.live-dot` / `.live-dot--on` CSS becomes unused** — `dot: false` on the only surviving arm.
  Leave the rules in place: they are 8 lines, they document the "lit only when somebody is actually
  in the round" rule, and a future chip would need them back. Do **not** delete `--color-live`
  (`colors.css`) — `HomeFloor`'s eyebrow dot and other surfaces use it.
- **Both surfaces change.** `/game` renders the same component; its cards lose the chip too. That is
  decision 3, not a regression.

### 7.3 `HomeHowItWorks.vue` — header, beats, button (AC5, AC6, AC7, AC8)

```ts
const steps = [
  { h: 'Place a bet',                p: /* unchanged */ 'Six characters, yours. Anyone can call the same round, nobody can call yours.' },
  { h: 'Edson starts (key)stroking', p: /* unchanged */ 'Live, from a seed committed before the round. Nothing is decided after it starts.' },
  { h: 'First bet typed wins',       p: /* unchanged */ 'His first landed call splits the pot. Two characters short is still short.' },
];
```

```html
<!-- before --> <home-section eyebrow="Three beats" title="HOW A ROUND GOES">
<!-- after  --> <home-section title="WANNA LEARN HOW TO PLAY?">
```

and the action button's label `How to play` → `Learn more`.

- The `eyebrow` prop is optional (`HomeSection.vue`, `withDefaults(..., { eyebrow: undefined })`) and
  the span is `v-if="eyebrow || $slots.eyebrow"`, so dropping it renders nothing and the
  `grid gap-2.5` header block collapses to the title alone. **No `HomeSection` change.**
- The button still `$emit('how-to-play')`; `HomePage.openHowToPlay` keeps firing
  `trackHomeLinkClick({ link: 'how_to_play' })` and routing to `HowToPlay`. **Only the label moves.**
- ⚠️ **Width check (AC7).** `WANNA LEARN HOW TO PLAY?` is 24 characters in Press Start 2P at
  `text-[13px] md:text-[19px]`, plus the two `+` marks and their `gap-2.5/3`. Press Start 2P advances
  ~1 em per character, so at 19 px it wants ~456 px + marks against a ~1116 px panel — fine on
  desktop; at 13 px it wants ~312 px against a ~330 px phone panel, which is **tight**. Check 390 px
  and accept a two-line title there if it wraps at a word boundary; do not shrink the title's font
  below the shared `HomeSection` scale, and do not `white-space: nowrap` it.
- ⚠️ **`EDSON STARTS (KEY)STROKING` is 26 characters** in a `minmax(210px, 1fr)` auto-fit column at
  `text-[10px]` — ~260 px wanted against a 210 px minimum, so it **will** wrap to two lines at three
  columns. That is acceptable (the beats are `content-start` in a grid, so a taller heading does not
  misalign the numbers), but verify the three beat blocks still read as one row and nothing overflows
  its column.
- The component's header comment says the section survived #130 as the page's only unprompted
  onboarding. Still true — keep it, and keep "The guide gets the button" accurate for `Learn more`.

### 7.4 `HomeFeatureCards.vue` + `HomePage.vue` — delete `TRY IT FREE` (AC9, AC10, AC11)

Delete the first `<home-section>` (the `TRY IT FREE` panel), its `practice()` handler and the
`practice` emit. What remains is the `PROVABLY FAIR` panel alone:

- **Drop the two-column grid.** The wrapper's
  `style="grid-template-columns: repeat(auto-fit, minmax(280px, 1fr))"` exists only to sit two panels
  side by side. With one panel left, render it full width — either by removing the wrapper entirely
  (preferred: the panel is already `h-full` and `HomePage`'s grid supplies the section gap) or by
  keeping a `grid gap-*` with no column template. **A one-column `auto-fit` grid is not the same
  thing** — it would still cap the panel at the implicit track and is not worth keeping.
- **The embedded branch must render nothing.** `PROVABLY FAIR` is `v-if="!isEmbedded"`; once it is
  the only child, an embedded render must produce **no empty wrapper**. If the wrapper is removed the
  point is moot; if it is kept, gate the wrapper itself. (Moot in practice — a framed `/` redirects
  to `/game` before `HomePage` mounts — but the rule is kept so a future embedded variant cannot
  regress it.)
- **`useAnalytics` / `trackHomeLinkClick` stays** in this file — the `fair` link still reports it.
  Only the `practice` call and the emit go. Re-check imports after the delete: an orphan fails
  `yarn lint`.
- **Update the component's header comment.** It currently frames the file as "the two objections a
  first-time visitor actually has"; after this there is one.
- **`HomeSection.vue`'s tone comment** names `Try it free` as one of the two cyan panels. Re-point it
  (the surviving cyan section is `YOUR RECORD`) — a comment that names a deleted section is worse
  than no comment.
- **`HomePage.vue`:** drop the `@practice="startPractice"` binding on `<home-feature-cards>` and move
  the tracking into the handler:

```ts
// The ONLY practice entry point on this page since #143 deleted the TRY IT FREE panel — so the
// event that panel used to fire lives here now, and every hero shape reports it identically.
const startPractice = () => {
  trackHomeLinkClick({ link: 'practice' });
  router.push({ name: 'Practice' });
};
```

`trackHomeLinkClick` is already imported in `HomePage.vue` (used by `openHowToPlay` and
`openHistory`), and `'practice'` is already a valid `link` value in `useAnalytics.ts` — **no type
change**.

⚠️ **Do not touch `HomePage.vue`'s `relative z-[2]` wrapper or its bottom padding.** `App.vue`'s two
starfields are `fixed` with `z: auto`, so unpositioned content paints under them, and the padding
clears the fixed seam vignette. The file's own comment says so.

### 7.5 `HomeFloor.vue` — defer the two reads (AC14, AC15, AC16)

Replace `onMounted(load)` with a one-shot `IntersectionObserver` on the section's root element:

```ts
const root = ref<{ $el?: Element } | null>(null);   // <home-section ref="root">, whose root IS a <section>
let observer: IntersectionObserver | null = null;

const stopObserving = () => {
  observer?.disconnect();
  observer = null;
};

onMounted(() => {
  const el = root.value?.$el as Element | undefined;
  // No element, or no IntersectionObserver at all: fall back to loading immediately. A missing
  // figure is a worse failure than an early request.
  if (!el || typeof IntersectionObserver === 'undefined') {
    load();
    return;
  }
  observer = new IntersectionObserver(
    (entries) => {
      if (!entries.some((entry) => entry.isIntersecting)) return;
      stopObserving();   // ONE shot: the reads are all-time aggregates, not a live feed.
      load();
    },
    // Start the reads slightly before the panel is on screen so the figures are there by the time
    // the player has scrolled to them.
    { rootMargin: '200px 0px' }
  );
  observer.observe(el);
});

onBeforeUnmount(stopObserving);
```

Rules this must respect:

- **`loading` stays `true` initially.** ⚠️ **Corrected during implementation** — this spec first said
  to start it `false`, which is wrong: the template's branch order is
  `loading → failed → !hasRounds → rows`, so a `false` initial value falls through to the empty state
  and the section announces **"No rounds have settled yet"** about a floor it has not read. Keeping
  `true` makes the flag mean *"we do not have the floor yet"*, covering both **deferred-not-started**
  and **in flight**, which must render identically. A slightly early `> reading the floor…` is honest;
  a settled-sounding claim nobody checked is not.
- **The three tiles always render.** They already read `stats.value?.x ?? 0` and the section's whole
  reason for existing is that it can never be empty (its header comment). Before the load they show
  `0` / `—`, which is what they show today during the fetch — **no new empty state, no skeleton, no
  reserved-height hack.**
- **Retry still works.** The `failed` branch's `@click="load"` calls the same `load()`; after a
  deferred first attempt the observer is already disconnected, so retry is a plain re-issue.
- **One shot only.** Disconnect on first intersection *before* calling `load()`, so a scroll-away-and-
  back never refires (AC15). `load()` has no in-flight guard of its own.
- **Already-in-view is not a special case.** `IntersectionObserver` fires on registration when the
  element is already intersecting, so a tall viewport or a short page loads immediately — correct.
- ⚠️ **Do not copy `HowToPlayPage.vue`'s scroll-listener idiom.** That file replaced an
  `IntersectionObserver` on purpose, because IO cannot express *"which section is most visible"*.
  This is the plain "has it appeared" question IO is actually for, and the objection there does not
  apply.

### 7.6 `App.vue` + `GameTypes.vue` — reuse the boot catalogue (AC12, AC13)

**Corrected premise, recorded on purpose.** Refinement claimed `GET /games/active-types` fires twice
per load. It does **not** reach the network twice: `mainService.getGamesTypes` is wrapped in
`singleFlight` (`specs/player-lockouts.md`, #104), whose post-ship measurement states *"the lobby
never duplicated (1:1)"*. The two call sites are concurrent in practice — `GameTypes` (a child) mounts
and fires, `App.vue`'s boot probe fires in the same tick — so they share one promise.

What is left is genuine but modest, so scope it honestly:

1. **`App.vue`** — assign the probe's result into the shared store instead of discarding it. The
   result is currently thrown away (`await runWithRecovery(() => mainService.getGamesTypes())`);
   `useGame`'s `gameTypes` is a **module-level `ref`** (`useGame.ts:39`), so writing it there is the
   whole mechanism.
2. **`App.vue`** — drop the outer `runWithRecovery` wrapper. `mainService.getGamesTypes` already
   applies `runWithRecovery` internally (`mainService.ts:78-86`), so the call site double-wraps it
   and gets two nested recovery loops over one request. Keep the surrounding `try/catch` exactly as
   it is — a terminal failure must still fall through to the same comment's behaviour.
3. **`GameTypes.vue`** — skip its own fetch when the store is already populated:

```ts
onMounted(async () => {
  try {
    // The boot probe (App.vue) usually filled this already; re-requesting it is dead weight even
    // though singleFlight (#104) keeps a concurrent double-call down to one HTTP request. A cold
    // mount from a non-player route still fetches — see isPlayerRoute() in App.vue.
    const types = gameTypes.value?.length ? gameTypes.value : (await mainService.getGamesTypes()).data;
    gameTypes.value = types;
    refreshLive(types);            // still NOT awaited: a live figure is an enrichment, never a gate
  } catch (error: unknown) {
    /* unchanged: mark handled, toast, console.error */
  } finally {
    loading.value = false;
  }
});
```

Rules this must respect:

- **`GameTypes` must still be able to fetch for itself.** The boot probe only runs for
  `isPlayerRoute()` (`App.vue`), so arriving from `/wallet-bridge`, `/admin` or `/error` leaves the
  store empty — and a failed probe leaves it empty too. The `toast.error` path stays exactly as it is
  (AC13).
- **`refreshLive(types)` runs in both branches.** It is what feeds `useLiveGames`; skipping it on the
  reuse path would silently kill the live layer this card is otherwise preserving.
- **`loading` must end `false` on the reuse path** — hence the shared `finally`.
- **No cross-navigation cache semantics are introduced.** The store already lives for the page load;
  a full reload refetches. Do not add a TTL, and do not make `GameTypes` a no-op on later mounts
  beyond the populated-store check.

### 7.7 What must NOT change

- **`useLiveGames.ts`** — untouched (decision 9). If the diff contains this file, the card was
  misread.
- **`GameTypeCard`'s status pill, `TICKET` chip, art, accents, tag, description and hover GIF** —
  settled in `specs/home-dashboard.md` §7.12.
- **The hero's layout, height, art, CTAs and its two connected shapes** — `specs/home-hero-trim.md`.
- **Section order on `/`**, `HomePlayerStats`, `HomeFooter`, `HomeSection`'s markup.
- **`THE FLOOR`'s content, tiles, formatting and its `Full stats` / `All history` buttons** — only
  *when* it loads changes.
- **The walletless `/players/preferences` call** and the header's balance poll.

---

## 8. Out of scope

- Removing the per-type `GET /games/{gameTypeId}` fan-out (decision 9).
- `/how-to-play`'s own content — **#144** owns it.
- Practice mode's behaviour — **#142**.
- The hero sprite — **#145** (blocked on real art).
- Any backend, DB, SignalR, endpoint or new analytics event.
- Landing the release train: `release/ui-ux-changes` → `main` is a separate ship
  (`/monkify-ship-feature release/ui-ux-changes`), after #142/#143/#144 are all on it.

---

## 9. File checklist

### 9.1 Backend (`monkify-backend`)

**None.** No files.

### 9.2 Frontend (`monkify-front`) — branch `feature/app-home-changes`

| # | File | Change |
| --- | --- | --- |
| 1 | `src/components/home/HomeHero.vue` | `liveLine`'s quiet fallback → `'Place a bet and get ready for the next round.'` (one string; §7.1) |
| 2 | `src/components/game-selection/GameTypeCard.vue` | `liveChip` returns `null` for non-lottery archetypes; lottery `POT` arm unchanged; template untouched (§7.2) |
| 3 | `src/components/home/HomeHowItWorks.vue` | Title → `WANNA LEARN HOW TO PLAY?`, `eyebrow` prop removed, three `steps[].h` values, button → `Learn more`; sub-copy untouched (§7.3) |
| 4 | `src/components/home/HomeFeatureCards.vue` | Delete the `TRY IT FREE` section + its handler/emit; `PROVABLY FAIR` full width; header comment re-written (§7.4) |
| 5 | `src/components/home/HomeSection.vue` | **Comment only** — the tone note names the deleted `Try it free` panel (§7.4) |
| 6 | `src/pages/HomePage.vue` | `startPractice` fires `trackHomeLinkClick({ link: 'practice' })`; drop `@practice` on `<home-feature-cards>` (§7.4) |
| 7 | `src/components/home/HomeFloor.vue` | `onMounted(load)` → one-shot `IntersectionObserver` + `loading` starts `false` (§7.5) |
| 8 | `src/App.vue` | Boot probe assigns into `gameTypes`; drop the redundant outer `runWithRecovery` (§7.6) |
| 9 | `src/components/game-selection/GameTypes.vue` | Reuse a populated `gameTypes`; still fetch when empty; `refreshLive` on both paths (§7.6) |

### 9.3 Meta (`monkify-meta`) — branch `feature/app-home-changes`

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/app-home-changes.md` | This spec |
| 2 | `INDEX.md` | Map row for this spec + "Feature currently in flight" |
| 3 | `features/home-dashboard.md` | **At ship:** "The picker card" loses the `PLAYERS` chip rule; the section list loses `TRY IT FREE`; "Live figures" gains the deferred-FLOOR + one-fetch-catalogue rules; the how-it-works section's title/button copy is updated |

---

## 10. Tests

- **Backend unit tests:** none — no backend change.
- **Integration tests:** none. **Minor change** (`constraints.md` §3): not a new feature, no game-flow
  change.
- **Frontend:** `monkify-front` has no test runner. Gates are `yarn lint` (⚠️ uniform CRLF) and
  `yarn build`, plus the manual QA in §12.

---

## 11. Acceptance criteria

Copied 1:1 from the Trello checklist (#143).

1. **AC1** — Given a connected player on `/` and no round in WaitingBets-with-players / GameStarting /
   InProgress, when the hero renders its live line, then it reads "Place a bet and get ready for the
   next round." and the old "Floor's quiet…" string exists nowhere in the repo.
2. **AC2** — Given a live round with players on a race or preset game type, when its picker card
   renders on `/` or on `/game`, then no "PLAYERS" chip is shown, while the "TICKET" chip and the
   "FILLING"/"STARTING"/"LIVE NOW" status pill still render exactly as before.
3. **AC3** — Given a lottery game type with a live snapshot, when its picker card renders, then the
   "POT" chip is still shown with its value and coin, and the single-chip HUD sits correctly over the
   artwork's bottom-right with no empty gap where the PLAYERS chip used to be.
4. **AC4** — Given at least one joinable round with players, when the picker header renders on `/`,
   then its eyebrow still reads "N games · M round(s) filling now".
5. **AC5** — Given a visitor on `/`, when the how-it-works section renders, then its title reads
   "WANNA LEARN HOW TO PLAY?", no eyebrow is rendered above it, and the section still renders inside
   the same `.arcade-frame` panel as its neighbours.
6. **AC6** — Given the how-it-works section, when its three beats render, then their headings read
   "PLACE A BET", "EDSON STARTS (KEY)STROKING" and "FIRST BET TYPED WINS", each numbered 01–03 as
   today, and the sub-copy under each is byte-for-byte the copy that shipped.
7. **AC7** — Given a 390×844 phone and a 1366×768 desktop, when the how-it-works beats render, then
   "EDSON STARTS (KEY)STROKING" wraps without overflowing its column and the three beat blocks stay
   aligned on one row at desktop width.
8. **AC8** — Given the how-it-works section, when the player clicks its action button, then the button
   reads "Learn more", `home_link_click` fires with `link: 'how_to_play'`, and the app routes to
   `/how-to-play`.
9. **AC9** — Given a visitor on `/`, when the page renders, then no "TRY IT FREE" section exists
   anywhere on it and "PROVABLY FAIR" occupies the full content width (no empty second column, no
   half-width panel) at 1366×768 and at 390×844.
10. **AC10** — Given a visitor on `/`, when they click the hero's "Try it free" button, then
    `home_link_click` fires with `link: 'practice'` and the app routes to `/game/practice` — the event
    that the deleted TRY IT FREE panel used to emit is still emitted exactly once per click.
11. **AC11** — Given the "Verify a round" button on the now-full-width PROVABLY FAIR panel, when it is
    clicked, then `home_link_click` fires with `link: 'fair'` and the header's shared Fair modal opens
    (one instance, one backdrop) exactly as before.
12. **AC12** — Given a cold load of `/` with the network panel open, when the page has finished its
    initial render, then `GET /games/active-types` appears exactly ONCE (not twice) and the picker
    still renders its cards.
13. **AC13** — Given the boot catalogue request fails (offline or 500), when `GameTypes` mounts on `/`
    or `/game`, then it fetches the catalogue itself and, if that also fails, shows its existing error
    toast — a failed boot probe never leaves a permanently empty picker.
14. **AC14** — Given a cold load of `/` where the player never scrolls, when the network panel is
    inspected, then neither `GET /games/stats` nor `GET /games/history?take=6` has been sent, and THE
    FLOOR's three static tiles still occupy their normal space with no layout jump.
15. **AC15** — Given a cold load of `/`, when the player scrolls THE FLOOR into view, then both
    `GET /games/stats` and `GET /games/history?take=6` fire exactly once, the figures and round rows
    populate, and scrolling away and back does not refire them.
16. **AC16** — Given THE FLOOR is scrolled into view and both reads fail, when the section renders,
    then it shows "> floor offline — retry" and clicking retry re-issues both requests successfully.
17. **AC17** — Given a cold load of `/` with no wallet, when the requests before any scroll are
    counted, then there are 5 (down from 8): one `/games/active-types`, three `GET /games/{id}`, one
    `/players/preferences`.
    ⚠️ **Amended by §7.6:** the pre-change count is **6, not 8**, because `singleFlight` already
    collapsed the catalogue pair. The post-change target of **5** before scroll is unchanged and is
    what this AC verifies; record the measured before/after counts either way.
18. **AC18** — Given a connected player with a live joinable round, when the hero renders after this
    card, then its live line, its Join/Watch primary CTA and the picker's live chips/pill all still
    work — `useLiveGames.ts` is unmodified by this card's diff.
19. **AC19** — Given the branch is cut, when its history is checked, then #138's hero trim is already
    in it — the work is based on `release/ui-ux-changes` (which contains `main` at `993261e`), never
    on a base predating #138.
20. **AC20** — Given the finished branch, when `yarn lint` and `yarn build` are run in
    `monkify-front`, then both pass with no unused-import/`no-unused-vars` errors and no CRLF
    (`Insert ␍`) failures.
21. **AC21** — Given the card ships, when `monkify-meta/features/home-dashboard.md` is read, then it
    no longer describes the PLAYERS chip, the TRY IT FREE section or THE FLOOR's on-mount reads, and
    states the one-fetch catalogue rule instead.

---

## 12. Manual QA

Run against the local stack (or any environment with the three seeded Main game types).

1. **Baseline first.** Open `/` cold (no wallet, empty cache), network panel filtered to XHR, and
   **record the request list before any change** — that is AC17's "before" number and it is
   unverifiable afterwards.
2. **Cold `/`** — confirm: no `TRY IT FREE` panel, `PROVABLY FAIR` full width, the how-it-works title
   and its three beats, `Learn more` on the button. Click it → `/how-to-play`.
3. **No scroll** — confirm `/games/stats` and `/games/history` are absent from the network list;
   THE FLOOR's tiles render with no gap or jump.
4. **Scroll to THE FLOOR** — both requests fire once, figures + rows populate. Scroll up and back:
   no refire.
5. **Offline retry** — throttle to offline, hard-reload, scroll to THE FLOOR → `> floor offline —
   retry`; go back online and click retry → it populates.
6. **Picker cards** — on `/` and on `/game`: no `PLAYERS` chip on Cosmic Sprint / Beast Mode, `POT`
   still on Space Jackpot, `TICKET` on all three. With a bet placed in a round, the `FILLING` pill
   still appears and the picker eyebrow still counts it.
7. **Connected hero** — connect a wallet with no live round and confirm the new quiet line; then with
   a filling round confirm the live line and that `Join <game>` still enters through the picker
   handler.
8. **Practice** — click the hero's `Try it free`; confirm `home_link_click` / `practice` in the GA
   debug view (or a `gtag` console spy) and the route change.
9. **Responsive** — repeat 2 and 6 at 390×844 and 1366×768.

---

## 13. Trello / pipeline

- **Source card:** [App home changes](https://trello.com/c/eS4SpiZ6) (#143) — list **Specs created**;
  the card's **Implementation** section carries branch, worktree, spec path, level and the release
  branch.
- **Siblings on the same train:** #142 (practice mode changes), #144 (how-to-play changes).
- **Next:** `/monkify-implement-feature App home changes`, then `/monkify-ship-feature App home
  changes` (merges into `release/ui-ux-changes` and leaves the card in **Implemented**). The train is
  landed separately with `/monkify-ship-feature release/ui-ux-changes`.

---

## 14. Revision log

| Date | Change |
| --- | --- |
| 2026-08-03 | Initial spec from refined card #143. Corrects the card's "duplicate `/games/active-types`" premise (§7.6, decision 12, AC17 amendment) — `singleFlight` (#104) already collapses it. Records that #138 landed on `main` before this branch was cut, so the release train needed no merge. |

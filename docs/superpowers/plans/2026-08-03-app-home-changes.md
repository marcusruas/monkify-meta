# App home changes (#143) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apply the #143 polish pass to the app's front door `/` — five copy/layout corrections plus
two request changes — without touching the live layer the page depends on.

**Architecture:** Six independent edits across nine files in `monkify-front`, each self-contained and
individually revertable. No shared abstraction is introduced; nothing is extracted. Two of the six
change *when* an existing request fires, never *what* it returns.

**Tech Stack:** Vue 3 (`<script setup>`, Composition API), TypeScript, Tailwind (utility classes in
templates, scoped CSS for card chrome), Vue CLI 5 (webpack), ESLint + Prettier, Yarn 1.

## Global Constraints

- **Repo:** `monkify-front` only. `monkify-backend` is untouched — no entity, migration, endpoint,
  DTO or SignalR change.
- **Worktree:** `.worktrees/app-home-changes/monkify-front`, branch `feature/app-home-changes`, cut
  off `release/ui-ux-changes`. The hub stays on `main`. Never `git checkout -b` in the hub.
- **Spec:** `monkify-meta/specs/app-home-changes.md` is authoritative. Section references below are
  to it.
- **`useLiveGames.ts` MUST NOT appear in the diff** (spec §7.7, decision 9).
- **No test runner exists in `monkify-front`.** The gates are `yarn lint`, `yarn build` and the manual
  QA in spec §12 — there is no unit test to write first, so the TDD cycle is replaced by
  *make the change → lint → build → verify in the browser* per task. This is stated so it reads as a
  deliberate exemption, not an omission.
- ⚠️ **`yarn lint` requires uniform CRLF.** LF-writing tools produce `Insert ␍` across a whole file.
  After editing, run `unix2dos <file>` (or verify with `file <path>`) before committing.
- **Copy is verbatim from the card.** No paraphrasing, no extra punctuation beyond the single decided
  trailing period on the hero line.
- **Level: Minor change** ⇒ no `SpaceStroker.Tests.Integration` test, no new `features/<slug>.md`.

---

## File Structure

| File | Responsibility after this plan |
| --- | --- |
| `src/components/home/HomeHero.vue` | Unchanged except one string in `liveLine` |
| `src/components/game-selection/GameTypeCard.vue` | `liveChip` renders only for the lottery archetype |
| `src/components/home/HomeHowItWorks.vue` | New title, no eyebrow, three new beat headings, `Learn more` |
| `src/components/home/HomeFeatureCards.vue` | One section only: `PROVABLY FAIR`, full width |
| `src/components/home/HomeSection.vue` | Comment only — its tone note must stop naming a deleted panel |
| `src/pages/HomePage.vue` | Owns the `practice` analytics event; no `@practice` on feature cards |
| `src/components/home/HomeFloor.vue` | Loads its two reads when it enters the viewport, once |
| `src/App.vue` | Boot probe fills the shared catalogue store instead of discarding it |
| `src/components/game-selection/GameTypes.vue` | Uses a populated catalogue; fetches only when empty |

---

### Task 1: The hero's quiet line

**Files:**
- Modify: `src/components/home/HomeHero.vue:142`

**Interfaces:**
- Consumes: nothing.
- Produces: nothing. `liveLine` keeps its `ComputedRef<string>` type.

- [ ] **Step 1: Change the string**

```ts
  // No loading state and no error state: the quiet line is TRUE while the snapshot is in flight, when
  // it failed, and when the floor really is empty. A spinner here would be three different lies.
  const liveLine = computed(() => {
    const live = featured.value;
    if (!live) return 'Place a bet and get ready for the next round.';
```

- [ ] **Step 2: Prove the old copy is gone**

Run: `grep -rn "Floor's quiet" src/`
Expected: no matches (AC1).

- [ ] **Step 3: Commit**

```bash
git add src/components/home/HomeHero.vue
git commit -m "Retire the hero's quiet-floor line for a call to action (monkify-front)."
```

---

### Task 2: Drop the PLAYERS chip from the picker card

**Files:**
- Modify: `src/components/game-selection/GameTypeCard.vue:155-175`

**Interfaces:**
- Consumes: `archetypeOf(game)` from `@/utils/gameArt` (already imported), `props.live: LiveGame`.
- Produces: `liveChip` narrows to `{ label: 'Pot'; text: string; coin: boolean; dot: boolean; hot: boolean } | null`.
  The template already handles `null` via `v-if="liveChip"` — do not touch the template.

- [ ] **Step 1: Rewrite `liveChip`**

```ts
  // The second chip asks the question that fits the game — and since #143 only a LOTTERY has one. A
  // race or a preset used to show "PLAYERS n": a scoreboard on a card whose job is to say what the
  // game IS, and the loudest thing on it on a quiet floor. The FILLING pill above already says a
  // round is alive, which is the part a player can act on.
  //
  // With NO snapshot at all the chip is omitted rather than guessed: an absent chip says nothing, and
  // a fabricated one says something false.
  const liveChip = computed(() => {
    const live = props.live;
    if (!live) return null;
    if (archetype.value !== 'lottery') return null;

    return {
      label: 'Pot',
      text: money(live.pot, props.game),
      coin: showCoin.value,
      dot: false,
      hot: live.pot > 0,
    };
  });
```

- [ ] **Step 2: Confirm nothing was orphaned**

Run: `grep -n "live.players\|countPlayers" src/components/game-selection/GameTypeCard.vue src/utils/players.ts`
Expected: `live.players` still read by the `status` computed (the `FILLING` branch), `countPlayers`
still exported and used by `useLiveGames`. Nothing to delete.

- [ ] **Step 3: Leave the `.live-dot` CSS in place**

Do **not** delete `.live-dot` / `.live-dot--on` from the scoped style block, and do **not** touch
`--color-live` in `src/assets/styles/colors.css` — `HomeFloor`'s eyebrow dot uses the token.
Run: `grep -rn "color-live" src/ | head` and confirm at least one other consumer.

- [ ] **Step 4: Commit**

```bash
git add src/components/game-selection/GameTypeCard.vue
git commit -m "Stop reporting a player count on the race and preset cards (monkify-front)."
```

---

### Task 3: Retitle the how-it-works section and relabel its beats

**Files:**
- Modify: `src/components/home/HomeHowItWorks.vue:20-37,64-66`

**Interfaces:**
- Consumes: `HomeSection` props (`title` required, `eyebrow` optional — `withDefaults(..., { eyebrow: undefined })`).
- Produces: nothing; the `how-to-play` emit is unchanged.

- [ ] **Step 1: Relabel the three beats (headings only — sub-copy stays byte-for-byte)**

```ts
  const steps = [
    {
      h: 'Place a bet',
      p: 'Six characters, yours. Anyone can call the same round, nobody can call yours.',
    },
    {
      h: 'Edson starts (key)stroking',
      p: 'Live, from a seed committed before the round. Nothing is decided after it starts.',
    },
    {
      h: 'First bet typed wins',
      p: 'His first landed call splits the pot. Two characters short is still short.',
    },
  ];
```

- [ ] **Step 2: Retitle the section and drop the eyebrow**

```html
  <home-section title="WANNA LEARN HOW TO PLAY?">
```

(The `eyebrow="Three beats"` attribute is removed entirely; `HomeSection` renders no eyebrow span
when neither the prop nor the slot is present, so the header block collapses to the title alone.)

- [ ] **Step 3: Relabel the button**

```html
      <button type="button" class="btn-arcade btn-arcade--ghost" @click="$emit('how-to-play')">
        Learn more
      </button>
```

- [ ] **Step 4: Verify the header comment still tells the truth**

The file's comment says this section is the app's only unprompted onboarding and that "The guide gets
the button". Both still hold — leave them, but make sure nothing in the comment still says
"HOW A ROUND GOES" or "three beats" as a name.

- [ ] **Step 5: Commit**

```bash
git add src/components/home/HomeHowItWorks.vue
git commit -m "Ask the question the section answers, and name the beats after what the player does (monkify-front)."
```

---

### Task 4: Delete TRY IT FREE, widen PROVABLY FAIR, keep the practice event

**Files:**
- Modify: `src/components/home/HomeFeatureCards.vue` (whole file)
- Modify: `src/pages/HomePage.vue:43,103`
- Modify: `src/components/home/HomeSection.vue` (comment only)

**Interfaces:**
- Consumes: `useHeaderModals().openFair`, `useEmbed().isEmbedded`, `useAnalytics().trackHomeLinkClick`.
- Produces: `HomeFeatureCards` no longer emits `practice`. Any parent binding `@practice` on it must
  be removed in the same task or the build warns on an unknown emit.

- [ ] **Step 1: Reduce `HomeFeatureCards.vue` to the fairness panel**

```html
<script setup lang="ts">
  import HomeSection from '@/components/home/HomeSection.vue';
  import { useEmbed } from '@/hooks/useEmbed';
  import { useAnalytics } from '@/hooks/useAnalytics';
  import { useHeaderModals } from '@/hooks/useHeaderModals';

  // The one objection a first-time visitor has that the page has to answer in its own words: "how do
  // I know this is not rigged". It used to be an entry in a dotted footnote row, which is the shape of
  // a legend, not of an answer.
  //
  // ⚠️ It used to have a twin — a "TRY IT FREE" panel — and #143 deleted it: the trimmed hero (#138)
  // carries a `Try it free` CTA of its own, so the panel was the same offer twice within one screen.
  // The practice ENTRY did not move to nowhere; it moved UP. `HomePage.startPractice` owns it,
  // analytics included.
  const { isEmbedded } = useEmbed();
  const { openFair } = useHeaderModals();
  const { trackHomeLinkClick } = useAnalytics();

  const fair = () => {
    trackHomeLinkClick({ link: 'fair' });
    openFair();
  };
</script>

<template>
  <!-- Full width since its twin was deleted: one panel, one column, no `auto-fit` grid to leave an
       empty track beside it. Hidden when embedded, exactly like the header's own Fair row — and with
       one child that means this renders NOTHING in a frame, not an empty box. -->
  <home-section v-if="!isEmbedded" eyebrow="Seed-locked" title="PROVABLY FAIR">
    <p class="vt323-regular m-0 text-[18px] leading-[1.35] text-[#8e9bad] md:text-[19px]">
      Every round commits its seed before the first character is typed. Check any finished round
      yourself — the inputs are all published.
    </p>
    <template #actions>
      <button type="button" class="btn-arcade btn-arcade--ghost" @click="fair">
        Verify a round
      </button>
    </template>
  </home-section>
</template>
```

- [ ] **Step 2: Move the practice analytics into `HomePage.vue`**

```ts
  // Practice mode is real since Trello #129: a full round at /game/practice that runs entirely in the
  // browser — no request, no socket, no wallet, no money.
  //
  // This is the ONLY practice entry point on the page since #143 deleted the TRY IT FREE panel, so the
  // `practice` link event that panel used to fire lives HERE now — otherwise the GA series would have
  // died silently on the day the panel went.
  const startPractice = () => {
    trackHomeLinkClick({ link: 'practice' });
    router.push({ name: 'Practice' });
  };
```

- [ ] **Step 3: Drop the `@practice` binding on the feature cards**

```html
      <home-feature-cards />
```

- [ ] **Step 4: Re-point `HomeSection.vue`'s tone comment**

```ts
  // TONE alternates down the page: `cyan` for the panel that is about the player (Your record) and
  // `line` for the informational ones. Mixing them is what stops five identical rectangles reading as
  // a list. (The other cyan panel, "Try it free", was deleted by #143.)
```

- [ ] **Step 5: Verify no orphaned import and no stale binding**

Run: `grep -rn "practice" src/components/home/HomeFeatureCards.vue`
Expected: only the comment mentioning the deletion — no emit, no handler.
Run: `grep -rn "TRY IT FREE" src/`
Expected: no matches (AC9).

- [ ] **Step 6: Commit**

```bash
git add src/components/home/HomeFeatureCards.vue src/components/home/HomeSection.vue src/pages/HomePage.vue
git commit -m "Stop offering the free round twice, and give fairness the full width (monkify-front)."
```

---

### Task 5: Defer THE FLOOR's two reads until it is on screen

**Files:**
- Modify: `src/components/home/HomeFloor.vue:26-70`

**Interfaces:**
- Consumes: `HomeSection`'s root element via a template ref (`$el` — its root IS a single `<section>`).
- Produces: `load()` keeps its signature (`() => Promise<void>`) so the `failed` branch's
  `@click="load"` retry is unchanged.

- [ ] **Step 1: Add the ref, the observer and the trigger**

```ts
  import { computed, onBeforeUnmount, onMounted, ref } from 'vue';
```

```ts
  const stats = ref<GlobalStats | null>(null);
  const rounds = ref<GameHistoryRow[]>([]);
  // Starts FALSE, unlike every other section on this page: the reads no longer begin on mount, so a
  // `true` here would render "> reading the floor…" under a section nobody has asked for yet.
  // `load()` raises it itself.
  const loading = ref(false);
  const failed = ref(false);
```

```ts
  // WHEN, not WHETHER. Both reads are all-time aggregates for a section that sits below the fold on
  // every viewport, so firing them on mount spends two requests of the front door's budget on
  // something the player may never scroll to. One shot: disconnect BEFORE loading, so scrolling away
  // and back never refires (there is no in-flight guard inside `load`).
  const root = ref<{ $el?: Element } | null>(null);
  let observer: IntersectionObserver | null = null;

  const stopObserving = () => {
    observer?.disconnect();
    observer = null;
  };

  onMounted(() => {
    const el = root.value?.$el;
    // No element, or a browser without IntersectionObserver: load immediately. A missing figure is a
    // worse failure than an early request.
    if (!el || typeof IntersectionObserver === 'undefined') {
      load();
      return;
    }

    observer = new IntersectionObserver(
      (entries) => {
        if (!entries.some((entry) => entry.isIntersecting)) return;
        stopObserving();
        load();
      },
      // Start the reads a little before the panel is actually on screen, so the figures are there by
      // the time the player has scrolled to them.
      { rootMargin: '200px 0px' }
    );
    observer.observe(el);
  });

  onBeforeUnmount(stopObserving);
```

- [ ] **Step 2: Attach the ref to the section**

```html
  <home-section ref="root" title="THE FLOOR" eyebrow-tone="cyan">
```

- [ ] **Step 3: Verify the retry path is untouched**

Run: `grep -n "@click=\"load\"\|onMounted(load)" src/components/home/HomeFloor.vue`
Expected: the retry `@click="load"` is still there; `onMounted(load)` is gone.

- [ ] **Step 4: Browser check (AC14, AC15, AC16)**

Load `/` with the network panel open and do not scroll → no `/games/stats`, no `/games/history`.
Scroll down → both fire once, figures populate. Scroll up and back → no refire. Throttle to offline
and reload → scrolling down shows `> floor offline — retry`; back online, retry populates it.

- [ ] **Step 5: Commit**

```bash
git add src/components/home/HomeFloor.vue
git commit -m "Read the floor when the player reaches it, not before (monkify-front)."
```

---

### Task 6: Reuse the boot catalogue instead of re-requesting it

**Files:**
- Modify: `src/App.vue:162-168`
- Modify: `src/components/game-selection/GameTypes.vue:28-43`

**Interfaces:**
- Consumes: `useGame().gameTypes` — a **module-level** `ref<ActiveGame[]>()` (`useGame.ts:39`), shared
  by every consumer in the page load.
- Produces: `gameTypes.value` is populated by whichever of the two sites resolves first;
  `refreshLive(types)` is still called exactly once by `GameTypes`.

- [ ] **Step 1: Make the boot probe keep its result (`App.vue`)**

```ts
    if (isPlayerRoute()) {
      // There is no operator or token to resolve any more, so the first thing the app needs is the
      // game list. `mainService.getGamesTypes` already wraps itself in runWithRecovery (a transient
      // failure here is what used to strand a visitor on /error — the 2026-07-26 incident), so this
      // call site must NOT wrap it a second time. The result is KEPT: `gameTypes` is a module-level
      // ref, so filling it here means the lobby/picker does not have to ask for the same list again.
      // Preferences follow so a wallet ALREADY connected at boot — a hydrated mobile deeplink session,
      // which never fires the watcher below — still gets the name prompt.
      try {
        gameTypes.value = await mainService.getGamesTypes();
        await refreshPreferences();
      } catch {
        // Terminal, or the retry budget ran out. The lobby renders its own empty state.
      }
    }
```

Add `gameTypes` to the existing `useGame()` destructure in `App.vue` (or add the call if the file does
not already use it) — check with `grep -n "useGame()" src/App.vue` first and extend rather than
duplicate.

⚠️ `mainService.getGamesTypes()` returns the **unwrapped** `ActiveGame[]` (it destructures
`const { data } = await runWithRecovery(...)` internally and returns `data`), while
`mainService.getGameInstance` returns the envelope. Assign the array directly — do **not** write
`.data` here. Confirm with `sed -n '78,86p' src/services/mainService.ts` before writing the line.

- [ ] **Step 2: Skip the second fetch when the store is already full (`GameTypes.vue`)**

```ts
  onMounted(async () => {
    try {
      // The boot probe in App.vue usually filled this already. Re-requesting it is dead weight — even
      // though singleFlight (#104) collapses a CONCURRENT double-call into one HTTP request, this
      // page has no reason to ask twice at all. A cold mount still fetches: App.vue only probes on
      // isPlayerRoute(), so arriving from /wallet-bridge, /admin or /error leaves the store empty, and
      // a failed probe leaves it empty too.
      const types = gameTypes.value?.length ? gameTypes.value : await mainService.getGamesTypes();
      gameTypes.value = types;
      // Deliberately not awaited, on BOTH paths: the cards must paint on the catalogue alone. A live
      // figure is an enrichment, never a gate — and every one of these calls is allowed to fail
      // silently. Dropping it on the reuse path would silently kill the live layer.
      refreshLive(types);
    } catch (error: unknown) {
      const err = error as AxiosNetworkError;
      err.handled = true; // Mark as handled to avoid duplicate error handling
      toast.error(err.message || 'Failed to fetch game types');
      console.error('Failed to fetch game types', error);
    } finally {
      loading.value = false;
    }
  });
```

- [ ] **Step 3: Verify the failure path still works (AC13)**

Block `/games/active-types` in devtools, reload `/` → the picker shows its error toast and the page
does not white-screen. Unblock, reload → cards render.

- [ ] **Step 4: Count the requests (AC12, AC17)**

Cold `/` with an empty cache, network panel filtered to XHR: exactly one `/games/active-types`, three
`GET /games/{id}`, one `/players/preferences` — five before any scroll. Record the number.

- [ ] **Step 5: Commit**

```bash
git add src/App.vue src/components/game-selection/GameTypes.vue
git commit -m "Ask for the game catalogue once per page load (monkify-front)."
```

---

### Task 7: Gates

**Files:** none (verification only).

- [ ] **Step 1: Normalize line endings**

Run: `unix2dos src/components/home/HomeHero.vue src/components/home/HomeHowItWorks.vue src/components/home/HomeFeatureCards.vue src/components/home/HomeFloor.vue src/components/home/HomeSection.vue src/components/game-selection/GameTypeCard.vue src/components/game-selection/GameTypes.vue src/pages/HomePage.vue src/App.vue`
Expected: no `Insert ␍` failures in the next step.

- [ ] **Step 2: Lint**

Run: `yarn lint`
Expected: exit 0, no `no-unused-vars` (AC20).

- [ ] **Step 3: Build**

Run: `yarn build`
Expected: compiled successfully (AC20).

- [ ] **Step 4: Push**

```bash
git push origin feature/app-home-changes
```

---

## Self-Review

**Spec coverage:** §7.1 → Task 1. §7.2 → Task 2. §7.3 → Task 3. §7.4 → Task 4. §7.5 → Task 5.
§7.6 → Task 6. §7.7 ("what must not change") → enforced by the Global Constraints and by Task 2's
step 2/3 and Task 6's `refreshLive` note. §10 (gates) → Task 7. Every AC 1–21 maps to a task step or
to the ship-time KB sync (AC21), which is `/monkify-ship-feature`'s job, not this plan's.

**Placeholder scan:** none — every step carries the literal code or the literal command.

**Type consistency:** `liveChip` narrows but keeps the same shape the template reads
(`label`/`text`/`coin`/`dot`/`hot`); `load()` keeps `() => Promise<void>` so the retry binding is
untouched; `gameTypes` is `Ref<ActiveGame[] | undefined>` in both Task 6 sites, and Step 1's ⚠️ pins
the unwrapped-array return of `getGamesTypes` against the envelope-returning siblings.

# Redesign Nice Win Modal — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild `OrbitFirstWinModal.vue` — the once-ever Orbit upsell after a player's first paid win — as a compact, Orbit-identity surface whose hype is carried by the winning word and one short game-type-conditional line.

**Architecture:** Pure presentation + a two-value data pass-through. `useGame.ts` already knows the winning word and the game type at the round-ended one-shot; it passes both into `useFirstWinUpsell().maybeOpen()`, which stores them in module refs **only after every guard passes**, and exposes them as computeds. `GameHeader.vue` binds them to the modal. The modal swaps `variant="black"/size="standard"` for `variant="orbit"/size="compact"`, retitles the bar `ORBIT`, and renders `NICE WIN!` + the word + one line in the body. Two new `arcade.css` classes carry the new typography (classes, not utilities — the cascade rule).

**Tech Stack:** Vue 3.2 + TypeScript 5.5, Vue CLI 5 (webpack), Tailwind 3 + DaisyUI, `arcade.css` design system. Yarn. No test runner in `monkify-front`.

## Global Constraints

- **Worktree-per-repo:** all work in `.worktrees/redesign-nice-win-modal/monkify-front` and `.worktrees/redesign-nice-win-modal/monkify-meta`, branch `feature/redesign-nice-win-modal`. Hubs stay on `main`.
- **CRLF:** `monkify-front` requires uniform CRLF line endings. **Never** use `sed -i` or any LF-rewriting tool on these files — `yarn lint` fails with `Insert ␍`.
- **`arcade.css` cascade rule:** `arcade.css` is imported *after* `@tailwind utilities` at equal specificity. A Tailwind utility stacked on an arcade class that declares the same property **silently loses**. New typography must be classes in `arcade.css`.
- **Palette rule:** violet = Orbit identity, amber = the primary action, red = warning only. Cyan stays unspent on the `--orbit` panel.
- **#118 Orbit invisibility:** copy may say **that** a word can be claimed — never **which** or **whose**, and it must introduce no reserved-word lookup.
- **`orbit-single-control.md` word-claim qualifiers:** whenever word ownership is advertised, **both** qualifiers are mandatory — *free-text games only* and *only while Orbit is running*. Satisfied here by rendering the claim beat only on free-text types, with `while it runs` inline.
- **Copy, verbatim:**
  - free-text: `Orbit keeps playing it for you, every round — and the word stays yours while it runs.`
  - preset **and unknown**: `Orbit keeps playing for you, every round. Hands off.`
- **No backend change of any kind** (AC17). No `monkify-backend` branch.
- **No motion added** (D13). **No monetary amount shown** (D12). **`variant="black"` / `.dialog--black` is kept, not deleted** (D2, AC15).
- **Verification bar:** `yarn lint` clean + `yarn build` clean + the §13 manual QA. There is no frontend test runner.

---

## File Structure

| File | Responsibility after this change |
| --- | --- |
| `monkify-front/src/hooks/useFirstWinUpsell.ts` | Owns the guard chain **and now the two display values** (`winningWord`, `isFreeTextGame`), set only at the moment of opening. |
| `monkify-front/src/hooks/useGame.ts` (~462) | Computes the free-text flag with the undefined-safe expression and passes `{ word, freeText }` into `maybeOpen`. |
| `monkify-front/src/components/GameHeader.vue` (~110, ~453) | Destructures the two new computeds and binds them as props. Mount location, `v-if` and `@enable` unchanged. |
| `monkify-front/src/components/orbit/OrbitFirstWinModal.vue` | The rebuilt surface: Orbit material, compact, `ORBIT` titlebar with `OrbitMark`, in-body headline, conditional word hero, one conditional line. |
| `monkify-front/src/assets/styles/arcade.css` | Adds `.dialog-headline` and `.dialog-word` to the dialog content helpers + their mobile step-downs. |
| `monkify-meta/conventions/frontend.md` | KB truth: `compact` = **540** (four places) and the tutorial/help **overlay** tier now has no implementation. |

---

### Task 1: Carry the word and the game-type kind through the composable

**Files:**
- Modify: `monkify-front/src/hooks/useFirstWinUpsell.ts`
- Test: none — `monkify-front` has no test runner. Verified by `yarn build` (types) and the Task 5 manual QA.

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `useFirstWinUpsell()` returns, in addition to today's `isOpen` / `hasSeen` / `maybeOpen` / `dismiss`:
  - `winningWord: ComputedRef<string>`
  - `isFreeTextGame: ComputedRef<boolean>`
  - `maybeOpen(context?: { word?: string | null; freeText?: boolean }): void`

- [ ] **Step 1: Add the two module refs beside `isOpen` / `seen`**

Immediately after the existing `const seen = ref(false);` / `let hydrated = false;` block:

```ts
/**
 * What the modal DISPLAYS, as opposed to whether it may open.
 *
 * ⚠️ Both are written ONLY at the moment the modal actually opens — after every guard in
 * `maybeOpen` — so a refused invitation never leaves stale state behind for the next round.
 * They are deliberately NOT cleared by `dismiss()`: the modal can never reopen (`seen` is set on
 * every exit), and blanking them would flash an empty body through the shell's 140ms leave
 * transition.
 */
const winningWord = ref('');
const isFreeTextGame = ref(false);
```

- [ ] **Step 2: Give `maybeOpen` its optional context argument and assign at the end**

Change the signature and the final line only — **every existing guard keeps its order and its comment**:

```ts
  const maybeOpen = (context?: { word?: string | null; freeText?: boolean }) => {
```

and replace the terminal `isOpen.value = true;` with:

```ts
    // Set ONLY once the modal is actually opening. A refusal must not mutate anything.
    // ⚠️ `=== true`, not `!!` — an absent flag means "we do not know the game type", and the
    // claim-free copy is the safe answer on unknown ground (AC8).
    winningWord.value = context?.word ?? '';
    isFreeTextGame.value = context?.freeText === true;
    isOpen.value = true;
```

The argument stays **optional** so an argument-less call still compiles and degrades safely (empty word ⇒ the row hides; preset copy ⇒ no claim).

- [ ] **Step 3: Expose both as computeds**

In the return object, beside `isOpen` / `hasSeen`:

```ts
  return {
    isOpen: computed(() => isOpen.value),
    hasSeen: computed(() => seen.value),
    winningWord: computed(() => winningWord.value),
    isFreeTextGame: computed(() => isFreeTextGame.value),
    maybeOpen,
    dismiss,
  };
```

`dismiss()` itself is **untouched**.

- [ ] **Step 4: Verify it compiles**

Run from `.worktrees/redesign-nice-win-modal/monkify-front`:

```
yarn lint
```

Expected: clean (no `Insert ␍`, no unused-var error).

- [ ] **Step 5: Do not commit yet** — Tasks 1–4 land as one commit in Task 6, because the props they add and consume do not type-check independently.

---

### Task 2: Pass the two values in from the round-ended one-shot

**Files:**
- Modify: `monkify-front/src/hooks/useGame.ts:462`
- Test: none (no runner) — the expression trap is covered by Task 5 QA step 5.

**Interfaces:**
- Consumes: `maybeOpen(context?: { word?: string | null; freeText?: boolean })` from Task 1.
- Produces: nothing new for later tasks.

- [ ] **Step 1: Replace the single `maybeOpen()` line**

The current line 462, inside the existing `if (didBet && endedGameId && endAudioFiredForGame !== endedGameId)` block (nothing about the block moves):

```ts
          if (won && ownBetWasPaid.value) useFirstWinUpsell().maybeOpen();
```

becomes:

```ts
          // ⚠️ NOT `!selectedGame.value?.presetChoices?.length` — that is TRUE when selectedGame is
          // UNDEFINED, which would assert word ownership on unknown ground. The game must be known
          // AND have no preset choices before the free-text copy is allowed. (The mirrored shape at
          // ~line 608 uses the optional-chained form correctly, because it defaults to PRESET — do
          // not harmonise the two, they need opposite fallbacks.)
          const freeText = !!selectedGame.value && !selectedGame.value.presetChoices?.length;
          if (won && ownBetWasPaid.value)
            useFirstWinUpsell().maybeOpen({ word: winnerCh, freeText });
```

`winnerCh` (`statusData.result?.firstChoiceTyped`) and `won` are already in scope four lines above. `ownBetWasPaid` still gates the call, so a **free-bet** win does not open the upsell.

- [ ] **Step 2: Confirm the sibling expression at ~608 is untouched**

Run:

```
grep -n "presetChoices" src/hooks/useGame.ts
```

Expected: the `~608` line still reads `const isPreset = !!selectedGame.value?.presetChoices?.length;` — unchanged.

- [ ] **Step 3: Verify**

```
yarn lint
```

Expected: clean.

---

### Task 3: Bind the two props through the header

**Files:**
- Modify: `monkify-front/src/components/GameHeader.vue:110` and `:453`

**Interfaces:**
- Consumes: `winningWord` / `isFreeTextGame` computeds from Task 1.
- Produces: the props `:winning-word` and `:free-text` on `<OrbitFirstWinModal>`, consumed by Task 4.

- [ ] **Step 1: Destructure the two new computeds (line ~110)**

```ts
  const {
    isOpen: isFirstWinUpsellOpen,
    dismiss: dismissFirstWinUpsell,
    winningWord: firstWinWord,
    isFreeTextGame: firstWinIsFreeText,
  } = useFirstWinUpsell();
```

`acceptFirstWinUpsell` below it is unchanged.

- [ ] **Step 2: Bind them on the modal (line ~453)**

```html
    <OrbitFirstWinModal
      v-if="orbitEnabled && !isEmbedded"
      :open="isFirstWinUpsellOpen"
      :winning-word="firstWinWord"
      :free-text="firstWinIsFreeText"
      @dismiss="dismissFirstWinUpsell"
      @enable="acceptFirstWinUpsell"
    />
```

The `v-if`, the surrounding comment, the `@enable` → `isOrbitOpen = true` hand-off and the mount location are **unchanged**.

- [ ] **Step 3: Verify**

```
yarn lint
```

Expected: clean.

---

### Task 4: The two `arcade.css` typography helpers

**Files:**
- Modify: `monkify-front/src/assets/styles/arcade.css` — the "Dialog content helpers" block (~line 539, after `.dialog-copy` / `.dialog-eyebrow`) and the existing `@media (max-width: 1023px)` dialog block (line 1660).

**Interfaces:**
- Consumes: nothing.
- Produces: the classes `.dialog-headline` and `.dialog-word`, used by Task 5.

- [ ] **Step 1: Add both classes after `.dialog-eyebrow` (~line 555), before the `.orbit-section-title` docblock**

```css
  /* The headline INSIDE a body — not the titlebar's name for the dialog. Ink, never amber: amber owns
     the primary action on these panels, and a headline that shouts in the CTA's colour competes with
     the only thing the player is meant to press. */
  .dialog-headline {
    display: block;
    margin: 0;
    font-family: var(--font-display, 'Press Start 2P', system-ui, sans-serif);
    font-size: 18px;
    line-height: 1.3;
    letter-spacing: 0.04em;
    text-align: center;
    color: var(--color-ink);
  }

  /* A hero VALUE under a headline — today the winning word. VT323, because the word is the game's own
     artifact (the terminal types it in this face) and because Press Start 2P at 20 characters cannot
     fit 540px minus 18px of body padding on either side. Full --color-orbit per #141's "frame deep,
     content full" split.
     ⚠️ The wrap rules are load-bearing, not defensive: `choiceRequiredLength` allows up to 20
     characters, and at the smallest supported width that MUST wrap to a second line rather than push
     the sheet sideways. */
  .dialog-word {
    margin: 0;
    max-width: 100%;
    font-family: var(--font-body, 'VT323', ui-monospace, monospace);
    font-size: 34px;
    line-height: 1.1;
    letter-spacing: 0.18em;
    text-align: center;
    text-transform: uppercase;
    overflow-wrap: anywhere;
    color: var(--color-orbit);
  }
```

- [ ] **Step 2: Add the mobile step-downs inside the existing `@media (max-width: 1023px)` block**

Place them after the `.btn-dialog { font-size: 9px; }` rule (~line 1693), still inside the block:

```css
  .dialog-headline {
    font-size: 15px;
  }
  .dialog-word {
    font-size: 28px;
    letter-spacing: 0.14em;
  }
```

**Fit maths (do not tune these numbers without redoing it).** At `compact` the dialog is 540 − 2×2 border − 2×18 `.dialog__body` padding ⇒ ~500px of text width. VT323's advance is ≈0.42em, so at 34px with 0.18em tracking a character costs ≈20.4px ⇒ 20 characters ≈ 408px, inside 500. On a 320px phone the sheet gives ≈284px; at 28px/0.14em a character costs ≈15.7px ⇒ 20 characters ≈ 314px, which **wraps to two lines** via `overflow-wrap: anywhere` and never scrolls the page sideways.

- [ ] **Step 3: Verify the classes resolve**

```
yarn lint
```

Expected: clean. (Visual verification is Task 5 / QA step 6.)

---

### Task 5: Rebuild the modal

**Files:**
- Modify: `monkify-front/src/components/orbit/OrbitFirstWinModal.vue` (whole file)

**Interfaces:**
- Consumes: props `open`, `winningWord`, `freeText` from Task 3; classes `.dialog-headline` / `.dialog-word` from Task 4.
- Produces: emits `dismiss` and `enable` — **unchanged**.

- [ ] **Step 1: Replace the file**

```vue
<script setup lang="ts">
  import { computed } from 'vue';
  import ArcadeModal from '@/components/ui/ArcadeModal.vue';
  import OrbitMark from '@/components/orbit/OrbitMark.vue';
  import edson from '@/assets/edson.png';

  /**
   * The first-win invitation into Orbit.
   *
   * ORBIT MATERIAL (#157) — violet-deep frame on void-deep, the same skin as `/orbit`, `OrbitModal`
   * and `HomeOrbitCard`, so the invitation and the thing it invites you to read as one surface. It
   * used to be the BLACK panel; that tier is kept in the design system but now has no overlay
   * consumer (conventions/frontend.md). `.arcade-frame--black` — the guide's GuideTip — is the black
   * MATERIAL's remaining live consumer and is untouched.
   *
   * ⚠️ THE COPY IS CONDITIONAL, NOT DISCLAIMED. The word-ownership beat may only be shown on
   * FREE-TEXT game types, and only with its "while it runs" lifetime qualifier — both are mandatory
   * whenever a word claim is advertised (specs/orbit-single-control.md). This modal fires after ANY
   * paid win, preset types included, where word claims do not exist, so a flat "your word stays
   * yours" would be FALSE for a Beast Mode winner. `freeText` defaults to false: unknown ground gets
   * the claim-free line. #118 is unbroken — this says THAT a word can be claimed, never WHICH or
   * WHOSE, and it adds no reserved-word probe.
   *
   * ⚠️ The uppercase word is PRESENTATIONAL ONLY (`text-transform` in `.dialog-word`). Stored choices
   * are always lowercase — uppercase is rejected as InvalidCharacters — so nothing here may echo the
   * rendered string back into a bet path.
   *
   * ⚠️ EVERY EXIT MARKS IT SEEN — the X, Esc, the backdrop and both buttons alike. Nobody is nagged
   * for dismissing a once-ever modal "the wrong way", which is why @close routes straight to dismiss
   * whatever reason the shell hands it.
   *
   * The shell owns the teleport, the dithered backdrop, Esc (through useOverlayStack, which routes it
   * to the TOPMOST overlay only), the focus trap and the 90dvh cap. This file hand-rolled every one of
   * them until #141.
   */

  const props = defineProps<{ open: boolean; winningWord?: string; freeText?: boolean }>();

  const emit = defineEmits<{
    (e: 'dismiss'): void;
    (e: 'enable'): void;
  }>();

  const supportingLine = computed(() =>
    props.freeText
      ? 'Orbit keeps playing it for you, every round — and the word stays yours while it runs.'
      : 'Orbit keeps playing for you, every round. Hands off.'
  );
</script>

<template>
  <ArcadeModal
    :open="open"
    variant="orbit"
    size="compact"
    title="ORBIT"
    :dismissible="true"
    @close="emit('dismiss')"
  >
    <!-- The same lead mark as OrbitModal's titlebar, at the same 13px — one Orbit titlebar look, not
         two. aria-hidden because the title text beside it is already the accessible name. -->
    <template #titlebar-lead>
      <OrbitMark :size="13" class="flex-none text-orbit" aria-hidden="true" />
    </template>

    <div class="flex flex-col items-center">
      <img
        :src="edson"
        alt=""
        aria-hidden="true"
        class="pixelated h-24 w-24 object-contain md:h-28 md:w-28"
      />

      <p class="dialog-headline mt-3">NICE WIN!</p>

      <!-- HIDDEN, not blanked: the invitation must never fail to open over a missing decoration. -->
      <p v-if="winningWord" class="dialog-word mt-2.5">{{ winningWord }}</p>

      <p class="dialog-copy mt-3 max-w-[32ch] text-balance text-center">{{ supportingLine }}</p>

      <!-- Both buttons come off the control-sizing scale rather than their old bespoke px-6 py-3.5 /
           bare-text pair, so they are 40px like every other flat control in the app — and 44px below
           lg, straight from the token block, with no call-site class.
           Amber stays the primary: Orbit's palette rule gives amber to the ACTION and violet to
           IDENTITY, so a violet fill here would be both off-rule and lower-contrast on a violet frame. -->
      <div class="mt-6 flex w-full flex-col items-center gap-2.5">
        <button type="button" class="btn-dialog btn-dialog--primary w-full" @click="emit('enable')">
          Set up Orbit
        </button>
        <button type="button" class="btn-dialog w-full" @click="emit('dismiss')">Not now</button>
      </div>
    </div>
  </ArcadeModal>
</template>
```

- [ ] **Step 2: Confirm the black tier survives with zero overlay consumers (AC15)**

```
grep -rn "variant=\"black\"" src/
grep -n "dialog--black\|arcade-frame--black" src/assets/styles/arcade.css | head
```

Expected: the first grep returns **nothing**; `.dialog--black` and `.arcade-frame--black` still exist in `arcade.css`.

- [ ] **Step 3: Lint and build**

```
yarn lint
yarn build
```

Expected: both clean; no type error on the two new optional props.

---

### Task 6: Commit the frontend work

**Files:** all five frontend files from Tasks 1–5.

- [ ] **Step 1: Verify line endings before staging (the CRLF trap)**

```
git -C <workspace-root>/.worktrees/redesign-nice-win-modal/monkify-front diff --stat
yarn lint
```

Expected: no `Insert ␍` from prettier. If it appears, run `unix2dos` on the offending file — never `sed -i`.

- [ ] **Step 2: Commit and push**

```bash
git -C <workspace-root>/.worktrees/redesign-nice-win-modal/monkify-front add -A
git -C <workspace-root>/.worktrees/redesign-nice-win-modal/monkify-front commit -m "Implement the Orbit first-win modal redesign (monkify-front)."
git -C <workspace-root>/.worktrees/redesign-nice-win-modal/monkify-front push origin feature/redesign-nice-win-modal
```

⚠️ **Gate:** this task runs only after the user has tested and approved (skill Step 9).

---

### Task 7: Correct the KB (`conventions/frontend.md`)

**Files:**
- Modify: `monkify-meta/conventions/frontend.md` — lines **324**, **366**, **436**, **652** (the `608` → `540` correction) and the black-overlay-tier statements at ~**259**, ~**336**, ~**657**.

**Interfaces:** documentation only.

- [ ] **Step 1: Fix `compact` 608 → 540 in all four places**

The code is truth: `ArcadeModal.vue:173` reads `compact: 'lg:max-w-[540px]'`. Verify the count first:

```
grep -n "608" monkify-meta/conventions/frontend.md
```

Expected before: four hits (324, 366, 436, 652). Expected after the edit: **zero**.

- [ ] **Step 2: Record that the tutorial/help OVERLAY tier has no implementation**

At the "Tutorial / help tier" bullet (~657) and the panel-table row that still names the first-win upsell as the black tier's reference overlay (~336), state that:
- the black **dialog** tier (`variant="black"` / `.dialog--black`) is **kept but has no consumer** — the first-win upsell moved to the Orbit material in #157;
- `.arcade-frame--black` (the How to play guide's `GuideTip`) remains the black **material's** live consumer and is unchanged;
- a documented tier with no consumer is recorded deliberately here rather than left for a reader to discover as drift.

- [ ] **Step 3: Verify AC16**

```
grep -n "608" monkify-meta/conventions/frontend.md      # expect: nothing
grep -n "540" monkify-meta/conventions/frontend.md      # expect: four hits
```

- [ ] **Step 4: Commit and push (after user approval, with Task 6)**

```bash
git -C <workspace-root>/.worktrees/redesign-nice-win-modal/monkify-meta add -A
git -C <workspace-root>/.worktrees/redesign-nice-win-modal/monkify-meta commit -m "Correct the dialog size token and record the consumer-less black overlay tier (monkify-meta)."
git -C <workspace-root>/.worktrees/redesign-nice-win-modal/monkify-meta push origin feature/redesign-nice-win-modal
```

---

## Manual QA (spec §13) — the verification bar, since there is no test runner

Run `yarn serve` from the worktree (copy the hub's `.env.local` and `yarn.lock` in first; pick a verified-free port).

1. **Reset "seen":** `localStorage.removeItem('space-stroker.orbit.first-win-seen')`, reload. Confirm the wallet is **not** Orbit-enrolled.
2. **Win a paid round** on a **free-text** game (Cosmic Sprint) with two players against the local Docker stack — a free bet will **not** open it.
3. **AC1/AC2/AC3:** violet frame, 540px on desktop, `⟡ ORBIT` titlebar with a working X, `NICE WIN!` in the body.
4. **AC4/AC6:** the word you bet is the hero; the line ends *"…and the word stays yours while it runs."*
5. **AC7/AC8:** repeat on **Beast Mode** (reset `seen` first) — the hands-off line, with **no** word-ownership claim.
6. **AC12:** with the modal open, `document.querySelector('.dialog-word').textContent = 'abcdefghijklmnopqrst'` — check 540px desktop **and** a 320px viewport; no horizontal scrollbar on `body`.
7. **AC5:** blank the word node — headline, line and both buttons still render.
8. **AC11:** close via Esc, reload, win again → must not reopen. Repeat for the X, the backdrop, `Not now` and `Set up Orbit` (the last also opens the Orbit panel).

---

## Self-review

**Spec coverage.** §7.1 → Task 1. §7.2 → Task 2. §7.3 → Task 3. §7.4 → Task 5. §7.5 → Task 4. §10.3 (`conventions/frontend.md`) → Task 7. §13 → the QA block. §6/§10.1 (no backend) → no task, deliberately (AC17). AC13/AC14 are guard behaviours this card does not touch — covered by leaving `maybeOpen`'s chain and `GameHeader`'s `v-if` untouched (Tasks 1 and 3 both state this explicitly). AC15 → Task 5 step 2.

**Placeholders.** None — every code step carries the literal content.

**Type consistency.** `winningWord` / `isFreeTextGame` are the composable's names (Task 1); `firstWinWord` / `firstWinIsFreeText` are the header's local aliases (Task 3); `winningWord` / `freeText` are the component's prop names (Task 5), bound as `:winning-word` / `:free-text`. `maybeOpen`'s context keys are `word` / `freeText` in Tasks 1 and 2. Checked.

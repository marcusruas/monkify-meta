# Orbit new UI — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild `/orbit` from a five-step collapse-to-dashboard list into five tabs, with
run/stop/turn-off moved out of the card into a control trio beneath it, at a type scale one class
above the modal's.

**Architecture:** `OrbitPage.vue` (1,379 lines) becomes a ~250-line shell that owns only layout and
the three control actions. Each tab is its own component owning its fields *and* its handlers. The
structural swap happens **before** any restyle, so the page keeps working at every commit and a
reviewer can reject "the tabs are wrong" separately from "the paint is wrong". No new state enters
`useOrbit.ts`.

**Tech Stack:** Vue 3.2 + TypeScript 5.5, Vue CLI 5 (webpack), Tailwind 3 + DaisyUI, `vue3-toastify`.
Yarn. **No test runner in this repo** ⇒ every task verifies with `yarn lint` + `yarn build` and a
named browser check.

**Spec:** `specs/orbit-new-ui.md` — authoritative. **No Trello card** (designed in a brainstorming
session, 2026-08-06, from two owner mockups).

## Global Constraints

- **Branch `feature/orbit-new-ui`**, worktree `.worktrees/orbit-ui-overhaul/monkify-front`; hubs stay on `main`. Push after every task (`AGENTS.md` rule 5).
- **`monkify-front` only.** `monkify-backend` must diff to **zero** changes against `33e396e` (spec AC17).
- ⚠️ **`OrbitStatusBanner.vue`, `OrbitAmountField.vue` and `OrbitWordField.vue` are SHARED with `OrbitModal.vue`.** Consume them as-is; **wrap, never edit**. Editing any of them regresses the panel shipped in `66e4df3` (spec AC12). `OrbitStepRow.vue` has one consumer and is deleted in Task 6.
- **Every control height comes from `--ctl-h-*`**, never padding, never a literal.
- **Do not stack Tailwind utilities on an arcade class.** Use it as-is, use plain Tailwind for a differently-shaped control, or add a modifier in `arcade.css`.
- **Dither, never blur.** No `backdrop-filter`, no `blur()`, no infinite animation, no wide glow — `usePerfMode` forces `html.light-graphics` below `lg`.
- **Colour has one job each:** green = running/OK, violet = identity + active tab, red = warning, amber = primary action buttons only. Inside the card, a running fully-configured Orbit shows **no amber and no red**; every in-card value is plain ink.
- ⚠️ **The focus ring on this page is VIOLET, not cyan.** "Cyan is the focus ring" is the app-wide default that Orbit surfaces deliberately override — `arcade.css:1032-1038`, commit `f7716c7`. Every new focusable control this card adds joins that violet exception block; do not write a cyan ring anywhere on `/orbit`.
- **Everything stays behind `orbitEnabled`** (`VUE_APP_ORBIT_ENABLED`, fail-closed `=== 'true'`).
- ⚠️ **Lint needs uniform CRLF.** `core.autocrlf=true`; never `sed -i` these files (it rewrites to LF and prettier then fails with `Insert ␍` and nothing else). Verify with `od -c` if lint complains about invisible characters.
- ⚠️ **Copy the hub's `yarn.lock` into the worktree** before `yarn install` — it is gitignored, and without it the build resolves different prettier/eslint versions than the hub and lint disagrees.
- **Copy comes from the spec, not the mockups.** Mockups routinely carry wording that refinement rejected.

---

## File Structure

**Create**

| File | Responsibility |
| --- | --- |
| `src/components/orbit/OrbitStatStrip.vue` | Presentational. Two cells: holds / per round. No fetching, no actions. |
| `src/components/orbit/OrbitTabBar.vue` | The numbered strip + `role="tablist"` semantics + horizontal scroll behaviour. Knows nothing about Orbit. |
| `src/components/orbit/OrbitControlBar.vue` | The trio. Maps `orbitStatus` → per-button state; emits `run` / `stop` / `turn-off`. Performs **no** mutation itself. |
| `src/components/orbit/tabs/OrbitGameTab.vue` | Word / auto-pick / game mode, and their handlers. |
| `src/components/orbit/tabs/OrbitMoneyTab.vue` | Add money + cash out, and their handlers. |
| `src/components/orbit/tabs/OrbitStopRulesTab.vue` | Threshold + floor, and their handlers. |
| `src/components/orbit/tabs/OrbitRecoverTab.vue` | Signature + amount recovery, and its handler. |
| `src/components/orbit/tabs/OrbitTelegramTab.vue` | Link / verify / unlink + the three toggles. |

**Modify**

| File | Change |
| --- | --- |
| `src/pages/OrbitPage.vue` | Rewritten to a shell: header, stat strip, notice band, tab bar, active panel, control trio, bet log. |
| `src/assets/styles/colors.css` | Add `--ctl-h-xl: 3.5rem` beside the existing three. |
| `src/assets/styles/arcade.css` | `.orbit-page` type scale, `.orbit-tab*`, `.orbit-statstrip*`, `.orbit-trio*` — all scoped under `.orbit-page`. |

**Delete**

| File | Why |
| --- | --- |
| `src/components/orbit/OrbitStepRow.vue` | The collapse unit. `OrbitPage.vue` is its only consumer. |

**Untouched (verify byte-identical at the end):** `OrbitStatusBanner.vue`, `OrbitAmountField.vue`,
`OrbitWordField.vue`, `OrbitBetLog.vue`, `OrbitModal.vue`.

---

## Task 1: Design pass + the page scale

⚠️ **`conventions/frontend.md` → "Design workflow (required for any new visual surface)" applies to
this card.** The two owner mockups (2026-08-06) are the design source and outrank the spec's prose
description of the visuals. Run this task before writing any markup.

**Files:** `src/assets/styles/colors.css`, `src/assets/styles/arcade.css`

**Interfaces:**
- Produces: CSS classes `.orbit-page` (the page wrapper, and the ancestor scope for all of the below), `.orbit-page-title`, `.orbit-field-label`, `.orbit-tabs`, `.orbit-tab`, `.orbit-tab--active`, `.orbit-statstrip`, `.orbit-statstrip__label`, `.orbit-statstrip__value`, `.orbit-trio`, `.orbit-trio__btn`, and the token `--ctl-h-xl`. Every later task consumes these by name. ⚠️ **All of them are defined as `.orbit-page <selector>`** — see the scoping warnings in Step 4.

- [ ] **Step 1: Run the `impeccable` skill** in `craft` mode against the three new surfaces (stat strip, tab bar, control trio), with the two mockups as the design source. ⚠️ It will report `NO_PRODUCT_MD` and offer an `init` interview — **skip that**; this workspace deliberately keeps no `PRODUCT.md`/`DESIGN.md` (decided 2026-07-29). The design source is the mockups plus the committed tokens in `colors.css`/`arcade.css`.

- [ ] **Step 2: Add the control token.** In `colors.css`, beside `--ctl-h-lg`:

```css
  --ctl-h-xl: 3.5rem; /* 56px — the /orbit control trio (RUN / STOP / TURN OFF).
     ⚠️ Deliberately has NO mobile step-up, matching --ctl-h-lg. Only --ctl-h-sm and --ctl-h-md step
     up below lg. The trio stacks full-width on phones, so it takes its touch target from width. */
```

- [ ] **Step 3: Add the page scale** to `arcade.css`, scoped under `.orbit-page` so the panel values shipped in `66e4df3` are untouched:

```css
  /* The /orbit page runs one size class above the panel: it is 1000px wide, not 380px.
     ⚠️ The 11px section title is NOT a reversal of the 2026-08-05 9px ruling. That ruling's stated
     principle is the RATIO to what a title introduces, not an absolute floor — and here the controls
     are 48px and the stat values 32px, so 11px/ink-dim ranks LOWER than 9px did in the panel. */
  .orbit-page .orbit-page-title {
    font-family: var(--font-display, 'Press Start 2P', system-ui, sans-serif);
    font-size: 20px;
    line-height: 1;
    letter-spacing: 0.02em;
  }

  .orbit-page .orbit-section-title { font-size: 11px; }
  .orbit-page .dialog-copy { font-size: 20px; }

  .orbit-page .orbit-field-label {
    display: block;
    font-family: var(--font-display, 'Press Start 2P', system-ui, sans-serif);
    font-size: 9px;
    line-height: 1;
    letter-spacing: 0.08em;
    color: var(--color-ink-dim);
  }
```

- [ ] **Step 4: Add the stat, tab and trio classes.** The strip scrolls on narrow widths; follow `specs/standardize-scrollbars.md` for the scrollbar treatment rather than inventing one.

⚠️ **Every selector below is scoped under `.orbit-page`, and that scoping is not cosmetic.** An
earlier draft of this plan wrote them bare, which shipped a real regression: `OrbitStatusBanner.vue`
defines its own `.orbit-stat` in a `<style scoped>` block with `display: flex; align-items: center`
and **no `flex-direction`**. Vue's scoping only arbitrates per-property where both rules set the same
property — so a bare `.orbit-stat { flex-direction: column }` applied unconditionally and flipped
that banner's cards from row to column *inside `OrbitModal.vue`*, breaking the "the modal's
appearance must not change" constraint. Neither `yarn lint` nor `yarn build` can catch that.

⚠️ **The stat classes are named `.orbit-statstrip*`, not `.orbit-stat*`**, for the same reason.
Scoping alone would leave `.orbit-page .orbit-stat` (0,2,0) tied with the scoped
`.orbit-stat[data-v-x]` (0,2,0) and decided by source order — too fragile to build on, and Task 2
introduces a second, differently-scaled stat concept that must not share a name with the panel's.

⚠️ **`.orbit-page` belongs on the page's outer `max-w-[1000px]` wrapper, not on the
`.arcade-frame--orbit` card.** The trio renders outside the card but inside that wrapper; scoping to
the card would leave it unstyled.

```css
  .orbit-page .orbit-tabs {
    display: flex;
    overflow-x: auto;
    scroll-snap-type: x proximity;
  }
  .orbit-page .orbit-tab {
    flex: 0 0 auto;
    scroll-snap-align: start;
    height: var(--ctl-h-lg);
    font-family: var(--font-display, 'Press Start 2P', system-ui, sans-serif);
    font-size: 11px;
    letter-spacing: 0.06em;
    color: var(--color-ink-dim);
    border-bottom: 2px solid transparent;
  }
  /* The seam: the active tab joins its panel by losing the border between them. This is the one cue
     that makes the strip read as tabs rather than as a row of buttons — do not remove it. */
  .orbit-page .orbit-tab--active {
    color: var(--color-ink);
    border-bottom-color: transparent;
    background-color: color-mix(in oklab, var(--color-orbit) 8%, var(--color-void-deep));
  }
  .orbit-page .orbit-trio__btn { height: var(--ctl-h-xl); }

  .orbit-page .orbit-statstrip__label {
    display: block;
    font-family: var(--font-display, 'Press Start 2P', system-ui, sans-serif);
    font-size: 9px;
    line-height: 1;
    letter-spacing: 0.08em;
    color: var(--color-ink-mute);
  }

  /* ⚠️ The ~3.5x step from the 9px label to the 32px value is deliberate. This strip is the one place
     on the page where a NUMBER is the loudest thing, and that is exactly what lets the 11px section
     titles below it stay quiet without the page losing its ranking. `tabular-nums` is load-bearing:
     these are money figures that update live, and without it the digits jitter sideways on every
     refresh. */
  .orbit-page .orbit-statstrip__value {
    font-family: var(--font-body, 'VT323', ui-monospace, monospace);
    font-size: 32px;
    line-height: 1;
    color: var(--color-ink);
    font-variant-numeric: tabular-nums;
  }

  /* One cell. Layout only — the strip's violet frame belongs to the container the component renders,
     never to each cell, or three borders stack into a table. */
  .orbit-page .orbit-statstrip { /* layout only */ }

  /* The trio's row: three across at `sm` and up; below `sm` it stacks and each control goes full
     width, KEEPING the run -> stop -> off order so the destructive control stays last. Button height
     is `.orbit-trio__btn` above, never set here. ⚠️ Scope the `@media` block's rules too — a bare
     rule inside a media query is still a bare rule. */
  .orbit-page .orbit-trio { /* three across at sm+, stacked below */ }
```

⚠️ **These four are part of Task 1's deliverable, not Task 2's or Task 4's.** Every `.orbit-*` class
in this codebase lives in `arcade.css` — including `.orbit-step*`, which are a *component's* own
classes (`arcade.css:722`). Do not put them in a component `<style>` block.

- [ ] **Step 5: Verify.** `yarn lint` — clean. `yarn build` — clean. Nothing renders differently yet (no consumer); that is expected.

- [ ] **Step 6: Commit.**

```bash
git add src/assets/styles/colors.css src/assets/styles/arcade.css
git commit -m "Add the /orbit page type scale and the 56px control token."
git push origin feature/orbit-new-ui
```

---

## Task 2: `OrbitStatStrip.vue`

**Files:** Create `src/components/orbit/OrbitStatStrip.vue`

**Interfaces:**
- Consumes: `.orbit-statstrip*` from Task 1. ⚠️ **Not `.orbit-stat*`** — that name belongs to `OrbitStatusBanner.vue`'s own scoped styles and must not be reused here.
- Produces: `<OrbitStatStrip :holds="number" :per-round="number | null" :token-label="string" />`. Purely presentational — no `useOrbit` import.

- [ ] **Step 1: Write the component.** **Two** cells, violet frame, 32px VT323 values, 9px ink-mute captions. `perRound` is nullable because there is not always a game to divide by — render `—`, never a fabricated `0`. A fake zero on a money readout is worse than an obvious blank.

⚠️ **There is no third cell, and mockup 2's `WALLET BALANCE` must not be re-added.** The page has no
non-duplicate source for it: `useOrbit`'s `balance` is `orbit.value?.balance` (`useOrbit.ts:182`) —
the *Orbit* balance, identical to `ORBIT HOLDS`. The real wallet balance is component-local state in
`TokenBalance.vue` behind an on-chain poll, reachable only by lifting it into a shared composable or
by running a second RPC read, and the app header already displays it. Owner ruling, 2026-08-06. Carry
that reason as a comment on the component, or the next reader re-adds the cell from the mockup.

```vue
<script setup lang="ts">
  defineProps<{
    holds: number;
    perRound: number | null;
    tokenLabel: string;
  }>();
</script>
```

- [ ] **Step 2: Add `class="orbit-page"` to the page's outer wrapper.** ⚠️ **Do this first, and do not skip it.** Task 1 scoped every one of its classes under `.orbit-page`, but nothing in the app applies that class yet — so all of them are currently **inert**. `OrbitPage.vue`'s outer wrapper is the `<div class="mx-auto w-full max-w-[1000px] …">` (around line 672); the class goes **there**, not on the `.arcade-frame arcade-frame--orbit` card nested inside it, because the control trio renders outside the card but inside the wrapper.

  If your strip renders unstyled, this is why. Do **not** work around it by writing local styles in the component — that reintroduces the page/panel divergence this whole card exists to remove.

- [ ] **Step 3: Mount it in its FINAL position** — top of `OrbitPage.vue`'s signed-in block, above the step list (which becomes the notice band + tab bar in Task 6) — wired to `orbit.balance`, `activeGameType?.requiredAmount` and `orbit.tokenLabel`. ⚠️ Do **not** wire `useOrbit`'s `balance` — it is the same value as `orbit.balance` (see Step 1's warning), and if the page no longer uses it, drop it from the destructure so lint stays clean. ⚠️ This is a **permanent** mount, not scaffolding: the strip's position is already known, so it goes there now rather than being moved later. In the same edit, **remove `<OrbitStatusBanner>` from the page** — the strip replaces it here. The component file itself is untouched; it stays the modal's.

- [ ] **Step 4: Verify in the browser.** Dev server from the worktree on a **verified-free** port (⚠️ another worktree commonly holds 8081; a collision fails quietly and you test the wrong build — confirm by grepping the served bundle for `OrbitStatStrip`). Check: the strip is **styled** (32px values — if they render at body size, Step 2 was missed); values match what `OrbitStatusBanner` showed before removal; `—` renders when no game is selected.

- [ ] **Step 5: Verify the panel did not move.** Open the Orbit modal on `/game/play` and confirm `OrbitStatusBanner` still renders there unchanged — the page stopped using it, the modal did not.

- [ ] **Step 6: Verify.** `yarn lint` + `yarn build` — clean.

- [ ] **Step 7: Commit.**

```bash
git add src/components/orbit/OrbitStatStrip.vue src/pages/OrbitPage.vue
git commit -m "Add the Orbit stat strip."
git push origin feature/orbit-new-ui
```

---

## Task 3: `OrbitTabBar.vue`

**Files:** Create `src/components/orbit/OrbitTabBar.vue`; modify `src/types/orbit.ts` and `src/assets/styles/arcade.css`

⚠️ **`arcade.css` is in scope here.** Task 1 gave `.orbit-tab` its type and colour but not its box, so
this task adds the layout (`display`, `align-items`, `gap`, `cursor`, `white-space`, horizontal
padding with its `md` step) to `.orbit-page .orbit-tab`. It does **not** go in the template: stacking
Tailwind utilities on an arcade class is forbidden by the global constraints.

⚠️ **The focus ring on this page is VIOLET, not cyan.** "Cyan is the focus ring" is the app-wide
default, and Orbit surfaces deliberately override it — `arcade.css:1032-1038` already repaints it for
`.arcade-frame--orbit`/`.dialog--orbit`, set by commit `f7716c7` ("put Orbit's focus ring in violet").
Add `.orbit-page .orbit-tab:focus-visible` to that existing block rather than writing a separate
rule, so the exception stays in one place.

**Interfaces:**
- Consumes: `.orbit-tabs`, `.orbit-tab`, `.orbit-tab--active` from Task 1 — all scoped under `.orbit-page`, which `OrbitPage.vue`'s outer wrapper already carries as of Task 2.
- Produces: `OrbitTabDef` in `src/types/orbit.ts`, and `<OrbitTabBar :tabs="OrbitTabDef[]" :active="string" @select="(id: string) => void" />`. Generic — it must not import `useOrbit` or know any Orbit concept.

- [ ] **Step 1: Add the type to `src/types/orbit.ts`** (not to the component):

```ts
/** One tab on the /orbit page. `index` is the pip number shown in the strip, 1-based. */
export interface OrbitTabDef {
  id: string;
  label: string;
  index: number;
}
```

⚠️ **The type cannot live in the component.** Vue's `<script setup>` compiles to a setup function and
**cannot contain ES module exports** — `export interface` inside it does not compile. No SFC in this
codebase does it, and shared types already live in `src/types/`.

- [ ] **Step 2: Write the component** with real tab semantics, not styled buttons:

```vue
<script setup lang="ts">
  import type { OrbitTabDef } from '@/types/orbit';

  defineProps<{ tabs: OrbitTabDef[]; active: string }>();
  defineEmits<{ (event: 'select', id: string): void }>();
</script>
```

Markup requirements: the container is `role="tablist"`; each control is `role="tab"` with
`:aria-selected`, `:aria-controls` pointing at the panel id, and `:tabindex="isActive ? 0 : -1"`
(roving tabindex — a tablist takes **one** tab stop, then Left/Right moves between tabs).

- [ ] **Step 3: Add Left/Right arrow-key handling** that moves the active tab and focuses the newly active control. Without it the roving tabindex in Step 2 traps keyboard users on one tab.

- [ ] **Step 4: Scroll the active tab into view on change** so a phone never leaves the active tab off-screen:

```ts
  watch(
    () => props.active,
    async (id) => {
      await nextTick();
      document
        .getElementById(`orbit-tab-${id}`)
        ?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    }
  );
```

⚠️ `behavior: 'smooth'` is deliberately omitted — `usePerfMode` forces `light-graphics` below `lg`,
which is exactly where this fires.

- [ ] **Step 5: Do NOT mount it in this task.** The tab bar's only honest position is above the tab panels, and those do not exist until Task 5 — a temporary mount here would be scaffolding that Task 6 deletes. Its keyboard and scroll behaviour is verified in **Task 6 Step 7**, where it is wired for real. Verify here only that it compiles and lints clean.

- [ ] **Step 6: Verify.** `yarn lint` + `yarn build` — clean.

- [ ] **Step 7: Commit.**

```bash
git add src/components/orbit/OrbitTabBar.vue src/types/orbit.ts
git commit -m "Add the Orbit tab bar."
git push origin feature/orbit-new-ui
```

---

## Task 4: `OrbitControlBar.vue`

**Files:** Create `src/components/orbit/OrbitControlBar.vue`; modify `src/types/orbit.ts`, `src/hooks/useOrbit.ts` and `src/pages/OrbitPage.vue`

**Interfaces:**
- Consumes: `.orbit-trio*`, `--ctl-h-xl` from Task 1.
- Produces: `<OrbitControlBar :status="OrbitStatus" :busy="boolean" :locked-label="string | null" @run @stop @turn-off />` where `OrbitStatus` is the union already exported by the `orbitStatus` computed in `useOrbit.ts:774`. **It performs no mutation** — the page shell owns the actions.

- [ ] **Step 1: Write the state mapping.** This is the whole point of the component and it must cover **all seven** values (spec §5.2). `current` means the state you are already in: filled, but not activatable.

⚠️ **First, make `OrbitStatus` a single source of truth.** Today the union exists **only inline** as the
type argument of the `orbitStatus` computed (`useOrbit.ts:773`), so declaring it again in this
component would leave two copies that drift silently the first time a state is added or renamed —
TypeScript would not catch it, because each file would be internally consistent. Add it to
`src/types/orbit.ts` and have **both** `useOrbit.ts` and this component import it:

```ts
/** What Orbit is doing right now. Order matters where it is computed, not here. */
export type OrbitStatus =
  | 'signed-out'
  | 'off'
  | 'cooling-off'
  | 'stopped'
  | 'unfunded'
  | 'paused'
  | 'running';
```

Then in `useOrbit.ts`, `computed<OrbitStatus>(...)` replaces the inline union. That is a **type-only**
change — no new state, no behaviour change, and the existing ordering comments stay exactly as they
are.

```ts
  import type { OrbitStatus } from '@/types/orbit';

  type TrioState = 'primary' | 'enabled' | 'current' | 'disabled';

  const props = defineProps<{
    status: OrbitStatus;
    busy: boolean;
    lockedLabel: string | null;
  }>();

  const run = computed((): { state: TrioState; label: string; note: string } => {
    switch (props.status) {
      case 'off':
        return { state: 'primary', label: 'RUN ORBIT', note: 'Start automated mode' };
      case 'stopped':
        return { state: 'enabled', label: 'RESUME', note: 'Start automated mode' };
      case 'running':
        return { state: 'current', label: 'RUNNING', note: 'Automated mode is on' };
      case 'unfunded':
      case 'paused':
        return { state: 'disabled', label: 'RUN ORBIT', note: 'Add money first' };
      case 'cooling-off':
        return { state: 'disabled', label: 'RUN ORBIT', note: props.lockedLabel ?? 'Cooling off' };
      default:
        return { state: 'disabled', label: 'RUN ORBIT', note: '' };
    }
  });

  const stop = computed((): { state: TrioState; label: string; note: string } => {
    switch (props.status) {
      case 'running':
        return { state: 'enabled', label: 'STOP ORBIT', note: 'Pause automated mode' };
      case 'stopped':
        return { state: 'current', label: 'STOPPED', note: 'Automated mode is paused' };
      default:
        return { state: 'disabled', label: 'STOP ORBIT', note: 'Pause automated mode' };
    }
  });

  // ⚠️ Enabled in unfunded AND paused on purpose: a player can hold a balance in an Orbit that never
  // ran, and turning off is how they get it back. Disabled in `off` (nothing to leave) and in
  // `cooling-off` (already off).
  const turnOff = computed((): { state: TrioState; label: string; note: string } => {
    switch (props.status) {
      case 'running':
      case 'stopped':
      case 'unfunded':
      case 'paused':
        return { state: 'enabled', label: 'TURN ORBIT OFF', note: 'Cash out and leave' };
      default:
        return { state: 'disabled', label: 'TURN ORBIT OFF', note: 'Cash out and leave' };
    }
  });
```

- [ ] **Step 2: Render the three buttons.** `signed-out` renders **nothing** (`v-if="status !== 'signed-out'"`) — the page shows the sign-in pitch instead. A `current` button must be `:disabled="false"` but **not activatable**: give it `aria-disabled="true"`, no `@click`, and an accessible name that states the condition (`Orbit is running`) so a screen reader is not told to press it. `TURN ORBIT OFF` renders as an **outline** control — it forces a cash-out and starts the cool-off, and must not carry the same visual pull as the reversible pause beside it. `busy` disables all three.

- [ ] **Step 3: Stack on mobile.** Below `sm` the trio is full-width and stacked, **keeping the order run → stop → off** so the destructive control stays last.

- [ ] **Step 4: Mount it in its FINAL position and wire the three actions.** ⚠️ **Permanent**, not scaffolding — outside and below the card, per spec §5.1. Wire the emits in `OrbitPage.vue` in the same edit so the trio ships working rather than as a dead control:

  - `run` → `updateSettings(wallet, { enabled: true })` when status is `off`; `updateSettings(wallet, { stopped: false })` when status is `stopped`.
  - `stop` → `updateSettings(wallet, { stopped: true })`. Moves no money, so it needs **no** signature.
  - `turn-off` → move the existing `onTurnOff` (`OrbitPage.vue:615`) **verbatim**.

  In the same edit, **remove the old turn-off button from the page's utility strip** — the trio owns that action now, and leaving both would ship two controls that force a cash-out.

⚠️ **Keep `onTurnOff`'s ordering exactly as it is.** It reads the balance *before* the patch:

```ts
    const cashedOut = orbit.value?.balance ?? 0;
    const label = orbit.value?.tokenLabel ?? '';
    await updateSettings(wallet.value, { enabled: false });
```

By the time the patch returns the balance is already `0`, so reading it afterwards reports "there was
no balance to cash out" to a player whose money was just sent — the toast would contradict their
wallet. `updateSettings` owns the signing, including the mobile-deeplink hop; do not add a signature
here.

- [ ] **Step 5: Verify in the browser.** Walk the states reachable without money: `signed-out` (nothing renders), `off` (RUN primary, other two disabled), `cooling-off` (all disabled, locked label shown). The funded states are covered by the Task 12 manual pass.

- [ ] **Step 6: Verify.** `yarn lint` + `yarn build` — clean.

- [ ] **Step 7: Commit.**

```bash
git add src/components/orbit/OrbitControlBar.vue src/pages/OrbitPage.vue
git commit -m "Add the Orbit control trio."
git push origin feature/orbit-new-ui
```

---

## Task 5: Extract the five tab bodies (behaviour-preserving)

**Goal of this task: change nothing a player can see.** It is a pure move, so a reviewer can check
"identical behaviour" here and judge the visuals separately in Tasks 7–11.

**Files:** Create the five `src/components/orbit/tabs/*.vue`; modify `src/pages/OrbitPage.vue`

**Interfaces:**
- Consumes: nothing from Tasks 1–4.
- Produces: five components, each self-contained. Each imports `useOrbit()` **itself** rather than receiving twenty props — the composable is module-level singleton state, so this adds no fetching and no duplicated requests. Each emits `(event: 'notice', text: string)` for the page-level band.

- [ ] **Step 1: `OrbitGameTab.vue`** — move the step-1 body plus `onToggleAutoPick`, the word save/clear handlers and the game-type selection. Reuse `OrbitWordField.vue` **unmodified**.

- [ ] **Step 2: `OrbitMoneyTab.vue`** — move the step-2 deposit body plus `canDeposit` and `onDeposit`, **and** the separate cash-out section plus its handler. This is the one task that merges two existing surfaces (spec D5). Reuse `OrbitAmountField.vue` **unmodified** for both.

- [ ] **Step 3: `OrbitStopRulesTab.vue`** — move the step-3 body plus `onSaveThreshold`, `onSaveFloor` and their clear paths, including the `thresholdError`/`floorError` refs and the minimum-value messages.

- [ ] **Step 4: `OrbitRecoverTab.vue`** — move the recover section plus `recoverySignature` and `onRecover`.

- [ ] **Step 5: `OrbitTelegramTab.vue`** — move the step-4 body plus `telegramLink`, `onStartTelegramLink`, `onVerifyTelegram`, `onUnlinkTelegram` and the three notify toggles.

- [ ] **Step 6: Render them from the still-existing step rows.** `OrbitPage.vue` keeps its `OrbitStepRow` list for now; each row's default slot becomes `<OrbitGameTab />` etc. The page's script sheds the moved handlers.

- [ ] **Step 7: Verify in the browser — the important check.** Every action still works and looks the same: deposit, cash out, save/clear both rules, recover, link/unlink Telegram, toggle auto-pick. Nothing moved position.

- [ ] **Step 8: Verify.** `yarn lint` + `yarn build` — clean.

- [ ] **Step 9: Commit.**

```bash
git add src/components/orbit/tabs src/pages/OrbitPage.vue
git commit -m "Extract the Orbit page's five sections into components."
git push origin feature/orbit-new-ui
```

---

## Task 6: Swap the step list for tabs

**Files:** Modify `src/pages/OrbitPage.vue`; **delete** `src/components/orbit/OrbitStepRow.vue`

**Interfaces:**
- Consumes: `OrbitTabBar` (Task 3), `OrbitStatStrip` (Task 2), `OrbitControlBar` (Task 4), the five tabs (Task 5).
- Produces: the final shell. Later tasks only touch tab internals.

- [ ] **Step 1: Replace the step list** with `<OrbitTabBar>` + a single `role="tabpanel"` region rendering the active tab. Tab defs:

```ts
  const TABS = [
    { id: 'game', label: 'GAME', index: 1 },
    { id: 'money', label: 'MONEY', index: 2 },
    { id: 'stop-rules', label: 'STOP RULES', index: 3 },
    { id: 'recover', label: 'RECOVER', index: 4 },
    { id: 'telegram', label: 'TELEGRAM', index: 5 },
  ];
```

- [ ] **Step 2: Default tab.** `game`, except open on `money` when `needsFirstDeposit` or `isPausedForFunds` is true — this preserves what the step list did by un-collapsing step 2. Compute the initial value **once on mount**; do not `watch` it, or the page yanks the player back to `money` mid-edit every time the balance changes.

- [ ] **Step 3: The trio is already mounted and wired** (Task 4 Step 4) — do **not** re-wire it. Verify only that its three actions still behave after the shell rewrite, and that `onTurnOff` still reads the balance *before* the patch:

⚠️ **Keep `onTurnOff`'s ordering exactly as it is.** It reads the balance *before* the patch:

```ts
    const cashedOut = orbit.value?.balance ?? 0;
    const label = orbit.value?.tokenLabel ?? '';
    await updateSettings(wallet.value, { enabled: false });
```

By the time the patch returns the balance is already `0`, so reading it afterwards reports "there was
no balance to cash out" to a player whose money was just sent — the toast would contradict their
wallet. `updateSettings` owns the signing, including the mobile-deeplink hop; do not add a signature
here.

- [ ] **Step 4: Move the notice/error band above the tab bar**, inside the card, and have it consume each tab's `notice` emit. A deposit error rendered *inside* `money` vanishes the moment the player opens `stop-rules`.

- [ ] **Step 5: Confirm the final vertical order** per spec §5.1. The stat strip (Task 2) and the trio (Task 4) are already in place; this step only positions the notice band and the bet log around them: header → stat strip → notice band → tab bar → active panel → **card ends** → trio → bet log. `OrbitStatusBanner` was already removed from the page in Task 2.

- [ ] **Step 6: Delete `OrbitStepRow.vue`** and confirm no imports remain:

```bash
grep -rn "OrbitStepRow" src/ || echo "no references"
```

- [ ] **Step 7: Verify in the browser — including the tab bar behaviour deferred from Task 3.** All five tabs reachable; every action still works; the page opens on `money` when unfunded; the bet log still loads only when shown. Then, at 1280px **and 360px**: the strip scrolls, Left/Right arrows move the active tab and move focus with it, Tab enters the strip **once** (roving tabindex), and the active tab scrolls into view on change.

- [ ] **Step 8: Verify.** `yarn lint` + `yarn build` — clean.

- [ ] **Step 9: Commit.**

```bash
git add -A src/pages/OrbitPage.vue src/components/orbit
git commit -m "Replace the Orbit page's step list with tabs."
git push origin feature/orbit-new-ui
```

---

## Tasks 7–11: Restyle each tab to the page scale

Each task is the same shape, one tab at a time, so a reviewer can reject one tab's design without
blocking the rest. For **each**: apply `.orbit-section-title` for the block heading,
`.orbit-field-label` for field labels, `.dialog-copy` for helper text, `--ctl-h-lg` for field
heights, and the `ⓘ` affordance beside labels that need explanation (per the mockups). Helper text
sits **under** its field. Keep copy from the spec, not the mockups.

⚠️ **`OrbitWordField` and `OrbitAmountField` are shared with the modal — wrap them, never edit them.**
If a field needs a different size on the page, achieve it with the `.orbit-page` scope, not by
touching the component.

- [ ] **Task 7 — `① GAME`.** Word field, auto-pick opt-in, game mode select. Section title `GAME SETUP`. Verify: preset types show the picker, free-text types show letter cells; saving a word turns auto-pick off and vice versa. Lint + build. Commit `"Restyle the Orbit game tab."` Push.
- [ ] **Task 8 — `② MONEY`.** Add money and cash out, side by side above `lg`, stacked below. Cash out stays an **outline** control, never amber. Verify: MAX fills the full balance; amounts below `MinimumCashOut` are refused inline. Lint + build. Commit `"Restyle the Orbit money tab."` Push.
- [ ] **Task 9 — `③ STOP RULES`.** Threshold above, floor below — ⚠️ **rule order is fixed everywhere** (rise on top, balance between, fall underneath; `orbit-ui-overhaul.md` §3.1). An **off** rule is drawn, never hidden. Verify: saving and clearing each rule independently. Lint + build. Commit `"Restyle the Orbit stop rules tab."` Push.
- [ ] **Task 10 — `④ RECOVER`.** Signature + amount + confirm, with a line explaining what this is for — it is the one tab a player arrives at confused. Verify: an arbitrary whole amount is accepted. Lint + build. Commit `"Restyle the Orbit recover tab."` Push.
- [ ] **Task 11 — `⑤ TELEGRAM`.** Link/verify/unlink and the three toggles. Verify: defaults are cash-out **on**, zero-out **on**, any-win **off**. Lint + build. Commit `"Restyle the Orbit telegram tab."` Push.

---

## Task 12: Screenshot-compare + the seven-state manual pass

⚠️ Required by `conventions/frontend.md` — *"Screenshot-compare the running surface against the mockup
at desktop and phone widths before hand-off. Looking at it is the step that catches what tests never
will."*

**Files:** none (verification only)

- [ ] **Step 1: Bring up the stack.** Backend on `:6001`. ⚠️ After a pull the front dev server self-recompiles but the backend image does **not** — rebuild with `docker compose up -d --build --no-deps api consumers`. If `database`/`redis` exited, use `docker start` (it preserves the 5433/6380 host remap); never `compose up`, which reverts it.
- [ ] **Step 2: Screenshot `/orbit` at 1280px** and compare against mockup 2 — tab strip, stat strip, trio placement, type ranking.
- [ ] **Step 3: Screenshot `/orbit` at 360px** — strip scrolls, active tab in view, trio stacked full-width in run/stop/off order.
- [ ] **Step 4: Walk all seven states** (spec §11), using the free-bets operator via `POST /dev/launch-token` + `#launch_token=`:
  1. Signed out → pitch, no tabs, no trio.
  2. `off` → RUN primary; STOP and TURN OFF disabled.
  3. `unfunded` → opens on `② MONEY`; RUN disabled reading `Add money first`; **TURN OFF enabled**.
  4. Deposit → `running`; RUN filled and inert; STOP and TURN OFF live.
  5. STOP → `stopped`; STOP inert, RUN reads `Resume`. Confirm balance intact, **no** cool-off, **no** forced cash-out.
  6. Balance below the stake → `paused`; RUN disabled, TURN OFF live.
  7. TURN OFF → forced cash-out, then `cooling-off`; all three disabled with `enableLockedLabel` shown.
- [ ] **Step 5: Cross-tab notice check.** Raise a deposit error in `② MONEY`, switch to `③ STOP RULES` — the notice is still visible.
- [ ] **Step 6: Confirm the modal is unchanged.** Open Orbit on `/game/play` and compare against `66e4df3`.

---

## Task 13: Verify and hand off

- [ ] **Step 1:** `yarn lint` + `yarn build` — clean.
- [ ] **Step 2: Prove the shared components were not touched** (spec AC12):

```bash
git diff --stat 66e4df3 -- \
  src/components/orbit/OrbitStatusBanner.vue \
  src/components/orbit/OrbitAmountField.vue \
  src/components/orbit/OrbitWordField.vue \
  src/components/orbit/OrbitModal.vue
# expected: no output
```

- [ ] **Step 3: Prove the backend is untouched** (spec AC17): in the backend worktree, `git diff --stat 33e396e` → no output.
- [ ] **Step 4: Check the size targets** (spec AC14): `wc -l src/pages/OrbitPage.vue` < 350; no new file over 300.
- [ ] **Step 5:** Push, then build the compare link from `monkify-front`'s **own** `origin` (the three repos have different owners).
- [ ] **Step 6:** ⚠️ **Do not merge to `main` before `feature/orbit-ui-overhaul` lands** (spec AC18) — this branch's ancestry contains #141 **and** #150, so merging first drags both in.

---

## Self-review

**Spec coverage.** §4 type scale → Task 1. §5.1 structure → Tasks 2, 6. §5.2 seven states → Task 4
(mapping) + Task 12 (walk). §5.3 tabs → Tasks 5–11; default-tab rule → Task 6 Step 2. §5.4 mobile →
Task 3 Steps 3–4, Task 4 Step 3, Task 12 Step 3. §5.5 errors → Task 6 Step 4, Task 12 Step 5. §7 file
checklist → File Structure. AC12 → Task 13 Step 2. AC17 → Task 13 Step 3. AC14 → Task 13 Step 4. AC18
→ Task 13 Step 6. `conventions/frontend.md` design workflow → Task 1 Step 1 + Task 12.

**Gap found and closed:** the first draft had no task for the `conventions/frontend.md` design
workflow (mockups → `impeccable` → screenshot-compare), which is **required** for any new visual
surface. It is now Task 1 Step 1 and Task 12.

**Type consistency.** `OrbitStatus` in Task 4 matches the union at `useOrbit.ts:774` exactly.
`OrbitTabDef` (Task 3) matches the `TABS` literal (Task 6 Step 1) — `id`, `label`, `index`. Tab ids
`game`/`money`/`stop-rules`/`recover`/`telegram` are used identically in Tasks 6–11. `TrioState`
values (`primary`/`enabled`/`current`/`disabled`) are used consistently across all three computeds.

**Placeholder scan.** No TBD/TODO. Every code step carries real code; every verification step names
the command and the expected result.

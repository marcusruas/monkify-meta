# Orbit UI/UX overhaul — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended)
> or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`)
> syntax for tracking.

**Goal:** Rebuild every Orbit surface onto the shipped design system — a compact 380 `ArcadeModal`
for money you move mid-round, a new `/orbit` page for the rules you set once, a console-cabinet
module on `/game/play`, one violet accent + a mark + four glyphs — and close the four flow items
(first-win reskin, copy pass, mobile deeplink sign-in, declinable redirect).

**Architecture:** Frontend-only (`monkify-front`). No backend change at all — `monkify-backend` gets
no branch and must diff clean against `main` (AC33). Every state the new UI renders already exists on
`GET /orbit` or is already computed client-side. The one new piece of shared state is derived, not
fetched, which is what makes AC18 (header and module never disagree) true by construction.

**Tech Stack:** Vue 3.2 `<script setup>` + TypeScript 5.5, Vue CLI 5 (webpack), Tailwind 3 + DaisyUI,
`vue3-toastify`, `vue-router` 4. Package manager: **yarn**.

## Global Constraints

Copied verbatim from `specs/orbit-ui-overhaul.md` §2 and `conventions/frontend.md`. Every task's
requirements implicitly include this section.

- **Worktrees:** all work happens in `<workspace-root>/.worktrees/orbit-ui-overhaul/monkify-front`
  and `.../monkify-meta`, branch `feature/orbit-ui-overhaul`. The shared checkouts stay on `main`.
- **There is NO test framework in `monkify-front`** — no `.spec`/`.test` file, no runner in
  `package.json`. TDD's failing-test step does not apply. **The per-task verification cycle is
  `yarn lint` + `yarn build`, plus a look at the running surface where the task is visual.** No
  integration test either: this is a UI overhaul of an existing feature with zero backend change, and
  `OrbitFlowTests.cs` already covers Orbit's happy path (`AGENTS.md` rule 8, spec §11).
- **`yarn lint` needs uniform CRLF.** The repo is on `core.autocrlf=true`. Never edit with a tool that
  writes LF; if prettier reports `Insert ␍`, fix with `unix2dos <file>` and verify with `od -c`.
- **Every overlay goes through `ArcadeModal.vue`.** No `<Teleport>`, no hand-rolled backdrop, no
  per-component `keydown` Esc listener (`useOverlayStack` owns Esc), no consumer-declared `max-width`
  or `max-height`, no literal `z-*`.
- **Every control takes its height from `--ctl-h-sm|md|lg`.** No control derives its height from
  padding. The `≤1023px` block in `colors.css` steps the tokens up — **no call-site class changes on
  mobile, ever**.
- **Never stack a Tailwind utility on an arcade class for a property that class declares.**
  `arcade.css` is imported after `@tailwind utilities` at equal specificity and wins. Use the class
  as-is, use plain Tailwind for a differently-shaped control, or add a modifier in `arcade.css`.
- **Dither, never blur.** No `backdrop-filter`, no `blur()`, no infinite animation, no wide glow
  anywhere in this work — `html.light-graphics` strips all four and every phone runs it.
- **Grey vs black is a rule.** Orbit modal + `/orbit` page = **grey**. First-win upsell = **black**.
- **Violet is identity only:** `--color-orbit: oklch(0.72 0.17 292)`. The mark, the balance figure's
  `USDC` marker, the on/off checkbox (Orbit's own on-state), the `STOP RULES` block's left edge, and
  the not-yet-enabled picker prompt's border. **Hover, focus, input borders and every primary action
  stay amber; the focus ring stays the single cyan element.** When Orbit **pauses** the band and label
  go **amber**.
- **Copy rules that may not be shortened away** (spec §7.10):
  - `constraints.md` §6 — a forged session, an unknown nonce and a missing session stay
    **indistinguishable**; the ordinary-state refusals (already cashed out today, below the minimum
    cash-out, no `/start` seen yet, deposit refused while off or in cool-off) stay **clear and name
    their reason**. Neither direction may be flipped.
  - Threshold vs floor state opposite conditions verbatim in effect:
    *"Orbit sends your balance to your wallet as soon as **a win takes it to** 20.00."* /
    *"Orbit stops and sends what is left to your wallet if **a bet takes your balance down to** 5.00."*
  - *"Turning this off cashes your whole balance out to your wallet."* appears on the modal's on/off
    band **and** above `TURN ORBIT OFF` on `/orbit`.
  - **"Paused" is reserved** for `isPausedForFunds` (`orbitBetsPlaced > 0`). A freshly enabled Orbit
    is **unfunded**, not paused.
  - **Cool-off is a fact with a time, not a fault** — `ERR` skin, tag **`WAIT`**.
- **Rule order is fixed everywhere:** rise on top, balance between, fall underneath — desktop, phone,
  and the modal's seam row.
- **Design source of truth:** the Option C artifact, unpacked to
  `<scratchpad>/optionC/template.pretty.html`. It outranks this plan's prose **on design**; **copy
  comes from the card's decisions and the rules above**.
- **Secrets:** no config file is touched by this card. Mock/placeholder values only if that changes.
- **Two decisions taken with the user (2026-08-03), recorded so they are not re-litigated:**
  1. The `ORBIT PLAYS` checkbox is **violet** (follow the mockup). AC40's "control border" ban covers
     generic chrome — hover, focus rings, input borders, primary actions — not the one control that
     *is* Orbit. To be reviewed at hand-off.
  2. `/orbit` with Orbit **off** renders the **full page plus an OFF notice**: rules stay drawn and
     editable (the cool-off state proves rules survive a disable — *"Kept: WIN AT 20.00"*), the
     balance reads `0.00` muted, and a band says Orbit is off with `OPEN ORBIT PANEL` as the way on.

---

## File Structure

### `monkify-front`

| File | Responsibility |
| --- | --- |
| `src/assets/styles/colors.css` | **Modify.** `--color-orbit` / `--color-orbit-deep` beside the existing accents, with the one-accent-exception comment. |
| `tailwind.config.js` | **Modify.** `orbit` / `orbit.deep` in `theme.extend.colors`, so call sites write `text-orbit` rather than two spellings of one token. |
| `src/components/orbit/OrbitMark.vue` | **New.** The one shared Orbit mark — 16px grid, 2px pixels, `currentColor`. Nothing re-inlines the path (AC15). |
| `src/components/orbit/OrbitGlyph.vue` | **New.** The four action glyphs on the same grid. `deposit`/`cash-out` are `currentColor`; `win-at`/`falls-to` carry the semantic OK/ERR hues (AC16). |
| `src/hooks/useOrbit.ts` | **Modify.** Derived state the new surfaces render (`isOrbitEligibleType`, `stopRules`, `recentOrbitWin`, `hasCashOut*`, `lastForcedCashOut`) + the whole mobile-deeplink branch. **No new network call.** |
| `src/hooks/useHeaderModals.ts` | **Modify.** `orbitOpen` + `openOrbit()` as module singletons, so `/orbit` and the console module open the header-owned instance (F1/D14). |
| `src/hooks/useFirstWinUpsell.ts` | **Modify.** `orbitOpen` joins the never-two-modals guard (F2). |
| `src/components/ui/ArcadeModal.vue` | **Modify.** A `titlebar-lead` slot so the mark rides in the grey titlebar without a call-site hack. |
| `src/components/orbit/OrbitModal.vue` | **Rebuild.** Compact 380 `ArcadeModal variant="grey"`, five states, the `STOP RULES` seam row. |
| `src/components/orbit/OrbitFirstWinModal.vue` | **Reskin.** `ArcadeModal variant="black"`; behaviour untouched. |
| `src/pages/OrbitPage.vue` | **New.** `.arcade-frame .arcade-frame--grey`, one 1000px frame, three bands, the drawn ladder. |
| `src/routes.ts` | **Modify.** The `Orbit` route. |
| `src/main.ts` | **Modify.** `'Orbit'` joins `EMBED_REDIRECT_ROUTES`. |
| `src/components/GameHeader.vue` | **Modify.** Local `isOrbitOpen` → `useHeaderModals().orbitOpen`. |
| `src/components/OrbitBalance.vue` | **Modify.** Subordinate readout: the mark at 12px, no violet fill or border of its own. |
| `src/components/orbit/OrbitConsoleModule.vue` | **New.** Cabinet furniture — balance + the ONE live state + the entrance. A documented design-system exception. |
| `src/components/game/GameConsole.vue` | **Modify.** Both inert `<p>`s out, `<OrbitConsoleModule>` into both branches. |
| `src/types/mobileWallet.ts` | **Modify.** `MobilePendingOrbit` with the three-value `action` discriminator. |
| `src/hooks/useMobileWallet.ts` | **Modify.** `PENDING_ORBIT_KEY`, `signMobileOrbit`, `getPendingOrbit` (stale-guarded), `clearPendingOrbit`; rename `readSelfExclusionSignature` → `readSignedMessageSignature`; the key joins `disconnectMobileWallet`. |
| `src/hooks/useSelfExclusion.ts` | **Modify.** Call-site update for the rename. No behaviour change. |
| `src/hooks/useGame.ts` | **Modify.** The Orbit arm in `handleMobileDeeplinkReturn`'s `'sign'` chain — **after** self-exclusion, **before** the bet. |
| `src/components/orbit/OrbitRedirectToast.vue` | **New.** The announcement plus `STAY HERE`. |
| `src/hooks/useOrbitRedirect.ts` | **Modify.** The announcement becomes a VNode toast; `declined` cancels the navigation. **Cadence and detection untouched.** |
| `src/hooks/useOrbitNotifications.ts` | **Modify.** `onWin` records the win as state beside the toast it already fires. Toast unchanged. |

### `monkify-meta`

`INDEX.md` already carries the spec row and the in-flight block. `conventions/frontend.md` and the
three `features/orbit-*.md` docs are **synced at ship** by `monkify-ship-feature`, not here.

---

## Task list

| # | Task | Depends on |
| --- | --- | --- |
| 1 | Identity layer — tokens, the mark, the four glyphs | — |
| 2 | Shared Orbit state — `useOrbit` derivations + the win-as-state write | — |
| 3 | Shared modal open-state — `useHeaderModals`, `GameHeader`, the upsell guard | — |
| 4 | `ArcadeModal` — the `titlebar-lead` slot | — |
| 5 | The compact 380 modal — rebuild `OrbitModal.vue` | 1, 2, 3, 4 |
| 6 | The `/orbit` page + route + embed redirect | 1, 2, 3 |
| 7 | The first-win upsell reskin | 3, 4 |
| 8 | The header row — subordinate readout | 1, 3 |
| 9 | The console module + both `GameConsole` branches | 1, 2, 3 |
| 10 | Mobile deeplink sign-in — the stash, the hop, the return | 2 |
| 11 | The declinable redirect | 1 |
| 12 | Copy-and-colour audit, full verification, screenshot pass | all |

---

## Task 1: Identity layer — tokens, the mark, the four glyphs

**Files:**
- Modify: `src/assets/styles/colors.css` (after `--color-live`, ~line 30)
- Modify: `tailwind.config.js` (`theme.extend.colors`, after `magenta`)
- Create: `src/components/orbit/OrbitMark.vue`
- Create: `src/components/orbit/OrbitGlyph.vue`

**Interfaces:**
- Produces: CSS custom properties `--color-orbit`, `--color-orbit-deep`; Tailwind utilities
  `text-orbit`, `border-orbit`, `bg-orbit`, `border-orbit-deep`.
- Produces: `<OrbitMark :size="number" />` — default `16`, colour from `currentColor`, always
  `aria-hidden`.
- Produces: `<OrbitGlyph name="deposit" | "cash-out" | "win-at" | "falls-to" :size="number" />` —
  default `16`. `deposit`/`cash-out` inherit `currentColor`; `win-at` is `--color-live`,
  `falls-to` is `oklch(0.8 0.16 25)`.

- [ ] **Step 1: Add the accent tokens**

In `src/assets/styles/colors.css`, immediately after the `--color-live` block:

```css
  /* — Orbit violet: the custodial wallet's identity. A KNOWING exception to the one-accent rule,
     beside GameTypeCard's three-way archetype accent (conventions/frontend.md → "Named exceptions").
     Hue 292 sits between the surfaces' 280 and magenta's 350, and nowhere near cyan's 205.
     6.1:1 on --color-surface, 10.4:1 on the console cabinet's near-black. FLAT — no glow: every
     phone runs html.light-graphics, which strips one, so a legibility that depended on it would be
     invisible on the device the console module is built for.
     ⚠️ IDENTITY ONLY: the mark, the balance figure's marker, the on/off control that IS Orbit, and
     the ownership of a rule. Hover, focus, input borders and every primary action stay AMBER; the
     focus ring stays the single cyan element. When Orbit PAUSES the band and label go amber — a call
     to act is not an identity.
     Cyan was rejected (it is the scarce one-per-grey-panel accent, the black panel's frame, AND
     Beast Mode's archetype accent — and the module renders inside Beast Mode); magenta was rejected
     (it means LOTTERY in GameTypeCard, the one thing Orbit cannot play).
     The 13% block tint is NOT a token — it is written
     `color-mix(in oklab, var(--color-orbit) 13%, transparent)` at the call site, matching how
     --color-bg-button and .btn-dialog--active are already written. */
  --color-orbit: oklch(0.72 0.17 292);
  --color-orbit-deep: oklch(0.5 0.13 292); /* hairlines / rules on the violet-owned blocks */
```

- [ ] **Step 2: Expose the token to Tailwind**

In `tailwind.config.js`, inside `theme.extend.colors`, immediately after the `magenta` line:

```js
        // Orbit's identity accent — see colors.css for the one-accent exception it knowingly makes.
        // Exposed here so call sites write `text-orbit` rather than drifting between that and
        // `text-[color:var(--color-orbit)]`.
        orbit: {
          DEFAULT: 'var(--color-orbit)',
          deep: 'var(--color-orbit-deep)',
        },
```

- [ ] **Step 3: Create the mark**

`src/components/orbit/OrbitMark.vue` — paths copied verbatim from the Option C artifact's
`#orb-mark` symbol:

```vue
<script setup lang="ts">
  /**
   * THE Orbit mark. One shared asset (AC15): the header row, the console module, the modal titlebar
   * and the /orbit page all render THIS — nothing re-inlines the path.
   *
   * 16px grid, 2px pixels, so every edge lands on the grid and it stays crisp at 12px. A body inside
   * a stepped orbital path, with the satellite thickened to a 4px node at the top right so the path
   * reads directional. Not a wallet, a gear or a coin.
   *
   * Colour is `currentColor` and the component sets none of its own — which is what keeps "violet is
   * identity" enforceable at the call site rather than baked in here.
   *
   * ⚠️ Orbit-scoped on purpose. This and OrbitGlyph are NOT the seed of an app-wide icon system
   * (spec D7): a documented rule with one feature's worth of consumers is exactly the dangling-rule
   * condition #124 was filed to fix.
   */
  withDefaults(defineProps<{ size?: number }>(), { size: 16 });
</script>

<template>
  <svg
    :width="size"
    :height="size"
    viewBox="0 0 16 16"
    fill="currentColor"
    aria-hidden="true"
    focusable="false"
    class="flex-none"
  >
    <rect x="6" y="0" width="4" height="2" />
    <rect x="4" y="2" width="2" height="2" />
    <rect x="10" y="2" width="4" height="4" />
    <rect x="2" y="4" width="2" height="2" />
    <rect x="0" y="6" width="2" height="2" />
    <rect x="14" y="6" width="2" height="2" />
    <rect x="0" y="8" width="2" height="2" />
    <rect x="14" y="8" width="2" height="2" />
    <rect x="2" y="10" width="2" height="2" />
    <rect x="12" y="10" width="2" height="2" />
    <rect x="4" y="12" width="2" height="2" />
    <rect x="10" y="12" width="2" height="2" />
    <rect x="6" y="14" width="4" height="2" />
    <rect x="6" y="6" width="4" height="4" />
  </svg>
</template>
```

- [ ] **Step 4: Create the four glyphs**

`src/components/orbit/OrbitGlyph.vue` — paths copied verbatim from the artifact's `#g-deposit`,
`#g-cashout`, `#g-win`, `#g-fall`:

```vue
<script setup lang="ts">
  import { computed } from 'vue';

  /**
   * The four Orbit action glyphs, same 16px/2px grid as OrbitMark.
   *
   * Each pair is ONE glyph flipped: deposit and cash-out share a bar; the two rules share three
   * columns against a line.
   *
   * ⚠️ The two RULE glyphs carry SEMANTIC colour — green for a rise, warm red for a fall, the same
   * two hues the dialog's OK/ERR strips already use — so nothing is stolen from the accents: none of
   * the four is amber, cyan or violet. deposit/cash-out stay `currentColor`, so the surface decides
   * (muted in the eyebrow, near-black inside the amber CASH OUT button).
   *
   * Orbit-scoped, like OrbitMark — no sprite sheet, no <Icon> wrapper, no registry (AC16).
   */
  const props = withDefaults(
    defineProps<{ name: 'deposit' | 'cash-out' | 'win-at' | 'falls-to'; size?: number }>(),
    { size: 16 }
  );

  // The ERR hue is written literally rather than tokenised: arcade.css's .dialog__strip--err states
  // it the same way, and adding a token for it is a design-system change this card is not making.
  const fill = computed(() => {
    if (props.name === 'win-at') return 'var(--color-live)';
    if (props.name === 'falls-to') return 'oklch(0.8 0.16 25)';
    return 'currentColor';
  });
</script>

<template>
  <svg
    :width="size"
    :height="size"
    viewBox="0 0 16 16"
    :fill="fill"
    aria-hidden="true"
    focusable="false"
    class="flex-none"
  >
    <template v-if="name === 'deposit'">
      <rect x="6" y="1" width="4" height="6" />
      <rect x="3" y="7" width="10" height="2" />
      <rect x="5" y="9" width="6" height="2" />
      <rect x="7" y="11" width="2" height="2" />
      <rect x="1" y="14" width="14" height="2" />
    </template>
    <template v-else-if="name === 'cash-out'">
      <rect x="7" y="1" width="2" height="2" />
      <rect x="5" y="3" width="6" height="2" />
      <rect x="3" y="5" width="10" height="2" />
      <rect x="6" y="7" width="4" height="6" />
      <rect x="1" y="14" width="14" height="2" />
    </template>
    <template v-else-if="name === 'win-at'">
      <rect x="0" y="0" width="16" height="2" />
      <rect x="1" y="11" width="3" height="5" />
      <rect x="6" y="7" width="3" height="9" />
      <rect x="11" y="2" width="3" height="14" />
    </template>
    <template v-else>
      <rect x="0" y="14" width="16" height="2" />
      <rect x="1" y="0" width="3" height="4" />
      <rect x="6" y="0" width="3" height="8" />
      <rect x="11" y="0" width="3" height="14" />
    </template>
  </svg>
</template>
```

- [ ] **Step 5: Verify**

```bash
cd <workspace-root>/.worktrees/orbit-ui-overhaul/monkify-front
yarn lint
yarn build
```

Expected: both clean. (Neither new component has a consumer yet — Vue CLI tree-shakes nothing here,
so a build failure would be a syntax error, not an unused-import warning.)

- [ ] **Step 6: Commit**

```bash
git add src/assets/styles/colors.css tailwind.config.js src/components/orbit/OrbitMark.vue src/components/orbit/OrbitGlyph.vue
git commit -m "Add Orbit's identity layer: the violet accent, the mark and the four glyphs."
```

---

## Task 2: Shared Orbit state — `useOrbit` derivations + the win-as-state write

**Files:**
- Modify: `src/hooks/useOrbit.ts`
- Modify: `src/hooks/useOrbitNotifications.ts:40-57` (the `onWin` handler)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces, all from `useOrbit()`:
  - `isOrbitEligibleType(gameTypeId: string | null | undefined): boolean`
  - `stopRules: ComputedRef<{ threshold: number | null; floor: number | null }>`
  - `hasCashOutThreshold: ComputedRef<boolean>` / `hasCashOutFloor: ComputedRef<boolean>`
  - `recentOrbitWin: ComputedRef<OrbitWinNotification | null>`
  - `recordOrbitWin(win: OrbitWinNotification): void`
  - `lastForcedCashOut: ComputedRef<number | null>`
  - `activeGameType: ComputedRef<OrbitEligibleGameType | null>`

- [ ] **Step 1: Add the module-level refs**

In `src/hooks/useOrbit.ts`, beside the existing `const orbit = ref<OrbitState | null>(null);` block:

```ts
/**
 * The last win Orbit reported, held as STATE beside the toast that announced it (D9 / AC19). The
 * toast says "it happened"; the console module says "it is true".
 *
 * ⚠️ It lives HERE and not in useOrbitNotifications, which the card named: that module transitively
 * imports useGameHub and useMobileWallet, and GameConsole must not pull the SignalR hub in to render
 * a label. useOrbitNotifications already imports useOrbit, so its onWin handler simply writes this.
 *
 * `betsPlacedAtWin` is what stops it going stale. "Won X last round" stops being true the moment
 * Orbit places another bet, so the win is dropped once orbitBetsPlaced moves past the value it had
 * when the win landed. That is a real signal already refreshed by refreshQuietly — no timer, and
 * nothing that has to be reactive to wall-clock time.
 */
const lastOrbitWin = ref<{ win: OrbitWinNotification; betsPlacedAtWin: number } | null>(null);

/**
 * What the forced cash-out sent back when the player turned Orbit off, for the cool-off state's
 * SENT BACK cell. Read BEFORE the patch, because by the time it returns the balance is already 0.
 *
 * In-session only, and deliberately not persisted: on a fresh load during a cool-off the honest
 * answer is that we do not know the amount, and the cool-off panel drops that cell rather than
 * inventing one.
 */
const lastForcedCashOut = ref<number | null>(null);
```

Add `OrbitEligibleGameType` and `OrbitWinNotification` to the existing `@/types/orbit` import.

- [ ] **Step 2: Clear both on `reset()`**

`reset()` is what re-scopes everything to a new wallet (AC31). Extend it:

```ts
  const reset = () => {
    orbit.value = null;
    sessionToken.value = null;
    sessionWallet.value = null;
    lastError.value = null;
    // A wallet switch must not leave the PREVIOUS wallet's win, or its forced cash-out, on screen.
    lastOrbitWin.value = null;
    lastForcedCashOut.value = null;
  };
```

- [ ] **Step 3: Add the derivations inside `useOrbit()`**

Place them next to `isPausedForFunds` / `needsFirstDeposit`:

```ts
  /**
   * Is `gameTypeId` a type Orbit is allowed to play? Derived from `eligibleGameTypes`, which the
   * backend already builds from GameParameters.AllowOrbit — so there is nothing new to fetch and no
   * new DTO field. Space Jackpot is absent from that list by seed, which is exactly AC08.
   *
   * Returns false while Orbit is unloaded: unknown is not "eligible", and the console module must
   * not flash in and then out on a slow boot.
   */
  const isOrbitEligibleType = (gameTypeId: string | null | undefined): boolean =>
    !!gameTypeId && !!orbit.value?.eligibleGameTypes.some((type) => type.id === gameTypeId);

  /** The type Orbit is pointed at, for the stake and the name every surface prints. */
  const activeGameType = computed<OrbitEligibleGameType | null>(() => {
    const state = orbit.value;
    if (!state?.gameParametersId) return null;
    return state.eligibleGameTypes.find((type) => type.id === state.gameParametersId) ?? null;
  });

  /**
   * ONE source for the two stop rules, so the modal's seam row, the page's ladder and the phone
   * layout cannot drift out of order (AC39: rise on top, balance between, fall underneath).
   */
  const stopRules = computed(() => ({
    threshold: orbit.value?.cashOutAtThreshold ?? null,
    floor: orbit.value?.cashOutFloor ?? null,
  }));

  const hasCashOutThreshold = computed(() => stopRules.value.threshold !== null);
  const hasCashOutFloor = computed(() => stopRules.value.floor !== null);

  /**
   * The last Orbit win, while it is still the LAST one. Superseded — not aged out — by the next
   * Orbit bet, so the console module never claims a win that a later round has already answered.
   */
  const recentOrbitWin = computed<OrbitWinNotification | null>(() => {
    const entry = lastOrbitWin.value;
    if (!entry) return null;
    return (orbit.value?.orbitBetsPlaced ?? 0) > entry.betsPlacedAtWin ? null : entry.win;
  });

  /**
   * Records a win as state. Called from useOrbitNotifications' onWin AFTER its refreshQuietly, so
   * `orbitBetsPlaced` already counts the bet this win came from and only a LATER bet supersedes it.
   */
  const recordOrbitWin = (win: OrbitWinNotification) => {
    lastOrbitWin.value = { win, betsPlacedAtWin: orbit.value?.orbitBetsPlaced ?? 0 };
  };
```

- [ ] **Step 4: Capture the forced cash-out in `updateSettings`**

In `updateSettings`, inside the `if (isDisabling)` branch, **before** the nonce is fetched:

```ts
      if (isDisabling) {
        // Read BEFORE the patch: disabling force-cashes the whole balance out, so by the time the
        // response lands the number the player needs to see is already 0. The cool-off panel's
        // SENT BACK cell is the only consumer.
        lastForcedCashOut.value = orbit.value?.balance ?? 0;

        const nonceResponse = await mainService.getWalletNonce(wallet, 'orbit-cashout');
```

- [ ] **Step 5: Export the new surface**

Add to the returned object in `useOrbit()`:

```ts
    isOrbitEligibleType,
    activeGameType,
    stopRules,
    hasCashOutThreshold,
    hasCashOutFloor,
    recentOrbitWin,
    recordOrbitWin,
    lastForcedCashOut: computed(() => lastForcedCashOut.value),
```

- [ ] **Step 6: Write the win as state from `onWin`**

In `src/hooks/useOrbitNotifications.ts`, pull `recordOrbitWin` out of `useOrbit()` alongside the
existing destructure, then change `onWin`'s tail. **The toast itself does not change** (AC19):

```ts
    // The amount and where it was won. The BALANCE deliberately is not in the payload — a threshold
    // cash-out fires the instant the credit lands, so the only trustworthy number is a fresh read.
    toast.success(`Orbit won ${win.amount} ${win.tokenLabel} on ${win.gameTypeName}.`);

    const wallet = effectivePublicKey.value;
    if (!wallet) {
      // No wallet to re-read for, so orbitBetsPlaced cannot be trusted to supersede this later.
      // Record it anyway: the toast has fired and the module should agree with it.
      recordOrbitWin(win);
      return;
    }

    // Record AFTER the re-read, so orbitBetsPlaced already counts the bet this win came from and only
    // a LATER Orbit bet supersedes it. The toast has already fired, so a slow read costs nothing.
    void refreshQuietly(wallet).then(() => recordOrbitWin(win));
```

- [ ] **Step 7: Verify**

```bash
yarn lint
yarn build
```

Expected: both clean. Nothing renders the new state yet, so there is no visual check for this task.

- [ ] **Step 8: Commit**

```bash
git add src/hooks/useOrbit.ts src/hooks/useOrbitNotifications.ts
git commit -m "Derive the Orbit state the new surfaces render, and keep the last win as state."
```

---

## Task 3: Shared modal open-state — `useHeaderModals`, `GameHeader`, the upsell guard

**Files:**
- Modify: `src/hooks/useHeaderModals.ts`
- Modify: `src/components/GameHeader.vue:55`, `:83-86`, `:97-100`, `:420`
- Modify: `src/hooks/useFirstWinUpsell.ts` (the never-two-modals guard)

**Interfaces:**
- Produces: `useHeaderModals()` gains `orbitOpen: Ref<boolean>` and `openOrbit(): void`.

**Why (spec F1 / D14):** `isOrbitOpen` is a **local** ref in `GameHeader.vue:55`. `/orbit`'s
`OPEN ORBIT PANEL` (AC43) and the console module's entrance (AC05) have no way to reach it, and
mounting a second `<OrbitModal>` would mean two backdrops and two Esc handlers.

- [ ] **Step 1: Move the open state into the singleton**

`src/hooks/useHeaderModals.ts` — add beside `settingsOpen` / `fairOpen`:

```ts
const settingsOpen = ref(false);
const fairOpen = ref(false);
// Orbit joined this file with the UI overhaul (#141). It was a LOCAL ref in GameHeader, which meant
// the /orbit page's OPEN ORBIT PANEL button and the game screen's console module had no way to open
// the header-owned instance — and mounting a second <OrbitModal> would put two backdrops and two Esc
// handlers on the page. Same reason the dashboard's quick links and the guide open the two above.
const orbitOpen = ref(false);

export const useHeaderModals = () => ({
  settingsOpen,
  fairOpen,
  orbitOpen,
  openSettings: (): void => {
    settingsOpen.value = true;
  },
  openFair: (): void => {
    fairOpen.value = true;
  },
  openOrbit: (): void => {
    orbitOpen.value = true;
  },
});
```

- [ ] **Step 2: Delegate from `GameHeader`**

Replace line 40's destructure and delete line 55's local ref:

```ts
  const {
    settingsOpen: isSettingsOpen,
    fairOpen: isFairOpen,
    orbitOpen: isOrbitOpen,
  } = useHeaderModals();
```

Delete `const isOrbitOpen = ref(false);` (line 55) and its comment's "the custodial wallet modal"
sentence stays — retarget it:

```ts
  // Orbit: the custodial wallet modal. Hidden when embedded, like Stats and Fair. Its open state is a
  // module singleton (useHeaderModals) so /orbit and the game screen's console module can open THIS
  // instance rather than mounting a second one.
```

`openOrbit` (line 83) and `acceptFirstWinUpsell` (line 97) keep working verbatim — they assign
`isOrbitOpen.value = true`, which now writes the singleton. Leave them as they are; the `menuOpen`
side effect in `openOrbit` is header-local and must stay.

Verify `ref` is still imported and used elsewhere in the file (it is — `menuOpen`, `menuRoot`,
`audioMuted`, `audioVolume`, `volumeOpen`), so no import changes.

- [ ] **Step 3: Add `orbitOpen` to the never-two-modals guard**

`src/hooks/useFirstWinUpsell.ts` — extend the destructure and the guard:

```ts
  const { settingsOpen, fairOpen, orbitOpen } = useHeaderModals();
```

```ts
    // Never two modals. This guarded against the Quick Tour until #131 traded the tours for the
    // /how-to-play PAGE, which is a route and cannot be open "over" anything — so the rule now points
    // at the overlays that do still exist, the header-owned Settings, Provably Fair and Orbit modals.
    // A first win lands while one of them is open easily enough: FAIR is the tab players sit on to
    // watch a round resolve, and ORBIT is where a player sits mid-enable.
    //
    // ⚠️ `orbit.value?.enabled` above does NOT cover the Orbit case: the not-enrolled player with the
    // panel open, halfway through enabling, is exactly the collision — enabled is still false there.
    if (settingsOpen.value || fairOpen.value || orbitOpen.value) return;
```

- [ ] **Step 4: Verify**

```bash
yarn lint
yarn build
```

Then serve and click through: `yarn serve --port <verified-free-port>`; open the ☰ menu → **Orbit**;
the modal opens exactly as before; Esc and the backdrop still close it.

⚠️ **Copy the hub's `.env.local` and `yarn.lock` into the worktree first** or the front reports "game
unavailable", and **verify the port is actually free** — another worktree's dev server holding it
makes `serve` fail quietly and you end up testing the wrong app.

- [ ] **Step 5: Commit**

```bash
git add src/hooks/useHeaderModals.ts src/components/GameHeader.vue src/hooks/useFirstWinUpsell.ts
git commit -m "Share the Orbit modal's open state so the page and the console module can reach it."
```

---

## Task 4: `ArcadeModal` — the `titlebar-lead` slot

**Files:**
- Modify: `src/components/ui/ArcadeModal.vue` (the `<h2 class="dialog__title">` block, ~line 205)

**Interfaces:**
- Produces: a `titlebar-lead` slot on `ArcadeModal`, rendered **inside** `.dialog__title`, after the
  6px amber square and **before** the title text, on the **grey** variant only.

**Why:** the mark cannot go in the `title` string, and this is a shell change rather than a call-site
hack (`conventions/frontend.md`: *"if a needed primitive is missing, add it to the design system"*).

⚠️ **Deviation from spec §7.2, deliberate and design-led.** The spec says "immediately before
`<h2 class="dialog__title">`", which would render **mark → square → ORBIT**. The Option C artifact
draws **square → mark → ORBIT** (`template.pretty.html:368-377`), and the artifact outranks the
spec's prose on design. `.dialog__title` is already `display:flex; gap:8px` with the square as its
`::before`, so a slot placed as its first child reproduces the mockup exactly. It also keeps
`aria-labelledby` pointing at an `<h2>` whose only non-`aria-hidden` content is the title text.

- [ ] **Step 1: Add the slot**

```html
            <h2 :id="titleId" class="dialog__title">
              <span v-if="variant === 'black'" class="dialog__spark" aria-hidden="true"></span>
              <!-- A lead mark for a surface with an identity of its own — Orbit's, today. It sits
                   INSIDE the title, after the grey panel's 6px amber square (the ::before) and
                   before the text, which is the order the design draws. Grey only: the black
                   variant centres its title between two sparkles and has no room for one.
                   Consumers must mark it aria-hidden — the adjacent text is the accessible name. -->
              <slot v-if="variant === 'grey'" name="titlebar-lead" />
              <span class="min-w-0 truncate">{{ title }}</span>
              <span v-if="variant === 'black'" class="dialog__spark" aria-hidden="true"></span>
            </h2>
```

- [ ] **Step 2: Document the slot in the props block**

Extend the comment above `interface Props` (or the slot list in `conventions/frontend.md` at ship —
the KB sync is `monkify-ship-feature`'s job, not this task's). In the component, add to the file's
header comment, after the "TWO PANELS, ONE GEOMETRY" paragraph:

```
// SLOTS: default (body, exposes titleId), `strip`, `footer`, `titlebar-actions` (right of the title,
// left of the X) and `titlebar-lead` (inside the title, after the grey panel's amber square) — added
// for Orbit's mark in #141.
```

- [ ] **Step 3: Verify**

```bash
yarn lint
yarn build
```

Then serve and open **Settings** and **Provably Fair**: both are grey `ArcadeModal`s with no
`titlebar-lead` consumer, so their titlebars must be pixel-identical to before (an empty `v-if` slot
renders nothing and the flex `gap` collapses with it). Open the first-win upsell if you can trigger
it — the black variant must be untouched.

- [ ] **Step 4: Commit**

```bash
git add src/components/ui/ArcadeModal.vue
git commit -m "Add a titlebar-lead slot to the grey dialog shell for a surface's own mark."
```

---

## Task 5: The compact 380 modal — rebuild `OrbitModal.vue`

**Files:**
- Modify: `src/hooks/useOrbit.ts` (two shared cool-off computeds — Task 6 consumes them too)
- Rewrite: `src/components/orbit/OrbitModal.vue`

**Interfaces:**
- Consumes: `--color-orbit` + `OrbitMark` + `OrbitGlyph` (Task 1); `stopRules`, `hasCashOutFloor`,
  `hasCashOutThreshold`, `activeGameType`, `lastForcedCashOut` (Task 2); `ArcadeModal`'s
  `titlebar-lead` slot (Task 4).
- Produces: `useOrbit()` gains `enableLockedUntil: ComputedRef<Date | null>` and
  `enableLockedLabel: ComputedRef<string | null>`. `OrbitModal` keeps its existing `{ open, wallet }`
  props and `close` emit, so `GameHeader`'s call site is unchanged.

**What is DELETED** (AC01/AC03): the `<Teleport>` (515), the `fixed inset-0 z-[60]` wrapper (516),
the `.orbit-backdrop` element and its `<style>` rule (519, 906-915), the
`max-w-[520px] max-h-[90dvh] bg-[#0b0b12] border-… rounded-[4px]` panel div (521-523), the
`<header>` and its close glyph (524-536), and the `onKeydown` Esc listener with its `onMounted`
registration (83-85, 508-511).

**What MOVES to `/orbit`** (Task 6): the two rule editors, recover-a-deposit, Telegram, and the
`WON VIA ORBIT` / `ORBIT BETS` counters. Their *values* stay visible in the panel through the seam row.

- [ ] **Step 1: Share the cool-off computeds from `useOrbit`**

The panel and the page both need them, and `useOrbit` already owns `isEnableLocked()`. Add inside
`useOrbit()`, next to it, and export both:

```ts
  /**
   * The live cool-off instant, or null when it has passed. Turning Orbit off withdraws everything, so
   * turning it back on waits. The server is authoritative (it refuses the patch either way); this
   * only stops the player spending a click to find out.
   */
  const enableLockedUntil = computed(() => {
    const until = orbit.value?.enableLockedUntil;
    if (!until) return null;

    const at = new Date(until);
    return at.getTime() > Date.now() ? at : null;
  });

  /**
   * UTC on purpose. The cool-off copy states a time, and the backend enforces a UTC instant — a local
   * rendering of it is how "available again at 19:42" stops matching what is actually being enforced.
   */
  const enableLockedLabel = computed(() =>
    enableLockedUntil.value ? `${enableLockedUntil.value.toISOString().slice(11, 16)} UTC` : null
  );
```

Delete the two identical local copies from `OrbitModal.vue` as part of Step 2's rewrite.

- [ ] **Step 2: Rewrite the script**

Keep every surviving handler **verbatim in behaviour** — AC32 says this is a visual and
interaction-surface change, not a behavioural one: `onToggleEnabled`, `onConfirmEnable`, `onDeposit`,
`onCashOut`, `onGameTypeChange`, `refresh`, `loadFailed`, `notice`, `canDeposit`, `canCashOut`,
`canEnable`, `isDepositingToSelf`, `pendingEnable`, `pendingGameId`, `eligibleGameTypes`,
`balanceLabel`, and all four watchers.

Delete `onSaveThreshold`, `onClearThreshold`, `onSaveFloor`, `onClearFloor`, `onRecover`, the three
Telegram handlers, and `thresholdInput` / `floorInput` / `recoverySignature` / `recoveryAmount` /
`telegramLink` / `telegramRetry` plus the two mirror watchers. They move to `OrbitPage.vue` in Task 6
**carried across verbatim**, including `thresholdText()` / `floorText()` — F9: those refs are
`string | number` on purpose, because `<input type="number">` makes Vue's `v-model` cast to a number
and `.trim()` on the raw ref throws, which took the whole SAVE click down.

New script additions:

```ts
  import ArcadeModal from '@/components/ui/ArcadeModal.vue';
  import OrbitMark from '@/components/orbit/OrbitMark.vue';
  import OrbitGlyph from '@/components/orbit/OrbitGlyph.vue';

  /**
   * The Orbit panel — MONEY YOU MOVE NOW. On/off plus the game picker, the balance, deposit, cash
   * out, and the seam row that states what the /orbit page holds.
   *
   * The split is spec section 3.1's accepted trade-off, and the seam row is the reason it is
   * acceptable at all: the two stop rules are READABLE here inside a live round even though they are
   * EDITABLE only on the page. An unset floor renders as a warm-red NOT SET, so the exposure is
   * legible from inside the round.
   *
   * Shell only: ArcadeModal owns the teleport, the dither backdrop, the 90dvh cap, the titlebar, the
   * bottom-sheet treatment below lg, Esc routing (useOverlayStack) and the focus trap. This file
   * declares no geometry, no z-index and no backdrop of its own.
   */

  const formatMoney = (value: number) =>
    value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

  /**
   * ONE state, resolved in this order. Cool-off outranks everything because it refuses both of the
   * things the rest of the panel offers; "picker" is simply "not enabled yet".
   */
  type PanelState = 'cool-off' | 'picker' | 'paused' | 'unfunded' | 'funded';

  const panelState = computed<PanelState>(() => {
    if (enableLockedUntil.value) return 'cool-off';
    if (!orbit.value?.enabled) return 'picker';
    if (isPausedForFunds.value) return 'paused';
    if (needsFirstDeposit.value) return 'unfunded';
    return 'funded';
  });

  /** The stake of the type Orbit is pointed at — the number "a round costs" refers to. */
  const stakeLabel = computed(() =>
    activeGameType.value ? formatMoney(activeGameType.value.requiredAmount) : null
  );

  /**
   * Which deposit chip takes the amber. Unfunded: all three, because any of them starts Orbit.
   * Paused: only the SMALLEST one that actually clears the stake — an amber chip that left Orbit
   * still paused would be a lie about what the click achieves.
   */
  const isSuggestedDeposit = (option: number): boolean => {
    if (panelState.value === 'unfunded') return true;
    if (panelState.value !== 'paused') return false;

    const stake = activeGameType.value?.requiredAmount ?? 0;
    const balance = orbit.value?.balance ?? 0;
    const options = [...(orbit.value?.depositOptions ?? [])].sort((a, b) => a - b);
    return options.find((candidate) => balance + candidate >= stake) === option;
  };

  /**
   * The disabled CASH OUT button says WHY it is disabled. Every one of these is ORDINARY STATE, so it
   * names its reason clearly — constraints.md section 6 keeps only auth and wallet-proof failures
   * vague, and flipping a clear refusal to vague is as much a defect as the other direction.
   */
  const cashOutLabel = computed(() => {
    const state = orbit.value;
    if (!state) return 'CASH OUT';
    if (state.balance <= 0) return 'CASH OUT — NOTHING IN THERE';
    if (!state.canCashOutToday) return 'ALREADY CASHED OUT TODAY';
    if (state.balance < state.minimumCashOut)
      return `MINIMUM CASH-OUT IS ${state.minimumCashOut} ${state.tokenLabel}`;
    return `CASH OUT ${balanceLabel.value} ${state.tokenLabel}`;
  });
```

- [ ] **Step 3: Write the shell and the body**

```html
<template>
  <ArcadeModal
    :open="open"
    size="compact"
    variant="grey"
    title="ORBIT"
    footer-layout="split"
    @close="emit('close')"
  >
    <!-- The one violet pixel in the bar. aria-hidden lives on the mark itself; the title beside it is
         the accessible name. -->
    <template #titlebar-lead>
      <OrbitMark :size="14" class="text-orbit" />
    </template>

    <!-- Full-bleed strip, above the body's padding and outside its scroll. Only two states earn it:
         a real pause, and the cool-off — which is tagged WAIT, never a failure word (AC41). -->
    <template v-if="panelState === 'paused' || panelState === 'cool-off'" #strip>
      <div class="dialog__strip dialog__strip--err">
        <span class="dialog__strip-tag">{{ panelState === 'cool-off' ? 'WAIT' : 'ERR' }}</span>
        <span class="dialog__strip-text">
          <template v-if="panelState === 'cool-off'">
            Cooling off until {{ enableLockedLabel }}.
          </template>
          <template v-else>
            Paused — {{ balanceLabel }} left and a round costs {{ stakeLabel }}. Orbit skips rounds
            until you top up.
          </template>
        </span>
      </div>
    </template>

    <div class="flex flex-col gap-4">
      <!-- The read failed — most often a wallet that was just switched and has no session here yet.
           Said plainly, because the alternative is an empty panel with no explanation. -->
      <p v-if="loadFailed" class="dialog-copy border border-line p-2 text-[17px]">
        Orbit could not be read for this wallet. Sign in again to load it.
      </p>

      <!-- ===================== COOL-OFF ===================== -->
      <template v-if="panelState === 'cool-off'">
        <p class="dialog-copy">
          You turned Orbit off and your whole balance was sent back to your wallet. Deposits and
          turning it on again are refused until {{ enableLockedLabel }}.
        </p>

        <!-- The only two questions worth asking here: where did the money go, and when can I come
             back. The amount is in-session only, so on a cold load during a cool-off the cell is
             dropped rather than invented. -->
        <div
          class="dialog-cells"
          :class="lastForcedCashOut === null ? 'grid-cols-1' : 'grid-cols-2'"
        >
          <div v-if="lastForcedCashOut !== null">
            <span class="dialog-eyebrow">SENT BACK</span>
            <span class="vt323-regular text-[21px] tabular-nums text-ink">
              {{ formatMoney(lastForcedCashOut) }} {{ orbit?.tokenLabel }}
            </span>
          </div>
          <div>
            <span class="dialog-eyebrow">AVAILABLE AGAIN</span>
            <span class="vt323-regular text-[21px] tabular-nums text-ink">
              {{ enableLockedLabel }}
            </span>
          </div>
        </div>
      </template>

      <!-- ============== ON / OFF PLUS THE GAME PICKER ============== -->
      <div v-else class="flex flex-col gap-2">
        <label class="flex cursor-pointer items-center gap-[9px]">
          <!-- Violet is Orbit's own on-state, which is identity, not chrome — the one control that IS
               the feature. Every other control in this box stays amber. -->
          <input
            type="checkbox"
            class="orbit-switch"
            :checked="orbit?.enabled || pendingEnable"
            :disabled="orbit?.enabled ? isBusy : !canEnable"
            @change="onToggleEnabled(($event.target as HTMLInputElement).checked)"
          />
          <span
            class="press-start-2p-regular text-[8px] uppercase leading-[1.4] tracking-[0.06em] text-ink"
          >
            {{ orbit?.enabled ? 'Orbit plays' : 'Enable Orbit — let it play for me' }}
          </span>
        </label>

        <!-- Enabled: the picker is how you change the answer. -->
        <select
          v-if="orbit?.enabled"
          class="input-arcade input-arcade--select orbit-select"
          :value="orbit?.gameParametersId ?? ''"
          :disabled="isBusy"
          @change="onGameTypeChange"
        >
          <option value="">Pick a game</option>
          <option v-for="type in eligibleGameTypes" :key="type.id" :value="type.id">
            {{ type.name }} — {{ type.requiredAmount }} {{ orbit?.tokenLabel }}
          </option>
        </select>

        <!-- Not enabled, box ticked: the prompt. THE ONE PLACE VIOLET TAKES A BORDER — Orbit asking a
             question about itself. TURN ON is amber, like every other primary action. -->
        <div
          v-else-if="pendingEnable"
          class="flex flex-col gap-2.5 rounded-arcade border border-orbit bg-surface-row px-[13px] py-[14px]"
        >
          <span
            class="press-start-2p-regular text-[8px] uppercase leading-[1.5] tracking-[0.06em] text-orbit"
          >
            Which game should Orbit play?
          </span>
          <select
            v-model="pendingGameId"
            class="input-arcade input-arcade--select orbit-select"
            :disabled="isBusy"
          >
            <option value="">Pick a game</option>
            <option v-for="type in eligibleGameTypes" :key="type.id" :value="type.id">
              {{ type.name }} — {{ type.requiredAmount }} {{ orbit?.tokenLabel }}
            </option>
          </select>
          <button
            type="button"
            class="btn-dialog btn-dialog--primary w-full"
            :disabled="isBusy || !pendingGameId"
            @click="onConfirmEnable"
          >
            Turn on
          </button>
          <p class="dialog-copy text-[17px] !text-ink-mute">
            One bet a round, at that game's stake, until you turn it off.
          </p>
        </div>

        <p v-if="!eligibleGameTypes.length" class="dialog-copy text-[17px] !text-ink-mute">
          No game is open to Orbit right now. Please try again later.
        </p>

        <!-- The single most consequential sentence in the feature. It appears here AND above TURN
             ORBIT OFF on /orbit, and may not be shortened away (spec section 7.10). -->
        <p v-if="orbit?.enabled" class="dialog-copy text-[17px] !text-ink-mute">
          Turning this off cashes your whole balance out to your wallet.
        </p>

        <p v-if="orbit?.selfExcluded" class="dialog-copy text-[17px] !text-ink-mute">
          Auto-play is unavailable while your self-exclusion is active.
        </p>
      </div>

      <div v-if="panelState !== 'cool-off'" class="h-px bg-line opacity-70"></div>

      <!-- ============ BALANCE · DEPOSIT · CASH OUT ============
           Dimmed to 35% and inert while the picker is open, so the player already knows the shape of
           the box they are unlocking before they unlock it. -->
      <div
        v-if="panelState !== 'cool-off'"
        class="flex flex-col gap-4"
        :class="panelState === 'picker' ? 'pointer-events-none opacity-35' : ''"
      >
        <div class="flex flex-col gap-2">
          <span class="dialog-eyebrow">ORBIT BALANCE</span>
          <div class="flex items-baseline gap-2.5">
            <span
              class="vt323-regular text-[46px] leading-[0.9] tabular-nums tracking-[0.01em]"
              :class="{
                'text-ink': panelState === 'funded',
                'text-ink-mute': panelState === 'unfunded' || panelState === 'picker',
                'text-[color:oklch(0.9_0.04_25)]': panelState === 'paused',
              }"
            >
              {{ balanceLabel }}
            </span>
            <!-- The balance figure's violet MARKER — identity, and the reason the number itself does
                 not need to be violet. -->
            <span class="press-start-2p-regular text-[9px] text-orbit">{{ orbit?.tokenLabel }}</span>
          </div>

          <div v-if="panelState === 'funded'" class="flex items-center gap-[7px]">
            <span class="h-[7px] w-[7px] flex-none bg-[color:var(--color-live)]"></span>
            <span class="vt323-regular text-[17px] text-ink-dim">
              Betting {{ stakeLabel }} a round on {{ activeGameType?.name }}.
            </span>
          </div>
          <!-- Paused: the band goes AMBER, not violet — at that moment the state is a call to act,
               not an identity (D6.1). -->
          <div v-else-if="panelState === 'paused'" class="flex items-center gap-[7px]">
            <span class="h-[7px] w-[7px] flex-none bg-primary"></span>
            <span class="vt323-regular text-[17px] text-ink-dim">
              {{ activeGameType?.name }} costs {{ stakeLabel }} a round.
            </span>
          </div>
          <!-- Switched on but never funded: an invitation. The word "paused" appears NOWHERE in this
               state — nothing has stopped, because nothing has started yet (AC10). -->
          <span v-else-if="panelState === 'unfunded'" class="vt323-regular text-[19px] text-ink-dim">
            Orbit starts the round after your first deposit.
          </span>
        </div>

        <div class="flex flex-col gap-2">
          <div class="flex items-center gap-[7px]">
            <OrbitGlyph
              name="deposit"
              :size="12"
              :class="
                panelState === 'paused' || panelState === 'unfunded'
                  ? 'text-primary'
                  : 'text-ink-mute'
              "
            />
            <span
              class="dialog-eyebrow"
              :class="panelState === 'paused' || panelState === 'unfunded' ? '!text-primary' : ''"
            >
              {{
                panelState === 'paused'
                  ? 'TOP UP TO RESUME'
                  : panelState === 'unfunded'
                    ? 'DEPOSIT TO START'
                    : 'DEPOSIT'
              }}
            </span>
          </div>
          <div class="flex gap-2">
            <button
              v-for="option in orbit?.depositOptions ?? []"
              :key="option"
              type="button"
              class="btn-dialog flex-1 tabular-nums"
              :class="isSuggestedDeposit(option) ? 'btn-dialog--primary' : ''"
              :disabled="!canDeposit"
              @click="onDeposit(option)"
            >
              {{ option }}
            </button>
          </div>

          <!-- Every deposit refusal here is ordinary state and names its reason. -->
          <p
            v-if="isDepositingToSelf"
            class="dialog-copy text-[17px] !text-[color:oklch(0.8_0.16_25)]"
          >
            This wallet is the Orbit account itself, so a deposit from it would move nothing. Connect
            a different wallet.
          </p>
          <p v-else-if="!orbit?.enabled" class="dialog-copy text-[17px] !text-ink-mute">
            Turn Orbit on above before depositing — it only holds money it is allowed to play with.
          </p>
          <p v-else class="dialog-copy text-[17px] !text-ink-mute">
            Sent from your connected wallet. One approval — no extra signature.
          </p>
        </div>

        <!-- The only filled button in the box: the only irreversible money move in it. -->
        <button
          type="button"
          class="btn-dialog btn-dialog--primary w-full"
          :disabled="!canCashOut"
          @click="onCashOut"
        >
          <OrbitGlyph v-if="canCashOut" name="cash-out" :size="12" />
          {{ cashOutLabel }}
        </button>
      </div>

      <!-- ===================== THE SEAM =====================
           A READOUT, not a link: it states both live values, so the player learns what lives on the
           page by reading the page's own numbers from inside the round. The WHOLE row is the tap
           target (AC37) — a bare "SETTINGS" button was rejected because it would teach nothing. -->
      <div class="flex flex-col gap-2">
        <span class="dialog-eyebrow">SET ONCE — ON THE ORBIT PAGE</span>
        <RouterLink
          :to="{ name: 'Orbit' }"
          class="block rounded-arcade border border-line border-l-[3px] border-l-orbit bg-surface-row px-3 py-[11px] no-underline"
          @click="emit('close')"
        >
          <div class="flex items-center justify-between gap-2.5">
            <span
              class="press-start-2p-regular flex items-center gap-2 text-[8px] uppercase leading-[1.4] text-ink"
            >
              <OrbitMark :size="12" class="text-orbit" />
              Stop rules
            </span>
            <span class="vt323-regular text-[19px] text-primary">▶</span>
          </div>

          <!-- Rule order is FIXED: rise first, fall second — here, on the page and on the phone. -->
          <div
            v-if="hasCashOutThreshold || hasCashOutFloor"
            class="mt-2 flex flex-wrap gap-x-3.5 gap-y-1"
          >
            <span class="vt323-regular flex items-center gap-1.5 text-[19px] text-ink-dim">
              <OrbitGlyph name="win-at" :size="11" />
              WIN AT
              <span :class="hasCashOutThreshold ? 'text-ink' : 'text-[color:oklch(0.8_0.16_25)]'">
                {{ hasCashOutThreshold ? formatMoney(stopRules.threshold as number) : 'NOT SET' }}
              </span>
            </span>
            <span
              class="vt323-regular flex items-center gap-1.5 text-[19px]"
              :class="hasCashOutFloor ? 'text-ink-dim' : 'text-[color:oklch(0.8_0.16_25)]'"
            >
              <OrbitGlyph name="falls-to" :size="11" />
              FALLS TO
              <span :class="hasCashOutFloor ? 'text-ink' : ''">
                {{ hasCashOutFloor ? formatMoney(stopRules.floor as number) : 'NOT SET' }}
              </span>
            </span>
          </div>
          <div v-else class="vt323-regular mt-2 text-[19px] text-[color:oklch(0.8_0.16_25)]">
            Neither is set. Set them before the money is real.
          </div>

          <div class="vt323-regular mt-1.5 text-[17px] text-ink-mute">
            Telegram · Recover a deposit · Counters
          </div>
        </RouterLink>
      </div>

      <p v-if="notice" class="dialog-copy text-[17px] !text-primary">{{ notice }}</p>
      <p v-if="lastError" class="dialog-copy text-[17px] !text-[color:oklch(0.8_0.16_25)]">
        {{ lastError }}
      </p>
    </div>

    <template #footer>
      <span class="dialog__footer-hint">Money here. Rules on the page.</span>
      <div class="flex gap-2.5">
        <button type="button" class="btn-dialog" @click="emit('close')">Close</button>
        <RouterLink :to="{ name: 'Orbit' }" class="btn-dialog no-underline" @click="emit('close')">
          Orbit page ▶
        </RouterLink>
      </div>
    </template>
  </ArcadeModal>
</template>
```

⚠️ **The cascade hazards were checked before writing the above** (`conventions/frontend.md`, #135's
six shipped no-ops). `.btn-dialog` declares `height`, `padding`, `font-size`, `color`, `border` and
`background`, so `flex-1`, `w-full` and `tabular-nums` are safe — it declares none of `flex`, `width`
or `font-variant-numeric`. `.dialog-copy` and `.dialog-eyebrow` both declare `color`, which is why
every colour override on them carries `!`. `.dialog-cells` declares `display: grid` but no
`grid-template-columns`, so `grid-cols-1` / `grid-cols-2` apply.

- [ ] **Step 4: Keep the two `<select>` rules, drop the rest of the scoped block**

`.orbit-backdrop` is deleted with the hand-rolled shell. The `<option>` colouring is **not** dead
(F11): `.input-arcade--select` owns the appearance and the arrow gutter, but the dropdown **list**
stays OS-drawn, so it still needs its own colours or the options render as light text on white.

```html
<style scoped>
  /* A native <select>'s POPUP LIST is painted by the OS, not by the control, so it does not inherit
     .input-arcade's fill — the options rendered as light text on white (reported from a screenshot of
     the game picker). `color-scheme: dark` also tells the engine to use its dark widget palette for
     the scrollbar and the highlight. Carried over from the hand-rolled modal's `.orbit-field`; the
     LIST itself staying unstyleable is documented as unavoidable in arcade.css. */
  .orbit-select {
    color-scheme: dark;
  }
  .orbit-select option {
    background-color: var(--color-surface-inset);
    color: var(--color-ink);
  }

  /* Orbit's on/off control. A native checkbox restyled rather than a bespoke widget, so it keeps its
     label association, its keyboard behaviour and its change event for free.
     ⚠️ VIOLET IS DELIBERATE and is the design's call (Option C, states 1-3, confirmed with the owner
     2026-08-03): this is Orbit's own on-state, which is identity — not the generic chrome AC40's
     "control border" clause is about. Every other control in this panel is amber. */
  .orbit-switch {
    appearance: none;
    -webkit-appearance: none;
    width: 16px;
    height: 16px;
    flex: none;
    margin: 0;
    background-color: transparent;
    border: 1px solid var(--color-orbit);
    border-radius: 2px;
    cursor: pointer;
  }
  .orbit-switch:checked {
    background-color: var(--color-orbit);
    /* The tick is drawn from two gradients rather than a font glyph: a glyph would resize with the
       type ramp and stop landing on the mark's 2px grid. */
    background-image: linear-gradient(
        45deg,
        transparent 42%,
        var(--color-void-deep) 42% 58%,
        transparent 58%
      ),
      linear-gradient(-45deg, transparent 42%, var(--color-void-deep) 42% 58%, transparent 58%);
  }
  .orbit-switch:disabled {
    opacity: 0.35;
    cursor: not-allowed;
  }
  /* The shared ring, matching arcade.css's focus rule — the one cyan element on a grey panel. */
  .orbit-switch:focus-visible {
    outline: 2px solid var(--color-cyan);
    outline-offset: 2px;
  }
</style>
```

- [ ] **Step 5: Verify — build, then measure**

```bash
yarn lint
yarn build
```

Then serve it and, on a **1440x900 GPU-rasterizing desktop** (the `backdrop-filter` traps are
invisible on a phone), open the panel and check in DevTools:

- the panel is **380px** wide and there is **no scrollbar** against the 90dvh cap (AC42);
- the computed `z-index` resolves through `z-modal`, the backdrop is `.overlay-backdrop`'s 9px
  dither, and nothing in the subtree has a `backdrop-filter` (AC03 / AC04);
- every button and select computes to **40px** (AC02);
- violet appears **only** on the titlebar mark, the `USDC` unit, the checkbox, and the `STOP RULES`
  block's 3px left edge and mark — never on a hover, a focus ring, an input border or a primary
  action (AC40).

Then walk all five states. The three that are hard to reach naturally are worth forcing from the
console against the module-level state rather than left unseen.

- [ ] **Step 6: Commit**

```bash
git add src/hooks/useOrbit.ts src/components/orbit/OrbitModal.vue
git commit -m "Rebuild the Orbit panel as a compact 380 arcade dialog with the stop-rules seam."
```

---

## Task 6: The `/orbit` page + route + embed redirect

**Files:**
- Create: `src/pages/OrbitPage.vue`
- Modify: `src/routes.ts` (a new record after `ResponsibleGaming`)
- Modify: `src/main.ts:79` (`EMBED_REDIRECT_ROUTES`)

**Interfaces:**
- Consumes: `OrbitMark` / `OrbitGlyph` (Task 1); `stopRules`, `hasCashOut*`, `activeGameType` (Task 2);
  `useHeaderModals().openOrbit()` (Task 3); `enableLockedLabel` (Task 5).
- Produces: a named route `Orbit` at `/orbit`, which Task 5's seam row and footer already link to.

- [ ] **Step 1: Register the route**

`src/routes.ts`, immediately after the `ResponsibleGaming` record:

```ts
  // The Orbit page: setup, the two stop rules, recovery and Telegram. The PANEL keeps the money you
  // move mid-round (deposit / cash out) — see specs/orbit-ui-overhaul.md section 3.1 for why the
  // split is drawn where it is, and why the panel still shows this page's two rule values.
  // Deliberately NOT an isPlayerRoute() in App.vue: it renders no display name, so booting player
  // preferences for it is wasted work — its own state comes from useOrbit, which GameHeader's
  // app-wide effectiveWallet watcher already hydrates on every route. Matches /responsible-gaming
  // and /how-to-play.
  // Embedded loads redirect to the lobby (main.ts): a framed player reaching a custody surface is a
  // leak, and Orbit is hidden in a frame by construction rather than by remembering to hide a link.
  {
    name: 'Orbit',
    path: '/orbit',
    component: require('@/pages/OrbitPage.vue').default,
  },
```

- [ ] **Step 2: Join the embed redirect set**

`src/main.ts` — one word, and extend the comment above it so the fourth entry is not a mystery:

```ts
const EMBED_REDIRECT_ROUTES = new Set([
  'Home',
  'HowToPlay',
  'ResponsibleGaming',
  'Practice',
  'Orbit',
]);
```

Add to that comment block:

```
// Orbit joined with #141: it is a CUSTODY surface, and Orbit is hidden entirely inside a frame — no
// module, no header row, no panel, no upsell — so a framed player who reached /orbit would be the one
// hole left in that rule.
```

- [ ] **Step 3: Write the page**

`src/pages/OrbitPage.vue`. The three sections carried across from the old modal — the two rule
editors, recover-a-deposit and Telegram — move **verbatim in behaviour**: `onSaveThreshold`,
`onClearThreshold`, `onSaveFloor`, `onClearFloor`, `onRecover`, `onStartTelegramLink`,
`onVerifyTelegram`, `onUnlinkTelegram`, the `thresholdInput` / `floorInput` string-or-number refs
with their `thresholdText()` / `floorText()` guards (F9), and the two mirror watchers that reflect
the saved values back into the fields.

```ts
  /**
   * The Orbit page — RULES YOU SET ONCE. The two stop rules, recover-a-deposit, Telegram and the
   * counters, plus the balance read-only and one button back to the panel.
   *
   * MATERIAL: `.arcade-frame .arcade-frame--grey`, joining /game/history, /game/stats and
   * /responsible-gaming. Grey is what this design system pairs with money, which makes the
   * modal-to-page move material-preserving. The `.dialog-*` CONTENT classes come with it — they are
   * the shared vocabulary for copy, eyebrows, labels and inputs on this material. The dialog-only
   * CONSTRUCTS do not: there are no sunken bars, because this is a page.
   *
   * ⚠️ NEITHER SURFACE IS A DEAD END (AC43). The panel states this page's two rule values; this page
   * states the panel's balance and carries the single .btn-arcade back to it.
   *
   * ⚠️ It reads useOrbit and never calls load / refreshQuietly itself. GameHeader's app-wide watcher
   * is the ONLY hydration point — a second refresh path is exactly how this page and the header would
   * come to disagree while refreshQuietly moves the balance without the player acting (AC18).
   */
```

Structure, following the artifact's plates 5 and 6:

1. **Header** — `OrbitMark` at 20px violet + `ORBIT` (Press Start 16px amber), the copy *"Your
   balance is held by the house and plays one bet a round for you. Set the rules that take the money
   back out."*, and a `◀ BACK TO GAME` `.btn-dialog` on the right. Then a `h-px bg-line opacity-70`
   rule.
2. **Band A — the readout strip** (`.dialog-cells` at `lg:grid-cols-[1.15fr_1fr_1fr]`,
   `grid-cols-1` below):
   - cell 1: eyebrow `ORBIT BALANCE — READ ONLY`, the figure at 52px tabular with the violet `USDC`
     marker, then the live square + *"Playing {game} · {stake} a round"*.
   - cell 2: `WON VIA ORBIT` `{totalWonViaOrbit} {token}` and `ORBIT BETS` `{orbitBetsPlaced}`, both
     at 26px tabular.
   - cell 3: *"Deposits and cash-outs stay in the Orbit panel, so you can use them without leaving a
     round."* plus **`OPEN ORBIT PANEL`** — the page's **single `.btn-arcade`**, sharing its row with
     nothing (AC43), carrying the `deposit` glyph, wired to `useHeaderModals().openOrbit()`.
3. **Band B — the pair**, one framed block with its own titlebar (`bg-surface-bar`, 1px line):
   `STOP RULES — BOTH DIRECTIONS` + *"One rule catches a rise. The other catches a fall. They are not
   alternatives — set both."* Inside, `lg:grid-cols-[250px_1fr]`:
   - **the ladder** (the only thing on the page **drawn** rather than written): ceiling row — the
     `win-at` glyph, the threshold in `--color-live`, and a dashed green rule; a 2px vertical
     `--color-line` connector; the **`NOW` chip** — `bg-[color-mix(in_oklab,var(--color-orbit)_13%,transparent)]`
     inside a 1px violet border, with the mark, the `NOW` eyebrow and the balance; another connector;
     the floor row in the ERR hue. **An OFF rule leaves a visibly open end** — the value renders as
     `OFF` and the dashed rule is dropped, so the ladder is missing that end rather than closing it.
   - **the two rule cards**, in the fixed order rise-then-fall. Each: a `bg-surface-row` block with a
     3px left edge in its own semantic hue (green / ERR red), the glyph plus its Press Start label,
     then `.input-arcade` + `SAVE` + `OFF` (`.btn-dialog` ×2), then the state line.
     - `CASH OUT ON A WIN AT` — *"Orbit sends your balance to your wallet as soon as a win takes it to
       {value}."*
     - `CASH OUT IF IT FALLS TO` — *"Orbit stops and sends what is left to your wallet if a bet takes
       your balance down to {value}."*
     - ⚠️ **An OFF rule is drawn, not hidden** (AC38): the empty floor field keeps a **warm-red
       border** (`border-[color:oklch(0.8_0.16_25)]`, an override of `.input-arcade`'s
       `border-color` — the class declares it, so the utility ties on specificity and **loses**; use
       `!border-[color:oklch(0.8_0.16_25)]`), the placeholder `Off — min {minimumCashOut}`, and the
       line *"OFF — nothing stops Orbit if your balance falls."*
   - a `WHY BOTH` callout on the ERR skin: *"A rule that fires on a win cannot catch a fall. With only
     the top rule set, a balance sliding to zero is never stopped."*
4. **Band C — two equal cards** (`lg:grid-cols-2`): `RECOVER A DEPOSIT` (copy, the signature input,
   the amount select, `RECOVER`, and *"We check it on-chain before crediting. Nothing is credited
   twice."*) and `TELEGRAM` (the three states carried over verbatim, the whole card hidden when
   `telegramEnabled` is false, exactly as today).
5. **The tail row:** *"Turning Orbit off cashes your whole balance out to your wallet, then locks it
   for 30 minutes."* + a `TURN ORBIT OFF` `.btn-dialog`, wired to
   `updateSettings(wallet, { enabled: false })` — the same call the panel makes, so the forced
   cash-out, its toast and the cool-off behave identically (AC32).

**Phone (below `lg`):** section order changes — **rules come before Telegram and recovery**, because
the two that matter to money go above the fold and the two once-a-lifetime chores below it. Do this
with `order-*` utilities on a flex column, not by duplicating markup. The ladder rotates into the gap
**between** the two rule cards, so the pair stays adjacent and both stay visible without a
disclosure. Text inputs are floored at **21px** (`.input-arcade`'s `lg` rung) so iOS never zooms.

**The three guard states**, in this order at the top of the frame:

```ts
  // Orbit is hidden entirely when embedded, but the route guard in main.ts means this component never
  // mounts in a frame — so there is no isEmbedded branch here, deliberately.
  //
  // No session: the same SIGN IN affordance the header row shows, never a zero. The balance is
  // genuinely unreadable without ownership proof (AC20).
  // Not configured (IsConfigured false — the local default): every Orbit surface hides, so the page
  // says so plainly rather than rendering dead controls (AC30).
  // Orbit OFF: the full page renders WITH an off notice — the rules survive a disable (the cool-off
  // panel says so: "Kept: WIN AT 20.00"), so hiding the editors would strand the player who came here
  // to set a floor. Decided with the owner 2026-08-03.
```

- [ ] **Step 4: Verify**

```bash
yarn lint
yarn build
```

Serve, then:
- `/orbit` renders on the grey page material at 1000px, three bands of different shapes (AC43);
- set a floor, then press `OPEN ORBIT PANEL` — the panel's seam row shows the new value (AC37/AC43);
- clear the floor — the seam row shows warm-red `NOT SET` and the page field keeps its warm-red
  border and states the consequence in words (AC38);
- rule order is rise-on-top on desktop **and** at 390px (AC39);
- load `/orbit?embed=1` (or in a frame) — it redirects to the lobby (AC29);
- violet appears only on the mark, the `USDC` marker and the `NOW` chip (AC40).

- [ ] **Step 5: Commit**

```bash
git add src/pages/OrbitPage.vue src/routes.ts src/main.ts
git commit -m "Add the /orbit page for the rules you set once, and hide it inside a frame."
```

---

## Task 7: The first-win upsell reskin

**Files:**
- Modify: `src/components/orbit/OrbitFirstWinModal.vue`

**Interfaces:**
- Consumes: `ArcadeModal` (Task 4's slot is not used here — black variant).
- Produces: no API change. `GameHeader` keeps passing `:open` and listening for `dismiss` / `enable`.

**What is DELETED** (AC21): the `<Teleport>` (45), the `<Transition name="first-win">` and its scoped
keyframes (46, 124-149), the `fixed inset-0 z-[60]` wrapper (47), the `bg-black/80` backdrop (50),
the `.first-win-frame` panel with its `max-h-[90dvh] max-w-[26rem]` (52-54, 120-122), the hand-rolled
close button and its inline `<svg>` (55-71), and the `onKeydown` Esc listener with its watcher and
`onUnmounted` (26-39).

⚠️ **Black is correct here and must not blur the rule.** Unprompted, once-ever, purely to read, light
in tone — both axes. **The Orbit panel stays grey.**

- [ ] **Step 1: Rewrite the component**

```html
<script setup lang="ts">
  import ArcadeModal from '@/components/ui/ArcadeModal.vue';
  import edson from '@/assets/edson.png';

  /**
   * The first-win invitation into Orbit.
   *
   * BLACK PANEL, not grey (conventions/frontend.md -> "Two panels"), and it needs BOTH axes to earn
   * it: it appears UNPROMPTED, it is short and purely to read, and its tone is celebratory. The Orbit
   * panel itself is grey for the opposite reason — money, and the player is doing something.
   *
   * It is the black tier's only overlay implementation, so it is also its reference one.
   *
   * ⚠️ EVERY EXIT MARKS IT SEEN — the X, Esc, the backdrop and both buttons. Nobody is nagged for
   * dismissing a once-ever modal "the wrong way", which is why @close routes straight to dismiss
   * whatever reason it carries (AC22).
   */

  defineProps<{ open: boolean }>();

  const emit = defineEmits<{
    (e: 'dismiss'): void;
    (e: 'enable'): void;
  }>();
</script>

<template>
  <ArcadeModal
    :open="open"
    variant="black"
    size="standard"
    title="NICE WIN!"
    :dismissible="true"
    @close="emit('dismiss')"
  >
    <div class="flex flex-col items-center">
      <img
        :src="edson"
        alt=""
        aria-hidden="true"
        class="pixelated h-24 w-24 object-contain md:h-28 md:w-28"
      />

      <p class="dialog-copy mt-3 max-w-[34ch] text-balance text-center">
        Put it in orbit. Fund an Orbit balance once and the house plays your game for you, every
        round, until you cash out.
      </p>

      <!-- Both buttons come off the control-sizing scale rather than their old bespoke px-6 py-3.5 /
           bare text, so they are 40px like every other flat control in the app. -->
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

⚠️ The heading moves from a body `<h2>` into the shell's `title`, which is what `aria-labelledby`
already points at — so the a11y wiring improves rather than regresses. `title` is upper-cased at the
call site because `.dialog__title` does not `text-transform` (unlike `.btn-dialog`, which does).

- [ ] **Step 2: Verify**

```bash
yarn lint
yarn build
```

Serve, then clear `space-stroker.orbit.first-win-seen` from localStorage and win a round (or call
`useFirstWinUpsell().maybeOpen()` from the console). Check:
- it is a **black** `ArcadeModal` — 2px cyan frame, centred cyan title between two sparkles (AC21);
- X, Esc, backdrop and both buttons all mark it seen — reopen after each and confirm it does not
  return (AC22);
- `Set up Orbit` still dismisses **and** opens the Orbit panel, which is now grey — the two panels
  are visibly different materials, so the rule reads (AC21).

- [ ] **Step 3: Commit**

```bash
git add src/components/orbit/OrbitFirstWinModal.vue
git commit -m "Reskin the first-win upsell onto the black arcade dialog, behaviour untouched."
```

---

## Task 8: The header row — subordinate readout

**Files:**
- Modify: `src/components/OrbitBalance.vue`

**Interfaces:**
- Consumes: `OrbitMark` (Task 1).
- Produces: no API change — `sign-in` and `open` still emit, and `GameHeader` still routes both to
  `openOrbit`.

⚠️ **The row stays on every route** (D8 / AC17). Hiding it on `/game/play` was rejected: the console
module renders only for enrolled players on eligible types, so signed-out, not-enrolled and Space
Jackpot players would lose the `SIGN IN` affordance to the burger menu — and header chrome that
appears and disappears by route reads as a bug.

⚠️ **AC18 is structural, not a rule to remember.** Both this row and the console module call
`useOrbit()`, whose state is module-level, and **only** `GameHeader.vue:74-81` hydrates it. Neither
surface may call `load` or `refreshQuietly` on its own.

- [ ] **Step 1: Add the mark and keep everything else**

```html
<template>
  <div class="flex items-center justify-end gap-1.5 text-[10px] leading-none">
    <!-- The mark leads the row at 12px. It and the figure's marker are the ONLY violet here: the row
         carries no violet fill and no border of its own, because on /game/play the console module is
         the loud one and this is a readout. -->
    <OrbitMark :size="12" class="text-orbit" />
    <span class="tracking-widest text-white/40">ORBIT</span>

    <button
      v-if="!hasSession"
      type="button"
      class="border border-white/25 px-1.5 py-0.5 tracking-widest text-white/70 transition-colors hover:border-white/50 hover:text-white"
      @click="emit('sign-in')"
    >
      SIGN IN
    </button>

    <button
      v-else
      type="button"
      class="tabular-nums text-primary transition-[filter] hover:brightness-125"
      @click="emit('open')"
    >
      {{ formatted }}<span class="ml-1 text-white/40">{{ orbit?.tokenLabel ?? tokenLabel }}</span>
    </button>
  </div>
</template>
```

Extend the component's header comment:

```
   * Since #141 the mark leads the row, and on /game/play the row is deliberately SUBORDINATE to the
   * console module: no violet fill, no border of its own, 10px type against the module's 16px mark
   * and its balance. Two live renderings of one balance now exist, and Orbit's balance moves without
   * the player acting (refreshQuietly), so both read the SAME module-level state and neither
   * refreshes independently — GameHeader's wallet watcher is the only hydration point (AC18).
```

- [ ] **Step 2: Verify**

```bash
yarn lint
yarn build
```

Serve and check the header on `/`, `/game`, `/game/play`, `/game/history`, `/orbit`: the row is
present on every one (AC17), shows `SIGN IN` and never a `0` when signed out (AC20), and the mark
sits at 12px in violet. Confirm the row does not wrap the cluster at 360px — the mark costs 12px plus
a 6px gap, and `WalletButtons` shares that column.

- [ ] **Step 3: Commit**

```bash
git add src/components/OrbitBalance.vue
git commit -m "Lead the header Orbit row with the mark and make it a subordinate readout."
```

---

## Task 9: The console module + both `GameConsole` branches

**Files:**
- Create: `src/components/orbit/OrbitConsoleModule.vue`
- Modify: `src/components/game/GameConsole.vue` (script; `:341-343` mobile; `:487-492` desktop+wide)

**Interfaces:**
- Consumes: `OrbitMark` (Task 1); `isOrbitEligibleType`, `activeGameType`, `recentOrbitWin` (Task 2);
  `useHeaderModals().openOrbit()` (Task 3).
- Produces: `<OrbitConsoleModule :compact="boolean" />` — `compact` is the mobile plate's tighter
  strip. It reads `myBetThisRoundIsOrbit` from `useGame` itself, so `GameConsole` passes nothing else.

⚠️ **This is a DOCUMENTED design-system exception** (AC11), joining `GameTerminal`, `GameConsole`,
the lever, Edson and the docked side panel in `conventions/frontend.md`'s named-exceptions list at
ship. It is game-screen furniture, built in the cabinet's own language — metal bezel, rivets,
recessed CRT, the `#5c6a8c` / `#92a1b9` / `#424c6e` / `#c7cfdd` palette the cabinet already uses —
and uses **none** of `.btn-dialog`, `.input-arcade` or `.arcade-frame`.

⚠️ **The Option C artifact does not draw this module.** It covers the panel, the page and the seam
only. The design here is derived from the spec's §7.4 prose plus the cabinet language already in
`GameConsole.vue` and `GameTerminal.vue` — so it is the one surface in this card that needs a real
look before hand-off rather than a mockup comparison.

- [ ] **Step 1: Write the module**

```html
<script setup lang="ts">
  import { computed } from 'vue';
  import OrbitMark from '@/components/orbit/OrbitMark.vue';
  import { useGame } from '@/hooks/useGame';
  import { useOrbit } from '@/hooks/useOrbit';
  import { useHeaderModals } from '@/hooks/useHeaderModals';

  /**
   * Orbit ON THE GAME SCREEN: the balance, the ONE live state, and an entrance.
   *
   * CABINET FURNITURE, and a documented design-system exception (AC11). It is built in the game
   * screen's own language — a metal bezel with rivets around a recessed black CRT — not in the
   * dialog's flat family, because it is bolted to the machine rather than floating over it.
   *
   * ⚠️ NO INLINE MONEY ACTION (D3). A mis-tap beside the bet lever would spend real USDC, and every
   * deposit guard (canDeposit, the cool-off, the self-deposit, enable-first) would have to be
   * reproduced in a space far too small to explain any of them. The whole module is one button and it
   * opens the panel, which is where those guards already live.
   *
   * ⚠️ It ABSORBS the inert `> ORBIT PLACED YOUR BET FOR THIS ROUND.` paragraph from both GameConsole
   * branches (D5 / AC06): "covered this round" is a STATE of this module, not a separate dead <p>.
   *
   * ⚠️ It NEVER hydrates. useOrbit is read-only here — GameHeader's app-wide wallet watcher is the
   * only load path, which is what makes this module and the header row structurally incapable of
   * showing different numbers (AC18). Do not call ensureSession or load from this file: reading mints
   * nothing, but ensureSession costs a wallet prompt (F4).
   */

  withDefaults(defineProps<{ compact?: boolean }>(), { compact: false });

  const { myBetThisRoundIsOrbit } = useGame();
  const { orbit, isPausedForFunds, needsFirstDeposit, activeGameType, recentOrbitWin } = useOrbit();
  const { openOrbit } = useHeaderModals();

  const balanceLabel = computed(() =>
    (orbit.value?.balance ?? 0).toLocaleString(undefined, {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    })
  );

  /**
   * EXACTLY ONE state line, first match wins. The order is the spec's (section 7.4.2) and it is not
   * arbitrary: a pause outranks everything because it is the only one asking for an action, and
   * "covered this round" outranks the last win because it is about the round on screen.
   *
   * The AMBER band belongs to the pause alone — a call to act is not an identity (D6.1). Everything
   * else carries the violet marker.
   */
  const state = computed(() => {
    if (isPausedForFunds.value) return { text: 'Paused — top up to resume.', tone: 'alert' } as const;
    // NEVER the word "paused" here: nothing has stopped, because nothing has started (AC10).
    if (needsFirstDeposit.value) return { text: 'Deposit to start.', tone: 'neutral' } as const;
    if (myBetThisRoundIsOrbit.value)
      return { text: 'Covered this round.', tone: 'orbit' } as const;
    if (recentOrbitWin.value)
      return {
        text: `Won ${recentOrbitWin.value.amount} ${recentOrbitWin.value.tokenLabel} last round.`,
        tone: 'orbit',
      } as const;
    return { text: 'Playing every round.', tone: 'orbit' } as const;
  });
</script>

<template>
  <!-- The bezel. `flex-none` and a fixed height on the CRT are load-bearing on the mobile branch: the
       left plate's column is overflow-hidden with a hard floor (spec F10), so a growable block here
       would push the keyboard out of the plate on a short phone. -->
  <button
    type="button"
    class="orbit-module w-full flex-none rounded-[3px] bg-[#5c6a8c] p-[3px] text-left"
    :aria-label="`Orbit — ${balanceLabel} ${orbit?.tokenLabel ?? ''}. ${state.text} Open the Orbit panel.`"
    @click="openOrbit()"
  >
    <div class="relative">
      <!-- Rivets, the same tell the cabinet's own plates carry. 2px, on the grid. -->
      <span class="absolute left-[2px] top-[2px] h-[2px] w-[2px] bg-[#c7cfdd] opacity-60"></span>
      <span class="absolute right-[2px] top-[2px] h-[2px] w-[2px] bg-[#c7cfdd] opacity-60"></span>
      <span class="absolute bottom-[2px] left-[2px] h-[2px] w-[2px] bg-[#c7cfdd] opacity-60"></span>
      <span class="absolute bottom-[2px] right-[2px] h-[2px] w-[2px] bg-[#c7cfdd] opacity-60"></span>

      <!-- The recessed screen. Flat black glass; no glow, because every phone strips one. -->
      <div
        class="flex items-center gap-2 overflow-hidden bg-black px-2"
        :class="compact ? 'h-[28px]' : 'h-[34px]'"
      >
        <OrbitMark
          :size="compact ? 12 : 16"
          :class="state.tone === 'alert' ? 'text-primary' : 'text-orbit'"
        />

        <span
          class="vt323-regular tabular-nums leading-none text-[#c7cfdd]"
          :class="compact ? 'text-[15px]' : 'text-[18px]'"
        >
          {{ balanceLabel }}
        </span>
        <span
          class="press-start-2p-regular leading-none"
          :class="[compact ? 'text-[6px]' : 'text-[7px]', state.tone === 'alert' ? 'text-primary' : 'text-orbit']"
        >
          {{ orbit?.tokenLabel }}
        </span>

        <span class="ml-auto flex min-w-0 items-center gap-1.5">
          <span
            class="h-[6px] w-[6px] flex-none"
            :class="state.tone === 'alert' ? 'bg-primary' : 'bg-[color:var(--color-orbit)]'"
          ></span>
          <span
            class="vt323-regular truncate leading-none"
            :class="[compact ? 'text-[13px]' : 'text-[15px]', state.tone === 'alert' ? 'text-primary' : 'text-[#92a1b9]']"
          >
            {{ state.text }}
          </span>
        </span>
      </div>
    </div>
  </button>
</template>

<style scoped>
  /* The cabinet's press dip, not the dialog family's hover. One transform, no loop — light-graphics
     has nothing to strip. */
  .orbit-module {
    transition: filter 0.15s ease;
  }
  .orbit-module:hover {
    filter: brightness(1.15);
  }
  .orbit-module:active {
    transform: translateY(1px);
  }
  /* The shared ring — the one cyan element, matching arcade.css's focus rule. */
  .orbit-module:focus-visible {
    outline: 2px solid var(--color-cyan);
    outline-offset: 2px;
  }
  @media (prefers-reduced-motion: reduce) {
    .orbit-module {
      transition: none;
    }
    .orbit-module:active {
      transform: none;
    }
  }
</style>
```

- [ ] **Step 2: Wire both `GameConsole` branches**

Add to the script:

```ts
  import OrbitConsoleModule from '@/components/orbit/OrbitConsoleModule.vue';
  import { useOrbit } from '@/hooks/useOrbit';

  // Orbit's presence on the game screen. READ ONLY — useOrbit mints nothing on read, and this file
  // must not call ensureSession or load (F4): GameHeader's app-wide watcher is the only hydration
  // point, which is also what keeps this module and the header row on one set of numbers (AC18).
  //
  // `myBetThisRoundIsOrbit` still comes from useGame and is deliberately NOT re-derived from useOrbit
  // — useGame.ts:198-202 documents why: it has to work for a player who never signs in.
  const { orbit, hasSession, isOrbitEligibleType } = useOrbit();

  const showOrbitModule = computed(
    () =>
      !isEmbedded && // AC29 — no custody surface inside a frame
      hasSession.value && // AC20 — signed out means absent, never a zero
      orbit.value?.enabled === true && // AC07 — not enrolled or switched off means absent
      isOrbitEligibleType(selectedGame.value?.gameTypeId) // AC08 — Space Jackpot means absent
  );
```

**Mobile branch** — replace `GameConsole.vue:341-343` (the inert `<p>`) with:

```html
        <!-- Orbit, absorbing the inert `> ORBIT PLACED YOUR BET FOR THIS ROUND.` line this replaces
             (D5 / AC06). A FIXED-HEIGHT strip, never growable: this column is overflow-hidden with a
             hard floor (F10), and the removed paragraph buys back most of what the strip costs, so
             the console still never shrinks and the lever stays reachable (AC12). -->
        <OrbitConsoleModule v-if="showOrbitModule" compact class="mb-[6px]" />
```

**Desktop + wide branch** — replace `GameConsole.vue:487-492` (the other inert `<p>`) with:

```html
      <!-- Same module, same absorbed line, in the desktop column. ⚠️ This `v-else` branch serves TWO
           mounts (F3): GameTerminal.vue:533's desktop column AND GamePage.vue:206's aspect-locked
           600-1023px band. Check both. -->
      <OrbitConsoleModule v-if="showOrbitModule" class="mx-4 mb-2" />
```

After this, `myBetThisRoundIsOrbit` has **no standalone `<p>` consumer left in either branch** (AC06).
`useGame.ts:493` and `:500` still use it for the bet-refusal copy and are untouched.

- [ ] **Step 3: Verify**

```bash
yarn lint
yarn build
```

Serve, sign in to Orbit, enable it on **Cosmic Sprint**, and check:
- the module renders with the balance, one state line and an entrance; clicking it opens the panel
  (AC05);
- the balance matches the header row exactly, and a deposit moves both with **no second fetch** in the
  network tab (AC18);
- switch the type to **Space Jackpot** — the module disappears with no layout error, and the header
  row stays (AC08 / AC17). Repeat at **800px**, the aspect-locked band, which is the second mount;
- sign out — the module is gone and the header shows `SIGN IN` (AC07 / AC20);
- at **390px**: the console does not shrink, the lever is still reachable, and focusing the bet input
  does not zoom (AC12);
- the styling uses none of `.btn-dialog` / `.input-arcade` / `.arcade-frame` (AC11), and there is no
  `backdrop-filter`, `blur()`, infinite animation or wide glow in it (AC04).

- [ ] **Step 4: Commit**

```bash
git add src/components/orbit/OrbitConsoleModule.vue src/components/game/GameConsole.vue
git commit -m "Give Orbit one console-cabinet module on the game screen and absorb the inert line."
```

---

## Task 10: Mobile deeplink sign-in — the stash, the hop, the return

**Files:**
- Modify: `src/types/mobileWallet.ts`
- Modify: `src/hooks/useMobileWallet.ts`
- Modify: `src/hooks/useSelfExclusion.ts:163` (rename call site only)
- Modify: `src/hooks/useOrbit.ts`
- Modify: `src/hooks/useGame.ts:710-748`
- Modify: `src/components/OrbitBalance.vue` / `GameHeader.vue` — `SIGN IN` passes `allowHop`

**Interfaces:**
- Produces: `MobilePendingOrbit`; `useMobileWallet()` gains `signMobileOrbit`, `getPendingOrbit`,
  `clearPendingOrbit`, and renames `readSelfExclusionSignature` → `readSignedMessageSignature`.
- Produces: `useOrbit()` gains `resumeOrbitDeeplink(params: URLSearchParams): Promise<boolean>`;
  `ensureSession(wallet, options?: { allowHop?: boolean })` and
  `load(wallet, options?: { allowHop?: boolean })`.

**Closes #121's unmet AC5.** Today Orbit is **unusable** on a deeplink wallet:
`useOrbit.signOrbitMessage` reads `signMessage.value` from `solana-wallets-vue`, which is **null**
when the wallet connected via the deeplink protocol, so `ensureSession` throws
`'This wallet cannot sign messages.'` and there is no route to a session at all.

- [ ] **Step 1: The stash type**

`src/types/mobileWallet.ts`, modelled on `MobilePendingSelfExclusion`:

```ts
/**
 * An Orbit action waiting on its signature from the wallet app. The nonce is carried across the hop
 * because it is single-use server-side: re-fetching one on return would invalidate the signature the
 * player just produced.
 *
 * ⚠️ `action` is REQUIRED, not cosmetic. Orbit signs TWO different messages — `orbit-session` for
 * SIGN IN and `orbit-cashout` for a withdrawal — and DISABLING Orbit signs the cash-out message too,
 * because turning it off forces a cash-out. Three completions, one signature shape; a two-value
 * discriminator would ship a known-dead disable path on the exact device this exists to fix.
 */
export interface MobilePendingOrbit {
  walletApp: WalletApp;
  wallet: string;
  action: 'session' | 'cashout' | 'disable';
  nonce: string;
  createdAt: number;
}
```

- [ ] **Step 2: The stash plumbing**

`src/hooks/useMobileWallet.ts`:

```ts
// An Orbit action awaiting its signature. A DISTINCT key: the wallet's return params are identical
// for signAllTransactions and signMessage, so what we stashed before leaving is the only thing that
// tells a bet, a self-exclusion and an Orbit action apart. ⚠️ A wrong key here breaks the BET hop,
// which is the money path.
const PENDING_ORBIT_KEY = 'swMobileWalletPendingOrbit';
```

```ts
  /**
   * Hop out to the wallet app to sign an Orbit challenge. Like signMobileTransfer this does NOT
   * return a value — the page navigates away and the answer arrives on the return hop, which
   * useOrbit.resumeOrbitDeeplink() completes.
   */
  const signMobileOrbit = (
    session: MobileWalletSession,
    pending: MobilePendingOrbit,
    message: string
  ): void => {
    writeJson(PENDING_ORBIT_KEY, pending);
    stashReturnTo();
    window.location.replace(
      buildSignMessageDeeplink(session.walletApp, {
        dappPublicKeyB58: session.dappPublicKeyB58,
        sharedSecretB58: session.sharedSecretB58,
        session: session.session,
        message,
        redirectLink: redirectLink(),
      })
    );
  };

  /**
   * ⚠️ STALE-GUARDED, unlike getPendingSelfExclusion's bare read. The `kind === 'error'` arm of the
   * return handler clears only the pending BET (useGame.ts:726-731), so a cancelled Orbit hop leaves
   * its stash behind. Ageing it out here is the fix that cannot change self-exclusion's behaviour —
   * widening that error arm could.
   */
  const getPendingOrbit = (): MobilePendingOrbit | null => {
    const pending = readJson<MobilePendingOrbit>(PENDING_ORBIT_KEY);
    if (pending && Date.now() - pending.createdAt > STALE_HOP_MS) {
      removeKey(PENDING_ORBIT_KEY);
      return null;
    }
    return pending;
  };

  const clearPendingOrbit = (): void => removeKey(PENDING_ORBIT_KEY);
```

Rename `readSelfExclusionSignature` → **`readSignedMessageSignature`** (F8: it is
`parseSignMessageReturn` + base58→base64 and knows nothing about self-exclusion). ⚠️ **Rename, do not
alias** — an alias is how two names for one thing start drifting (the `.dialog-input` →
`.input-arcade` precedent). Update its doc comment and its one caller,
`useSelfExclusion.ts:163`.

Add the key to `disconnectMobileWallet` (F6 — a stale Orbit hop must not resume against a wallet that
is no longer connected):

```ts
    removeKey(PENDING_ORBIT_KEY);
```

Export `signMobileOrbit`, `getPendingOrbit`, `clearPendingOrbit`, `readSignedMessageSignature`.

- [ ] **Step 3: The hop, opt-in per call site**

`src/hooks/useOrbit.ts` — `signOrbitMessage` gains the deeplink branch, returning
`'hopped' | 'signed'` in the shape `useSelfExclusion.exclude` already uses:

```ts
  /**
   * Signs an Orbit challenge, or hops out to the wallet app to have it signed.
   *
   * A deeplink wallet has no in-page signMessage, so `signMessage.value` is null and every Orbit
   * money path used to dead-end here. Now it stashes what the signature is FOR and navigates; the
   * return lands on resumeOrbitDeeplink().
   *
   * ⚠️ `allowHop` is false everywhere except three deliberate presses (see ensureSession). Anything
   * that reaches here with allowHop true has been asked for explicitly.
   */
  const signOrbitMessage = async (
    wallet: string,
    message: string,
    action: MobilePendingOrbit['action'],
    nonce: string,
    allowHop: boolean
  ): Promise<'hopped' | string> => {
    const session = getMobileSession();

    if (allowHop && session && isMobileDeeplinkContext()) {
      signMobileOrbit(
        session,
        { walletApp: session.walletApp, wallet, action, nonce, createdAt: Date.now() },
        message
      );
      return 'hopped';
    }

    if (!signMessage.value) throw new Error('This wallet cannot sign messages.');

    const signed = await signMessage.value(new TextEncoder().encode(message));
    return toBase64(signed);
  };
```

**D16 / F5 — `ensureSession(wallet, { allowHop = false })`.** `ensureSession` has ~10 callers, several
of them background (`loadIfSignedIn`, `resumePendingDeposit`, the notification channel). A blanket
hop would navigate a deeplink player into Phantom **on boot**. Exactly **three** entry points pass
`allowHop: true`, and all three are a deliberate press:

| Entry point | `action` | Message |
| --- | --- | --- |
| `OrbitBalance`'s `SIGN IN` → `load(wallet, { allowHop: true })` | `session` | `orbit-session` |
| `cashOut(wallet)` | `cashout` | `orbit-cashout` |
| `updateSettings(wallet, { enabled: false })` | `disable` | `orbit-cashout` |

Everything else keeps today's behaviour: no session, no hop, fail quietly. ⚠️ **A deposit never needs
a signature at all** — the on-chain transfer is the proof — so it must not hop; it already hops via
`sendToken`'s own path.

When `signOrbitMessage` returns `'hopped'` the page is navigating away, so the caller must return
rather than continue. Give `ensureSession` an explicit throw the callers already absorb, or return a
sentinel — whichever, **the branch must not fall through into a request that will never resolve**.

- [ ] **Step 4: The return, as the third arm**

`useOrbit.resumeOrbitDeeplink(params)`:

```ts
  /**
   * Completes an Orbit action whose signature was produced in the wallet app. A no-op unless an Orbit
   * hop was pending, which is how it stays out of the way of the identically-shaped bet and
   * self-exclusion returns (AC27).
   */
  const resumeOrbitDeeplink = async (params: URLSearchParams): Promise<boolean> => {
    const pending = getPendingOrbit();
    // THIS is what protects the bet hop: no Orbit stash, no claim on this return.
    if (!pending) return false;

    clearPendingOrbit();

    const signature = readSignedMessageSignature(params);
    if (!signature) return false;

    if (pending.action === 'session') {
      const result = await orbitService.createOrbitSession(pending.wallet, pending.nonce, signature);
      const token = result?.data?.sessionToken;
      if (!token) return false;
      storeSession(pending.wallet, token, result.data.expiresAt);
      sessionToken.value = token;
      sessionWallet.value = pending.wallet;
      await load(pending.wallet);
      return true;
    }

    const token = readStoredSession(pending.wallet);
    if (!token) return false;

    if (pending.action === 'cashout') {
      await orbitService.requestOrbitCashOut(token, pending.nonce, signature);
    } else {
      lastForcedCashOut.value = orbit.value?.balance ?? 0;
      const result = await orbitService.patchOrbitSettings(token, {
        enabled: false,
        nonce: pending.nonce,
        signature,
      });
      orbit.value = result?.data ?? orbit.value;
      // The same disable toast the in-page path fires, so the forced cash-out is never silent.
      toast.info(
        (lastForcedCashOut.value ?? 0) > 0
          ? `Orbit is off. Your balance of ${lastForcedCashOut.value} ${orbit.value?.tokenLabel ?? ''} has been cashed out to your wallet.`
          : 'Orbit is off. There was no balance to cash out.'
      );
    }

    await refreshQuietly(pending.wallet);
    return true;
  };
```

`src/hooks/useGame.ts` — insert the third arm in `handleMobileDeeplinkReturn`'s `kind === 'sign'`
chain, **after** self-exclusion and **before** the bet:

```ts
    // kind === 'sign' covers signAllTransactions (a bet) and signMessage (a self-exclusion, and since
    // #141 an Orbit action) — the wallet returns the same shape for each, so what we stashed before
    // leaving is the only thing that tells them apart. Each arm is a no-op unless its OWN stash is
    // present, and the BET is deliberately last: it is the money path, and a wrong `true` above it
    // would drop a paid bet.
    try {
      if (await useOrbit().resumeOrbitDeeplink(params)) {
        stripParams();
        return consumeReturnTo();
      }
    } catch {
      // The Orbit action could not be completed; fall through rather than stranding the return hop.
    }
```

- [ ] **Step 5: Verify**

```bash
yarn lint
yarn build
```

Then, on a **desktop** (what can be checked without a phone):
- a normal extension wallet still signs in, cashes out and disables in-page — `isMobileDeeplinkContext()`
  is false there, so no branch is taken and nothing changed;
- `disconnectMobileWallet()` clears five keys, not four;
- `useSelfExclusion.ts` compiles against the renamed helper and its behaviour is untouched.

⚠️ **AC26 and AC27 are verifiable ONLY on a physical phone**, and the `/responsible-gaming` deeplink
return has never been run on one either. Both must be exercised in the same session, plus a **paid
bet** hop — that last one is the money path, and a wrong `true` in the Orbit arm would drop it. This
is a hand-off item for the user, flagged explicitly in Task 12's report.

- [ ] **Step 6: Commit**

```bash
git add src/types/mobileWallet.ts src/hooks/useMobileWallet.ts src/hooks/useSelfExclusion.ts src/hooks/useOrbit.ts src/hooks/useGame.ts src/components/OrbitBalance.vue src/components/GameHeader.vue
git commit -m "Let Orbit sign in, cash out and disable on a deeplink wallet."
```

---

## Task 11: The declinable redirect

**Files:**
- Create: `src/components/orbit/OrbitRedirectToast.vue`
- Modify: `src/hooks/useOrbitRedirect.ts:76-78`

**UNTOUCHED (AC28):** `POLL_INTERVAL_MS = 2000`, `POLL_TIMEOUT_MS = 45000`, `findOrbitRound`,
`followOrbit`, the `Ended` watcher and the `onUnmounted` cancel. The 2 s cadence's cost is a separate,
earmarked follow-up card and is explicitly out of scope.

- [ ] **Step 1: The toast content component**

```html
<script setup lang="ts">
  /**
   * The redirect announcement, with the decline control in it.
   *
   * The TOAST carries STAY HERE, not the console module: the module renders only for an enrolled
   * player on an Orbit-eligible type, and this fires exactly when the player is watching a DIFFERENT
   * type (Space Jackpot included) — where there is no module on screen to put a button in.
   *
   * A toast is not a dialog: one sentence and one text button, nothing else.
   */
  defineProps<{ gameName: string }>();
  const emit = defineEmits<{ (e: 'stay'): void }>();
</script>

<template>
  <div class="flex flex-col gap-1.5">
    <span class="vt323-regular text-[17px] leading-[1.25]">
      Orbit placed a bet for you on {{ gameName }}. Taking you there…
    </span>
    <button
      type="button"
      class="press-start-2p-regular self-start text-[8px] uppercase tracking-[0.06em] text-primary underline underline-offset-2"
      @click.stop="emit('stay')"
    >
      Stay here
    </button>
  </div>
</template>
```

- [ ] **Step 2: Carry it in the toast**

`useOrbitRedirect.ts` — replace the bare `toast.info(string)` in `follow()`:

```ts
    // The 3s window IS the decline window.
    //
    // ⚠️ CLOSING THE TOAST IS NOT DECLINING. Only pressing STAY HERE sets `declined`; dismissing a
    // toast reads as acknowledging, and silently cancelling a redirect on dismissal would strand a
    // player on a dead round while their money rides on a live one — the exact failure this hook
    // exists to fix. Hence closeOnClick: false.
    let declined = false;
    toast.info(
      h(OrbitRedirectToast, {
        gameName: type.name,
        onStay: () => {
          declined = true;
        },
      }),
      { autoClose: REDIRECT_DELAY_MS, closeOnClick: false }
    );

    await sleep(REDIRECT_DELAY_MS);
    if (cancelled || declined) return;
```

`vue3-toastify@0.2.8`'s `Content` type is `string | VNode | ((props) => VNode) | DefineComponent`, so
an `h()`-built VNode is a supported first-class content value — no new dependency. Import `h` from
`vue` and the component from `@/components/orbit/OrbitRedirectToast.vue`.

- [ ] **Step 3: Verify**

```bash
yarn lint
yarn build
```

Serve, enable Orbit on **Beast Mode**, watch **Cosmic Sprint** to the end:
- the toast offers `STAY HERE`; press it and you stay on the current page (AC28);
- repeat and ignore it — you are taken there after 3 s;
- repeat and *dismiss the toast by clicking it* — you are still taken there, because dismissing is
  not declining;
- confirm in the network tab that the poll is still every 2 s and that `findOrbitRound` is unchanged.

- [ ] **Step 4: Commit**

```bash
git add src/components/orbit/OrbitRedirectToast.vue src/hooks/useOrbitRedirect.ts
git commit -m "Let the player decline Orbit's post-round redirect, cadence untouched."
```

---

## Task 12: Copy-and-colour audit, full verification, screenshot pass

**Files:** no new ones. This task is the verification bar the spec sets in place of a test suite
(§11), and it is where the copy pass is *checked* rather than written — the copy itself ships inside
Tasks 5-11.

- [ ] **Step 1: The `constraints.md` §6 audit (AC23)**

Walk every Orbit refusal and confirm the split survived **in both directions**:

- **Stay vague** — a forged session, an unknown nonce, and a missing session must remain
  indistinguishable to the caller. Nothing in Tasks 5-11 may have added a message that separates
  them. The panel's `loadFailed` line ("Orbit could not be read for this wallet. Sign in again to
  load it.") is the one surface near this, and it is deliberately about *this wallet's* readability,
  not about which half of the proof failed.
- **Stay clear and name their reason** — already cashed out today, below the minimum cash-out,
  "we haven't seen your `/start` yet", deposit refused while Orbit is off, deposit refused during the
  cool-off. Each of these renders as its own sentence, on the panel or the page.

⚠️ **Flipping a clear refusal to vague is as much a defect as the other direction.**

- [ ] **Step 2: The threshold-vs-floor audit (AC24)**

Read the two rule cards on `/orbit` side by side. They must state **opposite** conditions and neither
explanation may have been shortened into the other:

- *"Orbit sends your balance to your wallet as soon as **a win takes it to** 20.00."*
- *"Orbit stops and sends what is left to your wallet if **a bet takes your balance down to** 5.00."*

And confirm *"Turning this off cashes your whole balance out to your wallet."* appears on **both** the
panel's on/off band and above `/orbit`'s `TURN ORBIT OFF`.

- [ ] **Step 3: The violet audit (AC40)**

Grep the diff for every use of `orbit` as a colour and confirm each is one of: the mark, the balance
figure's `USDC` marker, the on/off checkbox, the `STOP RULES` block's left edge, the picker prompt's
border, the page's `NOW` chip. **No hover, no focus ring, no input border, no primary action.** And
confirm the paused state turns the band and label **amber**, not violet.

```bash
git diff main -- src/ | grep -nE "orbit(-deep)?\b" | grep -iE "hover|focus|ring|border-|bg-orbit"
```

- [ ] **Step 4: The word "paused" audit (AC10)**

```bash
git grep -in "paused" -- src/components/orbit src/pages/OrbitPage.vue src/components/game/GameConsole.vue
```

Every hit must sit behind `isPausedForFunds`. Nothing in the `needsFirstDeposit` branch may use it.

- [ ] **Step 5: The backend zero-diff assertion (AC33)**

```bash
git -C <workspace-root>/monkify-backend status --porcelain
git -C <workspace-root>/monkify-backend rev-parse --abbrev-ref HEAD
git -C <workspace-root>/monkify-backend diff origin/main --stat
```

Expected: clean, `main`, and an empty diff. There is no `feature/orbit-ui-overhaul` branch in that
repo and there must not be one.

- [ ] **Step 6: Full verification (AC36)**

```bash
cd <workspace-root>/.worktrees/orbit-ui-overhaul/monkify-front
yarn lint
yarn build
```

Both must pass clean. If prettier reports `Insert ␍`, the file was written with LF — fix with
`unix2dos <file>` and verify with `od -c`.

- [ ] **Step 7: The screenshot pass (AC35)**

Serve the app against the local Docker stack and compare every Orbit surface against the Option C
artifact at **1440×900** and **390×844**:

| Surface | Artifact plate |
| --- | --- |
| The panel, enabled + funded | 02 |
| The panel, states 2-5 | 03 |
| The panel as a bottom sheet | 04 |
| `/orbit`, desktop | 05 |
| `/orbit`, phone | 06 |
| The seam, both directions | 07 |
| The console module | **none — judge it against the cabinet, not a mockup** |

⚠️ Match the mockup's **design**; where its wording conflicts with the card's decisions, **the card's
copy wins** (AC35).

⚠️ Orbit is **inert without a configured Orbit account** — if `IsConfigured` is false every surface
hides, which is AC30 but tests nothing else. The local default is false, so the stack needs an Orbit
account configured for anything past AC30 to be checkable.

- [ ] **Step 8: Commit anything the audit changed**

```bash
git add -A src/
git commit -m "Audit the Orbit copy and the violet's scope across every surface."
```

---

## Self-review

**Spec coverage.** §4.1 tokens → Task 1. §4.2 mark → Task 1. §4.3 glyphs → Task 1. §5.1
`isOrbitEligibleType` → Task 2. §7.0 F1 → Task 3; F2 → Task 3; F3 → Task 9 (both mounts named);
F4 → Task 9 (read-only note); F5/D16 → Task 10; F6 → Task 10; F7 → Task 10 (`getPendingOrbit`'s
stale guard); F8 → Task 10 (rename, not alias); F9 → Task 6 (carried verbatim); F10 → Task 9 (fixed
height); F11 → Task 5 (the two `<select>` rules kept). §7.1 shared state → Task 2 + Task 5 Step 1.
§7.2 the panel → Task 5. §7.2.1 the five states → Task 5. §7.3 the page → Task 6. §7.4 the module →
Task 9. §7.5 the upsell → Task 7. §7.6 the header row → Task 8. §7.7 routing and embed → Task 6.
§7.8 the deeplink → Task 10. §7.9 the redirect → Task 11. §7.10 the copy pass → written in Tasks
5-11, audited in Task 12. §10.2's 24-file checklist maps 1:1 onto the File Structure table above.

**Type consistency.** `isOrbitEligibleType`, `activeGameType`, `stopRules`, `hasCashOutThreshold`,
`hasCashOutFloor`, `recentOrbitWin`, `recordOrbitWin`, `lastForcedCashOut`, `enableLockedUntil`,
`enableLockedLabel`, `resumeOrbitDeeplink`, `signMobileOrbit`, `getPendingOrbit`, `clearPendingOrbit`,
`readSignedMessageSignature`, `MobilePendingOrbit`, `openOrbit`, `orbitOpen` — each is defined in
exactly one task and spelled identically everywhere it is consumed.

**Known gaps, stated rather than hidden.**
1. **AC26 / AC27 cannot be verified here** — they need a physical phone with a deeplink wallet, plus a
   paid-bet hop and a `/responsible-gaming` hop in the same session. Task 10 Step 5 and Task 12 both
   flag it; it goes in the hand-off report as a user action.
2. **The console module has no mockup.** The Option C artifact draws the panel, the page and the seam
   only. Task 9's design is derived from spec §7.4 plus the cabinet language already in the codebase,
   so it is the one surface that needs the user's eye rather than a comparison.
3. **AC25 is satisfied vacuously** — Option C introduces no `ADVANCED` flap at all. It must stay
   satisfied if one is ever added.

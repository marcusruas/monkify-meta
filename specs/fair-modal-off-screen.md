# SpaceStroker — "FAIR modal is off screen" Fix — Implementation Spec

> **Status: Ready for implementation** — refined via Trello card
> [#109 fair modal is off screen](https://trello.com/c/ye9CYjpo) (2026-07-27).
> Repos affected: `monkify-front` (the only code change) and this doc in `monkify-meta`.
> **No `monkify-backend` change.**
>
> **Purpose:** On a GPU-rasterizing desktop the **FAIR** modal opens with roughly its top half above
> the viewport and its dim overlay covering only the 64px header strip, because the modal is a
> `position: fixed` element authored **inside** an ancestor that carries `backdrop-filter`. Fix the
> modal (and the two other overlays trapped by the same ancestor) so verification is reachable for
> every desktop player.

---

## 1. Summary

`GameHeader.vue`'s root element carries `backdrop-blur-md`. Per CSS, an element whose
`backdrop-filter` is not `none` becomes a **containing block for `position: fixed` descendants** —
exactly like `filter` and `transform` do. Every `fixed` overlay authored inside the header therefore
resolves `inset-0` against the header's own **64px-tall box** instead of the viewport.

| Overlay | Where | What that does today (desktop, GPU on) |
| --- | --- | --- |
| **FAIR modal** — `fixed inset-0 z-[60] flex items-center justify-center` | `src/components/game/GameFairModal.vue:13`, mounted at `GameHeader.vue:319` | Centres on the header's mid-line (y ≈ 32px), so ~half the panel is above the viewport top and unreachable. Its `absolute inset-0` backdrop (`:14`) dims only the header strip, so the page behind stays bright. |
| **☰ menu click-away** — `fixed inset-0 z-40` | `src/components/GameHeader.vue:221` | Covers only the header strip, so clicking out in the page body never closes the menu. |
| **Wallet popover click-away** — `fixed inset-0 z-40` | `src/components/header/WalletButtons.vue:80` | Same defect, but **latent** — see §7.4. |

**Why phones are fine.** `src/assets/styles/arcade.css:269` sets `backdrop-filter: none !important` on
every `[class*='backdrop-blur']` element under `html.light-graphics`, and `usePerfMode` puts every
viewport below `lg` (1024px) into light mode unconditionally (`usePerfMode.ts`, resolution order step
1). No `backdrop-filter` ⇒ no containing block ⇒ correct behaviour. A desktop with hardware
acceleration **off** lands in light mode too. The bug needs a **wide viewport with a working GPU** —
which is the majority of desktop players.

The sibling **settings** modal is unaffected because it already wraps itself in
`<Teleport to="#app">` (`PlayerCustomizationModal.vue:140`, with a comment recording an earlier
run-in with a stacking problem). `GameFairModal` was never given one.

**The fix is two different mechanisms on purpose** (see §3 decision 5 — this corrects the refined
card):

- **The modal** teleports to `#app`, matching the settings modal. A modal *wants* to paint above the
  whole app, and teleporting gives it exactly that.
- **The two click-aways cannot be teleported.** They must sit **above the page but below their own
  dropdown**, and once teleported to `#app` that is unexpressible — App.vue's root div is
  `relative z-10`, i.e. its own stacking context, so the header's `z-40` is sealed *inside* it and
  can never out-rank a teleported sibling (§7.3). They are replaced by a document-level
  outside-click listener instead.

This is a **bug fix**, not a new feature.

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo (§2):** work lives in `.worktrees/fair-modal-off-screen/<repo>`; both shared
  checkouts stay on `main`. Branch `feature/fair-modal-off-screen` is the merge/PR ref, already
  pushed to `origin` in `monkify-front` and `monkify-meta`.
- **No backend change** ⇒ **no `SpaceStroker.Tests` unit tests** and **no
  `SpaceStroker.Tests.Integration` test**. Per `constraints.md` → "Integration tests are mandatory for
  every new feature", this is a **bug fix**, explicitly excluded from that mandate.
- `monkify-front` has **no test runner** ⇒ verification is the manual QA matrix in §13.
- **Secrets/config:** none touched. No env var, no `VUE_APP_*` flag.
- **Scope:** edit the three existing components plus one new small composable; do not restyle the
  header, the modal, or anything else.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | Root cause is the header's `backdrop-filter` creating a containing block for `fixed` descendants — **not** a z-index, sizing, or `max-h` problem. Do not "fix" this by tweaking `max-h-[90vh]`, `items-center`, or z-indexes. |
| 2 | **The FAIR modal is fixed with `<Teleport to="#app">`**, byte-for-byte the pattern at `PlayerCustomizationModal.vue:140`. Not `body`: `#app` keeps the overlay inside the app's styling/inheritance chain. |
| 3 | **Rejected:** removing the header's `backdrop-blur`. It is the deliberate frosted header from `features/app-design-refinement.md`; undoing it is a visual regression to fix a layout bug, and it would leave the next `fixed` child in the header equally trapped. |
| 4 | **Rejected:** moving `GameFairModal` into `App.vue` behind shared open/close state. Larger diff, adds cross-component state for one modal, and does nothing for the two click-aways. |
| 5 | **⚠️ CORRECTS THE REFINED CARD.** The card said to teleport the two click-away backdrops and drop them `z-40 → z-30`. **That does not work** — see §7.3. The backdrops are **deleted** and replaced by a shared **`useClickOutside`** composable driving `menuOpen` / `showMobileMenu`. |
| 6 | The composable lands at **`src/hooks/useClickOutside.ts`** (two call sites, and `src/hooks` is where shared frontend behaviour lives per `conventions/frontend.md`). No existing click-outside helper was found in the repo. |
| 7 | **Accepted behaviour difference:** with the backdrop gone, an outside click both closes the menu **and** reaches whatever was under it (today the invisible backdrop swallowed it). This is standard menu behaviour and is not worth a capture-phase swallow. |
| 8 | **No behaviour change beyond positioning/dismissal.** The modal still closes only on ✕ and backdrop click. **No** Esc-to-close, **no** background scroll lock, **no** focus trap — deliberately parked so the fix stays trivially verifiable. |
| 9 | Level = **Minor change (bug fix)**. No new capability, no API/DB/SignalR surface, **no `features/` doc** at ship time. |
| 10 | Reach: the header renders identically on every non-`chromeless` route, so one fix covers the lobby, play, stats and history pages. No route-specific work. |

---

## 4. Data model

N/A — no backend, no entity, no EF mapping, no migration.

---

## 5. Configuration

N/A — no `GeneralSettings`, env var, seed or Terraform change.

---

## 6. Backend behaviour

N/A — `monkify-backend` is untouched. `GET /games/{id}/verification` and the whole provably-fair
surface (`features/provably-fair-final.md`) are unchanged: `GameFairPanel` is reused verbatim inside
the modal (`GameFairModal.vue:75`) and its `:active="open"` lazy-fetch trigger keeps the same
contract.

---

## 7. Frontend behaviour (`monkify-front`)

### 7.1 `GameFairModal.vue` — teleport the modal

Wrap the existing root in a `<Teleport>`; **nothing inside changes**. Keep `v-if="open"` on the inner
div (Teleport itself always renders — same shape as `PlayerCustomizationModal.vue:140-228`):

```html
<template>
  <!-- Teleported to #app (not <body>) so the fixed overlay escapes the header's containing block —
       the header carries `backdrop-filter` (backdrop-blur-md), which makes it the containing block
       for `position: fixed` descendants, so `inset-0` resolved against the 64px header box and put
       half the panel above the viewport. Same fix as PlayerCustomizationModal. -->
  <Teleport to="#app">
    <div v-if="open" class="fixed inset-0 z-[60] flex items-center justify-center p-3 md:p-6">
      <!-- …unchanged… -->
    </div>
  </Teleport>
</template>
```

Notes for the implementer:

- `GameFairModal.vue` has **no `<style scoped>` block** and styles itself with global classes
  (`arcade-panel`, `press-start-2p-regular`, `vt323-regular`, Tailwind utilities), so teleporting
  cannot break scoped-CSS attribute matching. Do **not** add a scoped block.
- `#app` always exists — it is the Vue mount root (`public/index.html:20`, `main.ts:153`) — which is
  the same assumption the settings modal has shipped on.
- `v-if="!isEmbedded"` stays on the component at `GameHeader.vue:319`, so nothing is teleported when
  embedded.

### 7.2 The two click-aways — replace the backdrop with an outside-click listener

New composable `src/hooks/useClickOutside.ts`:

```ts
import { onBeforeUnmount, watch, type Ref } from 'vue';

/**
 * Calls `handler` when a click lands outside `target` while `active` is true.
 *
 * Replaces the `fixed inset-0` click-away backdrops that used to sit inside the header: the header
 * carries `backdrop-filter`, which makes it the containing block for `position: fixed` descendants,
 * so those backdrops only ever covered the 64px header strip. Teleporting them out is not an option
 * either — App.vue's root is `relative z-10` (its own stacking context), so a teleported sibling can
 * only paint entirely above the app (swallowing clicks on the dropdown itself) or entirely below it
 * (never receiving a click at all).
 *
 * `target` must wrap BOTH the toggle button and the dropdown, so the click that opens the menu
 * counts as "inside" and cannot immediately close it.
 */
export function useClickOutside(
  target: Ref<HTMLElement | null>,
  handler: () => void,
  active: Ref<boolean>
) { /* add a document 'click' listener while active; remove on close and onBeforeUnmount */ }
```

Behaviour requirements:

- Listen on `document` for **`click`** (bubble phase). A synthesized `click` covers touch, so no
  separate `touchstart` listener is needed.
- Register only while `active` is `true`; remove the listener when it flips to `false` **and** in
  `onBeforeUnmount`. No listener may outlive the component.
- Ignore the event when `target.value?.contains(event.target as Node)` — this is what makes the
  opening click safe without a `nextTick` dance.
- Guard `event.target` not being a `Node` (defensive; never throw during a click).

**`GameHeader.vue`:**

1. Delete the backdrop at `:221` (`<div v-if="menuOpen" class="fixed inset-0 z-40" …>`).
2. Add `ref="menuRoot"` to the wrapper `<div v-if="hasOperatorContext" class="relative">` (`:196`),
   which already contains the ☰ button **and** the dropdown.
3. `useClickOutside(menuRoot, () => (menuOpen.value = false), menuOpen)`.
4. Leave every existing `menuOpen = false` in the item handlers untouched.

**`WalletButtons.vue`:**

1. Delete the backdrop at `:80`; the surrounding `<template v-if="showMobileMenu">` then wraps a
   single element and can collapse back onto the menu `div` (`v-if="showMobileMenu"`).
2. Add `ref="walletMenuRoot"` to `<div v-if="mobileConnected" class="relative">` (`:54`).
3. `useClickOutside(walletMenuRoot, () => (showMobileMenu.value = false), showMobileMenu)`.
4. The deliberate **two-tap disconnect** is unchanged — Disconnect is still a separate tap inside the
   menu.

### 7.3 Why the click-aways cannot be teleported (the finding that corrects the card)

`App.vue:243` renders the header as `<game-header class="relative z-40" />`, and App.vue's own root
(`:217`) is `class="min-h-screen flex flex-col relative z-10 overflow-x-hidden overflow-y-auto"`.
`position: relative` + a numeric `z-index` makes that root **its own stacking context**, so:

- everything inside it — header `z-40`, `<main>`, the dropdown's `z-50` — is ordered **only against
  its siblings inside that context**, and the whole subtree paints at the app root's `z-10` slot;
- a node teleported to `#app` is a **sibling of that root**, so its z-index is compared against
  `10`, never against `40`.

Therefore a teleported backdrop can only be:

| Teleported backdrop z-index | Result |
| --- | --- |
| `≥ 10` (incl. the card's proposed `z-30`) | Paints above the **entire** app root — including the header and the open dropdown — so clicks on the menu items hit the backdrop and the menu just closes. Broken. |
| `< 10` / `auto` | Paints below the app root, which covers the viewport (`min-h-screen`), so the backdrop never receives a click at all. Also broken. |

The modal has no such problem: it *wants* to be above everything, and `z-[60] > 10` gives it that.

### 7.4 The wallet popover is a **latent** case, fixed defensively

`WalletButtons.vue:80` lives inside `v-if="mobileConnected"`, which sits under
`v-if="isMobileDeeplinkContext() || mobileConnected"`. `isMobileDeeplinkContext()` requires
`isMobileBrowser()` (`useMobileWallet.ts:159-163`), so on a desktop UA the popover never renders and
the defect is not observable there.

It is **not** purely theoretical, though: light graphics is decided by **viewport width**
(`usePerfMode.ts` → `useIsMobile`, `< lg`), while this branch is decided by **user agent**. A tablet
or a large phone in landscape at **≥ 1024px with a mobile UA** gets the mobile wallet path *and* the
blur — the exact combination where the backdrop is trapped. The fix costs the same two lines as the
header menu, so it is applied rather than deferred.

### 7.5 Untouched

The header's appearance (`backdrop-blur-md` stays), the modal's markup, copy, `arcade-panel` styling,
`max-h-[90vh]`, the FAIR PLAY explainer, `GameFairPanel` and its Web Worker replay, `GameIdleOverlay`
(rendered from `GamePage.vue:113`, outside the header) and `MobileWalletModal` (already
`teleport to="body"`). No composable other than the new one, no service, type, route or env change.

### 7.6 Open QA item — the clipped wordmark in the card screenshot

The screenshot on card #109 also shows the `SPACE STROKER` wordmark cut off at the left edge, which
the refined card attributed to overflow from the trapped modal. **That attribution is not supported
by the code:** the trapped modal is centred within the header's own width (so it adds no horizontal
overflow), and App.vue's root is `overflow-x-hidden` anyway. Treat it as **unexplained**: check it
explicitly during QA (§13 step 8). If it survives the fix it is a separate defect and belongs on its
own card — do **not** widen this one to chase it.

---

## 8. Flow diagrams

N/A — a DOM-position fix and a listener swap; no flow.

---

## 9. Out of scope

- Esc-to-close, background scroll lock, focus trap / focus return for either modal.
- Any redesign or content change to the FAIR modal, `GameFairPanel`, or `public/verify.html`.
- A lint rule or convention doc forbidding `fixed` overlays inside the blurred header.
- Removing or altering the header's frosted `backdrop-blur`.
- The clipped-wordmark observation (§7.6) if it outlives the fix.
- Any backend, SignalR, or data change.

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

None.

### 10.2 Frontend (`monkify-front`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/components/game/GameFairModal.vue` | Wrap the root `v-if="open"` div in `<Teleport to="#app">` (+ the explanatory comment from §7.1). Nothing else. |
| 2 | `src/hooks/useClickOutside.ts` | **New.** Shared outside-click composable (§7.2): document `click` listener while active, containment check, cleanup on close + `onBeforeUnmount`. |
| 3 | `src/components/GameHeader.vue` | Delete the `fixed inset-0 z-40` backdrop (`:221`); add `ref="menuRoot"` to the `relative` wrapper (`:196`); wire `useClickOutside(menuRoot, …, menuOpen)`. |
| 4 | `src/components/header/WalletButtons.vue` | Delete the `fixed inset-0 z-40` backdrop (`:80`) and collapse the now-single-child `<template v-if="showMobileMenu">`; add `ref="walletMenuRoot"` to the `relative` wrapper (`:54`); wire `useClickOutside(walletMenuRoot, …, showMobileMenu)`. |
| 5 | `src/hooks/index.ts` | Only if it re-exports hooks — follow whatever pattern is already there; do not invent one. |

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/fair-modal-off-screen.md` | This spec. |
| 2 | `INDEX.md` | Spec row (status Ready) + "Feature currently in flight". |
| 3 | `conventions/frontend.md` | *(At ship time, via `/monkify-ship-feature`.)* Record the rule this bug came from: **any `fixed` overlay must be teleported out of the header** (it carries `backdrop-filter`, hence is a containing block for `fixed`), and **click-away backdrops must not be** — use `useClickOutside`, because App.vue's root `relative z-10` seals the header's z-40 inside its stacking context. |

---

## 11. Unit tests

N/A — frontend-only, and `monkify-front` has no test runner. No backend behaviour changes, so
`SpaceStroker.Tests` is untouched and no integration test is mandated (`constraints.md` → bug fixes
are excluded). Verification is §13.

---

## 12. Acceptance criteria

Copied from the Trello **Acceptance Criteria** checklist (item 6 reworded to match decision 5 — the
teleported-backdrop plan it referenced does not work):

1. Given a desktop browser with hardware acceleration ON (normal graphics mode), when the player
   opens Fair from the header menu, then the modal is fully inside the viewport and vertically
   centred on it — no part of the panel is above the top edge.
2. Given the Fair modal is open on desktop, when the player looks at the page behind it, then the
   whole viewport is dimmed by the `black/70` backdrop — not just the 64px header strip.
3. Given the Fair modal is open on desktop, when the page is inspected, then no horizontal scrollbar
   or sideways offset is introduced and the `SPACE STROKER` wordmark stays fully visible at the left
   of the header.
4. Given the Fair modal is open, when the player clicks the ✕ or the dimmed backdrop, then the modal
   closes — unchanged from today; and the modal's scrollable content, the FAIR PLAY explainer and the
   reused `GameFairPanel` behave exactly as before.
5. Given a phone-width viewport, or a desktop with hardware acceleration OFF
   (`html.light-graphics`), when the player opens Fair, then the modal is centred and dimmed exactly
   as it is today — no regression to the currently-working path.
6. Given the ☰ header menu is open on desktop, when the player clicks anywhere in the page body
   outside the header, then the menu closes; and when the player clicks a menu item (Settings /
   Stats / Fair / History), that item still receives the click and acts.
7. Given a connected wallet and the wallet popover open (mobile wallet path), when the player clicks
   anywhere outside it, then the popover closes; and the Disconnect button inside it still receives
   clicks (the two-tap disconnect is preserved).
8. Given the game is loaded in an iframe (embedded), when the header menu is opened, then Stats and
   Fair are still absent and no Fair overlay is teleported into `#app`.
9. Given the fix is applied, when Fair is opened from the lobby, the play screen, the stats page and
   the history page, then it renders correctly on all four — and the header's frosted
   `backdrop-blur` appearance is unchanged everywhere.

---

## 13. Manual QA

Running the front from the worktree needs two files the repo does not track — copy them from the hub
first (`.env.local`, otherwise the lobby reads "game unavailable"; and `yarn.lock`):

```bash
cp monkify-front/.env.local monkify-front/yarn.lock .worktrees/fair-modal-off-screen/monkify-front/
cd .worktrees/fair-modal-off-screen/monkify-front && yarn install --frozen-lockfile && yarn serve --port 8081
```

1. **Desktop, normal graphics (the failing case).** Open `/space-stroker/game`, ☰ → **Fair**. The
   panel is centred in the viewport, fully visible, and the whole page behind it is dimmed. Confirm
   in DevTools that the overlay is a direct child of `#app`, not of `.app-header`.
2. Scroll the modal body; ✕ and a backdrop click both close it.
3. Repeat from `/space-stroker/game/play` (during and after a round), `/game/stats` and
   `/game/history`. On `play`, after a round ends, the FAIR checks still populate.
4. **Desktop, light graphics.** Turn Chrome's hardware acceleration off (or use a `< lg` window),
   reload, and repeat step 1 — must be unchanged from today.
5. **Phone width** (device emulation): same.
6. ☰ menu — open it, click far out in the page body: it closes. Re-open and click **Settings**,
   **Stats**, **Fair**, **History** in turn: each one acts (the menu no longer eats the click).
   Confirm no stray full-screen div is left in the DOM while the menu is open.
7. **Embedded:** load `public/embed-example.html` (or `?embed=1`); ☰ shows no Stats/Fair, and `#app`
   contains no teleported Fair overlay.
8. **§7.6 check:** with the modal open on the desktop failing case, confirm there is no horizontal
   page scroll and the wordmark is whole. If it is still clipped, capture a screenshot + the window
   width and raise a separate card — do not fix it here.
9. `yarn lint` clean. ⚠️ The repo is CRLF (`core.autocrlf=true`); editing with a LF-writing tool
   produces `Insert ␍` prettier errors — verify line endings if lint complains.

---

## 14. Trello / pipeline

- **Source card:** [#109 — fair modal is off screen](https://trello.com/c/ye9CYjpo) — moved to
  **Specs created**; the **Implementation** section on the card carries branch + spec + worktree.
- **Next:** `/monkify-implement-feature 109` to build it, then `/monkify-ship-feature 109` to sync the
  KB (`conventions/frontend.md`) and merge to `main`.

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-07-27 | Initial spec from refined card #109 (frontend-only containing-block bug fix). Corrects the card's decision 5: the two click-away backdrops **cannot** be teleported (App.vue's root `relative z-10` stacking context, §7.3) and are replaced by a new `useClickOutside` composable. Records that the wallet popover is a **latent** case (§7.4) and that the clipped-wordmark symptom is **unexplained** by the root cause (§7.6). |

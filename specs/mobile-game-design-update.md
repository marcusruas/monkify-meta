# Space Stroker — "Mobile game design update" — Implementation Spec

> **Status: Shipped** — merged via `feature/mobile-game-design-update` on **2026-08-02**. Refined via
> Trello card
> [mobile game design update](https://trello.com/c/yPwW5Awx/134-mobile-game-design-update) (#134, refined 2026-08-02).
> Distilled summary: `../features/mobile-game-design-update.md`.
> ⚠️ **Shipped with three deviations this spec was not written for, plus partial verification — see
> §16.** Two of them contradict the card's own acceptance criteria (**AC #17** tab colour, **AC #18**
> desktop untouched); where this spec and the shipped code disagree, **the code is the newer
> decision**. §1's summary table and §2's "§8 Scope" line are stale on both counts — §16 is current.
> Repos affected: **`monkify-front`** and this doc in **`monkify-meta`**. **`monkify-backend` is not touched.**
> Branch / worktree: `feature/mobile-game-design-update` → `.worktrees/mobile-game-design-update/<repo>`.
> Change level: **Frontend feature (visual)** — new capability on the mobile game screen, no backend surface.
>
> **Purpose:** On phones the game screen looks nothing like the PC game. `features/mobile-layout.md`
> deliberately hides Edson and collapses the three arcade console modules into a generic gradient bet
> form — no cabinet art, no visor, no lever. This spec brings the **mascot** and the **real cabinet
> console with the lever** to the mobile `/game/play` screen, re-composed for portrait as **two plate
> modules**, per the mock attached to the card.

---

## 1. Summary

The mobile GAME segment becomes a **three-band vertical column** that fits the screen without page
scroll:

```
┌─────────────────────────────┐
│  GAME  |  CHAT   (tabs)     │  unchanged
├─────────────────────────────┤
│  terminal (.mobile-terminal)│  band 1 — min height, scrolls internally
├─────────────────────────────┤
│  Edson (GameMonkey, GIF)    │  band 2 — takes the remainder, shrinks FIRST
├─────────────────────────────┤
│ [visor+keyboard] [ lever ]  │  band 3 — the cabinet, pinned bottom, never shrinks
└─────────────────────────────┘
```

| Area | Today (shipped `mobile-layout` + `mobile-preset-keypad`) | After this spec |
| --- | --- | --- |
| **Bet UI** | One compact gradient panel: input **or** named key grid + a `PLACE BET` button | The **real cabinet**: `terminal.png` plate art, black visor input, `keyboard.png`, the lever |
| **Action** | `PLACE BET` / `NEXT ROUND` text button, disabled when it can't fire | The **lever**, always pullable; an invalid pull twitches + `error1.wav` |
| **Preset picker** | Named, uncapped arcade keys (`1 RABBIT`), selected state, dimmed when closed | The 8 `button<N>.png` keys **inside the plate**, **numbers only**, capped at 8 (desktop parity) |
| **Edson** | Hidden (`hidden lg:block`) | Mounted and **animating**, with a deferred GIF prefetch |
| **Wallet connect** | In-console `CONNECT WALLET` (deeplink) + header `CONNECT` | **Header `CONNECT` only** (the embedded popup-blocked retry stays on the plate) |
| **Tabs** | `GAME \| CHAT`, filled-amber active, unread dot | **Unchanged** |
| **Desktop (≥ lg)** | — | **Unchanged, pixel-identical** |

This intentionally **reverts the mobile half of `specs/mobile-preset-keypad.md`** (named, uncapped
keys) in favour of PC parity. The trade is accepted with eyes open: the readability win and the
uncapped keypad are given up, and a >8-choice game stays partly unplayable on mobile until the
existing desktop-cap card lands (see §9).

### 1.1 The mock

The card's attachment is the design source of truth (`conventions/frontend.md` → Design workflow). It
shows, top to bottom: header (logo · CONNECT · menu) → tabs → CRT terminal panel → Edson on his saucer
over the space background → **two light-grey riveted plates side by side**, roughly equal width, flush
to the bottom edge: the left carries a black visor showing `******` with `keyboard.png` beneath it,
the right carries the red-handled lever.

**Copy comes from the card's decisions, not the mock** — the mock's `>_ TERMINAL` / `💬 CHAT` tab
relabel is explicitly **declined** (decision 11). Match the mock's *design*, not its *wording*.

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **§2 Git — worktree per repo.** `feature/mobile-game-design-update` lives in
  `.worktrees/mobile-game-design-update/<repo>`; the shared checkouts stay on `main`. Push to `origin`
  right after branch work (`AGENTS.md` rule 5). Already done for `monkify-front` + `monkify-meta`.
- **§3 Unit tests.** No backend change ⇒ **no `SpaceStroker.Tests` change**. `monkify-front` has **no
  test framework** (`package.json` exposes `serve` / `build` / `lint` / `lint:fix` only).
- **`AGENTS.md` rule 8 / §3 Integration tests — N/A.** The mandatory happy-path integration test does
  **not** apply: this card has no backend surface to exercise (no endpoint, DTO, entity, migration,
  consumer, SignalR contract or game-flow change). Verification is manual/visual — §13.
- **§6 User-facing messaging.** No new copy at all; nothing new is revealed. Status text keeps coming
  from the terminal's existing `gameStatusLabel`.
- **§8 Scope.** Desktop (≥ 1024px) is untouched, in any way. No secrets, no config, no migrations.

Feature-specific, verified in the codebase:

- **Light graphics mode is ALWAYS on below `lg`.** `usePerfMode.ts:39` — `lightMode = isMobile || weakRenderer`,
  and `detect()` short-circuits on `isMobile`. Everything in this spec therefore renders under
  `html.light-graphics`, which **strips**: `backdrop-filter` on any `backdrop-blur-*`
  (`arcade.css:934`), wide glow halos (`:954-963`), blend modes (`:1007`), and the infinite animations
  `.blink`, `.blink-fast`, `.animate-wobble` (`:995-1004`). Consequences for this work:
  - The mobile visor placeholder **will not blink** — do not spec or rely on `.blink` there (the mock
    shows a static `******`, which is what you get).
  - `GameMonkey`'s `.animate-wobble` is already inert on mobile. That is fine and expected: the GIF's
    own frames are not a CSS animation and still play. `.animate-fly-in` is **not** in the strip list,
    so the entry fly-in still runs.
  - Any new plate/lever styling must add no `backdrop-filter`, `blur()`, `mix-blend-mode`, or infinite
    animation.
- **`arcade.css` beats Tailwind utilities — do not fight it with classes.** `src/main.css` `@import`s
  `arcade.css` **after** `@tailwind utilities`, so at equal specificity `.btn-arcade` /
  `.btn-arcade--sm` win over any utility stacked on them (`conventions/frontend.md`, and the
  `mobile-preset-keypad` post-ship note that learned it). **Build the plates with plain Tailwind +
  arcade tokens**, as `GameMobileTabs.vue` does. If a light-mode override is ever needed, it goes in
  the `arcade.css` `html.light-graphics` block, never in a component `<style scoped>` block.
- **CRLF / prettier.** `monkify-front` lints against CRLF line endings; tools that rewrite endings to
  LF produce `Insert ␍` failures. Verify with `yarn lint` before committing.

---

## 3. Resolved product decisions

Decisions 1–12 are the card's refinement (2026-08-02). Decisions 13–16 are **resolved by this spec** —
the mock shows a free-text game, so it does not answer where the preset keys go, and two acceptance
criteria pull against the card's prose (see §3.1).

| # | Decision |
| --- | --- |
| 1 | **The console uses the real desktop assets** — `terminal.png` (the plate), the black CSS visor, `keyboard.png`, and the `leverup.png` / `leverdown.png` / `lever_to_up.gif` / `lever_to_down.gif` lever — **re-composed for portrait as TWO modules** (visor + keyboard left, lever right). Not a scale-down of the desktop 3-column layout. |
| 2 | **Today's compact gradient bet panel is deleted.** The cabinet is the only mobile bet UI. `GameConsole`'s `mobile` branch is rewritten; `presetKeys`, `canPlaceBet`, `betButtonLabel`, `showMobileConnect` and the in-panel `CONNECT WALLET` go away. This reverts the mobile half of `specs/mobile-preset-keypad.md`. |
| 3 | **The terminal keeps its `.mobile-terminal` CRT panel** (metal bezel + recessed screen) — it already matches the mock. |
| 4 | **Preset games render the 8 `button<N>.png` keys inside the plate**, like desktop. The grey plate baked into that art was the reason mobile abandoned it; with the cabinet plate present that reason is gone. |
| 5 | **Keys are numbers only, no names.** The terminal's `> CHOICES:` list is the mapping — same mental model as PC. |
| 6 | **A game with more than 8 choices exposes keys 1–8 only**; choices 9+ are unreachable on mobile, matching desktop. The 8-key cap becomes **one shared gap on both layouts**, fixed once by the existing desktop-cap card. Accepted knowingly. |
| 7 | **Portrait fits the screen — no page scroll.** Order: tabs → terminal → Edson → console. Priority when space is tight: **console > terminal > Edson**. Console pinned bottom, terminal keeps a guaranteed minimum and scrolls internally, **Edson takes the remainder and shrinks first**. The lever is never scrollable out of view. |
| 8 | **The lever behaves exactly like desktop: always pullable.** An invalid pull (betting closed, already bet this round, preset game with nothing selected, self-excluded, choice too short, duplicate characters, choice taken) drops the lever briefly and plays `error1.wav` via the existing `leverError()`. **No dim/disabled lever state.** |
| 9 | **The lever doubles as "next round"** at `RewardForWinnersCompleted`, like desktop. The terminal copy (`PULL LEVER FOR NEXT RACE` / `NEXT DRAW: …`) is the only label — no `NEXT ROUND` button text anywhere. |
| 10 | **The in-console `CONNECT WALLET` is removed; the header `CONNECT` is the only connect affordance.** Verified safe: `WalletButtons.vue:71` branches on `isMobileDeeplinkContext() \|\| mobileConnected` and calls `openWalletModal()` — the same Phantom/Solflare chooser the console button opened. Only the second entry point is lost, not the deeplink path. **Exception:** the embedded-iframe **popup-blocked "Connect wallet" retry** stays on the cabinet plate (embedded-only, `isEmbedded && walletPopupBlocked`) — it rescues an in-flight bet and an embedded casino frame may hide our header. |
| 11 | **Edson is mounted and animates on mobile**, driven by the same status / typing-speed / own-bet watchers as desktop. **Payload:** the desktop eager preload of all 8 GIFs (**7.70 MB measured**) is not used; `monkeyIdle.gif` (661 KB) loads with the page and the other seven **prefetch after first paint**, never blocking it or contending with the live SignalR stream. Deliberate exception to `usePerfMode`'s GIF→still precedent (`HomeHero`'s `shipStill`) — the animations are the point of the card. |
| 12 | **While the visor input is focused, Edson is hidden and the terminal shrinks**, so the visor and the lever stay above the on-screen keyboard and the player can type and pull without dismissing it. Edson returns on blur. |
| 13 | **The tabs are unchanged** — `GAME \| CHAT` with today's filled-amber active state and unread dot. The mock's `>_ TERMINAL` / `💬 CHAT` relabel + restyle is declined. |
| 14 | **Layout of the two plates (resolved here).** **Free-text:** left plate = visor on top + `keyboard.png` below; right plate = lever. **Preset:** left plate = visor on top (read-only, showing the picked choice, exactly as desktop's `:disabled` input does) + the **4×2 grid of the 8 numbered keys in place of `keyboard.png`**; right plate = lever. The keys replace the keyboard rather than stacking under it: `keyboard.png` is decorative art, the visor is the readout, and a third stacked element would break decision 7's vertical budget. This keeps exactly two modules in both game types. |
| 15 | **The selected preset key gets a mobile-only lit state (resolved here).** The desktop key art has **no** selected state — its only feedback is `hover:brightness-200`, which never fires on touch. Acceptance criterion #16 requires "any lit preset key goes dark" on rollover, so a lit treatment must exist. Selection stays **derived from `userWord`** (never a local ref) and the lit class is applied **only on the mobile path**, so desktop stays pixel-identical (AC #18). |
| 16 | **Sideways / short viewports scroll (resolved shape).** A phone held sideways is still `< lg` but has ~336px of height; the three bands cannot fit, so the GAME view **scrolls** rather than crushing them. No separate arrangement, no rotate lock. Implemented as `overflow-y-auto` on the GAME band column (not on the page root, which stays `overflow-hidden` so the tab bar is fixed). |

### 3.1 Where the card's prose and its acceptance criteria disagree

The card's frontend-impact section says `GameConsole` "deletes `presetKeys`, `isKeySelected`,
`canPlaceBet`, `betButtonLabel`, `showMobileConnect`". Acceptance criterion #16 says "any **lit**
preset key goes dark". Both cannot be literally true.

**Resolution:** the deletion targets the *compact panel's named-key implementation* — `presetKeys`
(the uncapped, name-carrying list) goes, along with `canPlaceBet`, `betButtonLabel` and
`showMobileConnect`. The **derived-selection concept survives** as a mobile-only lit state over the
shared 8-slot `presetButtons` list. Decision 15 above is the binding form.

---

## 4. Data model

**None.** No entity, column, EF mapping, or migration. `presetChoices: string[]` already ships on
`GameParametersDto` and is already consumed by `selectedGame` in `useGame.ts`.

---

## 5. Configuration

**None.** No `GeneralSettings`, no env var, no seed data, no feature flag. (`VUE_APP_*` is this repo's
prefix — nothing new is introduced.)

---

## 6. Backend behaviour

**None.** No endpoint, DTO, handler, consumer, SignalR contract, or validation change. `useGame.bet()`
is untouched and the `POST /games/{id}/bets` body is byte-identical. No branch was created in
`monkify-backend`.

---

## 7. Frontend behaviour (`monkify-front`)

### 7.1 Ownership move — the console leaves the terminal

**Hidden blocker, confirmed in code.** `GameConsole` is rendered **inside** `GameTerminal`
(`GameTerminal.vue:517`, `<GameConsole :mobile="mobile" />`) on *both* layouts. The new mobile
layout puts **Edson between them**, so the terminal can no longer own the console on that path.

- `GameTerminal.vue:517` → `<GameConsole v-if="!mobile" :mobile="mobile" />`. The desktop path is
  unchanged (the console still sits inside the cabinet image, which is what positions it).
- `GamePage.vue` renders `<game-console mobile>` itself, as the third band.

**Second trap in the same area:** `GameTerminal`'s root is a bare
`<div :class="[showRiseIn ? 'animate-rise-in' : 'opacity-0']">` wrapper with **no height class of its
own** — today it inherits `h-full` from the `class="absolute inset-0 h-full"` GamePage passes down.
As a flex band it must become a flex item that can actually give its child a height: pass
`flex-1 min-h-[180px] flex flex-col` from GamePage and add `flex-1 min-h-0 flex flex-col` (or
equivalent) to that wrapper on the mobile path, or `#game-terminal-container`'s `h-full` resolves
against an auto-height parent and collapses.

### 7.2 `GamePage.vue` — the three-band GAME view

The mobile branch keeps its structure (page root `h-[calc(100dvh-64px)] overflow-hidden`, tabs at
`z-20`, the two views absolutely stacked with the hidden one `invisible pointer-events-none z-0` and
both kept mounted so chat keeps its subscription). What changes is the GAME view's *inside*:

```html
<!-- GAME view: three bands. overflow-y-auto so a viewport too short for them scrolls (AC #12)
     instead of crushing them — the page root stays overflow-hidden so the tabs never scroll away. -->
<div
  class="absolute inset-0 flex flex-col overflow-y-auto"
  :class="mobileTab === 'game' ? 'z-10' : 'invisible pointer-events-none z-0'"
>
  <game-terminal mobile class="flex flex-col flex-1 min-h-[180px]" />
  <game-monkey v-show="!betInputFocused" mobile class="flex-1 min-h-[80px] shrink" />
  <game-console mobile class="shrink-0" @focus-change="betInputFocused = $event" />
</div>
```

The band rules that make decision 7 true:

| Band | Sizing | Why |
| --- | --- | --- |
| Terminal | `flex-1 min-h-[180px]`, internal `overflow-auto` (already present on `#terminal-form`) | Guaranteed readable minimum; the stream scrolls inside it |
| Edson | `flex-1 min-h-[80px] shrink` | Takes the remainder and is the **first** to shrink (AC #13) |
| Console | `shrink-0`, height `clamp(130px, 21dvh, 190px)` | Never shrinks; the lever is never scrolled out of view (AC #1) |

The console height comes from the mock (≈155px on its 745px-tall screen ⇒ ≈21dvh), clamped so a very
tall phone doesn't blow it up and a very short one keeps the lever tappable. On an iPhone SE
(320×568 ⇒ ~460px of band space) the minimums sum to ~180 + 130 + 80 = 390px, so it fits without
scrolling; on a sideways phone (~336px) they don't, and the column scrolls — exactly AC #12.

New local state:

```ts
// Native keyboard open: hide Edson and let the terminal shrink so the visor and the lever stay
// above the keyboard (decision 12). GameConsole owns the input, so it reports focus up.
const betInputFocused = ref(false);
```

`v-show` (not `v-if`) on Edson so the GIF is not torn down and re-fetched on every focus/blur.

The **desktop branch is untouched**, including `<game-monkey class="flex-1" />` with no `mobile` prop.

### 7.3 `GameConsole.vue` — the portrait cabinet

The whole `v-if="mobile"` block (`:278-361`) is replaced. Everything below is mobile-only; the
desktop branch (`:364-433`) does not change.

#### 7.3.1 The plate art — `terminal.png` is a 3-plate sprite, not one plate

**Measured, not assumed.** `src/assets/game/terminal.png` is **970×420** and contains **three**
riveted light-grey plates side by side (each with a darker base strip), separated by dark gutters —
it is the whole desktop console face, which is why desktop uses it as one `bg-contain` background
behind three DOM columns.

For two portrait modules, each plate box shows **one** plate of that sprite. Plates 1 and 3 are
identical in shape (plate 2 is the taller lever well), so use those two:

```css
/* Each module is one well of the 3-up cabinet sprite. background-size: 300% 100% makes one plate
   exactly the box width; the x-position selects which. Flat colour blocks + four rivet squares, so
   the stretch to the box's aspect is invisible — do NOT aspect-lock the boxes to the sprite. */
.cabinet-plate {
  background-image: url('~@/assets/game/terminal.png');
  background-size: 300% 100%;
  background-repeat: no-repeat;
}
.cabinet-plate--left  { background-position: 0% 0; }   /* well 1 */
.cabinet-plate--right { background-position: 100% 0; } /* well 3 */
```

Widths from the mock: left ≈ **52%**, right ≈ **48%**, with a narrow dark gutter between them and both
flush to the bottom edge. Keep them as two boxes in a `flex` row so each scales independently.

> `visor1.png` is `import`ed at `GameConsole.vue:12` and **never used** — the desktop visor is built in
> CSS (`p-[8px] bg-[#92a1b9]` wrapper around a `bg-black` input, `:371-373`). Reuse that CSS idiom for
> the mobile visor; the dead import may be dropped.
> `glass.png` (`:426-430`) is desktop-only decoration over the empty keypad well on free-text games.
> The mobile left plate has no empty well, so it is **not** carried over.

#### 7.3.2 Left plate — visor + keyboard / keys

```
┌──────────────────────┐
│  ▓▓▓ visor ▓▓▓  ***  │   always: the black recessed input
│  ┌────────────────┐  │
│  │ keyboard.png   │  │   free-text games
│  │  — or —        │  │
│  │ [1][2][3][4]   │  │   preset games: the 8 button<N>.png keys, 4×2
│  │ [5][6][7][8]   │  │
│  └────────────────┘  │
└──────────────────────┘
```

- **Visor** — the same `<input id="word-type-input">` the desktop path uses: `v-model="userWord"`,
  `v-maska="currentMask"`, `:placeholder="placeholderText"`, `:disabled="isPresetGame"`,
  `:inputmode="inputMode"` (keep the existing `inputMode` computed — `numeric` for number games),
  `autocapitalize="off" autocomplete="off" spellcheck="false"`, `@keyup.enter="placeBet()"`, and
  `lowercase` + centred green mono styling. It is the **only** readout of the current pick in both
  game types.
  - ⚠️ **Two elements now carry `id="word-type-input"`** if both branches render — they do not (the
    branches are `v-if` / `v-else`), so the id stays unique. Keep it that way.
  - **No `.blink` class** — stripped under `html.light-graphics` (§2), so it would be dead code.
  - `@focus` / `@blur` set the existing local `isFocused` **and** `emit('focus-change', value)`.
- **Free-text** — `keyboard.png` below the visor, `w-full h-auto object-contain`, `draggable="false"`.
- **Preset** — `grid grid-cols-4 gap-1` of the **existing** `presetButtons` computed (8 slots,
  `disabled: index + 1 > presetChoices.length`), each an `<img :src="btn.img">` as desktop does.
  - **Disabled slots** are `opacity-50 cursor-not-allowed` and non-clickable (AC #6) — the current
    desktop class already expresses this.
  - **Selected slot** gets the mobile-only lit state (decision 15). Derived, never stored:
    ```ts
    // Mobile-only: the key art has no selected state and hover: never fires on touch. Derived from
    // userWord so it clears itself when placeBet() succeeds and when GamePage resets the round.
    const isKeySelected = (index: number) =>
      !!userWord.value && selectedGame.value?.presetChoices?.[index] === userWord.value;
    ```
    Treatment: `brightness-150` + a `ring-2 ring-primary` amber ring — the same active idiom as
    `GameMobileTabs.vue:17-21`, and **no** wide glow (light-graphics strips halos).
  - `@click` calls the **existing** `selectPresetButton(index)`, which already plays `buttonPress` and
    guards `if (!allowBets.value) return;`. No change to it.

#### 7.3.3 Right plate — the lever

```html
<button
  type="button"
  class="flex h-full min-h-[44px] w-full items-center justify-center"
  @click="gameInstance?.status === GameStatus.RewardForWinnersCompleted ? fetchNewGame() : placeBet()"
>
  <img :src="currentLever" draggable="false" class="h-full max-h-full w-auto object-contain" />
</button>
```

- Same `@click` expression as desktop (`:405-409`) — the lever doubles as "next round" (AC #5).
- Wrapped in a real `<button>` so the tap target is ≥ 44px (AC #14) and the control is focusable; the
  desktop `<img @click>` stays as-is.
- `leverDownAnimation` / `leverUpAnimation` / `leverError` are reused unchanged — `currentLever` is
  already shared by both branches.
- **Never dimmed, never disabled** (decision 8).

#### 7.3.4 Guards — nothing to change

`placeBet()` (`:101-151`) already refuses and calls `leverError()` for **every** case AC #4 lists,
before any animation runs:

| Case | Guard |
| --- | --- |
| Betting closed / self-excluded | `!allowBets` (`:74-79`, folds in `selfExcluded`) |
| Wallet required and absent | `walletGateBlocks` (`:83-90`) |
| Choice too short | `userWord.length < choiceRequiredLength` |
| Duplicate characters not accepted | `new Set(userWord).size !== userWord.length` |
| Free-text choice already taken | `gameInstance.bets.some(b => b.choice === …)` |
| Already bet this round | `betLimitBlocks` (`:95-99`) |
| Preset game, nothing selected | `userWord` empty ⇒ the length guard fires |

Do **not** add or relax a guard — this card only changes what the player taps.

#### 7.3.5 Deleted from `GameConsole.vue`

`presetKeys`, `canPlaceBet`, `betButtonLabel`, `showMobileConnect`, and the entire compact-panel
markup (`:278-361`), including its `> PICK YOUR CHOICE` / `> YOUR CHOICE` label and the
`CONNECT WALLET` button. `useMobileWallet`'s `isMobileDeeplinkContext` / `openWalletModal` imports go
with them **unless** `mobileConnected` is still needed by `walletGateBlocks` (`:87`) — it is, so keep
that one import.

**Kept:** `inputMode`, `presetButtons`, `selectPresetButton`, `placeBet`, `fetchNewGame`, the three
lever animators, `allowBets`, `walletGateBlocks`, `betLimitBlocks`, `isFreeBetActive`, `isPresetGame`,
`currentMask`, `placeholderText`, and the embedded popup-blocked retry button (rendered on the **left
plate**, `v-if="isEmbedded && walletPopupBlocked"`).

New emit:

```ts
const emit = defineEmits<{ (e: 'focus-change', focused: boolean): void }>();
```

### 7.4 `GameMonkey.vue` — Edson on mobile

1. **Render on mobile.** The visible `<img>`'s `hidden lg:block` (`:232`) becomes unconditional, with
   mobile sizing ahead of the existing desktop ladder: `h-full max-h-full w-auto object-contain` inside
   the band, then `lg:h-28 xl:h-72 2xl:h-[440px]` as today. The root's `px-6 lg:pr-16 pb-4` /
   `items-end` needs a mobile variant (`max-lg:px-2 max-lg:pb-1 max-lg:justify-center`) so the band
   isn't padded away when it shrinks.
2. **Do not eagerly preload 8 GIFs on mobile.** The hidden preload block (`:223-225`) is the
   **7.70 MB** payload (measured: `monkeyAddBet` 1.05 MB, `monkeyFinal` 2.96 MB, `monkeyGlasses`
   0.95 MB, `monkeyIdle` 0.68 MB, `monkeyRegen` 0.40 MB, `monkeyTyping` 0.55 MB, `monkeyTyping2`
   0.55 MB, `monkeyTyping3` 0.57 MB). Gate it and prefetch instead:

   ```html
   <!-- Desktop keeps the eager preload; on mobile it is 7 MB competing with first paint. -->
   <template v-if="!isMobile" v-for="anim in animations" :key="anim">
     <img :src="anim" alt="hidden img" class="hidden" />
   </template>
   ```

   ```ts
   // Mobile: monkeyIdle.gif ships with the page (it is the mounted frame); the other seven are
   // requested only after first paint, so they never block it or contend with the SignalR stream.
   onMounted(() => {
     if (!isMobile.value) return;
     const prefetch = () =>
       animations
         .filter((src) => src !== idle)
         .forEach((src) => {
           const img = new Image();
           img.src = src;
         });
     if (typeof window.requestIdleCallback === 'function') {
       window.requestIdleCallback(prefetch, { timeout: 4000 });
     } else {
       setTimeout(prefetch, 1500); // Safari < 17 has no requestIdleCallback
     }
   });
   ```

3. **Animations are unchanged.** The status watcher, the typing-speed watcher (1/2/3), the own-bet
   `enqueueBetAnimation` path and the `Ended` → `finale` → `regen` → `idle` chain all run as-is on
   mobile (AC #9). `.animate-wobble` is stripped by light-graphics on phones — expected, harmless; the
   GIF itself still animates. `.animate-fly-in` still runs.
4. **Hiding on focus** is done by the parent (`v-show` in `GamePage`), not inside this component.

### 7.5 `GameTerminal.vue`

- `:517` → `<GameConsole v-if="!mobile" :mobile="mobile" />` (§7.1).
- Root wrapper gets the flex-band treatment on the mobile path (§7.1).
- `#terminal-form`'s mobile classes (`w-full flex-1 min-h-0 mt-2 px-4 py-3`) stay; the **minimum
  height now lives on the band** (`min-h-[180px]` from `GamePage`), so the stream keeps a readable
  window and scrolls internally.
- `.mobile-terminal` styling, the `> POT:` line, the near-miss dock (`max-height: 40%`, AC #15), the
  `FREE BET MODE (DEV)` checkbox, `> CHOICES:`, `> BETS PLACED:`, `gameStatusLabel`, `betOutput` and
  the server-seed reveal are **unchanged**.

### 7.6 What must not change

- The desktop branch of every touched component, and `GameMobileTabs.vue` (AC #17), `GameSidePanel`,
  `GameChatPanel`, `useChatUnread`, `GameHeader`, `WalletButtons.vue`.
- `hooks/useIsMobile.ts` — still `(max-width: 1023px)`, no height query, no new breakpoint.
- `useGame.ts`, `useMobileWallet.ts`, `services/`, `types/`, `utils/walletDeepLink.ts` — no change.
- `src/assets/game/buttons/button1..8.png` — now used by **both** layouts.
- Any API call, SignalR subscription, or route.

---

## 8. Flow diagram

```mermaid
flowchart TD
  A[/game/play, viewport &lt; lg/] --> B[GamePage: GAME | CHAT tabs]
  B --> C[GAME view — flex column, overflow-y-auto]
  C --> D[Band 1: GameTerminal mobile<br/>flex-1 min-h-180px, scrolls internally]
  C --> E[Band 2: GameMonkey mobile<br/>flex-1 min-h-80px — shrinks first]
  C --> F[Band 3: GameConsole mobile<br/>shrink-0, clamp 130-190px]
  F --> G[Left plate: visor]
  G --> H{isPresetGame?}
  H -- no --> I[keyboard.png]
  H -- yes --> J[4x2 grid of button1..8.png<br/>disabled beyond N, lit = userWord]
  F --> K[Right plate: lever button, min 44px]
  K --> L{status === RewardForWinnersCompleted?}
  L -- yes --> M[fetchNewGame]
  L -- no --> N[placeBet]
  N --> O{guards pass?}
  O -- no --> P[leverError: twitch + error1.wav<br/>no bet, lever never dimmed]
  O -- yes --> Q[leverDownAnimation → useGame.bet → userWord cleared → leverUp]
  G -- focus --> R[emit focus-change true] --> S[Edson v-show false<br/>terminal band shrinks]
  G -- blur --> T[emit focus-change false] --> U[Edson returns]
```

---

## 9. Out of scope

- **Desktop (≥ 1024px)** — not touched, in any way.
- The **games lobby** (`/game`, game-type cards) — stays as shipped.
- A **sideways / short-viewport specific layout** — the view just scrolls; becomes its own card if it
  ever matters. Same for a short embedded frame.
- **Raising the 8 preset key cap** — belongs to the existing desktop-cap card, which now covers both
  layouts. A >8-choice game stays partly unplayable on mobile until then.
- The mock's **tab relabel/restyle** (`>_ TERMINAL` / `💬 CHAT`) — explicitly declined.
- Any **backend** work, including a maximum on `PresetChoices`.
- Adopting a frontend test framework.

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

**None** — the repo is not touched and no branch was created there.

### 10.2 Frontend (`monkify-front`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/components/game/GameConsole.vue` | Rewrite the `v-if="mobile"` branch as the portrait cabinet: two `terminal.png` plate modules (left visor + `keyboard.png` / 8-key 4×2 grid, right lever in a ≥44px `<button>`). Delete `presetKeys`, `canPlaceBet`, `betButtonLabel`, `showMobileConnect` and the compact panel. Add a mobile-only `isKeySelected(index)` lit state and a `focus-change` emit. Keep the embedded `walletPopupBlocked` retry on the left plate. **Desktop branch untouched.** |
| 2 | `src/components/game/GameMonkey.vue` | Drop `hidden lg:block`; add mobile sizing + root padding variants; gate the 8 eager preload `<img>`s to `!isMobile` and prefetch the other seven after first paint (`requestIdleCallback`, `setTimeout` fallback). Animation logic unchanged. |
| 3 | `src/pages/GamePage.vue` | Mobile GAME segment becomes a three-band flex column (`terminal` / `monkey` / `console`) with `overflow-y-auto`; owns `<game-console mobile>`; adds `betInputFocused` and `v-show`es Edson on it. Desktop branch untouched. |
| 4 | `src/components/game/GameTerminal.vue` | `<GameConsole v-if="!mobile">`; make the root wrapper a real flex band on the mobile path. Everything else unchanged. |
| 5 | `src/assets/styles/arcade.css` | **Only if needed** — a `html.light-graphics` override for a bespoke plate/lever surface. Not expected: the plates use a background image and flat borders, which light-graphics has nothing to strip. Plain CSS, never `@layer components`. |

No new components, assets, composables, types or routes.

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/mobile-game-design-update.md` | This spec. |
| 2 | `INDEX.md` | Spec row + "Feature currently in flight". |
| 3 | `features/mobile-layout.md` | *(at ship)* rewrite the "GAME view = full-width fluid terminal + compact bet panel", "The mobile preset keypad is named, uncapped and stateful" and "Monkey hidden on phones" bullets — they are now false. Add the three-band layout, the two-plate cabinet, the lever, the numbered 8-key cap, Edson + the deferred prefetch, and the header-only connect. |
| 4 | `specs/mobile-preset-keypad.md` | *(at ship)* add a superseded banner: its mobile half (named, uncapped keys, `PLACE BET` gating) is reverted by this card; its `arcade.css` specificity post-ship note still stands. |
| 5 | `conventions/frontend.md` | *(at ship)* update the "Responsive / mobile layout" bullet — the game screen's GAME segment is now a three-band column with the real cabinet, not a compact bet panel. |

---

## 11. Unit tests

**None.** `monkify-backend` is not touched (so no `SpaceStroker.Tests` change and, per `AGENTS.md`
rule 8 / `constraints.md` §3, **no integration test** — there is no backend surface to exercise), and
`monkify-front` has no test framework.

**Definition of done:** `yarn lint` and `yarn build` green, plus the §13 manual pass.

---

## 12. Acceptance criteria

Copied 1:1 from the Trello **Acceptance Criteria** checklist (#134).

1. Given a portrait phone viewport (< 1024px) on `/game/play`, when the GAME segment is active, then the terminal, Edson and the arcade cabinet console are all visible in that top-to-bottom order without scrolling the page.
2. Given a portrait phone, when the game screen renders, then the console is drawn on the `terminal.png` plate with the black visor input + `keyboard.png` on the left and the lever on the right, and the old compact gradient bet panel is not rendered anywhere in the app.
3. Given a free-text game on a phone, when the player types a valid choice into the visor and taps the lever, then the lever plays its down animation and the bet is submitted through the same `useGame.bet()` path desktop uses.
4. Given a phone in a state where no bet can be placed (betting closed, already bet this round, preset game with nothing selected, or self-excluded), when the player taps the lever, then the lever twitches and `error1.wav` plays, no bet is submitted, and the lever is never rendered dimmed or inert.
5. Given a phone at `GameStatus.RewardForWinnersCompleted`, when the player pulls the lever, then the next round is fetched (`fetchNewGame`) exactly as the desktop lever does, with no `NEXT ROUND` button label anywhere.
6. Given a preset-choice game with 8 or fewer choices on a phone, when the console renders, then one `button<N>.png` key is shown per configured choice inside the plate, unused keys are dimmed and non-clickable, and tapping a key sets that choice.
7. Given a preset-choice game configured with more than 8 choices on a phone, when the console renders, then keys 1–8 are shown and choices 9+ are not selectable — identical to today's desktop behaviour, and the keys carry numbers only (no choice names), with the terminal's `> CHOICES:` list as the mapping.
8. Given a phone, when the game screen first paints, then Edson is visible and animating on `monkeyIdle.gif`, and the other seven monkey GIFs have not been requested yet; they are prefetched afterwards without blocking first paint.
9. Given a phone, when the game status changes (`GameStarting`, `InProgress` with each typing speed, `Ended`) or the player's own bet lands, then Edson plays the same animation desktop plays for that event.
10. Given a free-text game on a phone, when the player focuses the visor and the native keyboard opens, then Edson is hidden and both the visor and the lever remain visible above the keyboard so the player can type and pull without dismissing it; when the input blurs, Edson returns.
11. Given a phone with no wallet session, when the player looks for a way to connect, then the console shows no `CONNECT WALLET` button and the header `CONNECT` button is the only affordance — and tapping it opens the Phantom/Solflare chooser (`MobileWalletModal`), leaving the existing deeplink bet flow working end to end.
12. Given the game embedded in a mobile iframe and a bet whose wallet popup was blocked, when the player looks at the console, then the "Connect wallet" retry button is visible on the cabinet plate and re-opens the wallet flow — the bet is never a silent dead end.
13. Given a viewport too short for the three bands (e.g. a phone held sideways, ~336px of height, or a short embedded frame), when the GAME segment is active, then the view scrolls rather than crushing the bands, and no separate short-viewport layout is introduced.
14. Given a small phone (320×568) in portrait, when the game screen renders, then Edson shrinks before the terminal or console do, the terminal still shows readable content, and the lever has a tap target of at least ~44px.
15. Given a race in progress on a phone, when the near-miss rows and the `> POT:` line are shown in the now-shorter terminal, then both stay legible and the docked `> N AWAY!` rows never cover the live character stream entirely.
16. Given a round rollover or a game change on a phone, when `GamePage` resets `userWord`, then the visor visibly clears and any lit preset key goes dark.
17. Given a phone, when the `GAME | CHAT` segmented tabs render, then they still read GAME and CHAT with today's filled-amber active state and unread dot, and switching to CHAT still clears the dot with both views staying mounted.
18. Given a viewport of 1024px or wider, when the game screen and the games lobby render, then the desktop layout, console, monkey and lobby cards are unchanged from before this card, and no backend, API, SignalR or database change was made.

---

## 13. Manual QA

Verification is manual — there is no frontend test suite, and no backend surface to integration-test.

1. Bring up the local stack (`monkify-backend`: `docker compose up -d --build`; API on `:6001`).
2. **Copy the hub's gitignored files into the worktree before serving** — a fresh worktree has neither:
   `cp monkify-front/.env.local monkify-front/yarn.lock .worktrees/mobile-game-design-update/monkify-front/`.
   Without `.env.local` every API call 404s against the dev server's own origin and the game reads as
   "unavailable". Serve on a spare port (`yarn serve --port 8081`) so it doesn't fight the hub.
3. **Screenshot-compare against the card's mock** at phone width before hand-off
   (`conventions/frontend.md` → Design workflow; the mock needs the user's Trello session, so open it
   with `claude-in-chrome`).
4. Portrait phone (390×844 and 320×568): verify AC 1, 2, 14 — three bands, no page scroll, Edson
   shrinking first on the SE, a 44px lever.
5. Free-text type: type a choice, pull the lever, confirm the down animation and the bet in
   `> BETS PLACED:` (AC 3). Focus the visor on a **real device** and confirm Edson hides and the lever
   stays above the keyboard (AC 10) — emulation does not raise a keyboard.
6. Pull the lever with betting closed, then again after already betting this round: twitch + error
   sound, never dimmed (AC 4). Let a round finish and pull for the next one (AC 5).
7. Preset type (`6X Main Derby`): confirm numbered keys inside the plate, dimmed unused slots, the lit
   selected key, and the visor showing the pick (AC 6). Create a **12-choice** type in the admin portal
   and confirm keys 1–8 only (AC 7). Let the round roll over and confirm the visor clears and the key
   goes dark (AC 16).
8. Reload with DevTools **Network** open: `monkeyIdle.gif` in the initial waterfall, the other seven
   only after load (AC 8). Watch a full round for the status/typing-speed/own-bet animations (AC 9).
9. Race in progress: near-miss rows + `> POT:` legible in the shorter terminal (AC 15).
10. Rotate to landscape (~336px tall): the view scrolls, nothing is crushed (AC 13).
11. Switch to CHAT and back: amber active state, unread dot, both views still mounted (AC 17).
12. ⚠️ **AC 11 needs a real mobile user-agent** — `isMobileDeeplinkContext()` calls `isMobileBrowser()`
    (`utils/walletDeepLink.ts`), which DevTools device emulation does not satisfy. Verify the header
    `CONNECT` → Phantom/Solflare chooser on a real phone, as `specs/mobile-preset-keypad.md` §16 had to.
13. AC 12 needs the embedded example page (`public/embed-example.html`) in a mobile-sized frame with
    the wallet popup blocked.
14. Resize past `lg`: desktop console, monkey and lobby pixel-identical (AC 18).
15. `yarn lint` + `yarn build` green.

---

## 14. Trello / pipeline

- **Source card:** [#134 mobile game design update](https://trello.com/c/yPwW5Awx/134-mobile-game-design-update)
  — list **Specs created**; the **Implementation** section on the card carries branch, worktree, spec path.
- **Next:** `/monkify-implement-feature mobile game design update`
- **Then:** `/monkify-ship-feature mobile game design update` (syncs the KB, merges to `main`).

### Concurrency note

`monkify-front` has another worktree in flight — `feature/wallet-autoconnect-mobile-only` at
`.worktrees/wallet-autoconnect-mobile-only/monkify-front`. It targets the wallet-connect path
(`WalletButtons.vue` / `useMobileWallet.ts`), which this card only **reads** (decision 10). No file
overlap is expected, but rebase order matters if both land close together.

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-08-02 | Initial spec from refined card #134, with the card's mock read in-browser and the console/monkey/page/terminal code paths verified. Decisions 14–16 resolved here (preset-key placement, the mobile-only lit key state, the scroll shape); §3.1 records the card-prose ↔ AC #16 conflict and its resolution. |
| 2026-08-02 | **Shipped.** Added §16 (shipped deviations): the tab selected-state dropped amber for the lit-screen treatment (amends **AC #17**); desktop is no longer untouched (amends **AC #18** and §9) because the chat composer moved onto `--ctl-h-sm` — **overriding `control-sizing-system` D5** — and `.animate-wobble` left the light-graphics kill list; and the screen panel was re-skinned to true-black glass in a 7px `#5c6a8c` housing, with the cabinet overlapping Edson on a measured tuck. §16.4 records which acceptance criteria were actually exercised and which were not. |

---

## 16. Shipped deviations (2026-08-02)

Three owner decisions landed **after** this spec was written. They are recorded here rather than
edited into the body above, so the spec still reads as the design that was approved and this section
carries what actually shipped. **Where the two disagree, the code is authoritative.**

### 16.1 The tab selected-state is no longer amber (amends AC #17)

The card said the `GAME | CHAT` tabs were **unchanged**, keeping "today's filled-amber active state",
and **AC #17** asserts it. Shipped instead: **both tabs sit on the same true black as the glass**, and
the selected one is painted as the screen it opens onto — `#4ade80` phosphor over a crisp 2px bar.

Why it changed: once the screen panel went true black (§16.3), the amber chip read as a brand sticker
stapled to a CRT. An intermediate pass gave the *unselected* tab a `#2a3149` key plate; that read as a
stray grey slab bolted to a black machine and was dropped — the plate was doing a job the type should
do.

Two rules worth keeping:

- **The 2px bar is not decoration.** With no fill, selection would otherwise be carried by hue alone,
  which fails WCAG 1.4.1. The bar is the non-colour cue. It is an `inset` box-shadow rather than a
  border or pseudo-element, so switching tabs moves no pixels.
- **No soft bloom.** `usePerfMode` forces `html.light-graphics` on every viewport below `lg`, and this
  component only renders below `lg` — a wide glow here would be built specifically to be stripped on
  the one device that always sees it. Only the tight `0 0 6px` halo survives, the same idiom
  `.pot-value` already uses on this screen.

Measured in a live browser: selected **12.05:1** on black, unselected **4.77:1**, both tabs 34px.

### 16.2 Desktop is no longer untouched (amends AC #18 and §9)

§9 lists "Desktop (≥ 1024px) — not touched, in any way" and **AC #18** asserts it. Two shipped changes
reach desktop:

- **`GameChatPanel`'s composer moved onto `--ctl-h-sm`.** The input and SEND sized themselves from font
  and padding alone and landed 6.6px apart (33.6px VT323/16px/`py-1` beside 27px Press Start/10px/
  `py-1.5`) — two recipes with no reason to agree. Both now take height from the token and carry
  horizontal padding only. ⚠️ **This deliberately overrides `specs/control-sizing-system.md` decision
  D5**, which had listed the chat composer as out of scope; D5's other exclusions (terminal word input,
  lever, mobile preset keypad, `GameMobileTabs`) still stand. Desktop moves 33.6px → 34.4px.
- **`.animate-wobble` left the `html.light-graphics` kill list** in `arcade.css`. Every phone runs that
  mode, so stripping it froze Edson on the one screen he is the mascot of. Light-graphics also engages
  on a **desktop with a software rasterizer**, so the wobble returns there too. It is the cheapest
  entry in that list to keep: one `<img>`, transform-only, 8s loop.

`GameHeader`'s change is `max-lg:`-scoped and is genuinely mobile-only, so it does not deviate.

### 16.3 The screen panel was re-skinned to true-black glass

This spec was written against the shipped `.mobile-terminal` treatment (`#0b0d18` fill, 2px `#92a1b9`
bezel, 8px radius) and said it "already matches the mock". It shipped as a CRT monitor instead: **true
black glass, a 7px `#5c6a8c` bezel, 4px corners**, shared by the tab strip, the terminal and the chat
panel so all three read as one housing.

⚠️ **Keep those values in lockstep across the three components** — switching tabs must not resize or
recolour the housing, only what is on the screen.

The bezel tone is picked for **contrast, not tone**. An intermediate pass took it to the console
wrapper's `#424c6e` navy; against black glass on one side and the dark page on the other it nearly
vanished, so a 6px bezel read *thinner* than the old 4px one. Thickness only reads when there is
enough contrast to see it.

Also shipped beyond the spec, and worth carrying:

- **The cabinet overlaps Edson** (`-mt-[clamp(16px,3.6dvh,34px)]`, `z-20`, two upward shadow casts).
  The tuck depth was measured off `monkeyIdle.gif`'s alpha + brightness profile: the hull is bright out
  to ~63-82% of frame width, then falls into a near-black underside. A shallower tuck cuts that dark
  tail, so it reads as the saucer fading out rather than going behind something, and the shadow has no
  lit surface to land on. The clamp is required — a fixed 34px would swallow half of the 80px Edson
  band a short viewport leaves.
- **The console's sprite crop moved to y=20** (`background-position-y: 15.625%`, was 9.375%/y=12). The
  old crop kept a hairline of the plate art's navy top border, which read as a seam under Edson.
- **The lever is shrunk with padding on the button, not by insetting it** — padding is inside the hit
  area, so the whole plate stays tappable. Verified: all four plate corners hit the button.
- **The visor input is floored at 16px.** `public/index.html` ships the viewport with no
  `maximum-scale`, so iOS Safari zooms the page on focus below 16px — which would fight this screen's
  own focus handling. Trimmed with leading (30→28px) and tracking (0.3→0.26em) instead.
- **The rise-in entry moved from the terminal to Edson on mobile** (`monkey-rise-in`, 40vh, scoped to
  `GameMonkey`). Desktop keeps the terminal's `animate-rise-in` and Edson's `animate-fly-in`.

### 16.4 Verification is partial — recorded, not claimed

Checked in a live browser at 430×932 and 360×640 against the running local stack: band order with no
page scroll, console composition, short-viewport scrolling, Edson shrinking first, the lever tap
target, tab contrast and heights, composer alignment, and the entry animation captured frame by frame.

**Not exercised** — they need a real round and a connected wallet: betting through the lever (AC #3),
the invalid-pull twitch (AC #4), next-round pull (AC #5), the preset keypad (AC #6, #7), the
per-status Edson animations (AC #9), native-keyboard focus behaviour (AC #10), wallet connect and the
deeplink path (AC #11, #12), near-miss rows during a live race (AC #15), and round-rollover clearing
(AC #16).

The card's 18 acceptance criteria were **left unchecked** at ship rather than marked on unverified
work. Anyone picking this up should exercise the list above on a real device before treating the card
as fully met.

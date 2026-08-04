# Hide Orbit behind a frontend env flag — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add one build-time frontend flag, `VUE_APP_ORBIT_ENABLED`, that hides every Orbit surface **and** silences every Orbit runtime path, so a production build can ship with Orbit dark while local and the GCP development deploy keep it on.

**Architecture:** A single fail-closed constant `orbitEnabled` in `src/constants/featureFlags.ts` (`process.env.VUE_APP_ORBIT_ENABLED === 'true'`), consumed at two layers: the **mount points** in `GameHeader.vue` / `GamePage.vue` (so nothing renders or starts) and **inside the composables themselves** (`useOrbitNotifications`, `useOrbitRedirect`, `useFirstWinUpsell`) so a future caller cannot reintroduce Orbit traffic by forgetting a `v-if`. No Orbit code is deleted or lazy-loaded — it stays compiled in and simply never runs.

**Tech Stack:** Vue 3.2 + TypeScript 5.5, Vue CLI 5 (webpack), Tailwind 3, Yarn. `monkify-backend` gets one line in a Windows `.bat` script (no C#). `monkify-meta` gets one JSON key + docs.

**Spec:** `monkify-meta/specs/orbit-frontend-flag.md` (authoritative — read §7 before starting).
**Trello:** [#146](https://trello.com/c/zJsoFVaW) · **Branch:** `feature/orbit-frontend-flag`
**Worktrees:** `.worktrees/orbit-frontend-flag/{monkify-front,monkify-backend,monkify-meta}`

## Global Constraints

- **⚠️ There is no test runner in `monkify-front`.** No Jest, no Vitest, no `vue-tsc`. Classic red-green TDD is **not available** for this feature, and no task below pretends otherwise. Each task's verification is a **runnable command** (`grep`/`yarn lint`/`yarn build`) plus the browser QA in Task 8. Do not add a test framework — that is out of scope for this card.
- **Work only inside `.worktrees/orbit-frontend-flag/<repo>`.** The shared checkouts (`monkify/<repo>`) are permanent `main` hubs — never `git checkout`/`git switch` there.
- **Fail-closed is `=== 'true'`, exactly.** Not `!== 'false'`. Do **not** "make it consistent" with `allowFreeBets` on the line above — the asymmetry is the feature.
- **`allowFreeBets` is not changed.** One line, one flag added; nothing existing is edited in `featureFlags.ts`.
- **Never edit `src/hooks/useGame.ts:335`** or the `if (didBet && endedGameId && …)` block around it. That block also plays the victory audio and posts the embedded `bet-settled` message. The upsell is gated **inside `maybeOpen()`**.
- **Every existing `!isEmbedded` test survives.** The new flag **stacks on top** (`orbitEnabled && !isEmbedded`); it never replaces an embed guard.
- **Do not gate `components/admin/GameTypeForm.vue`.** The `Allow Orbit auto-play` checkbox stays visible and functional — deliberate (AC 8).
- **Do not touch** `services/orbitService.ts`, `types/orbit.ts`, `types/index.ts`, `components/orbit/*`, `components/OrbitBalance.vue`, `hooks/useOrbit.ts`, `hooks/useGameHub.ts`.
- **Line endings: CRLF.** The repo lints under `autocrlf=true`. Never rewrite a whole file with LF — use surgical edits. If `yarn lint` reports `Insert ␍`, fix with `unix2dos <file>` and verify with `od -c <file> | head`.
- **Mock/placeholder values only** in committed env/secrets files. `deploy/secrets.example.json` keeps every `REPLACE_ME` untouched.
- **No C# changes.** No entity, migration, handler, consumer, endpoint. No unit test changes (`SpaceStroker.Tests` is untouched). **Integration test waived** — this is a gating/QOL change with no backend surface.

## File Structure

| Repo | File | Responsibility |
| --- | --- | --- |
| front | `src/constants/featureFlags.ts` | **The flag.** Sole source of `orbitEnabled`. Everything else imports it. |
| front | `src/hooks/useOrbitNotifications.ts` | Runtime guard — hub channel + both toasts |
| front | `src/hooks/useOrbitRedirect.ts` | Runtime guard — cross-round poller (composable side) |
| front | `src/pages/GamePage.vue` | Runtime guard — cross-round poller (call-site side) |
| front | `src/hooks/useFirstWinUpsell.ts` | Runtime guard — first-win modal (⚠️ inside `maybeOpen`) |
| front | `src/components/GameHeader.vue` | UI mount points — balance row, ☰ row, both modals, wallet watcher, notifications start |
| front | `src/hooks/useGame.ts` | Marker source — `myBetThisRoundIsOrbit` (covers 4 strings) |
| front | `src/components/game/GameTerminal.vue` | Marker — `YOU · ORBIT` row label |
| front | `src/pages/GameHistoryPage.vue` | Marker — two `ORBIT` chips |
| front | `.env.example` | Documents the flag for developers |
| backend | `scripts/setup_local_environment.bat` | Writes `=true` into the local Docker stack's env |
| meta | `deploy/secrets.example.json` | `frontBuild` template gains the key at `"false"` |

**Task order is load-bearing** (spec §12.1): the flag first (everything imports it), then the **runtime** guards (what AC 2/3/5/6 actually test and what a reviewer cannot see by looking at the page), then the UI, then the markers, then env plumbing, then the gates.

---

### Task 1: The flag + its documentation

**Files:**
- Modify: `monkify-front/src/constants/featureFlags.ts` (append; file is currently 2 lines)
- Modify: `monkify-front/.env.example` (append after the `VUE_APP_ALLOW_FREE_BETS` block)

**Interfaces:**
- Consumes: nothing.
- Produces: `export const orbitEnabled: boolean` from `@/constants/featureFlags`. **Every** later task imports exactly this name from exactly this path.

- [ ] **Step 1: Read the file as it stands**

Run: `cat .worktrees/orbit-frontend-flag/monkify-front/src/constants/featureFlags.ts`

Expected, exactly two lines:

```ts
// Enabled by default; set VUE_APP_ALLOW_FREE_BETS=false in production to hide the toggle.
export const allowFreeBets = process.env.VUE_APP_ALLOW_FREE_BETS !== 'false';
```

If it differs, stop — someone else has edited this file and the plan needs rechecking.

- [ ] **Step 2: Append the flag**

Append to `src/constants/featureFlags.ts` (leave the two existing lines untouched, one blank line between):

```ts

/**
 * Orbit (#121/#122/#123) is on `main` but is NOT production-deliverable yet. This hides every Orbit
 * surface AND silences every Orbit runtime path — no request, no hub group, no poller — so a
 * production build can ship with Orbit dark while local and the development deploy keep it on.
 *
 * ⚠️ FAIL-CLOSED, and deliberately the opposite test from `allowFreeBets` above: a build that omits
 * the key ships Orbit HIDDEN, because an un-configured production build must never leak a feature we
 * have called not production-ready. The cost is that a developer whose (git-ignored) .env.local
 * predates this line loses Orbit locally until they add it.
 *
 * Build-time only, by design — no `?orbit=1`, no localStorage override: anything in the bundle is
 * discoverable, and an escape hatch would let a stranger enrol real money into an untested feature.
 */
export const orbitEnabled = process.env.VUE_APP_ORBIT_ENABLED === 'true';
```

- [ ] **Step 3: Verify the fail direction is right**

Run: `grep -n "VUE_APP_ORBIT_ENABLED\|VUE_APP_ALLOW_FREE_BETS" .worktrees/orbit-frontend-flag/monkify-front/src/constants/featureFlags.ts`

Expected — the two tests must differ:

```
2:export const allowFreeBets = process.env.VUE_APP_ALLOW_FREE_BETS !== 'false';
17:export const orbitEnabled = process.env.VUE_APP_ORBIT_ENABLED === 'true';
```

If `orbitEnabled` reads `!== 'false'`, it is wrong — that ships Orbit into an un-configured production build, which is the exact failure this card exists to prevent.

- [ ] **Step 4: Document it in `.env.example`**

Append after the existing free-bet block (`# Free bet mode toggle — enabled by default; production: false` / `VUE_APP_ALLOW_FREE_BETS=true`):

```
# Orbit (custodial wallet + auto-bet + notifications). FAIL-CLOSED: only the exact string "true"
# enables it — unset, empty or anything else hides every Orbit surface AND silences every Orbit
# runtime path (no request, no hub group, no post-round poller). Note this is the OPPOSITE test from
# VUE_APP_ALLOW_FREE_BETS above, which is fail-open.
# production: false (Orbit is not production-deliverable yet); development deploy: true
VUE_APP_ORBIT_ENABLED=true
```

- [ ] **Step 5: Commit**

```bash
cd .worktrees/orbit-frontend-flag/monkify-front
git add src/constants/featureFlags.ts .env.example
git commit -m "Add the fail-closed orbitEnabled build flag."
```

---

### Task 2: Runtime guard — the notification hub channel

Kills the `JoinOrbitNotifications` group join, the Orbit win toast, and the "paused — insufficient funds" toast. This is also what makes **AC 3** hold after an idle disconnect **without touching `useGameHub.ts`**: `joinOrbitChannel` is the only caller that ever sets `useGameHub`'s module-level `orbitSessionToken`, and `built.onreconnected` only invokes `JoinOrbitNotifications` `if (orbitSessionToken)`.

**Files:**
- Modify: `monkify-front/src/hooks/useOrbitNotifications.ts` (import + line 92)

**Interfaces:**
- Consumes: `orbitEnabled` from `@/constants/featureFlags` (Task 1).
- Produces: nothing new — `useOrbitNotifications().start()` keeps its `void` signature and stays idempotent.

- [ ] **Step 1: Add the import**

In `src/hooks/useOrbitNotifications.ts`, the import block currently ends at `import { useOrbit } from './useOrbit';` (line 7). Add above the relative imports:

```ts
import { orbitEnabled } from '@/constants/featureFlags';
```

- [ ] **Step 2: Extend the existing first-line guard**

Find (line ~92, inside `const start = () => {`):

```ts
    if (started || isEmbedded) return;
```

Replace with:

```ts
    // ⚠️ orbitEnabled FIRST and it is load-bearing beyond this file: joinOrbitChannel below is the
    // only thing that ever sets useGameHub's module-level orbitSessionToken, and that token is the
    // sole condition on the JoinOrbitNotifications invoke in its onreconnected handler. Refusing to
    // start here is therefore what keeps the RECONNECT path clean after an idle disconnect too —
    // which is why useGameHub.ts needs no flag of its own.
    if (!orbitEnabled || started || isEmbedded) return;
```

- [ ] **Step 3: Verify the guard is first and `watchPause` is behind it**

Run: `sed -n '85,115p' .worktrees/orbit-frontend-flag/monkify-front/src/hooks/useOrbitNotifications.ts`

Expected: `!orbitEnabled` is the **first** condition on the `return` line, and both `watch(...)` (the join/leave watcher) and `watchPause()` sit **after** it — so neither runs when the flag is off.

- [ ] **Step 4: Verify `useGameHub.ts` was NOT edited**

Run: `git -C .worktrees/orbit-frontend-flag/monkify-front diff --name-only`

Expected: `src/hooks/useOrbitNotifications.ts` only. `src/hooks/useGameHub.ts` must **not** appear — it needs no change (see Step 2's comment).

- [ ] **Step 5: Commit**

```bash
cd .worktrees/orbit-frontend-flag/monkify-front
git add src/hooks/useOrbitNotifications.ts
git commit -m "Refuse the Orbit notification channel when the flag is off."
```

---

### Task 3: Runtime guard — the cross-round poller

Today, when a round ends, `useOrbitRedirect` polls `GET /games/{id}` every 2 s for up to 45 s looking for an Orbit-funded bet. Gated at **both** ends (AC 5).

**Files:**
- Modify: `monkify-front/src/hooks/useOrbitRedirect.ts` (import + first line of the factory)
- Modify: `monkify-front/src/pages/GamePage.vue` (import + line ~30)

**Interfaces:**
- Consumes: `orbitEnabled` from `@/constants/featureFlags` (Task 1).
- Produces: nothing — `useOrbitRedirect()` keeps returning `void`.

- [ ] **Step 1: Guard the composable itself**

In `src/hooks/useOrbitRedirect.ts`, add to the imports (the block ends with `import { useOrbit } from './useOrbit';`):

```ts
import { orbitEnabled } from '@/constants/featureFlags';
```

Then find (line ~33):

```ts
export const useOrbitRedirect = () => {
  const { gameInstance, selectedGame, gameTypes, updateNewGame, currentPlayerId } = useGame();
```

Replace with:

```ts
export const useOrbitRedirect = () => {
  // Before ANY destructuring: with Orbit dark there is no target type to follow, so the watcher, the
  // 2s/45s poll and the onUnmounted cancel are all dead weight. Guarding here as well as at the
  // GamePage call site means a future caller cannot restart the poll by forgetting the `if`.
  if (!orbitEnabled) return;

  const { gameInstance, selectedGame, gameTypes, updateNewGame, currentPlayerId } = useGame();
```

- [ ] **Step 2: Guard the call site**

In `src/pages/GamePage.vue`, add to the imports after `import { useOrbitRedirect } from '@/hooks/useOrbitRedirect';`:

```ts
  import { orbitEnabled } from '@/constants/featureFlags';
```

Then find (line ~28–30):

```ts
  // Watches for an Orbit bet placed on the player's behalf elsewhere once this round ends, and moves
  // them to it. Page-scoped on purpose: it cancels itself the moment they leave.
  useOrbitRedirect();
```

Replace with:

```ts
  // Watches for an Orbit bet placed on the player's behalf elsewhere once this round ends, and moves
  // them to it. Page-scoped on purpose: it cancels itself the moment they leave. Skipped entirely
  // when Orbit is dark — the composable refuses too, this is the visible half of that pair.
  if (orbitEnabled) useOrbitRedirect();
```

- [ ] **Step 3: Verify the composable's guard precedes every hook call**

Run: `sed -n '33,45p' .worktrees/orbit-frontend-flag/monkify-front/src/hooks/useOrbitRedirect.ts`

Expected: `if (!orbitEnabled) return;` appears **before** the `useGame()` / `useOrbit()` / `useRouter()` destructuring. If it sits after them, `useRouter()` still runs — harmless, but the point of the guard is that nothing at all executes.

- [ ] **Step 4: Verify both ends are gated**

Run: `grep -n "orbitEnabled" .worktrees/orbit-frontend-flag/monkify-front/src/hooks/useOrbitRedirect.ts .worktrees/orbit-frontend-flag/monkify-front/src/pages/GamePage.vue`

Expected: 2 hits in `useOrbitRedirect.ts` (import + guard) and 2 in `GamePage.vue` (import + `if (orbitEnabled)`).

- [ ] **Step 5: Commit**

```bash
cd .worktrees/orbit-frontend-flag/monkify-front
git add src/hooks/useOrbitRedirect.ts src/pages/GamePage.vue
git commit -m "Skip the cross-round Orbit poller when the flag is off."
```

---

### Task 4: Runtime guard — the first-win upsell ⚠️ THE DANGEROUS ONE

`useFirstWinUpsell().maybeOpen()` is called from **inside `useGame`'s victory one-shot** (`src/hooks/useGame.ts:335`), the same `if` block that plays the victory/lose audio and posts the embedded `bet-settled` message. **Gate inside `maybeOpen()`. Do not touch `useGame.ts`.** (AC 6.)

**Files:**
- Modify: `monkify-front/src/hooks/useFirstWinUpsell.ts` (import + first line of `maybeOpen`)
- **Must NOT modify:** `monkify-front/src/hooks/useGame.ts` — Task 6 edits it for a different reason; **line 335 and its enclosing `if` stay byte-identical in both tasks.**

**Interfaces:**
- Consumes: `orbitEnabled` from `@/constants/featureFlags` (Task 1).
- Produces: nothing — `maybeOpen()` keeps its `void` signature and its silent-refusal contract.

- [ ] **Step 1: Add the import**

In `src/hooks/useFirstWinUpsell.ts`, the import block ends with `import { useHeaderModals } from './useHeaderModals';` (line 7). Add:

```ts
import { orbitEnabled } from '@/constants/featureFlags';
```

- [ ] **Step 2: Make the flag the first refusal**

Find (line ~63):

```ts
  const maybeOpen = () => {
    if (seen.value) return; // once, ever
```

Replace with:

```ts
  const maybeOpen = () => {
    // ⚠️ Gated HERE, never at the call site. useGame's victory one-shot invokes this from inside the
    // same block that plays the victory audio and posts the embedded `bet-settled` message — gating
    // that block would cost a flag-off build both. Every refusal in this function is silent by
    // design, so this one needs no special handling.
    //
    // Ahead of `seen` on purpose: `seen` is never PERSISTED on a flag-off build, so a player who wins
    // while Orbit is dark still gets the invitation the first time they win after Orbit ships.
    if (!orbitEnabled) return;
    if (seen.value) return; // once, ever
```

- [ ] **Step 3: Prove `useGame.ts` is untouched**

Run: `git -C .worktrees/orbit-frontend-flag/monkify-front status --short`

Expected: `src/hooks/useGame.ts` does **not** appear. (Task 6 will change it — for the marker computed only, never for this.)

- [ ] **Step 4: Prove the victory one-shot still calls `maybeOpen` unconditionally**

Run: `grep -n "maybeOpen\|playAudio(won\|postToParent('bet-settled'" .worktrees/orbit-frontend-flag/monkify-front/src/hooks/useGame.ts`

Expected — all three still present, the `maybeOpen()` call still guarded only by `won && ownBetWasPaid.value`:

```
335:          if (won && ownBetWasPaid.value) useFirstWinUpsell().maybeOpen();
```

Plus the `playAudio(won ? victoryAudio : loseAudio)` line above it and the `postToParent('bet-settled', {` below it. If any of those moved or gained an `orbitEnabled` condition, revert and redo — that is the AC 6 regression.

- [ ] **Step 5: Commit**

```bash
cd .worktrees/orbit-frontend-flag/monkify-front
git add src/hooks/useFirstWinUpsell.ts
git commit -m "Refuse the Orbit first-win upsell inside maybeOpen when the flag is off."
```

---

### Task 5: The UI mount points in the header

Six sites in one file. All existing `!isEmbedded` tests survive — the flag stacks on top.

**Files:**
- Modify: `monkify-front/src/components/GameHeader.vue`

**Interfaces:**
- Consumes: `orbitEnabled` from `@/constants/featureFlags` (Task 1). In `<script setup>`, an imported const is automatically available to the template — no `defineExpose`, no wrapper computed.
- Produces: nothing.

- [ ] **Step 1: Add the import**

In `src/components/GameHeader.vue`, after `import { setMuted, setMasterVolume } from '@/constants/helpers';` (line ~21):

```ts
  import { orbitEnabled } from '@/constants/featureFlags';
```

- [ ] **Step 2: Guard the wallet-change watcher body**

Find (line ~74–81):

```ts
  watch(
    effectiveWallet,
    (wallet) => {
      syncWallet(wallet ?? null);
      if (wallet) void loadIfSignedIn(wallet);
    },
    { immediate: true }
  );
```

Replace with:

```ts
  watch(
    effectiveWallet,
    (wallet) => {
      // Orbit dark ⇒ no state to re-scope and nothing to read: loadIfSignedIn would fire GET /orbit
      // for anyone holding a stored session. Guarded inside rather than by deleting the watcher, so
      // the syncWallet comment above stays attached to the code it explains.
      if (!orbitEnabled) return;

      syncWallet(wallet ?? null);
      if (wallet) void loadIfSignedIn(wallet);
    },
    { immediate: true }
  );
```

- [ ] **Step 3: Guard the notification channel start**

Find (line ~90):

```ts
  useOrbitNotifications().start();
```

Replace with:

```ts
  if (orbitEnabled) useOrbitNotifications().start();
```

(`start()` refuses on its own too — Task 2. Both, deliberately.)

- [ ] **Step 4: Guard the ORBIT balance row**

Find (line ~255–260):

```html
          <OrbitBalance
            v-if="!isEmbedded && effectiveWallet"
```

Replace the `v-if` with:

```html
          <OrbitBalance
            v-if="orbitEnabled && !isEmbedded && effectiveWallet"
```

- [ ] **Step 5: Guard the ☰ menu Orbit row**

Find (line ~356–362), inside the `<template v-if="!isEmbedded">` that also holds Stats and Fair:

```html
            <button
              type="button"
              class="flex w-full items-center gap-2 rounded-sm px-3 py-2 vt323-regular text-[16px] text-terminal-light hover:bg-primary/10 hover:text-primary"
              @click="openOrbit"
            >
              <span>Orbit</span>
            </button>
```

Replace with the same button carrying its own `v-if`:

```html
            <button
              v-if="orbitEnabled"
              type="button"
              class="flex w-full items-center gap-2 rounded-sm px-3 py-2 vt323-regular text-[16px] text-terminal-light hover:bg-primary/10 hover:text-primary"
              @click="openOrbit"
            >
              <span>Orbit</span>
            </button>
```

⚠️ **Do not** move the flag onto the enclosing `<template v-if="!isEmbedded">` — that template also guards **Stats** and **Fair**, which must keep rendering (AC 4).

- [ ] **Step 6: Guard both modals**

Find (line ~420):

```html
    <OrbitModal :open="isOrbitOpen" :wallet="effectiveWallet" @close="isOrbitOpen = false" />
```

Replace with:

```html
    <OrbitModal
      v-if="orbitEnabled"
      :open="isOrbitOpen"
      :wallet="effectiveWallet"
      @close="isOrbitOpen = false"
    />
```

Find (line ~425–430):

```html
    <OrbitFirstWinModal
      v-if="!isEmbedded"
```

Replace the `v-if` with:

```html
    <OrbitFirstWinModal
      v-if="orbitEnabled && !isEmbedded"
```

- [ ] **Step 7: Verify all six sites and that no embed guard was lost**

Run: `grep -n "orbitEnabled\|isEmbedded" .worktrees/orbit-frontend-flag/monkify-front/src/components/GameHeader.vue`

Expected: **7** `orbitEnabled` hits (1 import + 6 sites), and every pre-existing `isEmbedded` occurrence still present — `gameHomeRoute`, `<OrbitBalance>`, the Stats/Fair `<template>`, `<OrbitFirstWinModal>`, `<GameFairModal>`, the Responsible Gaming link, `const { isEmbedded } = useEmbed();`.

- [ ] **Step 8: Commit**

```bash
cd .worktrees/orbit-frontend-flag/monkify-front
git add src/components/GameHeader.vue
git commit -m "Hide the header's Orbit surfaces when the flag is off."
```

---

### Task 6: The owner-only ORBIT markers

Three independent render paths reading the backend's owner-only `viaOrbit` / `myBetViaOrbit`. Gated so the word can never surface from legacy data (AC 7).

**Files:**
- Modify: `monkify-front/src/hooks/useGame.ts` (import already present at line 10 — extend it; guard the computed at line ~198)
- Modify: `monkify-front/src/components/game/GameTerminal.vue` (import at line 20 — extend it; `betPlayerLabel` at line ~77)
- Modify: `monkify-front/src/pages/GameHistoryPage.vue` (new import; two `v-if`s at ~218 and ~263)
- **Not modified:** `monkify-front/src/components/game/GameConsole.vue` — it destructures `myBetThisRoundIsOrbit` straight from `useGame()` at line 60, so gating the computed covers both of its render sites with no edit.

**Interfaces:**
- Consumes: `orbitEnabled` from `@/constants/featureFlags` (Task 1).
- Produces: `myBetThisRoundIsOrbit` keeps its exact existing type, `ComputedRef<boolean>`. It must return **`false`** (never `undefined`/`null`) when the flag is off, so every downstream `v-if` and ternary stays on its existing "ordinary bet" branch with no call-site change.

- [ ] **Step 1: Extend the `useGame.ts` import**

`src/hooks/useGame.ts:10` currently reads:

```ts
import { allowFreeBets } from '@/constants/featureFlags';
```

Replace with:

```ts
import { allowFreeBets, orbitEnabled } from '@/constants/featureFlags';
```

- [ ] **Step 2: Guard the computed**

Find (line ~198):

```ts
  const myBetThisRoundIsOrbit = computed(() => {
    const me = currentPlayerId.value;
    if (!me) return false;
    return !!gameInstance.value?.bets?.some((b) => b.externalPlayerId === me && b.viaOrbit);
  });
```

Replace with:

```ts
  const myBetThisRoundIsOrbit = computed(() => {
    // One computed, four strings: GameConsole reads it straight out of useGame() for its two
    // `> ORBIT PLACED YOUR BET FOR THIS ROUND.` notes, and betOutput below branches on it twice.
    // `false` rather than undefined on purpose — every consumer then takes its ordinary-bet branch
    // unchanged.
    if (!orbitEnabled) return false;

    const me = currentPlayerId.value;
    if (!me) return false;
    return !!gameInstance.value?.bets?.some((b) => b.externalPlayerId === me && b.viaOrbit);
  });
```

- [ ] **Step 3: Extend the `GameTerminal.vue` import and guard the row label**

`src/components/game/GameTerminal.vue:20` currently reads:

```ts
  import { allowFreeBets } from '@/constants/featureFlags';
```

Replace with:

```ts
  import { allowFreeBets, orbitEnabled } from '@/constants/featureFlags';
```

Then find (line ~77):

```ts
      return placedBet.viaOrbit ? 'YOU · ORBIT' : 'YOU';
```

Replace with:

```ts
      return orbitEnabled && placedBet.viaOrbit ? 'YOU · ORBIT' : 'YOU';
```

- [ ] **Step 4: Import into `GameHistoryPage.vue` and guard both chips**

In `src/pages/GameHistoryPage.vue`, after `import { ActiveGame, GameHistoryRow } from '@/types';` (line 7):

```ts
  import { orbitEnabled } from '@/constants/featureFlags';
```

Then replace **both** occurrences (line ~218, the desktop table cell, and line ~263, the mobile card) of:

```html
            <span v-if="row.myBetViaOrbit" class="chip ml-1 align-middle">ORBIT</span>
```

with:

```html
            <span v-if="orbitEnabled && row.myBetViaOrbit" class="chip ml-1 align-middle">ORBIT</span>
```

(The two lines have different leading indentation — preserve each one's own.)

- [ ] **Step 5: Verify all three paths and that `GameConsole.vue` was not touched**

Run:

```bash
grep -rn "orbitEnabled" .worktrees/orbit-frontend-flag/monkify-front/src/hooks/useGame.ts \
  .worktrees/orbit-frontend-flag/monkify-front/src/components/game/GameTerminal.vue \
  .worktrees/orbit-frontend-flag/monkify-front/src/pages/GameHistoryPage.vue
grep -rn "viaOrbit" .worktrees/orbit-frontend-flag/monkify-front/src/ | grep -v orbitEnabled
```

Expected from the first: 2 hits in `useGame.ts`, 2 in `GameTerminal.vue`, 3 in `GameHistoryPage.vue`.
Expected from the second: the only remaining ungated `viaOrbit` reads are in `useOrbitRedirect.ts:49` (inside a composable that early-returns, Task 3), `types/index.ts` (type declarations) and comments. **No ungated render site may remain.**

Run: `git -C .worktrees/orbit-frontend-flag/monkify-front status --short` — `src/components/game/GameConsole.vue` must **not** appear.

- [ ] **Step 6: Commit**

```bash
cd .worktrees/orbit-frontend-flag/monkify-front
git add src/hooks/useGame.ts src/components/game/GameTerminal.vue src/pages/GameHistoryPage.vue
git commit -m "Gate the owner-only ORBIT bet markers behind the flag."
```

---

### Task 7: Env plumbing — backend script and deploy template

Two repos, one line each. The frontend flag is build-time, so the value has to reach every build path: the local Docker stack (backend script) and the GCP deploys (deploy template → `frontBuild` → `.env.production.local`).

**Files:**
- Modify: `monkify-backend/scripts/setup_local_environment.bat` (one line after :73)
- Modify: `monkify-meta/deploy/secrets.example.json` (one key in `frontBuild`)

**Interfaces:**
- Consumes: the env var name `VUE_APP_ORBIT_ENABLED` (Task 1).
- Produces: nothing consumed by code. `SpaceStroker.Deploy` needs **no** change — `Deployer.WriteFrontEnv` (`deploy/tools/SpaceStroker.Deploy/Deployer.cs:588`) already serializes the whole `frontBuild` dictionary verbatim.

- [ ] **Step 1: Add the line to the backend script**

In `monkify-backend/scripts/setup_local_environment.bat`, find (line ~73):

```
  "  ('VUE_APP_ALLOW_FREE_BETS='+$s.Games.AllowFreeBets.ToString().ToLower())," ^
```

Insert **immediately after it**:

```
  "  'VUE_APP_ORBIT_ENABLED=true'," ^
```

⚠️ **Two traps, both documented in the file's own comments (lines 53–56 and 96–100) and both fatal:**
1. **No `REM` line inside the caret continuation.** A trailing `^` splices the next line into the same command, comment or not — a `REM` in the middle of this block gets handed to `powershell.exe` as an argument and the whole setup fails **after** the images have already rebuilt.
2. **No literal `"` inside a caret-continued argument.** `cmd` has no `\"` escape. The line above uses single quotes inside the outer double quotes, exactly like its neighbours.

- [ ] **Step 2: Verify the script's continuation block is still intact**

Run: `sed -n '64,78p' .worktrees/orbit-frontend-flag/monkify-backend/scripts/setup_local_environment.bat`

Expected: the new line sits between the `VUE_APP_ALLOW_FREE_BETS` line and the `VUE_APP_SOLANA_PROXY_URL` line; **every** line in the block still ends with `," ^` except the last (`'VUE_APP_DEV_LAUNCH=true');" ^`); no `REM` appears anywhere inside it.

- [ ] **Step 3: Add the key to the deploy template**

In `monkify-meta/deploy/secrets.example.json`, find inside `"frontBuild"`:

```json
    "VUE_APP_ALLOW_FREE_BETS": "false",
```

Insert immediately after:

```json
    "VUE_APP_ORBIT_ENABLED": "false",
```

- [ ] **Step 4: Add the explanatory comment key**

In the same file, immediately **before** the `"frontBuild": {` line, add a sibling comment key (the file already uses `_comment_*` keys this way — see `_comment_orbit`, `_comment_analytics`):

```json
  "_comment_orbit_flag": "ORBIT FRONTEND GATE (#146): VUE_APP_ORBIT_ENABLED in frontBuild decides whether the SPA SHOWS Orbit; the three Orbit secrets above decide whether the BACKEND engine is configured. They are independent — the engine keeps running on a flag-off build, which is safe only while no player is enrolled and no game type has AllowOrbit=true. FAIL-CLOSED: only the exact string 'true' enables it, so omitting the key ships Orbit hidden. production: 'false' (not production-deliverable yet); development: 'true'.",
```

- [ ] **Step 5: Verify the JSON still parses and no `REPLACE_ME` was disturbed**

Run:

```bash
node -e "const j=require('./.worktrees/orbit-frontend-flag/monkify-meta/deploy/secrets.example.json'); console.log(j.frontBuild.VUE_APP_ORBIT_ENABLED, Object.keys(j.frontBuild).length)"
git -C .worktrees/orbit-frontend-flag/monkify-meta diff -- deploy/secrets.example.json | grep -c "REPLACE_ME"
```

Expected: `false 15` from the first (the value, and 14 existing keys + 1 new). Expected `0` from the second — **no line containing `REPLACE_ME` may appear in the diff**, added or removed.

- [ ] **Step 6: Commit (two repos)**

```bash
cd .worktrees/orbit-frontend-flag/monkify-backend
git add scripts/setup_local_environment.bat
git commit -m "Enable Orbit in the local Docker stack's generated frontend env."

cd ../monkify-meta
git add deploy/secrets.example.json
git commit -m "Add the Orbit frontend gate to the deploy frontBuild template."
```

---

### Task 8: Gates — lint, build, and both-state verification

The only real test this feature has. **`yarn lint` and `yarn build` passing is necessary but nowhere near sufficient** — nothing in `monkify-front` type-checks `.vue` script blocks (`vue-tsc` is not a dependency), so a broken render passes both. The browser check is the test.

**Files:** none modified (unless lint/QA finds a defect — then fix and re-run).

**Interfaces:**
- Consumes: everything from Tasks 1–7.
- Produces: the evidence for AC 1–9 and 13.

- [ ] **Step 1: Install and lint**

```bash
cd .worktrees/orbit-frontend-flag/monkify-front
cp ../../../monkify-front/yarn.lock .          # gitignored in the worktree; the hub has it
yarn install --frozen-lockfile
yarn lint
```

Expected: clean. If it reports `Insert ␍` on a file you edited, the edit wrote LF into a CRLF file — fix with `unix2dos <file>`, confirm with `od -c <file> | head`, re-run `yarn lint`.

- [ ] **Step 2: Build with the flag OFF**

```bash
cd .worktrees/orbit-frontend-flag/monkify-front
yarn build
```

Expected: build succeeds. (No `.env.local` in the worktree ⇒ `VUE_APP_ORBIT_ENABLED` is unset ⇒ **fail-closed gives a flag-OFF bundle**, which is exactly the state AC 1 describes.)

- [ ] **Step 3: Serve the flag-OFF build and verify in the browser**

Run the dev server on a **verified-free** port (8081 is often held by another worktree's server — check first with `netstat -ano | findstr :8081`):

```bash
cd .worktrees/orbit-frontend-flag/monkify-front
cp ../../../monkify-front/.env.local .        # the hub's local config: API URL, token addresses
# then DELETE the VUE_APP_ORBIT_ENABLED line if present, to test the OFF state
yarn serve --port 8085
```

With the backend stack up, check against **AC 2, 3, 4, 5, 6**:
- Connect a wallet → **zero** `/orbit/*` requests and no `purpose=orbit-session|orbit-cashout` nonce calls in the network tab.
- Header: no ORBIT row, no layout gap; WALLET balance + wallet buttons unchanged.
- ☰ menu: no Orbit row; **Settings, Stats, Fair, History, Responsible Gaming all present**.
- Play a round to `Ended` → no `GET /games/{id}` poll for the next 45 s, no navigation away.
- Win a paid bet → no first-win modal, **victory audio still plays once**.
- Idle-disconnect and return → no `JoinOrbitNotifications` frame in the WS panel.

- [ ] **Step 4: Verify the flag ON state (AC 9)**

```bash
echo "VUE_APP_ORBIT_ENABLED=true" >> .env.local
# restart yarn serve
```

Expected: every Orbit surface behaves **exactly as on `main`** — header balance, modal, notifications, cross-round redirect, first-win upsell, owner-only markers. No regression.

- [ ] **Step 5: Verify the local Docker stack path (AC 11)**

⚠️ Needs the Docker daemon running.

```bash
cd .worktrees/orbit-frontend-flag/monkify-backend
scripts\setup_local_environment.bat
grep VUE_APP_ORBIT_ENABLED ../monkify-front/.env.production.local
```

Expected: `VUE_APP_ORBIT_ENABLED=true`, and Orbit **visible** in the container at `http://localhost:6003`.

- [ ] **Step 6: Viewport check (AC 13)**

At **1366×768** and **390×844**, confirm no layout gap where the ORBIT header row used to sit — the WALLET row should sit alone, correctly aligned, not floating with reserved space beneath it.

- [ ] **Step 7: Confirm the untouched-file list holds**

```bash
git -C .worktrees/orbit-frontend-flag/monkify-front diff --name-only origin/main
```

Expected — **exactly** these 10, nothing else:

```
.env.example
src/components/GameHeader.vue
src/components/game/GameTerminal.vue
src/constants/featureFlags.ts
src/hooks/useFirstWinUpsell.ts
src/hooks/useGame.ts
src/hooks/useOrbitNotifications.ts
src/hooks/useOrbitRedirect.ts
src/pages/GameHistoryPage.vue
src/pages/GamePage.vue
```

Any of `useGameHub.ts`, `useOrbit.ts`, `orbitService.ts`, `types/orbit.ts`, `types/index.ts`, `components/orbit/*`, `OrbitBalance.vue`, `GameConsole.vue`, `admin/GameTypeForm.vue` appearing here is a **scope violation** — revert it.

- [ ] **Step 8: Push all three branches**

```bash
for r in monkify-front monkify-backend monkify-meta; do
  git -C .worktrees/orbit-frontend-flag/$r push origin feature/orbit-frontend-flag
done
```

---

## Deferred to ops (not implementable here)

| Item | Why it is not a task |
| --- | --- |
| **AC 12** — verify production has no Orbit-enrolled players and no `AllowOrbit=true` game type | `constraints.md` §5 forbids the agent running production queries. **The user runs it** and records the result on the card. It blocks the flag-off *deploy*, not the merge — but it is the entire safety argument for a frontend-only gate. |
| `deploy/secrets.production.json` → `"false"`, `secrets.development.json` → `"true"` | Git-ignored, ops-owned. |
| Each developer adding `VUE_APP_ORBIT_ENABLED=true` to their own `.env.local` | Git-ignored. Consequence of fail-closed — must be in the ship notes. |

## Known collision

`feature/orbit-ui-overhaul` worktrees are open in `monkify-front` + `monkify-meta` and touch `GameHeader.vue` and `components/orbit/*`. **Whichever branch lands second must re-apply the gates** — a rebase that silently drops an `orbitEnabled` check ships Orbit into production. Call it out in the PR.

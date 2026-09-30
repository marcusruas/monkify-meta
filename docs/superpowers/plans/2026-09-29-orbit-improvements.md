# Orbit Improvements (quick popup + sectioned modal) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Orbit's panel + `/orbit` page with a header popup (design P1) and one sectioned modal (design M4), per `specs/orbit-improvements.md`.

**Architecture:** Extract the two money actions into self-contained components (`OrbitDepositControl`, `OrbitCashOutControl`) shared by a new popup (`OrbitQuickPanel`) and the modal's WALLET section. Rebuild `OrbitModal.vue` around a jump strip + five `OrbitSection` wrappers whose open state lives in `useOrbitSections`. Delete the page and every route to it.

**Tech Stack:** Vue 3 `<script setup>` + TypeScript, Tailwind + `arcade.css`, vue-router, vue3-toastify. No test runner in `monkify-front`: each task is verified with `yarn lint` (touched files) and, at the end, `yarn build` + browser QA (spec §13).

## Global Constraints

- Work only in `E:\Monkify\.worktrees\orbit-improvements\monkify-front` on `feature/orbit-improvements`.
- No backend change; every money action calls the same `useOrbit()` function as today (`deposit`, `cashOut`, `updateSettings`, `recoverDeposit`, telegram fns, `loadHistory`).
- Full cash-out sends NO amount (`cashOut(wallet)`); partial must be `>= minimumCashOut`.
- Keep every existing Orbit error string; new copy only as listed in spec §7.6.
- localStorage key: `space-stroker.orbit.sections`, every access in try/catch.
- Everything stays behind `orbitEnabled`; no new `VUE_APP_*` key.
- Files are CRLF in the repo: prettier `endOfLine: lf` — run `yarn lint --fix` on touched files.

---

### Task 1: Section-state plumbing (types + `useOrbitSections` + `useHeaderModals`)

**Files:**
- Modify: `src/types/orbit.ts` (add `OrbitSectionId`, remove `OrbitTabDef` at ~316)
- Create: `src/hooks/useOrbitSections.ts`
- Modify: `src/hooks/useHeaderModals.ts`

**Interfaces — Produces:**
- `export type OrbitSectionId = 'wallet' | 'autobet' | 'notifications' | 'recover' | 'history'`
- `useOrbitSections(visible: Ref<OrbitSectionId[]>)` → `{ expanded: Ref<Set<OrbitSectionId>>, isOpen(id), toggle(id), open(id), resetForOpen(opts: { running: boolean; forced: OrbitSectionId | null }) }`
- `useHeaderModals()` adds `orbitPopupOpen: Ref<boolean>`, `orbitSection: Ref<OrbitSectionId | null>`, `openOrbitPopup(): void`, `openOrbit(section?: OrbitSectionId): void`

- [ ] Step 1: Add the type; delete `OrbitTabDef`.
- [ ] Step 2: Write `useOrbitSections.ts`:

```ts
import { ref, type Ref } from 'vue';
import type { OrbitSectionId } from '@/types/orbit';

const STORAGE_KEY = 'space-stroker.orbit.sections';
const DEFAULT: OrbitSectionId[] = ['wallet'];
const ALL: OrbitSectionId[] = ['wallet', 'autobet', 'notifications', 'recover', 'history'];

const readStored = (): OrbitSectionId[] | null => {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.filter((id) => ALL.includes(id)) : null;
  } catch {
    return null;
  }
};

const writeStored = (ids: OrbitSectionId[]) => {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(ids));
  } catch {
    // storage blocked: the layout simply is not remembered
  }
};

export const useOrbitSections = (visible: Ref<OrbitSectionId[]>) => {
  const expanded = ref(new Set<OrbitSectionId>(DEFAULT));
  const isOpen = (id: OrbitSectionId) => expanded.value.has(id) && visible.value.includes(id);
  const persist = () => writeStored([...expanded.value]);
  const toggle = (id: OrbitSectionId) => {
    const next = new Set(expanded.value);
    if (next.has(id)) next.delete(id); else next.add(id);
    expanded.value = next;
    persist();
  };
  const open = (id: OrbitSectionId) => {
    if (expanded.value.has(id)) return;
    expanded.value = new Set([...expanded.value, id]);
    persist();
  };
  const resetForOpen = ({ running, forced }: { running: boolean; forced: OrbitSectionId | null }) => {
    const next = new Set(readStored() ?? DEFAULT);
    if (running) next.add('autobet');
    if (forced) next.add(forced);
    expanded.value = next;
  };
  return { expanded, isOpen, toggle, open, resetForOpen };
};
```

- [ ] Step 3: Extend `useHeaderModals.ts` with `orbitPopupOpen`, `orbitSection`, `openOrbitPopup`, `openOrbit(section?)` (sets `orbitSection.value = section ?? null; orbitPopupOpen.value = false; orbitOpen.value = true`).
- [ ] Step 4: `npx eslint` the three files. Commit.

### Task 2: Shared money controls

**Files:**
- Create: `src/components/orbit/OrbitDepositControl.vue` (logic from `OrbitModal.onDeposit` + `walletTokenBalance` + `OrbitAmountField` props)
- Create: `src/components/orbit/OrbitCashOutControl.vue` (logic from `tabs/OrbitMoneyTab.vue` 120-175 + UI 240-282 + toast rule from `OrbitModal.onCashOut`)

**Interfaces — Produces:** both `defineProps<{ compact?: boolean }>()`, `defineEmits<{ (e: 'notice', text: string | null): void }>()`; both read the wallet from `useMobileWallet().effectivePublicKey`.

- [ ] Step 1: Deposit control — `canDeposit = !isBusy && !selfExcluded`; on confirm `deposit(wallet, amount)`, then emit `Deposited {formatSol(amount)} {tokenLabel}.` (neutral: a deposit no longer implies auto-bet). Uses `useTokenBalance` activate/deactivate + mint guard exactly as `OrbitModal` 824-832.
- [ ] Step 2: Cash-out control — amount field pre-filled with balance (watch balance, refill when field empty or equals previous balance), MAX button, button `Cash out {formatSol(amount)}`; disabled per `canCashOut && cashOutAmountIsValid`; cool-off line when `isWithinCashOutGap`; "Nothing to cash out" line when balance is 0; after `cashOut` toast `{amount} SOL is on its way to your wallet.` only if balance dropped. Full → `cashOut(wallet)`.
- [ ] Step 3: eslint. Commit.

### Task 3: The popup (`OrbitQuickPanel`) + launcher + header wiring

**Files:**
- Create: `src/components/orbit/OrbitQuickPanel.vue`
- Modify: `src/components/orbit/OrbitLauncher.vue` (click + labels)
- Modify: `src/components/GameHeader.vue` (wrap launcher in `relative` div with the panel; remove ☰ ORBIT cell; first-win → `openOrbit('autobet')`; close popup on route change)

- [ ] Step 1: `OrbitQuickPanel` renders body (banner one-card + `OrbitStatusTag`, ADD MONEY + `OrbitDepositControl compact`, CASH OUT + `OrbitCashOutControl compact`, dashed divider + MORE OPTIONS button, notice/lastError) inside either an anchored `div` (≥ lg via `useMediaQuery`-style `matchMedia('(min-width: 1024px)')` ref) or `ArcadeModal size="compact" variant="orbit" title="ORBIT"` (< lg). Anchored: `useClickOutside(rootRef, close, orbitPopupOpen)` where `rootRef` is passed from GameHeader wrapper; Esc via `useOverlayStack().registerOverlay` while open.
- [ ] Step 2: Launcher click → `hasSession && orbit ? toggle orbitPopupOpen : openOrbit()`; labels `Orbit — {x} held. Open Orbit.` / `Orbit — sign in`.
- [ ] Step 3: GameHeader changes. eslint. Commit.

### Task 4: `OrbitSection` + `OrbitSectionStrip` + moved Telegram/Recover sections

**Files:**
- Create: `src/components/orbit/OrbitSection.vue` (props `id`, `title`, `open`; emits `toggle`; header `<button aria-expanded aria-controls>`; body `v-if`)
- Create: `src/components/orbit/OrbitSectionStrip.vue` (props `sections: {id,label}[]`, `active`; emits `jump(id)`)
- Move: `tabs/OrbitTelegramTab.vue` → `sections/OrbitNotificationsSection.vue`, `tabs/OrbitRecoverTab.vue` → `sections/OrbitRecoverSection.vue` (`git mv`), drop their `orbit-section-title` line, strip `orbit-field-lg`, `orbit-field-label` → `dialog-eyebrow`.
- [ ] Steps: write, eslint, commit.

### Task 5: Rebuild `OrbitModal.vue`

**Files:** Modify `src/components/orbit/OrbitModal.vue`

- [ ] Step 1: Remove (spec §7.3 table): switch guard, GAME MODE line, picker, auto-point, `currentGame`, `OrbitControlBar`, footer link, `onCashOut`, `onDeposit` + `walletTokenBalance` (moved), `useRoute`.
- [ ] Step 2: Add `autoBetType = computed(() => orbit.value?.eligibleGameTypes[0] ?? null)`; `wordGame = activeGameType ?? autoBetType`; `runTarget = autoBetType`; `onToggle` always sends `gameParametersId: autoBetType.id`.
- [ ] Step 3: Add the start/pause button (state table from `OrbitControlBar.toggle`, labels `▶ START AUTO-BET` / `RESUME AUTO-BET` / `❚❚ PAUSE AUTO-BET`), reusing `.orbit-trio .orbit-trio--compact .orbit-trio__btn` classes for a single full-width button.
- [ ] Step 4: Template: status row → strip → sections (WALLET, LET ORBIT PLAY {NAME}, NOTIFICATIONS if `telegramEnabled`, RECOVER A DEPOSIT, BET HISTORY) → notice/lastError → "How Orbit works" link. Info tips on the two rules. Rewrite header comment.
- [ ] Step 5: On `open` → `resetForOpen({ running: orbitStatus === 'running', forced: orbitSection })`, clear `orbitSection`, then `nextTick` scroll the forced section into view.
- [ ] Step 6: eslint. Commit.

### Task 6: Entry points, copy, deletions

**Files:** `useOrbitOutOfFunds.ts`, `pages/HomePage.vue`, `components/guide/sections/GuideOrbit.vue`, `routes.ts`, `main.ts`, delete `pages/OrbitPage.vue`, `OrbitTabBar.vue`, `OrbitStatStrip.vue`, `OrbitControlBar.vue`, `tabs/*`; `arcade.css` page-only rules; stale comments.

- [ ] Step 1: Rewire (spec §7.7). Step 2: delete files + route + embed entry. Step 3: remove `.orbit-page*`, `.orbit-statstrip*`, `.orbit-tab*` CSS (keep `.orbit-trio*`). Step 4: grep gate from spec §7.8. Step 5: commit.

### Task 7: Verify

- [ ] `yarn lint` clean; `yarn build` succeeds (copy hub `yarn.lock` first if needed).
- [ ] Browser QA per spec §13 against the local stack.

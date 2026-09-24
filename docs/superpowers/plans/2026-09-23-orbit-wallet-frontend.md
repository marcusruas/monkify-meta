# Orbit Wallet — Frontend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A "PAY FROM: WALLET | ORBIT" switch on the game console that places manual bets from the Orbit balance, a wallet-first `/orbit` page, and "auto-bets only" copy on every stop rule.

**Architecture:** A persisted preference hook (`usePaySource`) decides the source; `useGame.runBet` branches before any transfer is built and, for Orbit, calls `POST /orbit/wagers` then polls `GET /orbit/wagers/{id}`. `/orbit` stops requiring a game before deposits.

**Tech Stack:** Vue 3 + TypeScript, composables in `src/hooks`, axios services in `src/services`, `VUE_APP_*` build-time flags.

**Authoritative design:** `monkify-meta/specs/orbit-wallet.md` §7 (and §3 decisions 4, 7, 8, 9, 11). The backend contract is in the backend plan (`2026-09-23-orbit-wallet-backend.md`, Task 5 "Produces").

## Global Constraints

- Work only in `E:\Monkify\.worktrees\orbit-wallet\monkify-front` on `feature/orbit-wallet`.
- Before the first build: copy `E:\Monkify\monkify-front\yarn.lock` into the worktree (gitignored — a missing lockfile builds a stale image), and junction the hub's `node_modules` if not installed: `cmd /c mklink /J node_modules E:\Monkify\monkify-front\node_modules`.
- No test runner exists. DoD per task: `yarn lint` (ignore prettier CRLF false alarms on untouched files; files you edit must be LF-clean per `.prettierrc` `endOfLine: lf`) and `yarn build` green.
- Everything behind `orbitEnabled` (`src/constants/featureFlags.ts`); hidden when `isEmbedded` or `freeBetMode`.
- Balance checks read the SERVER figure `orbit.value.balance`, never the optimistic display `balance` (`useOrbit.ts:55-72`).
- `viaOrbit` on a bet now means **auto-placed** (backend change) — keep every existing `viaOrbit` consumer (console lock, once-per-round Orbit debit, `YOU · ORBIT`, win-toast exclusion) as-is.
- Copy strings exactly as given below. Commit after each task (`Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`); do not push.

---

### Task 1: Types, service, `usePaySource`

**Files:**
- Modify: `src/types/orbit.ts`, `src/services/orbitService.ts`
- Create: `src/hooks/usePaySource.ts`

**Produces:**

```ts
// types/orbit.ts
export interface OrbitWagerBody { requestId: string; gameId: string; choice: string; amount: number; seed: string; nonce: number; }
export type OrbitWagerStatus = 1 | 2 | 3 | 4; // Pending, Placed, Refused, NotFound (backend enum, numeric)
export const ORBIT_WAGER = { Pending: 1, Placed: 2, Refused: 3, NotFound: 4 } as const;
export interface OrbitWagerResult { requestId: string; status: OrbitWagerStatus; betId?: string | null; message?: string | null; }
export type OrbitBetSource = 1 | 2; // Auto, Manual
// OrbitHistoryRow gains:  source: OrbitBetSource;
```

Check how the backend serialises enums (look at `OrbitBetOutcomeWire` in `types/orbit.ts`): if outcomes arrive as numbers, keep numbers as above; if as strings, mirror that.

```ts
// services/orbitService.ts (same style as the existing calls, withSession())
placeWager: (body: OrbitWagerBody) => mainAPI.post<OrbitWagerResult>('/orbit/wagers', body, withSession()),
getWager: (requestId: string) => mainAPI.get<OrbitWagerResult>(`/orbit/wagers/${requestId}`, withSession()),
```

```ts
// hooks/usePaySource.ts — modelled on useDisplaySettings.ts
import { computed, ref } from 'vue';
import { orbitEnabled } from '@/constants/featureFlags';

export type PaySource = 'wallet' | 'orbit';
const KEY = 'space-stroker.paySource';

function read(): PaySource {
  try { return localStorage.getItem(KEY) === 'orbit' ? 'orbit' : 'wallet'; } catch { return 'wallet'; }
}
const preference = ref<PaySource>(read());

export function usePaySource() {
  const setPreference = (value: PaySource) => {
    preference.value = value;
    try { localStorage.setItem(KEY, value); } catch { /* storage unavailable — keep in memory */ }
  };
  /** Orbit only when the flag is on and the context allows it; callers pass the live context. */
  const effectiveSource = (ctx: { embedded: boolean; freeBet: boolean }) =>
    computed<PaySource>(() => (orbitEnabled && !ctx.embedded && !ctx.freeBet && preference.value === 'orbit' ? 'orbit' : 'wallet'));
  return { preference, setPreference, effectiveSource };
}
```

(Adapt the import alias and the embedded/free-bet sources to what `useGame.ts` / `GameConsole.vue` already use — `freeBetMode` from `useGame`, the embedded flag from wherever `GameHeader.vue` reads `isEmbedded`.)

- [ ] Implement; `yarn lint && yarn build`; commit `Add Orbit wager service, types and pay-source preference.`

### Task 2: Orbit branch in `useGame.runBet`

**Files:** Modify `src/hooks/useGame.ts` (`runBet` L935-1266, fee headroom L992-1003, `submitBet` L882-932), `src/hooks/useOrbit.ts` (expose `orbit`, `ensureSession`, `loadIfSignedIn`, `applyOrbitDebit`, `refreshQuietly` if not already exported).

Behaviour (spec §7.4):
- Compute `source` once at the start of `runBet` via `usePaySource().effectiveSource({ embedded, freeBet: freeBetMode.value })`.
- Auto-play block (both sources) right after the self-exclusion guard:

```ts
const o = useOrbit().orbit.value;
if (orbitEnabled && o?.enabled && o.gameParametersId === selectedGame.value?.id) {
  betOutput.value = 'Orbit is auto-playing this game — pause it to bet manually.';
  onBetFail();
  return;
}
```

- When `source === 'orbit'`: SKIP the wallet fee-headroom block; instead

```ts
const o = useOrbit().orbit.value;
if (o && o.balance < stake) {
  betOutput.value = 'Not enough Orbit balance — deposit';
  onBetFail();
  return;
}
```

  and after the existing shared guards (per-round limits, Hyper Roll board re-read / availability) and `isLoadingBet = true` + `nextBetSeed()`, branch BEFORE the free-bet/transfer code:

```ts
if (source === 'orbit') {
  await placeFromOrbit({ gameId, choice, amount, seed, nonce }, onBetFail);
  return;
}
```

  with

```ts
const ORBIT_POLL_MS = 1000;
const ORBIT_POLL_LIMIT_MS = 60000;

async function placeFromOrbit(body: Omit<OrbitWagerBody, 'requestId'>, onBetFail: () => void) {
  const orbitApi = useOrbit();
  const requestId = crypto.randomUUID();
  try {
    let result = (await orbitService.placeWager({ ...body, requestId })).data;
    const started = Date.now();
    while (result.status === ORBIT_WAGER.Pending && Date.now() - started < ORBIT_POLL_LIMIT_MS) {
      await new Promise((r) => setTimeout(r, ORBIT_POLL_MS));
      result = (await orbitService.getWager(requestId)).data;
    }
    if (result.status === ORBIT_WAGER.Placed) {
      betOutput.value = 'Your bet was successfully placed';
      orbitApi.applyOrbitDebit(body.amount, 'stake');
    } else if (result.status === ORBIT_WAGER.Refused) {
      betOutput.value = result.message || 'There was an error processing your bet';
      onBetFail();
    } else {
      betOutput.value = 'Still placing — check your Orbit history.';
    }
  } catch (err) {
    betOutput.value = extractApiMessage(err) || 'There was an error processing your bet';  // reuse submitBet's error extraction, incl. MAINTENANCE_MESSAGE handling
    onBetFail();
  } finally {
    isLoadingBet.value = false;
    void orbitApi.refreshQuietly(walletAddress, 'bet');   // use the existing reason union; add 'bet' only if it type-checks, else reuse an existing reason
  }
}
```

  Use whatever `submitBet` already does to turn an axios error into `betOutput` (don't invent a new helper if one exists). `gameId` must be the ROUND id the wallet path posts to (`/games/{id}/bets`): for Hyper Roll the solo round id.
- Do NOT call `useTokenBalance().applyDebit` on the Orbit path. Do NOT touch the `viaOrbit` once-per-round debit (L594-605).
- `bet()` (L1275-1287) already wraps `runBet` with `hyperRollBetInFlight` — no change.

- [ ] Implement; `yarn lint && yarn build`; commit `Place manual bets from the Orbit balance.`

### Task 3: PAY FROM switch on `GameConsole.vue`

**Files:** Modify `src/components/game/GameConsole.vue`. Read the whole file first (desktop + phone layouts, `placeBet` L198-255, `pullLever` L325-360, the ⚠️ comment L136-150).

- Rewrite the L136-150 comment: record that #194 (2026-09-23, owner decision in refinement) reversed the 2026-08-04 "no Orbit entry point" call; the console now reads Orbit state ONLY through `useOrbit().loadIfSignedIn` (stored session, never prompts) plus the PAY FROM switch.
- On mount / wallet change: `if (orbitEnabled && wallet) useOrbit().loadIfSignedIn(wallet)`.
- Segmented control rendered in both layouts next to the stake/lever area, shown only when `orbitEnabled && !isEmbedded && !freeBetMode && wallet`:

```html
<div class="pay-from" role="radiogroup" aria-label="Pay from">
  <span class="pay-from__label">PAY FROM</span>
  <button type="button" role="radio" :aria-checked="source === 'wallet'" @click="choose('wallet')">WALLET</button>
  <button type="button" role="radio" :aria-checked="source === 'orbit'" @click="choose('orbit')">
    ORBIT <MoneyValue v-if="orbit" :value="orbit.balance" :format="formatCompact" unit="SOL" />
  </button>
</div>
<p v-if="source === 'orbit'" class="pay-from__hint">Manual bets never trigger your auto cash-out rules.</p>
```

  Style with the existing console arcade tokens/classes (look at how the Hyper Roll stake chips / LCD are styled; reuse, don't add new colours).
- `choose('orbit')`: if `!useOrbit().hasSession.value`, `await ensureSession(wallet, { allowHop: true })`; on failure keep `wallet`. Then `setPreference(...)`.
- Disabled states (lever + inputs), with the message shown in the console's existing hint/status slot:
  - auto-play active on this type (see Task 2 predicate) → `Orbit is auto-playing this game — pause it to bet manually.` + `<router-link to="/orbit?tab=game">Pause it</router-link>`
  - source orbit and `orbit.balance < stake` (Hyper Roll: the LCD stake) → `Not enough Orbit balance —` `<router-link to="/orbit?tab=wallet">deposit</router-link>`
  Both use `leverError()` when the lever is pulled anyway.

- [ ] Implement; `yarn lint && yarn build`; commit `Add the PAY FROM switch to the game console.`

### Task 4: `/orbit` wallet-first + deposit without a game

**Files:** `src/pages/OrbitPage.vue`, `src/components/orbit/OrbitTabBar.vue`, `src/components/orbit/tabs/OrbitGameTab.vue`, `src/components/orbit/tabs/OrbitMoneyTab.vue`, `src/components/orbit/OrbitModal.vue`, `src/hooks/useOrbit.ts`.

- `OrbitPage.vue` `TABS` (L186-192): `{ id: 'wallet', label: 'WALLET' }` (the old `money` tab, same component), `{ id: 'game', label: 'AUTO-BET' }`, `{ id: 'stop-rules', label: 'AUTO CASH-OUT' }`, `recover`, `telegram`. Default tab `'wallet'` (L198). `?tab=money` → treat as `wallet` (L210-214). `applyBlockedTabIfUnchosen` targets `wallet`.
- Signed-out pitch / intro (L388-456): first card/line **"YOUR IN-GAME WALLET — Deposit once, then bet on any game without signing every time. Wins land back here."**; the auto-bet cards read as optional ("…and if you want, it can play Cosmic Sprint for you."). "Banks a winning run…" / "Stops a losing one…" say **auto-bet**.
- `OrbitGameTab.vue`: DELETE the auto-commit `watch` (L109-118). With one eligible type, keep the read-only line and add a button **"USE AUTO-BET ON {{ onlyEligibleType.name }}"** calling `commitGame()`, shown only while `orbit.gameParametersId !== onlyEligibleType.id`.
- `OrbitMoneyTab.vue`: `canDeposit` (L63-65) no longer requires `gameParametersId`; delete "Pick a game first — Orbit needs to know what it would play." (L221-223); L184 copy → **"Bet from this balance on any game — pick ORBIT under PAY FROM. Auto-bet spends from it too."**
- `useOrbit.ts deposit` (L569-572): delete the `gameParametersId` guard.
- `OrbitModal.vue`: `wordLocked` (L258) must no longer dim **ADD MONEY** (L1383-1408); keep it on the auto-bet rules.

- [ ] Implement; `yarn lint && yarn build`; commit `Make /orbit wallet-first; deposits no longer need a game.`

### Task 5: "Auto-bets only" copy everywhere + bet-log TYPE column

**Files and exact copy:**
- `components/orbit/tabs/OrbitStopRulesTab.vue`: under the title (L246-249) add **"These rules only watch auto-bets. Manual bets from your Orbit balance never trigger them."**; `thresholdHelp` (L75) → **"Orbit sends the balance to your wallet as soon as an auto-bet win takes it here."**; in `floorHelp` / `floorAlert` replace "a win"/"Orbit" references so they say *auto-bet*; fix the stale L20 comment (the panel DOES still render these fields in `OrbitModal.vue`).
- `components/orbit/OrbitModal.vue`: heading L1428 → **"AUTO CASHOUT — AUTO-BETS ONLY"**; add the same one-liner under it; `rulesHint` (L287-289) → **"No floor set — auto-bet plays down to zero."**; help popover L1061-1064 mentions the rules watch auto-bets only.
- `components/guide/sections/GuideOrbit.vue` rows (L21-50): new first row **title "IT'S YOUR IN-GAME WALLET"**, body **"Deposit once and pick ORBIT under PAY FROM to bet on any game without signing each time. Wins and refunds land back in your Orbit balance."**; "IT HAS ITS OWN BALANCE" body adds that manual bets spend from it too; "YOU SET WHEN IT STOPS" body appends **"These rules only watch auto-bets — manual bets never trigger them."**; new row **"ONE GAME, ONE DRIVER"** → **"While auto-bet is running on a game you can't also bet on it by hand. Pause it to play yourself."**
- `components/home/HomeOrbitCard.vue` beats (L42-55): add the wallet beat; the stop-rules beat says auto-bets.
- `components/orbit/OrbitBetLog.vue`: add a **TYPE** column after WHEN rendering `row.source === 1 ? 'AUTO' : 'MANUAL'`; fix the stale L10 comment.

- [ ] Implement; `yarn lint && yarn build`; commit `Say stop rules are auto-bet only; label Orbit bets Auto/Manual.`

## Self-review notes

Spec §7.1 → T1; §7.2 → T1; §7.3 → T3; §7.4 → T2; §7.5 → T4; §7.6-7.7 → T5; §7.8 needs no change (Orbit pill already in header).

# Monkey Duel — Frontend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the `monkify-front` half of Monkey Duel (#184) — a single machine with two CRT tabs: **VS HOUSE** (the default you land on, playable immediately) and **OPEN DUELS** (a live board of player-created rooms you can browse, take, or post to).

**Architecture:** A new `'duel'` archetype, resolved **first** in `resolveGameArchetype`, routes `GamePage` to a duel machine. State lives in a new `useDuel` composable (tab, board, cursor, sort, the clamped ceiling, open/join/cancel); the board updates over a **new per-type rooms channel on the existing single hub connection** — no second hub, no polling. Betting, availability and verification reuse the shipped `applyBet` / `checkGameAvailability` / `getGameVerification`, so referral stamping and the wallet flow are untouched.

**Tech Stack:** Vue 3.2 + TypeScript 5.5, Vue CLI 5 (webpack), Tailwind 3 + DaisyUI, `solana-wallets-vue`, `@solana/web3.js`, `@microsoft/signalr`, axios. Package manager: **yarn**. ⚠️ **No test framework in this repo** — verification is `yarn lint` + `yarn build` + manual QA in a browser against the local stack.

**Spec:** `monkify-meta/specs/monkey-duel.md` (§7 is the frontend section; read §1.2 and §7.6 before writing any copy)

**Design:** the settled clickable flow, round 6 — <https://claude.ai/artifact/JufhzgZu8FYtm1qfdqSRCU>. Its markup, tokens and key sprites are lifted from this repo already, so the page is a **port, not a redesign**.

**Backend plan:** `2026-09-24-monkey-duel-backend.md` — the API this plan consumes. Build it first, or at least Tasks 7, 8, 12, 13 and 15.

**Branch / worktree:** `feature/monkey-duel` → `C:\Users\kurok\Desktop\monkify\.worktrees\monkey-duel\monkify-front`. Never work in the hub.

---

## Global Constraints

- **⚠️ THE CUT IS 2%, NOT 5%, AND THE ARTIFACT'S FORMULA IS WRONG TWICE OVER.** This is AC 15 and the single most important rule in this plan. The artifact hard-codes `const CUT = 0.05` and computes `winOf = (stake) => 2 * stake * (1 - CUT)`. Both are wrong:
  - the rate must be read **live** from `selectedGame.commissionPercentage` (2 today), never hard-coded;
  - the cut applies **only to profit above the winner's own stake**, so `payout = stake × (2 − rate)` = **1.98 × stake** at 2% — not `2 × stake × (1 − rate)` = 1.96 × stake.
- **Every duel word is 6 letters, lowercase, `a–z`.** `choiceRequiredLength` comes from the type; do not hard-code 6 in a validator — read it, the way the shipped console does.
- **Vague messaging** (`constraints.md` §6): the house tab being unavailable, a room already taken, a refused stake and a refused word all reuse the **existing** server message verbatim. Add no new client-side explanation of *why*.
- **One connection per tab** (`specs/unified-websocket-connection.md`). The rooms channel joins the **existing** `useGameHub` connection. No second `HubConnection`, no `setInterval` refetch.
- **The coin is `monk-coin-spin.gif` (animated) / `monk-coin-main.png` (still).** ⚠️ **Never `monk-coin.png`** — that is the retired red-rim art.
- **Red = you, blue = your rival**, throughout the duel machine.
- **`yarn`, never `npm`.** `vue-cli-service serve` detects `yarn.lock` and aborts with exit 1 under npm.
- **Dev server runs from the worktree on a verified-free port**, never the `:6003` container (it builds in production mode → `mainnet-beta` → real money).
- **Commit after every task.** Push `feature/monkey-duel` to `origin` after the first commit.

## Review Focus

Five input classes the spec implies but the happy path does not exercise. Each is pinned in the task that owns the code.

1. **`commissionPercentage` arrives as `0`, `undefined`, or from a stale cached catalogue.** The payout line must not silently print a 100% payout or `NaN`. → Task 2.
2. **`availability.maxStake` is `0`** (the HouseSeed wallet is below the floor). The stake stepper must clamp to the floor and the tab must go unavailable — never offer a stake of 0, never divide by it. → Tasks 3 + 6.
3. **A `taken` event arrives for the room the cursor is currently on.** The cursor must move to a valid neighbour, not sit on a removed row or run off the end of the list. → Task 4.
4. **The SPA reloads mid-duel.** `GET /games/{type}?wallet=` must re-attach to *your* round (the backend returns owner-or-claimer), and the machine must land on the right tab for that round's `duelKind` — not the default. → Task 5.
5. **The player's word and the creator's word collide** (they typed the room's own word). The bet is refused server-side and refunded; the console must surface the vague message **with** the refund notice and re-enable the lever, not leave it stuck on `PLACING…`. → Task 6.

---

## File Structure

### Created

| File | Responsibility |
| --- | --- |
| `src/hooks/useDuel.ts` | All duel state: tab, board list, cursor, sort, clamped ceiling, open/join/cancel, the rooms subscription. |
| `src/components/game/DuelTabs.vue` | The two CRT tabs inside the tube. |
| `src/components/game/DuelBoard.vue` | The open-rooms grid + the `+ OPEN YOUR OWN DUEL` tile. |
| `src/components/game/DuelCard.vue` | The two corners either side of `VS`. |
| `src/components/game/DuelConsole.vue` | The two-row plates and the lever. |
| `src/components/game/DuelVsSplash.vue` | The `duelTimeUntilGameStarts` beat. |
| `src/utils/duel.ts` | Pure helpers: the payout formula, stake stepping/clamping, board sorting. |

### Modified

`src/types/index.ts`, `src/services/mainService.ts`, `src/hooks/useGameHub.ts`, `src/hooks/useGame.ts`, `src/utils/gameArchetype.ts`, `src/utils/gameArt.ts`, `src/components/game-selection/GameTypeCard.vue`, `src/components/game/GameTerminal.vue`, `src/components/game/GameConsole.vue`, `src/pages/GamePage.vue`, `src/utils/provablyFair.ts`, `public/verify.html`, `src/assets/`.

---

## Task 1: Types and services

**Files:**
- Modify: `src/types/index.ts`
- Modify: `src/services/mainService.ts`

**Interfaces:**
- Consumes: the backend contracts from backend Tasks 8, 15.
- Produces: `ActiveGame.isDuel`; `GameInstance.duelKind/stake/ownerWallet/isFull`; `GameAvailability.maxStake`; `BodyGameAvailability.gameId` optional; `DuelRoom`; `DuelRoomEvent`; `GameVerification.isDuel/duelKind/duelPoolSnapshot/houseWord`; `mainService.openDuelRoom/getDuelRooms/cancelDuelRoom`.

- [ ] **Step 1: Add the type fields**

In `src/types/index.ts`, inside `ActiveGame` (after `underMaintenance`):

```ts
  /**
   * #184 Monkey Duel: a 1v1 race type. `requiredAmount` is the MINIMUM stake and `maximumStake` the
   * maximum, and both bound a PER-ROUND stake the two sides must match exactly.
   *
   * ⚠️ Unlike `isHouseEdge`, a duel DOES take a cut — `commissionPercentage` is the real rate (2
   * today), and the payout is `stake * (2 - rate)`, not `2 * stake * (1 - rate)`. See utils/duel.ts.
   *
   * ⚠️ A duel type is FREE-TEXT: `presetChoices` is always empty, which is what keeps
   * `resolveGameArchetype` from reading it as a preset race.
   *
   * Optional so an older payload — or a mock — is simply a non-duel type.
   */
  isDuel?: boolean;
```

Inside `GameInstance` (after `boardExpiresAt`):

```ts
  // #184: which kind of duel this ROUND is. Absent/null for every other type.
  duelKind?: 'House' | 'OpenRoom' | null;
  // #184: this round's OWN stake. The joiner (or the house) matches it exactly.
  stake?: number | null;
  // #184: the creator's wallet — so your own rooms render in your corner colour.
  ownerWallet?: string | null;
  // #184: both sides are in. A full room is off the board and can no longer be cancelled.
  isFull?: boolean;
```

Inside `GameAvailability`:

```ts
  /**
   * #184: the maximum stake the app may offer — the type's ceiling, or the HouseSeed wallet's clamp,
   * whichever is lower. ABSENT means "no clamp" (every non-duel type).
   *
   * ⚠️ `0` is a real answer, not a missing one: the wallet cannot cover even the minimum, so the VS
   * HOUSE tab is unavailable. Never treat it as falsy-means-unclamped.
   */
  maxStake?: number | null;
```

Inside `BodyGameAvailability`, loosen `gameId`:

```ts
  /**
   * Solo rounds: the player's own board (round id).
   *
   * ⚠️ OPTIONAL for a duel (#184): the app asks for the VS HOUSE ceiling on mount, before a round
   * exists. A house-edge type still needs it.
   */
  gameId?: string;
```

Inside `GameVerification`, after `boardChoices`:

```ts
  // #184 Monkey Duel: this round was a 1v1 race.
  isDuel?: boolean;
  duelKind?: 'House' | 'OpenRoom' | null;
  /**
   * #184: the ordinal-sorted pool this HOUSE round could have drawn from, as it was when the round
   * opened — comma-separated. Published so the draw reproduces even after the pool is edited.
   */
  duelPoolSnapshot?: string | null;
  // #184: the word the house actually drew. Null for a PVP room.
  houseWord?: string | null;
```

And two new exported interfaces beside `BoardChoice`:

```ts
/**
 * #184: one room on the OPEN DUELS board.
 *
 * ⚠️ `word` is PUBLIC by design (decision 11): the joiner picks second, seeing it. That advantage is
 * accepted, not mitigated.
 */
export interface DuelRoom {
  gameId: string;
  word: string;
  stake: number;
  displayName?: string | null;
  wallet?: string | null;
  createdDate: string;
}

/** #184: a live change on the game type's rooms channel. `room` is present only on 'opened'. */
export interface DuelRoomEvent {
  change: 'opened' | 'taken' | 'cancelled';
  room?: DuelRoom | null;
  gameId: string;
}
```

- [ ] **Step 2: Add the three service calls**

In `src/services/mainService.ts`, after `getHouseEdgeRound`:

```ts
// #184 Monkey Duel. A round is opened by an explicit call — never at startup, never by rollover — so
// the seed is committed before any bet and the house word cannot be ground. All three are
// rate-limited; none is under `/bets`, which the API's rate-limit guard forbids.
const openDuelRoom = async (gameTypeId: string, kind: 'House' | 'OpenRoom') => {
  const { data } = await mainAPI.post<DefaultResponse<GameInstance>>(`/games/${gameTypeId}/duels`, {
    kind,
  });
  return data;
};

// The public OPEN DUELS board: unmatched player-created rooms, newest first. pageSize is capped at 30
// server-side.
const getDuelRooms = async (gameTypeId: string, pageNumber = 1, pageSize = 30) => {
  const { data } = await mainAPI.get<DefaultResponse<PaginatedList<DuelRoom>>>(
    `/games/${gameTypeId}/duels`,
    { params: { pageNumber, pageSize } }
  );
  return data;
};

// Cancel an unmatched room and get the full stake back. A wallet proof is REQUIRED because this moves
// money — the same `WalletProof` primitive Orbit's cash-out uses. Without it the creator's wallet is
// public on the board, so anyone could cancel anyone's room.
const cancelDuelRoom = async (
  roundId: string,
  proof: { wallet: string; nonce: string; signature: string }
) => {
  const { data } = await mainAPI.post<DefaultResponse<boolean>>(
    `/games/rounds/${roundId}/duel-cancel`,
    proof
  );
  return data;
};
```

Export all three from the default object beside `openHouseEdgeBoard`.

⚠️ Match `PaginatedList<T>`'s real shape — grep `src/types/index.ts` for how `getGamesHistory` types its page and reuse that exact generic, rather than assuming `{ items, totalCount }`.

⚠️ **Do not** add a duel branch to `applyBet` or `checkGameAvailability`. Betting and availability reuse them as-is — `applyBet`'s central referral stamping must not be bypassed.

- [ ] **Step 3: Verify it compiles and lints**

Run: `cd C:\Users\kurok\Desktop\monkify\.worktrees\monkey-duel\monkify-front && yarn lint`
Expected: no errors.

- [ ] **Step 4: Commit and push**

```bash
git add src/types/index.ts src/services/mainService.ts
git commit -m "Type the duel contracts and call the three new duel endpoints."
git push -u origin feature/monkey-duel
```

---

## Task 2: `utils/duel.ts` — the payout formula and stake maths

**Files:**
- Create: `src/utils/duel.ts`

**Interfaces:**
- Produces: `duelPayout(stake, commissionPercentage)`, `duelHouseCut(stake, commissionPercentage)`, `duelPot(stake)`, `stepDuelStake(current, delta, min, max)`, `clampDuelStake(value, min, max)`, `sortDuelRooms(rooms, order)`, `type DuelSortOrder = 'newest' | 'stakeAsc' | 'stakeDesc'`.

This file exists so **one** place owns the corrected formula. Every screen that quotes a number reads it from here.

- [ ] **Step 1: Write the module**

`src/utils/duel.ts`:

```ts
/**
 * #184 Monkey Duel — the money maths, in ONE place.
 *
 * ⚠️ THE DESIGN ARTIFACT IS WRONG TWICE OVER, and this module is the correction (spec §1.2, §7.6):
 *
 *   1. It hard-codes `const CUT = 0.05`. The live rate is **2%**, read from
 *      `ActiveGame.commissionPercentage`. Never hard-code it anywhere.
 *   2. It computes `2 * stake * (1 - CUT)`. The live rule taxes only the profit ABOVE the winner's
 *      own stake, so the payout is `stake * (2 - rate)` — **1.98 × stake** at 2%, not 1.96 × stake.
 *
 * Both corrections are AC 15. `BetDomainService.CommissionForWinner` is the server-side source of
 * truth; KEEP-IN-SYNC with it.
 */

/** The two stakes on the table. */
export const duelPot = (stake: number): number => stake * 2;

/**
 * The rate as a fraction, defensively.
 *
 * ⚠️ A missing, zero or non-finite `commissionPercentage` means "we do not know the rate", and the
 * honest answer is to quote NO cut rather than invent one — a stale cached catalogue, a mock, or an
 * older payload must never make the app print a bigger payout than the server will pay. 0 is also a
 * legitimate value (a type that takes no cut), and both cases behave identically here.
 */
const rateOf = (commissionPercentage?: number | null): number => {
  if (typeof commissionPercentage !== 'number' || !Number.isFinite(commissionPercentage)) return 0;
  if (commissionPercentage <= 0) return 0;
  return commissionPercentage / 100;
};

/**
 * What the winner actually receives: the 2-stake pot, less the cut on the profit above their own
 * stake. `stake * (2 - rate)`.
 */
export const duelPayout = (stake: number, commissionPercentage?: number | null): number => {
  if (!Number.isFinite(stake) || stake <= 0) return 0;
  return stake * (2 - rateOf(commissionPercentage));
};

/** The house's share: the rate applied to `pot - stake`, which is exactly one stake of profit. */
export const duelHouseCut = (stake: number, commissionPercentage?: number | null): number => {
  if (!Number.isFinite(stake) || stake <= 0) return 0;
  return (duelPot(stake) - stake) * rateOf(commissionPercentage);
};

/**
 * Clamp a stake into the round's bounds.
 *
 * ⚠️ `max` of 0 is a REAL answer (the HouseSeed wallet is below the floor), not a missing one — it
 * collapses the range to nothing, which is what makes the tab unavailable. Never treat it as
 * "unclamped".
 */
export const clampDuelStake = (value: number, min: number, max: number): number => {
  if (!Number.isFinite(value)) return min;
  if (max < min) return 0;
  return Math.min(Math.max(value, min), max);
};

/**
 * One press of the − / + keys. Steps on the same 0.001 SOL grid the backend validates against
 * (`decimal.Round(amount, 9) == amount` with a 0.002 floor), and rounds to kill float drift —
 * `0.1 + 0.002` must not become `0.10200000000000001` and be refused as InvalidAmount after the SOL
 * has already moved.
 */
export const DUEL_STAKE_STEP = 0.001;

export const stepDuelStake = (current: number, delta: number, min: number, max: number): number => {
  const next = Number((current + delta).toFixed(9));
  return clampDuelStake(next, min, max);
};

export type DuelSortOrder = 'newest' | 'stakeAsc' | 'stakeDesc';

/**
 * The SORT key's three orders. Sorts a COPY — the board list is reactive and shared with the live
 * rooms channel, which appends to it.
 */
export const sortDuelRooms = <T extends { stake: number; createdDate: string }>(
  rooms: readonly T[],
  order: DuelSortOrder
): T[] => {
  const copy = [...rooms];
  if (order === 'stakeAsc') return copy.sort((a, b) => a.stake - b.stake);
  if (order === 'stakeDesc') return copy.sort((a, b) => b.stake - a.stake);
  return copy.sort((a, b) => Date.parse(b.createdDate) - Date.parse(a.createdDate));
};
```

- [ ] **Step 2: Sanity-check the formula by hand**

In a node REPL from the worktree, confirm the corrected numbers before any UI reads them:

```bash
node -e "const s=0.5,r=2/100;console.log('payout',s*(2-r),'cut',(2*s-s)*r,'artifact-wrong',2*s*(1-r))"
```
Expected: `payout 0.99 cut 0.01 artifact-wrong 0.98`. The first two are what the UI must print; the third is what the artifact would have printed.

- [ ] **Step 3: Lint and commit**

```bash
yarn lint
git add src/utils/duel.ts
git commit -m "Correct the duel payout formula and centralise the stake maths."
git push origin feature/monkey-duel
```

---

## Task 3: The rooms channel on the existing connection

**Files:**
- Modify: `src/hooks/useGameHub.ts`

**Interfaces:**
- Consumes: `DuelRoomEvent` (Task 1); backend Task 13's `JoinDuelBoard` / `LeaveDuelBoard` and the `{gameTypeId}/DuelBoard` event.
- Produces: `attachToDuelBoard(gameTypeId, handler)` / `detachFromDuelBoard()` on the `useGameHub` object.

- [ ] **Step 1: Add the attach/detach pair**

Copy `attachToChat` / `detachFromChat` verbatim and change the three names. Add module-level state beside `currentChatTypeId` / `boundChatEvent`:

```ts
// #184: the OPEN DUELS board the connection is subscribed to, so it can leave without re-deriving it.
const currentDuelBoardTypeId = ref<string | null>(null);
let boundDuelBoardEvent: string | null = null;
```

Then, beside the chat pair:

```ts
/**
 * Subscribe to a GAME TYPE's open-duels board (#184 §6.8).
 *
 * ⚠️ On the SAME single connection this tab already holds — no second HubConnection, no polling
 * (specs/unified-websocket-connection.md). The group is keyed on the TYPE, not a round, so the
 * subscription survives every room opening and closing and we join ONCE.
 *
 * A call for the board we are already on is a no-op, which is what keeps a round rollover from
 * re-subscribing.
 */
const attachToDuelBoard = async (
  gameTypeId: string,
  handler: (payload: string) => void
): Promise<boolean> => {
  if (currentDuelBoardTypeId.value === gameTypeId && boundDuelBoardEvent) return false;

  await detachFromDuelBoard();

  const live = await ensureConnected();
  if (!live) return false;

  await live.invoke('JoinDuelBoard', gameTypeId);

  const event = `${gameTypeId}/DuelBoard`;
  live.on(event, handler);
  boundDuelBoardEvent = event;
  currentDuelBoardTypeId.value = gameTypeId;
  return true;
};

/** Leave the board (a game-type switch). The SOCKET STAYS UP. */
const detachFromDuelBoard = async (): Promise<void> => {
  const live = connection.value;
  const typeId = currentDuelBoardTypeId.value;

  if (live && boundDuelBoardEvent) live.off(boundDuelBoardEvent);
  boundDuelBoardEvent = null;
  currentDuelBoardTypeId.value = null;

  if (!live || !typeId) return;
  try {
    await live.invoke('LeaveDuelBoard', typeId);
  } catch {
    // ignore — the group dies with the connection either way
  }
};
```

- [ ] **Step 2: Rejoin the board after a reconnect**

Find the reconnect handler that re-invokes `JoinChat` (around line 75, inside the `onreconnected` / connection-built block) and add the board rejoin next to it:

```ts
        // #184: the board group is lost with the old connection, exactly as the chat room is.
        if (currentDuelBoardTypeId.value) await built.invoke('JoinDuelBoard', currentDuelBoardTypeId.value);
```

⚠️ Without this, a reconnect leaves the board silently frozen — it stops receiving `opened` / `taken` / `cancelled` and the player stares at rooms that no longer exist. That is exactly the "no refetch, no poll" promise (AC 10) failing invisibly.

- [ ] **Step 3: Export them**

Add `attachToDuelBoard`, `detachFromDuelBoard` and `currentDuelBoardTypeId: computed(() => currentDuelBoardTypeId.value)` to the object returned by `useGameHub()`.

- [ ] **Step 4: Lint and commit**

```bash
yarn lint
git add src/hooks/useGameHub.ts
git commit -m "Subscribe the open-duels board to the existing hub connection."
git push origin feature/monkey-duel
```

---

## Task 4: `useDuel` — the machine's state

**Files:**
- Create: `src/hooks/useDuel.ts`
- Modify: `src/hooks/index.ts` (if it re-exports composables — check)

**Interfaces:**
- Consumes: `mainService.openDuelRoom/getDuelRooms/cancelDuelRoom/checkGameAvailability` (Task 1); `useGameHub.attachToDuelBoard` (Task 3); `sortDuelRooms`, `clampDuelStake`, `stepDuelStake` (Task 2).
- Produces: a composable returning `{ tab, setTab, rooms, sortedRooms, cursor, moveCursor, sortOrder, cycleSort, maxStake, stake, setStake, bumpStake, isHouseTabAvailable, loadBoard, openHouseRound, openOwnRoom, takeRoom, cancelRoom, isBusy, lastRefusal }`.

- [ ] **Step 1: Write the composable**

`src/hooks/useDuel.ts`. The pieces that matter, with their reasoning:

```ts
/**
 * #184 Monkey Duel — the machine's state: which tab, the open-rooms board, the cursor, the sort, the
 * clamped stake ceiling, and the open/join/cancel actions.
 *
 * ⚠️ The board is LIVE, not polled (AC 10). `loadBoard` fetches once; every change after that arrives
 * on the rooms channel. Do NOT add a `setInterval` refetch — the whole point of the per-type channel
 * is that the board is correct without one.
 */
```

**Tab state.** `const tab = ref<'house' | 'board'>('house')` — the artifact lands on VS HOUSE (decision 1). But:

```ts
/**
 * AC 5 — when the house tab is unavailable the machine LANDS ON THE BOARD instead. Applied once, on
 * the first availability answer, so a mid-session clamp does not yank the player off a tab they are
 * actively using.
 */
```

**The clamped ceiling.** On mount, call `checkGameAvailability(gameTypeId, { choice: '', amount: 0, gameId: undefined })` — the backend duel branch answers with no round id. Store `maxStake` and derive:

```ts
const isHouseTabAvailable = computed(() => availability.value?.available !== false && (maxStake.value ?? 0) >= minStake.value);
```

⚠️ **Review Focus 2**: `maxStake === 0` is a real answer, not a missing one. `clampDuelStake(value, min, 0)` returns 0, and `isHouseTabAvailable` must be `false` — never offer a 0 stake, never divide by it.

**The rooms list + the live channel.**

```ts
const rooms = ref<DuelRoom[]>([]);

const onRoomEvent = (payload: string) => {
  const event = JSON.parse(payload) as DuelRoomEvent;

  if (event.change === 'opened' && event.room) {
    // Guard against a duplicate: the initial fetch and the first live event can race.
    if (!rooms.value.some((x) => x.gameId === event.room!.gameId)) rooms.value.unshift(event.room);
    return;
  }

  // 'taken' and 'cancelled' both just remove the row.
  const removedIndex = rooms.value.findIndex((x) => x.gameId === event.gameId);
  if (removedIndex === -1) return;
  rooms.value.splice(removedIndex, 1);

  // ⚠️ REVIEW FOCUS 3 — the cursor may have been ON the row that just vanished, or past the new end
  // of the list. Clamp it to a valid neighbour rather than leaving it pointing at nothing, which
  // would make Enter take a room that is already gone.
  if (cursor.value >= sortedRooms.value.length) cursor.value = Math.max(0, sortedRooms.value.length - 1);
};
```

**Actions.** Each one sets `isBusy` while it runs and stores the server's message verbatim in `lastRefusal` on failure:

- `openHouseRound()` → `openDuelRoom(typeId, 'House')`, store the returned `GameInstance`.
- `openOwnRoom()` → `openDuelRoom(typeId, 'OpenRoom')`, then the caller bets into it.
- `takeRoom(room)` → bets into `room.gameId` at exactly `room.stake`.
- `cancelRoom(roundId)` → mint a `duel-cancel` nonce, sign it, `cancelDuelRoom(roundId, proof)`.

⚠️ For `cancelRoom`, **reuse the shipped wallet-proof helper**. Open `src/hooks/useOrbit.ts` and copy how it mints a nonce (`GET /players/wallet-nonce?purpose=…`), builds the canonical message and calls `signMessage`; change only the purpose to `duel-cancel` and the message to match backend `CancelDuelRoomHandler.BuildMessage`. Do not write a second signing path.

⚠️ Every refusal is surfaced **verbatim** — the server already sends the vague message plus the refund notice. Do not re-word it, do not add a client-side "because…".

- [ ] **Step 2: Lint and commit**

```bash
yarn lint
git add src/hooks/useDuel.ts src/hooks/index.ts
git commit -m "Hold the duel machine's tab, board, cursor and clamped ceiling in a composable."
git push origin feature/monkey-duel
```

---

## Task 5: Archetype, routing and round re-attachment

**Files:**
- Modify: `src/utils/gameArchetype.ts`
- Modify: `src/utils/gameArt.ts`, `src/components/game-selection/GameTypeCard.vue`
- Modify: `src/hooks/useGame.ts`
- Modify: `src/pages/GamePage.vue`, `src/components/game/GameConsole.vue`
- Add: `src/assets/types/monkeyduel-poster.png`, `src/assets/types/monkeyduel.gif`

**Interfaces:**
- Consumes: `ActiveGame.isDuel`, `GameInstance.duelKind` (Task 1).
- Produces: `GameArchetype` gains `'duel'` in **both** resolvers; `useGame` gains a duel round-loading branch and a `PLACING…` state.

- [ ] **Step 1: Add `'duel'` to the archetype resolver**

In `src/utils/gameArchetype.ts`:

```ts
export type GameArchetype = 'preset' | 'race' | 'lottery' | 'houseEdge' | 'duel';
```

and, **first** in `resolveGameArchetype` — before the house-edge check:

```ts
  // #184 Monkey Duel is checked FIRST, for the reason the house-edge check is: it is a free-text type
  // with a 6-letter word, so every later branch would read it as a plain race.
  if (game.isDuel) return 'duel';
```

- [ ] **Step 2: Add the duel poster**

⚠️ `src/utils/gameArt.ts` declares its **own**, differently-spelled `GameArchetype` (`'lottery' | 'hyper-roll' | 'preset' | 'race'`) — it answers a different question and must not be conflated with the resolver above. Add `'monkey-duel'` to **that** union, a branch keyed on `game.isDuel` **before** the house-edge branch, and the two imports:

```ts
import monkeyDuelPoster from '@/assets/types/monkeyduel-poster.png';
import monkeyDuelGif from '@/assets/types/monkeyduel.gif';
```

The art is two Edsons across a stepped red/blue split with the duel coin between them — red = you, blue = your rival. ⚠️ The coin is `monk-coin-spin.gif` / `monk-coin-main.png`, **never** `monk-coin.png` (retired red-rim art).

- [ ] **Step 3: Load a duel round in `useGame`**

Mirror the house-edge branch of the round-loading path: the round id is held by the browser and read back with `GET /games/{type}?wallet=`, which the backend now answers with the caller's **own** round (owner **or** claimer).

⚠️ **Review Focus 4**: on reload, the machine must land on the tab matching `round.duelKind` — `'House'` → the house tab, `'OpenRoom'` → the board (showing your own room, or the duel if it was taken). Landing on the default tab would show a player an empty house card while their real duel runs on the other tab.

Add the `PLACING…` state: after a creator bet on a House round, the round has one bet and no `claimedByPlayerId`; the house bet arrives over SignalR seconds later. Hold `PLACING…` until the round reports `isFull` or a status past `WaitingBets`.

⚠️ **Review Focus 5**: a refused bet must clear `PLACING…` and re-enable the lever, surfacing the server's message. A bet that is refunded leaves the lever stuck forever otherwise.

- [ ] **Step 4: Route the archetype**

In `GamePage.vue`, mount the duel machine when `resolveGameArchetype(selectedGame) === 'duel'`. In `GameConsole.vue`, route that archetype to `DuelConsole`.

- [ ] **Step 5: Lint, build, commit**

```bash
yarn lint && yarn build
git add src/utils src/hooks/useGame.ts src/pages/GamePage.vue src/components src/assets
git commit -m "Resolve the duel archetype and re-attach a reloading player to their own round."
git push origin feature/monkey-duel
```

---

## Task 6: The machine — tabs, card, board, console, splash

**Files:**
- Create: `src/components/game/DuelTabs.vue`, `DuelBoard.vue`, `DuelCard.vue`, `DuelConsole.vue`, `DuelVsSplash.vue`
- Modify: `src/components/game/GameTerminal.vue`

**Interfaces:**
- Consumes: `useDuel` (Task 4), `utils/duel` (Task 2).
- Produces: the rendered machine. Port the artifact's markup, tokens and sprites.

Build in this order, linting after each component:

- [ ] **Step 1: `DuelTabs.vue`** — the two CRT tabs inside the tube: `VS HOUSE` / `OPEN DUELS {n}`. `VS HOUSE` selected on load; the count is `rooms.length`, live. When the house tab is unavailable it renders disabled with the **existing vague** maintenance copy and the board is selected instead (AC 5).

- [ ] **Step 2: `DuelCard.vue`** — two corners either side of `VS`: who · word · stake. **Red = you, blue = the rival.** Your word is blank until typed; the house word stays blank until the pull. On a taken room both words show.

- [ ] **Step 3: `DuelBoard.vue`** — one card per room, showing **who · word · stake** only. The `+ OPEN YOUR OWN DUEL` tile is pinned first. Scrolls inside the CRT with a drawn scrollbar. **Your own rooms render in red.** Empty state: the tile alone, with a line inviting the player to post one. A `taken` event dims and removes the card.

  Keyboard, as the artifact's `aria-label` states: **arrows browse, Enter takes, Escape lets go.**

- [ ] **Step 4: `DuelConsole.vue`** — the two-row plates:
  - left plate row 1: the word LCD (6 chars, lowercase) + the `?` random-word key;
  - left plate row 2: `−` / stake LCD / `+`;
  - right plate row 1: the two-position slide switch (house ↔ board) + `UP` / `DOWN`;
  - right plate row 2: `SORT` / wide `OK` / `CLEAR`;
  - centre: the lever. Tag reads `READY` → `PULL` → `PLACING…` → `LIVE`.

  ⚠️ `+` and the LCD refuse above `min(maximumStake, availability.maxStake)` — the clamped ceiling from `useDuel`. The server backstop still refunds an over-stake that gets through (AC 4), but the app must not offer one.

  ⚠️ Quick-stake chips (`.10 / .20 / .50 / MAX`) are **out of scope** — cut with the two-row plate. Do not re-add them.

- [ ] **Step 5: `DuelVsSplash.vue`** — the `duelTimeUntilGameStarts` beat: both words, both stakes, the coin (`monk-coin-spin.gif`), a countdown.

- [ ] **Step 6: `GameTerminal.vue` — the duel screens and THE COPY CORRECTIONS (AC 15)**

This is the task's highest-risk step. Every line below is hard-coded in the artifact and **must** read the live rate instead:

| Artifact | Must become |
| --- | --- |
| `const CUT = 0.05` | `selectedGame.commissionPercentage / 100`, via `utils/duel` |
| `winOf = (stake) => 2 * stake * (1 - CUT)` | `duelPayout(stake, commissionPercentage)` = `stake * (2 - rate)` |
| `> WINNER TAKES THE POT, LESS 5%` | `… LESS {rate}%` |
| `> POT {pot} · HOUSE CUT {pot * CUT} (5%) · SEED …` | `HOUSE CUT {duelHouseCut(stake, rate)} ({rate}%)` |
| `WINNER TAKES {winOf} SOL` | recompute with `duelPayout` |

`> HOUSE WORD DRAWN FROM THE SEED AT THE PULL.` and `> INSTANT DUEL · NOBODY TO WAIT FOR` are accurate and stay.

Reuse unchanged: `useNearMiss` for the near-miss dock + tug bar (two rows, one per word, in the corner colours), `HyperRollWinFx` re-coloured per corner for the win/lose banner, and the shipped `#183` `HOW TO PLAY ?` key.

- [ ] **Step 7: Grep for any surviving hard-coded rate**

```bash
grep -rn "0\.05\|LESS 5%\|(5%)\|2 \* stake \* (1" src/components/game src/utils/duel.ts
```
Expected: **no hits.** Any hit is AC 15 failing.

- [ ] **Step 8: Lint, build, commit**

```bash
yarn lint && yarn build
git add src/components/game
git commit -m "Build the duel machine and correct the artifact's 5% copy to the live rate."
git push origin feature/monkey-duel
```

---

## Task 7: Offline verification of the house word

**Files:**
- Modify: `src/utils/provablyFair.ts`
- Modify: `public/verify.html`

**Interfaces:**
- Consumes: backend `DuelHouseWordGenerator` (backend Task 2) — mirror it byte-for-byte.
- Produces: `deriveDuelHouseWord(serverSeed, gameId, attempt, pool)` in both files.

- [ ] **Step 1: Add the derivation to `provablyFair.ts`**

```ts
/**
 * #184 Monkey Duel: the HOUSE word for one draw attempt.
 * KEEP-IN-SYNC with backend DuelHouseWordGenerator.DeriveWord and public/verify.html.
 *
 *   prefix = `duel|${gameId}|${attempt}`   (gameId in "D" form, lowercase-hyphenated)
 *   word   = pool.sort(ORDINAL)[nextIndex(keystream(serverSeed, prefix), pool.length)]
 *
 * ⚠️ ORDINAL sort, not the published order — EF yields rows in whatever order it likes, so the
 * backend sorts before drawing and a verifier must too.
 */
export async function deriveDuelHouseWord(
  serverSeed: string,
  gameId: string,
  attempt: number,
  pool: readonly string[]
): Promise<string> {
  const ordered = [...pool].sort();   // JS default sort IS ordinal on ASCII words
  const keystream = new Keystream(serverSeed, `duel|${gameId}|${attempt}`);
  return ordered[await nextIndex(keystream, ordered.length)];
}
```

⚠️ Verify the `gameId` string form matches the backend's `{gameId:D}` exactly (lowercase, hyphenated). A casing mismatch silently produces a different word and the verifier reports a false failure.

- [ ] **Step 2: Use it in `verifyRound`**

For a round where `isDuel && duelKind === 'House'`: split `duelPoolSnapshot` on `,`, take attempts `0, 1, 2 …` and return the **first derived word that differs from the creator's word** — that is the house word, and it must equal the published `houseWord`. Nothing else can make an attempt fail (Orbit claims cannot exist on a duel type), which is precisely why the draw stays reproducible with no extra persisted state.

- [ ] **Step 3: Mirror it in `public/verify.html`**

`verify.html` is a standalone file with its own copy of the primitives. Add the same function there, with the same KEEP-IN-SYNC comment pointing both ways.

- [ ] **Step 4: Lint, build, commit**

```bash
yarn lint && yarn build
git add src/utils/provablyFair.ts public/verify.html
git commit -m "Reproduce the duel house word offline from the revealed seed."
git push origin feature/monkey-duel
```

---

## Task 8: Manual QA against the local stack

**Files:** none — this is verification.

⚠️ No test framework exists in this repo, so this task **is** the frontend's test suite. Do not skip it.

- [ ] **Step 1: Bring the stack up**

```bash
cd C:\Users\kurok\Desktop\monkify\monkify-backend
docker compose up -d --build
curl http://localhost:6001/health          # "Api running!"
```

⚠️ If the backend worktree has uncommitted duel code, rebuild only the two app containers so the database and its volume survive:
`docker compose --project-name monkify-backend --no-deps up -d --build api consumers`

- [ ] **Step 2: Serve the front from the worktree**

Copy the hub's `.env.local` and `yarn.lock` into the worktree (both are gitignored; without `.env.local` the app shows "game unavailable"), then:

```bash
cd C:\Users\kurok\Desktop\monkify\.worktrees\monkey-duel\monkify-front
yarn install --frozen-lockfile
yarn serve --port 8082
```

⚠️ **Verify the port is actually free first.** A port held by another worktree's server fails silently and you end up testing the wrong app. Confirm by grepping the served bundle for a symbol only this branch has (e.g. `deriveDuelHouseWord`).

- [ ] **Step 3: Seed a playable duel type locally**

The shipped type is `ComingSoon: true` with an **empty** pool (decision 10). For QA only, in **local config**, set `ComingSoon: false` and give `Duel.Words` 5–10 words. ⚠️ Revert both before committing — the real pool waits on the Monte Carlo.

- [ ] **Step 4: Walk the spec's Manual QA list (§12)**

- [ ] `/game-select` → the duel card renders with the red/blue poster.
- [ ] The game page opens on **VS HOUSE**; the stake LCD's ceiling is the **clamped** one.
- [ ] Free bets are on outside production: type a word, pull the lever → the house places a **0-amount** counterparty bet with no transfer, the VS splash plays, the stream runs, a winner is decided.
- [ ] With a funded mock HouseSeed wallet on devnet, repeat paid: check the memo `duel:{gameId}`, the payout **`stake × 1.98`**, and **one** `Credit` ledger row.
- [ ] **OPEN DUELS** → post a room → open a second browser profile with a different wallet → the room appears **without a refresh** → take it → both browsers see the duel start.
- [ ] Post a room, then cancel it → one wallet signature → the room disappears **in the other browser** and the SOL returns.
- [ ] Kill the HouseSeed wallet's balance → reload → the VS HOUSE tab is unavailable and the machine **lands on the board**.
- [ ] Open the verification page for a settled house duel → recompute the house word offline from the revealed seed, the game id, the pool snapshot and the creator's word.
- [ ] **Every screen that quotes the cut reads 2%, never 5%**, and the payout line reads `1.98 × stake`, never `1.96 × stake` (AC 15).
- [ ] Console is clean — 0 errors.

- [ ] **Step 5: Revert the QA-only config**

```bash
git -C C:\Users\kurok\Desktop\monkify\.worktrees\monkey-duel\monkify-backend diff -- "*spacestrokersettings.json"
```
Confirm the seeded duel type is back to `"ComingSoon": true` with `"Words": []`.

- [ ] **Step 6: Kill the dev server cleanly**

Kill orphan node/esbuild children afterwards, or `git worktree remove` fails on Windows.

---

## Final verification (before hand-off)

- [ ] `yarn lint` — clean.
- [ ] `yarn build` — succeeds.
- [ ] `grep -rn "0\.05\|LESS 5%\|(5%)" src/components/game` — no hits (AC 15).
- [ ] `grep -rn "monk-coin\.png" src/` — no hits (retired art).
- [ ] `grep -rn "setInterval" src/hooks/useDuel.ts` — no hits (AC 10: the board is live, not polled).
- [ ] `git log --oneline origin/main..feature/monkey-duel` — every task committed and pushed.
- [ ] Every Manual QA box in Task 8 ticked, in a real browser.

---

## Self-review notes

**Spec coverage.** §10.2's 17-file checklist maps to: files 1–2 → Task 1; 3 → Task 4; 4 → Task 3; 5 → Task 5; 6 → Task 5; 7 → Task 5; 8–12 → Task 6; 13–14 → Task 6; 15 → Task 5; 16 → Task 7; 17 → Task 5. §7.6's copy-correction table is Task 6 Step 6 and is grepped for in Step 7. §7.7's seven states are covered: unavailable house tab (Tasks 4 + 6 Step 1), clamped ceiling (Tasks 2 + 6 Step 4), empty board and room-taken (Task 6 Step 3), cancel (Task 4), house-bet-failed (Task 5's `PLACING…` timeout → offer cancel), `comingSoon` (already shipped by #168 — verify, do not rebuild). §12's Manual QA is Task 8.

**Deliberate non-goals.** Quick-stake chips, twin typers, blind words, auto-expiry, Orbit on duels, and any change to Hyper Roll are all out of scope (§8).

**Known gaps handed to the executor.** Three shapes must be matched against real code rather than assumed: `PaginatedList<T>`'s generic in `src/types` (Task 1), the wallet-proof minting/signing helper in `useOrbit.ts` (Task 4), and `{gameId:D}`'s exact string casing (Task 7). The duel poster art (Task 5) does not exist yet and must be produced or requested before that task can finish.

**Ordering.** Tasks 1–3 are independent. Task 4 needs 1–3. Task 5 needs 1 and 4. Task 6 needs 2, 4, 5. Task 7 needs only 1. Task 8 needs everything **and** the backend plan's Tasks 7–17.

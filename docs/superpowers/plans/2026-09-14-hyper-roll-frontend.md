# Hyper Roll — Frontend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render Hyper Roll in `monkify-front` exactly as mockup **A6**: today's terminal with per-word multiplier/chance/win lines, the stake typed in the console visor with bump chips, multiplier tags on the numbered keys, and a weighted-draw-aware provably-fair verifier.

**Architecture:** Everything keys off the catalogue flag `ActiveGame.isHouseEdge` and the instance's `boardChoices`, so other game types render byte-identically. Pure helpers live in a new `utils/houseEdge.ts`; the weighted draw joins the KEEP-IN-SYNC triad (`provablyFair.ts`, `public/verify.html`, backend `ProvablyFair.cs`); stake and payout state live in `useGame` module scope beside the existing per-round state.

**Tech Stack:** Vue 3 `<script setup>` + TypeScript, Vue CLI (webpack), Tailwind, WebCrypto.

**Spec:** `monkify-meta/specs/hyper-roll.md` §7 (as amended for A6). Backend contract: `monkify-meta/docs/superpowers/plans/2026-09-14-hyper-roll-backend.md` Task 7 and Task 9.

## Global Constraints

- Work only in `E:\Monkify\.worktrees\hyper-roll\monkify-front` on `feature/hyper-roll`.
- `monkify-front` has **no unit-test runner** (no jest/vitest). Verification is: the KAT command in Task 2, `eslint` on changed files, `yarn build`, and the browser QA in Task 7.
- Resolve by SHAPE (`isHouseEdge`, `boardChoices`), never by game name or `gameTypeId`.
- Wire contract from the backend (camelCase): `ActiveGame.{isHouseEdge?, maximumStake?, rtpPercent?}`; `GameInstance.boardChoices?: [{ choice, multiplier }] | null` in key order; `GameResultPayload.payouts?: [{ wallet, amount }]` on the reward-completed frame; `GameVerification.{isHouseEdge?, boardChoices?: [{ choice, multiplier, weight }]}` ordered by choice (ordinal).
- Weight formula: `Math.floor(100_000_000 / Math.round(multiplier * 100))`.
- Hot yellow is the terminal's existing `.result-win` token (`#fde047` + glow); do not add a new colour.
- Client-side stake refusal copy stays vague: `That stake could not be accepted.`
- Before building: junction the hub `node_modules` and copy the hub `yarn.lock` (both gitignored in the worktree; the lockfile gotcha boots a stale image otherwise).
- Commits end with:
  ```
  Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_016QNAKdJo28eauNmES75WZr
  ```
- Push after every commit: `git push origin feature/hyper-roll`.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `src/types/index.ts` | Wire types |
| `src/utils/houseEdge.ts` (new) | Stake parsing, chance, payout, multiplier formatting, top tier |
| `src/utils/provablyFair.ts` | `weightForMultiplier`, `weightedFallbackIndex`, house-edge verify branch |
| `public/verify.html` | Offline copy of the same |
| `src/components/game/GameFairPanel.vue` | Forward board to the worker; "WEIGHTED DRAW" label |
| `src/hooks/useGame.ts` | Stake + payouts state, stake-aware bet, near-miss off |
| `src/components/game/HyperRollStakeChips.vue` (new) | `+0.01 / +0.05 / +0.1 / MAX` |
| `src/components/game/GameConsole.vue` | Board-driven keys with tags, stake visor, chips in the desktop plate |
| `src/pages/GamePage.vue` | Chips row above the phone cabinet |
| `src/components/game/GameTerminal.vue` | A6 terminal lines |
| `src/components/game/GameStatsPanel.vue` | RTP badge |
| `src/components/game-selection/GameTypeCard.vue` | "From <min>" |
| `src/utils/gameArt.ts`, `src/utils/gameArchetype.ts` | Shape resolution |
| `src/components/guide/GuideModeCards.vue`, `src/components/guide/sections/GuideWhatYouWin.vue` | Manual copy |

---

### Task 0: Worktree tooling

- [ ] **Step 1: Link dependencies and the lockfile**

From `E:\Monkify\.worktrees\hyper-roll\monkify-front`:

```bash
cmd //c "mklink /J node_modules ..\..\..\monkify-front\node_modules"
cp ../../../monkify-front/yarn.lock ./yarn.lock
git status --short
```

Expected: `git status` shows nothing new (both paths are gitignored). If `yarn.lock` appears as untracked, do **not** add it.

---

### Task 1: Types and house-edge helpers

**Files:**
- Modify: `src/types/index.ts`
- Create: `src/utils/houseEdge.ts`

**Interfaces:**
- Produces: types `BoardChoice`, `GameResultPayout`, `VerificationBoardChoice`; `isHouseEdgeGame(game)`, `winChancePercent(board, multiplier)`, `formatChance(percent)`, `payoutFor(stake, multiplier)`, `isTopMultiplier(board, multiplier)`, `formatMultiplier(multiplier)`, `parseStake(raw, game)` from `@/utils/houseEdge`.
- Consumes: `weightForMultiplier` from `@/utils/provablyFair` (added in Task 2 — do Task 2 Step 3 first if implementing out of order), `toLamportGrid` from `@/utils/money`.

- [ ] **Step 1: Add the wire types**

In `src/types/index.ts`, inside `interface ActiveGame` after `commissionPercentage: number;` add:

```ts
  /**
   * #183 Hyper Roll: a player-vs-house preset type. `requiredAmount` is the MINIMUM stake and
   * `maximumStake` the maximum; `commissionPercentage` is 0 because the edge is in the multipliers.
   * Optional so an older payload — or a mock — is simply a non-house-edge type.
   */
  isHouseEdge?: boolean;
  maximumStake?: number | null;
  rtpPercent?: number | null;
```

Before `export interface GameInstance {` add:

```ts
/** #183 Hyper Roll: one word on the round's board. `boardChoices` arrive in KEY order (key 1 first). */
export interface BoardChoice {
  choice: string;
  multiplier: number;
}
```

Inside `interface GameInstance`, after `serverSeedHash?: string;` add:

```ts
  // #183 Hyper Roll: this round's 8 words in key order. Absent/null for every other type.
  boardChoices?: BoardChoice[] | null;
```

Inside `interface GameResultPayload`, after `netRewardPerWinner?: number | null;` add:

```ts
  /**
   * #183 Hyper Roll: what each PAID winner actually received (stake × multiplier). Only on the
   * reward-completed frame of a house-edge round; `netRewardPerWinner` is absent there.
   */
  payouts?: GameResultPayout[] | null;
```

After `interface GameResultPayload`'s closing brace add:

```ts
export interface GameResultPayout {
  wallet: string;
  amount: number;
}
```

Inside `interface GameVerification`, after `bets: GameVerificationBet[];` add:

```ts
  // #183 Hyper Roll: the winner is the weighted draw over boardChoices (ordered by choice, ordinal).
  isHouseEdge?: boolean;
  boardChoices?: VerificationBoardChoice[];
```

After `interface GameVerification`'s closing brace add:

```ts
export interface VerificationBoardChoice {
  choice: string;
  multiplier: number;
  weight: number;
}
```

- [ ] **Step 2: Create the helpers**

Create `src/utils/houseEdge.ts`:

```ts
// #183 Hyper Roll helpers. Pure functions over the catalogue + instance payloads, so the console, the
// terminal and the manual can never compute a chance or a payout differently.
//
// ⚠️ DISPLAY ONLY. The backend decides the stake bounds, the winner and the payout; these numbers only
// describe them. Chance uses the SAME integer weights the draw uses (provablyFair.weightForMultiplier),
// so the percentage on screen is the real one, not the rounded `RTP ÷ multiplier` approximation.
import { ActiveGame, BoardChoice } from '@/types';
import { weightForMultiplier } from '@/utils/provablyFair';
import { toLamportGrid } from '@/utils/money';

export const isHouseEdgeGame = (game?: ActiveGame | null): boolean => game?.isHouseEdge === true;

export const winChancePercent = (board: BoardChoice[], multiplier: number): number => {
  const total = board.reduce((sum, entry) => sum + weightForMultiplier(entry.multiplier), 0);
  return total > 0 ? (weightForMultiplier(multiplier) / total) * 100 : 0;
};

/** One decimal, truncated: 9.4987 → "9.4%", never rounded up past the real chance. */
export const formatChance = (percent: number): string =>
  `${(Math.floor(percent * 10) / 10).toFixed(1)}%`;

/** stake × multiplier on the lamport grid. `toFixed(12)` strips float noise before truncating. */
export const payoutFor = (stake: number, multiplier: number): number =>
  toLamportGrid(Number((stake * multiplier).toFixed(12)));

export const isTopMultiplier = (board: BoardChoice[], multiplier: number): boolean =>
  board.length > 0 && multiplier === Math.max(...board.map((entry) => entry.multiplier));

export const formatMultiplier = (multiplier: number): string => `${Number(multiplier.toFixed(2))}×`;

/**
 * The typed stake as a number, or null when it is empty, not a plain decimal, finer than the asset's
 * decimals, or outside [requiredAmount, maximumStake]. Null for any non-house-edge type.
 */
export const parseStake = (raw: string, game?: ActiveGame | null): number | null => {
  if (!game?.isHouseEdge) return null;

  const text = raw.trim().replace(',', '.');
  if (!/^\d+(\.\d+)?$/.test(text)) return null;

  const decimals = game.tokenDecimals ?? 9;
  const fraction = text.split('.')[1] ?? '';
  if (fraction.length > decimals) return null;

  const value = Number(text);
  const min = game.requiredAmount;
  const max = game.maximumStake ?? min;
  if (value < min || value > max) return null;

  return value;
};
```

- [ ] **Step 3: Commit (after Task 2 Step 3 exists, so the import resolves)**

```bash
git add src/types/index.ts src/utils/houseEdge.ts
git commit -m "Add Hyper Roll wire types and display helpers."
git push origin feature/hyper-roll
```

---

### Task 2: Weighted draw in the verifier triad

**Files:**
- Modify: `src/utils/provablyFair.ts`
- Modify: `public/verify.html`
- Modify: `src/components/game/GameFairPanel.vue`

**Interfaces:**
- Produces: `weightForMultiplier(multiplier: number): number`; `weightedFallbackIndex(serverSeed: string, prefix: string, weights: number[]): Promise<number>`; `VerificationPayload.{isHouseEdge?, boardChoices?}`.

- [ ] **Step 1: Write the failing known-answer check**

Run from the front worktree:

```bash
npx --yes tsx -e "import('./src/utils/provablyFair.ts').then(async (m) => console.log(await m.weightedFallbackIndex('4f3c2a1b0e9d8c7b6a5f4e3d2c1b0a99887766554433221100ffeeddccbbaa00', '11111111-1111-1111-1111-111111111111|seedA|seedB', [100000, 250000, 111111, 125000, 100000, 166666, 100000, 100000])))"
```

Expected: FAILS with `m.weightedFallbackIndex is not a function`.

- [ ] **Step 2: Document the algorithm in the header**

In `src/utils/provablyFair.ts`, after the line `//   fallbackIndex(N)= same rejection over bytes of HMAC(serverSeed, prefix + "|fallback")` add:

```ts
//   weight(m)       = floor(100000000 / round(m × 100))                     // #183 house-edge only
//   weightedIndex   = T = Σ weights; limit = 2^32 − (2^32 mod T); block k: HMAC(serverSeed,
//                     prefix + "|fallback" + (k ? "|" + k : "")) read as big-endian u32s; first
//                     u < limit → r = u mod T → first index whose running weight exceeds r
```

- [ ] **Step 3: Implement the primitives**

After the `fallbackIndex` function add:

```ts
/**
 * #183 Hyper Roll draw weight for a multiplier. Integer, so C#, TypeScript and verify.html agree exactly.
 * KEEP-IN-SYNC with backend HouseEdgeRules.WeightFor and public/verify.html.
 */
export function weightForMultiplier(multiplier: number): number {
  return Math.floor(100_000_000 / Math.round(multiplier * 100));
}

/**
 * #183 Hyper Roll weighted fallback draw. KEEP-IN-SYNC with backend ProvablyFair.WeightedFallbackIndex
 * and public/verify.html. Frozen vector: see ProvablyFairTests V3 (→ index 5).
 */
export async function weightedFallbackIndex(
  serverSeed: string,
  prefix: string,
  weights: number[]
): Promise<number> {
  const total = weights.reduce((sum, weight) => sum + weight, 0);
  if (!weights.length || weights.some((weight) => weight <= 0)) {
    throw new Error('weights must be a non-empty list of positive integers');
  }

  const twoPow32 = 4294967296;
  const limit = twoPow32 - (twoPow32 % total);

  for (let block = 0; ; block++) {
    const label = prefix + '|fallback' + (block === 0 ? '' : '|' + block);
    const bytes = await hmacSha256Bytes(serverSeed, label);

    for (let i = 0; i + 3 < bytes.length; i += 4) {
      const u = bytes[i] * 16777216 + (bytes[i + 1] << 16) + (bytes[i + 2] << 8) + bytes[i + 3];
      if (u >= limit) continue;

      let r = u % total;
      for (let index = 0; index < weights.length; index++) {
        if (r < weights[index]) return index;
        r -= weights[index];
      }
    }
  }
}
```

- [ ] **Step 4: Run the known-answer check**

Run the Step 1 command again.
Expected: prints `5`.

- [ ] **Step 5: Teach `verifyRound` the house-edge branch**

In `interface VerificationPayload`, after `charactersTyped?: number;` add:

```ts
  /** #183 Hyper Roll: the winner is the weighted draw over boardChoices, not the character stream. */
  isHouseEdge?: boolean;
  boardChoices?: { choice: string; multiplier: number; weight: number }[];
```

In `verifyRound`, right after `const keystream = new Keystream(payload.serverSeed, prefix);` add:

```ts
  // #183 Hyper Roll: natural matching is disabled and the run always reaches the cap, so the winner does
  // not depend on the stream at all — skip the replay and reproduce the weighted draw directly. Weights are
  // RE-DERIVED from the published multipliers rather than trusted from the payload.
  if (payload.isHouseEdge && payload.boardChoices?.length) {
    const board = payload.boardChoices;
    const index = await weightedFallbackIndex(
      payload.serverSeed,
      prefix,
      board.map((entry) => weightForMultiplier(entry.multiplier))
    );
    const recomputedWinner = board[index].choice;
    if (onProgress) onProgress(payload.maxCharacters);
    const hashOk = (await sha256Hex(payload.serverSeed)) === payload.serverSeedHash;
    return { ok: hashOk && recomputedWinner === payload.winningChoice, recomputedWinner, hashOk };
  }
```

- [ ] **Step 6: Mirror it in the offline verifier**

In `public/verify.html`:

1. After the inline `fallbackIndex` function add:

```js
      // #183 Hyper Roll — KEEP-IN-SYNC with src/utils/provablyFair.ts and backend ProvablyFair.cs.
      function weightForMultiplier(multiplier) {
        return Math.floor(100000000 / Math.round(multiplier * 100));
      }

      async function weightedFallbackIndex(serverSeed, prefix, weights) {
        const total = weights.reduce((sum, weight) => sum + weight, 0);
        const twoPow32 = 4294967296;
        const limit = twoPow32 - (twoPow32 % total);
        for (let block = 0; ; block++) {
          const label = prefix + '|fallback' + (block === 0 ? '' : '|' + block);
          const bytes = await hmacSha256Bytes(serverSeed, label);
          for (let i = 0; i + 3 < bytes.length; i += 4) {
            const u = bytes[i] * 16777216 + (bytes[i + 1] << 16) + (bytes[i + 2] << 8) + bytes[i + 3];
            if (u >= limit) continue;
            let r = u % total;
            for (let index = 0; index < weights.length; index++) {
              if (r < weights[index]) return index;
              r -= weights[index];
            }
          }
        }
      }
```

2. In the inline `verifyRound`, right after `const keystream = new Keystream(payload.serverSeed, prefix);` add:

```js
        if (payload.isHouseEdge && payload.boardChoices && payload.boardChoices.length) {
          const board = payload.boardChoices;
          const index = await weightedFallbackIndex(
            payload.serverSeed,
            prefix,
            board.map((entry) => weightForMultiplier(entry.multiplier))
          );
          const recomputedWinner = board[index].choice;
          const hashOk = (await sha256Hex(payload.serverSeed)) === payload.serverSeedHash;
          const ok = hashOk && recomputedWinner === payload.winningChoice;
          // The stream does not pick a Hyper Roll winner, so there is no run-up worth downloading.
          return { ok, recomputedWinner, hashOk, typed: payload.maxCharacters, tail: '', weighted: true };
        }
```

3. Replace `(r.typed >= payload.maxCharacters ? 'FALLBACK DRAW' : 'NATURAL MATCH') +` with:

```js
                (payload.isHouseEdge ? 'WEIGHTED DRAW' : r.typed >= payload.maxCharacters ? 'FALLBACK DRAW' : 'NATURAL MATCH') +
```

4. Replace `downloadEl.hidden = false;` with `downloadEl.hidden = !r.tail;`

5. Directly before `lines.push(r.ok ? '> RESULT: VERIFIED ✅` add (spec §7.6 — say why there is no download):

```js
          if (r.weighted) {
            lines.push('> WEIGHTED DRAW: the winner does not depend on the character stream, so there is no run-up to download.');
            lines.push('');
          }
```

- [ ] **Step 7: Forward the board to the worker and label the draw**

In `src/components/game/GameFairPanel.vue`:

1. After the line `orderedChoices: [...(raw.orderedChoices ?? [])],` add:

```ts
      // #183 Hyper Roll — plain copies, for the same structured-clone reason as the arrays above.
      isHouseEdge: raw.isHouseEdge === true,
      boardChoices: (raw.boardChoices ?? []).map((entry) => ({ ...entry })),
```

2. In `decidedByLabel`, before `const fallback = payload.value?.winnerDecidedByFallback;` add:

```ts
    if (payload.value?.isHouseEdge) return 'WEIGHTED DRAW';
```

- [ ] **Step 8: Verify the offline copy by hand**

```bash
node -e "console.log(require('crypto').createHash('sha256').update('4f3c2a1b0e9d8c7b6a5f4e3d2c1b0a99887766554433221100ffeeddccbbaa00').digest('hex'))"
```

Open `public/verify.html` in a browser, paste this JSON with `serverSeedHash` set to the printed hash, and click verify:

```json
{
  "gameId": "11111111-1111-1111-1111-111111111111",
  "serverSeed": "4f3c2a1b0e9d8c7b6a5f4e3d2c1b0a99887766554433221100ffeeddccbbaa00",
  "serverSeedHash": "<printed hash>",
  "winningChoice": "rocket",
  "characterPool": "abcdeghiklmnoprstuwy",
  "queueLength": 8,
  "maxCharacters": 1,
  "charactersTyped": 1,
  "orderedBetSeeds": ["seedA", "seedB"],
  "orderedChoices": ["asteroid", "banana", "capsule", "eclipse", "mandrill", "rocket", "starship", "wormhole"],
  "isHouseEdge": true,
  "boardChoices": [
    { "choice": "asteroid", "multiplier": 10, "weight": 100000 },
    { "choice": "banana", "multiplier": 4, "weight": 250000 },
    { "choice": "capsule", "multiplier": 9, "weight": 111111 },
    { "choice": "eclipse", "multiplier": 8, "weight": 125000 },
    { "choice": "mandrill", "multiplier": 10, "weight": 100000 },
    { "choice": "rocket", "multiplier": 6, "weight": 166666 },
    { "choice": "starship", "multiplier": 10, "weight": 100000 },
    { "choice": "wormhole", "multiplier": 10, "weight": 100000 }
  ]
}
```

Expected: `RECOMPUTED WINNER: rocket`, `decided by: WEIGHTED DRAW`, the `> WEIGHTED DRAW:` note, `RESULT: VERIFIED ✅`, no download button.

- [ ] **Step 9: Lint and commit (includes Task 1)**

```bash
npx eslint --ext .ts,.vue src/utils/provablyFair.ts src/utils/houseEdge.ts src/types/index.ts src/components/game/GameFairPanel.vue
git add src/utils/provablyFair.ts public/verify.html src/components/game/GameFairPanel.vue src/types/index.ts src/utils/houseEdge.ts
git commit -m "Verify Hyper Roll rounds by the weighted draw in the app and offline."
git push origin feature/hyper-roll
```

Expected: eslint reports no errors in these files (a `Delete ␍` prettier message is the known Windows CRLF false alarm — only fix it if the file was LF before your edit).

---

### Task 3: Stake and payout state in `useGame`

**Files:**
- Modify: `src/hooks/useGame.ts`

**Interfaces:**
- Consumes: `parseStake` (Task 1), `GameResultPayout` (Task 1).
- Produces (returned from `useGame()`): `stakeAmount: Ref<string>`, `stakeValue: ComputedRef<number | null>`, `bumpStake(step: number): void`, `setMaxStake(): void`, `myPayout: ComputedRef<number | null>`.

- [ ] **Step 1: Imports and module state**

Change the `@/types` import to include `GameResultPayout`, and add after `import { formatPayout } from '@/utils/money';`:

```ts
import { parseStake } from '@/utils/houseEdge';
```

After the `netRewardPerWinner` declaration add:

```ts
/**
 * #183 Hyper Roll: the stake typed in the console visor, as TEXT (the visor is a text input). Cleared on
 * rollover with the other per-round state (spec §7.3), so a stake is always chosen for the round it pays.
 */
const stakeAmount = ref('');
/**
 * #183 Hyper Roll: what each PAID winner received, from the reward-completed frame. Cleared on rollover
 * with the other per-round result state.
 */
const payouts = ref<GameResultPayout[]>([]);
```

- [ ] **Step 2: Near-miss is off for house-edge rounds**

In `recordOwnChoice`, replace `nearMiss.trackChoice(choice, L);` with:

```ts
  // #183 Hyper Roll: no word is ever typed out naturally, so a near-miss row would track an unreachable event.
  if (!selectedGame.value?.isHouseEdge) nearMiss.trackChoice(choice, L);
```

In `onTerminal`, replace `nearMiss.feed(data);` with:

```ts
      if (!selectedGame.value?.isHouseEdge) nearMiss.feed(data);
```

- [ ] **Step 3: Derived stake and own payout**

Directly before `const myWinningWalletBet = computed(() => {` add:

```ts
  /** #183 Hyper Roll: the typed stake when it is valid for this type, else null. */
  const stakeValue = computed(() => parseStake(stakeAmount.value, selectedGame.value));

  /** #183 Hyper Roll: this player's payout from the reward-completed frame, or null if they were not paid. */
  const myPayout = computed(() => {
    const me = currentPlayerId.value;
    if (!me) return null;
    return payouts.value.find((payout) => payout.wallet === me)?.amount ?? null;
  });
```

In `maybeFireWinToast`, replace `const net = netRewardPerWinner.value;` with:

```ts
    const net = selectedGame.value?.isHouseEdge ? myPayout.value : netRewardPerWinner.value;
```

- [ ] **Step 4: Capture payouts; report the real stake when embedded**

In `onStatus`, directly after `netRewardPerWinner.value = statusData.result.netRewardPerWinner ?? null;` add:

```ts
        payouts.value = statusData.result.payouts ?? [];
```

In the `postToParent('bet-settled', { ... })` call, replace `amount: selectedGame.value?.requiredAmount ?? 0,` with:

```ts
            amount: selectedGame.value?.isHouseEdge
              ? gameInstance.value?.bets?.find((b) => b.externalPlayerId === currentPlayerId.value)
                  ?.amount ?? 0
              : selectedGame.value?.requiredAmount ?? 0,
```

In the rollover reset block, after `netRewardPerWinner.value = null;` add:

```ts
    payouts.value = []; // #183 — per-round, like the net figure above
    stakeAmount.value = ''; // #183 — spec §7.3: the stake is chosen per round
```

- [ ] **Step 5: Stake-aware bet**

In `bet()`, directly before the comment line `// ⚠️ THE BALANCE ON SCREEN IS ALSO THE GAS.` add:

```ts
    // #183 Hyper Roll: the stake is the player's own, typed in the visor. Refuse before anything is signed —
    // an out-of-bounds stake that reached the chain would be refused server-side AFTER its SOL moved.
    const isHouseEdge = selectedGame.value?.isHouseEdge === true;
    const houseEdgeStake = isHouseEdge ? parseStake(stakeAmount.value, selectedGame.value) : null;
    if (isHouseEdge && !isFree && houseEdgeStake === null) {
      betOutput.value = 'That stake could not be accepted.';
      if (onBetFail) onBetFail();
      return;
    }
```

In the fee-headroom block, replace

```ts
      const stake = selectedGame.value?.requiredAmount ?? 0;

      if (hasLoaded.value && balance.value < stake + NATIVE_FEE_HEADROOM_SOL) {
```

with

```ts
      const stake = houseEdgeStake ?? selectedGame.value?.requiredAmount ?? 0;

      if (hasLoaded.value && balance.value < stake + NATIVE_FEE_HEADROOM_SOL) {
```

Replace `const amount = selectedGame.value?.requiredAmount || 1;` with:

```ts
    // The mobile deep-link resume and the embedded popup both reuse `amount`, so this one line covers them.
    const amount = houseEdgeStake ?? (selectedGame.value?.requiredAmount || 1);
```

- [ ] **Step 6: Chip actions**

Directly after the `bet` function's closing `};` add:

```ts
  /** #183 Hyper Roll: add `step` to the typed stake, clamped to [requiredAmount, maximumStake]. */
  const bumpStake = (step: number) => {
    const game = selectedGame.value;
    if (!game?.isHouseEdge) return;
    const decimals = game.tokenDecimals ?? 9;
    const max = game.maximumStake ?? game.requiredAmount;
    const current = parseStake(stakeAmount.value, game) ?? 0;
    const next = Math.min(max, Math.max(game.requiredAmount, current + step));
    stakeAmount.value = String(Number(next.toFixed(decimals)));
  };

  /** #183 Hyper Roll: the largest stake the wallet can pay (minus fee headroom), capped at maximumStake. */
  const setMaxStake = () => {
    const game = selectedGame.value;
    if (!game?.isHouseEdge) return;
    const decimals = game.tokenDecimals ?? 9;
    const max = game.maximumStake ?? game.requiredAmount;
    const { balance, hasLoaded } = useTokenBalance();
    const scale = 10 ** decimals;
    const affordable = hasLoaded.value
      ? Math.floor((balance.value - NATIVE_FEE_HEADROOM_SOL) * scale) / scale
      : max;
    const value = Math.min(max, affordable);
    if (value < game.requiredAmount) return;
    stakeAmount.value = String(Number(value.toFixed(decimals)));
  };
```

- [ ] **Step 7: Export**

In the object returned by `useGame()`, after `netRewardPerWinner: computed(() => netRewardPerWinner.value),` add:

```ts
    stakeAmount,
    stakeValue,
    bumpStake,
    setMaxStake,
    myPayout,
```

- [ ] **Step 8: Lint and commit**

```bash
npx eslint --ext .ts src/hooks/useGame.ts
git add src/hooks/useGame.ts
git commit -m "Carry the Hyper Roll stake and per-player payouts through useGame."
git push origin feature/hyper-roll
```

Expected: no eslint errors.

---

### Task 4: Console, stake chips and phone layout (A6)

**Files:**
- Create: `src/components/game/HyperRollStakeChips.vue`
- Modify: `src/components/game/GameConsole.vue`
- Modify: `src/pages/GamePage.vue`

**Interfaces:**
- Consumes: `stakeAmount`, `stakeValue`, `bumpStake`, `setMaxStake` (Task 3); `formatMultiplier`, `isTopMultiplier` (Task 1).
- Produces: `<hyper-roll-stake-chips :compact="boolean" />` — renders nothing unless the selected type is house-edge.

- [ ] **Step 1: Create the chips component**

Create `src/components/game/HyperRollStakeChips.vue`:

```vue
<script setup lang="ts">
  import { computed } from 'vue';
  import { useGame } from '@/hooks/useGame';
  import { GameStatus } from '@/types';

  // #183 Hyper Roll stake chips (mockup A6): bump the stake typed in the console visor, or MAX. Renders
  // nothing for any other type, so a parent can mount it unconditionally.
  //
  // ⚠️ AMBER hover and a CYAN focus ring, not OrbitAmountField's violet: violet is Orbit's identity
  // colour only (colors.css), and Hyper Roll never runs Orbit.
  const props = defineProps<{ compact?: boolean }>();

  const { selectedGame, gameInstance, bumpStake, setMaxStake } = useGame();

  const STEPS = [0.01, 0.05, 0.1];

  const visible = computed(() => selectedGame.value?.isHouseEdge === true);
  const steps = computed(() => STEPS.filter((step) => step <= (selectedGame.value?.maximumStake ?? 0)));
  // Mirrors GameConsole.allowBets' round gate. The backend refusal is what actually protects a round.
  const disabled = computed(
    () =>
      gameInstance.value?.status !== GameStatus.WaitingBets &&
      gameInstance.value?.status !== GameStatus.GameStarting
  );
</script>

<template>
  <div v-if="visible" class="stake-chips" :class="{ 'stake-chips--compact': props.compact }">
    <button
      v-for="step in steps"
      :key="step"
      type="button"
      class="stake-chip"
      :disabled="disabled"
      :aria-label="`Add ${step} SOL to the stake`"
      @click="bumpStake(step)"
    >
      +{{ step }}
    </button>
    <button
      type="button"
      class="stake-chip"
      :disabled="disabled"
      aria-label="Stake as much as your balance allows, up to the maximum"
      @click="setMaxStake()"
    >
      MAX
    </button>
  </div>
</template>

<style scoped>
  .stake-chips {
    display: flex;
    gap: 4px;
    width: 100%;
  }

  .stake-chips--compact {
    gap: 6px;
  }

  .stake-chip {
    flex: 1 1 0;
    height: var(--ctl-h-sm);
    min-width: 0;
    padding: 0 var(--ctl-px-sm);
    background: var(--color-surface-inset);
    border: 1px solid var(--color-line);
    border-radius: var(--radius-chip);
    color: var(--color-ink-dim);
    font-family: 'Press Start 2P', monospace;
    font-size: 0.5rem;
    line-height: 1;
    transition:
      color 0.15s ease,
      border-color 0.15s ease,
      background-color 0.15s ease;
  }

  /* 44px on phones — the touch-target floor. */
  .stake-chips--compact .stake-chip {
    height: 44px;
    font-size: 0.5625rem;
  }

  .stake-chip:hover:not(:disabled) {
    background: color-mix(in oklab, var(--color-primary) 16%, var(--color-surface-inset));
    border-color: var(--color-primary);
    color: var(--color-ink);
  }

  .stake-chip:active:not(:disabled) {
    transform: translateY(1px);
  }

  .stake-chip:focus-visible {
    outline: 2px solid var(--color-cyan);
    outline-offset: 2px;
  }

  .stake-chip:disabled {
    opacity: 0.45;
    cursor: not-allowed;
  }

  @media (prefers-reduced-motion: reduce) {
    .stake-chip {
      transition: none;
    }

    .stake-chip:active:not(:disabled) {
      transform: none;
    }
  }
</style>
```

- [ ] **Step 2: Console script**

In `GameConsole.vue` `<script setup>`:

1. Add imports:

```ts
  import HyperRollStakeChips from '@/components/game/HyperRollStakeChips.vue';
  import { formatMultiplier, isTopMultiplier } from '@/utils/houseEdge';
```

2. In the `useGame()` destructure, add `stakeAmount,` and `stakeValue,`.

3. After `const requiresWallet = computed(() => true);` add:

```ts
  // #183 Hyper Roll (mockup A6): the keys are this round's board, the visor is the stake.
  const isHouseEdge = computed(() => selectedGame.value?.isHouseEdge === true);
  const boardKeys = computed(() => gameInstance.value?.boardChoices ?? []);
  const stakePlaceholder = computed(
    () => `${selectedGame.value?.requiredAmount ?? ''}–${selectedGame.value?.maximumStake ?? ''}`
  );
```

4. In `placeBet`, replace `userWord.value.length < (selectedGame.value?.choiceRequiredLength || 4) ||` with:

```ts
      userWord.value.length < (isHouseEdge.value ? 1 : selectedGame.value?.choiceRequiredLength || 4) ||
```

and directly after that whole first `if (...) { leverError(); return; }` block add:

```ts
    // #183 Hyper Roll: never drop the lever without a valid stake. useGame.bet() owns the message.
    if (isHouseEdge.value && !isFreeBetActive.value && stakeValue.value === null) {
      leverError();
      return;
    }
```

5. Replace `presetButtons`, `isKeySelected` and `selectPresetButton` with:

```ts
  // The word behind key `index`: this round's board for Hyper Roll, the type's preset list otherwise.
  const keyChoice = (index: number): string | undefined =>
    isHouseEdge.value
      ? boardKeys.value[index]?.choice
      : selectedGame.value?.presetChoices?.[index];

  const presetButtons = computed(() => {
    const board = boardKeys.value;
    const available = isHouseEdge.value ? board.length : selectedGame.value?.presetChoices?.length || 0;

    return [...Array(8).keys()].map((b) => {
      const boardChoice = isHouseEdge.value ? board[b] : undefined;
      return {
        img: require(`@/assets/game/buttons/button${b + 1}.png`),
        disabled: b + 1 > available,
        // #183: the multiplier tag over the key; the top tier gets the solid-yellow tag.
        tag: boardChoice ? formatMultiplier(boardChoice.multiplier) : null,
        top: boardChoice ? isTopMultiplier(board, boardChoice.multiplier) : false,
      };
    });
  });

  // Selection is DERIVED from userWord, never stored locally: placeBet() clears userWord on success and
  // GamePage clears it on game/round change, so a local ref would keep a key lit after both.
  const isKeySelected = (index: number) => !!userWord.value && keyChoice(index) === userWord.value;

  const selectPresetButton = (btnIndex: number) => {
    if (!allowBets.value) return;
    playAudio(buttonPress);
    userWord.value = keyChoice(btnIndex) ?? '';
  };
```

- [ ] **Step 3: Console template — phone branch**

1. Replace the phone visor's opening input lines

```vue
            <input
              id="word-type-input"
              ref="wordInput"
              v-model="userWord"
              v-maska="currentMask"
              :placeholder="placeholderText"
```

with

```vue
            <!-- #183 Hyper Roll: the visor holds the STAKE; the lit key and the terminal show the word. -->
            <input
              v-if="isHouseEdge"
              id="word-type-input"
              ref="wordInput"
              v-model="stakeAmount"
              type="text"
              inputmode="decimal"
              autocomplete="off"
              spellcheck="false"
              aria-label="Stake in SOL"
              :placeholder="stakePlaceholder"
              :disabled="myBetThisRoundIsOrbit"
              class="w-full bg-black text-center text-[16px] leading-[28px] tracking-[0.1em] text-green-500 !ring-0 placeholder:text-green-400 focus:outline-none disabled:opacity-100"
              @focus="setFocused(true)"
              @blur="setFocused(false)"
              @keyup.enter="placeBet()"
            />
            <input
              v-else
              id="word-type-input"
              ref="wordInput"
              v-model="userWord"
              v-maska="currentMask"
              :placeholder="placeholderText"
```

2. Replace the phone keypad

```vue
          <img
            v-for="(btn, index) in presetButtons"
            :key="btn.img"
            :src="btn.img"
            draggable="false"
            class="h-full min-h-0 w-full object-contain transition-all"
            :class="[
              btn.disabled ? '!cursor-not-allowed opacity-50' : 'cursor-pointer',
              !btn.disabled && isKeySelected(index) ? 'brightness-150 ring-2 ring-primary' : '',
            ]"
            @click="!btn.disabled ? selectPresetButton(index) : null"
          />
```

with

```vue
          <div
            v-for="(btn, index) in presetButtons"
            :key="btn.img"
            class="relative flex min-h-0 justify-center"
          >
            <img
              :src="btn.img"
              draggable="false"
              class="h-full min-h-0 w-full object-contain transition-all"
              :class="[
                btn.disabled ? '!cursor-not-allowed opacity-50' : 'cursor-pointer',
                !btn.disabled && isKeySelected(index) ? 'brightness-150 ring-2 ring-primary' : '',
              ]"
              @click="!btn.disabled ? selectPresetButton(index) : null"
            />
            <span v-if="btn.tag" class="key-tag key-tag--compact" :class="{ 'key-tag--top': btn.top }">
              {{ btn.tag }}
            </span>
          </div>
```

- [ ] **Step 4: Console template — desktop branch**

1. Replace the desktop visor's opening input lines

```vue
            <input
              id="word-type-input"
              ref="wordInput"
              v-model="userWord"
              v-maska="currentMask"
              :class="userWord.length || isFocused ? '' : 'blink'"
```

with

```vue
            <!-- #183 Hyper Roll: the visor holds the STAKE; the lit key and the terminal show the word. -->
            <input
              v-if="isHouseEdge"
              id="word-type-input"
              ref="wordInput"
              v-model="stakeAmount"
              type="text"
              inputmode="decimal"
              autocomplete="off"
              spellcheck="false"
              aria-label="Stake in SOL"
              :placeholder="isFocused ? '' : stakePlaceholder"
              :disabled="myBetThisRoundIsOrbit"
              class="z-20 bg-black relative placeholder:text-green-400 text-center tracking-widest w-full !ring-0 focus:outline-none text-green-500"
              @focus="setFocused(true)"
              @blur="setFocused(false)"
              @keyup.enter="placeBet()"
            />
            <input
              v-else
              id="word-type-input"
              ref="wordInput"
              v-model="userWord"
              v-maska="currentMask"
              :class="userWord.length || isFocused ? '' : 'blink'"
```

2. Replace `<img :src="keyboard" draggable="false" class="w-full px-6 md:px-4" />` with:

```vue
      <img v-if="!isHouseEdge" :src="keyboard" draggable="false" class="w-full px-6 md:px-4" />
      <!-- #183 Hyper Roll (A6): the bump chips take the keyboard's place in the left plate. -->
      <hyper-roll-stake-chips v-else class="mt-1 px-4" />
```

3. Replace the desktop keypad

```vue
        <img
          v-for="(btn, index) in presetButtons"
          :key="btn.img"
          :src="btn.img"
          :class="
            btn.disabled ? 'opacity-50 !cursor-not-allowed hover:filter hover:brightness-100' : ''
          "
          class="cursor-pointer hover:filter hover:brightness-200 transition-all"
          draggable="false"
          @click="!btn.disabled ? selectPresetButton(index) : null"
        />
```

with

```vue
        <div v-for="(btn, index) in presetButtons" :key="btn.img" class="relative">
          <img
            :src="btn.img"
            :class="[
              btn.disabled ? 'opacity-50 !cursor-not-allowed hover:filter hover:brightness-100' : '',
              isHouseEdge && !btn.disabled && isKeySelected(index) ? 'brightness-150 ring-2 ring-primary' : '',
            ]"
            class="block w-full cursor-pointer hover:filter hover:brightness-200 transition-all"
            draggable="false"
            @click="!btn.disabled ? selectPresetButton(index) : null"
          />
          <span v-if="btn.tag" class="key-tag" :class="{ 'key-tag--top': btn.top }">{{ btn.tag }}</span>
        </div>
```

4. In the non-scoped `<style>` block, after the `@keyframes blink` rule, add:

```css
  /* #183 Hyper Roll (A6): the multiplier tag over a key's bottom edge. The top tier is solid yellow —
     the terminal's hot token (#fde047), not a new colour. */
  .key-tag {
    position: absolute;
    left: 50%;
    bottom: 2px;
    transform: translateX(-50%);
    padding: 1px 4px;
    background: var(--color-void-deep);
    color: var(--color-primary);
    font-family: 'Press Start 2P', monospace;
    font-size: 8px;
    line-height: 1.2;
    white-space: nowrap;
    pointer-events: none;
  }

  .key-tag--top {
    background: #fde047;
    color: #000;
    font-size: 9px;
  }

  .key-tag--compact {
    padding: 0 2px;
    font-size: 6px;
  }

  .key-tag--compact.key-tag--top {
    font-size: 7px;
  }
```

- [ ] **Step 5: Phone chips row in `GamePage`**

In `src/pages/GamePage.vue`:

1. Change `import { onMounted, ref, watch } from 'vue';` to `import { computed, onMounted, ref, watch } from 'vue';` and add:

```ts
  import HyperRollStakeChips from '@/components/game/HyperRollStakeChips.vue';
```

2. The script already destructures `selectedGame` from `useGame()` (`const { selectedGame, userWord, updateNewGame, isIdleDisconnected } = useGame();`). Directly after that line add:

```ts
  // #183 Hyper Roll (A6): the stake chips sit in a row directly above the phone cabinet.
  const showStakeChips = computed(() => selectedGame.value?.isHouseEdge === true);
```

3. Replace

```vue
        <game-console
          v-if="!isWideConsole"
          mobile
```

with

```vue
        <!-- #183 Hyper Roll (A6): the chips row takes over the cabinet's tuck into Edson's band, so the
             cabinet below drops its own negative margin while the row is shown. -->
        <hyper-roll-stake-chips
          v-if="!isWideConsole && showStakeChips"
          compact
          class="relative z-20 -mt-[clamp(16px,3.6dvh,34px)] shrink-0 px-3 pb-2"
        />
        <game-console
          v-if="!isWideConsole"
          mobile
          :class="{ '!mt-0': showStakeChips }"
```

- [ ] **Step 6: Lint and commit**

```bash
npx eslint --ext .ts,.vue src/components/game/HyperRollStakeChips.vue src/components/game/GameConsole.vue src/pages/GamePage.vue
git add src/components/game/HyperRollStakeChips.vue src/components/game/GameConsole.vue src/pages/GamePage.vue
git commit -m "Build the Hyper Roll console: stake visor, chips and multiplier-tagged keys."
git push origin feature/hyper-roll
```

---

### Task 5: Terminal (A6)

**Files:**
- Modify: `src/components/game/GameTerminal.vue`

**Interfaces:**
- Consumes: `stakeValue`, `myPayout` (Task 3); `formatChance`, `formatMultiplier`, `isTopMultiplier`, `payoutFor`, `winChancePercent` (Task 1).

- [ ] **Step 1: Script**

1. Add the import:

```ts
  import {
    formatChance,
    formatMultiplier,
    isTopMultiplier,
    payoutFor,
    winChancePercent,
  } from '@/utils/houseEdge';
```

2. In the `useGame()` destructure, add `stakeValue,` and `myPayout,`.

3. After the `isLotteryMode` computed add:

```ts
  // #183 Hyper Roll (mockup A6): same screen, same lines, plus per-word multiplier / chance / win.
  const isHouseEdge = computed(() => selectedGame.value?.isHouseEdge === true);
  // The typed stake when valid, else the minimum — so the WIN column always shows a real payout.
  const houseEdgeStake = computed(() => stakeValue.value ?? selectedGame.value?.requiredAmount ?? 0);
  const boardRows = computed(() => {
    const board = gameInstance.value?.boardChoices ?? [];
    return board.map((entry) => ({
      choice: entry.choice,
      multiplier: formatMultiplier(entry.multiplier),
      chance: formatChance(winChancePercent(board, entry.multiplier)),
      win: formatPayout(payoutFor(houseEdgeStake.value, entry.multiplier)),
      top: isTopMultiplier(board, entry.multiplier),
    }));
  });
  // What the PAID line prints: a Hyper Roll winner's own payout, or the uniform pari-mutuel figure.
  const paidFigure = computed(() => (isHouseEdge.value ? myPayout.value : netRewardPerWinner.value));
```

- [ ] **Step 2: Pre-run lines**

1. Replace

```vue
          <p>
            > BET:
            <span :title="formatAmount(selectedGame.requiredAmount).title">{{
              formatAmount(selectedGame.requiredAmount).text
            }}</span>
          </p>
```

with

```vue
          <p v-if="isHouseEdge">
            > BET:
            <span class="pot-value">{{ formatAmount(houseEdgeStake).text }}</span>
          </p>
          <p v-else>
            > BET:
            <span :title="formatAmount(selectedGame.requiredAmount).title">{{
              formatAmount(selectedGame.requiredAmount).text
            }}</span>
          </p>
```

2. Replace

```vue
          <!-- Pot: shown for every game type. Highlighted with the same green glow as the
               near-miss "4-3-2 away" state. -->
          <p>
            > POT:
```

with

```vue
          <!-- #183 Hyper Roll: a player-vs-house round has no pot to win, so the RTP takes the POT line. -->
          <p v-if="isHouseEdge">
            > RTP: {{ selectedGame.rtpPercent }}%<template v-if="!mobile">
              · BIGGER MULTIPLIER, RARER WORD</template
            >
          </p>
          <!-- Pot: shown for every other game type. Highlighted with the same green glow as the
               near-miss "4-3-2 away" state. -->
          <p v-else>
            > POT:
```

3. Replace

```vue
          <div v-if="selectedGame?.presetChoices?.length">
            <br />
            <p>> CHOICES:</p>
```

with

```vue
          <!-- #183 Hyper Roll: still one `> word` per line in KEY order, with multiplier, chance and what the
               current stake wins. The top tier borrows `.result-win` — the terminal's one hot token. -->
          <div v-if="isHouseEdge && boardRows.length">
            <br />
            <div class="board-row" :class="{ 'board-row--mobile': mobile }">
              <span>> CHOICES:</span>
              <span class="board-head">MULT</span>
              <span class="board-head">CHANCE</span>
              <span class="board-head">WIN</span>
            </div>
            <div
              v-for="row in boardRows"
              :key="row.choice"
              class="board-row ml-2"
              :class="{ 'board-row--mobile': mobile, 'result-win font-bold': row.top }"
            >
              <span>> {{ row.choice }}</span>
              <span>{{ row.multiplier }}</span>
              <span>{{ row.chance }}</span>
              <span>{{ row.win }}<template v-if="row.top && !mobile"> &lt; TOP</template></span>
            </div>
          </div>

          <div v-else-if="selectedGame?.presetChoices?.length">
            <br />
            <p>> CHOICES:</p>
```

- [ ] **Step 3: Running and result lines**

1. Replace

```vue
          <!-- Pot: stays visible during the run and after it ends (final total).
               Highlighted with the same green glow as the near-miss "4-3-2 away" state. -->
          <p>
            > POT:
```

with

```vue
          <!-- Pot: stays visible during the run and after it ends (final total). Not on a Hyper Roll round.
               Highlighted with the same green glow as the near-miss "4-3-2 away" state. -->
          <p v-if="!isHouseEdge">
            > POT:
```

2. Replace

```vue
            <p v-if="netRewardPerWinner">
              > PAID: {{ formatPayout(netRewardPerWinner) }}
```

with

```vue
            <p v-if="paidFigure">
              > PAID: {{ formatPayout(paidFigure) }}
```

3. Replace `<div v-if="nearMissEntries.length" class="near-miss-dock">` with:

```vue
          <div v-if="!isHouseEdge && nearMissEntries.length" class="near-miss-dock">
```

- [ ] **Step 4: Styles**

In the component's `<style>` block, after the `.pot-value` rule add:

```css
  /* #183 Hyper Roll: the aligned CHOICES columns. Widths match the A6 mockup at 12px/10px type. */
  .board-row {
    display: grid;
    grid-template-columns: 170px 70px 90px minmax(0, 1fr);
    column-gap: 18px;
  }
  .board-row--mobile {
    grid-template-columns: 104px 38px 50px minmax(0, 1fr);
    column-gap: 8px;
  }
  .board-head {
    color: rgba(34, 197, 94, 0.45);
  }
```

- [ ] **Step 5: Lint and commit**

```bash
npx eslint --ext .vue src/components/game/GameTerminal.vue
git add src/components/game/GameTerminal.vue
git commit -m "Show Hyper Roll multipliers, chances and wins in the terminal."
git push origin feature/hyper-roll
```

---

### Task 6: Lobby card, art, manual and stats badge

**Files:**
- Modify: `src/components/game/GameStatsPanel.vue`
- Modify: `src/components/game-selection/GameTypeCard.vue`
- Modify: `src/utils/gameArt.ts`
- Modify: `src/utils/gameArchetype.ts`
- Modify: `src/components/guide/GuideModeCards.vue`
- Modify: `src/components/guide/sections/GuideWhatYouWin.vue`

- [ ] **Step 1: RTP badge**

In `GameStatsPanel.vue`, replace

```ts
  const houseCutPct = computed(() => selectedGame.value?.commissionPercentage ?? 0);
  const houseCutLabel = computed(() => `${houseCutPct.value}% HOUSE CUT`);
  const houseCutBlurb = computed(() =>
    houseCutPct.value > 0
      ? `Winners keep the pot minus a ${houseCutPct.value}% house cut. Every round is seed-verifiable.`
      : 'Winners split 100% of the pot. Every round is seed-verifiable.'
  );
```

with

```ts
  const houseCutPct = computed(() => selectedGame.value?.commissionPercentage ?? 0);
  // #183 Hyper Roll takes no cut — its edge is in the multipliers — so the badge states the RTP instead.
  const houseCutLabel = computed(() =>
    selectedGame.value?.isHouseEdge
      ? `${selectedGame.value.rtpPercent ?? 0}% RTP`
      : `${houseCutPct.value}% HOUSE CUT`
  );
  const houseCutBlurb = computed(() => {
    if (selectedGame.value?.isHouseEdge)
      return 'Every pick pays its multiplier. Every round is seed-verifiable.';
    return houseCutPct.value > 0
      ? `Winners keep the pot minus a ${houseCutPct.value}% house cut. Every round is seed-verifiable.`
      : 'Winners split 100% of the pot. Every round is seed-verifiable.';
  });
```

- [ ] **Step 2: Lobby card**

In `GameTypeCard.vue`, replace `<span class="chip-label">Ticket</span>` with:

```vue
                <span class="chip-label">{{ props.game.isHouseEdge ? 'From' : 'Ticket' }}</span>
```

- [ ] **Step 3: Shape resolution**

In `src/utils/gameArt.ts`, inside `archetypeOf`, after `if (game.lotteryParameters?.lotteryFrequency) return 'lottery';` add:

```ts
  // #183 Hyper Roll reuses the preset (Beast Mode character) art until it has its own.
  if (game.isHouseEdge) return 'preset';
```

In `src/utils/gameArchetype.ts`, change the type to:

```ts
export type GameArchetype = 'preset' | 'race' | 'lottery' | 'houseEdge';
```

and inside `resolveGameArchetype`, after `if (!game) return null;` add:

```ts
  // #183 Hyper Roll is checked first: it has preset choices, but it is not a preset race.
  if (game.isHouseEdge) return 'houseEdge';
```

Find every exhaustive consumer of this type:

```bash
grep -rn "gameArchetype" src --include=*.ts --include=*.vue
```

Every `Record<GameArchetype, …>` in the results needs a `houseEdge` entry; `yarn build` fails on any that is missed. `GuideModeCards.vue` is handled below.

- [ ] **Step 4: Manual mode card**

In `GuideModeCards.vue`:

1. In `interface ModeCard`, after `price: string;` add `priceLabel: string;`.

2. Add a `houseEdge` entry to each record:

```ts
    houseEdge: (game) =>
      `Pick 1 of 8 words and set your stake. If the round lands on your word, the house pays your stake times its multiplier — up to 10×. Bigger multiplier, rarer word. ${game.rtpPercent ?? 95}% RTP, no house cut.`,
```

(in `BLURB`),

```ts
    houseEdge: 'MULTIPLIER',
```

(in `ARCHETYPE_LABEL`),

```ts
    houseEdge: 'text-cyan border-cyan/60',
```

(in `ARCHETYPE_ACCENT`).

3. In the `cards` map, replace

```ts
          price: formatGameAmount(game, game.requiredAmount).text,
          schedule: scheduleFor(game),
```

with

```ts
          price:
            archetype === 'houseEdge'
              ? `${formatGameAmount(game, game.requiredAmount).text} – ${
                  formatGameAmount(game, game.maximumStake ?? game.requiredAmount).text
                }`
              : formatGameAmount(game, game.requiredAmount).text,
          priceLabel: archetype === 'houseEdge' ? 'STAKE' : 'TICKET',
          schedule: scheduleFor(game),
```

4. In the template, replace `TICKET: {{ card.price }}` with `{{ card.priceLabel }}: {{ card.price }}`.

- [ ] **Step 5: Payout section of the manual**

In `GuideWhatYouWin.vue`, in `rows`, after the `THE HOUSE CUT` entry add:

```ts
    {
      title: 'HYPER ROLL IS DIFFERENT',
      body: "No pot and no house cut. You play against the house: if the round lands on your word, you get your stake times that word's multiplier, up to 10x. Its return to player is shown in its STATS panel.",
    },
```

- [ ] **Step 6: Build**

```bash
yarn build
```

Expected: `Build complete.` with no TypeScript errors.

- [ ] **Step 7: Lint and commit**

```bash
npx eslint --ext .ts,.vue src/components/game/GameStatsPanel.vue src/components/game-selection/GameTypeCard.vue src/utils/gameArt.ts src/utils/gameArchetype.ts src/components/guide/GuideModeCards.vue src/components/guide/sections/GuideWhatYouWin.vue
git add src/components/game/GameStatsPanel.vue src/components/game-selection/GameTypeCard.vue src/utils/gameArt.ts src/utils/gameArchetype.ts src/components/guide/GuideModeCards.vue src/components/guide/sections/GuideWhatYouWin.vue
git commit -m "Describe Hyper Roll in the lobby, the manual and the stats badge."
git push origin feature/hyper-roll
```

---

### Task 7: Browser QA against the local stack

Runs after the backend plan is merged into the same local stack (the implement-feature skill's deploy step). Check at 1280 px and at a true 360 px (CDP device emulation — `resize_page` stops at ~500 px).

- [ ] **Step 1: Lobby** — the Hyper Roll card reads `FROM 0.01` with the preset art; Beast Mode is absent.
- [ ] **Step 2: Terminal pre-run** — `> GAME: Hyper Roll`, `> BET: 0.01 SOL`, `> RTP: 95% · BIGGER MULTIPLIER, RARER WORD` (phone: `> RTP: 95%`), `> CHOICES:` with 8 lines in key order, the four 10× lines yellow with `< TOP` (desktop only), no `> POT:`.
- [ ] **Step 3: Console** — keys 1–8 carry `N×` tags with the 10× tags solid yellow; typing `0.05` in the visor updates every WIN value; `+0.01 / +0.05 / +0.1 / MAX` work (phone: the row sits directly above the cabinet); `0.001` and `0.3` make the lever twitch and refuse.
- [ ] **Step 4: Round** — free bet (dev toggle) runs ~10 s, the winning word is typed last, no `N AWAY!` row appears.
- [ ] **Step 5: Paid win** — `> PAID: <stake × multiplier> SOL` and the win toast show the same figure; a losing viewer sees no PAID line.
- [ ] **Step 6: FAIR panel** — shows `WEIGHTED DRAW`; recompute finishes instantly and verifies; the downloaded JSON verifies in `public/verify.html` offline.
- [ ] **Step 7: Other types** — Cosmic Sprint and Space Jackpot render exactly as before (keyboard art, `> POT:`, `N AWAY!` row, `TICKET` chip).

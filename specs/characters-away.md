# ChimpOut — "Characters Away" (near-miss reveal) — Implementation Spec

> **Status: Shipped** — merged via `feature/characters-away` on 2026-07-07.
> Repos affected: **`monkify-front`** + **`monkify-backend`** + this doc in **`monkify-meta`**.
> Distilled summary: `../features/characters-away.md`.
>
> **Purpose:** Make the most thrilling moment of a round — the monkey landing close to one of *your
> own* choices — visible, and keep it on screen, so even a loss feels like an "almost".

> ### ⚠️ Post-ship notes — the shipped design differs from §1–§15 below
> The sections below capture the **original** design (3/2/1 pinned-milestone history, frontend-only).
> During implementation the product owner iterated the behaviour; **what actually shipped** is:
> - **One row per own choice** (not a per-milestone history), docked at the **bottom** of the terminal
>   (transparent, always visible), beside a **`> POT:`** line (in-game pot = Σ bet amounts + lottery
>   base). `GameTerminal.vue` owns the dock; `GameTyper.vue` renders just the stream tail.
> - Each row shows the **closest approach so far** (`away = L − k`, KMP tail-prefix). It **climbs**, and
>   if it does not get closer for **2 s** it **resets to the start and climbs again** (a continuous
>   "keeps trying to type it" loop). On a **win**, the winning choice (if the player bet it) is **typed
>   out to the full word** and held.
> - **Backend (added):** on a **fallback-decided** winner, `GameService.RunGame` emits
>   `ChimpOutTyper.FirstChoiceTyped` as a final `<gameId>/Terminal` batch so the terminal ends with
>   the word "typed". Purely visual; winner/seed/payouts/status unchanged. Unit-tested
>   (`GameServiceTests.RunGame_FallbackDecidedWinner_EmitsWinningChoiceAsFinalTerminalBatch`).
> - Frontend detector is `src/utils/nearMiss.ts` (pure KMP; `advance()` → live matched length) +
>   `src/hooks/useNearMiss.ts` (`nearMissEntries`, `trackChoice`/`feed`/`finish`/`reset` — display
>   high-water, 2 s stall loop, win type-out). The original "pinned, monotonic, once-per-level" model
>   is superseded. See `features/characters-away.md` for the authoritative current behaviour.

---

## 1. Summary

When the live typer stream is streaming during a race/lottery, the client watches — for each choice the
current player committed this game — how close the stream currently is to *completing that choice at
its tail*. The moment the stream's trailing correct prefix first reaches **3, 2, and 1** characters short
of the full choice, the client:

1. shows a neutral **"N AWAY"** beat, and
2. **pins** the matched partial word (highlighted) inside the terminal so it survives the terminal's
   character crop and stays visible until the game ends.

Everything is computed client-side from the `<gameId>/Terminal` stream the client already receives.
**No backend, SignalR, DB, or contract change.** Outcomes, payouts, pacing, and the seed audit trail are
untouched.

### 1.1 The distance metric (the crux — read this first)

The card's prose said "positional match on the sliding window (`away = L − matchedPositions`)". During
spec review the product owner clarified the intended model with a concrete example ("for `jaguar` you
cannot go from `jag` to `jagua` without passing through `jagu`"). That intent is **not** a positional
Hamming compare of a fixed L-length window (which realigns every shift and *could* skip levels). It is
the **growing correct prefix at the tail of the stream**:

> Let `L = choice.length`. Let `k` = the length of the **longest suffix of the character stream so far
> that equals a prefix of `choice`**. Then `away = L − k`. A **win** is exactly `k == L` (the stream
> ends with the whole choice) — so this metric *does* mirror the win, and reaches `away == 0` exactly
> when the backend's `ChimpOutTyper.CheckForWinners` would (see
> `monkify-backend/src/ChimpOut.Domain/Games/Services/ChimpOutTyper.cs`, exact-window equality).

This is the **KMP partial-match state**: on each new character, `k` advances by **at most +1** (or drops
back via the failure function when the run breaks). Because `k` can only *grow one at a time*, it
necessarily passes through every intermediate value, so the levels fire in strict **3 → 2 → 1** order and
**a level can never be skipped**. "High-water mark" and "exact level" therefore collapse to the same
behaviour — we fire level `N` the first time `k` reaches `L − N`.

Worked example, `choice = "jaguar"` (`L = 6`):

| Stream tail … | `k` (matched prefix len) | `away` | Fires |
| --- | --- | --- | --- |
| `…xji` | 0 | 6 | — |
| `…j` | 1 | 5 | — |
| `…ja` | 2 | 4 | — |
| `…jag` | 3 | 3 | **3 AWAY** (pin `jag`) |
| `…jagu` | 4 | 2 | **2 AWAY** (pin `jagu`) |
| `…jagz` (run broke) | 0 | 6 | — (nothing removed; monotonic) |
| … later `…jagua` | 5 | 1 | **1 AWAY** (pin `jagua`) |
| `…jaguar` | 6 | 0 | win (backend ends the game) |

The **matched characters** to highlight at level `N` are exactly `choice.substring(0, L − N)` (the
correct partial word at the stream tail).

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Branch-per-repo:** `feature/characters-away` in `monkify-front` and `monkify-meta`. No backend
  branch (no backend surface). Push after branch work.
- **Backend integration-test mandate does NOT apply.** The "every new feature needs a happy-path backend
  integration test" rule (`constraints.md` §3) is scoped to backend / game-flow features. This card
  has **no** backend surface, so it is exempt. See §11 for how the front is verified instead.
- **No unit-test runner exists in `monkify-front`.** `package.json` has only `serve` / `build` /
  `lint` — no Jest/Vitest. Do **not** introduce a test framework as part of this card. Verification is
  **lint + typecheck (via `build`) + manual QA** (§13). To keep the logic verifiable, the detection is
  written as a **pure function** (`src/utils/nearMiss.ts`) so it *can* be unit-tested later once a runner
  is added — but no runner is wired here.
- **Display-only / RG-safe:** the near-miss is a neutral reveal. **No** "bet again" / spend CTA is
  attached to it. Outcomes, payouts, pacing, and the SignalR contract are unchanged and seed-auditable.
- **Secrets/config:** none touched. No operator-messaging surface involved.
- **Env vars:** none added. (If a flag is ever wanted, the codebase uses the `VUE_APP_*` prefix — see
  `src/constants/featureFlags.ts` — **not** `VITE_*`. This card ships the feature **always on**, no flag.)

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **Distance = tail-prefix progression (KMP partial-match), not window Hamming.** `away = L − k` where `k` is the longest stream-suffix that is a prefix of the choice. Supersedes the card's looser "positional match on the last L characters" wording; this is the model the product owner confirmed (see §1.1). |
| 2 | **Levels 3, 2, 1.** Level `N` fires the first time `k` reaches `L − N`. Because `k` grows by ≤ +1 per character, levels fire in strict 3 → 2 → 1 order and none can be skipped — so high-water-mark and exact-level are equivalent. |
| 3 | **Once per `(choice, level)` — monotonic.** Each `(choice, level)` fires exactly once (first time reached). If the run later breaks and `k` drops, nothing is removed and no milestone re-fires. Getting closer only ever *adds* pins/highlights. |
| 4 | **Skip meaningless levels.** Level `N` fires only if it corresponds to ≥ 1 matched char, i.e. `L − N ≥ 1` ⟺ `N ≤ L − 1`. For default 6/7-length games all three levels are meaningful; only very short `L` skips levels (e.g. `L = 3` skips level 3). |
| 5 | **Scope = all of the player's own choices this game, tracked independently.** A player may bet multiple choices; each gets its own `k`, failure table, fired-levels set, and pins. |
| 6 | **`lastOwnBetChoice` is NOT reused as the durable list — a new `ownBetChoices` list is added.** Code review found `lastOwnBetChoice` is a *one-shot* marker: `GameMonkey.vue` **nulls it** the instant the bet echoes back (to fire the "add bet" animation), so it cannot double as the durable per-game near-miss list. We keep `lastOwnBetChoice` unchanged for the monkey animation and add a separate, never-consumed `ownBetChoices: string[]`. (Hidden blocker the card missed — see §6.1.) |
| 7 | **Highlight lives inside the terminal element (not a separate side strip).** Pinned near-miss segments render within the same terminal output area, in the same green mono styling, above the live scrolling tail — reading as part of the stream. Live tail keeps its existing crop/refresh cadence. |
| 8 | **Pins are exempt from the crop; bounded ≤ 3 per own choice.** The live tail still crops 20000 → last 2000 chars; pins are held in their own reactive state and never cropped. Max pins = 3 × (number of own choices). |
| 9 | **Lifecycle: freeze on `Ended`, clear on rollover.** On `Ended`, stop feeding the detector (pins/highlights freeze and stay visible through the reward view). On game rollover (`updateNewGame`), clear `ownBetChoices`, all detector state, and all pins. |
| 10 | **Best-effort by design.** The terminal stream is not replayed, so a late joiner / post-reconnect client only detects near-misses that occur **after** it (re)connects. Acceptable for a display-only feature. |
| 11 | **Detection is fed the raw `Terminal` batches, independent of the display crop.** The detector consumes `data` from the `<gameId>/Terminal` handler directly (not the cropped `terminalOutput`), so the closest approach is never lost to cropping. It keeps only the KMP state `k` per choice — no need to retain the full stream. |
| 12 | **Always on.** No feature flag; the feature is display-only and RG-safe. |

---

## 4. Data model

**No backend data-model change. No EF entity, mapping, migration, DTO, endpoint, or SignalR contract
change.** This section is intentionally empty of backend work.

Client-side types only (frontend), defined in §6.

---

## 5. Configuration

**None.** No `GeneralSettings`, no env var, no seed data, no feature flag. The feature is always on.

---

## 6. Frontend behaviour (`monkify-front`)

All work is in `monkify-front/src`. Three code units: a **pure detector** (`utils/nearMiss.ts`), a
**stateful composable** (`hooks/useNearMiss.ts`), and wiring into **`useGame.ts`** +
**`GameTyper.vue`**.

### 6.1 Track the player's own choices (`hooks/useGame.ts`)

**Problem today:** `lastOwnBetChoice` (module ref, `useGame.ts:47`) is set optimistically in each of
the three `bet()` paths (regulated `:212`, crypto `:250`, free `:274`) and is **consumed (nulled)** by
`GameMonkey.vue:39-41` on the first echoing bet, and by the error paths. It is a transient single-use
flag, **not** a durable list — so it cannot be the near-miss source of truth.

**Change:**

1. Add a new module ref alongside it:
   ```ts
   const ownBetChoices = ref<string[]>([]); // durable, per-game; never consumed
   ```
2. In **each** of the three successful-bet paths, **after** `await mainService.applyBet(...)` resolves,
   record the choice durably and register it with the detector (idempotent / de-duped on the choice
   string):
   ```ts
   if (!ownBetChoices.value.includes(choice)) ownBetChoices.value.push(choice);
   nearMiss.trackChoice(choice, /* L */ selectedGame.value?.choiceRequiredLength ?? choice.length);
   ```
   Leave the existing `lastOwnBetChoice.value = choice` (optimistic, pre-`await`) and its rollback in the
   `catch` **unchanged** — the monkey animation still depends on it. `ownBetChoices` is appended only on
   success, so a failed bet adds nothing to clean up.
   > `L` is `selectedGame.choiceRequiredLength` for free-type/preset games (uniform per game).
   > Falling back to `choice.length` is safe because a committed choice already satisfies the required
   > length.
3. In `updateNewGame()` (`:314-324`), where `lastOwnBetChoice.value = null` is reset, also clear the
   durable list and the detector:
   ```ts
   ownBetChoices.value = [];
   nearMiss.reset();
   ```
4. Expose `ownBetChoices` (and the detector's pins, see §6.3) from `useGame`'s return object if any
   component needs them directly; `GameTyper.vue` reads pins from `useNearMiss` instead (see §6.4).

### 6.2 Feed the detector from the Terminal stream (`hooks/useGame.ts`)

In the `<gameId>/Terminal` handler (`:111-118`), after appending to `terminalOutput`, feed the raw
batch to the detector — **before/independent of** the crop that `GameTyper.vue` applies to
`terminalOutput`:

```ts
wsGamesConnection.value.on(`${gameInstance.value?.gameId}/Terminal`, (data: string) => {
  terminalOutput.value = terminalOutput.value + data;
  nearMiss.feed(data); // no-op once frozen; O(L) per char × own choices
});
```

In the `<gameId>/Status` handler (`:128-142`), when `statusData.status === GameStatus.Ended`,
**freeze** the detector so pins/highlights stop changing and persist through the reward view:

```ts
if (statusData.status === GameStatus.Ended) nearMiss.freeze();
```

### 6.3 New pure detector (`utils/nearMiss.ts`)

A framework-free, side-effect-free module so the logic is unit-testable later (no runner today). Core:

```ts
export const NEAR_MISS_LEVELS = [3, 2, 1] as const;
export type NearMissLevel = (typeof NEAR_MISS_LEVELS)[number];

export interface NearMissPin {
  choice: string;      // the full choice this near-miss belongs to
  level: NearMissLevel; // 3 | 2 | 1
  matched: string;     // choice.substring(0, L - level) — the highlighted partial word
  L: number;           // choice length (for rendering the remaining placeholder slots)
}

// Per-choice mutable matcher state (KMP).
export interface ChoiceMatcher {
  choice: string;
  L: number;
  failure: number[];   // KMP failure table for `choice`
  k: number;           // current matched-prefix length (0..L)
  fired: Set<NearMissLevel>;
}

export function buildFailureTable(s: string): number[] { /* standard KMP prefix-function */ }

export function createMatcher(choice: string, L: number): ChoiceMatcher; // k=0, fired=∅

// Advance one character; return any newly-fired pins (0..1 per char in practice).
export function advance(m: ChoiceMatcher, ch: string): NearMissPin[];
//   while (m.k > 0 && ch !== m.choice[m.k]) m.k = m.failure[m.k - 1];
//   if (ch === m.choice[m.k]) m.k++;
//   for level of [3,2,1]: p = m.L - level;
//       if (p >= 1 && m.k >= p && !m.fired.has(level)) { m.fired.add(level); emit pin(matched=choice.slice(0,p)) }
//   if (m.k === m.L) m.k = m.failure[m.L - 1] ?? 0;  // allow continued matching after a full hit (defensive)
```

Notes:
- `advance` fires **at most one new level per character** in practice (because `k` grows by ≤ 1), but the
  loop over levels is written defensively so a `k` that satisfies multiple unfired thresholds still emits
  them all (e.g. if a choice is registered mid-stream and `k` is seeded). Order emitted 3 → 2 → 1.
- Duplicate characters in a choice are handled correctly by the failure table (no special-casing needed,
  regardless of the game's `acceptDuplicatedCharacters`).
- Complexity: O(1) amortised per character per choice.

### 6.4 New composable (`hooks/useNearMiss.ts`)

Module-singleton (same pattern as `useGame`) so the Terminal feed (in `useGame`) and the render (in
`GameTyper.vue`) share one reactive state:

```ts
const matchers = new Map<string, ChoiceMatcher>(); // keyed by choice string (de-duped)
const pins = ref<NearMissPin[]>([]);
const frozen = ref(false);

export const useNearMiss = () => ({
  nearMissPins: computed(() => pins.value),
  trackChoice(choice: string, L: number) {            // idempotent; no-op if already tracked
    if (!choice || matchers.has(choice)) return;
    matchers.set(choice, createMatcher(choice, L));
  },
  feed(batch: string) {                               // no-op when frozen
    if (frozen.value || matchers.size === 0) return;
    for (const ch of batch)
      for (const m of matchers.values())
        for (const pin of advance(m, ch)) pins.value.push(pin);
  },
  freeze() { frozen.value = true; },                  // on Ended
  reset() { matchers.clear(); pins.value = []; frozen.value = false; }, // on rollover
});
```

`useGame` imports `useNearMiss()` once at module scope and calls `trackChoice` / `feed` / `freeze` /
`reset` at the points in §6.1–§6.2.

### 6.5 Segmented / pinned render (`components/game/GameTyper.vue`)

Today `GameTyper.vue` renders a single `<p id="output">{{ updatedOutput }}</p>` (throttled snapshot of
the cropped `terminalOutput`). Change to a **segmented render**:

1. Import pins: `const { nearMissPins } = useNearMiss();`
2. Keep the live tail exactly as today (`updatedOutput`, crop + refresh intervals unchanged).
3. Above the live tail, render the pinned near-miss segments **inside the same terminal element/flow**
   (same font/colour, not a separate strip). Suggested default markup (visual latitude — see §7):

```html
<template>
  <div id="output" class="break-words">
    <!-- Pinned near-misses (exempt from crop; persist until game end) -->
    <p v-for="(pin, i) in nearMissPins" :key="`${pin.choice}-${pin.level}-${i}`" class="near-miss">
      > {{ pin.level }} AWAY!
      <span class="near-miss-matched">{{ pin.matched }}</span
      ><span class="near-miss-rest">{{ '_'.repeat(pin.L - pin.matched.length) }}</span>
      ({{ pin.choice }})
    </p>
    <!-- Live scrolling tail (unchanged behaviour) -->
    <span>{{ updatedOutput }}</span>
  </div>
</template>
```

- `near-miss-matched` = the correct partial word, styled brighter/accent (the "highlight"); the remaining
  `L − matched.length` slots shown as neutral placeholders so the shape of "how close" reads at a glance.
- Because pins are ordered by fire time and per choice reach at most 3, the list is naturally grouped and
  bounded.
- Keep the auto-scroll-to-bottom behaviour in `GameTerminal.vue` (`watch(terminalOutput …)`); pins live
  above the tail and don't need their own scroll handling.

### 6.6 What is intentionally NOT changed

- No change to `bet()` payloads, `mainService`, headers, or types beyond the additions above.
- No change to `terminalOutput` cropping, the `/DisplaySpeed` ramp, `/Bets`, `/Status`, chat, or the
  winner/rewards render.
- `lastOwnBetChoice` semantics and the `GameMonkey.vue` animation are untouched.

---

## 7. Visual design (deferred details — defaults provided)

Exact colours, pin placement, and layout are design-stage details. Sensible defaults that satisfy the
card:

- **Placement:** pinned segments render at the top of the terminal output area (inside `#output`), above
  the live tail, so they stay on screen as the tail scrolls/crops.
- **Highlight:** matched partial word in a brighter/accent green (or per-level accent: 3 = dim, 2 =
  medium, 1 = hot) vs the normal stream green; remaining slots as `_` placeholders.
- **Beat:** a one-line `> N AWAY!` label per pin. Neutral wording; **no** CTA. A brief entrance
  animation (e.g. a flash) is optional and non-essential.
- **Per-level colour ramp and any pin grouping by choice** are open to the designer; the data model
  (`NearMissPin`) already carries `choice`, `level`, `matched`, `L` to support any of these.

---

## 8. Flow diagram

```mermaid
sequenceDiagram
  participant BE as Backend (Games Hub)
  participant US as useGame.ts
  participant NM as useNearMiss / nearMiss detector
  participant GT as GameTyper.vue

  Note over US: player places bet(s) → on success:<br/>ownBetChoices.push(choice); nearMiss.trackChoice(choice, L)
  loop each Terminal batch (InProgress)
    BE->>US: "<gameId>/Terminal" data
    US->>US: terminalOutput += data  (cropped later by GameTyper)
    US->>NM: feed(data)
    NM->>NM: per own choice: advance k (KMP); on k==L-N first time → pin(matched, level N)
    NM-->>GT: nearMissPins (reactive) updated
    GT->>GT: render pinned segments (exempt from crop) + live tail
  end
  BE->>US: "<gameId>/Status" Ended
  US->>NM: freeze()  (pins/highlights persist through reward view)
  Note over US,NM: on rollover updateNewGame(): ownBetChoices=[]; nearMiss.reset()
```

---

## 9. Out of scope

- Any **backend** recording of closest approach (e.g. a `Bet.ClosestDistance`), recap/share cards, "wall
  of almosts", or cross-game persistence — possible follow-ups.
- Highlighting **other** players' / preset choices the player did **not** bet on.
- Replaying near-misses that happened **before** the client connected (no stream replay).
- Final visual design (colours, pin placement/layout) — decided at design time (§7 gives defaults).
- Any change to win logic, payouts, pacing, or the SignalR contract.
- Introducing a frontend test runner (none exists; not this card's job).

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

**None.** No files change.

### 10.2 Frontend (`monkify-front`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/utils/nearMiss.ts` | **New.** Pure detector: `NearMissPin`/`ChoiceMatcher` types, `buildFailureTable`, `createMatcher`, `advance` (KMP tail-prefix, fires levels 3/2/1 once each, skips meaningless levels). |
| 2 | `src/hooks/useNearMiss.ts` | **New.** Module-singleton composable wrapping the detector: `nearMissPins`, `trackChoice`, `feed`, `freeze`, `reset`. |
| 3 | `src/hooks/useGame.ts` | Add `ownBetChoices` ref; append + `nearMiss.trackChoice` after each successful `applyBet` (3 paths); `nearMiss.feed(data)` in the `Terminal` handler; `nearMiss.freeze()` on `Ended` in the `Status` handler; clear `ownBetChoices` + `nearMiss.reset()` in `updateNewGame`; expose as needed. **Leave `lastOwnBetChoice` untouched.** |
| 4 | `src/components/game/GameTyper.vue` | Segmented render: keep live tail (`updatedOutput`, crop/refresh unchanged); render `nearMissPins` as pinned highlighted segments inside `#output`, above the tail; add `.near-miss*` styles. |

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/characters-away.md` | **New.** This spec. |
| 2 | `INDEX.md` | Add `specs/characters-away.md` row (status Ready); update "Feature currently in flight". |
| 3 | `features/characters-away.md` | **At ship time only** (not in this skill) — distilled summary once merged. |

---

## 11. Tests

**Backend:** none (no backend change; integration-test mandate N/A per §2).

**Frontend:** `monkify-front` has **no test runner** (`package.json` = `serve`/`build`/`lint` only).
Do not add one for this card. Verification:

1. **Typecheck + lint:** `yarn lint` and `yarn build` (Vue CLI + `tsc`) must pass with the new
   `utils/nearMiss.ts`, `hooks/useNearMiss.ts`, and edited files.
2. **Manual QA:** §13.
3. **Future-proofing (no runner wired):** the detector in `utils/nearMiss.ts` is a pure function, so when
   a runner is later added these cases should be covered — capture them here as the intended spec of the
   logic:

| Scenario | Input (choice `L`, stream) | Expected |
| --- | --- | --- |
| Ordered 3→2→1 | `jaguar` (6), stream `…jag…jagu…jagua` | pins fire `3 AWAY (jag)`, then `2 AWAY (jagu)`, then `1 AWAY (jagua)`, in that order |
| Once per level | `jaguar`, stream reaches `jag` twice | `3 AWAY` fires only the first time |
| Monotonic on regression | `jaguar`, `…jagu` then run breaks (`jagz`) | after `2 AWAY`, no pin removed; `k` drops but pins persist |
| Skip meaningless level | choice `L = 3`, e.g. `abc` | level 3 (`p = 0`) never fires; levels 2 (`p=1`), 1 (`p=2`) fire |
| Independent choices | two own choices `jaguar`, `condor` | each tracks + pins independently |
| Duplicate chars | choice `sunny` (dup `n`) | failure table handles it; correct `k` progression |
| Freeze on Ended | `freeze()` then `feed()` | `feed` is a no-op; pins unchanged |
| Reset on rollover | `reset()` | matchers cleared, `pins = []`, `frozen = false` |

---

## 12. Acceptance criteria

Mapped 1:1 from the Trello **Acceptance Criteria** checklist (metric interpreted per §1.1 — tail-prefix,
not window Hamming):

1. **Given** the player committed a choice of length `L` this game, **when** the stream's trailing
   correct-prefix first reaches `L − 3` (i.e. 3 away), **then** the matched characters
   (`choice.substring(0, L−3)`) are highlighted in the terminal and a **"3 AWAY"** beat is shown for that
   choice.
2. **Given** a committed choice, **when** the prefix first reaches `L − 2` (2 away) and later `L − 1`
   (1 away), **then** each level highlights its matched characters and shows its **"2 AWAY" / "1 AWAY"**
   beat the first time that distance is reached.
3. **Given** a milestone already fired for a `(choice, level)`, **when** that distance is reached again
   for that choice, **then** no new highlight or pin is created (each `(choice, level)` fires exactly
   once).
4. **Given** an existing highlight/pin, **when** the stream later moves to a worse (larger) distance for
   that choice, **then** all existing highlights and pins remain unchanged (monotonic — never removed).
5. **Given** a near-miss segment was pinned, **when** the live terminal stream grows past the crop
   threshold (20000 → last 2000 chars), **then** the pinned segment stays visible (exempt from cropping)
   until the game ends, while the live tail crops as before.
6. **Given** the player committed multiple choices this game, **when** different choices reach
   milestones, **then** each choice's near-misses are detected, highlighted, and pinned independently.
7. **Given** a game whose `ChoiceRequiredLength` is small enough that a level would yield zero matched
   characters (`L − N < 1`), **when** milestones are evaluated, **then** that level does not fire.
8. **Given** the round ends (status `Ended`), **when** the reward view is shown, **then** existing
   highlights and pins remain frozen and visible (not cleared); **and** when the game then rolls over,
   **when** the new game loads, **then** all highlights, pins, and tracked own choices are cleared.
9. **Given** the feature is active, **when** any near-miss occurs, **then** detection and rendering happen
   entirely client-side from the existing `<gameId>/Terminal` stream — with no backend call, no new
   SignalR event, and no change to the game outcome, payouts, or pacing.
10. **Given** a near-miss reveal is shown, **when** the player views it, **then** it is neutrally framed
    with **no** "bet again" / spend CTA attached to the reveal.

---

## 13. Manual QA

Run locally (crypto or regulated operator, dev launch buttons). Because a natural win/near-miss is
random, use a **short choice length** game (or a **preset-choice race**, whose typer pool is drawn
from the preset choices — see `ChimpOutTyper.SetCharactersOnTyper`) so the stream is biased toward the
choice characters and near-misses appear quickly.

1. Launch `/chimpout/game`, select a race, connect (or free-bet dev mode), place **one** bet (e.g. a
   6-letter word).
2. Pull the lever; while the stream runs, watch for `3 AWAY` → `2 AWAY` → `1 AWAY` beats with the matched
   partial word highlighted, appearing in order.
3. Let the stream run long enough to exceed the 20000-char crop; confirm earlier pins **stay visible**
   while the live tail keeps scrolling/cropping.
4. Place **two** different choices in one round; confirm each pins independently.
5. Let the round end; confirm pins persist through the `COMPLETE! / WINNER` reward view.
6. Wait for rollover to the next game; confirm all pins and tracked choices are **cleared**.
7. Confirm there is **no** "bet again"/spend CTA anywhere on the reveal.
8. `yarn lint` and `yarn build` pass.

---

## 14. Trello / pipeline

- **Source card:** [Characters Away — progressive near-miss highlight (3 / 2 / 1)](https://trello.com/c/vlarPOaw)
  — list **Specs created**; the **Implementation** section on the card lists branch + spec + worktree.
- **Next:** `/monkify-implement-feature Characters Away` to build it, then
  `/monkify-ship-feature Characters Away` to sync the KB and merge to `main`.

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-07-06 | Initial spec from refined card #30. Key spec-time clarification: the distance metric is the **tail-prefix progression (KMP partial-match)**, not positional window Hamming — confirmed with the product owner; this makes level-skipping impossible and levels fire in strict 3→2→1 order. Also documented the hidden blocker that `lastOwnBetChoice` is a one-shot marker consumed by `GameMonkey.vue`, so a new durable `ownBetChoices` list is added rather than reusing it. Noted `monkify-front` has no test runner → verify via lint/build/manual QA, detector written as a pure function for future testability. |

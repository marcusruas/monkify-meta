# Space Stroker — "Practice mode changes" — Implementation Spec

> **Status: Ready for implementation** — refined via Trello card
> [Practice mode changes](https://trello.com/c/EfIRqHbT) (#142, refined 2026-08-03).
> Repos affected: **`monkify-front`** and this doc in **`monkify-meta`**. **`monkify-backend` is not
> touched.**
> Branch / worktree: `feature/practice-mode-changes` → `.worktrees/practice-mode-changes/<repo>`.
> Level: **Feature** (see §2 — the mandatory integration test is **waived**, with reason).
> ⚠️ **Ship target: `release/ui-ux-changes`, not `main`.**
>
> **Purpose:** `/game/practice` shipped on 2026-08-03 as a faithful, unnarrated round that the player
> can lose. This card turns it into a **guided tutorial**: Edson narrates four scripted beats in a
> skippable talking bubble that holds the round between them, the player wins every time, and the
> character stream stops lagging behind itself.

---

## 0. ⚠️ This card REVERSES shipped decisions — read before touching the code

`features/practice-mode.md` and `specs/practice-mode.md` are the shipped truth for this surface, and
three of their stated rules are **deliberately overturned here**. The code carries guard comments
saying not to do exactly what this card asks for. Those comments are not stale — they were correct
until this card. **Rewrite them; do not work around them, and do not leave them contradicting the
code.**

| Shipped rule | Where it is asserted | What #142 does |
| --- | --- | --- |
| "The draw must never be biased toward the player" | `usePracticeGame.ts` — the `drawPracticeWinner` doc block | The winner is now **unconditionally** the player's choice (§7.1) |
| `MinimumNumberOfPlayers: 2` is "the only shape that can teach **LOSING**" | `constants/practiceGame.ts` — `PRACTICE_GAME` comment | Losing is now unreachable; the synthetic opponent survives only as the second bet the tutorial narrates |
| "Real timings, not compressed" — every phase derived from **one** timestamp | `features/practice-mode.md` → "The round"; `practicePhaseAt` doc block | The tutorial **pauses** that clock between beats (§7.5) |
| `PracticeTyper` is a line-for-line copy of `GameTyper.vue` | `PracticeTyper.vue` header comment | Its second render clock is **deleted** (§7.2) |

None of this relaxes the feature's **isolation** rule, which is untouched and still absolute: nothing
under `src/components/practice/`, `PracticePage.vue`, `usePracticeGame.ts` or `practiceGame.ts` may
import `useGame`, `useNearMiss`, `useGameHub`, `useGameChat`, `useIdleDisconnect`, `mainService` or
`solana-wallets-vue`, and no file on the real game screen may be edited.

---

## 1. Summary

Three independent changes to `/game/practice`, plus one shared piece of machinery (the pause).

| # | Change | Surface |
| --- | --- | --- |
| A | **The player always wins.** The uniform draw and the natural-hit override leave the outcome path; the lose branch is deleted. | `usePracticeGame.ts` |
| B | **The stream keeps up.** The terminal's scroll runs post-DOM and the typer's second render clock is deleted, so the newest character is always on screen. | `PracticeTyper.vue`, `PracticeTerminal.vue` |
| C | **Edson narrates.** A 4-step skippable talking bubble, an arrow at the console input, and Edson pinned to his max-speed GIF for the whole race. | new `usePracticeTutorial.ts` + `PracticeBubble.vue`, `PracticeConsole.vue`, `PracticeMonkey.vue`, `PracticePage.vue` |
| D | **The hold.** C requires the round clock to pause and resume; A and B do not. | `usePracticeGame.ts` |

| Beat | Today (shipped) | After #142 |
| --- | --- | --- |
| Page load | Cabinet, silent | Cabinet + **bubble 1** + arrow at the input |
| Player bets | Round clock starts | Same (bubble 1 clears) |
| +1500 ms | EDSON-BOT bets, countdown starts immediately | EDSON-BOT bets, **clock holds** on **bubble 2**; countdown starts on release |
| Countdown | 10 s | 10 s, unchanged (full duration after release) |
| Race starts | 12 s stream, Edson ramps `typing → typing2 → typing3` | Race starts, **clock holds** on **bubble 3** (stream keeps running behind it); Edson is `typing3` the whole run |
| Result | Win **or** lose, 50/50 | **Always a win**; **clock holds** on **bubble 4** |
| Rollover | 30 s, then a fresh round | 30 s starts on release; round 2 runs with **no bubbles** |

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **§2 Git — worktree per repo.** `feature/practice-mode-changes` lives in
  `.worktrees/practice-mode-changes/<repo>` for `monkify-front` + `monkify-meta`; the shared
  checkouts stay on `main`. Both branches were created off `origin/main` and pushed. Push after every
  further commit (`AGENTS.md` rule 5).
- **§3 Unit tests.** No backend change ⇒ **no `SpaceStroker.Tests` change**. `monkify-front` has **no
  test runner** (`package.json` exposes `serve` / `build` / `lint` / `lint:fix` only), so §11 is N/A
  and §13 is the verification.
- **`AGENTS.md` rule 8 / §3 Integration tests — WAIVED, deliberately.** The card is Level `Feature`,
  but the mandatory happy-path integration test has nothing to exercise: there is **no backend
  surface** — no endpoint, DTO, entity, migration, consumer, SignalR contract or game-flow change —
  and practice makes **zero network requests by construction**. Precedent: `specs/practice-mode.md`
  itself, plus `specs/analytics.md` and `specs/mobile-wallet-connect.md`. **Do not invent a backend
  surface to satisfy the rule.**
- **§6 User-facing messaging.** Practice reveals no system state. The bubble copy is fixed (§5.3) and
  the sticky money banner is **not** reworded (decision 3).
- **§8 Scope.** `/game`, `/game/play` and every real-game guard are untouched. No secrets, no config,
  no migrations.

Feature-specific, verified in the codebase:

- **Every viewport below `lg` runs `html.light-graphics`** (`usePerfMode.ts`: `lightMode = isMobile ||
  weakRenderer`). No `backdrop-filter`, `blur()`, `mix-blend-mode` or infinite animation on the bubble
  or the arrow.
- **The light-graphics animation kill list is an explicit allow-list**, not a blanket rule — see §7.7.
- **`monkify-front` builds fail on CRLF** (prettier `endOfLine: lf`). New files must be written LF.
- **Practice persists nothing** — no DB, no `localStorage`, no cookie. Decision 7 depends on this
  staying true.

---

## 3. Resolved product decisions

Carried from the refined card (answer mode: **short**). Numbering matches the card.

| # | Decision |
| --- | --- |
| 1 | **The winner is unconditionally the player's own choice.** `drawPracticeWinner` and the `naturalWinner` override leave the outcome path; the stream matcher stays because it still feeds the near-miss row. Chosen over weighting the draw or keeping 50/50 — a first-timer's single round must be interpretable. |
| 2 | **The lose path is deleted**, not left as dead code: the `lose.mp3` import and the win/lose ternary go; victory fires unconditionally. `> WINNER:` / `> PAID TO:` stay — they read from the bet list and need no branch. |
| 3 | **No new honesty copy.** The sticky banner is unchanged. Knowingly accepted: the screen never states the outcome is rigged; bubble 4's *"If this was a real game…"* carries the framing. |
| 4 | **Glitch root cause** (confirmed in code): a `flush: 'pre'` scroll watcher firing before the DOM is patched, compounded by a second render clock in `PracticeTyper` decoupled from the store's append tick. |
| 5 | **Fix = one clock + post-DOM scroll.** Delete `updatedOutput` + its interval, render `terminalOutput` directly, scroll post-flush. Knowingly accepted: `PracticeTyper` stops mirroring `GameTyper.vue`. The crop rule is unaffected and stays. |
| 6 | **Practice only.** `GameTerminal`/`GameTyper` are untouched — the real stream arrives in irregular SignalR batches where the refresh-interval indirection earns its keep. A separate card if the live screen has the same symptom. |
| 7 | **The tutorial runs once per page entry, first round only.** Not per-round (an unbounded practice loop would become an unbounded tutorial loop) and not persisted (a `localStorage` flag would break the "persists nothing" rule). Leaving and returning replays it. |
| 8 | **Each bubble HOLDS the round.** Bubble 1 gates naturally; bubble 2 holds the 10 s countdown; bubble 3 holds the race; bubble 4 holds the 30 s rollover. ⚠️ Raised and accepted: this breaks the "real timings, never compressed" property and requires pause/resume on the single-timestamp clock — implemented as an **accumulated-pause offset** (§7.5), never a second clock. |
| 8a | **The character stream KEEPS RUNNING behind bubble 3** (owner feedback on the deployed build, 2026-08-03 — this reverses the original decision 8, which froze the stream). Edson must visibly keep typing while the bubble says he can't talk. Only the **clock** is frozen, which is what stops the round *ending* behind the bubble; the stream and the near-miss row advance normally. |
| 8b | **`runMs` 12000 → 10000** (same feedback), so the round is over **within 10 s** of pressing `CONTINUE` on bubble 3. Because that bubble engages on the first tick of the run and the clock is frozen while it is up, the time left after release *is* `runMs` — the cap and the constant are the same number. |
| 9 | **Skip = per-step button + a separate "skip all".** The per-step button reads `SKIP` on bubble 1 (it genuinely skips typing your word) and `CONTINUE` on bubbles 2–4, which request no action. "Skip all" dismisses every remaining bubble and releases every remaining hold. |
| 10 | **Skipping bubble 1 auto-bets a random word from a NEW pool**, disjoint from `PRACTICE_OPPONENT_WORDS`. Chosen over one fixed word (reads canned on a second visit) or padding partial input (produces nonsense). |
| 11 | **Bubble copy is verbatim**, including `(Key)Stroking` (§5.3). |
| 12 | **Mobile: the bubble is an overlay**, absolutely positioned above Edson's band rather than stealing layout height from the three-band column. |
| 13 | **The arrow animates normally and is static under `html.light-graphics`** — no second exception to the infinite-animation rule. |
| 14 | **Edson is `monkeyTyping3.gif` for the whole `InProgress` phase.** `displaySpeed` and `PRACTICE_CHARS_PER_TICK` are untouched, so the stream still ramps. Accepted: Edson and the stream speed stop agreeing. |
| 15 | **Audio keeps playing through every hold** — `PracticeAudio` gains no tutorial dependency. |

---

## 4. Data model

**N/A.** No entity, column, enum, EF mapping or migration — `monkify-backend` is not touched.
Practice persists nothing (decision 7).

---

## 5. Configuration

No `GeneralSettings`, no env var, no secret, no seed data. Everything lives in
`monkify-front/src/constants/practiceGame.ts`.

### 5.1 Added

| Constant | Value | Why |
| --- | --- | --- |
| `PRACTICE_PLAYER_WORDS` | ≥ 12 lowercase 6-letter words, **disjoint from `PRACTICE_OPPONENT_WORDS`** | The pool `SKIP` draws from (decision 10). Disjointness is a second guard on top of the store's existing `pickOpponentWord(avoid)` filter. Suggested: `banana`, `rocket` is taken → use e.g. `edsons`, `typing`, `wallet`, `arcade`, `chimps`, `stroke`, `pixels`, `joypad`, `tokens`, `winner`, `crater`, `helmet`. |
| `PRACTICE_TUTORIAL_LINES` | The 4 verbatim strings (§5.3) | One place to read the copy. |
| `PracticeHoldBoundary` (type) | `'before-countdown' \| 'race-started' \| 'result-shown'` | Names the three hold points (§7.5). |

### 5.2 Removed

| Constant | Why |
| --- | --- |
| `PRACTICE_TYPER_REFRESH_MS` | Its only consumer is the render clock deleted in §7.2. Leaving an unused export invites someone to reintroduce the bug. |

`PRACTICE_CROP_THRESHOLD` / `PRACTICE_CROP_KEEP` / `PRACTICE_CROP_INTERVAL_MS` **stay** — the crop
rule is unaffected.

### 5.3 Bubble copy — verbatim, do not reword

| # | Trigger | Copy | Button |
| --- | --- | --- | --- |
| 1 | Page mount, before the player's bet | `Hey there! Type a random word in the terminal for me really quick` | `SKIP` |
| 2 | EDSON-BOT's bet lands | `Looks like another person has typed something as well, prepare yourself for the round!` | `CONTINUE` |
| 3 | The race starts | `I can't talk much right now, I'm (Key)Stroking! Root for me to type your word first, if I do, you win!` | `CONTINUE` |
| 4 | The result is on screen | `Ouch! sorry my head regenerated, but congratulations! If this was a real game, the money would already be in your wallet.` | `CONTINUE` |

A `skip all` affordance is present on all four (§7.8).

---

## 6. Backend behaviour

**None.** No entity, enum, handler, consumer, EF mapping, migration, endpoint, header, SignalR
contract or config. `monkify-backend` has no branch and no worktree for this card.

---

## 7. Frontend behaviour (`monkify-front`)

### 7.1 Always-win

In `usePracticeGame.tick()`, the `Ended` beat currently resolves
`const winner = naturalWinner ?? drawnWinner;`. Replace with the player's own choice:

```ts
if (!endHandled && phase >= GameStatus.Ended) {
  endHandled = true;
  // #142: practice is a tutorial, not a game — the player always wins so the round is
  // interpretable to someone seeing Space Stroker for the first time. The draw and the
  // natural-hit path are gone from the outcome; the stream matcher survives only to drive
  // the near-miss row.
  const winner = ownChoice.value;
  winnerChoice.value = winner;
  if (winner) terminalOutput.value += winner;
  finishNearMiss(winner);
  if (audioTabActive()) playAudio(victoryAudio);
}
```

Consequences the implementer must carry through:

- **Delete `drawPracticeWinner`** and its export, the `drawnWinner` field, the `if (drawnWinner ===
  null)` block inside the `InProgress` branch, and its reset in `resetRound()`.
- **Delete `naturalWinner`** and the two `naturalWinner === null` assignments inside `feedStream()`.
  ⚠️ **Keep `opponentMatcher` and the `advance()` call for it only if something still reads it — it
  does not.** Delete `opponentMatcher`, its `createMatcher` call in `tick()` and its reset; the
  player's `matcher` is the only one with a consumer (the near-miss row). This removes a per-character
  loop from the hot path.
- **Delete the `lose.mp3` import** and the win/lose ternary (decision 2). `playerWon` becomes
  permanently `true` — keep the computed (the terminal's `v-if="playerWon"` reads it) rather than
  hard-coding `true` in the template, so a future change has one place to touch.
- **Rewrite the guard comments** listed in §0. The `drawPracticeWinner` doc block goes with the
  function; the `PRACTICE_GAME` "teach LOSING" comment must be corrected in place.
- `winnerLabel` still resolves through the bet list and will now always read `YOU`. Leave it
  data-driven.

### 7.2 The stream keep-up fix

**Root cause, confirmed in the shipped code:**

1. `PracticeTerminal.vue` runs `watch(terminalOutput, scrollToBottom)`. Vue's default is
   `flush: 'pre'`, so the callback fires **before** the component tree is patched — it scrolls to a
   `scrollHeight` that does not yet include the new text.
2. The text on screen is not `terminalOutput` at all. `PracticeTyper.vue` copies it into its own
   `updatedOutput` on a **second** interval (`PRACTICE_TYPER_REFRESH_MS`, 50/35/25 ms) that is
   decoupled from the store's fixed 50 ms append, so the rendered string can be a tick stale
   regardless of scrolling.

When the stream stops, the `status` watcher's `scrollToBottom()` + `requestAnimationFrame` finally
catches up — which is exactly the reported "never keeps up until it reaches the end".

**Fix — `PracticeTyper.vue`:** delete `updatedOutput`, `refreshInterval`, `startRefreshInterval`, the
`displaySpeed` watcher and the `PRACTICE_TYPER_REFRESH_MS` import. `winningTail` and `streamBody`
compute from `practice.terminalOutput.value` directly, and the template renders them. **Keep** the
crop interval and `onUnmounted`'s `clearInterval(cropInterval)`.

This is not more work for the browser: the store appends on a fixed 50 ms tick, so at speed 1 the
render rate is unchanged and at speeds 2 and 3 the deleted 35/25 ms interval was re-reading unchanged
data — strictly fewer renders.

**Fix — `PracticeTerminal.vue`:**

```ts
// #142: post-flush. At the default `flush: 'pre'` this ran BEFORE Vue patched the DOM, so it
// scrolled to a height that did not yet include the new characters and the view sat permanently
// behind the stream.
watch(terminalOutput, scrollToBottom, { flush: 'post' });
```

⚠️ **Deviation from the card's wording, stated deliberately.** The card records the fix as
"`flush: 'post'` + rAF". Post-flush already runs after the DOM is patched, so an extra
`requestAnimationFrame` here would only re-introduce a frame of delay. The **existing** `status`
watcher keeps its `scrollToBottom()` + rAF pair unchanged — there the layout itself changes (the
result block appears), so the second pass after the next frame is doing real work.

Everything else in the terminal is untouched: the sticky banner, the near-miss dock, the
`overflow-hidden` during `InProgress`, and the `#practice-*` element ids.

### 7.3 Edson at max speed

`PracticeMonkey.vue`: on the `InProgress` transition set `currentAnimation.value = typing3` (was
`typing`), and **delete the `displaySpeed` watcher** — it exists only to select the ramp GIF. Drop
`displaySpeed` from the destructure and drop the now-unused `typing`/`typing2` imports **only if**
nothing else references them; they are still needed in the `animations` preload array, so keep the
imports and keep both files in `animations` (the preload cost is unchanged and dropping them would
change what is warmed).

`PRACTICE_CHARS_PER_TICK` and the store's `displaySpeed` ramp are **untouched** (decision 14) — the
stream still accelerates, and `displaySpeed` still drives nothing else after this change except the
stream itself.

### 7.4 The tutorial — state

New **factory**, `monkify-front/src/hooks/usePracticeTutorial.ts`, created by `PracticePage` and
shared through `provide`/`inject` under a new `PRACTICE_TUTORIAL_KEY` in `constants/keys.ts`.

⚠️ **It must be a factory, never a module singleton** — the same reason `usePracticeGame` is one
(`specs/practice-mode.md` §7.2): module state survives unmount, so a stale tutorial would bleed
across navigations and decision 7 ("replays on every page entry") would silently become "runs once
per browser session".

```ts
export type PracticeTutorialStep = 1 | 2 | 3 | 4 | null; // null = finished or skipped

export interface PracticeTutorial {
  step: Ref<PracticeTutorialStep>;
  /** The line to render, or null when no bubble is up. */
  line: ComputedRef<string | null>;
  /** `SKIP` on step 1, `CONTINUE` on 2–4. */
  buttonLabel: ComputedRef<string>;
  /** Step 1 only: the arrow at the console input. */
  showArrow: ComputedRef<boolean>;
  skipStep: () => void;
  skipAll: () => void;
  dispose: () => void;
}
```

**Dependency direction is one-way: the tutorial reads and drives the store; the store never imports
the tutorial.** The store exposes three new members (§7.5) and the tutorial consumes them.

Step resolution:

| Step | Shown when | Cleared by |
| --- | --- | --- |
| 1 | `status === WaitingBets && !ownChoice` and the tutorial has not advanced past 1 | the player's bet landing (watch `ownChoice`), `SKIP`, or `skipAll` |
| 2 | `heldAt === 'before-countdown'` | `CONTINUE` / `skipAll` → `releaseHold()` |
| 3 | `heldAt === 'race-started'` | `CONTINUE` / `skipAll` → `releaseHold()` |
| 4 | `heldAt === 'result-shown'` | `CONTINUE` / `skipAll` → `releaseHold()` |

After step 4 releases, the tutorial sets `step = null` and `holdsEnabled = false`, so the rollover
into round 2 runs with no bubbles (AC 21).

### 7.5 ⚠️ The hold — pause/resume on the round clock

This is the most delicate part of the card. The shipped store derives **every** phase, the countdown
number, the speed ramp and the rollover deadline from **one** origin (`roundStartedAt`) on a single
50 ms interval, precisely so a backgrounded tab resyncs instead of drifting. That property must
survive. **Implement the hold as an accumulated-pause offset on `elapsed` — never as a second clock,
and never by stopping and restarting `clockTimer`.**

```ts
// ---- Pause state (#142). One offset, applied to the single origin. ----
let pausedAt: number | null = null;   // wall clock when the current hold engaged
let pausedTotalMs = 0;                // total held time this round
const clockNow = () => (pausedAt ?? Date.now()) - pausedTotalMs;
const elapsedMs = () => clockNow() - roundStartedAt;
```

Every existing `const elapsed = now - roundStartedAt` becomes `elapsedMs()`. `resetRound()` clears
both fields.

New store members:

| Member | Type | Purpose |
| --- | --- | --- |
| `holdsEnabled` | `Ref<boolean>` | The tutorial sets it `true` on mount and `false` when it finishes or is skipped. `false` ⇒ no hold ever engages. |
| `heldAt` | `Ref<PracticeHoldBoundary \| null>` | Which boundary is currently holding; the tutorial watches it to pick the bubble. |
| `releaseHold()` | `() => void` | Resumes the clock. No-op when nothing is held. |

`releaseHold()`:

```ts
const releaseHold = () => {
  if (pausedAt === null) return;
  const heldFor = Date.now() - pausedAt;
  pausedTotalMs += heldFor;
  pausedAt = null;
  heldAt.value = null;
  // ⚠️ The near-miss display layer measures on the RAW wall clock, not on `elapsed`. Without this
  // rebase a hold longer than PRACTICE_NEAR_MISS_STALL_MS (2000) reads as a stall the instant the
  // round resumes and the row snaps back to 0 AWAY. Bubble 3 is held for as long as the player
  // takes to read it, so this is not a corner case — it is the common path.
  lastImproveAt += heldFor;
  lastStallCheckAt += heldFor;
};
```

**The three hold points.** Two kinds, because bubble 2 must land *before* its phase boundary and the
other two *after* theirs:

| Boundary | Kind | Engages | Why that kind |
| --- | --- | --- | --- |
| `before-countdown` (bubble 2) | **before** the status commit | On the tick that places EDSON-BOT's bet, i.e. the first tick where `elapsedMs() >= opponentBetMs`. Engage the pause and **`return` without writing `status`**. | The phase stays `NotEnoughPlayersToStart`, so the `game_starting` jingle has not fired, `PracticeAudio` keeps the waiting loop, `GLASSES_DELAY_MS` has not been scheduled, and on release the countdown starts cleanly at 10 (AC 15). Committing `GameStarting` first would fire a 10 s-aligned jingle against a frozen countdown. |
| `race-started` (bubble 3) | **after** the status commit | On the tick that first writes `status = InProgress`: commit normally (Edson flips to `typing3`, the running loop starts, one batch is emitted), then engage the pause at the end of that tick. ⚠️ **The stream is exempt from this pause** (decision 8a): while `heldAt === 'race-started'`, `tick()` still calls `emitRaceBatch()` and runs the near-miss stall check before returning. | Decisions 8a/8b: the race must look alive behind the bubble, but must not *end* behind it. |
| `result-shown` (bubble 4) | **after** the status commit | On the tick that first writes `status = RewardForWinnersCompleted` (the beat `finishedTyping` flips and the result block renders): commit, then engage. | The player must see the result the bubble is congratulating them for. Freezing here also stops the rollover, since `elapsedMs()` can no longer reach `roundLengthMs()`. |

Each boundary fires **at most once per round**, tracked in a `Set<PracticeHoldBoundary>` cleared by
`resetRound()`. `tick()` begins with `if (pausedAt !== null) return;` — while held, nothing is
recomputed, no characters are emitted and the rollover cannot be reached.

**What must NOT be paused** (verified against the shipped code):

- `PracticeAudio.vue` — status-driven, and no hold changes `status`, so the correct loop keeps
  playing through every hold with zero changes to the file (decision 15, AC 29).
- `PracticeMonkey.vue`'s `later()` timeouts (`finale` 3200 ms → `regen` 1250 ms → `idle`) — these run
  on the wall clock through the bubble-4 hold, which is *correct*: bubble 4 opens with "sorry my head
  regenerated".
- The 80 ms `fillTimer` win reveal, which runs during `Ended` and completes before bubble 4's hold at
  `RewardForWinnersCompleted`.

**Known and accepted:** a GIF's playback cannot be paused, so during the bubble-3 freeze Edson keeps
visibly typing while the stream is frozen. This was raised in refinement and accepted — **do not
"fix" it** by swapping to a still frame.

**The lever during bubble 4.** `nextRound()` is gated on `status === RewardForWinnersCompleted`, which
is exactly the held state, so the lever is live. It must `releaseHold()` and end the tutorial before
`resetRound()`:

```ts
const nextRound = () => {
  if (status.value !== GameStatus.RewardForWinnersCompleted) return;
  releaseHold();   // #142: the lever must never look broken (PracticeConsole's stated rule)
  resetRound();
};
```

The tutorial watches `step` reaching `null` via `heldAt` clearing, so a lever-driven rollover also
ends the tutorial.

### 7.6 The bubble — `PracticeBubble.vue`

A new practice-owned component under `src/components/practice/`. Contents: the line, the primary
button (`SKIP` / `CONTINUE`), and a smaller secondary `skip all` control. A downward tail points at
Edson.

**Rendering location: `PracticePage.vue`, inside each layout branch** — not inside `PracticeMonkey`.
Two shipped facts force this, and both will silently break an implementation that nests it in the
monkey:

1. `PracticeMonkey`'s root carries `max-lg:overflow-hidden`, which **clips** anything positioned
   above the GIF on mobile.
2. On mobile `PracticePage` renders `<practice-monkey v-show="!betInputFocused" …>` — Edson is hidden
   whenever the native keyboard is open. A bubble nested inside him would vanish mid-hold, and since
   the **only** way to release a hold is the button inside the bubble, bubbles 2–4 would become
   unreachable and the round would be stuck.

⚠️ Both layout branches are part of `PracticePage`'s `v-if`/`v-else-if`/`v-else` chain, which Vue
treats as a **single root** that inherits `class="mx-auto h-5/6 block"` from `App.vue`'s
`RouterView`. Mount the bubble **inside** each branch, exactly as `PracticeAudio` already is — a
fourth sibling root makes the component a fragment, the class is dropped, and the desktop `h-full`
resolves against nothing (a blank page).

Positioning — starting values, to be tuned in the §13 QA against the invariants below:

| Branch | Anchor | Suggested |
| --- | --- | --- |
| Desktop (`>= lg`) | the existing `div.overflow-hidden.flex.w-full.relative` | `absolute right-[4%] top-[14%] w-[min(340px,32%)] z-30` |
| Mobile (`< lg`) | the existing `div.relative.flex.h-[calc(100dvh-54px)]…` column root | `absolute inset-x-3 bottom-[clamp(150px,24dvh,205px)] z-30` — i.e. immediately above the console band, so it sits over Edson whether or not he is rendered |

Invariants (these are what QA checks, not the numbers):

- Paints **above** the terminal's `.practice-banner` (`z-index: 1`) and `.near-miss-dock`, and above
  the mobile console (`z-20`).
- Never overlaps the lever or the word input — both must stay tappable while a bubble is up.
- Adds **no** height and **no** scroll to the mobile three-band column (it is absolutely positioned).
- Survives `betInputFocused` toggling on mobile.

Styling: flat and opaque, in the arcade skin — VT323 body at ≥ 16 px, a 2 px `--color-line` border, an
opaque `--color-void` fill, and a CSS-triangle tail. **No `backdrop-filter`, no `blur()`, no
`mix-blend-mode`, no infinite animation** (§2). A one-shot entry transition is fine; gate it behind
`@media (prefers-reduced-motion: reduce)` as the other practice keyframes already are, and declare
any keyframes locally with a `practice-` prefix (`specs/practice-mode.md` §7.8 — depending on
`GameMonkey`'s or `GameTerminal`'s keyframes is invisible coupling).

**Mobile behaviour worth stating:** bubble 1 is positioned above the console band, so it stays
visible while the player types. If tuning ever moves it into Edson's band, it must still not depend
on `v-show="!betInputFocused"`.

### 7.7 The arrow

Rendered by **`PracticeConsole.vue`**, not as a free-floating overlay: `#practice-word-input` lives
inside that component in *both* the mobile and desktop branches, and both already have a
`position: relative` wrapper around the visor. Anchoring it there removes all cross-subtree
positioning math and makes it work on both layouts for free.

`PracticeConsole` injects `PRACTICE_TUTORIAL_KEY` and renders the arrow under
`v-if="tutorial.showArrow"` (step 1 only). It disappears the moment bubble 1 resolves — by the bet
landing, by `SKIP`, or by `skip all`.

⚠️ **The light-graphics kill list is an explicit allow-list, not a blanket rule.** `arcade.css`
(~line 1083) reads:

```css
html.light-graphics .blink,
html.light-graphics .blink-fast,
html.light-graphics .lottery-badge,
html.light-graphics .retro-bar::before,
html.light-graphics .pixelated-card:hover .card-content {
  animation: none !important;
}
```

A new animated class is **not** stripped automatically. Decision 13 therefore requires adding
`html.light-graphics .practice-arrow` to that selector list — a one-line change to the shared
stylesheet, and the correct place for it: the file's own header explains that classes declared in
`<style scoped>` blocks must be overridden from here, because a `html.light-graphics …` rule written
inside the component would land on the wrong element.

Do **not** add the arrow to the `.animate-wobble` exception below that block — that exception exists
for the mascot and taking a second one was explicitly rejected in refinement.

### 7.8 Skip semantics

| Control | Step 1 | Steps 2–4 |
| --- | --- | --- |
| Primary button | `SKIP` — places a bet with a random `PRACTICE_PLAYER_WORDS` entry via the store's existing `placeBet()`, then advances | `CONTINUE` — `releaseHold()` |
| `skip all` | Places the same auto-bet (otherwise the round would never start), then sets `holdsEnabled = false`, calls `releaseHold()` and sets `step = null` | Sets `holdsEnabled = false`, calls `releaseHold()`, sets `step = null` |

After `skip all` the round continues at its **real** timings with no further bubbles (AC 19).

The auto-bet goes through `placeBet()` unchanged — it already validates length, the `[a-z]` charset
and `canBet`, and it already fires `ownBetSignal` (Edson's call GIF + `phone_beep.mp3`). Words from
`PRACTICE_PLAYER_WORDS` satisfy the validation by construction; the store's `pickOpponentWord(avoid)`
still filters the player's word out of the opponent pool, so the two can never collide even if the
pools are edited to overlap later.

### 7.9 Teardown, self-exclusion, embed

- **`dispose()`** — `PracticePage.onUnmounted` calls `practice.dispose()` today; it must now also call
  `tutorial.dispose()`, which clears any timer the tutorial owns and releases any hold. Leaving
  practice mid-hold must leave nothing running (AC 27).
- **Self-exclusion** — the blocked branch renders the RG refusal and mounts neither `PracticeAudio`
  nor the cabinet. The bubble and arrow must not mount there either; because both are rendered inside
  the two cabinet branches, this is satisfied by construction (AC 26).
- **Embed** — `/game/practice` is already in `EMBED_REDIRECT_ROUTES` and redirects before the
  component mounts. Nothing to do.
- **Backgrounded tab** — `clockNow()` freezes at `pausedAt`, so a tab backgrounded mid-hold
  accumulates nothing and resumes exactly where it paused (AC 28).

---

## 8. Flow

```mermaid
sequenceDiagram
    participant P as Player
    participant T as usePracticeTutorial
    participant S as usePracticeGame
    participant U as Cabinet (terminal/Edson/audio)

    Note over T,U: page mount — holdsEnabled = true, step 1
    T->>U: bubble 1 + arrow at #practice-word-input
    P->>S: types a word, pulls the lever (or SKIP → auto-bet)
    S->>S: roundStartedAt = now, clock starts
    T->>U: bubble 1 clears

    Note over S: elapsed >= opponentBetMs
    S->>U: EDSON-BOT bet appears
    S->>S: HOLD before-countdown (status stays NotEnoughPlayersToStart)
    T->>U: bubble 2 — waiting loop still playing, no jingle
    P->>T: CONTINUE
    T->>S: releaseHold() — rebase near-miss timestamps
    S->>U: countdown 10 → 1, jingle, glasses at 8800 ms

    S->>U: status = InProgress — Edson typing3, running loop, first batch
    S->>S: HOLD race-started (clock frozen, STREAM KEEPS RUNNING)
    T->>U: bubble 3
    P->>T: CONTINUE
    T->>S: releaseHold()
    S->>U: the full 10 s run plays out, near-miss row climbs

    S->>U: Ended — winner = own choice, victory.mp3, row fills to 0 AWAY
    S->>U: status = RewardForWinnersCompleted — result block
    S->>S: HOLD result-shown (rollover frozen)
    T->>U: bubble 4
    P->>T: CONTINUE (or pulls the lever)
    T->>S: releaseHold(); step = null; holdsEnabled = false
    S->>U: 30 s hold, then round 2 — no bubbles
```

---

## 9. Out of scope

- Any backend change; any `/game` lobby or `/game/play` change; any change to real-game behaviour or
  its guards.
- Fixing the same stream-lag pattern on the live `GameTerminal`/`GameTyper` (decision 6) — a separate
  card if the live screen shows the symptom.
- Preset or lottery practice rounds; practice leaderboard, stats, history, or any persistence of
  practice results.
- Simulated provably-fair content of any kind on the practice screen — still forbidden: the browser
  generates both sides, so any fairness display would be fabricated evidence.
- A "play for real" CTA on the practice screen.
- Rewording the sticky banner (decision 3) or the result-block copy.
- Changing `PRACTICE_CHARS_PER_TICK`, the crop rule, or the 10 s countdown / 30 s rollover timings. (`runMs` **is** changed — 12000 → 10000, decision 8b.)

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

**None.**

### 10.2 Frontend (`monkify-front`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/hooks/usePracticeGame.ts` | Winner = `ownChoice` (§7.1); delete `drawPracticeWinner`, `drawnWinner`, `naturalWinner`, `opponentMatcher` and the `lose.mp3` import; add `pausedAt`/`pausedTotalMs`/`clockNow()`/`elapsedMs()`, `holdsEnabled`, `heldAt`, `releaseHold()` and the three hold points (§7.5); rebase `lastImproveAt`/`lastStallCheckAt` on release; `nextRound()` releases the hold; rewrite the §0 guard comments. |
| 2 | `src/hooks/usePracticeTutorial.ts` | **New.** The 4-step factory: `step`, `line`, `buttonLabel`, `showArrow`, `skipStep()`, `skipAll()`, `dispose()` (§7.4, §7.8). |
| 3 | `src/constants/practiceGame.ts` | Add `PRACTICE_PLAYER_WORDS`, `PRACTICE_TUTORIAL_LINES`, `PracticeHoldBoundary`; **delete** `PRACTICE_TYPER_REFRESH_MS`; correct the `PRACTICE_GAME` "teach LOSING" comment (§5). |
| 4 | `src/constants/keys.ts` | Add `PRACTICE_TUTORIAL_KEY` (type-only import, as `PRACTICE_GAME_KEY` is). |
| 5 | `src/components/practice/PracticeBubble.vue` | **New.** Line, `SKIP`/`CONTINUE`, `skip all`, tail, flat/opaque styling, local `practice-`-prefixed keyframes (§7.6). |
| 6 | `src/components/practice/PracticeTyper.vue` | Delete `updatedOutput`, `refreshInterval`, `startRefreshInterval`, the `displaySpeed` watcher and the `PRACTICE_TYPER_REFRESH_MS` import; render `terminalOutput` directly; keep the crop interval (§7.2). |
| 7 | `src/components/practice/PracticeTerminal.vue` | `watch(terminalOutput, scrollToBottom, { flush: 'post' })`; leave the `status` watcher (and its rAF) alone (§7.2). |
| 8 | `src/components/practice/PracticeMonkey.vue` | `typing3` on the `InProgress` transition; delete the `displaySpeed` watcher; keep the preload array intact (§7.3). |
| 9 | `src/components/practice/PracticeConsole.vue` | Inject the tutorial; render the `.practice-arrow` at the visor under `v-if="tutorial.showArrow"`, in **both** the mobile and desktop branches (§7.7). |
| 10 | `src/pages/PracticePage.vue` | Create + `provide` the tutorial; mount `<practice-bubble>` **inside each** cabinet branch (never as a fourth root); `tutorial.dispose()` on unmount (§7.6, §7.9). |
| 11 | `src/assets/styles/arcade.css` | Add `html.light-graphics .practice-arrow` to the infinite-animation kill list (~line 1083). One line (§7.7). |

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/practice-mode-changes.md` | **New** — this spec. |
| 2 | `INDEX.md` | Map row + "Feature currently in flight". |
| 3 | `features/practice-mode.md` | **At ship time.** Correct the unbiased-draw and "teach LOSING" claims, the "real timings, not compressed" rule, the `PracticeTyper`-mirrors-`GameTyper` fidelity rule, and add the tutorial. |
| 4 | `specs/practice-mode.md` | **At ship time.** Post-ship note pointing at this spec for the reversed decisions (it is already flagged as a design archive). |

---

## 11. Unit tests

**N/A — no backend change**, so `SpaceStroker.Tests` is untouched and `SpaceStroker.Tests.Integration`
is waived with the reason in §2. `monkify-front` has no test runner.

Two pure, DOM-free helpers are worth keeping exported so a future runner can pin them without a
browser, in the spirit of the shipped `practicePhaseAt`:

| Export | Assertion a future runner would make |
| --- | --- |
| `practicePhaseAt(elapsedMs, timings)` | Unchanged by this card — the pause is applied to the input, not to the function. |
| `clockNow` / `elapsedMs` semantics | Held time never advances `elapsed`; `pausedTotalMs` is additive across multiple holds in one round. |

Verification is §13.

---

## 12. Acceptance criteria

Copied 1:1 from the Trello **Acceptance Criteria** checklist (31 items).

1. Given a practice round reaches its end, when the winner is decided, then the winner is unconditionally the player's own choice — neither the uniform draw nor a natural stream hit can change it.
2. Given ten consecutive practice rounds are played, when each result renders, then the player won all ten and `> YOU WON! CONGRATS!` appeared every time.
3. Given the round ends, when the result audio fires, then `victory.mp3` plays; and `lose.mp3` is imported nowhere under `src/components/practice/` or `src/hooks/usePracticeGame.ts`.
4. Given the round ends, when the near-miss row and the stream tail are compared, then the row fills to `0 AWAY` on the player's choice and the highlighted tail at the end of the stream is that same choice — they never contradict each other.
5. Given the always-win change is shipped, when `usePracticeGame.ts`, `features/practice-mode.md` and `specs/practice-mode.md` are read, then no comment or doc line still claims the draw is unbiased or that practice teaches losing.
6. Given the race is running, when the stream is watched at any point in the run, then the most recently appended character is visible at the bottom of the terminal — the view never trails behind and never "snaps" to the end when the run finishes.
7. Given `PracticeTyper.vue` after the fix, when it is read, then `updatedOutput` and its `PRACTICE_TYPER_REFRESH_MS` interval are gone and the component renders `terminalOutput` directly.
8. Given `terminalOutput` passes 20 000 characters during the run, when the crop interval fires, then it is cropped to the last 2 000 characters and the view stays pinned to the latest character across the crop.
9. Given the branch diff, when it is reviewed, then `GameTerminal.vue`, `GameTyper.vue`, `GameConsole.vue`, `GameMonkey.vue` and `useGame.ts` are unchanged.
10. Given the race is in progress, when Edson is rendered, then `monkeyTyping3.gif` is shown for the entire `InProgress` phase while the character stream still ramps 120 → 220 → 400 per tick.
11. Given a player opens `/game/practice`, when the page mounts, then bubble 1 appears above Edson reading "Hey there! Type a random word in the terminal for me really quick" and an arrow points at `#practice-word-input`.
12. Given bubble 1 is up, when the player types a valid 6-letter word and pulls the lever, then the bet lands and both the bubble and the arrow disappear.
13. Given bubble 1 is up, when the player presses `SKIP` inside the bubble, then a random word from the practice pool is placed as the player's bet, it is never equal to EDSON-BOT's word, and the tutorial advances.
14. Given the player's bet has landed, when EDSON-BOT's bet appears, then bubble 2 shows "Looks like another person has typed something as well, prepare yourself for the round!" and the 10 s countdown does not start while it is up.
15. Given bubble 2 is released with `CONTINUE`, when the countdown begins, then it starts at 10 and ticks down to 1 — the full duration, with no time consumed while the bubble was held.
16. Given the countdown reaches zero and the race starts, when bubble 3 appears with "I can't talk much right now, I'm (Key)Stroking! …", then the **character stream keeps running** behind it while the round clock stays frozen — so the round cannot reach its result until the bubble is released.
17. Given bubble 3 is released with `CONTINUE`, when the race plays out, then the round reaches its result in **at most 10 seconds**.
18. Given the player wins, when bubble 4 appears with "Ouch! sorry my head regenerated, but congratulations! …", then the 30 s rollover hold does not begin until it is released.
19. Given any bubble is up, when the player uses "skip all", then every remaining bubble is dismissed, every remaining hold is released, and the round continues at its real timings with no further bubbles.
20. Given a bubble is displayed, when its button is read, then it says `SKIP` on bubble 1 and `CONTINUE` on bubbles 2, 3 and 4, and the "skip all" affordance is present on all four.
21. Given the tutorial has completed or been skipped, when the round auto-rolls over into round 2, then no bubble and no arrow appear and the round runs at its real timings.
22. Given the player leaves `/game/practice` and navigates back, when the page mounts again, then the tutorial plays again from bubble 1.
23. Given a full tutorial-led round has been played, when the browser is inspected, then no `localStorage` key, no cookie and no network request (XHR or WebSocket) was created by practice mode or the tutorial.
24. Given a 390×844 mobile viewport, when any bubble is shown, then it overlays above Edson with a downward tail, is fully readable, paints above the sticky practice banner and the near-miss dock, and adds no height or scroll to the three-band column.
25. Given `html.light-graphics` is applied, when bubble 1's arrow and the bubble itself render, then neither uses an infinite CSS animation, `blur()`, `backdrop-filter` or `mix-blend-mode` — the arrow is static.
26. Given a self-excluded wallet is connected, when `/game/practice` loads, then the refusal panel renders and no bubble, arrow, round or audio starts.
27. Given a bubble is holding the round, when the player navigates away from `/game/practice`, then every timer — the round clock, the win-fill and the tutorial's own — is cleared and nothing keeps running.
28. Given a bubble is holding the round, when the tab is backgrounded for 30 s and refocused, then no held time was accumulated and the round resumes exactly where it was paused.
29. Given any hold is active, when the audio bed is checked, then it keeps playing uninterrupted and `PracticeAudio.vue` has no dependency on the tutorial.
30. Given the finished branch, when the isolation rule is checked, then nothing under `src/components/practice/`, `PracticePage.vue`, `usePracticeGame.ts` or `practiceGame.ts` imports `useGame`, `useNearMiss`, `useGameHub`, `useGameChat`, `useIdleDisconnect`, `mainService` or `solana-wallets-vue`, and the tutorial state is not a module singleton.
31. Given the finished branch, when the gates are run, then `yarn lint` and `yarn build` both pass in `monkify-front`.

---

## 13. Manual QA

`monkify-front` has no test runner, so this is the verification. Run the app locally and drive
`/game/practice`.

**Desktop 1440 × 900**

1. Load the page cold. Bubble 1 is up, the arrow points at the visor input. Type `monkey`, pull the
   lever → bubble and arrow clear, `> monkey (YOU)` appears (AC 11, 12).
2. EDSON-BOT's bet appears and bubble 2 opens. **Wait 30 s.** The countdown has not started, the
   status line still reads `> YOUR BET IS IN! NEED 1 MORE PLAYERS...`, the idle loop is still
   playing and the countdown jingle has **not** fired. Press `CONTINUE` → the countdown starts at
   **10** and ticks to 1 (AC 14, 15, 29).
3. The race starts and bubble 3 opens. Confirm the stream is frozen and Edson is on `typing3`. **Wait
   30 s**, then `CONTINUE` → the stream runs its **full** remainder and the near-miss row climbs
   normally. ⚠️ Specifically confirm the row does **not** snap back to `0/6` on release — that is the
   `lastImproveAt` rebase in §7.5 (AC 16, 17, 10).
4. Watch the stream throughout: the newest characters are always at the bottom, with no visible lag
   and no snap at the end (AC 6).
5. The result renders, `> YOU WON! CONGRATS!` and the victory sound fire, the row reads `0 AWAY` and
   the highlighted tail is your word. Bubble 4 opens; wait 40 s and confirm no rollover. `CONTINUE`
   → the 30 s hold runs and round 2 starts with **no** bubbles (AC 2, 4, 18, 21).
6. Repeat from a fresh page load and take the other paths: `SKIP` on bubble 1 (a pool word is bet,
   never EDSON-BOT's), and `skip all` from each of the four bubbles (AC 13, 19, 20).
7. Reload and navigate away mid-hold; confirm via DevTools that no interval or timeout survives
   (AC 27).
8. Play 10 rounds. The player wins all 10 (AC 2).
9. DevTools → Network + Application: zero XHR/WebSocket, no `localStorage` key, no cookie (AC 23).
10. Console shows 0 errors.

**Mobile 390 × 844 (device emulation, `html.light-graphics` active)**

11. Every bubble is readable, sits above Edson with a downward tail, paints over the terminal's
    banner and near-miss dock, and adds no page scroll (AC 24).
12. Focus the word input so the keyboard opens and Edson hides — **bubble 1 stays visible**, and the
    lever and input stay reachable (§7.6).
13. During bubbles 2–4, confirm the `CONTINUE` button is reachable with the keyboard open (this is
    the trap §7.6 exists to prevent).
14. Inspect the arrow: no running animation, no blur/backdrop-filter (AC 25).

**Regression**

15. `/game` and `/game/play` behave exactly as before; `git diff` touches none of the real-game files
    (AC 9).
16. `yarn lint` and `yarn build` pass (AC 31). ⚠️ Fix CRLF with `unix2dos`/`dos2unix` as the repo's
    prettier config requires LF.

**Not exercisable locally:** a genuinely self-excluded wallet (AC 26) — reason through the blocked
branch instead, as `specs/practice-mode.md` did.

---

## 14. Trello / pipeline

- **Source card:** [Practice mode changes](https://trello.com/c/EfIRqHbT) (#142) — list
  **Specs created**; the **Implementation** section on the card carries branch, worktree, spec path
  and level.
- **Next:** `/monkify-implement-feature 142` to build it, then `/monkify-ship-feature 142`.
- ⚠️ **Ship target is `release/ui-ux-changes`, not `main`** — recorded as `**Release branch:**` in the
  card's Implementation block, alongside cards #143 and #144.

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-08-03 | Initial spec from refined card #142 (15 decisions, 31 acceptance criteria). Adds §0 (reversed shipped decisions), §7.5 (the pause/resume clock and the near-miss timestamp rebase), and the §7.6 bubble-placement blockers found in the shipped code. |

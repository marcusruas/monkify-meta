# Feature — Practice mode

> Status: **shipped.** Authoritative spec: `../specs/practice-mode.md`
> (merged via `feature/practice-mode` on 2026-08-03).
> **Frontend-only** — `monkify-front`. No backend, API, SignalR or database surface.
> ⚠️ The spec was written before owner feedback during implementation; where the two disagree the
> **code is authoritative**. See "Deviations from the spec" below.

## What it does

A first-time visitor cannot see what Space Stroker is. Watching one round end requires a connected
wallet, an on-chain USDC transfer **and** a second player — every seeded type is
`MinimumNumberOfPlayers: 2`. `/game/practice` is a complete round that runs entirely in the browser:
no request, no socket, no wallet, no money. It is reached from the three Practice CTAs already on `/`,
and it works with the API down.

It is built to feel like the real game rather than to approximate it — same cabinet, same status copy,
same timings, same animations, same audio bed. **If the real game screen changes, practice should
change with it.**

## Core rules

### Entry and isolation

- Route **`Practice` at `/game/practice`**, registered in production. `HomePage.startPractice` is the
  only call site; `HomeHero` (both hero states) and `HomeFeatureCards` emit `practice` through it.
- **`/game` and `/game/play` are untouched.** No practice card in `GameTypes.vue`, and no practice
  branch in any money guard (`walletGateBlocks`, `allowBets`, `betLimitBlocks`, choice exclusivity).
  That isolation is the feature's central constraint — a diff inside `useGame` / `GameConsole` /
  `GameTerminal` / `GameMonkey` / `GamePage` is out of scope by construction.
- Nothing under `src/components/practice/`, `src/pages/PracticePage.vue`,
  `src/hooks/usePracticeGame.ts` or `src/constants/practiceGame.ts` may import `useGame`,
  `useNearMiss`, `useGameHub`, `useGameChat`, `useIdleDisconnect`, `mainService` or
  `solana-wallets-vue`. `PracticePage` uses `usePlayerPreferences` / `useSelfExclusion` /
  `useMobileWallet` for the exclusion gate **only**.
- **`usePracticeGame` is a factory shared by `provide`/`inject`**, deliberately departing from the
  repo's module-singleton idiom (`useGame`, `useNearMiss`, `useIdleDisconnect` are all module scope).
  Module state survives unmount, so a stale rollover timer or a second mount would bleed across
  navigations — and practice state could be mistaken for the live game's. The factory makes "practice
  state cannot outlive the practice page" structural rather than a rule to remember.
- Joins **`EMBED_REDIRECT_ROUTES`** in `main.ts`: a framed load redirects to `GameLobby` in
  `beforeEach`, so no pageview is recorded and the component never mounts.
- **Not an `isPlayerRoute()`** in `App.vue` — it renders no display name and resolves its own
  exclusion state, the pattern `ResponsibleGamingPage` uses.
- `/soon` and `ComingSoonPage.vue` are **deleted** — practice was their last consumer. A bookmarked
  `/soon?feature=practice` falls through the catch-all to `/error`; no redirect was added (it was
  never linked externally, and the app host serves `Disallow: /` plus `X-Robots-Tag: noindex`).

### The round — real timings, not compressed

| Phase | Status | Duration | Source of the number |
| --- | --- | --- | --- |
| Open | `WaitingBets` | until the player pulls the lever | — |
| Opponent bets | `NotEnoughPlayersToStart` | 1500 ms | practice-only |
| Countdown | `GameStarting` | **10 000 ms** | backend `GeneralSettings.Games.TimeUntilGameStarts` |
| Run | `InProgress` | 12 000 ms | inside the real race target (`RaceTargetMeanSeconds` 8–18 s) |
| Result | `Ended` → `RewardForWinnersCompleted` | 1200 ms, then the rest of the hold | — |
| Rollover | back to `WaitingBets` | total hold = **`VUE_APP_NEW_GAME_DELAY`** (30 s) | shared with `GameConsole` |

- The countdown is the **real 10 s**, not a compressed one. This is load-bearing beyond fidelity:
  `GameMonkey`'s `GLASSES_DELAY_MS = 8800` only lands inside a 10 s countdown, so Edson puts his
  glasses on 1.2 s before typing on both screens.
- The post-round hold reads the **same env var the real console uses**, so retuning the real rollover
  retunes practice with it.
- `RewardForWinnersInProgress` and `ErrorWhenProcessingRewards` are **never entered** — nothing is
  paid, so nothing waits and nothing fails. `> SENDING REWARDS...` is therefore never printed.
- Every phase, the countdown number, the speed ramp and the rollover are **derived from one timestamp**
  on a single 50 ms clock. Background tabs throttle timers, so a countdown decremented in a callback
  drifts; the real `GameTerminal` reads a target date for the same reason.
- The status copy is copied **verbatim** from `GameTerminal.gameStatusLabel`'s race branch, including
  `> REWARDS SENT. PULL LEVER FOR NEXT ROUND!` — practice's lever really does start the next round.
  The lottery branch is not copied.

### ⚠️ The winner is DRAWN, then revealed

- With a 26-character pool and a 6-character choice there are `26^6 ≈ 3.09e8` possible windows against
  ~59 000 characters per run. **A "generate characters until someone's word appears" loop never
  terminates.** This is the single most likely way to get this feature wrong.
- The winner is **drawn uniformly** from the placed choices when the run starts, then revealed at the
  end. That is what the real game does: `SpaceStrokerTyper.DecideFallbackWinner` draws from the placed
  choices whenever nothing lands inside the character cap.
- **A natural hit still beats the draw.** Both choices are matched against the stream, and a full match
  on either becomes the winner — the same `CheckForWinners` → `DecideFallbackWinner` order the backend
  uses. It is a ~0.02% event, but it means the near-miss row and the result can never contradict
  each other.
- `drawPracticeWinner(choices, random)` and `practicePhaseAt(elapsedMs, timings)` are **exported, pure
  and DOM-free** so a future test runner can pin them without a browser. The draw must never be biased
  toward the player.
- **The winning word is appended to the end of the stream and highlighted.** In a real race the game
  ends the instant the sliding window of the last N characters *is* a placed choice, so the stream's
  final characters are the winner; practice reproduces that rather than resolving off-screen.

### The character stream

- The backend ships the terminal in **batches**, not characters: `GameService` flushes at
  `GeneralSettings.Games.TerminalBatchLimit` (500). A real race is capped at `MaxTyperCharacters`
  (5 000 000) inside an 8–18 s budget, so the live screen is a wall of text far too fast to read — the
  near-miss row is what a player actually reads.
- Practice emits **120 / 220 / 400 characters per 50 ms tick** by display speed (~2.4k/4.4k/8k per
  second, ~59 000 per round), which reads like the real thing and makes the 20 000-character crop rule
  live rather than theoretical. `DisplaySpeed` ramps 1→2→3 at a third and two thirds of the run.
- The typer's refresh cadence (`{1: 50, 2: 35, 3: 25}` ms) and crop rule (20 000 → keep 2 000 every
  250 ms) are copied from `GameTyper.vue`.

### Near-miss rows

- Practice reuses the **pure `utils/nearMiss.ts`** (`buildFailureTable` / `createMatcher` / `advance`),
  so the behaviour is identical to the real screen by construction.
- ⚠️ **Never call `useNearMiss()`.** It is a module singleton — `attempts`, `entries`, `frozen` and its
  interval are module scope and shared with the live game screen; calling it from practice would put
  practice rows on a real round and vice versa. The composable's small display layer (display
  high-water, the 2000 ms stall reset, the 250 ms tick, the 80 ms win fill) is re-implemented in the
  practice store with those four constants unchanged.
- Only the **player's own** choice gets a visible row, exactly as the real screen only tracks
  `ownBetChoices`. The opponent's matcher exists but is never rendered.

### Money

- Ticket `1 USDC`, pot = `ticket × bets` (`1` after the player bets, `2` once the opponent has), both
  rendered through the real `usePlayerPreferences().formatGameAmount` from a synthetic `PRACTICE_GAME`
  config — no fork of the formatter.
- `PRACTICE_GAME` is deliberately **not typed as `ActiveGame`**. That would invite someone to hand it
  to a real-game code path, which is exactly the contamination this feature's isolation prevents.
- The round copy is the real copy, including `> PAID TO:`. The honesty lives in **one permanent line**
  pinned to the top of the terminal: `> PRACTICE ROUND. NOTHING IS CHARGED, NOTHING IS PAID.` It is
  sticky, so it stays visible while the stream auto-scrolls and is never off-screen at the same time
  as the pot.
- No balance is read and no payout is ever performed.

### The cabinet

- Practice-owned components under `src/components/practice/` reproduce the post-#134 cabinet. Desktop:
  `PracticeTerminal` + `PracticeMonkey`, no side panel, the console rendered **inside** the terminal.
  Mobile: the same three-band column as `features/mobile-game-design-update.md`, minus the tab strip
  and chat.
- **Distinct DOM ids** throughout — `practice-terminal-container`, `practice-terminal-form`,
  `practice-word-input`, `practice-output`. The real screen owns the unprefixed ones, and the desktop
  terminal width is computed from its own element's `clientHeight × 0.984`.
- ⚠️ **`.mobile-terminal` is not reusable.** It ships `border-top: 0` and `border-radius: 0 0 4px 4px`
  because `GameMobileTabs` supplies the top of that housing. **Practice has no tabs**, so its screen
  needs a **closed four-sided bezel** (`border: 7px solid #5c6a8c; border-radius: 4px`) on the same
  black glass.
- All keyframes are **re-declared locally** (`practice-rise-in`, `practice-fly-in`, `practice-wobble`,
  `practice-terminal-rise-in`). `GameMonkey`'s live in its scoped block and `GameTerminal`'s
  `.animate-rise-in` in an unscoped one — depending on either would be invisible coupling that breaks
  the moment someone touches the real game screen.
- The lever is **always pullable**: an invalid pull twitches and plays `error1.wav`; once the result is
  up it becomes "next round" (drop, 500 ms, up), exactly as `GameConsole.fetchNewGame` behaves.
- Edson runs the full `GameMonkey` mapping — idle, `typing`/`typing2`/`typing3` by display speed,
  glasses at 8800 ms of the countdown, `finale` → 3200 ms → `regen` → 1250 ms → idle, and the
  `monkeyAddBet` + `phone_beep.mp3` one-shot on the player's own bet. The `MAX_BET_ANIMATIONS` queue is
  dropped: practice has two bets and only one is the player's.

### Audio

- `PracticeAudio.vue` is a practice-owned copy of `GameAudio.vue` — the same three-track bed (idle loop
  → countdown jingle over silence → running loop), the same `visibilityState && hasFocus` gate and the
  same refocus resync. `GameAudio` cannot be mounted here: it reacts to `gameInstance.status` off the
  `useGame` singleton.
- The victory/lose one-shots are fired from the **store**, not the audio component — mirroring how
  `useGame` owns them on the real screen, so they survive teardown and play through.
- ⚠️ `PracticeAudio` is mounted **inside** each layout branch, not as a sibling root. The page's three
  branches are a `v-if`/`v-else-if`/`v-else` chain, which Vue treats as a **single root**, so it
  inherits `class="mx-auto h-5/6 block"` from `App.vue`'s `RouterView`. A fourth sibling makes the
  component a fragment, the class is dropped, and the desktop `h-full` resolves against nothing — a
  blank page.

### Fairness, self-exclusion, teardown

- **No fairness content of any kind** on the practice screen: no server seed, no commitment hash, no
  verification control, and nothing that simulates commit-reveal. The browser generates both sides
  here, so any such display would be fabricated evidence of fairness on the one product whose trust
  claim is that fairness is not client-controlled. The header's FAIR modal stays reachable.
- **Self-excluded wallets are blocked, fail-open**: only a preferences read that **succeeded** *and*
  reports `selfExcluded` blocks. A failed read, an absent wallet or a still-loading state never blocks.
  With no wallet connected the request is **skipped entirely**, so a first-time visitor makes literally
  zero network requests. Blocked renders the RG refusal (near-black `.arcade-frame`) and starts no
  round and no audio. This is a partial block by construction — exclusion is per-wallet, the same
  honest gap `/responsible-gaming` already states.
- `dispose()` on unmount clears every timer the round owns (the clock and the win fill); `PracticeTyper`
  and `PracticeMonkey` clear their own. Leaving mid-round leaves nothing running.
- **No new analytics events.** `home_link_click { link: 'practice' }` already fires at the feature card
  and the router `afterEach` gives `page_view`. (Note: the two `HomeHero` CTAs never fired that event —
  unchanged by this feature.)

## Deviations from the spec (the code is the newer decision)

Owner feedback during implementation superseded several spec decisions. The spec's §7.3, §7.4, §7.7,
§7.8, decision 9, decision 10 and AC #16 describe the **pre-feedback** build.

- **Timings are the real ones, not "compressed"** (spec decision 9). The countdown is 10 s and the
  post-round hold is `VUE_APP_NEW_GAME_DELAY` (30 s), not 3 s and ~6 s.
- **The rollover is not purely automatic** (spec decision 10). The lever doubles as "next round"
  during the hold, exactly as the real console does; the round still rolls over on its own if the
  lever is never pulled.
- **The stream is batched** (~59 000 characters/round), not the spec's ~700.
- **The winner is appended to the stream and highlighted**; the spec revealed it only in the near-miss
  row.
- **The result block uses the real payout copy** — `> PAID TO:`, `> REWARDS SENT. PULL LEVER FOR NEXT
  ROUND!`, `> ROUND OVER. PULL LEVER FOR NEXT RACE.` — rather than practice-specific wording. The
  single sticky banner carries the not-charged statement instead (spec §7.7 had reworded each line).
- **The full audio bed ships**, not just one-shots (spec §7.8 said one-shots only).
- **AC #16 is superseded.** The screen carries **no** "practice rounds are not verifiable" line at all;
  the owner removed it. The clauses that matter still hold: no seed, no hash, no verification control,
  no simulated commit-reveal.
- **A natural stream hit beats the draw** — the spec described the draw alone.

## Verification status

`monkify-front` has **no test runner**, so this shipped on `yarn lint` + `yarn build` + browser QA.

Exercised in a live browser: both win and lose outcomes, the drawn winner and its highlighted tail at
the end of the stream, the near-miss row filling to `0 AWAY`, the 10 s countdown ticking 10→1, the
30 s hold and auto-rollover (measured 53.5 s round total), the lever as next-round, the invalid-pull
twitch, **zero XHR/WebSocket requests with the API reachable**, all practice timers cleared on
navigate-away, no leakage into or out of `/game/play`, the embed redirect, `/soon → /error`, the
`/space-stroker/` shim still outranking the catch-all, desktop 1440 and mobile 390 with no page scroll,
the closed four-sided bezel, and 0 console errors.

**Not exercised:** a real self-excluded wallet (the gate was reasoned through, not driven), iOS Safari
input-zoom behaviour, and the 200-sample draw distribution against the live export — sampled at source
level instead (47.0 / 54.0 / 53.0 / 52.0 / 58.5 % over 5×200). The card's 21 acceptance criteria are
left unchecked rather than claimed.

## Key code touchpoints

- `monkify-front/src/pages/PracticePage.vue` — creates + `provide`s the store, the fail-open
  self-exclusion gate, both layouts, `dispose()` on unmount.
- `monkify-front/src/hooks/usePracticeGame.ts` — the factory store: the one round clock, the drawn
  winner (`drawPracticeWinner`), the pure phase map (`practicePhaseAt`), the stream, the near-miss
  display layer, the rollover, `nextRound()` and `dispose()`.
- `monkify-front/src/constants/practiceGame.ts` — every hard-coded value and the copied status labels.
- `monkify-front/src/constants/keys.ts` — `PRACTICE_GAME_KEY` (type-only import, no runtime coupling).
- `monkify-front/src/components/practice/PracticeTerminal.vue` — the CRT, the sticky banner, the closed
  mobile bezel, the near-miss dock, the result block.
- `monkify-front/src/components/practice/PracticeConsole.vue` — plate crop, visor, keyboard, lever.
- `monkify-front/src/components/practice/PracticeTyper.vue` — the stream renderer and the winner
  highlight.
- `monkify-front/src/components/practice/PracticeMonkey.vue` — Edson's status → GIF mapping.
- `monkify-front/src/components/practice/PracticeAudio.vue` — the three-track bed.
- `monkify-front/src/routes.ts` — the `Practice` route; the `ComingSoon` route removed.
- `monkify-front/src/main.ts` — `EMBED_REDIRECT_ROUTES`.
- `monkify-front/src/pages/HomePage.vue` — `startPractice`, the only call site.

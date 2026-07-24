# Feature — Characters Away (near-miss reveal)

> Status: **shipped.** Authoritative spec: `../specs/characters-away.md`
> (merged via `feature/characters-away` on 2026-07-07).
> Note: the spec body describes the original 3/2/1 pinned-milestone design; the shipped behaviour is
> the closest-approach loop below (see the spec's **Post-ship notes** for the reconciliation).

## What it does

While a round is running, the game terminal shows — for each word **the current player bet this
game** — how close the monkey has come to typing that word, as a live `> N AWAY!` row docked at the
bottom of the terminal, next to a `> POT:` line. Each row climbs as the monkey builds a longer correct
prefix, loops if it stalls, and on a win "types out" the full word. It is a player-facing, display-only
thrill layer — even a loss shows how close you got. It is on for every game type.

## Core rules

- **Scope = the player's own committed choices.** Only words the current client successfully bet this
  game are tracked (one row each). Other players' and un-bet preset choices are never shown.
- **Distance = tail-prefix progression (KMP), not window Hamming.** For a choice of length `L`,
  `away = L − k` where `k` is the length of the longest suffix of the character stream so far that is a
  **prefix** of the choice. A natural win is exactly `k == L`, so this mirrors
  `SpaceStrokerTyper.CheckForWinners`. Matched characters shown = `choice.slice(0, k)`; the remaining `away`
  slots render as neutral `_` placeholders.
- **Closest-approach, climbing, with a 2s loop.** Each row shows the best (largest) `k` reached in the
  current attempt. If a row does not get any closer for **2 seconds**, it resets to the start
  (`away = L`, blank) and climbs again — a continuous "the monkey keeps trying to type it" loop. The
  displayed number only ticks down within an attempt; it never regresses mid-attempt.
- **Win reveal.** When the round ends, the winning word — **if the player bet it** — is typed out the
  rest of the way to the full word (`away = 0`) at ~80 ms/char and then held. Non-winning rows freeze
  where they were. This sells the illusion the monkey finished typing the winner.
- **Always visible, bottom-docked.** The rows and the `> POT:` line live in a sticky, **opaque** dock
  at the bottom of the terminal so the auto-scrolling stream never hides them. The dock is filled
  **near-black** (`#050505` — matched to the desktop cabinet screen, sampled ~`#040404`; a scoped
  `.mobile-terminal .near-miss-dock` override keeps the mobile CRT tone `#0b0d18`) with a `box-shadow`
  that paints over the scroll container's `py-6` (24px) bottom padding and over-covers the left/right
  edges, so no stream characters bleed through behind, below, or beside the rows (a sticky element
  can't sit below its content box, and `box-shadow` doesn't add a scrollbar the way negative margins
  would). Present for every game type (races, presets, lottery) and all bet paths (crypto, regulated,
  free). *(Bug fix #90 — the dock was originally `background: transparent`, which let the stream show
  through; see `../specs/near-miss-dock-bg.md`.)*
- **In-game pot.** `> POT:` shows the pot **while the round runs** = Σ of the game's bet amounts
  (+ the lottery base pot for lottery), formatted with the existing currency logic (USDC for crypto,
  the player's display currency for regulated). Free bets (amount 0) contribute nothing. (This overlaps
  the separate "show pot value" story; both compute the pot client-side.)
- **Lifecycle.** Rows appear when the player bets **and are re-derived on every game load** — after a
  page refresh or rejoin, `updateNewGame` re-seeds the player's own choices from the loaded game's bets
  (matched by identity), so a returning bettor is **not** treated as a spectator (this is the fix in
  `../specs/restore-own-bets-on-refresh.md`). They are cleared on genuine game rollover
  (`updateNewGame`'s reset). On end they freeze and persist through the reward view.
- **Best-effort / display-only.** Detection runs entirely client-side from the existing
  `<gameId>/Terminal` stream — no new SignalR event, no backend call for the near-miss itself, and
  no change to outcomes, payouts, pacing, or the seed audit trail. There is **no** `<gameId>/Terminal`
  stream replay, so after a refresh/rejoin the **rows are restored** (re-seeded from the loaded bets)
  but each climbs fresh from reconnection onward (starting at `away = L`); the pre-refresh progression
  is not reconstructed.
- **Own-bet cues restored together.** Because the returning-bettor **victory/lose sound** and the
  embedded **`bet-settled`** signal also gate on `ownBetChoices`, the same load-time re-seed restores
  them: a bettor who refreshes mid-run and stays connected still gets the winning-word type-out + the
  victory/lose sound on the live `Ended`. Reloading into an **already-ended** reward view is out of
  scope (the active-game GET never returns an `Ended` game).

### Backend: fallback terminal reveal

- For **big search spaces** (e.g. 8-char alphanumeric games) the monkey statistically never approaches
  any word, and the round is decided by the **seed-based fallback winner** at the character cap — the
  winning sequence is never naturally typed into the stream.
- In that case `GameService.RunGame` now **emits the winning word (`SpaceStrokerTyper.FirstChoiceTyped`)
  as a final `<gameId>/Terminal` batch** when `WinnerDecidedByFallback` is true, so the real stream
  (for every client) ends with the monkey "typing" the winner — matching how a natural win already
  looks. This is purely visual: winner, seed, payouts, and the status flow are unchanged.

## Key code touchpoints

**Frontend (`monkify-front`):**
- `src/utils/nearMiss.ts` — pure KMP tail-prefix matcher (`buildFailureTable`, `createMatcher`,
  `advance` → live matched length).
- `src/hooks/useNearMiss.ts` — module-singleton: per-choice display high-water, the 2 s stall-reset
  loop, and the win type-out. Exposes `nearMissEntries`, `trackChoice`, `feed`, `finish`, `reset`.
- `src/hooks/useGame.ts` — durable `ownBetChoices` recorded after each successful bet (regulated /
  crypto / free) **and re-derived on load**: `reseedOwnBetsFromInstance()` (called at the end of
  `updateNewGame`, after the reset) matches the loaded `gameInstance.bets` against a shared reactive
  `currentPlayerId` (SolanaToken → connected wallet; else launch-token `externalPlayerId`/`sub`) and
  replays owned choices through the idempotent `recordOwnChoice`; a **per-consumer** `watch(currentPlayerId)`
  re-runs it when a wallet reconnects, and the live `{gameId}/Bets` handler also matches by identity.
  `nearMiss.feed(data)` in the `<gameId>/Terminal` handler; `nearMiss.finish(result.firstChoiceTyped)`
  on `Ended`; `nearMiss.reset()` in `updateNewGame`. `lastOwnBetChoice` (the one-shot marker consumed
  by `GameMonkey.vue`) is left untouched.
- `src/components/game/GameTerminal.vue` — the bottom dock: `> POT:` line (`displayPot`) + one
  near-miss row per `nearMissEntries` with an escalating colour ramp; `GameTyper.vue` renders just the
  live stream tail. `betPlayerLabel`/`myId` (the "YOU" label) key off the shared `currentPlayerId` from
  `useGame`, so the label and the near-miss re-seed mark the same bets.

**Backend (`monkify-backend`):**
- `SpaceStroker.Infrastructure/Services/Games/GameService.cs` → `RunGame` — appends
  `monkey.FirstChoiceTyped` as a final terminal batch when `monkey.WinnerDecidedByFallback`.
- Covered by `SpaceStroker.Tests/UnitTests/Services/GameServiceTests.cs` →
  `RunGame_FallbackDecidedWinner_EmitsWinningChoiceAsFinalTerminalBatch`.

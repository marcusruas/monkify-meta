# Feature — Off-page game presentation gate

> Status: **shipped.** Authoritative spec: `../specs/off-page-game-presentation.md`
> (merged via `feature/off-page-game-presentation` on 2026-08-10).
> **Frontend-only** — `monkify-front`. **Zero backend surface**: no entity, enum, handler, consumer,
> endpoint, header, EF mapping, migration or SignalR contract change, and no `monkify-backend` branch
> ever existed for it. Bug fix, so `constraints.md` §3's mandatory-integration-test rule did not
> apply.

## What it does

A round the player walks away from no longer drives the app. Leaving `/game/play` mid-round used to
leave that round's `DisplaySpeed` / `Bets` / `Status` events ramping the starfield to speed 2/3 on the
lobby, Orbit, how-to-play, history and stats, firing a torpedo per incoming bet, and dropping the
victory/lose sting on top of another page's music — or into a page with no music at all. Now the
global animation layer mirrors the live round **only on the game page**; everywhere else it renders
the calm default sky.

The cause was structural, not a leak: `GameBackground` (×2) and `TorpedoSpawner` are mounted in
`App.vue`, so they render on **every** page, and they read `useGame` — a module singleton whose
SignalR subscription is tab-scoped and **deliberately** outlives `GamePage`
(`unified-websocket-connection`). The in-game music **loops** already stopped correctly (`GameAudio`
unmounts and calls `stopLoop`); the starfield/torpedo layer was the only game visual that is global,
which is why it was the only thing that leaked.

## Core rules

### The gate

- **One flag, one writer, three readers.** `inGame` is a module-level `ref(false)` in `useGame.ts`;
  `App.vue` is its **only** writer, from a single `route.name === 'Game'` watcher. The readers are the
  two `GameBackground` speed props, the torpedo spawn watcher, and the end-of-round sting.
- **The gate is the PAGE, not the socket.** `useGame` keeps its subscription, its state and its group
  membership. ⚠️ **Never "fix" this by calling `detachFromGame()` on leaving the page** — the same
  `Status → Ended` also drives the balance re-read with win/refund attribution, the embedded
  `bet-settled` / `game-ended` signals and `useOrbitRedirect`, so detaching silently breaks settlement
  feedback for the common case: bet, wander to the lobby, round settles while away.
- ⚠️ **`typingSpeed` itself is NEVER mutated.** The typer (`GameTyper`) and Edson (`GameMonkey`) read
  the same ref, so zeroing it would leave a returning player looking at a stalled round until the next
  `DisplaySpeed` event. Gating happens at the **consumer**, which makes the return instant and free.
- **The flag lives in `useGame.ts` module scope, not in `App.vue`.** The audio consumer sits inside
  `onStatus`, a handler bound onto the tab's connection by `hub.attachToGame` whose closure outlives
  the component that bound it — a computed in `App.vue` is unreachable from there. This mirrors
  `useDisplaySettings.setCanonicalGraphics`, which `App.vue` writes the same way.
- **No synchronous pathname seed** (unlike `canonicalGraphics`, which needs one). An unresolved route
  at first paint yields `false` ⇒ the default speed — which is the correct value on a cold load
  anyway, since `typingSpeed` starts at 1 and no round has loaded.

### What each consumer does

- **Backdrop:** both `GameBackground` instances take `inGame ? typingSpeed : 1`. Off the game page the
  sky eases down to the default **once** and stays there for the rest of the round, however many
  `DisplaySpeed` events arrive.
- **Torpedoes:** the bet watcher returns early when `inGame` is false. ⚠️ **This is a guard inside
  `TorpedoSpawner`, NOT a `v-if` in `App.vue`** — `onUnmounted` removes every tracked node and
  `#torpedo-container` is the component's own template root, so unmounting would take every torpedo
  mid-blast straight out of the DOM. What is already flying must finish. The guard sits **after** the
  `ignoreInitialBet` one-shot: guarding first would leave that one-shot armed off-page and swallow the
  first torpedo after the player returns.
- **Victory / lose sting:** suppressed off-page and **consumed** — `endAudioFiredForGame` is stamped
  either way (it already ran one line earlier), so the sting can never fire late into an already-ended
  round the player walks back into. See [audio-overhaul](audio-overhaul.md).

### What deliberately still happens off-page

- The **Orbit first-win modal** (`useFirstWinUpsell().maybeOpen()`) still opens. It only fires on a
  **paid** win and is how a player who walked away learns about Orbit — the one off-page effect that
  is useful rather than decorative.
- The **balance re-read** with win/refund attribution, and the embedded `bet-settled` / `game-ended`
  `postToParent` signals. Nothing on a money or settlement path changed.
- `gradualSpeedReset()` / `evaluateSpeedReset()` and their module-scope `visibilitychange` listener
  still land `typingSpeed` at 1, whatever route the player is on.

### Nothing is purged

- Sprites already on screen keep drifting (at the default speed), and a torpedo mid-blast completes
  its animation and is removed normally by the existing `animationend` + `BLAST_MS` backstop. The only
  visible change at the moment of leaving is the speed easing back to 1.

### `/background`

- The capture route stops inheriting a live round's ramp **and** stops spawning torpedoes for it.
  Intended, and exactly what its `canonicalGraphics` flag already asks for: the marketing asset must
  not depend on whoever recorded it. Its route name is `Background`, so it gets the gate for free —
  this is **not** a per-route override (see [background-lifecycle-hardening](background-lifecycle-hardening.md)).

## Gotchas

- **`/game/practice` needs no gate and must not get one.** Practice never drove the global starfield —
  it does not touch `typingSpeed` (`PracticeMonkey.vue` says so in its own comment). It was already
  quiet.
- **Do not add a repair path for the return transition.** `GameBackground`'s existing
  `speedMultiplier` watcher runs a normal forward eased transition in both directions, and
  `handleVisibilityChange` re-derives from the prop — so a hidden/shown tab off-page cannot resurrect
  the ramp. Repairing a paused transition is precisely the ~27× speed error #149 fixed.
- **`GameBackground` is mounted only in `App.vue`.** If a third instance is ever added, it needs the
  same gated prop — the flag does not reach it by itself.
- **The reward view is still "in game."** `GamePage` stays mounted at `/game/play` through the reward
  screen, so `inGame` stays true there; a player who never left hears their sting as before.
- ⚠️ **Shipped with the browser QA pass waived by the owner.** The automated bar (`yarn lint` +
  `yarn build`) was green, but **acceptance criteria 1–10 were never driven against a running app** —
  including AC 7 (the balance re-read and the embedded `bet-settled` / `game-ended` signals), whose
  code this change does not touch but which nobody watched settle a round. Treat the off-page
  settlement path as **unverified in practice** until someone exercises it.

## Key code touchpoints

`monkify-front`:

- `src/hooks/useGame.ts` — the module-scope `inGame` ref (next to `hub` and the speed-reset deadline),
  `inGame` + `setInGame` on the composable's return, and the gated `playAudio` in the
  `GameStatus.Ended` branch. The `endAudioFiredForGame` stamp immediately above it is what makes the
  sting *consumed* rather than deferred — do not move it below the gate.
- `src/App.vue` — the `route.name === 'Game'` watcher (the single writer, beside the
  `canonicalGraphics` one) and the two `:speed-multiplier="inGame ? typingSpeed : 1"` bindings. The
  `<torpedo-spawner v-if="!lightMode && density > 0">` is deliberately **ungated** here.
- `src/components/background/TorpedoSpawner.vue` — `if (!inGame.value) return;` in the bets watcher,
  after the `ignoreInitialBet` block.
- `src/components/background/GameBackground.vue` — **unchanged**, but it is what makes the gate cheap:
  the `speedMultiplier` watcher eases both directions and `handleVisibilityChange` re-derives from the
  prop.

# Feature — Audio Overhaul with AI

> Status: **shipped.** Authoritative specs: `../specs/audio-overhaul.md` (manifest/§7 contract),
> `../specs/audio-overhaul-generation.md` (asset generation), `../specs/audio-overhaul-wiring.md`
> (wiring) — merged via `feature/audio-overhaul` on 2026-07-07 (`monkify-front` + `monkify-meta`;
> **no `monkify-backend` change**). Shipped as **three cards on one branch** (#17 manifest, #34
> generate, #35 wire) that shipped together.

## What it does

Gives SpaceStroker one cohesive, AI-generated (Suno) **warm arcade-chiptune** sound identity across the
game. Background music follows the game lifecycle, own-bet / button / error SFX and end-of-round
victory/lose cues give players real-time feedback, a menu track plays while browsing games, and a
header **mute + volume** control (persisted) governs everything. **Entirely frontend, display-only** —
audio never affects bets, settlement, winners, or game state.

## Core rules

- **Display-only.** Audio has zero effect on bets, payouts, winners, or game state.
- **Assets (9 generated mp3s** in `monkify-front/src/assets/audios/`, all warm 8/16-bit arcade
  chiptune, loudness-normalized): `game_waiting` (idle loop), `game_starting` (~11s one-shot),
  `game_running_1/2/3` (~15s loops), `phone_beep` (own-bet chirp), `victory`, `lose`, `button_press`,
  `menu_lobby` (~116s menu loop). **`error1.wav` is retained** (invalid-lever error, not regenerated);
  `success1.wav` and the four old `.wav` originals (`game_starting/game_running/game_waiting/phone_beep`)
  were **deleted**. (mp3 imports need a `*.mp3` module declaration in `shims-vue.d.ts`.)
- **Status → background track (exactly one loop at a time):** `WaitingBets` → `game_waiting` idle
  loop; `GameStarting` → `game_starting` one-shot **once per game** (and the game's running
  variant is chosen + preloaded here so it starts instantly); `InProgress` → a **random** one of the
  three `game_running` loops; `Ended`/reward → loops stop.
- **Victory / lose — bettors only, once per game.** On `Status → Ended`: the local player hears
  `victory` iff they **bet and won**, `lose` iff they **bet and lost**; spectators (no own bet) hear
  neither. Selected from `useGame` `ownBetChoices` vs `winnerChoice`.
  ⚠️ **Since #162 the sting also requires the player to be ON `/game/play`**
  ([off-page-game-presentation](off-page-game-presentation.md)). `useGame` is a module singleton whose
  socket outlives `GamePage`, so this one-shot used to fire on whatever page the player had wandered
  to — over the lobby's `menu_lobby` track, or into a page with no music at all. It is now
  **suppressed off-page and CONSUMED**: `endAudioFiredForGame` is stamped either way, so it can never
  play late when the player walks back into an already-ended round. The bettors-only / once-per-game /
  tab-active gates are unchanged and compose with it.
- **SFX:** own bet arriving → `phone_beep` (in sync with the call animation, capped at 2); preset
  choice button → `button_press`; invalid lever → the retained `error1.wav`.
- **Menu music:** the **games page** (`GameSelectPage`) loops `menu_lobby`. The **admin page is
  silent** (no lobby music). The reusable `LobbyAudio.vue` component stops the loop on navigation, so
  it never overlaps the in-game audio.
- **Master mute + volume.** A speaker icon in the game header toggles mute on click and reveals a
  volume slider on hover; the setting persists in `localStorage` under `space-stroker.audio` (default
  **unmuted, full volume**) and applies **live** to a currently-playing loop. Backed by a master
  gain/mute multiplier in `helpers.ts` (`effective = base × (muted ? 0 : master)`); music loops play
  at base `0.1` to match the one-shot songs.
- **Tab-visibility gating preserved.** Nothing starts while the tab is inactive; on refocus the
  background loop re-syncs to the current status — but the time-aligned `game_starting` one-shot is
  **never replayed on refocus**.
- **`/` now redirects to `/space-stroker/game`** (was `/error`). ⚠️ **Superseded by `home-dashboard`:**
  `/` is a real page now (the starting page), not a redirect, and the prefix is gone.

## Notable fixes shipped alongside

- **Game-connection teardown (pre-existing bug).** Switching games or rolling over now calls
  `disconnectAllPreviousConnections()` in `useGame.updateNewGame` (it was commented out). Before
  this, a previous game's SignalR connection stayed alive and its `Status`/`Terminal`/`Bets` events
  kept mutating the **shared** `gameInstance`, corrupting audio, pot, bets, and terminal on
  game switch. The teardown is hardened with `try/catch` so a down connection can't break loading
  the next game.
- **Loop robustness.** `playLoopingAudio` returns a stop that **cancels** a not-yet-started deferred
  play, and `stopAudioSrc(src)` stops a single track without touching other one-shots — so rapid
  status changes never leave two tracks overlapping, and victory/lose one-shots are never cut off.

## Not implemented (deliberately)

- **3/2/1 proximity "coin" bleeps** (spec §6.5/§7.5). The three coin-bleep assets were **dropped by
  the owner** during generation (Suno couldn't reliably produce them), so the proximity audio was not
  wired. The **near-miss visual** (`characters-away`, `useNearMiss`) is unchanged and still works — it
  just has no sound. The manifest/generation/wiring specs still describe the bleeps + "12 files"; the
  shipped reality is **9 assets** and no proximity audio.

## Key code touchpoints (`monkify-front`, on `main`)

- `src/constants/helpers.ts` — master gain/mute (`setMasterVolume`/`setMuted`/`getMuted`/
  `getMasterVolume`/`applyVolumeToActive`, per-element base `WeakMap`), cancellable `playLoopingAudio`,
  `stopAudioSrc`.
- `src/components/game/GameAudio.vue` — status→track (idle / one-shot starting / random running),
  single-track handoff, `gameId`-aware reset, tab gating.
- `src/components/LobbyAudio.vue` — menu loop for the games page.
- `src/components/GameHeader.vue` — speaker mute + hover volume slider, `localStorage` persistence.
- `src/components/game/GameMonkey.vue` (`phone_beep`), `src/components/game/GameConsole.vue`
  (`button_press`, keep `error1.wav`).
- `src/hooks/useGame.ts` — victory/lose on `Ended` (bettors only, once, **and only on `/game/play`**
  since #162 — the `inGame` module flag) **and** `disconnectAllPreviousConnections()` on game
  switch/rollover.
- `src/pages/GameSelectPage.vue` (mounts `LobbyAudio`), `src/pages/AdminPage.vue` (no lobby).
- `src/routes.ts` — `/` → `/space-stroker/game` (**since superseded**: `/` is the starting page and the
  prefix is dropped — see `features/home-dashboard.md`). `src/App.vue` — preloads the new tracks.
- `src/assets/audios/` — the 9 mp3s + retained `error1.wav`. `src/shims-vue.d.ts` — `*.mp3` shim.

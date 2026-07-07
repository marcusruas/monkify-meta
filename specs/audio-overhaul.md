# Monkify — "Audio Overhaul with AI" — Audio Manifest & Suno Generation Spec

> **Status: Ready for implementation** — refined via Trello card
> [Overhaul audio/songs with AI — (1/3) Audio manifest & Suno generation spec](https://trello.com/c/9qiDtDAe/17-overhaul-audio-songs-with-ai-1-3-audio-manifest-suno-generation-spec)
> (2026-07-07).
> Repos affected: **`monkify-front`** (assets & wiring — cards 2 & 3) + this doc in **`monkify-meta`**.
> **No `monkify-backend` change** — frontend-only overhaul.
>
> **Purpose:** Give Monkify a single, cohesive AI-generated sound identity. Recreate every existing
> game sound and add the missing feedback (win/lose, proximity bleeps, session-start sting, button
> click, idle loop) so a round *feels* alive and every player gets real-time audio feedback on their
> own bets.

> ### ℹ️ This is the design/manifest card (1 of 3) — it produces THIS spec only
> This card writes the authoritative manifest below. It generates **no audio** and touches **no
> application code**. The other two cards implement it, **all three on the same shared branch**
> `feature/audio-overhaul`:
>
> | Card | Trello | Deliverable |
> | --- | --- | --- |
> | **(1/3) — this card** | [#17 `9qiDtDAe`](https://trello.com/c/9qiDtDAe) | `specs/audio-overhaul.md` (this file): audit + 12-sound manifest + Suno prompts + the FE integration contract cards 2/3 consume |
> | **(2/3) — Generate assets** | [#34 `OuXhyqyU`](https://trello.com/c/OuXhyqyU) | Run every §5 item through Suno, trim/normalize, drop the 12 files into `src/assets/audios/` |
> | **(3/3) — Wire into the game** | [#35 `RvVdpO25`](https://trello.com/c/RvVdpO25) | Engine gain/mute + all triggers (§7): idle loop, sting, own-bet, error, victory/lose, proximity bleeps, button press, mute+volume control |
>
> **Hard rule — shared branch/worktree.** All three cards use **one** branch and **one** worktree per
> repo: `feature/audio-overhaul`. They are refined and spec'd separately (own specs) but implemented on
> the **same branch** so they cross-reference and **ship together**. `create-spec`/`implement` for
> cards 2 & 3 must **reuse** `feature/audio-overhaul` — never fork a new slug.

---

## 1. Summary

Monkify's audio today is six ad-hoc `.wav` files (~5 MB each for the loops) with no shared style, one
dead file, no win/lose feedback, no proximity cues, and no volume control. This overhaul:

- **Recreates** all four live sounds (`game_starting`, `game_running`, `phone_beep`, `error1`) in one
  cohesive AI-generated style.
- **Repurposes** the currently-unused `game_waiting` slot as a new **idle loop** on `WaitingBets`.
- **Drops** the never-referenced `success1.wav`.
- **Adds** seven new cues: a **session-started sting**, **victory**, **lose**, **3/2/1-away proximity
  bleeps**, and a **button-press** click.
- **Adds** (card 3) a game-header **mute + volume control** persisted in `localStorage`.

Everything is generated through **Suno** (music *and* SFX; short SFX may need manual trimming). Loops
move to compressed **mp3**; SFX stay small mp3. **No backend, DB, migration, endpoint, or SignalR
change** — proximity is computed client-side from the already-streamed terminal text.

**Before → after (per audio slot):**

| Slot | Today (`src/assets/audios`) | After |
| --- | --- | --- |
| Idle (`WaitingBets`) | *none* (`game_waiting.wav` imported-out) | **NEW idle loop** in the `game_waiting` slot |
| Starting loop | `game_starting.wav` | recreated, mp3 |
| Running loop | `game_running.wav` | recreated, mp3 |
| Session-started sting | *none* | **NEW** one-shot |
| Own-bet "call" | `phone_beep.wav` | recreated, mp3 |
| Invalid-lever error | `error1.wav` | recreated, mp3 |
| Victory | *none* | **NEW** one-shot ≤2 s |
| Lose | *none* | **NEW** one-shot ≤2 s |
| 3 / 2 / 1-away bleeps | *none* | **NEW** three pitched one-shots |
| Button pressed | *none* | **NEW** one-shot |
| `success1.wav` | dead (never referenced) | **dropped** |

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Shared branch/worktree:** `feature/audio-overhaul` for **all three** cards — one worktree per
  repo (`monkify-meta`, `monkify-front`); the shared checkout stays on `main`. Cards 2 & 3 reuse this
  branch; never fork a new slug.
- **Frontend-only → the mandatory backend happy-path integration test does NOT apply** (no backend
  behavior changes). Unit-test discipline still applies to the FE logic added in card 3 (proximity
  detection, win/lose selection, gain/mute) — see §11.
- **Secrets/config:** any Suno API key/config stays a **mock/placeholder** in committed files — never
  a real secret (card 2's concern; called out here so it isn't missed).
- **Frontend env vars use `VUE_APP_*`**, never `VITE_*`.
- **EF / migrations / operator-messaging** constraints are **N/A** (no backend surface touched).
- **Format is a reversible convention:** prefer compressed **mp3** for music/loops, small files for
  SFX; if a slot needs `.wav` it may keep it — the wiring (card 3) imports whatever files land.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **Recreate everything via AI (Suno).** All existing sounds are regenerated for one cohesive style. Music *and* SFX go through Suno; SFX may be manually trimmed/normalized after generation. |
| 2 | **`success1.wav` is dropped** (never referenced in code); **`game_waiting`** becomes the new `WaitingBets` idle loop. |
| 3 | **Creative briefs + Suno prompts live in this spec** (§5). Refinement locked scope, the 12-sound list, and the triggers; per-sound duration/mood/instrumentation/prompt are authored here. |
| 4 | **Proximity bleeps are frontend-only, local player's own bets only.** Computed client-side from the streamed terminal text. No backend change, no new SignalR event. 3/2/1 escalating pitch; re-arm on match break; closest own bet drives the tier. |
| 5 | **Victory / Lose audience = only players who bet.** Victory if the local player placed a **winning** bet; Lose if the local player bet but **did not win**; spectators (no bet) hear **neither**. Each fires once at session end. |
| 6 | **Mute + volume control** in the game header, persisted in `localStorage`, **default unmuted / full volume**, applied via a global gain/mute multiplier in `helpers.ts` (client display-only). Implemented in card 3. |
| 7 | **Format:** prefer compressed **mp3** for music/loops (current `.wav` loops are ~5 MB); keep SFX small. Reversible convention. |
| 8 | **Existing tab-visibility gating is preserved** — audio does not start while the tab is inactive (`GameAudio.vue` `isActiveTab()`); the new idle loop obeys the same gate. |

---

## 4. Current audio inventory (audit)

The current engine is plain `HTMLAudioElement` — **no Web Audio graph, no gain node.** Verified files
and call sites:

**Files — `monkify-front/src/assets/audios/`:**

| File | Referenced by | Fate |
| --- | --- | --- |
| `game_starting.wav` | `GameAudio.vue` (loop on `SessionStarting`) | **recreate** → `game_starting.mp3` |
| `game_running.wav` | `GameAudio.vue` (loop on `InProgress`) | **recreate** → `game_running.mp3` |
| `game_waiting.wav` | `GameAudio.vue` — **import commented out**, currently unused | **repurpose** as the idle loop → `game_waiting.mp3` |
| `phone_beep.wav` | `GameMonkey.vue` (`playAudio(beep)` on own-bet call anim) | **recreate** → `phone_beep.mp3` |
| `error1.wav` | `GameConsole.vue` (`leverError()` on invalid lever) | **recreate** → `error1.mp3` |
| `success1.wav` | *nothing* (grep-clean) | **drop / delete** |

**Engine — `src/constants/helpers.ts`:** `HTMLAudioElement` per src cached in `audioCache`.
- `playAudio(src, volume = 0.1)` — one-shot; resets `currentTime`, plays once.
- `playLoopingAudio(src, volume = 0.02)` — returns a `stop()` fn; loops.
- `stopAllAudio()` / `preloadAudios(srcs)` — bulk stop / preload.
- **`volume` is a raw per-call number** (loops default `0.02`, SFX `0.1`). There is **no master
  gain/mute** — that is the gap card 3 fills (§7.1).

**Status → track mapping — `src/components/game/GameAudio.vue`:**
`SessionStarting → starting loop`, `InProgress → running loop`, `WaitingBets / Reward / Ended →
stopAudio()` (i.e. **silence on `WaitingBets` today** — the idle loop is new). Tab-visibility gating
via `isActiveTab()` (visible **and** focused).

**One-shot SFX call sites:** `GameMonkey.vue` `tryPlayNextBetAnimation()` → `playAudio(beep)` (own-bet
call, capped at `MAX_BET_ANIMATIONS = 2`); `GameConsole.vue` `leverError()` → `playAudio(errorAudio)`.
`GameConsole.vue` `selectPresetButton()` currently plays **nothing** (target for the new button click).

**Proximity infrastructure already exists** (shipped "Characters Away"): `src/utils/nearMiss.ts`
(pure KMP: `advance(matcher, ch)` returns the live matched-prefix length `k`) + `src/hooks/useNearMiss.ts`
(module-singleton fed raw `Terminal` batches from `useSession.ts`). The proximity bleeps (§7.5) **reuse
this**, not a second matcher.

---

## 5. The sound manifest — core deliverable

### 5.0 Style bible (apply to every Suno prompt for cohesion)

> **Monkify house style:** playful **retro-arcade casino** with a light jungle/monkey wink. Warm
> chiptune + analog synth bleeps, tape-y lo-fi drums, a touch of slot-machine sparkle. Clean, punchy,
> mono-friendly, never harsh. Family-friendly, upbeat, a little cheeky. **Instrumental, no vocals.**

Each item below is generation-ready: **trigger · type · target duration · mood · instrumentation ·
Suno prompt · post-processing.** Loops must be **seamless** (no audible seam at wrap); SFX must be
**dry and short** with minimal tail. All prompts are **instrumental**.

---

#### 1. Idle loop — repurposes the `game_waiting` slot

- **Trigger:** session status `WaitingBets` (players are joining / placing bets). Loops via
  `playLoopingAudio`. **New behavior** — today `WaitingBets` is silent.
- **Type:** seamless loop · **target ~60 s** · target file `game_waiting.mp3`
- **Mood:** calm, inviting, low-stakes anticipation — "the lobby before the round."
- **Instrumentation:** soft chiptune arpeggio, mellow bass pulse, gentle shaker, occasional slot
  sparkle; low energy, leaves headroom.
- **Suno prompt:** *"[Monkify house style — retro-arcade casino, light jungle wink] Calm, inviting
  lobby loop, ~60s, seamless, low energy. Soft 8-bit arpeggio over a warm analog bass pulse, mellow
  lo-fi shaker groove, occasional gentle slot-machine sparkle. Relaxed anticipation, not busy.
  Instrumental, no vocals, loopable with no seam."*
- **Post-processing:** trim to a clean bar-aligned loop point; normalize; export mp3.

#### 2. Starting loop — recreate `game_starting`

- **Trigger:** session status `SessionStarting` (countdown to the run). Loops.
- **Type:** seamless loop · **target ~10–20 s** · target file `game_starting.mp3`
- **Mood:** rising tension, "get ready," countdown energy — a notch above idle.
- **Instrumentation:** ticking/countdown percussion, ascending synth motif, tightening hats, subtle
  riser feel that still loops cleanly.
- **Suno prompt:** *"[Monkify house style] Tense pre-round countdown loop, ~15s, seamless. Ticking
  arcade percussion, ascending chiptune motif, tightening hi-hats, building anticipation like a slot
  machine about to spin. Energetic but loopable, instrumental, no vocals, no hard ending."*
- **Post-processing:** loop-align; match loudness to the idle loop; mp3.

#### 3. Running loop — recreate `game_running`

- **Trigger:** session status `InProgress` (the monkey is typing / the race is on). Loops.
- **Type:** seamless loop · **target ~20–40 s** · target file `game_running.mp3`
- **Mood:** driving, exciting, frantic-fun — the peak-energy bed of the round.
- **Instrumentation:** fast chiptune sequence, punchy lo-fi drums, bouncy bassline, playful typewriter
  clatter texture; the busiest of the three loops.
- **Suno prompt:** *"[Monkify house style] High-energy race loop, ~30s, seamless. Fast bouncy
  chiptune sequence, punchy lo-fi drums, driving synth bass, a playful typewriter-clatter percussion
  texture. Fun and frantic like a monkey typing at top speed. Instrumental, no vocals, loopable."*
- **Post-processing:** loop-align; sits **above** starting-loop energy; mp3.

#### 4. Session-started sting — NEW

- **Trigger:** the `SessionStarting → InProgress` transition (the moment the monkey "puts its glasses
  on" and the race begins). One-shot that **layers over** the running-loop start.
- **Type:** one-shot · **target ~1–1.5 s** · target file `session_start_sting.mp3`
- **Mood:** triumphant "here we go!" hit — confident, arcade-y.
- **Instrumentation:** short brassy/synth fanfare stab + rising sparkle, quick and bright.
- **Suno prompt:** *"[Monkify house style] Short 1.5s triumphant 'here we go' sting. Bright synth
  fanfare stab with a quick rising sparkle, arcade game-start energy, confident and punchy. One-shot,
  dry, minimal tail, instrumental, no vocals."*
- **Post-processing:** trim to ≤1.5 s; mixes on top of loop #3, so keep it bright/cutting but not
  overpowering.

#### 5. Own-bet "call" — recreate `phone_beep`

- **Trigger:** the local player's **own** bet arrives (drives the monkey "call" animation in
  `GameMonkey.vue`). One-shot; can fire up to `MAX_BET_ANIMATIONS = 2` times.
- **Type:** one-shot · **target ~0.6–1 s** · target file `phone_beep.mp3`
- **Mood:** friendly "your bet is in!" chirp — positive confirmation, phone/notification flavored.
- **Instrumentation:** two-note ascending chiptune chirp, clean and cute.
- **Suno prompt:** *"[Monkify house style] Cute 0.8s confirmation chirp, like a friendly retro phone
  notification. Two-note ascending 8-bit blip, clean and positive, says 'your bet is in'. One-shot,
  dry, instrumental, no vocals."*
- **Post-processing:** trim to ≤1 s; **SFX may need manual trimming** if Suno pads silence.

#### 6. Invalid-lever error — recreate `error1`

- **Trigger:** invalid lever pull (`GameConsole.vue` `leverError()` — wallet not connected, word too
  short, duplicate chars when disallowed, bets closed). One-shot.
- **Type:** one-shot · **target ~0.3–0.5 s** · target file `error1.mp3`
- **Mood:** clear negative "nope" — arcade error buzzer, **not** harsh or alarming.
- **Instrumentation:** short descending two-tone buzz / retro "wrong" blip.
- **Suno prompt:** *"[Monkify house style] Short 0.4s arcade error buzzer. Descending two-tone
  'wrong / try again' blip, retro game feel, clearly negative but soft and friendly, not harsh. One-shot,
  dry, instrumental, no vocals."*
- **Post-processing:** trim tight; keep quiet-friendly; **manual trim likely.**

#### 7. Victory — NEW

- **Trigger:** session `Ended` **and** the local player **bet and won** (§7.4 audience rule). One-shot,
  fires once.
- **Type:** one-shot · **target ≤2 s** (hard cap) · target file `victory.mp3`
- **Mood:** joyful win celebration — coins, fanfare, dopamine.
- **Instrumentation:** bright ascending fanfare + slot-machine coin sparkle cascade, resolved happy
  chord.
- **Suno prompt:** *"[Monkify house style] Joyful 2s win fanfare. Bright ascending chiptune melody
  resolving to a happy major chord, slot-machine coin-cascade sparkle, celebratory arcade jackpot
  feel. One-shot, ≤2 seconds, dry, instrumental, no vocals."*
- **Post-processing:** trim to **≤2 s**; Suno may overshoot — **manual trim to the cap.**

#### 8. Lose — NEW

- **Trigger:** session `Ended` **and** the local player **bet but did not win** (§7.4). Spectators
  (no bet) hear **neither** #7 nor #8. One-shot, fires once.
- **Type:** one-shot · **target ≤2 s** (hard cap) · target file `lose.mp3`
- **Mood:** gentle, good-humored "aww, so close" — sympathetic, **not** punishing or sad-core.
- **Instrumentation:** short descending "wah-wah" motif with a soft comedic bounce; light, encourages
  a retry.
- **Suno prompt:** *"[Monkify house style] Gentle 2s 'aww, better luck next time' cue. Short descending
  chiptune wah-wah motif with a soft comedic bounce, good-humored and light, never harsh or depressing.
  One-shot, ≤2 seconds, dry, instrumental, no vocals."*
- **Post-processing:** trim to **≤2 s**; keep it clearly softer than victory.

#### 9–11. Proximity bleeps (3-away / 2-away / 1-away) — NEW, a pitched set

Generate as **one coherent, escalating three-note set** (same timbre, rising pitch and tension) so the
climb reads as one instrument. Fire semantics in §7.5.

- **Trigger:** the local player's own bet is **N characters away** from completing (§7.5). `away = 3`
  → #9, `2` → #10, `1` → #11.
- **Type:** one-shot, **very quick** (~0.1–0.2 s each) · target files `bleep_3away.mp3`,
  `bleep_2away.mp3`, `bleep_1away.mp3`
- **Mood:** escalating tension — like sonar/heartbeat closing in. **3-away = lowest pitch, calmest;
  1-away = highest pitch, most tense.**
- **Instrumentation:** single clean synth blip per tier; identical timbre, pitch rising 3→2→1.
- **Suno prompt (generate together, then slice):** *"[Monkify house style] Three very short escalating
  sonar-style blips, same clean synth timbre, rising pitch and tension: first low and calm, second mid,
  third high and urgent — like something closing in. Each ~0.15s, dry, staccato, spaced by a brief gap
  so they can be sliced into three separate one-shots. Instrumental, no vocals."*
- **Post-processing:** **slice into three files** at the gaps; trim each to ~0.15 s; normalize so the
  perceived loudness rises slightly 3→1. **Manual slicing/trimming expected.**

#### 12. Button pressed — NEW

- **Trigger:** a preset-choice ("horse race") button click — `GameConsole.vue` `selectPresetButton()`.
  One-shot, very quick.
- **Type:** one-shot, **very quick** (~0.08–0.15 s) · target file `button_press.mp3`
- **Mood:** satisfying tactile arcade button — crisp, neutral-positive.
- **Instrumentation:** single short "clicky" synth blip / arcade key press.
- **Suno prompt:** *"[Monkify house style] Ultra-short 0.1s tactile arcade button click. Single crisp
  clicky synth blip, satisfying and neutral-positive, like pressing a physical casino button. One-shot,
  dry, no tail, instrumental, no vocals."*
- **Post-processing:** trim to the transient only; **manual trim expected.**

---

## 6. Suno generation workflow (for card 2)

1. **Verify the Suno MCP first.** Card 2 requires the **Suno MCP** (AceDataCloud / SunoMCP), which is
   **not yet confirmed installed**. Card 2 verifies it before generating; if missing, it stops with
   install instructions. Any Suno API key stays a **mock/placeholder** in config (never a real secret).
2. **Generate** each §5 item from its prompt. Prefer **instrumental** mode. For the loops, request the
   longest clean take and pick a seamless section; for the 3 bleeps, generate the single §9–11 take and
   **slice**.
3. **Trim / normalize** (Suno rarely emits ≤2 s SFX directly — manual trimming is expected and
   allowed): enforce the per-item duration caps (victory/lose **≤2 s**, bleeps ~0.15 s), loop-align the
   three loops, and loudness-match so loops sit under SFX and the bleep set rises 3→1.
4. **Export** to the target filenames (§5 / §8) as **mp3**; drop into `monkify-front/src/assets/audios/`
   on `feature/audio-overhaul`.
5. Card 2 produces **assets only** — no wiring. Card 3 imports them.

---

## 7. Frontend integration contract (for card 3)

This section is the **contract** card 3 implements. It names exact files and the existing state it
reuses. No backend/SignalR change anywhere.

### 7.1 Engine: global gain + mute — `src/constants/helpers.ts`

Add a module-level master gain/mute and fold it into every play call:

- Module state: `masterVolume` (0..1, default `1`) and `muted` (default `false`).
- Effective volume for any play = `clampVolume(baseVolume * (muted ? 0 : masterVolume))`. Keep the
  existing per-call bases (loops `0.02`, SFX `0.1`) as the **relative mix**; the master scales all of
  them together.
- Exports: `setMasterVolume(v)`, `setMuted(b)`, getters, and a way to **re-apply to currently-playing
  loops** (update the live `HTMLAudioElement.volume` of tracked `activeAudios`, so dragging the slider
  changes the running loop immediately — not just the next sound).
- Still plain `HTMLAudioElement` (no Web Audio refactor required).

### 7.2 Idle loop + session-start sting — `src/components/game/GameAudio.vue`

- **Idle loop (#1):** on `WaitingBets`, start the `game_waiting.mp3` loop (uncomment / add the import;
  add a `'waiting'` track to `currentTrack`). Obey the existing `isActiveTab()` gate and stop it on any
  other status, same as the other loops.
- **Session-start sting (#4):** on the `SessionStarting → InProgress` transition, `playAudio` the sting
  **once** so it layers over the running loop start. Detect the transition in the status watcher (fire
  only when the previous status was `SessionStarting`), respecting `isActiveTab()`.

### 7.3 Own-bet call + error + button press

- **Own-bet (#5):** `GameMonkey.vue` already calls `playAudio(beep)` in `tryPlayNextBetAnimation()` —
  just swap the import to `phone_beep.mp3`.
- **Invalid-lever (#6):** `GameConsole.vue` `leverError()` already plays it — swap import to
  `error1.mp3`.
- **Button press (#12):** `GameConsole.vue` `selectPresetButton()` currently plays nothing — add
  `playAudio(buttonPress)` at the top of it.

### 7.4 Victory / Lose — end-of-session, bettors only

Driven off the existing `useSession` state on `Status → Ended`:

- **Did the local player bet?** True iff `ownBetChoices.value.length > 0` (this list is the durable
  record of the player's own committed choices; it covers crypto, regulated, and free-bet paths). If
  **false → play neither** (spectators are silent). *(Belt-and-suspenders: for crypto, an own bet is
  also `bets.some(b => b.externalPlayerId === wallet)`; `ownBetChoices` already captures it via
  `recordOwnChoice`.)*
- **Did they win?** `useSession` already exposes `winnerChoice` (= `result.firstChoiceTyped`) and
  `winners` (= `result.winners`). The player **won** iff `ownBetChoices` includes `winnerChoice`
  (their own choice was the one typed). *(Equivalently, own wallet ∈ `winners` for crypto; use the
  choice test as the primary signal since it works across all payment models.)*
- **Selection:** bet & won → **victory (#7)**; bet & not won → **lose (#8)**; no bet → **neither**.
  Fire **once** per session end (guard against the `Status` handler running twice), respect the tab
  gate, and reset the fired-flag on rollover (`updateNewSession`).
- **Where:** a small watcher on `sessionInstance.status === Ended` (in `useSession` or a dedicated
  end-of-session audio composable) — no new event, no backend.

### 7.5 Proximity bleeps (#9/#10/#11) — frontend-only, reuse the KMP matcher

**Reuse the existing near-miss pipeline** (`src/utils/nearMiss.ts` + `src/hooks/useNearMiss.ts`),
which already runs every streamed `Terminal` character through a per-own-choice KMP matcher.

- **Signal:** for each own choice, `advance(matcher, ch)` returns the **raw** matched-prefix length
  `k` per character. `away = choiceLength − k` = "N away". *(Use this raw `k`, **not** the composable's
  `displayK`: `displayK` is a display high-water that only drops on a 2 s stall, whereas bleeps need
  the true match state, which drops immediately when the run breaks.)*
- **Fire once, escalating:** when `away` crosses **downward** to `3`, `2`, or `1`, play the matching
  bleep once (#9/#10/#11). Escalating pitch is baked into the assets (3 = lowest … 1 = highest).
- **Re-arm on break:** track a per-choice armed/fired state; when the match breaks and `k` drops (so
  `away` grows past a tier), **re-arm** that tier so it can fire again on the next approach. Never spam
  the same tier within one continuous match run.
- **Closest bet drives the tier:** with multiple own choices, evaluate each per character; the
  **smallest `away`** that char produces drives which single bleep fires (do not stack multiple bleeps
  on one character).
- **Short choices:** `L = choiceRequiredLength` is uniform (normally ≥4), but handle generically —
  a choice of length `L` fires only the tiers `≤ L` (e.g. `L = 2` → only 2-away & 1-away; `L = 1` →
  only 1-away).
- **Implementation shape:** extend `useNearMiss` to emit a proximity event/callback from inside its
  existing `feed()` loop (it already advances every matcher per char) — e.g. an `onProximity(tier)`
  hook or a reactive `proximityTier` pulse — and have the audio layer play the bleep. This keeps a
  single matcher pass and avoids a second KMP walk. **No backend/SignalR change.**
- **Respect** the tab-visibility gate and the master mute/volume.

### 7.6 Mute + volume control — game header

- A **mute toggle + volume slider** in the game header (e.g. `GameHeader.vue`), bound to §7.1's
  `setMuted` / `setMasterVolume`.
- **Persist** to `localStorage` (suggested key `monkify.audio` → `{ muted, volume }`); **default
  unmuted, full volume**; hydrate on mount. **Client display-only** — no server, no bet/settlement
  effect.
- Applies globally and live to any currently-playing loop (via §7.1's re-apply).

### 7.7 Preserved behavior

- **Tab-visibility gating** (`isActiveTab()` in `GameAudio.vue`) is preserved for the idle loop and all
  new cues — nothing starts while the tab is inactive.
- Loops still fully stop on `Ended` / rollover (`stopAllAudio` on session change); the near-miss reset
  on `updateNewSession` already clears proximity state.

---

## 8. Asset file naming (target)

All mp3, under `monkify-front/src/assets/audios/` on `feature/audio-overhaul`:

| # | File | Recreate / New / Repurpose |
| --- | --- | --- |
| 1 | `game_waiting.mp3` | repurpose (idle loop) |
| 2 | `game_starting.mp3` | recreate |
| 3 | `game_running.mp3` | recreate |
| 4 | `session_start_sting.mp3` | new |
| 5 | `phone_beep.mp3` | recreate |
| 6 | `error1.mp3` | recreate |
| 7 | `victory.mp3` | new |
| 8 | `lose.mp3` | new |
| 9 | `bleep_3away.mp3` | new |
| 10 | `bleep_2away.mp3` | new |
| 11 | `bleep_1away.mp3` | new |
| 12 | `button_press.mp3` | new |
| — | `success1.wav` | **delete** |

> The old `.wav` files are replaced; because the extension changes (`.wav → .mp3`), card 3 updates the
> import statements in `GameAudio.vue`, `GameMonkey.vue`, `GameConsole.vue`. Filenames may keep `.wav`
> if a slot's mp3 proves lossy for a tiny SFX — reversible convention (§2), but mp3 is the default.

---

## 9. Out of scope (this card)

- **Actual asset generation** (Suno runs, trimming, files) — **card 2 (#34)**.
- **All wiring / implementation code**, including the engine gain/mute, the idle loop, the sting, the
  victory/lose logic, the proximity bleeps, the button click, and the **mute/volume control** —
  **card 3 (#35)**.
- Any backend, DB, migration, endpoint, or SignalR work — **none** for this feature at all.
- A Web Audio (gain-node) engine rewrite — not needed; card 3 folds a master multiplier into the
  existing `HTMLAudioElement` engine.

---

## 10. File checklist

### 10.1 Meta (`monkify-meta`) — **this card**

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/audio-overhaul.md` | **New** — this manifest/spec (the whole deliverable) |
| 2 | `INDEX.md` | Add spec row (status Ready) + "Feature currently in flight" entry for `feature/audio-overhaul` |

### 10.2 Frontend (`monkify-front`) — **cards 2 & 3, same branch** (listed for context, not built here)

| Card | File | Change |
| --- | --- | --- |
| 2 | `src/assets/audios/*.mp3` (12 files) | Generate + drop in; delete `success1.wav` |
| 3 | `src/constants/helpers.ts` | Master gain/mute + live re-apply to loops (§7.1) |
| 3 | `src/components/game/GameAudio.vue` | Idle loop on `WaitingBets`; session-start sting on `SessionStarting→InProgress` (§7.2) |
| 3 | `src/components/game/GameMonkey.vue` | Own-bet import → `phone_beep.mp3` (§7.3) |
| 3 | `src/components/game/GameConsole.vue` | Error import → `error1.mp3`; add button-press SFX in `selectPresetButton` (§7.3) |
| 3 | `src/hooks/useNearMiss.ts` | Emit proximity tier events from `feed()`; fire 3/2/1 bleeps (§7.5) |
| 3 | `src/hooks/useSession.ts` (or new end-of-session audio composable) | Victory/lose selection on `Ended`, bettors only (§7.4) |
| 3 | `src/components/**/GameHeader.vue` (header) | Mute toggle + volume slider, `localStorage` persistence (§7.6) |

### 10.3 Backend (`monkify-backend`)

**None** — no files touched, no branch/worktree, no tests. (Frontend-only feature.)

---

## 11. Tests

- **Backend integration test:** **N/A** — frontend-only feature; the mandatory happy-path integration
  test in `Monkify.Tests.Integration` does **not** apply (per `constraints.md` → "Scope of new feature").
- **This card (1/3):** design/manifest only — **no code, no tests.**
- **Card 3 (FE unit tests)** — the logic added there should be unit-tested (Vitest/Jest per the front
  repo's setup), specifically:
  - **Proximity tier crossing:** feeding a character stream to the near-miss matcher fires each tier
    (3/2/1) once on downward crossing, **re-arms after a break**, and picks the **closest** own bet's
    tier; short choices fire only tiers ≤ L.
  - **Victory/lose selection:** bet & won → victory; bet & not won → lose; **no bet → neither**; fires
    once per session end.
  - **Master gain/mute:** effective volume = `base × (muted ? 0 : master)`, clamped; slider re-applies
    to a live loop; `localStorage` round-trips (default unmuted/full).

---

## 12. Acceptance criteria

Mapped 1:1 from the Trello card's **Acceptance Criteria** checklist.

1. **Manifest exists & is complete** — `specs/audio-overhaul.md` exists in `monkify-meta` on
   `feature/audio-overhaul` and lists **all 12 sounds**, each with **trigger, type (loop/one-shot),
   target duration, source (Suno), and a Suno prompt** (§5).
2. **Existing sounds accounted for** — `game_starting`, `game_running`, `phone_beep`, `error1` are
   marked **recreate-via-AI**; `game_waiting` is **repurposed** as the `WaitingBets` idle loop;
   `success1.wav` is marked **dropped** (§4, §5, §8).
3. **Proximity bleeps documented** — the spec specifies **frontend-only** computation from the streamed
   terminal text for the **local player's own bets**, the **`N away = choiceLength − matchedLength`**
   semantics, **3/2/1 escalating pitch**, **re-arm on match break**, **closest-bet-wins**, **short-choice
   handling**, and **no backend/SignalR change** (§7.5, decision #4).
4. **Victory/lose audience documented** — **victory** plays only if the local player **bet and won**,
   **lose** only if the local player **bet and lost**, and **spectators (no bet) hear neither** (§7.4,
   decision #5).
5. **Mute/volume control documented** — a game-header **mute toggle + volume slider** persisted in
   **`localStorage`**, **default unmuted**, applied via a **global gain/mute multiplier in `helpers.ts`**
   (client display-only) (§7.1, §7.6, decision #6).
6. **3-card split recorded** — cards **2 (#34)** and **3 (#35)** exist and are linked from this card,
   and **all three** record the shared **`feature/audio-overhaul`** branch/worktree and the
   **ship-together** rule (this spec's header table + the card description + the worktree comment).
7. **Spec-ready, no open questions** — a spec-writer/implementer reading this doc has **no open question**
   about **triggers, timing, win/lose audience, audio format, or generation source** — it is
   implementation-ready without further clarification.

---

## 13. Manual QA (design review)

Since this card ships a **document**, "QA" is a review pass:

1. Open `specs/audio-overhaul.md`; confirm all **12 rows** in §5 each carry trigger + type + duration +
   Suno prompt.
2. Cross-check §4 fates against the current `src/assets/audios/` listing (6 files) — every existing
   file has a fate; nothing is orphaned.
3. Confirm §7.4/§7.5/§7.6 answer the AC #3/#4/#5 questions with no "TBD".
4. Confirm the header table + Trello links tie cards #17/#34/#35 to the one `feature/audio-overhaul`
   branch.

---

## 14. Trello / pipeline

- **Source card:** [#17 `9qiDtDAe`](https://trello.com/c/9qiDtDAe) — moved to **Specs created**;
  **Implementation** section on the card lists the shared branch + this spec.
- **Sibling cards on the same branch:** [#34 `OuXhyqyU`](https://trello.com/c/OuXhyqyU) (generate
  assets), [#35 `RvVdpO25`](https://trello.com/c/RvVdpO25) (wire into game).
- **Next:** run `/monkify-create-spec` for **#34** and **#35** (they **reuse** `feature/audio-overhaul`,
  never a new slug), then `/monkify-implement-feature` per card, then **ship all three together** via
  `/monkify-ship-feature`.

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-07-07 | Initial manifest/spec from refined card #17. Audited current audio (6 `.wav`, engine, call sites, near-miss KMP infra); authored the 12-sound manifest with Suno prompts and the card-2/card-3 integration contract. |

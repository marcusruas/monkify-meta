# Monkify — "Audio Overhaul with AI" — Audio Manifest & Suno Generation Spec

> **Status: Ready for implementation** — refined via Trello card
> [Overhaul audio/songs with AI — (1/3) Audio manifest & Suno generation spec](https://trello.com/c/9qiDtDAe/17-overhaul-audio-songs-with-ai-1-3-audio-manifest-suno-generation-spec)
> (2026-07-07).
> Repos affected: **`monkify-front`** (assets & wiring — cards 2 & 3) + this doc in **`monkify-meta`**.
> **No `monkify-backend` change** — frontend-only overhaul.
>
> **Purpose:** Give Monkify a single, cohesive AI-generated sound identity in the spirit of a **warm
> 16-bit SNES soundtrack** — recreate the existing game sounds and add the missing feedback (win/lose,
> proximity bleeps, button click, idle loop) so a round *feels* alive and every player gets real-time
> audio feedback on their own bets, while staying **relaxing and easy on the ears** (this is a betting
> game — never fatiguing).

> ### ℹ️ This is the design/manifest card (1 of 3) — it produces THIS spec only
> This card writes the authoritative manifest below. It generates **no audio** and touches **no
> application code**. The other two cards implement it, **all three on the same shared branch**
> `feature/audio-overhaul`:
>
> | Card | Trello | Deliverable |
> | --- | --- | --- |
> | **(1/3) — this card** | [#17 `9qiDtDAe`](https://trello.com/c/9qiDtDAe) | `specs/audio-overhaul.md` (this file): audit + sound manifest + Suno prompts + the FE integration contract cards 2/3 consume |
> | **(2/3) — Generate assets** | [#34 `OuXhyqyU`](https://trello.com/c/OuXhyqyU) | Run every §5 item through Suno, trim/normalize, drop the files (§8) into `src/assets/audios/` |
> | **(3/3) — Wire into the game** | [#35 `RvVdpO25`](https://trello.com/c/RvVdpO25) | Engine gain/mute + all triggers (§7): idle loop, Game Starting (one-shot, glasses-sting baked in), random running variant, own-bet, victory/lose, proximity bleeps, button press, mute+volume control |
>
> **Hard rule — shared branch/worktree.** All three cards use **one** branch and **one** worktree per
> repo: `feature/audio-overhaul`. They are refined and spec'd separately (own specs) but implemented on
> the **same branch** so they cross-reference and **ship together**. `create-spec`/`implement` for
> cards 2 & 3 must **reuse** `feature/audio-overhaul` — never fork a new slug.

---

## 1. Summary

Monkify's audio today is six ad-hoc `.wav` files (~5 MB each for the loops) with no shared style, one
dead file, no win/lose feedback, no proximity cues, and no volume control. This overhaul gives it one
cohesive **warm 16-bit SNES** sound identity that is **chill and relaxing** (see §5.0 art direction):

- **Recreates** the two live music tracks (`game_starting`, `game_running`) and **remasters** the
  own-bet chirp (`phone_beep`).
- **Keeps the invalid-lever error (`error1`) exactly as-is** — not regenerated (per 2026-07-07 art
  direction).
- **Repurposes** the currently-unused `game_waiting` slot as a new **idle loop** on `WaitingBets`.
- **Drops** the never-referenced `success1.wav`.
- **Restructures** two items: the **session-start sting is folded into "Game Starting"** (no standalone
  file), and **"Game Starting" is now a one-shot** (10 s), not a loop.
- **Turns the running loop into 3 interchangeable variants** (any one plays per session; identical
  parameters).
- **Adds** cues: **victory**, **lose**, **3/2/1-away proximity bleeps**, and a **button-press** click.
- **Adds** (card 3) a game-header **mute + volume control** persisted in `localStorage`.

Everything except the retained `error1` is generated through **Suno** (music *and* SFX; short SFX may
need manual trimming). Music/loops move to compressed **mp3**; SFX stay small mp3. **No backend, DB,
migration, endpoint, or SignalR change** — proximity is computed client-side from the already-streamed
terminal text.

**Before → after (per audio slot):**

| Slot | Today (`src/assets/audios`) | After |
| --- | --- | --- |
| Idle (`WaitingBets`) | *none* (`game_waiting.wav` imported-out) | **NEW idle loop** (~60 s) in the `game_waiting` slot |
| Game Starting | `game_starting.wav` (looped) | recreated, **one-shot ~10 s**, glasses-sting baked into its tail |
| Session-started sting | *none* (was a planned standalone) | **folded into Game Starting** — no separate file |
| Running | `game_running.wav` (single loop) | **3 interchangeable loop variants** (~15 s), random per session |
| Own-bet "call" | `phone_beep.wav` | **remastered**, mp3 |
| Invalid-lever error | `error1.wav` | **unchanged — kept as-is** |
| Victory | *none* | **NEW** one-shot ~5 s |
| Lose | *none* | **NEW** one-shot ~5 s |
| 3 / 2 / 1-away bleeps | *none* | **NEW** three pitched one-shots (<1 s), Mario-coin flavored |
| Button pressed | *none* | **NEW** one-shot (~0.1 s) |
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
  SFX; if a slot needs `.wav` it may keep it — the wiring (card 3) imports whatever files land. (The
  retained `error1` stays `.wav`.)

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **Recreate via AI (Suno), with two exceptions.** Music and SFX are regenerated for one cohesive style — **except** the **invalid-lever error stays as-is** (not regenerated), and the **session-start sting is not a standalone file** (folded into Game Starting). SFX may be manually trimmed/normalized after generation. |
| 2 | **`success1.wav` is dropped** (never referenced); **`game_waiting`** becomes the new `WaitingBets` idle loop. |
| 3 | **Creative briefs + Suno prompts live in this spec** (§5) — including per-item **duration, mood, instrumentation, SNES inspiration, and (where given) the reference video URL**, which **must be passed into the Suno prompt**. |
| 4 | **Overall aesthetic (2026-07-07 art direction): warm, cozy 16-bit SNES soundtrack; relaxing and "chill".** Audio must **never be aggressive on the ears** — no very sharp/piercing tones, no harsh transients — because it plays throughout a betting game. |
| 5 | **Session-start sting folded into "Game Starting".** When the Game Starting track ends, it resolves with a short "cool" sting for the monkey to put its glasses on — **contained within** the track's duration, never exceeding it. There is no separate sting asset. |
| 6 | **"Starting loop" → "Game Starting", and it is no longer a loop.** It is a **one-shot ~10 s** track played once on `SessionStarting`. |
| 7 | **Running is 3 interchangeable variants.** The running-state music is **three** separate audios; the game plays **any one** of them per session. **All three share identical parameters** (duration, key/tempo baseline, seamless loop, the two intensity "spikes"). |
| 8 | **Proximity bleeps are frontend-only, local player's own bets only.** Client-side from the streamed terminal text. No backend change, no new SignalR event. 3/2/1 escalating pitch; re-arm on match break; closest own bet drives the tier. |
| 9 | **Victory / Lose audience = only players who bet.** Victory if the local player placed a **winning** bet; Lose if the local player bet but **did not win**; spectators (no bet) hear **neither**. Each fires once at session end. |
| 10 | **Mute + volume control** in the game header, persisted in `localStorage`, **default unmuted / full volume**, applied via a global gain/mute multiplier in `helpers.ts` (client display-only). Implemented in card 3. |
| 11 | **Format:** compressed **mp3** for music/loops (current `.wav` loops are ~5 MB); keep SFX small; retained `error1` stays `.wav`. Reversible convention. |
| 12 | **Existing tab-visibility gating is preserved** — audio does not start while the tab is inactive (`GameAudio.vue` `isActiveTab()`); the new idle loop obeys the same gate. |

---

## 4. Current audio inventory (audit)

The current engine is plain `HTMLAudioElement` — **no Web Audio graph, no gain node.** Verified files
and call sites:

**Files — `monkify-front/src/assets/audios/`:**

| File | Referenced by | Fate |
| --- | --- | --- |
| `game_starting.wav` | `GameAudio.vue` (loop on `SessionStarting`) | **recreate → one-shot** `game_starting.mp3` (~10 s, sting baked into tail) |
| `game_running.wav` | `GameAudio.vue` (loop on `InProgress`) | **recreate as 3 variants** `game_running_1/2/3.mp3` (~15 s each) |
| `game_waiting.wav` | `GameAudio.vue` — **import commented out**, currently unused | **repurpose** as the idle loop → `game_waiting.mp3` (~60 s) |
| `phone_beep.wav` | `GameMonkey.vue` (`playAudio(beep)` on own-bet call anim) | **remaster** → `phone_beep.mp3` |
| `error1.wav` | `GameConsole.vue` (`leverError()` on invalid lever) | **keep as-is — unchanged** (stays `error1.wav`) |
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

**Glasses timing (for the Game Starting sting alignment):** `GameMonkey.vue` schedules the "monkey puts
glasses on" GIF at `GLASSES_DELAY_MS = 8800` ms into `SessionStarting`. A ~10 s Game Starting track
whose sting lands at ~8.8–9.5 s therefore **lines up with the on-screen glasses moment** — target that.

**Proximity infrastructure already exists** (shipped "Characters Away"): `src/utils/nearMiss.ts`
(pure KMP: `advance(matcher, ch)` returns the live matched-prefix length `k`) + `src/hooks/useNearMiss.ts`
(module-singleton fed raw `Terminal` batches from `useSession.ts`). The proximity bleeps (§7.5) **reuse
this**, not a second matcher.

---

## 5. The sound manifest — core deliverable

### 5.0 Style bible & art direction (apply to EVERY Suno prompt)

> **Monkify house style:** a warm, cozy **16-bit SNES soundtrack** — the era of *Donkey Kong Country*,
> *Super Mario World*, and *Super Bomberman*. Rounded chiptune/FM timbres, soft bells, mellow lo-fi
> groove, a light jungle/monkey wink and a gentle slot-machine sparkle. **Instrumental, no vocals.**

**General rules — MANDATORY for every item (2026-07-07 art direction):**

1. **Relaxing and "chill" above all — never aggressive on the ears.** This is a betting game the player
   sits with; keep a calm, warm atmosphere with **no very sharp/piercing tones**, no harsh transients,
   no sudden loud stabs. Even wins, losses, and errors stay soft and rounded.
2. **One coherent SNES-inspired family.** Every item shares the warm 16-bit palette above so the whole
   game sounds like a single soundtrack.
3. **Always pass the reference into the Suno prompt.** When an item cites a track/effect (and, where
   provided, a video link), name it **explicitly** in the prompt as a *style reference* — e.g. "in the
   style of *<track>* from *<SNES game>*" **and include the video URL** — so the generation is anchored.
   Do not paraphrase the reference away.
4. Loops must be **seamless** (no audible seam at wrap); SFX must be **dry and short** with minimal
   tail; all prompts are **instrumental**.

Each item below is generation-ready: **trigger · type · target duration · mood · instrumentation ·
SNES inspiration (+ video) · Suno prompt · post-processing.**

---

#### 1. Idle loop — repurposes the `game_waiting` slot

- **Trigger:** session status `WaitingBets` (players joining / placing bets). Loops via
  `playLoopingAudio`. **New behavior** — today `WaitingBets` is silent.
- **Type:** seamless loop · **duration ~60 s (1 minute)** · target file `game_waiting.mp3`
- **Mood:** calm, inviting, low-stakes anticipation — "the lobby before the round." The most relaxing
  track in the game.
- **Instrumentation:** mellow SNES-style melody, soft synth pads, gentle shaker/groove, a little jungle
  warmth; low energy, plenty of headroom.
- **SNES inspiration:** *"Simian Segue"* (the DK Island Swing / map theme) from **Donkey Kong Country
  (SNES)** — that laid-back, breezy jungle groove.
- **Suno prompt:** *"Warm cozy 16-bit SNES soundtrack, in the style of 'Simian Segue' from Donkey Kong
  Country (SNES). Relaxing 1-minute lobby loop, seamless, low energy — laid-back breezy jungle groove,
  mellow melody, soft synth pads, gentle shaker. Calm and chill, never sharp or aggressive.
  Instrumental, no vocals, loopable with no seam."*
- **Post-processing:** trim to a clean bar-aligned loop point (~60 s); normalize; export mp3.

#### 2. Game Starting — one-shot, sting baked into the tail (was "Starting loop")

- **Trigger:** session status `SessionStarting` (the countdown to the run). Played **once** (one-shot,
  **not** a loop) via `playAudio`.
- **Type:** one-shot · **duration ~10 s** · target file `game_starting.mp3`
- **Mood:** upbeat "get ready" build that stays warm and playful, resolving into a short, **cool**
  "glasses-on" sting at the very end. Rising anticipation, but soft-edged — no piercing risers.
- **Structure (IMPORTANT):** the track **must end with a short sting** (the moment the monkey puts its
  glasses on) that is **contained within the ~10 s** — it must **not** exceed the duration. Aim the
  sting at ~8.8–9.5 s so it aligns with the on-screen glasses GIF (`GLASSES_DELAY_MS = 8800`, §4).
- **Instrumentation:** bouncy SNES lead + light percussion building over ~8 s, then a brief bright
  chord/arpeggio "snap" sting to finish.
- **SNES inspiration:** the *"Start"* jingle from **Super Bomberman (SNES)** —
  https://www.youtube.com/watch?v=OvrbXJSHN-c
- **Suno prompt:** *"Warm 16-bit SNES soundtrack, in the style of the 'Start' jingle from Super
  Bomberman (SNES) — reference video https://www.youtube.com/watch?v=OvrbXJSHN-c. A 10-second one-shot
  'get ready' intro: bouncy playful SNES lead with light percussion building anticipation for ~8
  seconds, then ending on a short, cool bright 'snap' sting (like a character putting sunglasses on).
  Keep it chill and warm, not sharp or aggressive. The ending sting must land within 10 seconds.
  Instrumental, no vocals, NOT looped — a self-contained intro with a clear ending."*
- **Post-processing:** trim to **≤10 s** with the sting fully inside; do **not** loop-align (it ends).
  Manual trim likely to place the sting at ~9 s.

#### 3. Running loop — **3 interchangeable variants**, with two intensity spikes

- **Trigger:** session status `InProgress` (the monkey is typing / the race is on). The game picks
  **one of the three variants at random** per session and loops it (§7.2).
- **Type:** seamless loop · **duration ~15 s each** · target files `game_running_1.mp3`,
  `game_running_2.mp3`, `game_running_3.mp3`
- **Identical parameters (decision #7):** all three variants share the same **duration (~15 s)**, the
  same **key/tempo baseline**, seamless looping, comparable loudness, and the same **two intensity
  "spikes"** structure — they are interchangeable, just different melodies.
- **The two "spikes":** the game speeds up **twice** during a run (the typer's display-speed ramps,
  §4/`typingSpeed`). Each variant should mirror that: start at a comfortable groove, then **build to a
  faster, more intense passage twice** (two peaks) before settling — so the music *feels* like the
  monkey speeding up. This is a **baked-in feel**, not runtime-synced to the speed events (out of
  scope). Keep even the peaks **warm and non-fatiguing** — intensity via rhythm/energy, not harshness.
- **Mood:** driving, fun, a little frantic — the peak-energy bed of the round, but still chill-leaning.
- **Instrumentation:** SNES chiptune sequence, bouncy bass, soft lo-fi drums, playful typewriter-clatter
  texture; busiest of the loops.
- **SNES inspiration:** build on the **current `game_running.wav`** feel (keep its identity) reimagined
  in the warm SNES palette, with the two added spikes.
- **Suno prompt (run 3× for three distinct melodies, same spec):** *"Warm 16-bit SNES soundtrack,
  high-energy but chill race loop, ~15 seconds, seamless. Bouncy SNES chiptune sequence, driving bass,
  soft lo-fi drums, playful typewriter-clatter texture — like a monkey typing fast. Include TWO clear
  intensity spikes where the tempo/energy builds faster and more intense, then settles, mirroring an
  in-game speed-up. Keep it warm and fun, never sharp or harsh. Instrumental, no vocals, loopable with
  no seam. (Variant N of 3 — same tempo/key/energy, different melody.)"*
- **Post-processing:** loop-align all three to the same length; loudness-match across the three and
  under the SFX; export mp3.

#### 4. Own-bet "call" — remaster `phone_beep`

- **Trigger:** the local player's **own** bet arrives (drives the monkey "call" animation in
  `GameMonkey.vue`). One-shot; can fire up to `MAX_BET_ANIMATIONS = 2` times.
- **Type:** one-shot · **duration ~0.6–1 s** · target file `phone_beep.mp3`
- **Mood:** friendly "your bet is in!" chirp — positive confirmation, phone/notification flavored, soft.
- **Instrumentation:** two-note ascending SNES chirp, clean and cute; remaster of the current sound in
  the warm palette (keep it recognizably the same cue, just cleaner/cohesive).
- **SNES inspiration:** a soft SNES-menu confirmation blip; base it on the **current `phone_beep.wav`**.
- **Suno prompt:** *"Warm 16-bit SNES sound effect: a cute ~0.8s confirmation chirp, like a friendly
  retro phone/menu notification — two-note ascending blip, clean, soft and positive ('your bet is in').
  Chill, not sharp. One-shot, dry, instrumental, no vocals."*
- **Post-processing:** trim to ≤1 s; **manual trimming** if Suno pads silence.

#### 5. Invalid-lever error — **UNCHANGED (kept as-is)**

- **Trigger:** invalid lever pull (`GameConsole.vue` `leverError()`). One-shot.
- **Decision:** **Keep the existing `error1.wav` exactly as it is** — **not** regenerated, **not**
  reformatted. No Suno prompt, no generation, no trimming. Card 2 leaves it untouched; card 3 keeps its
  current import.
- **Rationale:** the current error blip already fits and is intentionally retained per art direction.

#### 6. Victory — NEW

- **Trigger:** session `Ended` **and** the local player **bet and won** (§7.4 audience rule). One-shot,
  fires once.
- **Type:** one-shot · **duration ~5 s** · target file `victory.mp3`
- **Mood:** joyful, warm win celebration — happy and rewarding, but **soft and pleasant**, not a harsh
  blaring fanfare.
- **Instrumentation:** bright-but-round SNES victory melody resolving to a happy chord, gentle sparkle.
- **SNES inspiration:** the *"Battle Victory"* effect from **Super Bomberman (SNES)** —
  https://www.youtube.com/watch?v=GclBEQN4Ol0
- **Suno prompt:** *"Warm 16-bit SNES victory jingle, in the style of the 'Battle Victory' theme from
  Super Bomberman (SNES) — reference video https://www.youtube.com/watch?v=GclBEQN4Ol0. A ~5-second
  joyful win celebration resolving to a happy major chord, with a gentle sparkle. Warm and rewarding,
  soft on the ears — celebratory but never harsh or piercing. One-shot, instrumental, no vocals, clear
  ending within 5 seconds."*
- **Post-processing:** trim to **~5 s** (hard-cap the tail); keep it warm and non-fatiguing.

#### 7. Lose — NEW

- **Trigger:** session `Ended` **and** the local player **bet but did not win** (§7.4). Spectators
  (no bet) hear **neither** #6 nor #7. One-shot, fires once.
- **Type:** one-shot · **duration ~5 s** · target file `lose.mp3`
- **Mood:** gentle, good-humored "aww, so close" — sympathetic and soft, **never** punishing, alarming,
  or sad-core. Encourages a retry.
- **Instrumentation:** short descending SNES motif, mellow, a touch of comedic bounce.
- **SNES inspiration:** the *"Death Sound Effect"* from **Donkey Kong Country (SNES)** —
  https://www.youtube.com/watch?v=Q8b-uzoxvFI (take its playful, non-harsh "aww" character, softened).
- **Suno prompt:** *"Warm 16-bit SNES 'better luck next time' cue, in the style of the death/failure
  sound from Donkey Kong Country (SNES) — reference video https://www.youtube.com/watch?v=Q8b-uzoxvFI.
  A ~5-second gentle descending motif, mellow with a soft comedic bounce, good-humored and light —
  never harsh, alarming, or depressing. One-shot, instrumental, no vocals, clear ending within 5
  seconds."*
- **Post-processing:** trim to **~5 s**; keep clearly softer/lower-energy than victory.

#### 8–10. Proximity bleeps (3-away / 2-away / 1-away) — NEW, a pitched Mario-coin set

Generate as **one coherent, escalating three-note set** (same timbre, rising pitch/tension) so the
climb reads as one instrument. Fire semantics in §7.5.

- **Trigger:** the local player's own bet is **N characters away** from completing (§7.5). `away = 3`
  → #8, `2` → #9, `1` → #10.
- **Type:** one-shot, **very short** (**< 1 s each**) · target files `bleep_3away.mp3`,
  `bleep_2away.mp3`, `bleep_1away.mp3`
- **Mood:** escalating anticipation as the monkey closes in — but **soft and pleasant** (it may fire
  often, so it must never grate). **3-away = lowest pitch, calmest; 1-away = highest pitch, most
  excited.**
- **Instrumentation:** the classic bright-but-round **coin "ding"**, one per tier, identical timbre,
  pitch rising 3→2→1.
- **SNES inspiration:** the **coin-collected** sound from **Super Mario World (SNES)** — that satisfying
  little "ding".
- **Suno prompt (generate together, then slice):** *"Warm 16-bit SNES sound effects, in the style of
  the coin-collected 'ding' from Super Mario World (SNES). Three very short escalating coin blips, same
  clean round timbre, rising pitch and excitement: first low and calm, second mid, third high and
  bright — like getting closer to a prize. Each under 1 second, dry, staccato, spaced by a brief gap so
  they can be sliced into three separate one-shots. Soft and pleasant, not sharp. Instrumental, no
  vocals."*
- **Post-processing:** **slice into three files** at the gaps; trim each to **< 1 s** (aim ~0.3–0.6 s);
  normalize so perceived brightness/excitement rises slightly 3→1 while loudness stays gentle. **Manual
  slicing/trimming expected.**

#### 11. Button pressed — NEW (create one)

- **Trigger:** a preset-choice ("horse race") button click — `GameConsole.vue` `selectPresetButton()`.
  One-shot, very quick.
- **Type:** one-shot, **very quick** (~0.08–0.15 s) · target file `button_press.mp3`
- **Mood:** satisfying tactile arcade/console button — crisp but **soft**, neutral-positive.
- **Instrumentation:** single short "clicky" SNES-menu blip.
- **SNES inspiration:** a soft SNES menu-select/cursor click (no specific track — **create one** in the
  house palette).
- **Suno prompt:** *"Warm 16-bit SNES menu sound effect: an ultra-short ~0.1s tactile button/cursor
  click — single crisp but soft blip, satisfying and neutral-positive, like selecting an option on a
  cozy SNES menu. Not sharp or harsh. One-shot, dry, no tail, instrumental, no vocals."*
- **Post-processing:** trim to the transient only; **manual trim expected.**

---

## 6. Suno generation workflow (for card 2)

1. **Verify the Suno MCP first.** Card 2 requires the **Suno MCP** (AceDataCloud / SunoMCP), which is
   **not yet confirmed installed**. Card 2 verifies it before generating; if missing, it stops with
   install instructions. Any Suno API key stays a **mock/placeholder** in config (never a real secret).
2. **Generate** each §5 item from its prompt (skip #5 — `error1` is retained as-is). Prefer
   **instrumental** mode. **Always include the SNES reference (and video URL where given) in the
   prompt** per §5.0 rule 3. For the running variants, run the prompt **3×** for three distinct
   melodies with identical parameters; for the bleeps, generate the single §8–10 take and **slice**.
3. **Trim / normalize** (Suno rarely emits sub-second SFX or exact lengths directly — manual trimming
   is expected and allowed): enforce the per-item durations (idle ~60 s, Game Starting **≤10 s with the
   sting inside**, running ~15 s ×3, victory/lose ~5 s, bleeps <1 s), loop-align the loops, and
   loudness-match so loops sit under SFX, the three running variants match, and the bleep set rises
   3→1. Keep everything **soft and non-fatiguing** (§5.0 rule 1).
4. **Export** to the target filenames (§8) as **mp3**; drop into `monkify-front/src/assets/audios/` on
   `feature/audio-overhaul`. Leave `error1.wav` untouched; delete `success1.wav`.
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

### 7.2 Game Starting (one-shot) + random running variant + idle loop — `src/components/game/GameAudio.vue`

- **Idle loop (#1):** on `WaitingBets`, start the `game_waiting.mp3` loop (uncomment / add the import;
  add a `'waiting'` track to `currentTrack`). Obey the existing `isActiveTab()` gate and stop it on any
  other status, same as the other tracks.
- **Game Starting (#2) — now a one-shot, not a loop:** on `SessionStarting`, `playAudio` the ~10 s
  `game_starting.mp3` **once** (use `playAudio`, not `playLoopingAudio`; drop the old `'starting'` loop
  track). The glasses-sting is **baked into its tail**, so there is **no separate sting playback**. If
  `SessionStarting` outlasts ~10 s, the remaining time is intentionally quiet until `InProgress`.
- **Running (#3) — pick one of three at random:** on `InProgress`, choose one of
  `game_running_1/2/3.mp3` at random (e.g. `arr[Math.floor(Math.random() * 3)]`) and loop it via
  `playLoopingAudio`. Selection is per session/`InProgress` entry.
- All of the above respect `isActiveTab()` and stop on status change exactly as today.

### 7.3 Own-bet call + error + button press

- **Own-bet (#4):** `GameMonkey.vue` already calls `playAudio(beep)` in `tryPlayNextBetAnimation()` —
  swap the import to the remastered `phone_beep.mp3`.
- **Invalid-lever (#5):** `GameConsole.vue` `leverError()` — **no change**, keeps importing the existing
  `error1.wav`.
- **Button press (#11):** `GameConsole.vue` `selectPresetButton()` currently plays nothing — add
  `playAudio(buttonPress)` (`button_press.mp3`) at the top of it.

### 7.4 Victory / Lose — end-of-session, bettors only

Driven off the existing `useSession` state on `Status → Ended`:

- **Did the local player bet?** True iff `ownBetChoices.value.length > 0` (the durable record of the
  player's own committed choices; covers crypto, regulated, and free-bet paths). If **false → play
  neither** (spectators are silent). *(Belt-and-suspenders for crypto: an own bet is also
  `bets.some(b => b.externalPlayerId === wallet)`; `ownBetChoices` already captures it via
  `recordOwnChoice`.)*
- **Did they win?** `useSession` already exposes `winnerChoice` (= `result.firstChoiceTyped`) and
  `winners` (= `result.winners`). The player **won** iff `ownBetChoices` includes `winnerChoice`.
  *(Equivalently, own wallet ∈ `winners` for crypto; use the choice test as the primary signal since it
  works across all payment models.)*
- **Selection:** bet & won → **victory (#6)**; bet & not won → **lose (#7)**; no bet → **neither**.
  Fire **once** per session end (guard against the `Status` handler running twice), respect the tab
  gate, and reset the fired-flag on rollover (`updateNewSession`).
- **Where:** a small watcher on `sessionInstance.status === Ended` (in `useSession` or a dedicated
  end-of-session audio composable) — no new event, no backend.

### 7.5 Proximity bleeps (#8/#9/#10) — frontend-only, reuse the KMP matcher

**Reuse the existing near-miss pipeline** (`src/utils/nearMiss.ts` + `src/hooks/useNearMiss.ts`),
which already runs every streamed `Terminal` character through a per-own-choice KMP matcher.

- **Signal:** for each own choice, `advance(matcher, ch)` returns the **raw** matched-prefix length
  `k` per character. `away = choiceLength − k` = "N away". *(Use this raw `k`, **not** the composable's
  `displayK`: `displayK` is a display high-water that only drops on a 2 s stall, whereas bleeps need
  the true match state, which drops immediately when the run breaks.)*
- **Fire once, escalating:** when `away` crosses **downward** to `3`, `2`, or `1`, play the matching
  bleep once (#8/#9/#10). Escalating pitch is baked into the assets (3 = lowest … 1 = highest).
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

Under `monkify-front/src/assets/audios/` on `feature/audio-overhaul`. All **mp3** except the retained
`error1.wav`:

| # | File | Fate |
| --- | --- | --- |
| 1 | `game_waiting.mp3` | repurpose (idle loop, ~60 s) |
| 2 | `game_starting.mp3` | recreate as **one-shot ~10 s** (glasses-sting baked in) |
| 3 | `game_running_1.mp3` | recreate — running variant 1 (~15 s) |
| 4 | `game_running_2.mp3` | recreate — running variant 2 (~15 s) |
| 5 | `game_running_3.mp3` | recreate — running variant 3 (~15 s) |
| 6 | `phone_beep.mp3` | remaster |
| 7 | `victory.mp3` | new (~5 s) |
| 8 | `lose.mp3` | new (~5 s) |
| 9 | `bleep_3away.mp3` | new (<1 s) |
| 10 | `bleep_2away.mp3` | new (<1 s) |
| 11 | `bleep_1away.mp3` | new (<1 s) |
| 12 | `button_press.mp3` | new (~0.1 s) |
| — | `error1.wav` | **keep unchanged** (not regenerated) |
| — | `success1.wav` | **delete** |

**12 files to generate via Suno** (rows 1–12); `error1.wav` is retained untouched; `success1.wav` is
deleted. Because the recreated/new files are mp3 (extension changes from `.wav`), card 3 updates the
import statements in `GameAudio.vue` and `GameMonkey.vue` (add the running-variant array + one-shot
Game Starting; swap `phone_beep`); `GameConsole.vue` keeps the `error1.wav` import and adds the
`button_press.mp3` import.

---

## 9. Out of scope (this card)

- **Actual asset generation** (Suno runs, trimming, slicing, files) — **card 2 (#34)**.
- **All wiring / implementation code**, including the engine gain/mute, the idle loop, the one-shot
  Game Starting, the random running-variant pick, the victory/lose logic, the proximity bleeps, the
  button click, and the **mute/volume control** — **card 3 (#35)**.
- Any backend, DB, migration, endpoint, or SignalR work — **none** for this feature at all.
- **Runtime-syncing** the running-music "spikes" to the live typer speed events — the spikes are a
  **baked-in feel** inside each 15 s variant, not dynamically synced.
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
| 2 | `src/assets/audios/*.mp3` (12 files, §8) | Generate + drop in; **keep** `error1.wav`; **delete** `success1.wav` |
| 3 | `src/constants/helpers.ts` | Master gain/mute + live re-apply to loops (§7.1) |
| 3 | `src/components/game/GameAudio.vue` | Idle loop on `WaitingBets`; **one-shot** Game Starting on `SessionStarting` (no separate sting); **random** running variant on `InProgress` (§7.2) |
| 3 | `src/components/game/GameMonkey.vue` | Own-bet import → remastered `phone_beep.mp3` (§7.3) |
| 3 | `src/components/game/GameConsole.vue` | Keep `error1.wav`; add button-press SFX (`button_press.mp3`) in `selectPresetButton` (§7.3) |
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
  - **Running-variant pick:** `InProgress` selects one of the three variants (deterministic when
    `Math.random` is stubbed).

---

## 12. Acceptance criteria

Mapped from the Trello card's **Acceptance Criteria** checklist (with the 2026-07-07 art-direction
refinements reflected).

1. **Manifest exists & is complete** — `specs/audio-overhaul.md` exists in `monkify-meta` on
   `feature/audio-overhaul` and lists every sound, each with **trigger, type (loop/one-shot), target
   duration, source, SNES inspiration (+ video where given), and a Suno prompt** (§5). *(Structure vs.
   the card's original "12 sounds": the session-start sting is now folded into Game Starting and the
   running loop is 3 variants, so the manifest is 11 items producing **12 generated files** + the
   retained `error1` — see §8.)*
2. **Existing sounds accounted for** — `game_starting` (→ one-shot, sting baked in) and `game_running`
   (→ 3 variants) are **recreated**; `phone_beep` is **remastered**; **`error1` is kept as-is**;
   `game_waiting` is **repurposed** as the `WaitingBets` idle loop; `success1.wav` is **dropped**
   (§4, §5, §8).
3. **Proximity bleeps documented** — **frontend-only** computation from the streamed terminal text for
   the **local player's own bets**, the **`N away = choiceLength − matchedLength`** semantics, **3/2/1
   escalating pitch** (Mario-coin flavored), **re-arm on match break**, **closest-bet-wins**,
   **short-choice handling**, and **no backend/SignalR change** (§7.5, decision #8).
4. **Victory/lose audience documented** — **victory** only if the local player **bet and won**, **lose**
   only if the local player **bet and lost**, and **spectators (no bet) hear neither** (§7.4, decision
   #9).
5. **Mute/volume control documented** — a game-header **mute toggle + volume slider** persisted in
   **`localStorage`**, **default unmuted**, applied via a **global gain/mute multiplier in `helpers.ts`**
   (client display-only) (§7.1, §7.6, decision #10).
6. **3-card split recorded** — cards **2 (#34)** and **3 (#35)** exist and are linked from this card,
   and **all three** record the shared **`feature/audio-overhaul`** branch/worktree and the
   **ship-together** rule (this spec's header table + the card description + the worktree comment).
7. **Spec-ready, no open questions** — a spec-writer/implementer reading this doc has **no open question**
   about **triggers, timing, win/lose audience, audio format, generation source, durations, or the SNES
   inspirations** — it is implementation-ready without further clarification.

---

## 13. Manual QA (design review)

Since this card ships a **document**, "QA" is a review pass:

1. Open `specs/audio-overhaul.md`; confirm every §5 item carries trigger + type + **duration** +
   **SNES inspiration (+ video link where given)** + Suno prompt, and that the running item lists **3
   variants** and Game Starting notes the **baked-in sting within ≤10 s**.
2. Cross-check §4/§8 fates against the current `src/assets/audios/` listing (6 files) — every existing
   file has a fate; `error1.wav` is retained; `success1.wav` deleted; nothing orphaned.
3. Confirm §5.0's **"chill / never aggressive on the ears"** rule and the **"pass the reference/video
   into the prompt"** rule are present and reflected in every prompt.
4. Confirm §7.2/§7.4/§7.5/§7.6 answer the AC #3/#4/#5 questions with no "TBD".
5. Confirm the header table + Trello links tie cards #17/#34/#35 to the one `feature/audio-overhaul`
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
| 2026-07-07 | Initial manifest/spec from refined card #17. Audited current audio (6 `.wav`, engine, call sites, near-miss KMP infra); authored the sound manifest with Suno prompts and the card-2/card-3 integration contract. |
| 2026-07-07 | Art-direction pass (from card owner): overall **warm 16-bit SNES, chill/relaxing, never aggressive on the ears**; **always pass the SNES reference + video URL into the Suno prompt**. Folded the session-start **sting into "Game Starting"** and made **Game Starting a one-shot (~10 s)** with the sting in its tail (aligned to `GLASSES_DELAY_MS`). Turned the **running loop into 3 interchangeable ~15 s variants** with **two intensity "spikes"** each. **`phone_beep` → remaster**; **`error1` kept as-is** (not regenerated). Set durations + SNES inspirations: idle 60 s (*Simian Segue*, DKC), Game Starting 10 s (*Start*, Super Bomberman), victory 5 s (*Battle Victory*, Super Bomberman), lose 5 s (*Death SFX*, DKC), 3/2/1 bleeps <1 s (Mario-coin), button-press created. Updated §1/§3/§4/§5/§6/§7.2/§7.3/§8/§10/§11/§12/§13 accordingly. |

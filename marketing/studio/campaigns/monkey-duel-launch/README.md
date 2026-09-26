# Playbook — the live-page walkthrough video

`monkey-duel-launch` is the template for a **heavily animated walkthrough of a real game screen**:
the live page, driven through a scripted round with every request mocked, a camera that zooms the
real DOM, and captions / spotlights / title cards drawn on top. Owner-approved 2026-09-26 for the
Monkey Duel launch (posted to X).

Use it when a new mode needs a "here is the screen, here is how it works" piece. Use
`hyper-roll-win` for a plain gameplay clip and `tools/build.mjs` pieces for type-led videos with no UI.

```
cd monkify-front && yarn serve --port 8080          # devnet dev server — NOT the :6003 container
cd monkify-meta/marketing/studio
node campaigns/monkey-duel-launch/record.mjs         # ~1 min → out/monkey-duel-launch/x-01-monkey-duel.mp4
node tools/captions.mjs monkey-duel-launch           # → out/monkey-duel-launch/CAPTIONS.md (post + reply)
```

No backend, no wallet, no Docker. The MP4 is gitignored like every render; the sources here are the
truth. ⚠️ A re-render films the app **as it is today** — if the duel UI has changed since, the video
changes with it (and a selector below may need updating).

## The three files

| File | Holds | Edit it when |
| --- | --- | --- |
| `content.mjs` | every word on screen and in the post, the words/stake, music + loudness | copy changes |
| `record.mjs` | the mocks (REST + SignalR), the beat-by-beat direction, the ffmpeg mux | the story or the flow changes |
| `overlay.js` | camera, spotlight, captions, cursor, flash/shake/wipe, title cards | the look changes |

## How it works (and the owner direction behind each choice)

1. **The real page, not practice.** Owner: "use mocks to perform the actions on the screen, shouldn't
   look like a practice round". Practice also lacked half the mode (no OPEN DUELS board).
   - **REST**: `page.route` on the API origin. Game types come from the local API with the new mode's
     entry pinned; everything the flow reads (board, round, availability, balance, preferences) is a
     fixed payload. Unknown GETs fall through to the local API.
   - **SignalR**: `page.routeWebSocket(/\/Hubs\/Games/)` plays the hub — answer the
     `{"protocol":"json"}` handshake with `{}\x1e`, complete **every** invocation
     (`{"type":3,"invocationId":…}`), then send the round as the backend would
     (`{round}/Status`, `/Bets`, `/Terminal`, `/DisplaySpeed`), each argument a JSON **string**.
   - **No signature**: the lever takes the app's dev free-bet path (checkbox hidden); the round the mock
     returns carries the display stake, and every surface reads the round, so 0.05 shows everywhere.
   - **Wallet**: `swMobileWalletSession` in localStorage (seeded before boot) is a connected wallet.
2. **Fluid, not "a recording of clicking".** The camera is a CSS transform on `#app`, so zooms are
   re-rasterised sharp; graphics live in `#promo`, a sibling the camera never scales. The spotlight
   re-measures its target every frame, so it rides the camera.
   - ⚠️ **GPU flags are mandatory**: `--enable-gpu --ignore-gpu-blocklist --use-angle=d3d11`. Headless
     Chrome otherwise renders with SwiftShader and the screencast tops out at ~17 fps — every move
     steps. With them: ~41 fps captured at deviceScaleFactor 2. If a render looks choppy, check the
     WebGL renderer string first.
3. **Music low and constant.** Owner: "music kinda low and constant, no changes in it". One bed
   (`game_running_1.mp3`, loops seamlessly), **no** sfx and **no** app cues replayed. Gain is a
   **measured constant** to `video.loudness` (−20 LUFS) — never one-pass `loudnorm`, which rides the
   gain up over ~2 s and sounds like a slow fade-in.

## Gotchas this campaign hit (each cost a take)

- **Measure after the camera lands.** Mid-move, an element is somewhere else on screen: wait for the
  move (`ms`) before `glide`-ing the cursor to it.
- **Measure after the layout settles.** The tube runs an entrance for ~1.5 s after mount; framing it
  on the first frame put the camera on the control plate. Poll the target's box until it is still —
  and do it **on camera** (the power-up is the app's own motion), not behind 1.5 s of solid bars.
  Later beats are scheduled with `slip` so a slow entrance does not squeeze them.
- **Off-camera elements can't be `click()`ed** by Playwright ("outside of the viewport") — use a DOM
  click (`locator.evaluate(n => n.click())`) or `focus()`.
- **Hide, don't fake**: the SERVER SEED line (fairness evidence is never invented), the chat panel
  (fabricated chatter = fake social proof; it mounts late, so hide it on a timer), toasts, the dev
  free-bet checkbox. A mocked 404 raises an error toast — answer with an empty 200 instead.
- **Hub timers keep Node alive** — the script ends with `process.exit(0)`.
- **Lobby card selector** is `.pixelated-card.accent-<art>` where `<art>` comes from `utils/gameArt.ts`
  (`monkey-duel`), not the archetype (`duel`).

## Adapting it to the next mode

1. Copy the folder; rename `slug` / `video.id`.
2. Map the new mode's flow first — which REST calls, which hub events, which selectors. For the duel
   that was one research pass over `useGame.ts` / `useDuel.ts` / `useGameHub.ts`; write the answers
   into the header of `record.mjs`, as done here.
3. Rewrite the beats in `record.mjs` (each is `mark()` → camera → caption → action → `at(ms)`), then
   the copy in `content.mjs`.
4. Before posting: `references/compliance.md` §7 — every claim traced to `features/`, every number a
   stand-in with the win scene saying **Example round**, no "house" in our own copy, no real players.

# Space Stroker — "Marketing studio" — Implementation Spec

> **Status: DELIVERED** — built on `feature/marketing-studio` (2026-09-02).
> Repos affected: **`monkify-meta` only.** No backend, no frontend, no LP, no DB, no migration, no
> endpoint, no SignalR contract.
>
> **Purpose:** a project-owned system for producing Space Stroker marketing **images and videos**,
> replacing the need to work out of a Portuguese-language kit that lives outside the workspace
> (`~/Desktop/mkt`) and was written for a different product, brand and language.

---

## 1. Summary

`monkify-meta/marketing/` already held four campaign documents (X, Telegram, promotion channels,
UTM conventions). All four are **copy and channel strategy**; their "Media direction index" sections
point at existing art and stop there. **Nothing in the repo produced a piece.**

The gap was filled from an existing Portuguese kit that had run a real campaign. That kit's value
splits in two, and both halves were carried across:

- **Craft** — how to script a video, how a static piece is composed, how each platform crops.
- **Failures** — the defects that actually reached publication, and the checks that catch them.

Delivered: `monkify-meta/marketing/studio/`, a self-contained build system, plus a thin skill at
`monkify-meta/skills/spacestroker-marketing/` mirrored into `.claude/skills/` and `.cursor/skills/`.

**What this is NOT:** it is not channel strategy (that exists), not a posting scheduler, and not
part of the refine → spec → implement → ship pipeline. It moves no Trello card.

## 2. Constraints

- `monkify-meta` only. No application repo is touched, so **`AGENTS.md` rule 8's mandatory
  integration test has no happy path to drive** — this is tooling and content, not a feature.
  Verification is a real end-to-end render (§6).
- Worktree-first: built in `.worktrees/marketing-studio/monkify-meta` on `feature/marketing-studio`;
  the shared checkout stayed on `main`.
- Rendered output is **gitignored**. The KB repo is already large (`INDEX.md` ~415 KB) and a
  campaign's PNGs and MP4s run to tens of MB, all regenerable by one command.

## 3. Resolved decisions

| # | Decision | Reason |
| --- | --- | --- |
| 1 | **One folder**, `marketing/studio/`, holding docs + brand + tools + campaigns | The user asked for one place to look. A thin skill routes into it rather than duplicating it. |
| 2 | Port the **tools**, not just the prose | Half the source kit's value is in `render.mjs`'s sync and nominal-size fixes. Re-deriving them costs a republished post each time. |
| 3 | Brand tokens are a **transcribed copy**, not an import | A piece must open standalone in three years. Cost: an app brand change does not propagate; noted in `tokens.css`. |
| 4 | Playwright is **this folder's own dependency** | The source kit reached into an unrelated project's `node_modules` by absolute path. That breaks the moment that project moves, with an error that says nothing about marketing. |
| 5 | Copy lives in `campaigns/<slug>/content.mjs`; drawing lives in `tools/build.mjs` | Changing a headline never means opening a generator; reviewing a campaign never means reading markup. |
| 6 | Em-dash ban scoped to **rendered copy on pieces**, not prose docs | The source kit banned it globally. Here it is a typographic call at display size; the KB uses em dashes throughout and should keep doing so. |
| 7 | Audio is the **game's own**, from `monkify-front/src/assets/audios` | User request, and it makes a video sound like the product. ⚠️ `menu_lobby.mp3` flagged for provenance confirmation before publication. |
| 8 | `image-rendering: pixelated` by default on art bands | The app's own rule (`GameTypeCard.vue`, `GameTypes.vue`). The art is sprite art and is always enlarged; smoothed it reads as a mistake. Smooth brand marks stay `auto`. |
| 9 | The one measured safe zone is **carried over with its provenance** | App geometry is not campaign-specific so it transfers, but the attribution and its stated limits travel with it. Everything else is marked unmeasured. |
| 10 | TTS narration **deferred** | The valuable idea (voice durations drive scene lengths) is documented in `video.md`; the tool adds a dependency for a beat not yet needed. |

## 4. What was delivered

```
marketing/studio/
  README.md                60-second runbook + folder map
  references/
    voice.md               house voice; "check the product before the tone"
    still.md               static piece anatomy, boards, art, series
    video.md               scenes, sound, MP4 export, slideshow
    platforms.md           safe zones; measured-only rule; how to measure
    brand.md               the visual system and its sources
    compliance.md          money / token / player-safety guardrails
  brand/
    tokens.css             ~40 vars transcribed from colors.css + LP @theme
    materials.css          arcade surfaces transcribed from arcade.css (2,659 -> ~250 lines)
    fonts/                 4 woff2 copied from monkify-lp/public/fonts
    assets.md              inventory of 74 art files WITH MEASURED RESOLUTIONS, plus audio
  tools/
    boards.mjs             nominal sizes, external to the pieces
    assets.mjs             art/audio/font resolution and base64 embedding
    browser.mjs            shared Playwright launch + settle
    checks.mjs             the two automated checks (§5)
    build.mjs              content.mjs -> self-contained .html
    export-png.mjs         stills -> PNG, size-validated
    render.mjs             video -> MP4
    slides.mjs             video scenes -> slideshow PNG, two framings
    proof.mjs              safe-zone overlay
    contact-sheet.mjs      review grid
  campaigns/_example/      a working campaign, used as the smoke test
  package.json .gitignore

skills/spacestroker-marketing/SKILL.md   (mirrored to .claude/ and .cursor/)
```

**Page contract** (English, replacing the source kit's Portuguese identifiers): `__ready`,
`__start()`, `__durationMs`, `__done`, `__startMarker`, `__sceneCount()`, `__showScene(n)`,
`__settleMs`, `__noSfx`, `renderWav({music, sfx})`. Query flags `?render=1`, `?reels=1`, `?slides=1`.

## 5. The checks, which are the reason this is a pipeline and not a design tool

Both run in the page against what the browser actually drew, so neither can be fooled by intent.

1. **Line-break check** (new; the source kit documented the rule but performed it by hand). Counts
   distinct rendered line tops against `<br>` + 1 and **fails the export**. Necessary because
   Press Start 2P advances exactly 1em per glyph: a headline that fits at sixteen characters wraps
   at seventeen with no visual warning. `--allow-wrap` is the deliberate escape hatch.
2. **Art-upscale check** (new). Reports the enlargement factor from the painted box against
   `naturalWidth`. Warns rather than fails, because `pixelated` makes moderate enlargement
   legitimate. Caught `game/keyboard.png` at **7.4×** in the example.

Carried over from the source kit: nominal-size validation against an **external** table
(`boards.mjs`) — the original bug was a check that measured an element and compared it to itself,
which passed on a deliberately wrong piece.

## 6. Verification

No test suite exists or is applicable. The bar was a real end-to-end render, all of it run:

- `build.mjs _example` → 4 self-contained pieces (3 stills + 1 video, 106–597 KB).
- `export-png.mjs _example` → **1080×1350, 1080×1080, 1600×900**, independently confirmed with
  `ffprobe` rather than trusting the tool's own report.
- `render.mjs _example` → **1080×1920 H.264 + AAC, 13.8s**, magenta-marker cut at 1.03s; first frame
  sampled at RGB(16,9,14), confirming no magenta leak; frames pulled at 0.05/1.3/3.9/6.8/9.8/12.5s
  confirmed the correct scene in each.
- `slides.mjs _example` → 5 slides at 1080×1920, all passing the wrap check.
- `proof.mjs`, `contact-sheet.mjs` → ran clean.

**Five defects were found and fixed by this verification**, which is the argument for having done
it: Playwright's named export not surviving import-by-path; display type not calibrated for a
monospaced face (three boards wrapped); chips ignoring the piece accent; the recorder filming page
padding and the toolbar into the MP4; and a 200ms marker being needed because two animation frames
did not survive 30fps sampling.

⚠️ Two consecutive renders of the same piece measured head slack at **0.97s** and **1.03s** —
direct confirmation that cutting from the end of the file would drift.

## 7. Known gaps

- **Only one safe zone is measured**, and it was measured for another product on one Android device.
  Every Space Stroker surface is marked unmeasured and uses the conservative fallback. Measure on
  first publication and record it in `platforms.md` §3.
- **`templates/` is empty.** The builder emits markup directly; the folder is reserved rather than
  used. Harmless, but it is a promise the layout makes and does not keep.
- **`menu_lobby.mp3` provenance** is unconfirmed for use outside the app.
- **TTS narration** deferred (decision 10).
- **No leaderboard campaign yet** — deliberately. See §8.

## 8. Follow-up: the leaderboard campaign

The trigger for this work. ⚠️ **Do not build it from Trello card #178.** That card describes a
weekly wager race with real prizes and a hall of fame. What shipped is
[`specs/leaderboards.md`](leaderboards.md) / [`features/leaderboards.md`](../features/leaderboards.md)
(card #182, merged 2026-09-02), and it differs in ways that decide the entire campaign:

- **No prize, no pool, no season, no payout.** It moves no money. The paying half was cut by the
  owner during refinement.
- **Only one of three boards renders.** The gamble race is live; weekly PNL and hall of fame sit
  behind `VUE_APP_WEEKLY_PNL_ENABLED` and `VUE_APP_HALL_OF_FAME_ENABLED`, both fail-closed `false`
  and both **build-time**. ⚠️ The hall of fame — the board card #178 called "the shareable asset for
  X/Telegram" — is the one that is currently invisible.
- **"gamble", not "wager"**, in the copy and in the wire contract, by owner request.
- **Rolling 7 days, not a calendar week**, so "the race ends Sunday" would be false.
- `/leaderboard` is a shareable route, unlisted in the ☰ menu.

This is precisely the failure mode `references/voice.md` §1 is built around, and it is why that
section exists.

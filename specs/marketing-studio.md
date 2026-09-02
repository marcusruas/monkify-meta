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
    captions.mjs           CAPTIONS.md posting sheet: caption + hashtags + day per piece
  campaigns/_example/      a working campaign, used as the smoke test
  campaigns/leaderboard-launch/  the first real campaign (see §8)
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

**Six defects were found and fixed by this verification**, which is the argument for having done
it: Playwright's named export not surviving import-by-path; display type not calibrated for a
monospaced face (three boards wrapped); chips ignoring the piece accent; the recorder filming page
padding and the toolbar into the MP4; a 200ms marker being needed because two animation frames did
not survive 30fps sampling; and — caught by `proof.mjs` on the first real campaign — the **story,
reels and slides margins being written in `cqw` as though they were height percentages**, which put
the mark at 12% and the handle at 81% of a 1080x1920 board, i.e. inside both interface bands. `cqw`
is a percent of the WIDTH, so 20% of 1920 is 35.6cqw, not 20cqw. Corrected at the layout level, so
every future piece inherits the fix.

⚠️ Two consecutive renders of the same piece measured head slack at **0.97s** and **1.03s** —
direct confirmation that cutting from the end of the file would drift.

## 7. Known gaps

- **Only one safe zone is measured**, and it was measured for another product on one Android device.
  Every Space Stroker surface is marked unmeasured and uses the conservative fallback. Measure on
  first publication and record it in `platforms.md` §3.
- **`menu_lobby.mp3` provenance** is unconfirmed for use outside the app.
- **TTS narration** deferred (decision 10).
- **Only the gamble race can be advertised** until the other two boards are switched on; those are
  build-time flags, so it is a frontend rebuild, not a toggle. See §8.

## 8. The leaderboard campaign (delivered on the same branch)

The trigger for this work, built once the leaderboard code reached `main` in `monkify-front` and
`monkify-backend` (2026-09-02). Lives at `marketing/studio/campaigns/leaderboard-launch/`.

⚠️ **It was NOT built from Trello card #178.** That card describes a weekly wager race with a real
prize pool and a hall of fame. What shipped is
[`specs/leaderboards.md`](leaderboards.md) / [`features/leaderboards.md`](../features/leaderboards.md)
(card #182), and it differs in ways that decided the entire campaign:

- **No prize, no pool, no season, no payout.** It moves no money; the owner cut the paying half
  during refinement. So the campaign sells **status**, and says so outright: *"No prize. Just the
  top of the board."*
- **Only one of three boards renders.** The gamble race is live; weekly PNL and hall of fame sit
  behind `VUE_APP_WEEKLY_PNL_ENABLED` and `VUE_APP_HALL_OF_FAME_ENABLED`, both fail-closed `false`
  and both **build-time**. ⚠️ The hall of fame — the board #178 called "the shareable asset for
  X/Telegram" — is the one that is currently invisible, so **no piece references it**.
- **"gamble", not "wager"**, in the copy and the wire contract, by owner request.
- **Rolling 7 days, not a calendar week**, so "the race ends Sunday" would be false.

**The campaign's hook is a real product property, not a claim.** `RequiredAmount` is fixed per game
type and it is one bet per player per round, so the feature doc's own trap list records that a
gamble race "is separated by **rounds played**, not bet size". Nobody can buy the top of this board,
because there is no bet-sizing dimension to buy it with. In a category where leaderboards belong to
whoever deposits most, that is the honest differentiator — and it is checkable in the code.

**Delivered:** 5 stills (feed / 2× square / X 16:9 / story), a **14.2s** 1080×1920 MP4, a 5-slide
slideshow cut, safe-zone proofs, a contact sheet and `CAPTIONS.md`. Every still passed the
line-break and nominal-size checks; the video's five scenes were confirmed by sampling a frame in
each.

⚠️ **No fabricated leaderboard was drawn.** A piece showing invented ranks and wallet names is a
fake metric with extra steps, and `references/compliance.md` §3 forbids it. The pieces are
typographic and state only facts traceable to `features/leaderboards.md`.

# Brand

The visual system, and the rules that keep a piece recognisably Space Stroker rather than
generically retro.

Unlike a general-purpose marketing kit, **none of this is a question to ask.** The brand is fixed,
it is already implemented in two shipped codebases, and the marketing tokens are transcribed from
them.

---

## 1. Where it comes from

| Source | Role |
| --- | --- |
| `monkify-front/src/assets/styles/colors.css` | The app's token source of truth |
| `monkify-lp/src/styles/global.css` (`@theme`) | The LP's, and the two agree by construction |
| `monkify-front/src/assets/styles/arcade.css` | The material system, 2,659 lines |
| [`../brand/tokens.css`](../brand/tokens.css) | The marketing subset, transcribed |
| [`../brand/materials.css`](../brand/materials.css) | The handful of surfaces a piece needs |

⚠️ **The marketing files are a COPY, not an import.** A piece is a self-contained file that must
still open in three years, long after any build tooling has moved on. The cost is that an app brand
change does not reach here on its own: if the amber ever changes, change it here too and date the
change in the campaign's `content.mjs`.

## 2. Direction

A beat-up space arcade cabinet. Art-directed chaos, not clean SaaS minimalism. Retro without being
a nostalgia costume: the pixel grid is real, the type is genuinely a bitmap face, the surfaces are
lit like moulded plastic.

The mascot is **Edson**, a space chimpanzee. He types.

## 3. Colour, and the accent rule

Amber `#ffa214` is the brand. Everything else is scarce **and already means something in the
product**:

| Token | Means, in the app | So on a piece |
| --- | --- | --- |
| `--color-primary` amber | The coin, the primary action | The default accent |
| `--color-cyan` | Screen glow, focus. One per grey panel | Fairness, verification, the screen |
| `--color-magenta` | Live / bet pulse — **and the lottery mode** in the lobby | Only when the piece is about liveness or Space Jackpot |
| `--color-live` green | "Something is happening right now" | Live rounds, real-time |
| `--color-orbit` violet | **Orbit, the custodial wallet.** Identity only | Only when the piece is about Orbit |
| `--color-phosphor` | Text the monkey typed, on a black screen, in the display face | Quoted terminal output, and nothing else |

**A piece picks amber plus at most one other, and it picks the one that fits its subject.** A
leaderboard piece is not magenta because magenta looks good — magenta already says *lottery* to
anyone who has opened the lobby. `tools/build.mjs` rejects an unknown accent rather than quietly
defaulting to amber, because a piece that silently loses its accent looks fine and is wrong.

Surfaces are a deep-space ramp from `--color-void-deep` through `--color-surface-2`; text is the
warm near-white `--color-ink` down to `--color-ink-mute`.

## 4. Type

Two faces. There is no third.

- **Press Start 2P** — display. Headlines, kickers, chips, the mark, the handle.
- **VT323** — text. Subs, body, anything read in quantity.

Both are self-hosted, copied from `monkify-lp/public/fonts`, and embedded as base64 by the builder
so a piece is one file.

⚠️ **Press Start 2P advances exactly 1em per glyph.** A line's width is literally its character
count, which has three consequences the whole system is shaped around:

1. **A line that fits at sixteen characters wraps at seventeen**, with no visual warning. This is
   why `tools/checks.mjs` counts rendered lines against the `<br>` you wrote, and fails the export.
2. **Display sizes have to be calibrated for it**, not borrowed from a proportional face. The
   builder settled on 5.2cqw for feed and square (~16 characters per line), 5.6cqw for story, and
   3.8cqw for the landscape boards, whose copy column is narrower because it occupies the left half.
3. **Character budgets, not word counts**, are the useful way to write a headline here.

⚠️ **Fonts load with `font-display: block`, deliberately** — the opposite of the right choice on a
website. A piece is photographed, and a swap would let the exporter catch a frame drawn in the
fallback face, whose different metrics move every hand-placed line break.

## 5. Materials

Ported verbatim in `materials.css`. The distinction that matters:

- **`.arcade-panel`** is the cabinet — the stuff the machine is *made of*. Bevelled, gradient,
  amber key-drop.
- **`.crt-screen`** is the glass the game shows *through*. Flat, near-black, thin cyan rim,
  deliberately not the cabinet material.

Plus `.scanlines`, `.pixel-notch`, `.chip`, `.pixel-rule` and the glows. Chips follow the piece
accent rather than a fixed amber, because one piece has one accent.

Marketing-only additions with no app equivalent: `.space-ground` (a **static** starfield — see
[`platforms.md`](platforms.md) §5), `.edge-vignette`, and the `.art-band` fuse gradient.

## 6. Pixel art scaling

The app's own rule, and the marketing system adopts it:

- **Pixel art → `image-rendering: pixelated`.** Every art band defaults to this
  (`GameTypeCard.vue`, `GameTypes.vue`). It is what makes an unavoidable 2–3× enlargement read as
  deliberate pixel art instead of a blurry photo.
- **Smooth brand marks → `auto`.** The coin, and any third party's logo. `arcade.css` and
  `OrbitStatStrip.vue` both make this call, the latter noting that pixel-snapping someone else's
  logo is not our decision.

Sizes and what is big enough for what: [`../brand/assets.md`](../brand/assets.md).

## 7. Audio

The game's own sounds, from `monkify-front/src/assets/audios`. Using them is what makes a video
sound like the product rather than like stock music, and players already associate them with it.

Full inventory, the size trade-off, and the one file whose provenance must be confirmed before
publication: [`../brand/assets.md`](../brand/assets.md) → *Audio*.

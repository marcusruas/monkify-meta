# Safe zones per platform

Every app draws its own interface around your piece, and **each one does it differently**. Getting
this wrong does not spoil the piece on your monitor; it spoils it on the phone of the person seeing
it, which is the only place that counts.

> **House rule: a number that was not measured does not go in a table here.** If you must publish
> before measuring, use the conservative fallback at the bottom and come back to correct it.

---

## 1. Two families, and they want opposite margins

The central trap is assuming app interfaces behave alike. They do not:

| Family | What the app does | What the layout must do |
| --- | --- | --- |
| **Overlaid** | The piece fills the screen and the interface is drawn **on top** | Reserve generous bottom margin, or text disappears behind a button |
| **Inset** | The piece is fitted to the width and the interface sits **below** it | That same generous margin becomes a **dead gap**. Use nearly the full height |

**Instagram Stories is overlaid. TikTok photo mode is inset.** Treating the second like the first
produces the defect that looks like a rendering offset: a hollow strip at the bottom of the piece
with the app's own black band right below it. Reserving space for interface that is not there is
exactly as bad as failing to reserve for interface that is.

This is why `tools/slides.mjs` takes an argument instead of guessing.

## 2. How to measure, with the piece already published

Do not deduce it from the app's documentation — a version bump changes everything. Publish a piece,
screenshot it, measure.

**The trick:** use an element **you drew** whose position as a percentage you already know — the
mark, a rule, the frame. It is your ruler, because in the screenshot you know where it *should* be
relative to the whole image.

Two known positions give you the rest:

```
image height on screen = (pixels between two landmarks) / (their difference in %)
image top              = pixel of the upper landmark - (its %) x height
```

A **negative** top means the image is being cropped upward, and you have just measured by how much.
Measure with two different pairs (one horizontal, one vertical): if they agree, the measurement is
good.

Then convert the app's own furniture — the top menu, the step dots, the column of action icons — to
percentages of the image. Those are the numbers that go in the table.

## 3. TikTok photo mode — MEASURED

⚠️ **Provenance:** measured **2026-08-29** on a 20:9 Android (610×1356 in the screenshot), from a
real published post, in the campaign this system was adapted from — **not** from a Space Stroker
post. The geometry is a property of the app, not of whose picture it is, so it transfers; the
sample size does not improve by being copied here.

| What | Where, as % of the image |
| --- | --- |
| Cropped at the top | **0% to 5%** |
| Behind the top menu (Explore / Following / For You) | to **17%** |
| Like / comment / share column | from **88% of the width** |
| Step dots and caption block | from **92%** |

**How it fits:** the image is drawn at the **full screen width** and centred vertically in an area
that excludes the caption block. There is leftover image, and it is cropped top and bottom, roughly
evenly. Name, caption, music and the tab bar are **outside** the image, below it.

**Layout targets:**

- Content between **19% and 86%** of the height.
- Nothing past **80% of the width** (leaving slack before the icon column at 88%).
- **No progress bar of your own.** The app draws its dots.

**Honest limit:** one device, one aspect ratio. How much the app crops depends on screen height, so
expect more than 5% on a squarer phone. The targets above have slack for that, but measure again on
another device and record it here.

## 4. Not yet measured

Do not invent these. Publish, measure by §2, fill in.

| Surface | Likely family | State |
| --- | --- | --- |
| Instagram Reels | overlaid | **not measured** |
| Instagram Stories | overlaid | **not measured** |
| Instagram feed carousel | inset | **not measured** |
| YouTube Shorts | overlaid | **not measured** |
| TikTok video | overlaid | **not measured** |
| X timeline | inset (16:9 shown whole) | **not measured** |

Until then, use this — conservative, and safe for both behaviours:

- Content between **20% and 78%** of the height.
- Nothing past **80% of the width**.
- The mark below 20%, never against the top edge.

It wastes space on an inset surface, deliberately. That is the cheap error. The expensive one is
text vanishing behind a button.

`tools/proof.mjs` carries these same numbers and draws them over an export. Red is cropped, amber is
under interface. Run it for the surface a piece is going to, before the piece goes anywhere.

## 5. The safe zone is not only about text

Easy to get right for the copy and forget for the picture: **the subject of the background art has
to land in the visible band too.** In the top third it hides behind the app's menu, and the piece
publishes an empty throne.

Fix it in this order:

1. **Move the whole art band down.** Costs nothing and works immediately: the top becomes a dark
   strip, which is exactly where the app draws its interface, so nothing is lost.
2. **Only then zoom in.** Zooming also works, but it spends resolution, and resolution is what this
   art does not have (see [`../brand/assets.md`](../brand/assets.md)).

⚠️ **A still piece must not have a moving background.** If the background breathes, the framing
depends on the instant the photograph was taken, and two slides from one series come out with the
art in different places. `materials.css` uses a static field for exactly this reason.

## 6. One piece, two framings

When the same artwork serves video and slideshow, do not force a single framing. The zones differ
enough that copy is cramped in one and loose in the other.

What works: one base stylesheet with the design, and a block per surface changing only the margins,
the type size, and what appears or disappears (the progress bar). Here that is the `reels` /
`slides` class on `<body>`, switched by `?reels=1` or `?slides=1`.

With smaller margins there is less width, so **the type has to come down with them** — otherwise a
line wraps somewhere ugly. After changing a margin, re-run the export: `tools/checks.mjs` counts the
rendered lines against the `<br>` you wrote and fails if they disagree.

## 7. Measure the piece, not the preview

Before measuring any safe zone, confirm the export is at **nominal size**. A piece that fits itself
to the browser window exports smaller than it should, and then everything measured afterwards is
measured against the wrong object: the percentages still agree with each other, and the published
file is still at 94% with the app stretching it back.

This has really happened: a stage sized `min(94vw, ...)` exported at 1016×1806 instead of
1080×1920, which in video became a **32px black border** at the sides and 58px top and bottom. It
survived several rounds of review, because a black border on a black piece is invisible.

```bash
ffprobe -v error -show_entries stream=width,height -of csv=p=0 piece.png
```

A number that is not exactly nominal is a stop-everything.

`tools/export-png.mjs` and `tools/slides.mjs` both enforce this, and the expected size comes from
`tools/boards.mjs` rather than from the piece — because comparing an element against itself is a
question that always answers yes. That was the original bug: the first version of the check measured
the element and compared it to the element, and passed on a deliberately wrong piece.

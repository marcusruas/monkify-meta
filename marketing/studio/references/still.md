# Static pieces

A still has about half a second to stop a thumb, and it gets to deliver **one idea**. A video has
time to argue. Do not write a still like a small video.

The output is a self-contained `.html` per piece, drawn at the exact publish size, exported to PNG
by `tools/export-png.mjs` (or by the piece's own button, for someone without Node).

---

## 1. Pick the board

Sizes live in [`../tools/boards.mjs`](../tools/boards.mjs), which is also what the exporter checks
against. Default is `feed`.

| Board | Pixels | For |
| --- | --- | --- |
| `feed` | 1080×1350 | **The Instagram default.** Takes more screen than a square. |
| `story` | 1080×1920 | Story, Reels cover, TikTok, **and a YouTube Short's cover**. |
| `square` | 1080×1080 | Carousels, LinkedIn, a tidy grid. |
| `x16` | 1600×900 | **X.** The only shape its timeline does not crop. |
| `thumb` | 1280×720 | Landscape video thumbnail. **Not** a Short's cover. |

⚠️ **The X piece is born 16:9; it is never the feed piece re-cropped.** The X timeline crops tall
images hard enough to eat the mark and half the headline. Drawing it properly also buys two things:
most source art is landscape and finally fits, and the reading axis turns — on a wide board the copy
sits in the **left half** with the veil coming from the left, not from the bottom. Reusing the
bottom gradient on a 16:9 is what makes it look squashed. The builder already does this per board.

⚠️ **YouTube has two formats and this is the trap.** `thumb` is for a landscape video. A **Short**
stands upright in the Shorts feed and in the channel grid, so a landscape cover is centre-cropped
and loses its sides. A Short's cover is `story`. And check whether the video needs a custom cover at
all — the frame the platform picks is often fine, especially for a video that opens on its subject.

## 2. Anatomy

Four blocks, maximum. If there are five, cut one.

1. **Kicker** — small, uppercase, letter-spaced, in the accent. One to three words of context.
2. **Headline** — the idea, three to eight words, genuinely large. One word may take the accent.
3. **Sub / chips / stat** — pick exactly **one**. These replace body copy; they never stack on it.
   - *chips*: two or three short items. A glance, not a list.
   - *stat*: one enormous figure with a label. **Only with a real number** — see
     [`compliance.md`](compliance.md) §3.
4. **Handle** — always. It is the only thing that keeps a screenshot traceable.

If the headline explains itself, do not write a sub out of obligation.

## 3. The rules that separate a good piece from a template

- **One idea per piece.** Two ideas is two pieces.
- **Write line breaks by hand**, with `<br>`. Then count the rendered lines against the `<br>` you
  wrote. `tools/checks.mjs` does this on every export and **fails**, because the display face is
  monospaced at 1em per glyph: a headline that fits at sixteen characters wraps at seventeen with no
  warning, usually stranding one word.
- **Cut the copy, never the type size.** Type that had to shrink to fit is copy that was too long.
- **High contrast.** Check it at 150px wide — `tools/contact-sheet.mjs` has a toggle for exactly
  this. If the headline stops reading there, it will not read in a feed.
- **Generous margins.** The builder uses ~7.4% of the board width. Text against the edge is the
  signature of an amateur piece, and platforms crop a little in preview anyway.
- **One accent, not five.** Amber plus at most one other, and the other one must *mean* what the
  piece is about — cyan, magenta, green, violet and phosphor each already signify something in the
  product. See [`../brand/tokens.css`](../brand/tokens.css). The builder rejects an unknown accent
  rather than silently falling back.

## 4. Art

A piece can be type-only and be strong — the display face carries a board on its own, and the
builder centres the copy when there is no art. Reach for art when it earns its place.

**The arrangement that works:** art occupies a band at the top, copy sits below it on solid ground,
and a gradient fuses the two. The subject stays whole and the text is never read over a busy field.

⚠️ **The gradient must be fully opaque BEFORE the image ends.** If it is still translucent where the
`<img>` is cut, the image's straight edge draws a visible line across the piece. Nobody sees it in a
draft; everybody sees it in a feed. `materials.css` closes the fuse at 78% of the band for this
reason.

⚠️ **Art behind the whole piece is a trap.** The obvious rescue is to darken the image until the
text reads, and at that point the image is a smudge and was not worth including. Either it gets its
own band and appears properly, or it does not go on.

**Framing is three variables, not one.** Horizontal position, a zoom, and the focus point that
stays put while it zooms. The zoom solves the common case: the subject is usually in the top third,
which is exactly where the mark and the gradient live, so putting the focus *above* the subject and
zooming in brings it down into the piece instead of leaving it squashed behind the branding.

**The veil is per piece.** A colour wash over the art makes different sources look like one
campaign — but the opacity that settles a bright image erases a dark one, and then the subject is
gone and the art was pointless. This happened while building the example: `space-bg.png` at `0.42`
went entirely black and had to come down to `0.1`.

**Resolution decides your layout.** Everything here is web sprite art. See
[`../brand/assets.md`](../brand/assets.md) for the real sizes and which files are big enough for a
full-bleed band. The exporter reports the enlargement factor on every piece.

## 5. Campaigns are series, not pieces

A single post does nothing. What works is a sequence with an arc:

1. **The gap** — the problem the audience feels but has not named.
2. **The idea** — the turn, without detailing the product yet.
3. **The taste** — one concrete, true thing about the product.
4. **The call** — what to do now.

Number files by their **position in the calendar**, not their subject (`s2-01-name`), so the folder
reads as a schedule.

⚠️ **Before launch the call to action is "follow", never "go to the site".** Sending traffic to
something that is not ready burns an audience once.

## 6. Ship the caption with the piece

Each still in `content.mjs` carries its own `caption`, `hashtags` and `when`. The image never
travels alone:

- **Caption** — first line repeats or completes the headline, since that is what shows before the
  fold. Then two to four short lines. Then the call to action.
- **Hashtags** — five to twelve, mixing broad and narrow. Not a wall of generic tags.

## 7. Make a contact sheet before you ship

`node tools/contact-sheet.mjs <slug>` puts every export side by side. It is where the defects a
single piece hides become obvious: a background used twice, one piece far darker than its
neighbours, six pieces framed identically, a word repeated on adjacent pieces. You will not catch
any of those reviewing pieces one at a time, which is how everyone reviews them.

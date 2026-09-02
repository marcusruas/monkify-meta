# Marketing studio

Builds Space Stroker marketing pieces — **static images and videos** — from a single copy file,
using the app's own design system, art and audio. Everything renders from HTML through real Chrome;
nothing here needs a designer's licence, a screen recording, or an export from another tool.

Adapted from a Portuguese-language marketing kit that ran a real campaign. What carried over is the
craft and, more importantly, the **failures it recorded**. What was rewritten is everything
brand-, product- and language-specific.

---

## 60-second runbook

```bash
cd monkify-meta/marketing/studio
npm install                              # once. Also: npx playwright install chromium

node tools/build.mjs       <slug>        # content.mjs  ->  self-contained .html pieces
node tools/export-png.mjs  <slug>        # stills       ->  PNG at exact publish size
node tools/render.mjs      <slug>        # video piece  ->  MP4 (H.264 + AAC)
node tools/slides.mjs      <slug>        # video scenes ->  PNG, for a slideshow post
node tools/proof.mjs       <slug> tiktok-photo   # draw the app's safe zones over the exports
node tools/contact-sheet.mjs <slug>      # every piece side by side, for review
node tools/captions.mjs    <slug>        # CAPTIONS.md: the posting sheet
```

Output lands in `out/<slug>/` and is **gitignored**: sources are the truth, and every image and
video is one command away.

Try it now — `_example` is a real, working campaign:

```bash
node tools/build.mjs _example && node tools/export-png.mjs _example
```

**Requires:** Node 18+, `ffmpeg`/`ffprobe` on `PATH`, and Playwright's chromium. All three are
present on the machine this was built on.

## Where things are

```
references/     the instructions. Read these before writing a campaign.
  voice.md        how we sound, and the check that matters most
  still.md        static piece anatomy and rules
  video.md        scene scripting, sound, MP4 export, slideshow
  platforms.md    per-surface safe zones. Measured only
  brand.md        the visual system and where it comes from
  compliance.md   money, token and player-safety guardrails. Outranks the rest

brand/          tokens.css · materials.css · fonts/ · assets.md (what art exists, how big)
tools/          the pipeline (see runbook above)
                build - export-png - render - slides - proof - contact-sheet - captions
campaigns/      one folder per campaign, each with a content.mjs
out/            rendered output (gitignored)
```

## How a campaign works

One folder, one file to read:

```
campaigns/<slug>/content.mjs     copy, art choices, scene timings, captions, hashtags
campaigns/<slug>/pieces/         generated .html (gitignored, never hand-edited)
```

`content.mjs` exports `campaign`, `stills[]` and `video`. That is the whole authoring surface —
copy lives there, drawing lives in `tools/build.mjs`, so changing a headline never means opening a
generator and reviewing a campaign never means reading markup.

Start by copying `campaigns/_example/content.mjs`.

## What the tools refuse to let you ship

The point of a pipeline over a design tool is that it can *check*. These are not style opinions;
each one is a defect that reached publication in the campaign this was adapted from.

- **Off-nominal export.** A piece scaled to fit the browser exports at ~94% and the app stretches it
  back. Verified against `tools/boards.mjs`, which is deliberately **outside** the piece being
  measured — comparing an element to itself is a question that always answers yes.
- **Self-wrapped lines.** The display face advances exactly 1em per glyph, so a headline that fits
  at sixteen characters wraps at seventeen with no warning, usually stranding one word. The exporter
  counts rendered lines against the `<br>` you wrote and **fails**.
- **A/V drift.** Recording starts with the window, so the head carries page-load slack that varies
  run to run (0.97s then 1.03s on two runs of the same piece). The piece paints a magenta frame at
  t=0 and the cut is the end of it, never the end of the file.
- **Page chrome in the video.** The recorder captures the window, so padding and toolbar would be
  filmed in as a border — invisible on a black design. Under `?render=1` the piece *is* the frame.
- **Silent files, silently.** Asking for effects on a music-only piece is refused before any work
  happens, instead of producing a mute MP4 nobody notices until it is posted.
- **Over-enlarged art.** Warns with the factor. The art here is web sprite art (see
  `brand/assets.md`) and is nearly always being enlarged; past ~4× the subject stops reading.

## The rule that outranks all of them

**Write from `features/` and `specs/`, never from a Trello card.**

A card is what someone hoped to build. In August 2026 a card described the leaderboard as a weekly
wager race with real prizes; what shipped on 2026-09-02 has **no prize, no pool and no payout** —
the paying half was cut during refinement. A campaign written from the card would have advertised
money that does not exist.

See `references/compliance.md` before writing a word.

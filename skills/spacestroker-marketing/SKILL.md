---
name: spacestroker-marketing
description: Create Space Stroker marketing content — social images, animated videos rendered to MP4, slideshows and captions — built from the app's own design system, art and audio. Use when the user asks for a marketing piece, post, banner, reels/short/video, story, carousel, thumbnail, launch campaign or promo material for Space Stroker (Monkify). Renders from HTML via Playwright + ffmpeg; no screen recording. Needs monkify-meta/marketing/studio.
---

# Space Stroker — marketing

Turn a feature or a launch into **publishable pieces**: static images at exact platform sizes, an
animated video rendered straight to MP4, a slideshow cut from the same scenes, and the captions to
post them with.

Everything lives in **`monkify-meta/marketing/studio/`**. This file routes you into it; it does not
duplicate it. The studio README is the runbook, `references/` is the craft.

---

## Before anything else: write from what shipped

**Read `monkify-meta/features/<feature>.md` and `specs/<feature>.md`. Never write a campaign from a
Trello card.**

A card is what someone hoped to build. The leaderboard card (#178, Aug 2026) described a *weekly
wager race with real prizes*; what shipped as #182 on 2026-09-02 has **no prize, no pool, no
payout**, and only **one of its three boards is even visible** — the other two sit behind
fail-closed build-time flags. A campaign written from the card would have advertised money that
does not exist, and pointed at boards nobody can see.

Then read **`studio/references/compliance.md`**. It outranks every aesthetic judgement in this
skill: no promised money, no invented metric, no token ticker, no named player without consent.

## The workflow

1. **Establish the facts.** Open the `features/` doc. Write down what is true, what is *live* (flags
   matter), the currency (**SOL**, not USDC, since 2026-08-20) and the vocabulary (**gamble**, not
   wager). Anything you cannot source, you cannot claim.
2. **Agree the shape** with the user: which surfaces, how many pieces, one video or a series, and
   what the call to action is. Pre-launch, the CTA is *follow*, never *visit the site*.
3. **Read the references you need** — `voice.md` always; then `still.md`, `video.md`, and
   `platforms.md` before fixing any margin.
4. **Write `campaigns/<slug>/content.mjs`.** Copy `campaigns/_example/content.mjs` and edit. This is
   the only file that holds copy. Record decisions (currency, vocabulary, what was deliberately not
   claimed) in `campaign.notes` so a reader in six months can recover them.
5. **Build and export**, then *look at the output*:
   ```bash
   cd monkify-meta/marketing/studio
   node tools/build.mjs <slug> && node tools/export-png.mjs <slug>
   node tools/render.mjs <slug>          # if there is a video
   node tools/slides.mjs <slug>          # slideshow cut, if it suits the content
   node tools/proof.mjs <slug> <surface> # safe zones, for the surface it is going to
   node tools/contact-sheet.mjs <slug>   # review the set, not the piece
   ```
6. **Verify, do not assert.** Read the PNGs. Pull frames from the middle of each video scene and
   check they are the right scenes. Run the pre-publish checklist at the end of `compliance.md`.
7. **Hand over** the file paths, the captions and the suggested schedule. Output is in `out/<slug>/`
   and is gitignored — say so, and say which command regenerates it.

## What the tools enforce, so you do not have to remember

Off-nominal exports, headlines that wrapped where you did not ask, A/V drift, page chrome recorded
into the video, a silent MP4 produced without warning, over-enlarged art. Each is a real defect that
reached publication once. If a check fails, **fix the piece** — `--allow-wrap` exists for
deliberate exceptions, not for getting past a check.

## Setup

`npm install` in `studio/`, plus `npx playwright install chromium`. Needs `ffmpeg` and `ffprobe` on
`PATH`. Node 18+.

## Scope

This skill makes **pieces**. Channel strategy, posting schedules and where to post already exist and
are not duplicated here:
`marketing/twitter-launch-campaign.md`, `marketing/telegram-launch-campaign.md`,
`marketing/promotion-channels.md`, `marketing/utm-conventions.md`.

It is not part of the `monkify-refine-tasks` → `create-spec` → `implement-feature` → `ship-feature`
pipeline and moves no Trello card.

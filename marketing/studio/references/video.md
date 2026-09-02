# Video

A short video that runs in the browser and renders to MP4 without anyone recording a screen. The
`.html` **is** the video: scenes, animation, and a soundtrack built from the game's own audio.

```
node tools/build.mjs  <slug>     content.mjs  ->  the piece
node tools/render.mjs <slug>     the piece    ->  MP4
node tools/slides.mjs <slug>     the piece    ->  PNG per scene (slideshow)
```

---

## 1. Format and length

| Format | Pixels | For |
| --- | --- | --- |
| `reels` | 1080×1920 | Reels, Stories, TikTok, Shorts. **The default.** |
| `square` | 1080×1080 | Feed video. |
| `wide` | 1920×1080 | YouTube, the site, an ad. |

**Ten to twenty seconds.** Short converts. The example runs 13.8s across five scenes.

## 2. Scenes

Four to six scenes, two to four seconds each:

1. **Hook (0–3s)** — the line that stops the scroll. Big type, few words.
2. **Premise** — the one-line explanation.
3. **Substance** — the concrete thing. Three chips, quick rhythm.
4. **Trust** — why it is not a trick. Provably fair, on-chain.
5. **Close** — the name and where to go.

**The first three seconds decide everything.** A weak hook makes the rest irrelevant.

Scene durations are authored in `content.mjs` and drive both picture and sound, because
`tools/build.mjs` computes the absolute scene times once and hands the same numbers to both. They
cannot drift apart.

**Few words per scene.** A video is not a paragraph. And the same hand-written-line-break rule
applies — `tools/slides.mjs` runs the wrap check on every scene, which is the easiest way to catch
a scene that overflows, since nobody reviews a video frame by frame.

## 3. Animation

Already in the template, so this is about restraint rather than technique:

- Scene entry is `opacity` plus a small `translateY`, on `--ease-out-expo`.
- Copy staggers: kicker, then headline, then body. Chips pop in sequence on `--ease-arcade`.
- A progress bar fills across the piece. **Hidden in slideshow mode** — the app draws its own step
  dots, and two progress indicators on one screen is confusion.
- The background is **static**. See [`platforms.md`](platforms.md) §5 for why a breathing
  background ruins a set of stills.

## 4. Sound

Every sound is a **real clip from the game** (`monkify-front/src/assets/audios`), which is what
makes a video sound like the product instead of like stock. Set `music` for a bed and a per-scene
`sfx` to mark each cut.

Read [`../brand/assets.md`](../brand/assets.md) → *Audio* before publishing anything with a bed —
particularly the note about `menu_lobby.mp3`, whose provenance must be confirmed first.

**A recorded page is always silent.** Page recording captures picture only. The soundtrack is
rendered separately, offline, by the piece itself, and muxed in by ffmpeg.

⚠️ **There is exactly one scheduling function**, used by the live preview and the offline render
alike. It takes a context and an absolute time; it never says "play now". The moment there are two,
the file that ships stops being the thing that was approved.

**On Reels and TikTok, publishing silent is usually the better move** — pick a trending sound in the
app, where it helps distribution and the licence is already handled. `--silent` renders that
version. Keep the sounded one for YouTube, the site, or anywhere it must stand alone. ⚠️ Instagram
on the **web** has no sound picker, so a silent file posted from a desktop stays a silent Reel.

**Never embed commercial music.** Inside Reels and TikTok the licence belongs to the app; the same
MP4 posted elsewhere becomes a copyright claim.

Output is normalised to **-14 LUFS**, which is where the networks normalise anyway. Louder just gets
turned back down; quieter does not get lifted.

## 5. Rendering, and the three things that break it

`tools/render.mjs` does it in three steps: record the page at nominal size, render the soundtrack
offline in the same page, mux to H.264 + AAC. All three of the following are already handled — this
is so you know why the code looks the way it does, and do not "simplify" one away.

**Sync.** Recording starts with the *window*, so the head of the file carries page-load slack the
audio does not have, and it differs every run — measured at 0.97s and then 1.03s on two consecutive
runs of the same piece. Trimming from the **end** feels safe and is wrong, because the recorder
keeps running after the piece finishes while it waits and flushes. So the piece paints a **magenta
frame** at t=0 and the cut is the end of that frame. It must last long enough to survive at 30fps:
two animation frames (~33ms) was not enough and the detector found nothing; it is now 200ms.

**Nominal size.** The recorder captures the whole window, so anything that is not the piece gets
filmed into the MP4 as a border — and on a black design nobody sees it. Under `?render=1` the page
drops its padding and hides its toolbar so the piece **is** the frame. A stage scaled to fit a
laptop screen must never be recorded; that is how a series ships at 94% with the app stretching it
back.

**Throttling.** Chrome drops animation frames when a window is not focused. The launch flags in
`tools/browser.mjs` disable that. Without them the recording stutters.

**Verify the result, do not trust the process.** Pull a frame from the middle of each scene and
check it is the right scene:

```bash
ffmpeg -ss 6.8 -i out/<slug>/<id>.mp4 -frames:v 1 frame.png
ffprobe -v error -show_entries stream=width,height -of csv=p=0 out/<slug>/<id>.mp4
```

## 6. Sometimes the answer is a slideshow, not a video

TikTok has photo mode and Instagram has carousels: several images, swiped, with the step indicator
drawn by the app. **For content made of text this usually beats video**, for three reasons:

- **The reader sets the pace.** Nobody loses a line by reading slowly, and anyone can go back.
- **The step indicator is free**, and it is the one that audience already recognises.
- **The soundtrack is chosen in the app**, where the licence is handled.

The scenes already exist, so it is one command. Two things change on export, both handled:
the progress bar is hidden, and each scene is photographed only after its entry animation settles
(`__settleMs`, 1300ms) — shoot earlier and a chip is caught halfway in on one slide out of five.

⚠️ **The framings are opposite and not interchangeable.** `node tools/slides.mjs <slug>` produces
the **inset** framing (TikTok photo mode, interface *below* the piece, content may use to 86%);
adding `overlaid` produces the **Stories** framing (interface *on top*, content to 78%). Publishing
the inset export to Stories is the exact mistake [`platforms.md`](platforms.md) §1 describes.

**One script difference decides which format to use:** in a slideshow, slide one is a **cover** and
has to hold on its own, with no animation and no sound. If the first scene is only context, keep the
video. Where both work, ship both — it is the same work and they cover different bets.

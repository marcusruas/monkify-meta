# Asset inventory

What art and audio exist, how big they actually are, and what each is for.

**Rule: reuse only, no new art.** The X and Telegram campaign docs already committed to this
(`marketing/twitter-launch-campaign.md` §5, `marketing/telegram-launch-campaign.md` §5), and it is
the right call: the art is a coherent set, and a one-off illustration made for one post is the
fastest way to make a feed look like it came from two companies.

Everything below lives in **`monkify-lp/src/assets/`**, mirrored (under different names, and not
completely) in `monkify-front/src/assets/`. `tools/assets.mjs` searches the LP first, then the app,
so a shorthand like `edson.png` or `game/keyboard.png` just resolves.

*Resolutions measured 2026-09-02. If one looks wrong, re-measure — do not trust this table over
`ffprobe`.*

---

## The resolution problem, and it decides your layout

This is web sprite art. It was drawn to sit at 200–400px inside a page, not to fill a 1080-wide
board. A marketing piece is between 1080 and 1600 across, so **almost everything here is being
enlarged**, and the exporters report the factor for exactly this reason.

Two things make that survivable, and one thing does not:

- **`image-rendering: pixelated` is the default** on every art band, matching what the app does in
  `GameTypeCard.vue` and `GameTypes.vue`. Snapped to the pixel grid, an enlargement reads as pixel
  art — which is what the brand is. Smoothed, the same image reads as a low-resolution photograph,
  which reads as a mistake. The exception is the same one the app makes: a smooth brand mark (the
  coin) stays `auto`.
- **Recompose instead of enlarging.** Give the art a shorter band, move it off full-bleed, or pick
  a bigger file. There is no high-resolution version of these to go and fetch.
- **What does not work is pushing through it.** Past roughly 4×, the subject stops being readable
  however crisp the blocks are. `game/keyboard.png` at 260×120 in a full-width band is a **7.4×**
  enlargement, and it looks like grey rectangles. That piece wants to be type-only.

**A type-only piece is a first-class option, not a fallback.** The display face is loud enough to
carry a board on its own, and the builder centres the copy when a piece declares no art.

---

## Big enough for a full-bleed band (use these first)

| File | Size | What it is |
| --- | --- | --- |
| `space-bg.png` | 1920×1080 | The starfield. The only true landscape plate; the natural choice for an `x16` board. |
| `edson-ship-typing.gif` | 1850×1160 | **The signature shot**: Edson typing in his saucer. The face of the brand, and the largest character art there is. |
| `cards/cosmicsprint.gif` · `-poster.png` | 1320×1270 | Cosmic Sprint mode card. The poster is the still frame — prefer it for a still piece. |
| `backstory/part1.png` … `part6.png` | 1280×640 | The six story panels, in order. Landscape, and a ready-made carousel. |
| `roadmap/traceline.png` | 2220×690 | Roadmap trace. Decorative, very wide, rarely what you want. |

## Usable with care (1.5× to 3×)

| File | Size | Notes |
| --- | --- | --- |
| `cards/spacejackpot.gif` · `-poster.png` | 840×1200 | Portrait — the one card that suits a `story` board. |
| `game/typing.gif` · `idle.gif` · `call.gif` | 880×800 | Edson at the keyboard. `typing` is the workhorse. |
| `game/typing2.gif` | 740×720 | Keys flying. |
| `cards/beastmode.gif` · `-poster.png` | 670×750 | Beast Mode card. ⚠️ **Beast Mode is `ComingSoon` and unplayable** — do not build a piece that implies otherwise. |
| `backstory/mobile1..6.png` | 640×750 | Portrait cuts of the story panels. |
| `elements/saturn.png` · `blackhole.png` · `galaxy.png` | ~1010×530 and down | Set dressing. |

## Small — mark, chip and inset only, never a band

| File | Size | Notes |
| --- | --- | --- |
| `monk-coin.png` · `-main.png` · `-spin.gif` | 480×500 | **The coin.** Used as the mark on every piece. Rendered `auto`, not pixelated: it is a smooth brand asset, the same call `arcade.css` makes. |
| `edson.png` | 480×480 | Portrait crop of the mascot. At full-bleed on a feed board this is ~2.4× — acceptable, and it is flagged on export. |
| `cards/a.png` · `b.png` · `c.png` | ~254×449 | Small mode cards. Too small for a band; fine as an inset. |
| `game/keyboard.png` | 260×120 | ⚠️ **7.4× in a full-width band.** Effectively unusable as a plate. |
| `game/leverup.png` · `leverdown.png` · `lever_to_*.gif` | 140×220 | The lever. Tiny; an inset at most. |
| `team/*.png` | 360×480 | The three team avatars. |
| `elements/star*.gif`, `roadmap/icon*.png` | ≤230px | Decoration. The starfield is drawn in CSS instead (`materials.css` → `.space-ground`), so these are rarely needed. |

⚠️ **A different filename does not guarantee a different picture.** Catalogues reuse art, and a
`-poster.png` is a still of the `.gif` beside it. Before a series ships, look at the contact sheet:
identical files are caught by their size, but the same scene in a different crop is caught only by
your eye.

⚠️ **A GIF in a still export is frozen at whatever frame the capture landed on**, which is not
necessarily a good one and is not necessarily the same one twice. For a still piece, use the
`-poster.png` where one exists.

---

## Audio — `monkify-front/src/assets/audios/`

The game's own sounds. Using them is what makes a video sound like the product rather than like
stock, and they arrive already synchronised to the brand because players hear them in the app.

`tools/build.mjs` embeds these as base64 and schedules them through the piece's own scheduling
function, so what ends up in the MP4 is what you heard in the browser.

| File | Size | Good for |
| --- | --- | --- |
| `button_press.mp3` | 5 KB | The cut marker. One per scene change; this is the default. |
| `phone_beep.mp3` | 7 KB | A lighter tick, for a list item arriving. |
| `error1.wav` | 45 KB | A negative beat. Use sparingly and never to mock the viewer. |
| `lose.mp3` | 53 KB | Same, with more finality. |
| `victory.mp3` | 87 KB | The win. Natural closer for the final scene. |
| `game_starting.mp3` | 244 KB | Short bed with a build. The default music bed here. |
| `game_running_1/2/3.mp3` | ~305–335 KB | Longer beds, loopable. |
| `game_waiting.mp3` | 977 KB | Slow, ambient. |
| `menu_lobby.mp3` | 2.5 MB | The lobby theme. ⚠️ See below before this goes anywhere public. |

**Three things about audio that cost money or reach if you get them wrong:**

1. ⚠️ **`menu_lobby.mp3` needs its provenance confirmed before it is published in a video.** At
   2.5 MB it is a composed music track, not a game sound effect, and the licence that lets us ship
   it *inside the app* is not automatically a licence to publish it as the soundtrack of a video on
   someone else's platform. Shipping it in the app is not evidence either way. Ask before using it;
   the short beds are safe and are game audio we clearly own.
2. **A bed makes the file much larger.** Everything is embedded so a piece stays one file, so a
   2.5 MB bed becomes ~3.4 MB of base64. `game_starting.mp3` keeps a video piece near 600 KB.
3. **On Reels and TikTok, the usual best move is to publish SILENT** and pick a trending sound
   inside the app: it helps distribution and the licence is handled there. Render with `--silent`
   for that. Keep the sounded version for YouTube, the site, and anywhere the video has to stand on
   its own — and note that Instagram on the **web** has no trending-sound picker, so a silent file
   posted from a desktop stays silent.

**Also true and easy to forget:** a browser will not play audio before a click, so a page that
autoplays on open is silent until someone interacts. It does not affect rendering (the soundtrack
is produced offline), only what you hear while reviewing.

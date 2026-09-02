// The two checks that the reference docs demand and that nobody performs by eye.
//
// Both run in the page, against what the browser ACTUALLY drew, so neither can be fooled by what
// the markup intended.
//
// ── 1. LINE BREAKS ───────────────────────────────────────────────────────────
// still.md: write the line breaks by hand, then count the lines that rendered against the <br>
// you wrote. A mismatch means the type is too wide for the column and a line wrapped itself,
// usually leaving one orphan word — the single thing that most makes a piece look unintentional.
//
// It matters more here than in most design systems because the display face is Press Start 2P,
// whose every glyph is exactly 1em wide. A line's width is literally its character count, so a
// headline that fits at fifteen characters breaks at sixteen with no warning, and "it looked fine
// in the last piece" is not evidence about this one.
//
// ── 2. ART UPSCALE ───────────────────────────────────────────────────────────
// The art in these repos is web sprite art: edson.png is 480x480, cards/a.png is 254x449,
// game/keyboard.png is 260x120. Drawn across a 1080-wide band they are being enlarged two to four
// times. With `image-rendering: pixelated` that reads as deliberate pixel art rather than as a
// blurry photo, which is why the pieces set it — but past a point the subject stops being legible
// no matter how crisply the blocks are drawn.
//
// So this reports the factor rather than forbidding it. There is no public high-resolution version
// of these assets to fetch, so the fix is never "enlarge harder": it is to recompose — give the
// art a shorter band, move it off full-bleed, or choose one of the few large files
// (space-bg.png 1920x1080, backstory/part*.png 1280x640).

/** Runs both checks in the page. Returns { wraps: [...], upscales: [...] }. */
export async function inspect(page) {
  return page.evaluate(() => {
    const wraps = [];
    const upscales = [];

    for (const el of document.querySelectorAll('.headline, .sub')) {
      // Only count VISIBLE elements: a video piece holds every scene in the DOM at once and the
      // inactive ones are transparent, but they still lay out and would report bogus lines.
      const cs = getComputedStyle(el);
      if (cs.display === 'none' || cs.visibility === 'hidden') continue;
      const scene = el.closest('.scene');
      if (scene && !scene.classList.contains('active')) continue;

      const authored = el.querySelectorAll('br').length + 1;

      /* Count DISTINCT LINE TOPS, not client rects. A rect is produced per inline box, so a
         headline with an <em> inside reports several rects on one line; grouping by rounded top
         collapses those back into the line they actually are. */
      const range = document.createRange();
      range.selectNodeContents(el);
      const tops = new Set();
      for (const r of range.getClientRects()) {
        if (r.height > 1 && r.width > 0) tops.add(Math.round(r.top));
      }
      const drawn = tops.size;
      if (drawn > authored) {
        wraps.push({
          what: el.className,
          authored,
          drawn,
          text: el.textContent.trim().slice(0, 70),
        });
      }
    }

    for (const img of document.querySelectorAll('.art-band img')) {
      const scene = img.closest('.scene');
      if (scene && !scene.classList.contains('active')) continue;
      if (!img.naturalWidth) continue;
      /* getBoundingClientRect already includes the zoom transform, so this is the size the image
         is genuinely painted at. `object-fit: cover` scales the content to cover that box, so the
         effective factor is whichever axis has to stretch further. */
      const r = img.getBoundingClientRect();
      const factor = Math.max(r.width / img.naturalWidth, r.height / img.naturalHeight);
      if (factor > 1.5) {
        upscales.push({
          src: (img.currentSrc || img.src).slice(0, 24) + '…',
          natural: `${img.naturalWidth}x${img.naturalHeight}`,
          drawn: `${Math.round(r.width)}x${Math.round(r.height)}`,
          factor: Number(factor.toFixed(2)),
        });
      }
    }

    return { wraps, upscales };
  });
}

/** Prints upscale warnings. Never fatal: with pixelated rendering this is a judgement call. */
export function reportUpscales(label, upscales) {
  for (const u of upscales) {
    console.log(
      `  ⚠ ${label}: art ${u.natural} drawn at ${u.drawn} (${u.factor}x). ` +
        'Pixel-crisp, but check the subject still reads. Recompose rather than enlarge.',
    );
  }
}

/** Turns wrapped lines into an error, unless the caller opted out. */
export function assertNoWraps(label, wraps, allow) {
  if (!wraps.length) return;
  const detail = wraps
    .map((w) => `  ${w.what}: wrote ${w.authored} line(s), rendered ${w.drawn} — "${w.text}"`)
    .join('\n');
  if (allow) {
    console.log(`  ⚠ ${label}: line breaks differ from the markup (allowed by --allow-wrap)\n${detail}`);
    return;
  }
  throw new Error(
    `${label}: the browser broke lines you did not write.\n${detail}\n\n` +
      'The export no longer matches the layout you approved, and a self-wrapped line usually ' +
      'strands one word on its own. Shorten the copy (not the type size — that is the rule in ' +
      'references/still.md), or move the <br>. Pass --allow-wrap to export anyway.',
  );
}

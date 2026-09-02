// Exports every still of a campaign as a PNG at its nominal publish size.
//
//   node tools/export-png.mjs <campaign-slug>
//
// Why this exists next to the "Save PNG" button that every piece already has: the button is for
// someone without Node, and its renderer is not the browser's normal paint path. Here the painter
// is real Chrome, so the PNG is exactly what the piece is.
//
// THE CHECK IS THE POINT. Two failures this refuses to let through, both of which have shipped
// before in the system this is adapted from:
//
//   · the preview transform left on, so a 1080x1920 exports at 1016x1806 and the app stretches it
//     back — a defect invisible on dark art and visible only to ffprobe;
//   · the window too narrow for the widest board, so the screenshot silently includes page
//     background and the toolbar instead of the piece.
//
// The expected size comes from tools/boards.mjs, NOT from the piece being measured. Comparing an
// element against itself is a question that always answers yes.

import { existsSync, mkdirSync, readdirSync, rmSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

import { BOARDS, BOARD_NAMES } from './boards.mjs';
import { LAUNCH_ARGS, chromium, settle } from './browser.mjs';
import { assertNoWraps, inspect, reportUpscales } from './checks.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const slug = process.argv[2];
if (!slug || slug.startsWith('--')) throw new Error('usage: node tools/export-png.mjs <campaign-slug> [--allow-wrap]');
const allowWrap = process.argv.includes('--allow-wrap');

const PIECES = join(here, '..', 'campaigns', slug, 'pieces');
const OUT = join(here, '..', 'out', slug, 'png');

async function main() {
  if (!existsSync(PIECES)) throw new Error(`no pieces at ${PIECES}\nRun first: node tools/build.mjs ${slug}`);

  const files = readdirSync(PIECES).filter((f) => f.endsWith('.html') && !f.startsWith('_')).sort();
  if (!files.length) throw new Error(`no pieces in ${PIECES}`);

  rmSync(OUT, { recursive: true, force: true });
  mkdirSync(OUT, { recursive: true });

  const launch = await chromium();
  const browser = await launch.launch({ args: LAUNCH_ARGS });
  const page = await browser.newPage({ viewport: { width: 1700, height: 2000 }, deviceScaleFactor: 1 });

  let done = 0;
  for (const file of files) {
    await page.goto(pathToFileURL(join(PIECES, file)).href);
    await page.waitForFunction(() => window.__ready === true, null, { timeout: 30000 }).catch(() => {});
    await settle(page);

    // Which board this piece declared, and therefore how big it MUST come out.
    const cls = await page.evaluate(() => document.body.className.trim());
    const board = BOARD_NAMES.find((n) => cls.split(/\s+/).includes(n));
    if (!board) {
      // A video piece declares `reels`, not a board. Skip it; render.mjs and slides.mjs own it.
      console.log(`${file.padEnd(40)} skipped (not a still)`);
      continue;
    }
    const nominal = BOARDS[board];

    /* The window has to hold the WHOLE piece. The widest board here is 1600px, and in a narrower
       window the element overflows — Playwright then returns an image of the right dimensions
       made of the wrong pixels, with the toolbar and page background shifted into frame. It comes
       out the correct size, so only looking at it catches the problem. Resize per piece. */
    await page.setViewportSize({
      width: nominal.w + 80,
      height: Math.min(nominal.h + 200, 4000),
    });
    await settle(page);

    /* Checked at the FINAL viewport, because the column width the type has to fit in is settled
       only after the resize above. Checking earlier would report line breaks from a layout that
       is not the one being photographed. */
    const { wraps, upscales } = await inspect(page);
    assertNoWraps(file, wraps, allowWrap);
    reportUpscales(file, upscales);

    const target = page.locator('.art');
    const box = await target.boundingBox();
    const name = file.replace(/\.html$/, '.png');
    await target.screenshot({ path: join(OUT, name) });

    if (Math.round(box.width) !== nominal.w || Math.round(box.height) !== nominal.h) {
      throw new Error(
        `${file}: exported ${Math.round(box.width)}x${Math.round(box.height)}, but board ` +
          `"${board}" is ${nominal.w}x${nominal.h} (${nominal.where}).\n` +
          'Stop and fix this before measuring anything else — every safe-zone number you take ' +
          'from here on would be measured against the wrong object.',
      );
    }
    console.log(
      `${name.padEnd(40)} ${box.width}x${box.height}  ${board.padEnd(7)} ` +
        `${Math.round(statSync(join(OUT, name)).size / 1024)} KB`,
    );
    done++;
  }

  await browser.close();
  console.log(`\n${done} PNG in ${OUT}`);
}

main().catch((e) => {
  console.error('\nfailed:', e.message);
  process.exit(1);
});

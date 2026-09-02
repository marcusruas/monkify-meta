// Photographs each scene of the video piece, so it can be published as a SLIDESHOW instead.
//
//   node tools/slides.mjs <campaign-slug>            inset framing  -> out/<slug>/slides/
//   node tools/slides.mjs <campaign-slug> overlaid   overlaid frame -> out/<slug>/stories/
//
// WHY BOTHER, when the video already exists: for content made of TEXT, a slideshow usually beats
// it. The reader sets the pace, so nobody loses a line by reading slowly and anyone can go back;
// the step indicator comes free and is the one the audience already recognises from that app; and
// the soundtrack gets chosen inside the app, where the licence is already handled.
//
// ⚠️ THE TWO FRAMINGS ARE NOT INTERCHANGEABLE, which is why this takes an argument.
//   inset    (TikTok photo mode) fits the piece and draws its interface BELOW it, so the piece may
//            use nearly the full height.
//   overlaid (Instagram Stories) draws its interface ON TOP, so the same piece loses its footer
//            behind a button.
// Publishing the inset export to Stories is exactly the mistake platforms.md §1 is about.
//
// One difference of SCRIPT decides whether a slideshow is even the right call: slide one is a
// cover and has to hold on its own, with no animation and no sound to help it. If the first scene
// is only context, keep the video.

import { existsSync, mkdirSync, readdirSync, rmSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

import { FORMATS } from './boards.mjs';
import { LAUNCH_ARGS, chromium } from './browser.mjs';
import { assertNoWraps, inspect, reportUpscales } from './checks.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const slug = process.argv[2];
if (!slug) throw new Error('usage: node tools/slides.mjs <campaign-slug> [overlaid]');
const overlaid = process.argv.includes('overlaid');
const allowWrap = process.argv.includes('--allow-wrap');

const DIR = join(here, '..', 'campaigns', slug);
const OUT = join(here, '..', 'out', slug, overlaid ? 'stories' : 'slides');

async function main() {
  const content = await import(pathToFileURL(join(DIR, 'content.mjs')).href);
  const video = content.video;
  if (!video) throw new Error(`campaign "${slug}" declares no video in content.mjs`);
  const piece = join(DIR, 'pieces', `${video.id}.html`);
  if (!existsSync(piece)) throw new Error(`piece not built: ${piece}\nRun first: node tools/build.mjs ${slug}`);

  const fmt = FORMATS[video.format];
  rmSync(OUT, { recursive: true, force: true });
  mkdirSync(OUT, { recursive: true });

  const launch = await chromium();
  const browser = await launch.launch({ args: LAUNCH_ARGS });
  const page = await browser.newPage({ viewport: { width: fmt.w, height: fmt.h }, deviceScaleFactor: 1 });

  // `?slides=1` also hides the piece's own progress bar: the app draws its step dots, and two
  // progress indicators on one screen is just noise.
  await page.goto(pathToFileURL(piece).href + (overlaid ? '?reels=1' : '?slides=1'));
  await page.waitForFunction(() => window.__ready === true, null, { timeout: 30000 });
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(600);

  const count = await page.evaluate(() => window.__sceneCount());
  // How long entry animations take to finish, declared by the piece. Photograph earlier and a chip
  // is caught halfway in, on one slide out of six, which is the kind of thing nobody notices until
  // it is posted.
  const settle = await page.evaluate(() => window.__settleMs ?? 1400);
  const stage = page.locator('.art');

  for (let i = 0; i < count; i++) {
    await page.evaluate((n) => window.__showScene(n), i);
    await page.waitForTimeout(settle);

    // Same two checks the still exporter runs. `inspect` looks only at the ACTIVE scene, so this
    // reports the slide being photographed rather than the five sitting transparent behind it.
    const { wraps, upscales } = await inspect(page);
    assertNoWraps(`scene ${i + 1}`, wraps, allowWrap);
    reportUpscales(`scene ${i + 1}`, upscales);

    const name = `${overlaid ? 'story' : 'slide'}-${String(i + 1).padStart(2, '0')}.png`;
    const box = await stage.boundingBox();
    if (Math.round(box.width) !== fmt.w || Math.round(box.height) !== fmt.h) {
      throw new Error(
        `slide ${i + 1}: stage is ${Math.round(box.width)}x${Math.round(box.height)}, expected ` +
          `${fmt.w}x${fmt.h}. The preview transform is still on.`,
      );
    }
    await stage.screenshot({ path: join(OUT, name) });
    console.log(`${name}  ${box.width}x${box.height}`);
  }

  await browser.close();
  console.log(`\n${readdirSync(OUT).length} slides in ${OUT}`);
  console.log(
    overlaid
      ? 'OVERLAID framing (Stories): content between 20% and 78% of the height'
      : 'INSET framing (TikTok photo mode): content between 19% and 86% of the height',
  );
}

main().catch((e) => {
  console.error('\nfailed:', e.message);
  process.exit(1);
});

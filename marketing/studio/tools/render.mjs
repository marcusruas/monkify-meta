// Turns a campaign's video piece into an MP4 ready to publish.
//
//   node tools/render.mjs <campaign-slug>                 picture + full soundtrack (the default)
//   node tools/render.mjs <campaign-slug> --silent        no audio at all, to add sound in-app
//   node tools/render.mjs <campaign-slug> --no-music      game sounds only, no music bed
//   node tools/render.mjs <campaign-slug> --slides-frame  the inset framing (TikTok photo mode)
//
// WHY THIS IS NOT "RECORD YOUR SCREEN". A screen recording comes out at the size of the window,
// with the cursor in it, with dropped frames, and has to be redone from scratch every time a word
// of copy changes. This is three steps instead:
//
//   1. Playwright opens the piece in a window of exactly the nominal size and records it. Page
//      recording captures PICTURE ONLY — the file is always silent, which is why step 2 exists.
//   2. The same page renders the soundtrack in an OfflineAudioContext and hands back a WAV. It
//      runs the piece's OWN scheduling function, so what lands in the MP4 is what you heard.
//   3. ffmpeg muxes them into H.264 + AAC, which is what Reels, TikTok and Shorts want.
//
// SYNC, which is where this breaks if you let it. Recording starts with the WINDOW, so the head of
// the file carries page-load slack that the audio does not have, and that slack is different every
// run (measured between 0.77 and 1.05 seconds where this technique comes from). Trimming from the
// END feels safe and is not: the recorder keeps running after the piece finishes while it waits
// and flushes. So the piece paints one magenta frame at the instant it starts, and the cut is the
// end of that frame — a point both sides genuinely know.

import { execFileSync, spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

import { FORMATS } from './boards.mjs';
import { LAUNCH_ARGS, chromium } from './browser.mjs';
import { assertNoWraps, inspect, reportUpscales } from './checks.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const slug = args[0];
if (!slug || slug.startsWith('--')) throw new Error('usage: node tools/render.mjs <campaign-slug> [--silent] [--no-music] [--slides-frame]');

const silent = args.includes('--silent');
const noMusic = args.includes('--no-music');
const slidesFrame = args.includes('--slides-frame');
const allowWrap = args.includes('--allow-wrap');
if (silent && noMusic) {
  throw new Error('--silent and --no-music contradict each other: one asks for no sound, the other for some.');
}

const DIR = join(here, '..', 'campaigns', slug);
const PIECES = join(DIR, 'pieces');
const OUT = join(here, '..', 'out', slug);
const TEMP = join(OUT, '.render');
const FPS = 30;

function ffmpeg(a) {
  return execFileSync('ffmpeg', ['-hide_banner', '-loglevel', 'error', ...a], { encoding: 'utf8' });
}
function ffprobe(a) {
  return execFileSync('ffprobe', ['-hide_banner', '-loglevel', 'error', ...a], { encoding: 'utf8' }).trim();
}

/* Finds the instant the magenta marker ENDS, which is the instant the piece begins.
   Decodes only the first seconds, resampled to a FIXED rate and scaled to one pixel per frame —
   three bytes a frame, so the whole head fits in memory and nothing touches the disk. The fixed
   rate is what makes a frame index mean a time: Playwright's recording is variable-rate, and
   counting its frames would give a confidently wrong answer. */
function markerEnd(webm, seconds = 12) {
  const raw = execFileSync(
    'ffmpeg',
    ['-hide_banner', '-loglevel', 'error', '-i', webm, '-t', String(seconds),
      '-vf', `fps=${FPS},scale=1:1`, '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-'],
    { encoding: 'buffer', maxBuffer: 64 * 1024 * 1024 },
  );
  const isMagenta = (i) => raw[i] > 170 && raw[i + 1] < 90 && raw[i + 2] > 170;
  const frames = Math.floor(raw.length / 3);
  let first = -1;
  for (let f = 0; f < frames; f++) if (isMagenta(f * 3)) { first = f; break; }
  if (first < 0) return null;
  let last = first;
  while (last + 1 < frames && isMagenta((last + 1) * 3)) last++;
  // Cut at the FIRST frame after the marker; including the last magenta one leaves a pink flash at
  // the head of the published file.
  return (last + 1) / FPS;
}

async function main() {
  if (!existsSync(join(DIR, 'content.mjs'))) throw new Error(`no campaign at ${DIR}`);
  const content = await import(pathToFileURL(join(DIR, 'content.mjs')).href);
  const video = content.video;
  if (!video) throw new Error(`campaign "${slug}" declares no video in content.mjs`);

  const piece = join(PIECES, `${video.id}.html`);
  if (!existsSync(piece)) throw new Error(`piece not built: ${piece}\nRun first: node tools/build.mjs ${slug}`);

  const fmt = FORMATS[video.format];

  // One file per soundtrack variant. If the silent render overwrote the one with sound, you would
  // find out while posting.
  const suffix = (silent ? '-silent' : noMusic ? '-no-music' : '') + (slidesFrame ? '-inset' : '');
  const OUTFILE = join(OUT, `${video.id}${suffix}.mp4`);

  mkdirSync(OUT, { recursive: true });
  rmSync(TEMP, { recursive: true, force: true });
  mkdirSync(TEMP, { recursive: true });

  const launch = await chromium();
  const browser = await launch.launch({ args: LAUNCH_ARGS });

  /* ── CHECK THE SCENES FIRST, IN A CONTEXT THAT IS NOT BEING RECORDED ───────────
     The line-break check used to reach video only through slides.mjs, which is a 9:16-only path,
     so a landscape piece skipped it in silence — and nobody reviews a video frame by frame.

     ⚠️ It has to happen in its OWN context. Playwright records from the moment the recording
     context exists, so stepping the scenes inside it films ~1.3s per scene of pre-roll before the
     piece even starts. The start marker still cuts correctly, but the head of the raw file grows
     with the scene count, and `markerEnd` only scans the first 12 seconds — so a longer piece
     would fail with "no magenta frame found", pointing at sync when the real cause was this. */
  {
    const checkCtx = await browser.newContext({
      viewport: { width: fmt.w, height: fmt.h },
      deviceScaleFactor: 1,
    });
    const check = await checkCtx.newPage();
    await check.goto(pathToFileURL(piece).href + `?render=1${slidesFrame ? '&slides=1' : ''}`);
    await check.waitForFunction(() => window.__ready === true, null, { timeout: 30000 });
    await check.evaluate(() => document.fonts.ready);
    const sceneCount = await check.evaluate(() => window.__sceneCount());
    const settleMs = await check.evaluate(() => window.__settleMs ?? 1400);
    for (let i = 0; i < sceneCount; i++) {
      await check.evaluate((n) => window.__showScene(n), i);
      await check.waitForTimeout(settleMs);
      const { wraps, upscales } = await inspect(check);
      assertNoWraps(`scene ${i + 1}`, wraps, allowWrap);
      reportUpscales(`scene ${i + 1}`, upscales);
    }
    await checkCtx.close();
  }

  const context = await browser.newContext({
    viewport: { width: fmt.w, height: fmt.h },
    deviceScaleFactor: 1,
    recordVideo: { dir: TEMP, size: { width: fmt.w, height: fmt.h } },
  });
  const page = await context.newPage();

  console.log('opening the piece...');
  await page.goto(pathToFileURL(piece).href + `?render=1${slidesFrame ? '&slides=1' : ''}`);
  await page.waitForFunction(() => window.__ready === true, null, { timeout: 30000 });
  // Fonts have loaded, but the very next frame can still be painted without them.
  await page.waitForTimeout(600);

  /* A piece with no sound design refuses a "game sounds only" render BEFORE any work happens.
     Asking for sfx on a music-only piece would produce a silent MP4 with no warning at all, and
     refusing after the render works identically while wasting the render. */
  const noSfx = await page.evaluate(() => window.__noSfx === true);
  if (noSfx && !silent && noMusic) {
    throw new Error(
      `${video.id} has no sound effects, only a music bed, and --no-music asks for effects only.\n` +
        'The file would come out silent without saying so. Drop --no-music, or use --silent.',
    );
  }

  // Audio first: rendering offline does not disturb the recording, and it saves running the piece
  // through twice.
  let wav = null;
  if (!silent) {
    console.log('rendering the soundtrack...');
    const b64 = await page.evaluate((o) => window.renderWav(o), { music: !noMusic, sfx: true });
    wav = join(TEMP, 'track.wav');
    writeFileSync(wav, Buffer.from(b64, 'base64'));
  }

  console.log('recording...');
  const hasMarker = await page.evaluate(() => typeof window.__startMarker === 'number');
  const durationMs = await page.evaluate(() => { window.__start(); return window.__durationMs; });
  await page.waitForFunction(() => window.__done === true, null, { timeout: 180000 });

  const duration = durationMs / 1000;
  await context.close(); // closing the context is what finalises the webm on disk
  await browser.close();

  const rawName = readdirSync(TEMP).find((f) => f.endsWith('.webm'));
  if (!rawName) throw new Error('Playwright recorded no video');
  const webm = join(TEMP, rawName);

  const total = Number(ffprobe(['-v', 'error', '-show_entries', 'format=duration', '-of', 'csv=p=0', webm]));
  console.log(`raw: ${total.toFixed(2)}s · piece: ${duration.toFixed(2)}s`);

  let cut;
  if (hasMarker) {
    cut = markerEnd(webm);
    if (cut === null) {
      throw new Error(
        'the piece declares a start marker but no magenta frame was found in the recording.\n' +
          'Without it the cut would be early and the picture would run ahead of its own sound.',
      );
    }
    console.log(`start marker: cut at ${cut.toFixed(2)}s (tail slack ${(total - duration - cut).toFixed(2)}s)`);
  } else {
    cut = Math.max(0, total - duration);
    console.log(`no start marker: cut at ${cut.toFixed(2)}s, measured from the end`);
  }

  const videoOnly = join(TEMP, 'video.mp4');
  ffmpeg([
    '-y', '-ss', String(cut), '-i', webm,
    '-an', '-r', String(FPS),
    '-c:v', 'libx264', '-preset', 'slow', '-crf', '18',
    '-pix_fmt', 'yuv420p', '-profile:v', 'high', '-level', '4.0',
    '-movflags', '+faststart',
    '-t', String(duration),
    videoOnly,
  ]);

  if (wav) {
    /* -14 LUFS, because that is where the networks normalise anyway. Delivering louder just makes
       the app turn it back down; delivering quieter, it does not lift. Loudness, not peak, is what
       decides how a mix of music and sharp game sounds actually sounds after upload. */
    const measure = spawnSync('ffmpeg', ['-hide_banner', '-i', wav, '-af', 'volumedetect', '-f', 'null', '-'], { encoding: 'utf8' });
    // ⚠️ volumedetect writes its report to STDERR. Reading stdout returns an empty string and the
    // gain silently comes out as zero.
    const peak = /max_volume:\s*(-?[\d.]+) dB/.exec(measure.stderr || '');
    console.log(`audio: peak ${peak ? peak[1] : '?'} dB, normalising to -14 LUFS`);

    ffmpeg([
      '-y', '-i', videoOnly, '-i', wav,
      '-map', '0:v', '-map', '1:a',
      '-af', 'loudnorm=I=-14:TP=-1.5:LRA=11',
      '-c:v', 'copy', '-c:a', 'aac', '-b:a', '192k',
      '-shortest', '-movflags', '+faststart',
      OUTFILE,
    ]);
  } else {
    ffmpeg(['-y', '-i', videoOnly, '-c', 'copy', '-movflags', '+faststart', OUTFILE]);
  }

  const info = ffprobe([
    '-v', 'error',
    '-show_entries', 'stream=codec_type,codec_name,width,height,r_frame_rate',
    '-show_entries', 'format=duration,size',
    '-of', 'default=noprint_wrappers=1',
    OUTFILE,
  ]);
  rmSync(TEMP, { recursive: true, force: true });

  console.log('\n' + OUTFILE);
  console.log(info);
  console.log(
    `\nsound: ${silent ? 'none (silent, to pick an in-app audio)' : noMusic ? 'game sounds only' : 'game sounds + music bed'}`,
  );
  console.log(
    `framing: ${
      slidesFrame
        ? 'INSET (TikTok photo mode)'
        : video.format === 'reels'
          ? 'OVERLAID (Reels / Stories / Shorts)'
          : `${fmt.where} — shown whole, no interface band to dodge`
    }`,
  );
}

main().catch((e) => {
  console.error('\nfailed:', e.message);
  process.exit(1);
});

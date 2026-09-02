// Draws the platform's safe zones over an exported image, so you can SEE what the app will cover.
//
//   node tools/proof.mjs <campaign-slug> <surface>
//     surfaces: story · reels · tiktok-photo · feed · x
//
// Reads out/<slug>/{png,slides,stories}/*.png and writes out/<slug>/proof-<surface>/.
// Red   = cropped away entirely.
// Amber = still there, but underneath the app's own interface.
//
// This exists because a safe zone cannot be checked by eye. With copy anchored to the bottom, one
// piece grows a line, the block creeps up into the covered band, and nothing looks wrong until it
// is published. It takes a minute and it is cheaper than reposting.
//
// ⚠️ Only numbers that were MEASURED on a real published post belong in the table below. Where a
// surface has not been measured, the conservative fallback from references/platforms.md is used
// and labelled as such — never quietly promoted to fact.

import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readdirSync, rmSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));

/** Bands as a FRACTION of the image, so one table serves every size.
 *  `crop` is lost outright, `ui` sits under interface. */
const SURFACES = {
  'tiktok-photo': {
    label: 'TikTok photo mode — MEASURED 2026-08-29 (see platforms.md §3)',
    crop: [{ y: 0, h: 0.05 }],
    ui: [{ y: 0.05, h: 0.12 }, { y: 0.92, h: 0.08 }, { x: 0.88, w: 0.12, y: 0.17, h: 0.75 }],
  },
  story: {
    label: 'Instagram Stories — NOT MEASURED, conservative fallback',
    crop: [],
    ui: [{ y: 0, h: 0.2 }, { y: 0.78, h: 0.22 }, { x: 0.8, w: 0.2, y: 0.2, h: 0.58 }],
  },
  reels: {
    label: 'Reels / Shorts — NOT MEASURED, conservative fallback',
    crop: [],
    ui: [{ y: 0, h: 0.2 }, { y: 0.78, h: 0.22 }, { x: 0.8, w: 0.2, y: 0.2, h: 0.58 }],
  },
  feed: {
    label: 'Instagram feed — NOT MEASURED, conservative fallback',
    crop: [],
    ui: [{ y: 0, h: 0.06 }, { y: 0.94, h: 0.06 }],
  },
  x: {
    label: 'X timeline — a 16:9 piece is shown whole; this checks margins only',
    crop: [],
    ui: [{ y: 0, h: 0.05 }, { y: 0.95, h: 0.05 }],
  },
};

const slug = process.argv[2];
const surface = process.argv[3];
if (!slug || !surface) {
  throw new Error(`usage: node tools/proof.mjs <campaign-slug> <surface>\nsurfaces: ${Object.keys(SURFACES).join(' · ')}`);
}
const spec = SURFACES[surface];
if (!spec) throw new Error(`unknown surface "${surface}". Use: ${Object.keys(SURFACES).join(' · ')}`);

const ROOT = join(here, '..', 'out', slug);
const OUT = join(ROOT, `proof-${surface}`);

function size(file) {
  const out = execFileSync('ffprobe', ['-v', 'error', '-show_entries', 'stream=width,height', '-of', 'csv=p=0', file], { encoding: 'utf8' }).trim();
  const [w, h] = out.split(',').map(Number);
  return { w, h };
}

function boxes(spec, w, h) {
  const draw = (b, colour) => {
    const x = Math.round((b.x ?? 0) * w);
    const y = Math.round((b.y ?? 0) * h);
    const bw = Math.round((b.w ?? 1) * w);
    const bh = Math.round((b.h ?? 1) * h);
    return `drawbox=${x}:${y}:${bw}:${bh}:${colour}:t=fill`;
  };
  return [
    ...spec.crop.map((b) => draw(b, 'red@0.45')),
    ...spec.ui.map((b) => draw(b, 'orange@0.35')),
  ].join(',');
}

const sources = ['png', 'slides', 'stories']
  .map((d) => join(ROOT, d))
  .filter((d) => existsSync(d));
if (!sources.length) throw new Error(`nothing exported yet under ${ROOT}\nRun export-png.mjs or slides.mjs first.`);

rmSync(OUT, { recursive: true, force: true });
mkdirSync(OUT, { recursive: true });

let n = 0;
for (const dir of sources) {
  for (const f of readdirSync(dir).filter((f) => f.endsWith('.png'))) {
    const src = join(dir, f);
    const { w, h } = size(src);
    const filter = boxes(spec, w, h);
    execFileSync('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-y', '-i', src, '-vf', filter, join(OUT, f)]);
    console.log(`${f.padEnd(40)} ${w}x${h}`);
    n++;
  }
}

console.log(`\n${n} proofs in ${OUT}`);
console.log(spec.label);
console.log('red = cropped away · amber = under the app interface');
console.log('If any text, mark or frame touches a band, fix it before publishing.');

// Finds art and audio in the sibling repos and turns it into base64 data URIs.
//
// WHY EVERYTHING IS EMBEDDED, and it is two separate reasons that both bite:
//
//   1. A piece is meant to be one file you can open from disk in three years. An .html opened over
//      `file://` does not reliably load a relative image, and `fetch()` of a relative path is
//      blocked outright — which is what decodeAudioData needs. Embedded, the piece always works.
//   2. An EXTERNAL image taints the canvas, so `toDataURL` throws and a piece's own "Save PNG"
//      button silently stops working with no explanation on screen.
//
// The cost is file size, and it is real: a 2.5 MB music bed becomes ~3.4 MB of base64. That is the
// trade this system takes on purpose (see references/brand.md → "Audio"), and it is why the bed is
// a per-piece choice rather than a default.

import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, extname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));

/** <workspace-root>, i.e. the folder holding monkify-meta / monkify-front / monkify-lp.
 *  From tools/ that is five levels up: tools -> studio -> marketing -> monkify-meta -> root.
 *  ⚠️ In a worktree the repo lives at .worktrees/<slug>/monkify-meta, so the naive parent is
 *  `.worktrees/<slug>`, not the root. Both layouts are handled by walking up for the sibling. */
function findWorkspaceRoot() {
  let dir = resolve(here, '..', '..', '..'); // -> the monkify-meta repo root
  for (let i = 0; i < 6; i++) {
    dir = resolve(dir, '..');
    if (existsSync(join(dir, 'monkify-front')) && existsSync(join(dir, 'monkify-lp'))) return dir;
  }
  throw new Error(
    'could not find the workspace root (the folder containing monkify-front and monkify-lp).\n' +
      `started from ${here}`,
  );
}

export const ROOT = findWorkspaceRoot();

/* WHERE ART COMES FROM, in priority order. The LP and the app hold overlapping sets under
   different names (the LP's `edson-ship-typing.gif` is the app's `hero/ship-orbit.gif` family),
   so a shorthand is resolved against every root and the first hit wins. Ambiguity is reported
   rather than guessed at, because "nothing guarantees two files with different names are two
   different pictures" is a mistake this system's ancestor already made once. */
const ART_ROOTS = [
  join(ROOT, 'monkify-lp', 'src', 'assets'),
  join(ROOT, 'monkify-front', 'src', 'assets'),
];

const AUDIO_ROOT = join(ROOT, 'monkify-front', 'src', 'assets', 'audios');

const MIME = {
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.gif': 'image/gif',
  '.webp': 'image/webp',
  '.svg': 'image/svg+xml',
  '.woff2': 'font/woff2',
  '.mp3': 'audio/mpeg',
  '.wav': 'audio/wav',
};

function dataUri(file) {
  const type = MIME[extname(file).toLowerCase()];
  if (!type) throw new Error(`no MIME type known for ${file}`);
  return `data:${type};base64,${readFileSync(file).toString('base64')}`;
}

/**
 * Resolve an art shorthand ("edson.png", "game/keyboard.png") to an absolute path.
 * Throws with the candidate list rather than returning something plausible.
 */
export function findArt(shorthand) {
  for (const root of ART_ROOTS) {
    const p = join(root, shorthand);
    if (existsSync(p) && statSync(p).isFile()) return p;
  }
  throw new Error(
    `art not found: ${shorthand}\nlooked in:\n` + ART_ROOTS.map((r) => `  ${r}`).join('\n'),
  );
}

/** Art shorthand -> data URI. */
export function art(shorthand) {
  return dataUri(findArt(shorthand));
}

/**
 * Audio shorthand ("victory.mp3") -> data URI, from monkify-front/src/assets/audios.
 * ⚠️ These are the GAME's own sounds. See references/brand.md → "Audio" for the one file whose
 * provenance must be confirmed before it goes into anything published.
 */
export function audio(name) {
  const p = join(AUDIO_ROOT, name);
  if (!existsSync(p)) {
    const have = existsSync(AUDIO_ROOT) ? readdirSync(AUDIO_ROOT).join(', ') : '(no audios folder)';
    throw new Error(`audio not found: ${name}\nin ${AUDIO_ROOT}\navailable: ${have}`);
  }
  return dataUri(p);
}

/** Size on disk in KB, for the build report — file size is a publishing constraint, so show it. */
export function sizeKb(file) {
  return Math.round(statSync(file).size / 1024);
}

/**
 * The @font-face block, fonts embedded. Both families, both subsets.
 * Kept here rather than in brand/tokens.css because tokens.css must stay readable as CSS, and a
 * base64 woff2 is 16 KB of noise on one line.
 */
export function fontFaceCss() {
  const dir = join(here, '..', 'brand', 'fonts');
  const face = (family, file) =>
    `@font-face{font-family:'${family}';font-style:normal;font-weight:400;font-display:block;` +
    `src:url('${dataUri(join(dir, file))}') format('woff2');}`;
  // `font-display: block` (not `swap`): a piece is SCREENSHOTTED. A swap would let the exporter
  // catch a frame drawn in the fallback face, and the fallback is a different width, so the
  // headline's hand-written line breaks would land somewhere else. Blocking is correct here and
  // wrong on a website, which is why this differs from the app's own rule.
  return [
    face('Press Start 2P', 'press-start-2p-latin.woff2'),
    face('Press Start 2P', 'press-start-2p-latin-ext.woff2'),
    face('VT323', 'vt323-latin.woff2'),
    face('VT323', 'vt323-latin-ext.woff2'),
  ].join('\n');
}

/** brand/tokens.css + brand/materials.css, read as text for inlining. */
export function brandCss() {
  const dir = join(here, '..', 'brand');
  return [
    readFileSync(join(dir, 'tokens.css'), 'utf8'),
    readFileSync(join(dir, 'materials.css'), 'utf8'),
  ].join('\n');
}

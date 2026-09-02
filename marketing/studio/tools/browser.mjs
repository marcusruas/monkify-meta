// One place that knows how to get a real Chrome, so the five tools that need one agree.
//
// ⚠️ Playwright is THIS folder's dependency, resolved from `marketing/studio/node_modules`.
// The system this was adapted from reached into an unrelated project's node_modules by absolute
// path to avoid a second 300 MB browser download. That works exactly until someone moves or
// deletes that project, and then every marketing tool breaks with a module-not-found error that
// says nothing about marketing. Here it is a declared dependency; the browser binaries themselves
// are shared out of the user-level Playwright cache anyway, so the disk cost is not paid twice.

import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const require = createRequire(join(here, '..', 'package.json'));

/**
 * ⚠️ `require('playwright')`, NOT `import()` of the resolved file path.
 * Playwright ships CommonJS. Importing its entry file by URL bypasses the package's own export
 * map, and Node's named-export detection then comes back with `chromium === undefined` — which
 * surfaces one call later as "Cannot read properties of undefined (reading 'launch')" and looks
 * like a browser problem rather than a module-resolution one. Going through `require` keeps the
 * normal interop and hands back the real object.
 */
export async function chromium() {
  try {
    return require('playwright').chromium;
  } catch (e) {
    if (e && e.code === 'MODULE_NOT_FOUND') {
      throw new Error(
        'Playwright is not installed.\n' +
          '  cd monkify-meta/marketing/studio && npm install\n' +
          'If the browser download was skipped, also run:  npx playwright install chromium',
      );
    }
    throw e;
  }
}

/** Launch flags shared by every tool here. */
export const LAUNCH_ARGS = [
  // Without these Chrome throttles animation frames whenever the window is not focused, and a
  // recording made in the background comes out stuttering rather than smooth.
  '--disable-backgrounding-occluded-windows',
  '--disable-renderer-backgrounding',
  '--disable-background-timer-throttling',
  '--autoplay-policy=no-user-gesture-required',
  // Pixels must be pixels: at any other device scale factor a 1080-wide board is captured at a
  // different size and every measurement taken afterwards is measured against the wrong object.
  '--force-device-scale-factor=1',
];

/**
 * Turn off the preview transform and wait for the fonts.
 * Both halves matter. The transform makes the piece fit a laptop screen and would otherwise be
 * baked into the capture at ~94% of nominal size; the font wait stops the first frame being drawn
 * in the fallback face, which has different metrics and moves every hand-written line break.
 */
export async function settle(page) {
  await page.evaluate(() => document.fonts.ready);
  await page.evaluate(() => {
    const s = document.querySelector('.stage');
    if (s) { s.classList.add('no-scale'); s.style.transform = 'none'; s.style.height = 'auto'; }
  });
  await page.waitForTimeout(250);
}

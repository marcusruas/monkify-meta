// Screenshots the REAL app UI into a campaign, so a piece can show the product instead of
// describing it.
//
//   node tools/capture-ui.mjs <campaign-slug>
//
// Reads `campaigns/<slug>/capture.mjs` and writes PNGs to `campaigns/<slug>/ui/`, which
// `tools/build.mjs` adds as an art root — so a shot is referenced from content.mjs exactly like
// any other asset, by filename.
//
// ── WHY IT MOCKS THE API ──────────────────────────────────────────────────────
// A local dev database is not a demo. It holds whatever two accounts happened to bet during
// testing, in whatever week that was, so the board renders half-empty behind a "no rounds this
// week — showing the most recent week that had results" banner. That is honest and it is also the
// worst possible frame for a launch video.
//
// So a shot declares the exact response it wants. Playwright intercepts the endpoint and answers
// with it, the real app renders it with the real components, and what comes out is genuinely our
// UI — the layout, the type, the plinths, the colour — carrying stand-in numbers.
//
// ⚠️ THAT MAKES EVERY SHOT A MOCK, AND IT MUST BE TREATED AS ONE. The names and figures are
// invented. Never caption a shot as live standings, never quote a number off it, and never let one
// imply a real person is winning. `references/compliance.md` §3 forbids fabricated metrics; this
// tool is for showing the INTERFACE, and the campaign's copy has to carry that distinction.
// Mocking here was the owner's explicit call for the leaderboard launch video.
//
// ── WHAT IT NEEDS RUNNING ─────────────────────────────────────────────────────
// The app on `http://localhost:8080` (`cd monkify-front && yarn serve`), pointed at a local API.
// ⚠️ `yarn serve`, not the :6003 container — that one builds in production mode and resolves to
// mainnet, which after the SOL cutover is real money.

import { existsSync, mkdirSync, rmSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

import { LAUNCH_ARGS, chromium } from './browser.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const slug = process.argv[2];
if (!slug) throw new Error('usage: node tools/capture-ui.mjs <campaign-slug>');

const DIR = join(here, '..', 'campaigns', slug);
const OUT = join(DIR, 'ui');
const CONFIG = join(DIR, 'capture.mjs');
if (!existsSync(CONFIG)) throw new Error(`no capture config at ${CONFIG}`);

const { shots, base = 'http://localhost:8080' } = await import(pathToFileURL(CONFIG).href);

async function main() {
  // Fail early and clearly: a dev server that is not up otherwise shows as a screenshot of
  // Chrome's error page, which looks like a styling bug rather than a missing server.
  try {
    const probe = await fetch(base, { signal: AbortSignal.timeout(8000) });
    if (!probe.ok) throw new Error(String(probe.status));
  } catch (e) {
    throw new Error(
      `the app is not answering at ${base} (${e.message}).\n` +
        'Start it first:  cd monkify-front && yarn serve --port 8080\n' +
        '⚠️ Use the dev server, not the :6003 container — that one builds production/mainnet.',
    );
  }

  rmSync(OUT, { recursive: true, force: true });
  mkdirSync(OUT, { recursive: true });

  const launch = await chromium();
  const browser = await launch.launch({ args: LAUNCH_ARGS });

  for (const shot of shots) {
    /* deviceScaleFactor 2 by default. A shot is composited into a 1920-wide board and often shown
       larger than it was captured, so capturing at 1x means the app's own pixel type gets resampled
       and goes soft — the one thing a screenshot of a pixel-art UI must not do. */
    const context = await browser.newContext({
      viewport: { width: shot.width ?? 1440, height: shot.height ?? 900 },
      deviceScaleFactor: shot.scale ?? 2,
    });
    /* Pre-accept the overlays a first-time visitor gets, or every shot is a photograph of the
       18+/Terms gate instead of the page. It is a COOKIE (`ss_terms`), not localStorage, because
       the LP and the app are different origins on one registrable domain — see
       monkify-front/src/utils/legalGate.ts. The cookie-consent banner is gated behind the same
       acceptance, so it is set here too. */
    const cookies = Object.entries(shot.cookies ?? { ss_terms: 'v1', ss_consent: 'denied' }).map(
      ([name, value]) => ({ name, value, url: base }),
    );
    if (cookies.length) await context.addCookies(cookies);

    const page = await context.newPage();

    /* THE MOCK. Matched on the path so it catches the query string the hook appends
       (`?parametersId=&wallet=&size=`), and returned inside the `ApiResult` envelope the whole API
       uses — `{ data, messages }` — because the hook unwraps `.data`. */
    if (shot.mock) {
      for (const [pattern, body] of Object.entries(shot.mock)) {
        await page.route(pattern, (route) =>
          route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({ data: body, messages: [] }),
          }),
        );
      }
    }

    await page.goto(base + shot.path, { waitUntil: 'networkidle' });
    await page.evaluate(() => document.fonts.ready);
    if (shot.waitFor) await page.waitForSelector(shot.waitFor, { timeout: 20000 });
    // The app animates its starfield and its panels in. Photograph before that settles and a
    // panel is caught mid-fade.
    await page.waitForTimeout(shot.settle ?? 1600);

    if (shot.hide?.length) {
      await page.evaluate((sels) => {
        for (const s of sels) document.querySelectorAll(s).forEach((el) => { el.style.visibility = 'hidden'; });
      }, shot.hide);
    }

    const target = shot.selector ? page.locator(shot.selector).first() : page;
    const file = join(OUT, `${shot.id}.png`);
    await target.screenshot({ path: file, ...(shot.selector ? {} : { fullPage: false }) });

    const kb = Math.round(statSync(file).size / 1024);
    console.log(`${(shot.id + '.png').padEnd(30)} ${shot.selector ?? 'viewport'}  ${kb} KB`);
    await context.close();
  }

  await browser.close();
  console.log(`\n${shots.length} shots in ${OUT}`);
  console.log('⚠️ These are MOCKED renders of the real UI. Never caption them as live standings.');
}

main().catch((e) => {
  console.error('\nfailed:', e.message);
  process.exit(1);
});

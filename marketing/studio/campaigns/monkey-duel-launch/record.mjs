// MONKEY DUEL LAUNCH — the promo, filmed off the LIVE duel screen with every byte of data mocked.
//
//   cd monkify-front && yarn serve --port 8080      (devnet dev server; NOT the :6003 container)
//   node campaigns/monkey-duel-launch/record.mjs
//
// Output: out/monkey-duel-launch/x-01-monkey-duel.mp4 (1920x1080, 30fps, H.264 + AAC)
//
// ─── WHY THIS, AND NOT THE PRACTICE ROUND ─────────────────────────────────────
// Owner direction (2026-09-25): "use mocks to perform the actions on the screen, shouldn't look like
// a practice round", and "more dynamic and fluid than the hyper roll one, which was just a recording
// of clicking". The practice duel also has no OPEN DUELS board, which is half of what is new.
// So this drives the REAL `/game/play?type=<duel>` page, and answers every request it makes:
//   · REST — page.route on the API origin. Game types are the local API's own list with the duel
//     entry pinned (see DUEL_TYPE); the board, the round, the bet and the balance are fixed payloads.
//   · SignalR — page.routeWebSocket plays the hub: handshake, invocation completions, then a
//     scripted round (splash → race → win) as the same frames the backend sends.
// No backend is needed for any of it, and no wallet ever signs: the lever takes the app's own
// dev free-bet path (checkbox hidden), and the round the mock returns carries the 0.05 stake.
//
// ⚠️ A MOCK. Every figure on screen is a stand-in: the 0.05 stake, the +0.098, the board's rooms and
// their wallets. Nobody placed, won or was paid any of it. The caption must never call it a real
// round or a real win (references/compliance.md §2–3), and the win scene says EXAMPLE on it.
// ⚠️ The live screen's SERVER SEED line is HIDDEN, not faked: fairness evidence is never invented.
//
// ─── HOW THE PICTURE IS MADE ──────────────────────────────────────────────────
// CDP screencast at deviceScaleFactor 2 (3840x2160 JPEG), downscaled at the end — the technique
// hyper-roll-win proved on this machine. What is new is overlay.js: the camera is a CSS transform
// on the app root (zooms are re-rasterised, never upscaled video), and captions, spotlights, the
// cursor and the title cards are drawn by a layer the camera does not scale.
//
// ─── SOUND ────────────────────────────────────────────────────────────────────
// ONE bed, low and constant, no effects on top (owner, 2026-09-25: "music kinda low and constant,
// no changes in it"). The app's own cues are NOT replayed — a victory sting is exactly the change
// that direction rules out. game_running_1.mp3 loops seamlessly (the app loops it in-round).

import { execFileSync, spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { LAUNCH_ARGS, chromium } from '../../tools/browser.mjs';
import { ROOT } from '../../tools/assets.mjs';
import { video as V } from './content.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const OUT = join(here, '..', '..', 'out', 'monkey-duel-launch');
const FRAMES = join(OUT, '.frames');
const BASE = process.env.APP_URL ?? 'http://localhost:8080';
const API = process.env.API_BASE ?? 'http://localhost:6001';
const W = 1920;
const H = 1080;
const DSF = Number(process.env.DSF ?? 2);
const FPS = 30;

/* Stand-ins. The wallet is base58-shaped and nobody's; it never signs and nothing real sees it. */
const WALLET = '8kQ2rXsPmVhT4dYzB6nLcW9fJgH3uRaEoKpN5vZtQwMx';
const ROUND = '7d3e1c52-9a0b-4f6e-8c21-5b9d0e4a7f13';
const MY_WORD = V.words.you;
const RIVAL_WORD = V.words.rival;
const STAKE = V.stake;
const RATE = 2; // the live commissionPercentage (features/monkey-duel.md: "the live 2%")
const PAYOUT = Math.round(STAKE * (2 - RATE / 100) * 1e9) / 1e9; // S × (2 − rate), the shipped formula

/* The OPEN DUELS board. Wallets only — no display names: "a display name is a person"
   (compliance §5), and a mocked board must not put a real player's name in an ad. */
const ROOMS = [
  ['cosmos', 0.05, 'Fq7pX2mZ9rLkB4vN8sTe3HwYc6JdGu1aRiKoP5nQxVb'],
  ['meteor', 0.1, '3HzJ8uWq5nKcV2rTe7LmYp9BxFs4DgNa6RiOk1QwEvC'],
  ['banana', 0.02, 'Bn5vQx1LmP8zTr3KcW7eYu2HdJs9GfNa4RiXo6MwEkZ'],
  ['saturn', 0.25, 'Mw2kE9xRp4LzVn7TcQ5bYu8HdJs3GfNa1RiKo6PqXvB'],
  ['planet', 0.01, 'Xv6QpN3zLm8KcT1rWe5YuB9HdJs2GfNa7RiOk4MwEqF'],
  ['launch', 0.005, 'Kc4TrW8eYu1HdJs6GfNa3RiOk9MwEqFzLm2XvQpN5Bx'],
].map(([word, stake, wallet], i) => ({
  gameId: `0e8c6b1a-3f2d-4c5e-9a7b-00000000000${i}`,
  word,
  stake,
  displayName: null,
  wallet,
  createdDate: new Date(Date.now() - (i + 1) * 97000).toISOString(),
}));

// ─── helpers ──────────────────────────────────────────────────────────────────
const ok = (data) => ({
  status: 200,
  contentType: 'application/json',
  headers: {
    'Access-Control-Allow-Origin': BASE,
    'Access-Control-Allow-Credentials': 'true',
  },
  body: JSON.stringify({ data, messages: [] }),
});
const RS = '\x1e';

/** Seeded, so two renders of the same cut type the same stream. */
function rng(seed) {
  let s = seed >>> 0;
  return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 2 ** 32);
}

/* The race stream: noise, with the two words surfacing in part (the near-miss row counts them
   down) and the player's word completing at the very end. Batches of ~9 chars every 100ms. */
function raceBatches() {
  const r = rng(184);
  const noise = (n) => Array.from({ length: n }, () => 'abcdefghijklmnopqrstuvwxyz'[Math.floor(r() * 26)]).join('');
  // Never let noise complete either word by accident.
  const clean = (s) => s.replaceAll(MY_WORD, MY_WORD.slice(0, 3) + 'q').replaceAll(RIVAL_WORD, RIVAL_WORD.slice(0, 3) + 'q');
  const stream = clean(
    noise(70) + MY_WORD.slice(0, 3) + noise(60) + RIVAL_WORD.slice(0, 4) + noise(55) +
      MY_WORD.slice(0, 4) + noise(50) + RIVAL_WORD.slice(0, 5) + noise(40) + MY_WORD.slice(0, 5) + noise(30),
  ) + MY_WORD;
  const out = [];
  for (let i = 0; i < stream.length; ) {
    const n = 7 + Math.floor(r() * 5);
    out.push(stream.slice(i, i + n));
    i += n;
  }
  return out;
}

function concatList(shots, endMs) {
  let text = '';
  for (let i = 0; i < shots.length; i++) {
    const next = i + 1 < shots.length ? shots[i + 1].t : endMs;
    text += `file '${shots[i].file.replace(/\\/g, '/')}'\nduration ${((next - shots[i].t) / 1000).toFixed(4)}\n`;
  }
  return text + `file '${shots[shots.length - 1].file.replace(/\\/g, '/')}'\n`;
}

const dataUri = (file, mime) => `data:${mime};base64,${readFileSync(file).toString('base64')}`;

// ─── main ─────────────────────────────────────────────────────────────────────
async function main() {
  try {
    const probe = await fetch(BASE, { signal: AbortSignal.timeout(8000) });
    if (!probe.ok) throw new Error(String(probe.status));
  } catch (e) {
    throw new Error(`the app is not answering at ${BASE} (${e.message}).\nStart it: cd monkify-front && yarn serve --port 8080`);
  }

  // The duel type, from the local API's own list so the lobby shows the real line-up, with the duel
  // entry pinned to what the video needs. Falls back to a stand-alone entry if the API is down.
  let types = [];
  try {
    types = (await (await fetch(`${API}/games/active-types`)).json()).data ?? [];
  } catch {}
  let duel = types.find((t) => t.isDuel);
  if (!duel) {
    duel = { gameTypeId: '2f69b277-ff1e-49c2-b3a4-17be977a5bf3', name: 'Monkey Duel',
      description: "Type a word and duel the house on the spot - or take a player's open duel instead.",
      gameCharacterType: 2, minimumNumberOfPlayers: 2, presetChoices: [], lotteryParameters: null,
      tokenSymbol: 'SOL', tokenDecimals: 9, tokenIsNative: true, tokenMintAddress: 'So11111111111111111111111111111111111111112',
      tokenReceiverPublicKey: '9DMtb5k18HpcLnXRLjfLtP3N2Ew6oF78mDLVuz8A7jmR', isHouseEdge: false, underMaintenance: false };
    types.push(duel);
  }
  Object.assign(duel, {
    isDuel: true, comingSoon: false, underMaintenance: false, allowFreeBet: true, allowOrbit: false,
    requiredAmount: 0.002, maximumStake: 0.5, commissionPercentage: RATE, choiceRequiredLength: 6,
  });
  const TYPE = duel.gameTypeId;

  rmSync(FRAMES, { recursive: true, force: true });
  mkdirSync(FRAMES, { recursive: true });

  const pw = await chromium();
  /* ⚠️ THE GPU FLAGS ARE WHAT MAKE THIS FLUID. Headless Chrome defaults to SwiftShader (software GL)
     here, and the screencast came out at ~17 fps: every camera move stepped. On the RTX it runs ~57 fps
     (measured 2026-09-26, same page, same move). If a render comes out choppy, check the renderer. */
  const browser = await pw.launch({ args: [...LAUNCH_ARGS, '--enable-gpu', '--ignore-gpu-blocklist', '--use-angle=d3d11'] });
  const context = await browser.newContext({ viewport: { width: W, height: H }, deviceScaleFactor: DSF });
  await context.addCookies([['ss_terms', 'v1'], ['ss_consent', 'denied']].map(([name, value]) => ({ name, value, url: BASE })));
  await context.addInitScript(({ wallet }) => {
    localStorage.setItem('swMobileWalletSession', JSON.stringify({ walletPublicKey: wallet, createdAt: Date.now() }));
  }, { wallet: WALLET });
  await context.addInitScript({ path: join(here, 'overlay.js') });
  await context.addInitScript(() => {
    // The live screen's SERVER SEED line: hidden, never faked (see header).
    const hide = () => {
      for (const p of document.querySelectorAll('p')) {
        if (p.textContent.trim().startsWith('> SERVER SEED') && p.style.display !== 'none') p.style.display = 'none';
      }
    };
    new MutationObserver(hide).observe(document, { subtree: true, childList: true, characterData: true });

    /* The chat panel: empty in a mock, and inventing chatter would be fabricated social proof. Kept
       hidden on a timer rather than once, because it mounts AFTER the machine does — a one-shot hide
       missed it and it showed in the full-frame reveal. Found by its own copy, not by a class. */
    setInterval(() => {
      const line = [...document.querySelectorAll('p, span, div')].find(
        (n) => n.children.length === 0 && /comms open/i.test(n.textContent),
      );
      let n = line;
      while (n && n.parentElement && n.getBoundingClientRect().height < 400) n = n.parentElement;
      if (n && n !== document.body && n.style.visibility !== 'hidden') n.style.visibility = 'hidden';
    }, 100);
  });

  const page = await context.newPage();
  globalThis.__page = page;
  page.on('pageerror', (e) => console.log('  [page error]', e.message));

  // ── REST ──
  let placed = false;
  const round = () => ({
    gameId: ROUND, gameTypeId: TYPE, status: 1, duelKind: 1, stake: STAKE, isFull: true,
    houseWord: RIVAL_WORD, ownerWallet: WALLET, serverSeedHash: null, bets: [],
  });
  await page.route(`${API}/**`, async (route) => {
    const req = route.request();
    const url = new URL(req.url());
    const p = url.pathname;
    const m = req.method();
    if (m === 'OPTIONS') {
      return route.fulfill({ status: 204, headers: {
        'Access-Control-Allow-Origin': BASE, 'Access-Control-Allow-Credentials': 'true',
        'Access-Control-Allow-Methods': 'GET,POST,PATCH,OPTIONS', 'Access-Control-Allow-Headers': '*' } });
    }
    if (p === '/Hubs/Games/negotiate') {
      return route.fulfill({ status: 200, contentType: 'application/json',
        headers: { 'Access-Control-Allow-Origin': BASE, 'Access-Control-Allow-Credentials': 'true' },
        body: JSON.stringify({ negotiateVersion: 1, connectionId: 'c1', connectionToken: 't1',
          availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] }) });
    }
    if (p === '/games/active-types') return route.fulfill(ok(types));
    if (p.startsWith('/players/preferences')) {
      return route.fulfill(ok({ externalPlayerId: WALLET, displayName: 'spaceape', selfExcluded: false, selfExcludedUntil: null }));
    }
    if (p.startsWith('/solana/balance')) {
      return route.fulfill(ok({ wallet: WALLET, mint: duel.tokenMintAddress, uiAmount: 1.37, rawAmount: '1370000000', decimals: 9 }));
    }
    if (p === `/games/${TYPE}/duels` && m === 'GET') {
      return route.fulfill(ok({ items: ROOMS, currentPage: 1, totalNumberOfRecords: ROOMS.length, totalNumberOfPages: 1 }));
    }
    if (p === `/games/${TYPE}/duels` && m === 'POST') return route.fulfill(ok(round()));
    if (p === `/games/${TYPE}/availability`) return route.fulfill(ok({ available: true, maxStake: 0.5 }));
    if (p === `/games/${ROUND}/bets`) { placed = true; return route.fulfill(ok(true)); }
    if (p === `/games/${TYPE}` && m === 'GET') return route.fulfill(ok(placed ? round() : null));
    if (p === `/games/${TYPE}/chat`) return route.fulfill(ok([]));
    // 200 with no seed: the reveal stays blank (and its line is hidden). A 404 raised an error toast.
    if (p === `/games/${ROUND}/verification`) return route.fulfill(ok({ gameId: ROUND, status: 5, serverSeed: null }));
    // Everything else (lobby stats, history, leaderboard...) is read-only: let the local API answer.
    return route.continue().catch(() => route.fulfill(ok(null)));
  });

  // ── SignalR ──
  let hub = null;
  let joinedChat = null;
  const joined = new Promise((res) => (joinedChat = res));
  await page.routeWebSocket(/\/Hubs\/Games/, (ws) => {
    hub = ws;
    ws.onMessage((raw) => {
      for (const frame of String(raw).split(RS).filter(Boolean)) {
        const msg = JSON.parse(frame);
        if (msg.protocol) { ws.send('{}' + RS); continue; }
        if (msg.type === 1 && msg.invocationId !== undefined) {
          ws.send(JSON.stringify({ type: 3, invocationId: msg.invocationId, result: null }) + RS);
          if (msg.target === 'JoinChat' && placed) joinedChat();
        }
      }
    });
    const ping = setInterval(() => { try { ws.send(JSON.stringify({ type: 6 }) + RS); } catch { clearInterval(ping); } }, 10000);
  });
  const emit = (target, arg) =>
    hub.send(JSON.stringify({ type: 1, target, arguments: [typeof arg === 'string' ? arg : JSON.stringify(arg)] }) + RS);

  // ── Stage the lobby (off camera) ──
  await page.goto(`${BASE}/`, { waitUntil: 'networkidle' });
  await page.evaluate(() => document.fonts.ready);
  await page.addStyleTag({ content: `
    .duel-freebet, .how-to-play { display: none !important; }
    /* Toasts: the app confirms the (mock) win in one — the banner already says it, twice is noise. */
    .Toastify, .Toastify__toast-container { display: none !important; }
    /* The Nuxt/Vue dev overlay, if a warning ever raises one mid-take. */
    #webpack-dev-server-client-overlay { display: none !important; }
  ` });
  const card = '.pixelated-card.accent-monkey-duel';
  await page.waitForSelector(card, { timeout: 20000 });
  await page.evaluate(() => window.__promo.mount());
  await page.evaluate((c) => window.__promo.cam(`${c} .art`, { pad: 10, ms: 0, max: 5 }), card);
  await page.waitForTimeout(600);
  await page.mouse.move(W * 0.72, H * 0.62);

  // Art for the cards, inlined (the page is http://, the files are on disk).
  const coin = dataUri(join(ROOT, 'monkify-front/src/assets/game/duel/monk-coin-spin.gif'), 'image/gif');
  const ship = dataUri(join(ROOT, 'monkify-lp/src/assets/edson-ship-typing.gif'), 'image/gif');

  // ── Recording ──
  const cdp = await context.newCDPSession(page);
  const frames = [];
  cdp.on('Page.screencastFrame', async ({ data, metadata, sessionId }) => {
    const file = join(FRAMES, `f${String(frames.length).padStart(5, '0')}.jpg`);
    writeFileSync(file, Buffer.from(data, 'base64'));
    frames.push({ file, t: metadata.timestamp * 1000 });
    try { await cdp.send('Page.screencastFrameAck', { sessionId }); } catch {}
  });
  const startedAt = Date.now();
  await cdp.send('Page.startScreencast', { format: 'jpeg', quality: 90, maxWidth: W, maxHeight: H, everyNthFrame: 1 });
  let slip = 0; // see the premise beat
  const at = (ms) => page.waitForTimeout(Math.max(0, startedAt + ms + slip - Date.now()));
  const P = (fn, arg) => page.evaluate(fn, arg);
  const marks = {};
  const mark = (k) => (marks[k] = ((Date.now() - startedAt) / 1000).toFixed(2));

  let mouse = { x: W * 0.72, y: H * 0.62 };
  const ease = (t) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);
  async function glide(to, ms) {
    const from = { ...mouse };
    const begin = Date.now();
    for (;;) {
      const k = ease(Math.min(1, (Date.now() - begin) / ms));
      await page.mouse.move(from.x + (to.x - from.x) * k, from.y + (to.y - from.y) * k);
      if (k >= 1) break;
      await page.waitForTimeout(12);
    }
    mouse = { ...to };
  }
  const centre = (sel) => P((s) => window.__promo.screenCenter(s), sel);
  async function clickAt(sel, ms = 520) {
    await glide(await centre(sel), ms);
    await page.waitForTimeout(90);
    await page.mouse.down();
    await page.waitForTimeout(110);
    await page.mouse.up();
  }

  // 1 · HOOK — the lobby card, tight, and the name slammed over it.
  mark('hook');
  await P(() => window.__promo.cam('.pixelated-card.accent-monkey-duel .art', { pad: 40, ms: 2600, max: 5, ease: 'cubic-bezier(.2,.7,.2,1)' }));
  await P((html) => window.__promo.card('intro', html), `
    <div class="kick">${V.intro.kick}</div>
    <div class="title"><span class="red" style="animation-delay:260ms">MONKEY</span><span class="blue" style="animation-delay:420ms">DUEL</span></div>
    <div class="line" style="animation-delay:800ms">${V.intro.line}</div>`);
  await at(2500);
  await P(() => window.__promo.uncard('intro'));

  // 2 · INTO THE MACHINE — the whole card, the cursor presses START, bars cover the route change.
  mark('enter');
  await P(() => window.__promo.cam('.pixelated-card.accent-monkey-duel', { pad: 60, ms: 700, max: 3 }));
  await at(3000);
  await P(() => window.__promo.cursor(true));
  await clickAt('.pixelated-card.accent-monkey-duel .card-content', 600);
  await P(() => window.__promo.wipe('in'));
  mark('wipe');
  await page.waitForSelector('.duel-card', { timeout: 20000 });
  await page.waitForSelector('input[aria-label="Your word"]', { timeout: 20000 });
  mark('routed');
  // The chat panel on the right: empty in a mock, and inventing chatter would be fabricated social
  // proof. Removed from the frame.
  await P(() => {
    // Found by its own copy, not a class: climb from the "comms open" line to the panel box.
    const line = [...document.querySelectorAll('p, span, div')].find((n) => /comms open/i.test(n.textContent) && n.children.length === 0);
    let n = line;
    while (n && n.parentElement && n.getBoundingClientRect().height < 400) n = n.parentElement;
    if (n && n !== document.body) n.style.visibility = 'hidden';
    else console.warn('promo: chat panel not found');
  });
  // The free-bet path (checkbox hidden): the lever posts without a signature. See header.
  await P(() => document.querySelector('.duel-freebet input')?.click());
  await P(() => window.__promo.cursor(false));
  /* ⚠️ Wait for the layout to STOP MOVING before framing anything. The tube runs its own entrance
     for ~1.5s after mount; framing the corners mid-entrance put the camera on the control plate in
     one take out of two. So the bars open at once and the machine powers up full-frame ON CAMERA —
     the entrance is the app's own motion, which beats 1.5s of solid bars — and only then the push-in. */
  await P(() => window.__promo.cam(null, { ms: 0 }));
  await page.waitForTimeout(80);
  await P(() => window.__promo.wipe('out'));
  await P(async () => {
    const box = () => JSON.stringify(document.querySelector('.duel-card__duo').getBoundingClientRect());
    let last = '', same = 0;
    for (let i = 0; i < 40 && same < 3; i++) {
      await new Promise((r) => setTimeout(r, 120));
      const b = box();
      same = b === last ? same + 1 : 0;
      last = b;
    }
  });
  // Every beat after this one is scheduled from here: how long the entrance takes varies by take.
  slip = Math.max(0, Date.now() - startedAt - 4400);

  // 3 · THE PREMISE — the two corners, face to face.
  mark('premise');
  await P(() => window.__promo.cam(['.duel-card__duo', '.duel-card__foot'], { pad: 50, ms: 1300, max: 3, room: 260, ease: 'cubic-bezier(.3,0,.1,1)' }));
  await page.waitForTimeout(250);
  await P((c) => window.__promo.caption(c), V.captions.premise);
  await at(7300);

  // 4 · VS HOUSE — the default tab, nobody to wait for.
  mark('house');
  await P(() => window.__promo.caption(null));
  await P(() => window.__promo.cam(['#terminal-form [role="tab"]', '.duel-card'], { pad: 40, ms: 900, max: 2.2, room: 240 }));
  await page.waitForTimeout(500);
  await P(() => window.__promo.spotlight('#terminal-form [role="tab"]', { label: 'VS HOUSE', side: 'below', color: '#4ade80', pad: 10 }));
  await P((c) => window.__promo.caption(c), V.captions.house);
  await at(10200);

  // 5 · OPEN DUELS — the board, and a cursor walking it.
  mark('board');
  await P(() => window.__promo.spotlight(null));
  await P(() => window.__promo.caption(null));
  await P(() => window.__promo.cam('#terminal-form', { pad: 20, ms: 600, max: 1.6 }));
  // Measure only once the camera has landed: mid-move, the tab is somewhere else on screen.
  await page.waitForTimeout(650);
  await P(() => window.__promo.cursor(true));
  const openTab = page.locator('[role="tab"]', { hasText: 'OPEN DUELS' });
  await glide(await openTab.evaluate((n) => { const r = n.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; }), 600);
  await page.mouse.down(); await page.waitForTimeout(100); await page.mouse.up();
  await P(() => window.__promo.flash());
  await page.waitForSelector('.duel-room', { timeout: 8000 });
  await P(() => {
    // The rooms cascade in: a stagger on the app's own cards.
    const s = document.createElement('style');
    s.textContent = `@keyframes promo-room { from { opacity: 0; transform: translateY(30px) scale(.92); } }
      .duel-board .duel-room { animation: promo-room 420ms cubic-bezier(.2,1.3,.4,1) backwards; }` +
      [...Array(10)].map((_, i) => `.duel-board .duel-room:nth-child(${i + 1}) { animation-delay: ${i * 80}ms; }`).join('');
    document.head.appendChild(s);
  });
  await P(() => window.__promo.cursor(false));
  await P(() => window.__promo.cam('.duel-board', { pad: 30, ms: 900, max: 2.4, room: 250, whip: true }));
  await P((c) => window.__promo.caption(c), V.captions.board);
  await page.waitForTimeout(1100);
  await page.locator('.duel-board').focus();
  for (let i = 0; i < 3; i++) {
    await page.keyboard.press('ArrowDown');
    await page.waitForTimeout(340);
  }
  await P(() => window.__promo.spotlight('.duel-room--cursor', { label: 'TAKE IT', side: 'below', color: '#3d8cff', pad: 8 }));
  await at(15600);

  // 6 · YOUR WORD — back on VS HOUSE, a macro on the word LCD while it is typed.
  mark('word');
  await P(() => window.__promo.spotlight(null));
  await P(() => window.__promo.caption(null));
  await page.locator('[role="tab"]', { hasText: 'VS HOUSE' }).evaluate((n) => n.click()); // off camera: a DOM click
  await P(() => window.__promo.flash());
  await P(() => window.__promo.cam('input[aria-label="Your word"]', { pad: 60, ms: 800, max: 3.4, room: 250, whip: true }));
  await page.waitForTimeout(700);
  await P((c) => window.__promo.caption(c), V.captions.word);
  await page.locator('input[aria-label="Your word"]').pressSequentially(MY_WORD, { delay: 170 });
  await page.waitForTimeout(300);
  // Pull up to the corner, where the word now sits under YOU.
  await P(() => window.__promo.cam('.duel-card__side--you', { pad: 40, ms: 700, max: 3, room: 250 }));
  await at(19400);

  // 7 · THE STAKE.
  mark('stake');
  await P(() => window.__promo.caption(null));
  await P(() => window.__promo.cam(['input[aria-label="Stake in SOL"]', 'button[aria-label="Raise the stake by 0.001 SOL"]', 'button[aria-label="Lower the stake by 0.001 SOL"]'], { pad: 40, ms: 700, max: 3, room: 250 }));
  await page.waitForTimeout(600);
  await P((c) => window.__promo.caption(c), V.captions.stake);
  const stake = page.locator('input[aria-label="Stake in SOL"]');
  await stake.evaluate((n) => { n.focus(); n.select(); });
  await stake.pressSequentially((STAKE - 0.003).toFixed(3), { delay: 150 });
  await stake.press('Enter');
  await page.waitForTimeout(250);
  await P(() => window.__promo.cursor(true));
  for (let i = 0; i < 3; i++) await clickAt('button[aria-label="Raise the stake by 0.001 SOL"]', i ? 160 : 450);
  await page.waitForTimeout(200);
  await P(() => window.__promo.spotlight('input[aria-label="Stake in SOL"]', { pad: 14 }));
  await at(22800);

  // 8 · PULL — lever, shake, flash, and straight into the VS splash.
  mark('pull');
  await P(() => window.__promo.spotlight(null));
  await P(() => window.__promo.caption(null));
  await P(() => window.__promo.cam('img.duel-lever-img', { pad: 110, ms: 600, max: 2.6 }));
  await page.waitForTimeout(500);
  await P((c) => window.__promo.caption(c), V.captions.pull);
  await clickAt('img.duel-lever-img', 380);
  await P(() => { window.__promo.shake(); window.__promo.flash(); });
  await P(() => window.__promo.cursor(false));
  await Promise.race([joined, page.waitForTimeout(6000)]);
  await page.waitForTimeout(250);
  emit(`${ROUND}/Bets`, { amount: STAKE, choice: MY_WORD, externalPlayerId: WALLET, displayName: null });
  const startIso = new Date(Date.now() + 3200).toISOString();
  emit(`${ROUND}/Status`, { status: 3, startDate: startIso });
  await page.waitForSelector('.duel-vs', { timeout: 8000 });
  mark('splash');
  await P(() => window.__promo.caption(null));
  await P(() => window.__promo.cam('.duel-vs', { pad: 40, ms: 500, max: 2.4, room: 250, whip: true }));
  await page.waitForTimeout(400);
  await P((c) => window.__promo.caption(c), V.captions.splash);
  await page.waitForTimeout(2900);

  // 9 · THE RACE — Edson types; both words surface; yours lands.
  mark('race');
  emit(`${ROUND}/Status`, { status: 4, startDate: startIso });
  emit(`${ROUND}/DisplaySpeed`, { typingSpeed: 2 });
  await P(() => window.__promo.caption(null));
  await P(() => window.__promo.flash());
  await P(() => window.__promo.cam(window.__promo.tube(300), { pad: 10, ms: 700, max: 2.2, room: 240 }));
  await page.waitForTimeout(500);
  await P((c) => window.__promo.caption(c), V.captions.race);
  const batches = raceBatches();
  for (const b of batches) {
    emit(`${ROUND}/Terminal`, b);
    await page.waitForTimeout(95);
  }
  await page.waitForTimeout(400);
  emit(`${ROUND}/Status`, { status: 5, startDate: startIso, result: { winners: [WALLET], numberOfWinners: 1, firstChoiceTyped: MY_WORD } });
  await page.waitForTimeout(700);
  emit(`${ROUND}/Status`, { status: 7, startDate: startIso,
    result: { winners: [WALLET], numberOfWinners: 1, firstChoiceTyped: MY_WORD, netRewardPerWinner: PAYOUT } });

  // 10 · THE WIN — punch in on the banner the moment it lands.
  await page.waitForSelector('.duel-fx--win', { timeout: 8000 });
  mark('win');
  await P(() => window.__promo.caption(null));
  await P(() => { window.__promo.shake(); window.__promo.flash(); });
  await P(() => window.__promo.cam(window.__promo.tube(520), { pad: 10, ms: 450, max: 1.9, room: 230, ease: 'cubic-bezier(.2,.9,.2,1)' }));
  await page.waitForTimeout(300);
  await P((c) => window.__promo.caption(c), V.captions.win);
  await page.waitForTimeout(3000);

  // 11 · END CARD.
  mark('end');
  await P(() => window.__promo.caption(null));
  await P((a) => window.__promo.card('end', `
    <img class="coin" src="${a.coin}" alt="">
    <div class="kick" style="background:#4ade80">${a.kick}</div>
    <div class="title"><span class="red" style="animation-delay:160ms">MONKEY</span><span class="blue" style="animation-delay:300ms">DUEL</span></div>
    <div class="line" style="animation-delay:620ms">${a.line}</div>
    <div class="url" style="animation-delay:900ms">${a.url}</div>`,
  { solid: true, extra: `<img class="ship" src="${a.ship}" alt="">` }), { coin, ship, ...V.end });
  await page.waitForTimeout(V.endMs);

  await cdp.send('Page.stopScreencast');
  const stoppedAt = Date.now();
  await browser.close();
  console.log('beats (s):', JSON.stringify(marks));
  if (frames.length < 300) throw new Error(`only ${frames.length} frames captured`);

  // ── Assemble ──
  const t0 = frames[0].t;
  const clip = (stoppedAt - t0) / 1000;
  const list = join(FRAMES, 'list.txt');
  writeFileSync(list, concatList(frames, stoppedAt));
  const mp4 = join(OUT, `${V.id}.mp4`);
  const fadeOut = 1.6;
  const bed = join(ROOT, 'monkify-front/src/assets/audios', V.music);
  /* ONE FIXED GAIN, measured. ffmpeg's one-pass `loudnorm` is dynamic: it starts quiet and rides the
     gain up over ~2s, which sounded like a slow fade-in on a bed that is meant to be flat. So the
     looped bed is measured first and turned by a constant amount to `V.loudness`. */
  // ⚠️ ebur128 reports on STDERR, so this reads stderr (execFileSync would hand back empty stdout).
  const measured = spawnSync('ffmpeg', ['-hide_banner', '-nostats', '-stream_loop', '-1', '-i', bed, '-t', clip.toFixed(3),
    '-af', 'ebur128=framelog=quiet', '-f', 'null', '-'], { encoding: 'utf8' }).stderr ?? '';
  const iMatch = [...measured.matchAll(/I:\s+(-?[\d.]+) LUFS/g)].pop();
  if (!iMatch) throw new Error('could not measure the bed loudness');
  const gain = V.loudness - Number(iMatch[1]);
  console.log(`bed ${iMatch[1]} LUFS -> ${V.loudness} LUFS (gain ${gain.toFixed(1)} dB, constant)`);
  execFileSync('ffmpeg', [
    '-hide_banner', '-loglevel', 'error', '-y',
    '-f', 'concat', '-safe', '0', '-i', list,
    '-stream_loop', '-1', '-i', bed,
    '-filter_complex',
    `[0:v]fps=${FPS},scale=${W}:${H}:flags=lanczos,setsar=1,format=yuv420p[v];` +
      `[1:a]atrim=0:${clip.toFixed(3)},asetpts=PTS-STARTPTS,volume=${gain.toFixed(2)}dB,afade=t=in:d=0.3,` +
      `afade=t=out:st=${(clip - fadeOut).toFixed(3)}:d=${fadeOut},aresample=48000[a]`,
    '-map', '[v]', '-map', '[a]',
    '-c:v', 'libx264', '-preset', 'slow', '-crf', '17', '-profile:v', 'high', '-r', String(FPS),
    '-c:a', 'aac', '-b:a', '192k', '-movflags', '+faststart', '-t', clip.toFixed(3),
    mp4,
  ], { stdio: 'inherit' });
  rmSync(FRAMES, { recursive: true, force: true });
  console.log(`wrote ${mp4} (${clip.toFixed(1)}s, ${frames.length} frames)`);
}

main().then(() => process.exit(0)).catch(async (e) => {
  try { await globalThis.__page?.screenshot({ path: join(OUT, 'failed.png') }); console.error('screenshot: out/monkey-duel-launch/failed.png'); } catch {}
  console.error('\nfailed:', e.stack || e.message);
  process.exit(1);
});

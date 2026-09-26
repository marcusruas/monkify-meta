// The promo layer, injected into the LIVE duel page by record.mjs (addInitScript, so it survives
// the SPA's own route changes and is in place before the app mounts).
//
// Two halves, deliberately separate:
//   · THE CAMERA is a CSS transform on `#app`, the app's own root. Zooming the real DOM means text
//     and pixel art are re-rasterised at the zoom level — a 3x punch-in on the stake LCD is as
//     sharp as the page at 1x. A crop of a recording never is.
//   · THE GRAPHICS (captions, spotlight, callouts, title cards) live in `#promo`, a sibling of
//     `#app` on <body>, so the camera never scales them. They track the camera instead: the
//     spotlight re-measures its target every animation frame.
//
// Nothing here talks to the network or the app's state. It only draws.

(() => {
  if (window.__promo) return;

  const W = 1920;
  const H = 1080;
  const css = `
  #promo { position: fixed; inset: 0; z-index: 2147483000; pointer-events: none; overflow: hidden;
    font-family: 'Press Start 2P', monospace; color: #fff7ea; }
  #promo * { box-sizing: border-box; }
  html.promo-on #app { transform-origin: 0 0;
    transition: transform var(--cam-ms, 1100ms) var(--cam-ease, cubic-bezier(.65,0,.35,1)), filter 220ms; }
  html.promo-on #app.whip { filter: blur(7px) saturate(1.3); }
  html.promo-on, html.promo-on body { overflow: hidden !important; background: #07060d; }
  html.shake { animation: promo-shake 420ms steps(2) 1; }
  @keyframes promo-shake {
    0% { transform: translate(0,0) } 15% { transform: translate(-14px, 8px) } 30% { transform: translate(12px,-10px) }
    45% { transform: translate(-9px, -6px) } 60% { transform: translate(7px, 6px) } 80% { transform: translate(-3px,2px) } 100% { transform: none } }

  /* ── a permanent grade: vignette + faint scanlines, so the live page and the title cards read as
     one piece rather than a screen recording with stickers on it ── */
  #promo .grade { position: absolute; inset: 0;
    background:
      radial-gradient(ellipse at 50% 50%, transparent 55%, rgba(4,3,10,.55) 100%),
      repeating-linear-gradient(0deg, rgba(0,0,0,.10) 0 2px, transparent 2px 4px); }

  /* ── spotlight ── */
  #promo .spot { position: absolute; left: 0; top: 0; width: 10px; height: 10px; opacity: 0;
    box-shadow: 0 0 0 4000px rgba(5,4,12,.62); border-radius: 6px; transition: opacity 380ms; }
  #promo .spot.on { opacity: 1; }
  #promo .spot i { position: absolute; width: 26px; height: 26px; border: 0 solid var(--spot, #ffa214);
    filter: drop-shadow(0 0 8px var(--spot, #ffa214)); animation: promo-bracket 1.1s ease-in-out infinite; }
  #promo .spot i:nth-child(1) { left: -10px; top: -10px; border-left-width: 5px; border-top-width: 5px; }
  #promo .spot i:nth-child(2) { right: -10px; top: -10px; border-right-width: 5px; border-top-width: 5px; }
  #promo .spot i:nth-child(3) { left: -10px; bottom: -10px; border-left-width: 5px; border-bottom-width: 5px; }
  #promo .spot i:nth-child(4) { right: -10px; bottom: -10px; border-right-width: 5px; border-bottom-width: 5px; }
  @keyframes promo-bracket { 50% { transform: scale(1.18); } }
  #promo .spot .tag { position: absolute; white-space: nowrap; font-size: 22px; letter-spacing: 2px;
    padding: 12px 16px 10px; background: var(--spot, #ffa214); color: #0b0a14;
    box-shadow: 6px 6px 0 rgba(0,0,0,.55); transform: scale(0); transform-origin: var(--tag-origin, 0 100%);
    transition: transform 420ms cubic-bezier(.3,1.5,.5,1) 180ms; }
  #promo .spot.on .tag { transform: scale(1); }
  #promo .spot .tag.above { bottom: calc(100% + 26px); left: -10px; --tag-origin: 0 100%; }
  #promo .spot .tag.below { top: calc(100% + 26px); left: -10px; --tag-origin: 0 0; }
  #promo .spot .tag.right { left: calc(100% + 30px); top: 50%; translate: 0 -50%; --tag-origin: 0 50%; }
  #promo .spot .tag.left { right: calc(100% + 30px); top: 50%; translate: 0 -50%; --tag-origin: 100% 50%; }

  /* ── captions: one statement, slammed in word by word on a hard plate ── */
  #promo .cap { position: absolute; left: 50%; bottom: 76px; translate: -50% 0; text-align: center;
    display: flex; flex-direction: column; align-items: center; gap: 18px; width: max-content; max-width: 1760px; }
  #promo .cap.top { bottom: auto; top: 70px; }
  #promo .cap .kick { font-size: 20px; letter-spacing: 6px; color: #0b0a14; background: var(--accent, #ffa214);
    padding: 10px 14px 8px; clip-path: inset(0 100% 0 0); animation: promo-wipe 360ms steps(8) forwards; }
  #promo .cap .head { font-size: 58px; line-height: 1.25; letter-spacing: 1px; padding: 22px 34px 18px;
    background: rgba(8,7,18,.86); border: 4px solid rgba(255,247,234,.9);
    box-shadow: 10px 10px 0 rgba(0,0,0,.6), 0 0 60px -10px var(--accent, #ffa214);
    clip-path: inset(0 50% 0 50%); animation: promo-open 300ms cubic-bezier(.2,.9,.2,1) forwards; white-space: nowrap; }
  #promo .cap .head span { display: inline-block; opacity: 0; transform: translateY(26px) scale(1.5); filter: blur(8px);
    animation: promo-slam 380ms cubic-bezier(.2,1.4,.4,1) forwards; }
  #promo .cap .head em { font-style: normal; color: var(--accent, #ffa214); text-shadow: 0 0 22px var(--accent, #ffa214); }
  #promo .cap .sub { font-family: 'VT323', monospace; font-size: 40px; letter-spacing: 1px; color: #e9e3ff;
    background: rgba(8,7,18,.78); padding: 4px 16px; opacity: 0; animation: promo-fade 400ms ease-out forwards; }
  #promo .cap .fine { font-family: 'VT323', monospace; font-size: 34px; color: #cfcae8; opacity: 0;
    animation: promo-fade 400ms ease-out forwards; background: rgba(8,7,18,.7); padding: 2px 12px; }
  #promo .cap.out { animation: promo-leave 200ms ease-in forwards; }
  @keyframes promo-wipe { to { clip-path: inset(0 0 0 0); } }
  @keyframes promo-open { to { clip-path: inset(0 0 0 0); } }
  @keyframes promo-slam { to { opacity: 1; transform: none; filter: none; } }
  @keyframes promo-fade { to { opacity: 1; } }
  @keyframes promo-leave { to { opacity: 0; transform: translateY(18px) scale(.97); filter: blur(4px); } }

  /* ── flash + wipe, for cuts ── */
  #promo .flash { position: absolute; inset: 0; background: #fff; opacity: 0; mix-blend-mode: screen; }
  #promo .flash.go { animation: promo-flash 320ms ease-out 1; }
  @keyframes promo-flash { 0% { opacity: .75 } 100% { opacity: 0 } }
  #promo .wipe { position: absolute; inset: 0; display: flex; opacity: 1; visibility: hidden; }
  #promo .wipe b { flex: 1; background: #0b0a14; transform: scaleY(0); }
  #promo .wipe b:nth-child(odd) { background: #ea323c; } #promo .wipe b:nth-child(3n) { background: #3d8cff; }
  #promo .wipe.in { visibility: visible; } #promo .wipe.in b { animation: promo-bar-in 300ms cubic-bezier(.7,0,.3,1) forwards; }
  #promo .wipe.outgo { visibility: visible; } #promo .wipe.outgo b { transform: scaleY(1); animation: promo-bar-out 340ms cubic-bezier(.7,0,.3,1) forwards; }
  @keyframes promo-bar-in { from { transform: scaleY(0); transform-origin: top } to { transform: scaleY(1); transform-origin: top } }
  @keyframes promo-bar-out { from { transform: scaleY(1); transform-origin: bottom } to { transform: scaleY(0); transform-origin: bottom } }

  /* ── full-frame title cards ── */
  #promo .card { position: absolute; inset: 0; display: grid; place-items: center; opacity: 0; transition: opacity 260ms; }
  #promo .card.on { opacity: 1; }
  #promo .card .bg { position: absolute; inset: 0; background: rgba(6,5,14,.72); backdrop-filter: blur(3px); }
  #promo .card.solid .bg { background: radial-gradient(ellipse at 50% 45%, #1b1836 0%, #0a0916 60%, #050409 100%); backdrop-filter: none; }
  #promo .card .inner { position: relative; display: flex; flex-direction: column; align-items: center; gap: 28px; }
  #promo .card .kick { font-size: 26px; letter-spacing: 10px; color: #0b0a14; background: #ffa214; padding: 12px 18px 10px;
    clip-path: inset(0 100% 0 0); }
  #promo .card.on .kick { animation: promo-wipe 420ms steps(10) 120ms forwards; }
  #promo .card .title { display: flex; gap: 42px; font-size: 128px; letter-spacing: 4px; line-height: 1; }
  #promo .card .title span { display: inline-block; opacity: 0; transform: scale(2.4); filter: blur(14px); }
  #promo .card.on .title span { animation: promo-slam 460ms cubic-bezier(.2,1.3,.4,1) forwards; }
  #promo .card .title .red { color: #ea323c; text-shadow: 0 0 30px rgba(234,50,60,.8), 8px 8px 0 #3a0c10; }
  #promo .card .title .blue { color: #3d8cff; text-shadow: 0 0 30px rgba(61,140,255,.8), 8px 8px 0 #0c1f3f; }
  #promo .card .line { font-family: 'VT323', monospace; font-size: 52px; color: #f2eee4; opacity: 0; letter-spacing: 1px; }
  #promo .card.on .line { animation: promo-fade 500ms ease-out forwards; }
  #promo .card .url { font-size: 30px; color: #ffa214; letter-spacing: 3px; opacity: 0; text-shadow: 0 0 18px rgba(255,162,20,.7); }
  #promo .card.on .url { animation: promo-fade 500ms ease-out forwards; }
  #promo .card .coin { width: 150px; height: 150px; image-rendering: pixelated; opacity: 0; transform: scale(.2) rotate(-40deg); }
  #promo .card.on .coin { animation: promo-slam 600ms cubic-bezier(.2,1.4,.4,1) forwards; }
  #promo .card .vs { font-size: 54px; color: #fff7ea; opacity: 0; text-shadow: 0 0 20px #fff; }
  #promo .card.on .vs { animation: promo-slam 380ms cubic-bezier(.2,1.4,.4,1) forwards; }
  #promo .card .ship { position: absolute; right: 90px; bottom: 60px; width: 420px; image-rendering: pixelated; opacity: 0; transform: translateX(160px); }
  #promo .card.on .ship { animation: promo-glide 900ms cubic-bezier(.16,1,.3,1) 400ms forwards; }
  @keyframes promo-glide { to { opacity: 1; transform: none; } }
  #promo .card .stars { position: absolute; inset: 0; opacity: .8;
    background-image: radial-gradient(2px 2px at 10% 20%, #fff 50%, transparent 51%), radial-gradient(2px 2px at 80% 30%, #cfe 50%, transparent 51%),
      radial-gradient(3px 3px at 30% 80%, #fff 50%, transparent 51%), radial-gradient(2px 2px at 60% 60%, #fed 50%, transparent 51%),
      radial-gradient(2px 2px at 90% 85%, #fff 50%, transparent 51%), radial-gradient(2px 2px at 45% 12%, #fff 50%, transparent 51%);
    animation: promo-drift 12s linear infinite; }
  @keyframes promo-drift { to { transform: translateX(-120px); } }

  /* ── cursor (a screencast has none, and clicks need a hand in frame) ── */
  #promo .cursor { position: absolute; left: 0; top: 0; opacity: 0; transform-origin: 3px 2px; transition: opacity 300ms, scale 90ms;
    filter: drop-shadow(0 0 8px rgba(255,162,20,.7)) drop-shadow(3px 3px 0 rgba(0,0,0,.6)); }
  #promo .cursor.on { opacity: 1; } #promo .cursor.down { scale: .8; }
  #promo .ripple { position: absolute; width: 20px; height: 20px; margin: -10px 0 0 -10px; border: 4px solid #ffa214; border-radius: 50%;
    animation: promo-ripple 520ms ease-out forwards; }
  @keyframes promo-ripple { to { transform: scale(4.5); opacity: 0; } }
  `;

  const el = (tag, cls, html) => {
    const n = document.createElement(tag);
    if (cls) n.className = cls;
    if (html != null) n.innerHTML = html;
    return n;
  };

  let root, spot, spotTarget = null, flashEl, wipeEl, cursorEl;
  const cards = {};

  function mount() {
    if (root && root.isConnected) return;
    document.documentElement.classList.add('promo-on');
    const style = el('style');
    style.textContent = css;
    document.head.appendChild(style);
    root = el('div');
    root.id = 'promo';
    root.appendChild(el('div', 'grade'));
    spot = el('div', 'spot', '<i></i><i></i><i></i><i></i><div class="tag above"></div>');
    root.appendChild(spot);
    flashEl = el('div', 'flash');
    wipeEl = el('div', 'wipe', '<b></b>'.repeat(12));
    root.appendChild(flashEl);
    root.appendChild(wipeEl);
    cursorEl = el('div', 'cursor', cursorSvg());
    root.appendChild(cursorEl);
    document.body.appendChild(root);
    requestAnimationFrame(trackSpot);
  }

  function cursorSvg() {
    const px = ['X..........', 'XX.........', 'XOX........', 'XOOX.......', 'XOOOX......', 'XOOOOX.....',
      'XOOOOOX....', 'XOOOOOOX...', 'XOOOOOOOX..', 'XOOOOOOOOX.', 'XOOOOOXXXXX', 'XOOXOOX....',
      'XOX.XOOX...', 'XX..XOOX...', 'X....XOOX..', '.....XOOX..', '......XX...'];
    let r = '';
    px.forEach((row, y) => [...row].forEach((c, x) => {
      if (c !== '.') r += `<rect x="${x}" y="${y}" width="1" height="1" fill="${c === 'X' ? '#181b31' : '#ffffff'}"/>`;
    }));
    return `<svg viewBox="0 0 11 17" width="36" height="56" shape-rendering="crispEdges">${r}</svg>`;
  }

  const app = () => document.getElementById('app');

  /** The camera transform currently ON SCREEN (mid-transition included): scale + translate. */
  function current() {
    const m = getComputedStyle(app()).transform;
    if (!m || m === 'none') return { s: 1, tx: 0, ty: 0 };
    const v = m.match(/matrix\(([^)]+)\)/)[1].split(',').map(Number);
    return { s: v[0], tx: v[4], ty: v[5] };
  }

  /** An element's box in PAGE coordinates (as if the camera were at 1x), whatever the camera is doing. */
  function pageRect(target) {
    if (!target) return null;
    if (typeof target === 'object' && 'w' in target) return target;
    const node = typeof target === 'string' ? document.querySelector(target) : target;
    if (!node) throw new Error(`promo: no element for ${target}`);
    const r = node.getBoundingClientRect();
    const { s, tx, ty } = current();
    return { x: (r.left - tx) / s, y: (r.top - ty) / s, w: r.width / s, h: r.height / s };
  }

  /** Union of several targets' page rects. */
  function unionRect(targets) {
    const rs = targets.map(pageRect);
    const x = Math.min(...rs.map((r) => r.x)), y = Math.min(...rs.map((r) => r.y));
    return { x, y, w: Math.max(...rs.map((r) => r.x + r.w)) - x, h: Math.max(...rs.map((r) => r.y + r.h)) - y };
  }

  /**
   * Frame a target. `pad` is page px around it; `max` caps the zoom; `dy` shifts the framing down
   * (screen px) to leave room for a caption under it; `ms`/`ease` set the move.
   */
  function cam(target, o = {}) {
    mount();
    const a = app();
    if (!target) {
      a.style.setProperty('--cam-ms', `${o.ms ?? 1100}ms`);
      a.style.transform = 'translate(0px, 0px) scale(1)';
      return;
    }
    const r = Array.isArray(target) ? unionRect(target) : pageRect(target);
    const pad = o.pad ?? 40;
    let s = Math.min(W / (r.w + pad * 2), (H - (o.room ?? 0)) / (r.h + pad * 2));
    s = Math.max(1, Math.min(s, o.max ?? 4));
    const cx = r.x + r.w / 2, cy = r.y + r.h / 2;
    let tx = W / 2 - s * cx + (o.dx ?? 0);
    let ty = H / 2 - s * cy - (o.room ?? 0) / 2 + (o.dy ?? 0);
    tx = Math.min(0, Math.max(W - W * s, tx));
    ty = Math.min(0, Math.max(H - H * s, ty));
    a.style.setProperty('--cam-ms', `${o.ms ?? 1100}ms`);
    a.style.setProperty('--cam-ease', o.ease ?? 'cubic-bezier(.65,0,.35,1)');
    a.style.transform = `translate(${tx}px, ${ty}px) scale(${s})`;
    if (o.whip) {
      a.classList.add('whip');
      setTimeout(() => a.classList.remove('whip'), Math.min(260, (o.ms ?? 1100) * 0.5));
    }
  }

  function trackSpot() {
    if (spotTarget && spot) {
      const node = typeof spotTarget.target === 'string' ? document.querySelector(spotTarget.target) : null;
      const r = node ? node.getBoundingClientRect() : spotTarget.rect;
      if (r) {
        const p = spotTarget.pad;
        spot.style.left = `${r.left - p}px`;
        spot.style.top = `${r.top - p}px`;
        spot.style.width = `${r.width + p * 2}px`;
        spot.style.height = `${r.height + p * 2}px`;
      }
    }
    requestAnimationFrame(trackSpot);
  }

  /** Spotlight an element (re-measured every frame, so it rides the camera). null clears it. */
  function spotlight(target, o = {}) {
    mount();
    if (!target) {
      spot.classList.remove('on');
      setTimeout(() => { if (!spot.classList.contains('on')) spotTarget = null; }, 400);
      return;
    }
    spotTarget = { target, pad: o.pad ?? 12 };
    spot.style.setProperty('--spot', o.color ?? '#ffa214');
    const tag = spot.querySelector('.tag');
    tag.className = `tag ${o.side ?? 'above'}`;
    tag.textContent = o.label ?? '';
    tag.style.display = o.label ? '' : 'none';
    spot.classList.remove('on');
    void spot.offsetWidth;
    spot.classList.add('on');
  }

  let capEl = null;
  /** One caption. `head` accepts <em>…</em> for the accent word(s); `|` breaks nothing, words slam in order. */
  function caption(c) {
    mount();
    if (capEl) {
      const old = capEl;
      old.classList.add('out');
      setTimeout(() => old.remove(), 220);
      capEl = null;
    }
    if (!c) return;
    const n = el('div', `cap ${c.pos ?? ''}`);
    n.style.setProperty('--accent', c.accent ?? '#ffa214');
    const delay = c.delay ?? 0;
    let i = 0;
    const words = c.head.replace(/<em>(.*?)<\/em>/g, (_, w) => w.split(' ').map((x) => `\u0001${x}`).join(' '));
    const head = words.split(' ').map((w) => {
      const accent = w.startsWith('\u0001');
      const txt = accent ? w.slice(1) : w;
      const d = delay + 120 + i++ * 90;
      return `<span style="animation-delay:${d}ms">${accent ? `<em>${txt}</em>` : txt}</span>`;
    }).join(' ');
    const after = delay + 160 + i * 90;
    n.innerHTML =
      (c.kick ? `<div class="kick" style="animation-delay:${delay}ms">${c.kick}</div>` : '') +
      `<div class="head" style="animation-delay:${delay}ms">${head}</div>` +
      (c.sub ? `<div class="sub" style="animation-delay:${after}ms">${c.sub}</div>` : '') +
      (c.fine ? `<div class="fine" style="animation-delay:${after + 150}ms">${c.fine}</div>` : '');
    root.appendChild(n);
    capEl = n;
  }

  function flash() {
    mount();
    flashEl.classList.remove('go');
    void flashEl.offsetWidth;
    flashEl.classList.add('go');
  }

  function shake() {
    const h = document.documentElement;
    h.classList.remove('shake');
    void h.offsetWidth;
    h.classList.add('shake');
    setTimeout(() => h.classList.remove('shake'), 460);
  }

  /** Bars close over the frame (in) / open off it (out): a cut that hides a route change. */
  function wipe(dir) {
    mount();
    wipeEl.className = 'wipe';
    void wipeEl.offsetWidth;
    wipeEl.className = `wipe ${dir === 'in' ? 'in' : 'outgo'}`;
    const bars = [...wipeEl.children];
    bars.forEach((b, i) => (b.style.animationDelay = `${i * 18}ms`));
    if (dir !== 'in') setTimeout(() => (wipeEl.className = 'wipe'), 600);
  }

  /** Full-frame cards, built from HTML record.mjs hands in (art is passed as data: URIs). */
  function card(name, html, o = {}) {
    mount();
    if (!cards[name]) {
      const n = el('div', `card ${o.solid ? 'solid' : ''}`, `<div class="bg">${o.solid ? '<div class="stars"></div>' : ''}</div><div class="inner">${html}</div>${o.extra ?? ''}`);
      root.insertBefore(n, cursorEl);
      cards[name] = n;
    }
    requestAnimationFrame(() => cards[name].classList.add('on'));
  }
  function uncard(name) {
    const n = cards[name];
    if (!n) return;
    n.classList.remove('on');
    setTimeout(() => { n.remove(); delete cards[name]; }, 300);
  }

  // Cursor: record.mjs moves the real mouse; this draws it where the mouse is.
  function cursor(on) {
    mount();
    cursorEl.classList.toggle('on', !!on);
  }
  window.addEventListener('mousemove', (e) => {
    if (!cursorEl) return;
    cursorEl.style.left = `${e.clientX}px`;
    cursorEl.style.top = `${e.clientY}px`;
  }, true);
  window.addEventListener('mousedown', (e) => {
    if (!cursorEl || !cursorEl.classList.contains('on')) return;
    cursorEl.classList.add('down');
    const r = el('div', 'ripple');
    r.style.left = `${e.clientX}px`;
    r.style.top = `${e.clientY}px`;
    root.appendChild(r);
    setTimeout(() => r.remove(), 600);
  }, true);
  window.addEventListener('mouseup', () => cursorEl && cursorEl.classList.remove('down'), true);

  /** Where an element's centre is ON SCREEN right now (for moving the mouse to it). */
  function screenCenter(target) {
    const node = typeof target === 'string' ? document.querySelector(target) : target;
    const r = node.getBoundingClientRect();
    return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
  }

  /** The top `h` page px of the terminal tube: where the race and the result print. The tube itself
      is a tall fixed box that is mostly empty black during a race, so framing all of it shows air. */
  function tube(h) {
    const r = pageRect('#terminal-form');
    return { x: r.x, y: r.y, w: r.w, h: Math.min(h, r.h) };
  }

  window.__promo = { tube, mount, cam, pageRect, spotlight, caption, flash, shake, wipe, card, uncard, cursor, screenCenter, current };
})();

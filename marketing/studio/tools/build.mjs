// Turns a campaign's `content.mjs` into self-contained .html pieces.
//
//   node tools/build.mjs <campaign-slug>
//
// The content/render split is the point. Copy lives in ONE file that a person can read and argue
// with (`campaigns/<slug>/content.mjs`); how it is drawn lives here. Editing a headline never means
// opening a generator, and reviewing the campaign never means reading markup.
//
// Output: `campaigns/<slug>/pieces/*.html`, one file per still plus one for the video, each a
// single file with fonts, art and audio embedded (see tools/assets.mjs for why).
//
// Pieces are REGENERATED, not edited. If you hand-edit a piece, the next build silently discards
// it — so orphans are removed here rather than left to rot, and anything starting with `_` is
// treated as a tool of the campaign and spared.

import { existsSync, mkdirSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

import { BOARDS, FORMATS } from './boards.mjs';
import { addArtRoot, art, audio, brandCss, fontFaceCss, sizeKb } from './assets.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const CAMPAIGNS = join(here, '..', 'campaigns');

const slug = process.argv[2];
if (!slug) {
  const have = existsSync(CAMPAIGNS)
    ? readdirSync(CAMPAIGNS, { withFileTypes: true }).filter((d) => d.isDirectory()).map((d) => d.name)
    : [];
  throw new Error(`usage: node tools/build.mjs <campaign-slug>\ncampaigns: ${have.join(', ') || '(none)'}`);
}

const DIR = join(CAMPAIGNS, slug);
const OUT = join(DIR, 'pieces');
if (!existsSync(join(DIR, 'content.mjs'))) {
  throw new Error(`no campaign at ${DIR}\nexpected ${join(DIR, 'content.mjs')}`);
}

// A campaign has its own captured UI shots in ui/; resolve them by filename like any asset.
addArtRoot(join(DIR, 'ui'));

const content = await import(pathToFileURL(join(DIR, 'content.mjs')).href);
const campaign = content.campaign ?? {};
const stills = content.stills ?? [];
const video = content.video ?? null;

/* ── escaping ────────────────────────────────────────────────────────────────
   Headlines carry deliberate <br> (still.md: write the line breaks by hand so the export matches
   the preview), so copy fields are inserted as-is and are trusted markup, not user input. Only the
   attribute-position values are escaped, because a stray quote there silently breaks the piece
   rather than showing up as a wrong character. */
const attr = (s) => String(s).replace(/"/g, '&quot;');

/* ── the accent rule, enforced ───────────────────────────────────────────────
   tokens.css explains why these are scarce and what each MEANS. Here it is mechanical: a piece
   names one, and the value it maps to is the only non-amber colour that reaches the page. An
   unknown name is a build failure, not a silent fallback to amber — a piece that quietly loses its
   accent looks fine and is wrong. */
const ACCENTS = {
  amber: 'var(--color-primary)',
  cyan: 'var(--color-cyan)',
  magenta: 'var(--color-magenta)',
  live: 'var(--color-live)',
  orbit: 'var(--color-orbit)',
  phosphor: 'var(--color-phosphor)',
};
function accentVar(name = 'amber', where) {
  const v = ACCENTS[name];
  if (!v) {
    throw new Error(
      `${where}: unknown accent "${name}". Use one of: ${Object.keys(ACCENTS).join(', ')}\n` +
        'See brand/tokens.css — each accent already means something in the product.',
    );
  }
  return v;
}

/* ── shared page CSS ─────────────────────────────────────────────────────────
   Everything inside the board is sized in `cqw` (percent of the board's own width) via a size
   container, so one set of type rules holds at 1080 and at 1600 without a second scale factor to
   keep in step.

   THE STAGE IS SHRUNK FOR PREVIEW AND MUST NOT BE FOR EXPORT. `.stage` carries a transform so a
   1080x1920 piece fits a laptop screen. Every exporter here turns that transform off before it
   captures, and then checks the pixels it got. Forgetting either half is how a series gets
   published at 94% of nominal with the app stretching it back (platforms.md §"Measure the piece,
   not the preview"). */
function pageCss() {
  return `
${fontFaceCss()}
${brandCss()}

*, *::before, *::after { box-sizing: border-box; }
html, body { margin: 0; padding: 0; background: #0b0b12; }
body { display: flex; flex-direction: column; align-items: center; gap: 18px; padding: 18px; }

/* The stage exists ONLY to fit the board on a screen. It never affects the exported pixels. */
.stage { transform-origin: top center; }
.stage.no-scale { transform: none !important; }

/* THE PIECE. Exactly nominal pixels, always. Never a viewport unit. */
.art {
  position: relative;
  overflow: hidden;
  container-type: size;
  font-family: var(--font-text);
  color: var(--color-ink);
  --accent: var(--color-primary);
}

/* toolbar — deliberately OUTSIDE .art so it can never enter a capture */
.bar {
  font-family: var(--font-text);
  font-size: 18px;
  color: var(--color-ink-mute);
  display: flex; align-items: center; gap: 14px;
}
.bar button {
  font-family: var(--font-display); font-size: 10px; color: #10101a;
  background: var(--color-primary); border: 0; border-radius: 3px;
  padding: 10px 14px; cursor: pointer;
}

/* ── type ───────────────────────────────────────────────────────────────── */
.kicker {
  font-family: var(--font-display);
  font-size: 1.9cqw; letter-spacing: 0.22em; text-transform: uppercase;
  color: var(--accent);
}
.headline {
  font-family: var(--font-display);
  font-size: 5.2cqw; line-height: 1.36; letter-spacing: 0.01em;
  text-wrap: balance; margin: 0;
}
.headline em { font-style: normal; color: var(--accent); }
.sub {
  font-size: 4.4cqw; line-height: 1.16; color: var(--color-ink-dim);
  text-wrap: balance; margin: 0;
}
.chips { display: flex; flex-wrap: wrap; gap: 1.4cqw; }
.chips .chip { font-size: 1.75cqw; }
.stat { display: flex; flex-direction: column; gap: 0.6cqw; }
.stat b {
  font-family: var(--font-display); font-size: 11cqw; line-height: 1;
  color: var(--accent); font-weight: 400;
}
.stat span { font-size: 3.4cqw; color: var(--color-ink-mute); letter-spacing: 0.04em; }

/* ── furniture ──────────────────────────────────────────────────────────── */
.mark {
  position: absolute; z-index: 5; display: flex; align-items: center; gap: 1.6cqw;
}
.mark img { width: 6.2cqw; height: auto; image-rendering: auto; filter: drop-shadow(0 2px 6px rgba(0,0,0,.85)); }
.mark span {
  font-family: var(--font-display); font-size: 2.1cqw; letter-spacing: 0.16em;
  text-shadow: 0 2px 8px rgba(0,0,0,.9);
}
.handle {
  position: absolute; z-index: 5;
  font-family: var(--font-display); font-size: 1.9cqw; letter-spacing: 0.12em;
  color: var(--color-ink-mute);
}
.copy { position: absolute; z-index: 4; display: flex; flex-direction: column; }

/* ── PORTRAIT / SQUARE: art on top, copy below on solid ground ──────────── */
body.feed .art, body.story .art, body.square .art { }
body.feed .art-band, body.story .art-band, body.square .art-band {
  position: absolute; left: 0; right: 0; top: 0;
}
body.feed .art-band { height: 52%; }
body.story .art-band { height: 48%; }
body.square .art-band { height: 46%; }

body.feed .mark, body.square .mark { top: 6cqw; left: 7.4cqw; }
body.feed .handle, body.square .handle { bottom: 6cqw; left: 7.4cqw; }
body.feed .copy, body.square .copy { left: 7.4cqw; right: 7.4cqw; bottom: 13cqw; gap: 3.2cqw; }
/* No art band: centre the block rather than leaving the top half empty. */
body.no-art .copy { top: 50%; bottom: auto; transform: translateY(-50%); }
body.no-art.story .copy { top: 46%; }

/* STORY MARGINS ARE NOT THE FEED'S. Instagram draws its interface OVER the piece: roughly the
   top 250px and bottom 350px of a 1080x1920 are covered. Content lives between 20% and 78% of
   the height. platforms.md holds the measured numbers and the method for the rest. */
body.story .mark { top: 37cqw; left: 8cqw; }        /* 400px = 20.8% of 1920 */
body.story .handle { bottom: 44cqw; left: 8cqw; }   /* 475px: top edge at 74.2% */
body.story .copy { left: 8cqw; right: 8cqw; bottom: 52cqw; gap: 3.4cqw; }  /* 562px = 70.7% */
body.story .headline { font-size: 5.6cqw; }

/* ── LANDSCAPE: copy in the LEFT HALF, veil from the left ───────────────────
   Not the portrait layout squashed. On a wide board the art finally fits (most source art is
   landscape to begin with) and the reading axis turns: text sits left, the veil comes in from the
   left edge. Reusing the bottom gradient here is exactly what makes a 16:9 look cramped. */
body.x16 .art-band, body.thumb .art-band { position: absolute; inset: 0; }
body.x16 .art-band::before, body.thumb .art-band::before {
  background: linear-gradient(90deg,
    var(--color-void) 0%, var(--color-void) 34%,
    color-mix(in oklab, var(--color-void) 55%, transparent) 62%, transparent 100%);
}
body.x16 .mark, body.thumb .mark { top: 4.4cqw; left: 4.4cqw; }
body.x16 .handle, body.thumb .handle { bottom: 4.4cqw; left: 4.4cqw; }
body.x16 .copy, body.thumb .copy { left: 4.4cqw; right: 48%; top: 14cqw; gap: 2.4cqw; }
body.x16 .headline, body.thumb .headline { font-size: 3.8cqw; line-height: 1.38; }
body.x16 .sub, body.thumb .sub { font-size: 2.8cqw; }
body.x16 .kicker, body.thumb .kicker { font-size: 1.3cqw; }
body.x16 .stat b, body.thumb .stat b { font-size: 7cqw; }

/* A landscape THUMBNAIL is read at about 210px wide in the app's grid. Four words, drawn big.
   It has to work small, so it is checked small — never on the monitor at full size. */
body.thumb .headline { font-size: 4.6cqw; }
`;
}

/* ── still ─────────────────────────────────────────────────────────────────── */
function stillHtml(piece) {
  const board = BOARDS[piece.board];
  if (!board) {
    throw new Error(
      `${piece.id}: unknown board "${piece.board}". Use one of: ${Object.keys(BOARDS).join(', ')}`,
    );
  }
  const accent = accentVar(piece.accent, piece.id);

  const artBlock = piece.art
    ? `<div class="art-band" style="--art-x:${attr(piece.art.x ?? '50%')};--art-y:${attr(
        piece.art.y ?? '50%',
      )};--art-zoom:${attr(piece.art.zoom ?? 1)};--art-veil:${attr(piece.art.veil ?? 0.28)};--art-render:${attr(piece.art.render ?? 'pixelated')};--art-fit:${attr(piece.art.fit ?? 'cover')}">
      <img src="${art(piece.art.src)}" alt="">
    </div>`
    : '';

  // Chips and a stat REPLACE body copy; they never stack on top of it (still.md §"Anatomy").
  const body = piece.stat
    ? `<div class="stat"><b>${piece.stat.value}</b><span>${piece.stat.label}</span></div>`
    : piece.chips
      ? `<div class="chips">${piece.chips
          .map((c) => `<span class="chip">${c}</span>`)
          .join('')}</div>`
      : piece.sub
        ? `<p class="sub">${piece.sub}</p>`
        : '';

  return `<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<title>${attr(piece.id)} · ${attr(campaign.slug ?? slug)}</title>
<style>${pageCss()}</style>
</head>
<body class="${piece.board}${piece.art ? '' : ' no-art'}">
<div class="stage">
  <div class="art space-ground edge-vignette" style="width:${board.w}px;height:${board.h}px;--accent:${accent}">
    ${artBlock}
    <div class="mark"><img src="${art('monk-coin.png')}" alt=""><span>SPACE STROKER</span></div>
    <div class="copy">
      ${piece.kicker ? `<div class="kicker">${piece.kicker}</div>` : ''}
      <h1 class="headline">${piece.headline}</h1>
      ${body}
    </div>
    <div class="handle">${attr(campaign.handle ?? '@SpaceStroker')}</div>
  </div>
</div>
<div class="bar">
  <button onclick="savePng()">Save PNG</button>
  <span>${board.where} &middot; ${attr(piece.id)}</span>
</div>
<script>
/* Fit the board to the window for review. Turned off before any capture — by this button, and by
   export-png.mjs. Both, because whichever one you forget is the one that ships. */
function fit(){
  const s=document.querySelector('.stage'), a=document.querySelector('.art');
  const k=Math.min(1,(window.innerWidth-40)/a.offsetWidth,(window.innerHeight-140)/a.offsetHeight);
  s.style.transform='scale('+k+')';
  s.style.height=(a.offsetHeight*k)+'px';
}
addEventListener('resize',fit); fit();
document.fonts.ready.then(()=>{ fit(); window.__ready=true; });

/* The in-page button is the fallback for someone without Node. It draws with an SVG
   foreignObject, which is the browser's own renderer, so it does not reimplement CSS the way a
   screenshot library does. It is still the SECOND-best export: export-png.mjs drives real Chrome
   and checks the result. Prefer that; this exists so the file is useful on its own. */
async function savePng(){
  const s=document.querySelector('.stage'); s.classList.add('no-scale');
  const a=document.querySelector('.art');
  const w=a.offsetWidth,h=a.offsetHeight;
  const html=new XMLSerializer().serializeToString(a);
  const svg='<svg xmlns="http://www.w3.org/2000/svg" width="'+w+'" height="'+h+'">'+
    '<foreignObject width="100%" height="100%">'+
    '<div xmlns="http://www.w3.org/1999/xhtml">'+html+'</div></foreignObject></svg>';
  const img=new Image();
  img.src='data:image/svg+xml;charset=utf-8,'+encodeURIComponent(svg);
  await img.decode();
  const c=document.createElement('canvas'); c.width=w; c.height=h;
  c.getContext('2d').drawImage(img,0,0);
  const link=document.createElement('a');
  link.download=${JSON.stringify(piece.id + '.png')};
  link.href=c.toDataURL('image/png'); link.click();
  s.classList.remove('no-scale');
}
</script>
</body></html>`;
}

/* ── video ─────────────────────────────────────────────────────────────────── */
function videoHtml(v) {
  const fmt = FORMATS[v.format];
  if (!fmt) {
    throw new Error(
      `video: unknown format "${v.format}". Use one of: ${Object.keys(FORMATS).join(', ')}`,
    );
  }
  const scenes = v.scenes ?? [];
  if (!scenes.length) throw new Error('video: no scenes');

  // Absolute scene start times, computed HERE and shared by the picture and the sound, so the two
  // can never disagree about when a cut happens.
  let t = 0;
  const timed = scenes.map((s) => {
    const at = t;
    t += s.ms;
    return { ...s, at };
  });
  const durationMs = t;

  const sfxNames = [...new Set(timed.map((s) => s.sfx).filter(Boolean))];
  const clips = Object.fromEntries(sfxNames.map((n) => [n, audio(n)]));
  if (v.music) clips.__music = audio(v.music);

  const sceneMarkup = timed
    .map((s, i) => {
      const accent = accentVar(s.accent ?? v.accent, `${v.id} scene ${i + 1}`);
      const artBlock = s.art
        ? `<div class="art-band ${s.art.frame === 'right' ? 'art-band--right' : ''}" style="--art-x:${attr(s.art.x ?? '50%')};--art-y:${attr(
            s.art.y ?? '50%',
          )};--art-zoom:${attr(s.art.zoom ?? 1)};--art-veil:${attr(s.art.veil ?? 0.28)};--art-render:${attr(s.art.render ?? 'pixelated')};--art-fit:${attr(s.art.fit ?? 'cover')}">
          <img src="${art(s.art.src)}" alt=""></div>`
        : '';
      const body = s.podium
        ? `<div class="podium">${s.podium
            .map(
              (pl, j) =>
                `<div class="place place--${pl.place}" style="--d:${j * 160}ms">` +
                `<span class="rank">${pl.label ?? pl.place + (['st','nd','rd'][pl.place - 1] ?? 'th')}</span>` +
                `<span class="sol">${pl.sol}</span>` +
                (pl.stroke ? `<span class="stroke">${pl.stroke}</span>` : '') +
                `</div>`,
            )
            .join('')}</div>`
        : s.stat
        ? `<div class="stat"><b>${s.stat.value}</b><span>${s.stat.label}</span></div>`
        : s.chips
          ? `<div class="chips">${s.chips
              .map((c, j) => `<span class="chip" style="--d:${j * 140}ms">${c}</span>`)
              .join('')}</div>`
          : s.sub
            ? `<p class="sub">${s.sub}</p>`
            : '';
      return `<section class="${s.layout === 'full' ? 'scene scene--full' : 'scene'}" data-at="${s.at}" data-ms="${s.ms}" style="--accent:${accent}">
        ${artBlock}
        <div class="copy">
          ${s.kicker ? `<div class="kicker">${s.kicker}</div>` : ''}
          <h1 class="headline">${s.headline}</h1>
          ${body}
        </div>
      </section>`;
    })
    .join('\n');

  /* PIECE-LEVEL BACKGROUND, and it is mostly a landscape concern.
     On a 9:16 piece the copy fills the frame, so per-scene art is enough. On a 16:9 the copy sits
     in the left half by design, which leaves the right half empty on every scene that has no art
     of its own — four scenes out of five, reading as dead black rather than as space. A plate that
     persists behind every scene fixes that without making the video jumpy, which is what adding
     a different image per scene would do. Scene art still draws on top when present. */
  const bg = v.bg
    ? `<div class="bg-art art-band" style="--art-x:${attr(v.bg.x ?? '50%')};--art-y:${attr(
        v.bg.y ?? '50%',
      )};--art-zoom:${attr(v.bg.zoom ?? 1)};--art-veil:${attr(v.bg.veil ?? 0.3)};--art-render:${attr(
        v.bg.render ?? 'pixelated',
      )}"><img src="${art(v.bg.src)}" alt=""></div>`
    : '';

  return `<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<title>${attr(v.id)} · ${attr(campaign.slug ?? slug)}</title>
<style>${pageCss()}

body { background:#0b0b12; }
.art { width:${fmt.w}px; height:${fmt.h}px; }

/* Behind every scene, and behind the copy. Full-bleed, never a top band. */
.bg-art { position:absolute; inset:0; height:auto; z-index:0; }

.scene { position:absolute; inset:0; opacity:0; z-index:3; transition: opacity 520ms ease; }
/* ⚠️ The OUTGOING scene has to keep painting while it fades, so it cannot be display:none. Both
   scenes are composited for half a second; that is the cross-fade. */
.scene.active { opacity:1; }
.scene.active .copy > *, .scene.active .stat, .scene.active .chips {
  animation: rise .62s var(--ease-out-expo) both;
}
.scene.active .kicker { animation-delay: .04s; }
.scene.active .headline { animation-delay: .12s; }
.scene.active .sub, .scene.active .stat, .scene.active .chips { animation-delay: .24s; }
.scene.active .chip { animation: pop .42s var(--ease-arcade) both; animation-delay: calc(.3s + var(--d)); }
.scene.active .place { animation: pop .5s var(--ease-arcade) both; animation-delay: calc(.34s + var(--d)); }
@keyframes rise { from { opacity:0; transform: translateY(26px); } to { opacity:1; transform:none; } }
@keyframes pop { from { opacity:0; transform: scale(.82); } to { opacity:1; transform:none; } }

/* Progress bar. HIDDEN in slideshow export: the app draws its own step dots, and two progress
   indicators on one screen is just confusion. */
.progress { position:absolute; top:0; left:0; height:6px; width:0; z-index:9; background: var(--color-primary); }
body.slides .progress { display:none; }

/* THE START MARKER. One magenta frame painted the instant the piece begins, so the recorder can
   find t=0 exactly. Recording starts with the browser window, which means an unpredictable amount
   of page-load slack at the head — measured elsewhere at 0.77 to 1.05 seconds, varying run to run.
   Cutting from the END instead looks right and is wrong: every scene change lands late against the
   sound, which you only notice by measuring, never by watching. */
.marker { position:absolute; inset:0; z-index:99; background:#ff00ff; display:none; }
/* The piece IS the frame while recording: no page padding, no toolbar. */
body.render { padding:0 !important; gap:0 !important; }
body.render .bar { display:none !important; }
body.render .marker.on { display:block; }

/* Video safe framing, and the two families are OPPOSITE. Overlaid surfaces (Reels, Stories,
   Shorts) draw interface ON TOP, so the bottom must be kept clear. Inset surfaces (TikTok photo
   mode) place interface BELOW the piece, where the same reserved band becomes a dead gap.
   platforms.md carries the measurements and which is which. */
.copy { left:8cqw; right:8cqw; gap:3.4cqw; }
body.reels .copy { bottom:46cqw; }   /* 497px: block foot at 74.1% of 1920 */
body.reels .mark { top:37cqw; left:8cqw; }  /* 400px = 20.8% */
body.slides .copy { bottom:28cqw; }  /* inset allows to 86%: 302px = 84.3% */
body.slides .mark { top:35cqw; left:8cqw; } /* 378px = 19.7%, inset floor is 19% */
.art-band { position:absolute; left:0; right:0; top:0; height:46%; }

/* ── PODIUM ─────────────────────────────────────────────────────────────────
   ⚠️ A NAMED EXCEPTION to the one-accent rule (brand/tokens.css). A podium is medal metal, not
   brand accent: gold, silver and bronze are what the shape means to everyone who has ever seen
   one, and forcing all three places into a single accent throws away the ranking the block
   exists to show. It is licensed HERE ONLY, for a prize breakdown. */
.podium { display:flex; align-items:flex-end; gap:1.6cqw; }
.podium .place {
  flex:1; display:flex; flex-direction:column; align-items:center; gap:0.5cqw;
  padding:1.4cqw 1cqw; border-radius:var(--radius-arcade);
  border:2px solid color-mix(in oklab, var(--medal) 55%, var(--color-line));
  background: linear-gradient(180deg,
    color-mix(in oklab, var(--medal) 16%, transparent) 0%,
    color-mix(in oklab, var(--color-void) 92%, transparent) 100%);
  box-shadow: 0 0 26px -8px color-mix(in oklab, var(--medal) 60%, transparent);
}
.podium .place--1 { --medal: var(--color-primary);      transform: translateY(-1.6cqw); }
.podium .place--2 { --medal: oklch(0.86 0.02 250); }
.podium .place--3 { --medal: var(--color-primary-deep); }
.podium .rank {
  font-family:var(--font-display); font-size:2.4cqw; color:var(--medal); line-height:1;
}
.podium .sol {
  font-family:var(--font-display); font-size:2.9cqw; color:var(--color-ink); line-height:1.1;
  white-space:nowrap;
}
.podium .stroke { font-size:2cqw; color:var(--medal); letter-spacing:0.04em; white-space:nowrap; }
body.wide .podium .rank { font-size:1.5cqw; }
body.wide .podium .sol { font-size:2cqw; }
body.wide .podium .stroke { font-size:1.35cqw; }


/* ── LANDSCAPE (X, YouTube, the site) ────────────────────────────────────────
   The same reading axis the x16 STILL uses, for the same reason: on a wide board the copy sits in
   the LEFT HALF and the veil comes in from the left, because reusing the portrait layout's bottom
   gradient is exactly what makes a 16:9 look squashed. Art is full-bleed here rather than a top
   band, since a landscape plate finally fits a landscape board.

   ⚠️ X is an INSET surface for video: the timeline shows a 16:9 whole and draws no interface over
   the frame, so there is no band to dodge. What it does add is a mute control and a duration chip
   in the BOTTOM CORNERS, which is why nothing here reaches the last ~12% of the height. */
body.wide .art-band, body.square .art-band { position:absolute; inset:0; height:auto; }
body.wide .art-band::before, body.square .art-band::before {
  background: linear-gradient(90deg,
    var(--color-void) 0%, var(--color-void) 34%,
    color-mix(in oklab, var(--color-void) 55%, transparent) 62%, transparent 100%);
}
body.wide .copy { left:4.4cqw; right:48%; top:26cqw; bottom:auto; gap:2.2cqw; }
body.wide .mark { top:4.4cqw; left:4.4cqw; }
body.wide .headline { font-size:3.8cqw; line-height:1.38; }
body.wide .sub { font-size:2.8cqw; }
body.wide .kicker { font-size:1.3cqw; }
body.wide .chips { gap:1cqw; }
body.wide .chips .chip { font-size:1.25cqw; }
body.wide .stat b { font-size:7cqw; }
/* The progress bar is kept on a landscape video: X draws a scrubber only on hover, so unlike a
   Story there is no second ruler competing with it. */

body.square .copy { left:7cqw; right:7cqw; bottom:12cqw; top:auto; gap:2.8cqw; }
body.square .mark { top:6cqw; left:7cqw; }
body.square .headline { font-size:5cqw; }

/* A SHOT SCENE. When the art IS the message — a screenshot of our own UI — the copy must not sit
   on top of it, which is what the default full-bleed band produces. The band is inset to the right
   and the fusing gradient is dropped, because there is no longer any text over the picture to
   protect. */
body.wide .art-band--right {
  left: 41%; right: 2.5%; top: 9%; bottom: 9%;
  border-radius: var(--radius-arcade);
  box-shadow: 0 0 40px -10px color-mix(in oklab, var(--color-primary) 45%, transparent);
}
body.wide .art-band--right::before { display: none; }
body.wide .art-band--right::after { opacity: var(--art-veil, 0); }

/* A FULL-WIDTH SCENE. The podium is a three-column block; squeezed into the left half it reads as
   three chips rather than as a ranking. This scene gets the whole frame. */
body.wide .scene--full .copy { left: 5cqw; right: 5cqw; top: 20cqw; }
body.wide .scene--full .headline { font-size: 4.4cqw; }
body.wide .scene--full .podium { margin-top: 1cqw; }
body.wide .scene--full .podium .rank { font-size: 1.9cqw; }
body.wide .scene--full .podium .sol { font-size: 2.6cqw; }
body.wide .scene--full .podium .stroke { font-size: 1.6cqw; }
</style>
</head>
<body class="${v.format}">
<div class="stage">
  <div class="art space-ground edge-vignette">
    ${bg}
    <div class="progress"></div>
    ${sceneMarkup}
    <div class="mark"><img src="${art('monk-coin.png')}" alt=""><span>SPACE STROKER</span></div>
    <div class="marker"></div>
  </div>
</div>
<div class="bar">
  <button onclick="__start()">Replay</button>
  <button onclick="document.querySelector('.art').requestFullscreen()">Full screen</button>
  <span>${fmt.where} &middot; ${(durationMs / 1000).toFixed(1)}s &middot; ${attr(v.id)}</span>
</div>
<script>
const SCENES = ${JSON.stringify(timed.map((s) => ({ at: s.at, ms: s.ms, sfx: s.sfx ?? null })))};
const CLIPS  = ${JSON.stringify(clips)};
const MUSIC_GAIN = ${JSON.stringify(v.musicGain ?? 0.45)};
const DURATION = ${durationMs};
const params = new URLSearchParams(location.search);
const isRender = params.has('render');
const isSlides = params.has('slides');

window.__durationMs = DURATION;
window.__done = false;
/* No synthesized sound design here: every sound is a real clip from the game. render.mjs reads
   this so a "sound effects only" render of a music-only piece is refused instead of quietly
   producing a silent file that nobody notices until it is posted. */
window.__noSfx = ${JSON.stringify(!sfxNames.length)};
window.__startMarker = 200; // ms of magenta at t=0; must outlast several frames at 30 fps

/* The slideshow cut only makes sense for the vertical format — it exists to be swiped in TikTok
   photo mode or Stories, which are 9:16 surfaces. On a landscape piece it is refused rather than
   silently producing wrongly-framed stills. */
window.__slidesOk = ${JSON.stringify(v.format === 'reels')};
if (isSlides && window.__slidesOk) { document.body.classList.remove('reels'); document.body.classList.add('slides'); }
if (isRender) document.body.classList.add('render');

const els = [...document.querySelectorAll('.scene')];
window.__sceneCount = () => els.length;
window.__showScene = (n) => { els.forEach((e,i)=>e.classList.toggle('active', i===n)); };
/* How long the exporter must wait before photographing a scene: the longest entry animation plus
   its delay. A slide shot too early catches a chip halfway in. */
window.__settleMs = 1500;

/* ── audio ────────────────────────────────────────────────────────────────
   ONE scheduling function, used by the live preview and by the offline render alike. The moment
   there are two, the file that ships stops being the thing that was approved — so this takes a
   context and an absolute start time and never says "play now". */
const buffers = {};
async function decodeAll(ctx){
  for (const [name, uri] of Object.entries(CLIPS)) {
    const bytes = Uint8Array.from(atob(uri.split(',')[1]), c => c.charCodeAt(0));
    buffers[name] = await ctx.decodeAudioData(bytes.buffer);
  }
}
function schedule(ctx, at0, { music = true, sfx = true } = {}){
  const secs = DURATION / 1000;
  if (music && buffers.__music) {
    const g = ctx.createGain();
    g.gain.value = MUSIC_GAIN;
    /* The bed is almost always shorter than the piece, so it loops, and it fades at the end
       rather than being cut mid-note. */
    g.gain.setValueAtTime(MUSIC_GAIN, at0 + Math.max(0, secs - 1.2));
    g.gain.linearRampToValueAtTime(0.0001, at0 + secs);
    g.connect(ctx.destination);
    const s = ctx.createBufferSource();
    s.buffer = buffers.__music; s.loop = true; s.connect(g);
    s.start(at0); s.stop(at0 + secs);
  }
  if (sfx) {
    for (const sc of SCENES) {
      if (!sc.sfx || !buffers[sc.sfx]) continue;
      const s = ctx.createBufferSource();
      s.buffer = buffers[sc.sfx];
      const g = ctx.createGain(); g.gain.value = 0.9;
      s.connect(g); g.connect(ctx.destination);
      s.start(at0 + sc.at / 1000);
    }
  }
}

/* Renders the soundtrack offline and hands back a WAV. Page recording captures PICTURE ONLY and
   always produces a silent file — this is the other half, and render.mjs muxes the two. */
window.renderWav = async (opts = {}) => {
  const secs = DURATION / 1000 + 0.35;
  const off = new OfflineAudioContext(2, Math.ceil(44100 * secs), 44100);
  await decodeAll(off);
  schedule(off, 0, opts);
  const buf = await off.startRendering();
  return wavBase64(buf);
};
function wavBase64(buf){
  const n = buf.length, ch = buf.numberOfChannels, rate = buf.sampleRate;
  const out = new DataView(new ArrayBuffer(44 + n * ch * 2));
  const str = (o, s) => { for (let i=0;i<s.length;i++) out.setUint8(o+i, s.charCodeAt(i)); };
  str(0,'RIFF'); out.setUint32(4, 36 + n*ch*2, true); str(8,'WAVEfmt ');
  out.setUint32(16,16,true); out.setUint16(20,1,true); out.setUint16(22,ch,true);
  out.setUint32(24,rate,true); out.setUint32(28,rate*ch*2,true);
  out.setUint16(32,ch*2,true); out.setUint16(34,16,true);
  str(36,'data'); out.setUint32(40, n*ch*2, true);
  let o = 44;
  for (let i=0;i<n;i++) for (let c=0;c<ch;c++) {
    const v = Math.max(-1, Math.min(1, buf.getChannelData(c)[i]));
    out.setInt16(o, v < 0 ? v*0x8000 : v*0x7fff, true); o += 2;
  }
  let bin = ''; const b = new Uint8Array(out.buffer);
  for (let i=0;i<b.length;i++) bin += String.fromCharCode(b[i]);
  return btoa(bin);
};

let live = null;
window.__start = () => {
  window.__done = false;
  const marker = document.querySelector('.marker');
  const bar = document.querySelector('.progress');

  /* The marker is painted BEFORE the first scene and removed one frame later, so the recording
     carries an unmistakable t=0 that both sides agree on. */
  const begin = () => {
    marker.classList.remove('on');
    const t0 = performance.now();

    /* The browser refuses audio until a gesture, so a piece that autoplays on open is silent.
       In render mode the sound is produced offline anyway and nothing is played here. */
    if (!isRender) {
      live = live || new (window.AudioContext || window.webkitAudioContext)();
      live.resume().then(() => decodeAll(live).then(() => schedule(live, live.currentTime)));
    }

    SCENES.forEach((s, i) => setTimeout(() => window.__showScene(i), s.at));
    const tick = () => {
      const p = Math.min(1, (performance.now() - t0) / DURATION);
      bar.style.width = (p * 100) + '%';
      if (p < 1) requestAnimationFrame(tick); else window.__done = true;
    };
    requestAnimationFrame(tick);
  };

  /* MARKER_MS of solid magenta before anything else, so the recorder has several unmistakable
     frames to find. Cutting from the end instead is what this replaces: the recorder keeps
     running after the piece finishes, and that tail varies run to run. */
  if (isRender) {
    marker.classList.add('on');
    setTimeout(begin, window.__startMarker);
  } else {
    requestAnimationFrame(() => requestAnimationFrame(begin));
  }
};

function fit(){
  const s=document.querySelector('.stage'), a=document.querySelector('.art');
  const k=Math.min(1,(window.innerWidth-40)/a.offsetWidth,(window.innerHeight-140)/a.offsetHeight);
  s.style.transform='scale('+k+')'; s.style.height=(a.offsetHeight*k)+'px';
}
/* In render and slideshow modes the stage must sit at NOMINAL size: the recorder captures the
   whole window, so a stage scaled to 94% is stored as a smaller piece inside a black frame — and
   on a piece that is already black, nobody sees it. */
if (isRender || isSlides) { document.querySelector('.stage').classList.add('no-scale'); }
else { addEventListener('resize', fit); fit(); }

document.fonts.ready.then(() => {
  window.__showScene(0);
  window.__ready = true;
  /* Autoplay on open, EXCEPT under ?render=1 where render.mjs decides the moment, so the
     recording and the soundtrack share one origin. */
  if (!isRender && !isSlides) window.__start();
});
</script>
</body></html>`;
}

/* ── go ────────────────────────────────────────────────────────────────────── */
mkdirSync(OUT, { recursive: true });

const written = new Set();
for (const piece of stills) {
  const name = `${piece.id}.html`;
  writeFileSync(join(OUT, name), stillHtml(piece), 'utf8');
  written.add(name);
  console.log(`${name.padEnd(40)} ${BOARDS[piece.board].where.padEnd(28)} ${sizeKb(join(OUT, name))} KB`);
}
if (video) {
  const name = `${video.id}.html`;
  writeFileSync(join(OUT, name), videoHtml(video), 'utf8');
  written.add(name);
  console.log(`${name.padEnd(40)} ${FORMATS[video.format].where.padEnd(28)} ${sizeKb(join(OUT, name))} KB`);
}

// Orphans: a piece removed from content.mjs must not linger and get published by accident.
// Files starting with `_` are the campaign's own tools and are spared, the way the contact sheet is.
for (const f of readdirSync(OUT)) {
  if (f.endsWith('.html') && !f.startsWith('_') && !written.has(f)) {
    rmSync(join(OUT, f));
    console.log(`removed orphan ${f}`);
  }
}

console.log(`\n${written.size} pieces in ${OUT}`);

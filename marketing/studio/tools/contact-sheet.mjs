// Builds one page showing every exported piece of a campaign side by side, as thumbnails.
//
//   node tools/contact-sheet.mjs <campaign-slug>
//
// Writes out/<slug>/_contact-sheet.html. Open it.
//
// This is where the defects a single piece hides become obvious: the same background used twice
// in a series, one piece far darker than its neighbours, six pieces that all framed the art the
// same way, a headline that is fine alone and repeats a word from the piece next to it. You do not
// see any of that reviewing pieces one at a time, which is how everyone reviews them.
//
// It also doubles as the thumbnail test still.md asks for: a headline that stops working at
// 150px wide is a headline that will not work in a feed either.

import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const slug = process.argv[2];
if (!slug) throw new Error('usage: node tools/contact-sheet.mjs <campaign-slug>');

const ROOT = join(here, '..', 'out', slug);
if (!existsSync(ROOT)) throw new Error(`nothing exported yet under ${ROOT}`);

const groups = readdirSync(ROOT, { withFileTypes: true })
  .filter((d) => d.isDirectory() && !d.name.startsWith('.'))
  .map((d) => ({
    name: d.name,
    files: readdirSync(join(ROOT, d.name)).filter((f) => f.endsWith('.png')).sort(),
  }))
  .filter((g) => g.files.length);

if (!groups.length) throw new Error(`no PNG found under ${ROOT}`);

const OUT = join(ROOT, '_contact-sheet.html');

/* Images are referenced by RELATIVE PATH, not embedded. A contact sheet is a local review tool
   that is never published, so the reasons a published piece must be self-contained do not apply,
   and inlining forty PNG would make a file too large to open comfortably. */
const html = `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>${slug} · contact sheet</title>
<style>
  body { background:#0b0b12; color:#e9e9f2; font:14px/1.5 ui-monospace,Menlo,Consolas,monospace; margin:0; padding:28px; }
  h1 { font-size:16px; letter-spacing:.16em; text-transform:uppercase; color:#ffa214; margin:0 0 4px; }
  h2 { font-size:12px; letter-spacing:.16em; text-transform:uppercase; color:#8f9bb3; margin:32px 0 12px; }
  .note { color:#8f9bb3; margin:0 0 8px; }
  .grid { display:flex; flex-wrap:wrap; gap:18px; align-items:flex-start; }
  figure { margin:0; width:220px; }
  figure img { width:100%; height:auto; display:block; background:#000; border:1px solid #2b2f45; }
  figcaption { font-size:11px; color:#8f9bb3; margin-top:6px; word-break:break-all; }
  .tiny .grid { gap:10px; }
  .tiny figure { width:150px; }
  label { display:inline-flex; gap:8px; align-items:center; color:#8f9bb3; cursor:pointer; }
</style></head>
<body>
<h1>${slug} · contact sheet</h1>
<p class="note">Look for: a background used twice, one piece darker than the rest, the same framing
every time, a word repeated on neighbouring pieces.</p>
<label><input type="checkbox" onchange="document.body.classList.toggle('tiny',this.checked)">
Shrink to 150px — if a headline stops reading here, it will not read in a feed either.</label>
${groups
  .map(
    (g) => `<h2>${g.name} · ${g.files.length}</h2>
<div class="grid">${g.files
      .map(
        (f) =>
          `<figure><img src="${relative(ROOT, join(ROOT, g.name, f)).split('\\').join('/')}" alt="${f}"><figcaption>${f}</figcaption></figure>`,
      )
      .join('')}</div>`,
  )
  .join('\n')}
</body></html>`;

mkdirSync(ROOT, { recursive: true });
writeFileSync(OUT, html, 'utf8');
console.log(`${groups.reduce((n, g) => n + g.files.length, 0)} pieces\n${OUT}`);

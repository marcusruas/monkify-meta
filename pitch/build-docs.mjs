// Build customer-facing PDFs from the Markdown deliverables, using md-to-pdf + a Chromium browser.
//
// Per supplier:  commercial-proposal.pdf, nda.pdf, speaker-notes.pdf, and
//                technical-integration.pdf (supplier overview + the shared Operator Integration Guide).
// Shared:        foundation/operator-integration-guide.pdf (send to an operator on its own).
//
// Requirements:  Node.js, `md-to-pdf` (npm i md-to-pdf), and Edge/Chrome.
// Run:           node build-docs.mjs            (all suppliers)
//                node build-docs.mjs softswiss  (one supplier; the shared guide is always rebuilt)
// Browser:       auto-detects Edge/Chrome; override with CHROME_PATH. If md-to-pdf is installed
//                elsewhere, set MD_TO_PDF_DIR=<node_modules-parent> (ESM ignores NODE_PATH).

import { createRequire } from 'node:module';
import { readFileSync, existsSync, readdirSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { dirname, join } from 'node:path';

const requireFrom = createRequire(
  process.env.MD_TO_PDF_DIR ? pathToFileURL(join(process.env.MD_TO_PDF_DIR, 'noop.js')) : import.meta.url,
);
const { mdToPdf } = requireFrom('md-to-pdf');

const HERE = dirname(fileURLToPath(import.meta.url)); // pitch/
const CSS = join(HERE, 'assets', 'theme', 'doc.css');
const OPERATOR_GUIDE = join(HERE, 'foundation', 'operator-integration-guide.md');

const BROWSERS = [
  process.env.CHROME_PATH,
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
].filter(Boolean);
const EXECUTABLE = BROWSERS.find((p) => existsSync(p));
if (!EXECUTABLE) { console.error('No Edge/Chrome found. Set CHROME_PATH.'); process.exit(1); }

const PDF_OPTS = {
  format: 'A4',
  printBackground: true,
  margin: { top: '20mm', bottom: '18mm', left: '18mm', right: '18mm' },
};
const launch = { executablePath: EXECUTABLE, args: ['--no-sandbox'] };
const rel = (p) => p.replace(HERE + '\\', '').replace(HERE + '/', '');

async function toPdf(content, dest) {
  await mdToPdf({ content }, { dest, stylesheet: [CSS], launch_options: launch, pdf_options: PDF_OPTS });
  console.log('  ->', rel(dest));
}
const read = (p) => readFileSync(p, 'utf8');
const PAGE_BREAK = '\n\n<div class="page-break"></div>\n\n';

console.log('Browser:', EXECUTABLE);

// Shared: the standalone Operator Integration Guide (send to an operator by itself).
console.log('==> foundation');
await toPdf(read(OPERATOR_GUIDE), join(HERE, 'foundation', 'operator-integration-guide.pdf'));
// INTERNAL reference (Marcus only — do NOT send): the alternative commercial models.
const ALTS = join(HERE, 'foundation', 'commercial-alternatives-internal.md');
if (existsSync(ALTS)) await toPdf(read(ALTS), join(HERE, 'foundation', 'commercial-alternatives-internal.pdf'));

const suppliersDir = join(HERE, 'suppliers');
const argSlugs = process.argv.slice(2);
const slugs = argSlugs.length ? argSlugs
  : readdirSync(suppliersDir).filter((d) => existsSync(join(suppliersDir, d, 'commercial-proposal.md')));

for (const slug of slugs) {
  const dir = join(suppliersDir, slug);
  console.log('==>', slug);
  const proposal = join(dir, 'commercial-proposal.md');
  const nda = join(dir, 'nda.md');
  const notes = join(dir, 'speaker-notes.md');
  const overview = join(dir, 'technical-integration.md');

  if (existsSync(proposal)) await toPdf(read(proposal), join(dir, 'commercial-proposal.pdf'));
  if (existsSync(nda)) await toPdf(read(nda), join(dir, 'nda.pdf'));
  if (existsSync(notes)) await toPdf(read(notes), join(dir, 'speaker-notes.pdf'));

  // technical-integration.pdf = supplier overview + the shared Operator Integration Guide
  const techParts = [];
  if (existsSync(overview)) techParts.push(read(overview));
  techParts.push(PAGE_BREAK + read(OPERATOR_GUIDE));
  await toPdf(techParts.join('\n'), join(dir, 'technical-integration.pdf'));
}
console.log('Done.');
process.exit(0);

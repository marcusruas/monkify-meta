# Monkify — Sales Pitch Kit

Everything needed to pitch Monkify for **licensing / sale to game suppliers, aggregators, and crypto
casinos**. All deliverables are **Markdown-backed** so they can be regenerated/updated by AI; decks
compile to `.pptx` via Marp. Source of the target list: the two supplier research files in
[`attachments/`](attachments/).

> **Confidential.** Everything here is shared with prospects only **under NDA** and only after **owner
> sign-off**. Draft legal/commercial docs are marked "draft — not legal advice."

## Folder structure

```
pitch/
├── README.md                     ← you are here (master index + how-to-regenerate)
├── build-decks.sh                ← build every supplier deck.md → deck.pptx (Marp)
├── build-docs.mjs                ← build proposal/nda/technical PDFs per supplier (md-to-pdf)
├── attachments/                  ← the two source supplier lists (research input)
├── assets/
│   ├── media/                    ← curated game media (real screenshots incl. winner + animation stills)
│   │   └── raw/                  ← raw Selenium captures
│   └── theme/
│       ├── monkify.css           ← Marp DECK theme (retro-arcade identity)
│       └── doc.css               ← light DOCUMENT theme (PDFs)
├── foundation/                   ← SHARED, supplier-agnostic assets
│   ├── operator-integration-guide.md  ← OPERATOR tech-team guide (→ .pdf); the integration handbook
│   ├── commercial-pricing-baseline.md      ← INTERNAL: public offer + negotiation room
│   ├── commercial-alternatives-internal.md ← INTERNAL: the other 2 models (exclusivity, full-code) — consult only
│   └── templates/                ← deck / speaker-notes / nda / commercial-proposal / email templates
└── suppliers/                    ← ONE folder per supplier (personalized set: deck+notes, NDA, proposal, email, PDFs)
    ├── playtech/                 ← reference set 1 — regulated-only track ✅
    └── softswiss/                ← reference set 2 — unified crypto+regulated track ✅
```

## The two pitch tracks

Each supplier gets one of two tracks (decided from its crypto posture in the research files):

- **Regulated-only** (supplier does **not** support crypto) — off-chain USD settlement only.
  Reference: [`suppliers/playtech/`](suppliers/playtech/).
- **Unified** (supplier **supports crypto**) — states Monkify supports **both** regulated
  (operator-wallet USD) **and** crypto (on-chain Solana) models. Reference:
  [`suppliers/softswiss/`](suppliers/softswiss/).

## Per-supplier deliverable set

Every `suppliers/<slug>/` folder contains:

| File | What |
| --- | --- |
| `deck.md` → `deck.pptx` | The pitch deck (20–30 min). **Minimal / topics-only, non-technical** — the deck shows topics; the talking points live in `speaker-notes.md`. Markdown is the source; pptx is generated. |
| `speaker-notes.md` → `.pdf` | The **full talking script** Marcus reads while presenting (per slide, with the numbers not printed on the deck). |
| `technical-integration.md` → `.pdf` | Supplier-facing overview; the PDF bundles it with the shared **Operator Integration Guide**. Clean (no internal repo/code refs). |
| `nda.md` → `.pdf` | Mutual NDA, **governing law = the supplier's HQ country**. Pitch-stage (pre-disclosure) mutual NDA. |
| `commercial-proposal.md` → `.pdf` | **Single recommended offer** (fixed setup/monthly + rev-share + SLAs), open to negotiation. |
| `email.md` | Outreach subject + body, to the supplier's **verified** intake address. |
| `README.md` | Supplier facts, track, recommended offer, hooks. |

**Customer-sendable PDFs** — `commercial-proposal.pdf`, `nda.pdf`, `speaker-notes.pdf`, and a combined
`technical-integration.pdf` (supplier overview + the shared Operator Integration Guide) — are generated
per supplier via `build-docs.mjs` (below), alongside the standalone
`foundation/operator-integration-guide.pdf`.

## Shared foundation (reused by every supplier)

- **[`foundation/operator-integration-guide.md`](foundation/operator-integration-guide.md)** →
  `.pdf` — the **operator tech-team** handbook: how an operator sends us their wallet API / RG check /
  auth key, the endpoint contracts, our game API, iframe embedding, and a "what to send us" checklist.
  Self-contained and clean (no internal repo/code references). Bundled into each supplier's technical
  PDF **and** available standalone to hand to an operator.
- **[`foundation/commercial-pricing-baseline.md`](foundation/commercial-pricing-baseline.md)** —
  **INTERNAL only.** The public offer (fixed) + the **negotiation room** behind it. Never sent.
- **[`foundation/commercial-alternatives-internal.md`](foundation/commercial-alternatives-internal.md)**
  → `.pdf` — **INTERNAL only.** The full **exclusivity** and **full-code sale** proposals, kept for
  Marcus to consult and raise verbally. The PDF is marked internal-do-not-send. Never sent.
- **[`foundation/templates/`](foundation/templates/)** — the deck / **speaker-notes** / NDA /
  commercial-proposal / email templates each supplier set is instantiated from.

## How to (re)generate the decks

**Requirements:** Node.js + npx, and a Chromium browser (Edge or Chrome) for pptx export.

```bash
# from pitch/
./build-decks.sh                    # build all suppliers/*/deck.pptx
./build-decks.sh suppliers/playtech # build one
# override the browser if needed:
CHROME_PATH="/path/to/msedge.exe" ./build-decks.sh
```

The decks reference local images (theme + media) — the script passes `--allow-local-files`. Edit any
`deck.md` and rebuild; the Markdown stays the source of truth.

> The YAML front matter (`marp: true`, `theme: monkify`, …) **must** be the very first thing in each
> `deck.md`. Do not put a comment above it or Marp renders it as text.

## How to (re)generate the customer PDFs

**Requirements:** Node.js, `md-to-pdf` (`npm i md-to-pdf`), and a Chromium browser (Edge/Chrome).

```bash
# from pitch/ — builds proposal.pdf, nda.pdf, technical-integration.pdf per supplier
node build-docs.mjs                 # all suppliers
node build-docs.mjs softswiss       # one
# if md-to-pdf is installed elsewhere: MD_TO_PDF_DIR=/path/to/node_modules-parent node build-docs.mjs
```

PDFs use the light document theme `assets/theme/doc.css`. The technical PDF bundles the supplier
overview + the 6 foundation docs into one file.

## Before anything is sent externally

Monkify-side identity is **filled in**: Marcus Vinicius Ruas de Andrade / **M V R Programação Software**
(CNPJ 46.358.140/0001-37, Gravataí–RS, Brazil); sender **Marcus Ruas · marcusruas.andrade@gmail.com**.
Only the **counterparty** `[brackets]` remain in each NDA (their registered office, effective date,
signatory) — complete and verify those before executing.

## Positioning rules (baked into every deliverable)

- **Certification — transparent.** Monkify is **provably fair by design and independently verifiable**,
  but **not yet lab-certified** (GLI/BMM). We say so plainly, and the **setup fee funds certification**.
  Do **not** claim "cert-ready".
- **Crypto — Solana today.** We settle crypto in **Solana** tokens now, and are **open to adding more
  tokens/chains** as partners need them.
- **Commercial docs show one model only** (the recommended SaaS licence) with **fixed** values and an
  "open to negotiation" note. The alternative models + negotiation ranges live **internally** in
  `foundation/commercial-pricing-baseline.md` and are handled verbally.
- **Decks are non-technical and topics-only** (sales audience) — no technologies/architecture on slides.
- **Never externalised** (internal only): team size, any cash constraint, the "a supplier might build it
  in-house" concern.
- Revenue share is quoted on **GGR** (industry-standard base); NGR framing only on request.

## Media provenance

Real screenshots — including the **winner/payout** shot — were captured from the **live local build**
(`setup_local_environment` + Selenium at `localhost:6003`); mascot/FX stills come from the
`Monkify animations/` asset set. See [`assets/media/`](assets/media/).

## Status

| | |
| --- | --- |
| Foundation (pricing, technical pack, templates, theme, build tooling) | ✅ done |
| Reference set — **Playtech** (regulated-only) | ✅ done |
| Reference set — **SOFTSWISS** (unified) | ✅ done |
| Remaining suppliers (fan-out after sign-off) | ⏳ pending owner approval of the two references |

**Suppliers still to produce** (≈19, after the two reference templates are approved): from
`attachments/game-suppliers.md` — SoftGamings, Hub88, Pariplay, Slotegrator, Bragg, EveryMatrix, 1X2
Network, Games Global, Relax Gaming, Yggdrasil; from `attachments/crypto-suppliers-and-casinos.md` —
Roobet, BC.Game, TrustDice, BGaming, Winz.io, Stake Engine, BetConstruct, Groove, Gamdom. (Hub88,
SOFTSWISS, Slotegrator appear in both lists — one unified set each.)

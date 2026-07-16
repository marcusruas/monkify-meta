# Feature — Landing page: ChimpOut token-free relaunch

> Status: **shipped.** Authoritative spec: `../specs/lp-chimpout-relaunch.md`
> (merged via `feature/rebranding-chimpout` on 2026-07-16; `monkify-lp` → `master`,
> `monkify-meta` → `main`).

## What it does

`monkify-lp` is the public marketing landing page (Astro 5 + Vue 3 + Tailwind v4, static). It now
presents **ChimpOut** as a **token-free launch**: players bet **real USDC** on what Edson the space
chimp types next — provably fair, instant on-chain (Solana) payouts, auto-refunds — with **no house
coin to buy**. Audience: pre-launch degen/arcade players. The page is static and only deep-links into
the game app via the "Play now" URL. This pass finished the LP work the rebrand (#49) deferred, on top
of the arcade-overhaul redesign.

## Core rules (current LP content)

- **Brand:** every user-visible surface reads **ChimpOut** — wordmark **CHIMP·OUT** ("OUT" in primary
  orange `#ffa214`), `<title>`/meta, header, footer. No "Monkify"/"MONKIFY"/"$MONKIFY" anywhere.
- **USDC is the stake** wherever money is named; no token is offered as currency.
- **No token to buy.** The old Tokenomics section is gone, replaced by a short honest **"No token.
  Yet."** note (`#token`): nothing to buy now, a token *may* come later (ticker/date TBD — **never** a
  ticker or contract address), plus an anti-scam line (a "$CHIMPOUT" for sale today is not us).
- **Trust points** are all stated: provably-fair per-round seed, **instant on-chain (Solana)
  payouts**, **auto-refund** of bad bets.
- **Roadmap** perks are future/earned-by-playing (no "$MONKIFY holders" gating; phase 1 does **not**
  claim "$MONKIFY live"); one light "a token maybe later / early players first" tease.
- **CTAs:** primary **"Play now"** → the app launch URL; secondary **"Join the troop"** → `#community`.
  There is **no "Buy" button** anywhere (desktop or mobile); the nav item "Token" points at the note.
- **Ship-blocking placeholders (must be filled before the real public launch):** the app URL
  (`PLAY_URL = https://PLACEHOLDER-app-url/chimpout/game` — real route confirmed, **domain
  undecided**) and the Twitter/Telegram/Discord links (`#`). Both carry a `// TODO(ship)` in source.
- Edson, all art/sprites, the 6-slide story, and the retro/space/arcade skin are **unchanged** from the
  arcade-overhaul redesign this shipped on top of. Verification is `npm run build` + browser QA (no
  test suite); the built site contains no user-visible `monkify`/`$MONKIFY`.

## Key code touchpoints (`monkify-lp` on `master`)

- `src/pages/index.astro` — hero (wordmark, `PLAY_URL`, trust chips, coin `alt`), how-it-works (USDC
  steps, 3-point trust line), the `#token` "no token (yet)" note, `#community` + team.
- `src/components/Header.vue` — CHIMP·OUT wordmark, "Token" nav → `#token`, no "Buy" (desktop + mobile).
- `src/components/SpaceRoadmap.vue` — 4 phases; de-tokenized perks + future-token tease.
- `src/components/Footer.vue` — CHIMP·OUT + "© 2026 CHIMPOUT".
- `src/layouts/Layout.astro` — token-free `<title>`/description; scroll-reveal + `prefers-reduced-motion` script.
- `src/styles/global.css` — Tailwind v4 `@theme` tokens (orange `#ffa214`, cyan/magenta, Press Start 2P + VT323, arcade-panel/scanlines).

## Not covered / follow-ups

- A **full visual redesign** of the LP (keep the sprites + product story, reimagine
  layout/composition/motion) is a separate follow-up — an art change, to be run as its own card off
  `master`.
- Filling the flagged placeholders (real app **domain** + social URLs) before the public launch.
- The LP repo's own `PRODUCT.md` / `CLAUDE.md` still describe the old memecoin product (out of scope).

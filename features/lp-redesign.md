# Feature — Landing page: visual redesign (arcade-cabinet)

> Status: **shipped.** Authoritative spec: `../specs/lp-redesign.md`
> (merged via `feature/lp-redesign` on 2026-07-19; `monkify-lp` → `master` as `5a5003d Arcade-cabinet
> LP redesign`, `monkify-meta` KB → `main`).

## What it does

A **selective visual revamp** of the shipped SpaceStroker landing page (`monkify-lp`, Astro 5 + Vue 3 +
Tailwind v4, static) — cherry-picking the winning pieces of a Claude design mock onto the live LP,
re-implemented in the real Astro/Vue components and staying **inside the existing arcade skin** (the
Tailwind v4 `@theme` tokens; no palette/font drift). Frontend-only; no backend / game-app / DB change.
It restructures and restyles several sections while **preserving** the SpaceStroker brand, the token-free
USDC messaging, the three trust points, and the ship placeholders documented in
`features/lp-space-stroker-relaunch.md`. Audience: pre-launch degen/arcade players.

## Core rules (current LP structure)

- **Section order:** hero → `#about` (how-it-works) → `#modes` ("SELECT YOUR GAME", **new**) → `#story`
  → `#token` → `#roadmap` → `#community`.
- **Hero is two-column:** copy + CTAs + a cosmetic **LIVE typing pill** on the left; a glowing Edson
  coin + a separate mascot chimp (`edson.png`) + a drifting comet on the right. Stacks to one column on
  mobile.
- **The LIVE typing pill** (`● LIVE · EDSON'S TYPING xxxxx`) is **purely cosmetic** — locally-generated
  random characters on a timer, `● LIVE` in the magenta live-pulse accent. It makes **no win/pot/money
  claim and issues no network request**; `prefers-reduced-motion` freezes it to a static string.
- **How-it-works** renders three horizontal `01/02/03` step cards followed by a **full-width FAIR PLAY
  bar** stating all three trust points (provably-fair, instant on-chain Solana payouts, auto-refund).
- **`#modes` "SELECT YOUR GAME"** is its own image-forward section (Races / Beast Mode / Lotteries)
  where the mode artwork is the visual focus — a responsive grid, deliberately **not** a tabbed
  one-at-a-time layout.
- **Story** keeps the EDSON.LOG CRT terminal (typed caption, `CH n/6`, PREV/NEXT + dots) wrapping
  Bolso's painted `part1–6` slides — **restyled, not rebuilt**.
- **`#token` "No token. Yet."** is a two-column card (heading + body left; "$SPACESTROKER is not us"
  anti-scam warning + "Join the troop" CTA right); the token-free copy is unchanged in meaning.
- **Header:** coin icon + SPACE·STROKER wordmark, nav **Modes / Story / Roadmap / Troop**, and a **PLAY
  NOW** button → `PLAY_URL`. The nav intentionally drops the standalone "Token"/"About" slots; those
  sections stay scroll-reachable (a redesign choice, not a regression of the `#token` note).
- **Footer** shows the **"BET ON THE MONKEY"** tagline beside the wordmark.
- **Roadmap** keeps the `SpaceRoadmap` star-map + phase titles; the phase-detail panel shows short
  **no-emoji** items with **no per-phase perk box**.
- **Richer per-section space art:** slowly-drifting sprites (planets / black hole / satellites /
  asteroids / comets, from the existing `src/assets/elements/*` set) sit behind every section, not just
  the hero. **No** wins/pot ticker or fabricated live-activity numbers appears anywhere.
- **Preserved / must not regress:** the SPACE·STROKER brand (no user-visible "Monkify"/"$MONKIFY"),
  token-free USDC + the anti-scam line, the three trust points, and the `PLAY_URL` + social `#`
  placeholders (still carrying `// TODO(ship)`; the app **domain** and social URLs are unfilled).
- Every new animation honors `prefers-reduced-motion`, and every adopted section holds on portrait +
  landscape phones. Verification is `npm run build` clean + browser QA (the LP has no test suite); the
  built site contains no user-visible `monkify`/`$MONKIFY`.

## Key code touchpoints (`monkify-lp` on `master`)

- `src/pages/index.astro` — two-column hero, `#about` step cards + full-width FAIR PLAY bar, the new
  `#modes` section, the two-column `#token` card, per-section art wiring; `PLAY_URL` placeholder kept.
- `src/components/Header.vue` — coin + wordmark, Modes/Story/Roadmap/Troop nav, PLAY NOW button.
- `src/components/HeroTypingPill.vue` — the cosmetic, local LIVE typing pill.
- `src/components/GameModes.vue` — image-forward modes panel (art the focus).
- `src/components/StoryCarousel.vue` — restyled EDSON.LOG terminal (painted slides kept).
- `src/components/SpaceRoadmap.vue` — simplified no-emoji items; perk box removed.
- `src/components/SectionSpaceArt.vue` + `src/components/ContentSection.vue` — per-section drifting
  scenery behind content.
- `src/components/Footer.vue` — "BET ON THE MONKEY" tagline.
- `src/styles/global.css` — reuses the Tailwind v4 `@theme` tokens + arcade component classes.

## Not covered / follow-ups

- Filling the ship placeholders (real app **domain** + social URLs) before the public launch — still
  open (`// TODO(ship)`).
- **Post-ship polish (2026-07-19, `monkify-lp` `master` `6cc713a`, beyond this card's acceptance
  criteria):** the per-section backdrops were refined — each content section now carries a distinct
  nebula tint in a one-way blue→red journey with per-section bloom patterns/strengths, and the drifting
  scenery gained depth-based scroll parallax (`SectionSpaceArt.vue`, `global.css`
  `.nebula-field--tinted` / `.neb-p*`).
- The LP repo's stale `PRODUCT.md` / `CLAUDE.md` (still describe the old memecoin product) — out of
  scope.

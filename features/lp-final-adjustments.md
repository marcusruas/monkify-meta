# Feature — LP final adjustments (pre-launch polish)

> Status: **shipped.** Authoritative spec: `../specs/lp-final-adjustments.md`
> (merged via `feature/lp-final-adjustments` on 2026-07-22). **Frontend-only, `monkify-lp` only.**

## What it does

A pre-launch honesty + polish pass on the Space Stroker landing page (`monkify-lp`, on `master`). The
three game-mode cards now match the real seeded **Main** production game types, carry final art, and
the launch/social links + LP domain are real (no more placeholders). No backend, no game app change.

## Core rules

- **The three mode cards map 1:1 to the seeded Main types** (source of truth:
  `monkify-backend` `SpaceStroker.Consumers/DefaultGameParameters.cs` → `GenerateMainDefaultParameters`),
  and their copy must not contradict them:
  - **Cosmic Sprint** (= 6X Main Sprint) — one **6-character alphanumeric** race (A–Z + 0–9); tag
    `6 CHARS · A–Z + 0–9`. Never "6 letters / 6 numbers" (there is no letters-only or numbers-only variant).
  - **Beast Mode** (= 6X Main Derby) — **back 1 of 8** preset runners; blurb says "1 of 8 beasts".
    Never the invented "rabbit / monkey / jaguar / falcon" set.
  - **Space Jackpot** (= 6X Main Millions) — lottery drawn **once a day (24h)**, pot grows all day;
    tag `DAILY DRAW · 24H`. Never "every 4h" (the 4h lotteries are DEV-only defaults, not Main).
- **Mode art:** each card has its own animated GIF with a static first-frame poster (poster shown at
  rest, GIF on hover/active, static under `prefers-reduced-motion`). Scene art that carries its own
  background (Cosmic Sprint, Space Jackpot) fills the square frame edge-to-edge (`object-cover`; Space
  Jackpot uses `object-top` so the character's head isn't cropped); the transparent Beast character
  stays contained + bottom-anchored. All three cards share one uniform square art frame.
- **Launch + social links are real** (no longer `TODO(ship)` placeholders):
  - App URL (`PLAY_URL`, used by every "Play now" CTA): `https://app.spacestroker.com/space-stroker/game`.
  - Socials: **X** `https://x.com/SpaceStroker`, **Telegram** `https://t.me/spacestroker`. **Discord
    was removed** (the socials grid is 2-up). LP self-domain `https://spacestroker.com` is set as the
    Astro `site` (for canonical/sitemap).
- **Brand:** the built site contains no user-visible `Monkify` / `$MONKIFY` (brand is **Space Stroker**);
  the `#token` panel keeps its final "No token. Yet." / dimmed-coin "TBD ticker" visual (its old
  `TODO(art)` markers are gone). Roadmap HI-SCORE banner + phases read as **labelled goals** ("HI-SCORE
  WE'RE CHASING"), never live metrics; phase-1 "all 3 modes live" is retained.
- **Community wording:** the nav label stays **"Community"**; "gang" appears in CTAs as intentional flavor.
- **No `/placeholder.png` fallback ever renders** — the `SpaceBackground.vue` spawn pool is always
  populated (`constants/space-elements.ts`), so the `el.src || '/placeholder.png'` branch is unreachable.
- **No tests:** the LP has no unit/integration suite and this is a frontend-only polish change; DoD =
  `npm run build` clean + browser QA (0 console errors) + brand sweep clean.

## Key code touchpoints (`monkify-lp`, on `master`)

- `src/components/GameModes.vue` — the three-card `modes` array (names/tags/blurbs), new GIF + poster
  imports, `cover`/`object-top` art handling, reduced-motion swap.
- `src/assets/cards/` — `cosmicsprint.gif` + `cosmicsprint-poster.png`, `spacejackpot.gif` +
  `spacejackpot-poster.png` (Beast keeps `beastmode.gif` / `beastmode-poster.png`).
- `src/pages/index.astro` — `PLAY_URL`, the `socials` array (X + Telegram, no Discord; `sm:grid-cols-2`),
  the `#token` visual (markers removed).
- `src/components/Header.vue` — `PLAY_URL`, logo `href="#hero"`.
- `astro.config.mjs` — `site: 'https://spacestroker.com'`.
- Audit-only (no change): `src/components/SpaceRoadmap.vue` (HI-SCORE goals framing),
  `src/components/SpaceBackground.vue` (`/placeholder.png` fallback unreachable).

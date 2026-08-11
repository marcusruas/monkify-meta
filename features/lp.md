# Domain — Landing page (`monkify-lp`)

> **What this covers.** The public marketing site at `spacestroker.com` — Astro 5 + Vue 3 + Tailwind v4,
> static, on branch **`master`** (not `main`). It only deep-links into the game app; there is no backend,
> DB or game surface here, and the repo has **no test suite** — DoD is `npm run build` clean + browser QA.
>
> Three passes built the current page: the token-free relaunch, the arcade-cabinet visual redesign, and
> the pre-launch polish. Deep design:
> [`../specs/lp-space-stroker-relaunch.md`](../specs/lp-space-stroker-relaunch.md) ·
> [`../specs/lp-redesign.md`](../specs/lp-redesign.md) ·
> [`../specs/lp-final-adjustments.md`](../specs/lp-final-adjustments.md).

## Positioning

Space Stroker is presented as a **token-free launch**: players bet **real USDC** on what Edson the space
chimp types next — provably fair, instant on-chain (Solana) payouts, auto-refunds — with **no house coin
to buy**. Audience: pre-launch degen/arcade players.

- **Brand:** every user-visible surface reads **Space Stroker** — wordmark **SPACE·STROKER** ("OUT" in
  primary orange `#ffa214`). The built site contains **no user-visible `Monkify` / `$MONKIFY`**.
- **USDC is the stake** wherever money is named; no token is offered as currency.
- **No token to buy.** The old Tokenomics section is gone, replaced by a two-column **"No token. Yet."**
  card (`#token`): nothing to buy now, a token *may* come later (⚠️ **never** a ticker or contract
  address), plus an anti-scam line — a "$SPACESTROKER" for sale today is not us.
- **Trust points**, all stated: provably-fair per-round seed, instant on-chain Solana payouts,
  auto-refund of bad bets.
- **Roadmap** perks are future/earned-by-playing — no holder gating, no "$MONKIFY live". The HI-SCORE
  banner and phases read as **labelled goals** ("HI-SCORE WE'RE CHASING"), never live metrics.
- ⚠️ **The house commission is stated by hand on the Terms page** (`src/pages/terms.astro`) and **must be
  updated with any rate change** — see [`money.md`](money.md). It was once found stating 0.5% after the
  rate moved to 1%.

## Page structure

Section order: hero → `#about` (how-it-works) → `#modes` → `#story` → `#token` → `#roadmap` →
`#community`.

- **Hero is two-column:** copy + CTAs + a cosmetic **LIVE typing pill** on the left; a glowing Edson coin
  + mascot chimp (`edson.png`) + a drifting comet on the right. Stacks to one column on mobile.
- ⚠️ **The LIVE typing pill is purely cosmetic** — locally-generated random characters on a timer. It
  makes **no win/pot/money claim and issues no network request**; `prefers-reduced-motion` freezes it to
  a static string.
- **How-it-works** = three `01/02/03` step cards + a full-width **FAIR PLAY** bar carrying all three trust
  points.
- **`#modes` "SELECT YOUR GAME"** is image-forward (art is the visual focus), a responsive grid —
  deliberately **not** a tabbed one-at-a-time layout.
- **Story** keeps the EDSON.LOG CRT terminal (typed caption, `CH n/6`, PREV/NEXT + dots) wrapping Bolso's
  painted `part1–6` slides — restyled, not rebuilt.
- **Header:** coin + wordmark, nav **Modes / Story / Roadmap / Troop**, **PLAY NOW** button. There is **no
  "Buy" button** anywhere. **Footer:** the **"BET ON THE MONKEY"** tagline beside the wordmark.
- **Per-section space art:** slowly-drifting sprites (planets / black hole / satellites / asteroids /
  comets) behind every section, with distinct nebula tints in a one-way blue→red journey and depth-based
  scroll parallax. ⚠️ **No wins/pot ticker or fabricated live-activity numbers appears anywhere.**
- Every animation honours `prefers-reduced-motion`, and every section holds on portrait + landscape
  phones.

## ⚠️ The three mode cards must match the seeded Main game types

Source of truth: `monkify-backend` `SpaceStroker.Consumers/DefaultGameParameters.cs` →
`GenerateMainDefaultParameters`. The copy must not contradict it.

| Card | Real type | Must say | Must never say |
| --- | --- | --- | --- |
| **Cosmic Sprint** | 6X Main Sprint | One **6-character alphanumeric** race (A–Z + 0–9); tag `6 CHARS · A–Z + 0–9` | "6 letters / 6 numbers" — there is no letters-only or numbers-only variant |
| **Beast Mode** | 6X Main Derby | **Back 1 of 8** preset runners | The invented "rabbit / monkey / jaguar / falcon" set |
| **Space Jackpot** | 6X Main Millions | Lottery drawn **once a day (24h)**; tag `DAILY DRAW · 24H` | "every 4h" — the 4h lotteries are DEV-only defaults, not Main |

**Mode art:** each card has its own animated GIF with a static first-frame poster (poster at rest, GIF on
hover/active, static under `prefers-reduced-motion`). Scene art carrying its own background (Cosmic
Sprint, Space Jackpot) fills the square frame edge-to-edge via `object-cover` — Space Jackpot uses
`object-top` so the character's head isn't cropped; the transparent Beast character stays contained and
bottom-anchored. All three share one uniform square frame.

## Links (all real — no placeholders left)

- **App URL** for every "Play now" CTA: `https://app.spacestroker.com`. ⚠️ The constant is **`GAME_URL`**
  in `src/constants.ts` and it is the **bare origin** — no `/space-stroker/game` path. Since the home
  dashboard dropped the route prefix, the origin resolves to the app's **starting page** rather than the
  bare lobby.
- **Socials:** X `https://x.com/SpaceStroker`, Telegram `https://t.me/spacestroker`. **Discord was
  removed** (the grid is 2-up). Nav label stays **"Community"**; "gang" appears in CTAs as flavour.
- **LP self-domain** `https://spacestroker.com` is the Astro `site` (canonical/sitemap).
- **No `/placeholder.png` fallback ever renders** — the `SpaceBackground.vue` spawn pool is always
  populated, so the `el.src || '/placeholder.png'` branch is unreachable.

## Key code touchpoints (`monkify-lp`, on `master`)

| File | Role |
| --- | --- |
| `src/pages/index.astro` | Hero, `#about` + FAIR PLAY bar, `#modes`, `#token` card, socials array, per-section art wiring |
| `src/pages/terms.astro` | ⚠️ The hand-written commission rate |
| `src/constants.ts` | `GAME_URL` (bare origin) |
| `src/components/Header.vue` · `Footer.vue` | Coin + wordmark + PLAY NOW · "BET ON THE MONKEY" |
| `src/components/HeroTypingPill.vue` | The cosmetic local LIVE pill |
| `src/components/GameModes.vue` | The three-card `modes` array, GIF + poster imports, `cover`/`object-top`, reduced-motion swap |
| `src/components/StoryCarousel.vue` | EDSON.LOG terminal (painted slides kept) |
| `src/components/SpaceRoadmap.vue` | Star map, no-emoji items, no perk box |
| `src/components/SectionSpaceArt.vue` · `ContentSection.vue` · `SpaceBackground.vue` | Drifting scenery, tints, parallax |
| `src/assets/cards/` | `cosmicsprint` / `beastmode` / `spacejackpot` GIF + poster pairs |
| `src/styles/global.css` · `astro.config.mjs` | Tailwind v4 `@theme` tokens + arcade classes · `site` |

## Known gaps

- The LP repo's own `PRODUCT.md` / `CLAUDE.md` still describe the **old memecoin product** — stale, and
  out of scope of every LP card so far.

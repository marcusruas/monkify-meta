# Domain — Landing page (`monkify-lp`)

> **What this covers.** The public marketing site at `spacestroker.com` — Astro 5 + Vue 3 + Tailwind v4,
> static, on branch **`master`** (not `main`). It only deep-links into the game app; there is no backend,
> DB or game surface here, and the repo has **no test suite** — DoD is `npm run build` clean + browser QA.
>
> Three passes built the current page: the token-free relaunch, the arcade-cabinet visual redesign, and
> the pre-launch polish. A fourth added the token surface. Deep design:
> [`../specs/lp-space-stroker-relaunch.md`](../specs/lp-space-stroker-relaunch.md) ·
> [`../specs/lp-redesign.md`](../specs/lp-redesign.md) ·
> [`../specs/lp-final-adjustments.md`](../specs/lp-final-adjustments.md) ·
> [`../specs/token-launch-ui.md`](../specs/token-launch-ui.md).

## Positioning

Players bet **real USDC** on what Edson the space chimp types next — provably fair, instant on-chain
(Solana) payouts, auto-refunds. Audience: pre-launch degen/arcade players.

⚠️ **"Token-free" is now a BUILD STATE, not the product's positioning.** The page has a full pump.fun
token surface behind `PUBLIC_TOKEN_MINT`; with that key empty it renders exactly the token-free page
described below. **The coin is never a stake** — rounds still settle in USDC and every token claim on
the page is information only. See [`token-launch.md`](token-launch.md).

- **Brand:** every user-visible surface reads **Space Stroker** — wordmark **SPACE·STROKER** ("OUT" in
  primary orange `#ffa214`). The built site contains **no user-visible `Monkify` / `$MONKIFY`**.
- **USDC is the stake** wherever money is named; **the coin is never offered as currency**, in either
  build state.
- **Pre-launch (`PUBLIC_TOKEN_MINT` empty): no token to buy.** The old Tokenomics section is gone,
  replaced by a two-column **"No token. Yet."** card: nothing to buy now, a token *may* come later
  (⚠️ **never** a ticker or contract address), plus an anti-scam line — a "$SPACESTROKER" for sale today
  is not us. It now lives in `TokenSection.vue` rather than inline in `index.astro`.
- **Post-launch (mint configured): the live token section replaces that card in the same slot.** Coin,
  ticker, copyable contract bar, two CTAs (Buy on pump.fun · Chart), two **future-tense** mechanic cards
  and the treasury block. ⚠️ **No total supply, decimals, mint authority or LP status** — that split is
  deliberate; the app's manual carries those figures instead. Rules in
  [`token-launch.md`](token-launch.md).
- **Trust points**, all stated: provably-fair per-round seed, instant on-chain Solana payouts,
  auto-refund of bad bets.
- **Roadmap** perks are future/earned-by-playing — no holder gating, no "$MONKIFY live". The HI-SCORE
  banner and phases read as **labelled goals** ("HI-SCORE WE'RE CHASING"), never live metrics.
  ⚠️ **Phase 5 must never claim the token is a stake** — it used to read *"the token stays your stake in
  all of it"* and was rewritten for exactly that reason.
- ⚠️ **The house commission is stated by hand on the Terms page** (`src/pages/terms.astro`) and **must be
  updated with any rate change** — see [`money.md`](money.md). It was once found stating 0.5% after the
  rate moved to 1%.

## Page structure

Section order: hero → **`#token`** → **`#feedbacker`** → `#about` (how-it-works) → `#modes` → `#story`
→ `#roadmap` → `#community`. ⚠️ **`#token` was 6th and is now 2nd**, in **both** build states.
**`#feedbacker` (Trello #173) is 3rd and renders only when the mint is set** — see
[`feedbacker.md`](feedbacker.md).

⚠️ **`ContentSection`'s backdrop props are POSITIONAL.** `hue` / `neb` / `pattern` / `nebOpacity` carry
the one-way blue→red journey down the page and **stay with the slot**; only `art` travels with its
section. Moving a section without rebalancing them puts two similar backdrops next to each other — the
`#token` promotion had to rebalance all six.

⚠️ **The `neb` hue sweep is now 248 → 263 → 278 → 305 → 332 → 8 → 42, and `pattern` maps 1–7.**
Inserting `#feedbacker` took the **midpoint** of its two neighbours (263, with `.neb-p7` newly authored
in `global.css`), which is the one insertion that keeps the journey one-way **without rebalancing any
existing slot** — none of the other six changed. A future insertion should do the same rather than
re-spreading the whole page. `#feedbacker` carries **`art=""`**: only the `token` scene has measured
phone placements, so a desktop-percentage scene would drop sprites onto the page's densest copy.

- **Hero is two-column:** copy + CTAs + a cosmetic **LIVE typing pill** on the left; a glowing Edson coin
  + mascot chimp (`edson.png`) + a drifting comet on the right. Stacks to one column on mobile. The
  secondary CTA is `Join the gang` pre-launch and `Buy $TICKER` once the mint is set.
- **The hero's arcade-cabinet HUD is TWO blocks, not one.** The decorative strings (`1P · READY`, plus
  `HI-SCORE` / `INSERT COIN` pre-launch only) stay inline in `index.astro`, `aria-hidden` and
  click-through. The three corners that hold **real controls** — pump.fun status, socials, contract bar —
  live in `HeroHud.vue`, which is ⚠️ **deliberately not `aria-hidden`** (a focusable control inside an
  aria-hidden subtree is Tab-reachable but invisible to a screen reader) and is ⚠️ **mounted after the
  hero copy** so a keyboard user reaches "Play now" before "copy the contract". Both blocks hide below
  `sm`, so nothing there may be the only route to its destination.
- ⚠️ **The LIVE typing pill is purely cosmetic** — locally-generated random characters on a timer. It
  makes **no win/pot/money claim and issues no network request**; `prefers-reduced-motion` freezes it to
  a static string.
- **How-it-works** = three `01/02/03` step cards + a full-width **FAIR PLAY** bar carrying all three trust
  points.
- **`#modes` "SELECT YOUR GAME"** is image-forward (art is the visual focus), a responsive grid —
  deliberately **not** a tabbed one-at-a-time layout.
- **Story** keeps the EDSON.LOG CRT terminal (typed caption, `CH n/6`, PREV/NEXT + dots) wrapping Bolso's
  painted `part1–6` slides — restyled, not rebuilt.
- **Header:** coin + wordmark, nav **Modes / Story / Roadmap / Troop**, **PLAY NOW** button. Once the
  mint is set, **`Token` is prepended as the first nav item and `Rewards` (→ `#feedbacker`) follows it
  as the second** (desktop + mobile both iterate one array, so gating them there covers both) and
  nothing else is renamed. ⚠️ The nav says **`Rewards`** while the anchor and the app's guide fragment
  are both `feedbacker` — the row is Press Start 2P and cannot fit the longer word; the shortening is
  the nav label only. **Footer:** the **"BET ON THE MONKEY"** tagline beside the wordmark.
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
- **Socials:** X `https://x.com/SpaceStroker`, Telegram `https://t.me/SpaceStrokerportal`, YouTube
  `https://www.youtube.com/@EdsonStroker`. **Discord was removed**; the grid is **3-up at `lg`** and
  stacked below it (three columns inside that panel below ~1024px wrap the "TELEGRAM" label). Nav label
  stays **"Community"**; "gang" appears in CTAs as flavour. ⚠️ **`SOCIALS` in `src/constants.ts` is THE
  list** — the hero HUD and the community slots both iterate it, each mark is **one path on a 24×24 box**
  so each consumer builds its own `<svg>`, and the array's **order and length** drive the community
  grid's column count.
  ⚠️ **The Telegram handle is a verification PORTAL bot, not the group** — the community now sits behind
  it, so `t.me/spacestroker` (the old direct group link, still recorded in the historical `INDEX.md`
  entry for the launch-links card) is dead everywhere. **Four** places carry it and they must move
  together: `SOCIALS` (hero HUD + community slot), the JSON-LD `sameAs` in `Layout.astro`, and the
  CONTACT links on **`terms.astro` and `responsible-gambling.astro`** — the legal pages also print the
  handle as **visible text**, so an href-only edit leaves the copy contradicting the link. Unrelated to
  the **Orbit** Telegram bot in `monkify-front`, whose `t.me/<bot>?start=<code>` deep link is built by
  the backend from `ORBIT_TELEGRAM_BOT_USERNAME` — a different bot that this change does not touch.
- **LP self-domain** `https://spacestroker.com` is the Astro `site` (canonical/sitemap).
- **No `/placeholder.png` fallback ever renders** — the `SpaceBackground.vue` spawn pool is always
  populated, so the `el.src || '/placeholder.png'` branch is unreachable.

## Key code touchpoints (`monkify-lp`, on `master`)

| File | Role |
| --- | --- |
| `src/pages/index.astro` | Hero + decorative HUD, `#token` slot, `#about` + FAIR PLAY bar, `#modes`, per-section art wiring |
| `src/pages/terms.astro` | ⚠️ The hand-written commission rate · the gated, deliberately **number-free** `REWARDS FOR FEEDBACK` clause ([`feedbacker.md`](feedbacker.md)) |
| `src/components/FeedbackerSection.vue` | The `#feedbacker` section — ⚠️ **static, no `client:*` directive** ([`feedbacker.md`](feedbacker.md)) |
| `src/icons.ts` · `PixelIcon.vue` | The drawn pixel marks. ⚠️ `▾`/`↓` are **outside** the self-hosted Press Start 2P subset and must be drawn; `·` and `–` **are** in range and stay characters |
| `src/constants.ts` | `GAME_URL` (bare origin), `SOCIALS`, and the whole token block — see [`token-launch.md`](token-launch.md) |
| `src/components/TokenSection.vue` · `HeroHud.vue` | The `#token` section (both states) · the interactive hero corners |
| `src/components/PixelText.vue` · `PumpMark.vue` · `PixelIcon.vue` · `src/icons.ts` | ⚠️ Press Start 2P's `$`/`!` are drawn wrong and glyphs like `●` fall outside the self-hosted subset — display copy goes through `PixelText`, marks are drawn geometry, never a bare character |
| `src/components/Header.vue` · `Footer.vue` | Coin + wordmark + PLAY NOW + gated `Token` nav item · "BET ON THE MONKEY" |
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

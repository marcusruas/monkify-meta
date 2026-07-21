# SpaceStroker — "Landing-page launch relaunch — SpaceStroker rebrand + token-free (USDC)" — Implementation Spec

> **Status: Shipped (2026-07-16)** — merged via `feature/rebranding-spacestroker` (`monkify-lp` → `master`, `monkify-meta` → `main`). Refined via Trello card
> [Revisar landing page para lançamento — rebrand para SpaceStroker + launch sem token (USDC)](https://trello.com/c/Y0PpEDii) (2026-07-16).
> Repos affected: **`monkify-lp`** (the only app repo touched) and this doc in `monkify-meta`.
> **Branch:** `feature/rebranding-spacestroker` (⚠️ reuses the pre-existing arcade-overhaul branch — see §4).
>
> **Purpose:** Turn the landing page into the real pre-launch surface for **SpaceStroker**: *play with
> real **USDC**, provably fair — there is **no coin to buy yet**, a token may come later.* This is a
> copy / structure / brand pass on the **arcade-overhaul LP redesign** (not an art change), finishing
> the LP work that the shipped rebrand (#49) deliberately deferred.

---

## 1. Summary

The app + knowledge base already shipped the **Monkify → SpaceStroker / Session → Game** rebrand
(#49, `feature/rebrand-chimpout`, merged). That rebrand **deferred all LP token/domain work** (its
§9.3): the LP still advertises a `$MONKIFY` memecoin — hero "Buy $MONKIFY", a full Tokenomics
section, `$MONKIFY`-holder-gated roadmap perks. This card finishes the LP for launch.

The launch is **token-free**: players bet **real USDC**, provably fair, instant on-chain (Solana)
payouts, auto-refund of bad bets — **no house coin to buy**. So the LP must:

| Area | Before (memecoin LP) | After (this card) |
| --- | --- | --- |
| Brand | `MONKIFY` wordmark, "$MONKIFY", "the MONKIFY coin" | **CHIMP**`OUT` wordmark ("OUT" orange), no "Monkify/$MONKIFY" anywhere |
| Stake currency | "Drop your $MONKIFY on the pick" | **USDC** |
| Tokenomics section | Pie chart, ticker `$MONKIFY`, supply, trading-tax, "Buy $MONKIFY" CTA | **Removed** → a short honest **"no token (yet)"** note (a token may come later; ticker/date TBD, never "$MONKIFY") |
| Roadmap | `✅ $MONKIFY live`, "Holder lounge", "Holders get jackpot tickets" | Feature milestones; token perks reframed as **future / earned by playing** ("early players get first dibs") |
| Hero CTAs | Primary "Play now" → `app.monkify.net`; secondary **"Buy $MONKIFY"** | Primary "Play now" → **flagged placeholder** app URL; secondary **community CTA** ("Join the troop") |
| Trust points | provably-fair + auto-refund | **kept + completed**: provably-fair, **instant on-chain (Solana) payouts**, auto-refund |
| Edson / art / story / arcade skin | — | **Unchanged** (copy/structure/brand only) |

**Scope = `monkify-lp` only.** No `monkify-backend` / `monkify-front` change (already rebranded). No
backend/DB/API/SignalR change ⇒ **no backend integration test** (constraints §3 mandate is scoped to
backend features).

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there). LP-relevant subset:

- **Worktree-first (per repo).** LP work lives in `.worktrees/rebranding-spacestroker/monkify-lp` off
  the LP hub's **`master`** (the LP's default branch is `master`, **not** `main`). The spec lives in
  `.worktrees/rebranding-spacestroker/monkify-meta` off `main`. The shared checkouts stay on their
  default branch — never switch a hub's branch. **Push after branch work** (AGENTS.md rule 5).
- **Secrets/config = mock/placeholder only.** The "Play now" app URL and social links are **flagged
  placeholders**, not real secrets — write them as obvious placeholders with a visible `TODO(ship)`.
- **No backend integration test required** — marketing/static LP change, not a backend feature.
- **No unit tests** — the LP has no test suite; verification is `npm run build` + browser QA (§12).
- **Operator messaging** — n/a (no operator-status surfaces on the LP).
- **Art is untouched** — reuse existing Edson art, coin, cards, roadmap PNGs; no new/replacement
  assets (card **Out of scope**).

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **Rebrand every user-visible "Monkify / MONKIFY / $MONKIFY" → SpaceStroker.** Wordmark = **CHIMP**`OUT` with "OUT" in primary orange (`#ffa214`), mirroring the old MONKI`FY` split. |
| 2 | **Launch model = real crypto, no house coin.** Stake is **USDC**; replace all `$MONKIFY` currency references with USDC. |
| 3 | Keep the genuine, already-true trust points: **provably-fair** per-round seed, **instant on-chain (Solana) payouts**, **auto-refund** of invalid/bad bets. |
| 4 | **Tokenomics section removed** → replaced by a short, honest **"no token (yet)"** note: nothing to buy right now; a token is **planned for later** (ticker/date TBD; **never reuse "$MONKIFY"**, never a fake CA). |
| 5 | **Roadmap** keeps a light **"token later / early players get first dibs"** tease; strip present-tense holder-gated claims and the "$MONKIFY live" assertion. |
| 6 | **"Play now" primary CTA** → app launch URL as a **clearly-marked placeholder** (`https://PLACEHOLDER-app-url/space-stroker/game`), flagged **must-fill before ship** (real domain undecided; the app's real route is `/space-stroker/game`). |
| 7 | **Hero secondary CTA** = a **community CTA** ("Join the troop" → `#community` / socials), **not** "Buy". Captures early players for the future token. |
| 8 | **Coin art** (`monk-coin-main.png`) is **kept** but reframed as Edson's **arcade credit / brand emblem** — not a tradeable/purchasable token (update `alt`/caption; no "the MONKIFY coin"). |
| 9 | **Nav/header:** remove the "Buy" button; "Tokenomics" nav → **"Token"** (points at the note) on desktop + mobile. |
| 10 | **Edson, all art, the retro/space/arcade skin, and the 6-slide story stay unchanged.** Copy/structure/brand only. |
| 11 | **Socials** stay `#` placeholders (flag them); **Team** (Bidi/Ryugaki/Bolso) unchanged. |
| 12 | Prefer **"game/round"** over "session" in copy (the app renamed Session→Game). |

---

## 4. Base branch & reconciliation context (READ FIRST)

This card was refined against the **arcade-overhaul LP redesign**, which — at spec time — was **not on
`master`**. The LP had two diverged lines off `518871f`:

- **Overhaul** (`feature/rebranding-spacestroker`, was `b07556d`): the full redesign the card describes
  (arcade hero HUD + chips, `GameModes.vue` card-stack, planet-map `SpaceRoadmap`, rewritten
  `index.astro` / `global.css`). Still Monkify-branded, still has Tokenomics.
- **`origin/master`** (`c6e1c36`): the **older** layout + `#49`'s minimal brand-string swap
  (wordmark/title only), still $MONKIFY/tokenomics-heavy.

**Reconciliation performed before this spec (approved: "one coherent landing"):**

1. Merged `origin/master` into `feature/rebranding-spacestroker` favouring the overhaul
   (`git merge -X ours`, 2 trivial conflicts → overhaul). New tip **`755102c`**, pushed. `master` can
   now fast-forward to it with no conflict. The merge also folded in `#49`'s SpaceStroker **Header
   wordmark** and **Layout `<title>`** (so those two are already done — see §6).
2. Cleaned the LP hub: local `master` reset to `origin/master` (`c6e1c36`); the overhaul is safely on
   the branch/origin.

**The plan:** do this card's work **on the same `feature/rebranding-spacestroker` branch**, then land the
whole thing (overhaul + SpaceStroker + token-free) onto `master` **once, at ship time** — so `master`
never receives a half-branded intermediate.

> ⚠️ **Landmine — do not delete this branch.** `specs/rebrand-chimpout.md` §15 lists a cleanup to
> "delete the abandoned `feature/rebranding-spacestroker` branches". That is now **superseded** — this
> branch is the **active** branch for card #51 (it carries the LP overhaul + this launch pass). The
> shipped rebrand went out on the differently-named `feature/rebrand-chimpout` (already merged); do
> **not** confuse the two, and do **not** delete `feature/rebranding-spacestroker` until this card ships.

---

## 5. Data model / API / backend

**None.** No entity, DTO, EF mapping, migration, controller, consumer, SignalR channel, or config
change. The LP is a static Astro site that only deep-links into the app via the "Play now" URL. §4/§5
of the spec template are **N/A** for this card.

---

## 6. Landing-page changes (the actual work — `monkify-lp`)

Base = reconciled overhaul (`755102c`). **Already done by the `#49` merge (do not redo):** the
`Header.vue` wordmark (`CHIMP·OUT`, `Header.vue:60`) and the `Layout.astro` `<title>` prefix
(`SPACESTROKER …`, `Layout.astro:7`). Everything below is the remaining card #51 work. Line numbers are
against `755102c`.

### 6.1 `src/pages/index.astro`

| Loc | Now | Change |
| --- | --- | --- |
| `:11–12` imports | `import graph from "../assets/graph.png"` (+ `coin`) | **Drop the `graph` import** once the Tokenomics block is removed (keep `coin`). |
| `:68` `steps[]` "Ante up" | "Drop your **$MONKIFY** on the pick…" | "Drop your **USDC** on the pick…" |
| `:72–76` `dist[]` | tokenomics distribution array | **Delete** (only used by the removed Tokenomics block). |
| `:136` coin `alt` | `Edson, the MONKIFY space chimp coin` | Reframe: `Edson, the SpaceStroker space chimp` (arcade credit / brand emblem — **no "coin"/"MONKIFY"**). |
| `:141–143` hero wordmark | `MONKI<span class="text-primary …">FY</span>` | `CHIMP<span class="text-primary text-glow-primary">OUT</span>` (mirror the split; "OUT" orange). |
| `:154`, `:210` primary "Play now" | `href="https://app.monkify.net"` | `href={PLAY_URL}` — a single `const PLAY_URL = "https://PLACEHOLDER-app-url/space-stroker/game"` in the frontmatter with a **`// TODO(ship): real SpaceStroker app URL — domain undecided`** comment (AC5, AC11). |
| `:155` hero secondary | `<SecondaryButton asLink href="#tokenomics">Buy $MONKIFY</SecondaryButton>` | Community CTA: `<SecondaryButton asLink href="#community">Join the troop</SecondaryButton>` (may become a direct Discord link once real; AC5). |
| `:159–161` hero chips | `NOW PLAYING` · `ON SOLANA` | `REAL STAKES` · `ON SOLANA` · `PROVABLY FAIR` (keep arcade tone; reuse `chip`/`chip--cyan`). |
| `:198–208` trust line | "Provably fair… Bad bets get auto-refunded on Solana." | Amend so **all three** trust points are stated: provably-fair rounds **+ instant on-chain (Solana) payouts +** auto-refund of bad bets (AC7). |
| `:235–312` **Tokenomics `<ContentSection id="tokenomics">`** | pie (`graph`), `dist` map, TOKEN DETAILS (TICKER `$MONKIFY`, supply, network, trading tax), "Buy $MONKIFY now" (`:309`) | **Remove entirely**; replace with the **"no token (yet)"** note (§7), a `<ContentSection id="token">`. |
| `:314–377` commented-out **Live Stats** block | dead HTML: "Token Price", "Token Holders", "Market Cap" | **Remove** the stale comment block (token-centric; keeps the source clean — AC9 is built-site, but remove to avoid confusion). |

### 6.2 `src/components/Header.vue`

| Loc | Now | Change |
| --- | --- | --- |
| `:60` logo | `CHIMP…OUT` | **Already SpaceStroker** (from #49) — leave. |
| `:69` desktop nav | `<a href="#tokenomics" …>Tokenomics</a>` | Relabel **"Token"**, point at `#token` (the note): `href="#token"` + `smoothScroll(e,'#token')`. |
| `:73–76` desktop CTA | `<PrimaryButton asLink href="#about" small>Buy</PrimaryButton>` | **Remove** the "Buy" button (AC6). |
| `:106` mobile nav | `…href="#tokenomics"…>Tokenomics</a>` | Same as `:69` → "Token" / `#token`. |
| `:110–114` mobile CTA | `<PrimaryButton asLink small>Buy</PrimaryButton>` | **Remove** (AC6). |

> Alternative allowed by the card: **remove** the Token nav item entirely instead of relabeling. Chosen
> default: **relabel to "Token" → `#token`** so the honest note keeps a nav slot. Keep desktop + mobile
> in sync.

### 6.3 `src/components/SpaceRoadmap.vue`

Rewrite `phases[].items` / `perk` / `perkDesc` to remove holder-gated + "$MONKIFY live" claims and
reframe token perks as future/earned:

| Loc | Now | Change |
| --- | --- | --- |
| `:46` phase-1 item | `✅ $MONKIFY live on Solana` | Replace with a true, non-token milestone, e.g. `✅ SpaceStroker is live — real USDC bets on Solana`. Phase 1 must **not** assert "$MONKIFY live" (AC4). |
| `:73–74` phase-2 perk | `🎟️ Holder lounge` / `Badges and early seats for $MONKIFY holders.` | Reframe to earned-by-playing, e.g. `🎟️ Early-player perks` / `Badges and early seats for the first players in.` |
| `:96` phase-3 perkDesc | `Holders get into the big rollover draws.` | Reframe: `Regulars get into the big rollover draws.` (no holder gating). |
| `:103` phase-4 subtitle | `Monkify in your pocket` | `SpaceStroker in your pocket`. |
| light token tease | — | Add a **light** "token later / early players first" tease in one phase (e.g. a phase-4/later `perk`: `🪙 A token, maybe later` / `If a coin ever lands, early players get first dibs.`) — future-framed, no ticker (AC4). |

### 6.4 `src/components/Footer.vue`

| Loc | Now | Change |
| --- | --- | --- |
| `:7` wordmark | `MONKI<span class="text-primary">FY</span>` | `CHIMP<span class="text-primary">OUT</span>`. |
| `:10` copyright | `© 2025 MONKIFY — BET ON THE MONKEY` | `© 2026 SPACESTROKER — BET ON THE MONKEY` (rebrand; year bumped to 2026). |

### 6.5 `src/layouts/Layout.astro`

| Loc | Now | Change |
| --- | --- | --- |
| `:7` `title` | `SPACESTROKER — The space chimp coin` | Drop the memecoin framing: `SPACESTROKER — Bet on the space chimp` (or similar; remove "coin"). |
| `:8–9` `description` | `Play, bet and collect MONKI SHEKELS — gamified crypto experience.` | Rewrite token-free: e.g. `Bet real USDC on what Edson the space chimp types next — provably fair, instant on-chain payouts.` (**kill "MONKI SHEKELS"**). |

### 6.6 `src/components/GameModes.vue`

**Verify only — no change expected.** RACES / BEAST MODE / LOTTERIES copy has no "$MONKIFY" and no
currency mismatch (pots/tickets are generic). Confirm during implementation; if any "$MONKIFY"/
"Monkify" slips in, fix it.

### 6.7 `src/styles/global.css` (optional, non-user-visible)

`:4` header comment `MONKIFY — Arcade design system …` → `SPACESTROKER — …`. **Optional** — a CSS comment
that is stripped from the built output; not required by any AC. Update for hygiene if convenient.

### 6.8 LP repo docs (optional, out of AC scope)

`monkify-lp/PRODUCT.md` and `monkify-lp/CLAUDE.md` still describe the `$MONKIFY` holder-gated product.
They are **not** part of the built site (AC9 is about the built site), so they are **out of scope** for
this card — optional hygiene only; do not expand scope to a doc rewrite unless asked.

---

## 7. The "no token (yet)" note (replaces the Tokenomics section)

Replace `index.astro` `<ContentSection id="tokenomics">` with a short `<ContentSection id="token">`
holding one honest note (arcade skin kept; **no** pie chart, ticker, supply, trading-tax, or Buy CTA):

- **Heading:** e.g. `No token. Yet.`
- **Body (straight, trust-building — the one non-irreverent beat):** there is **nothing to buy right
  now** — SpaceStroker runs on **real USDC**, provably fair. **A token may come later**; ticker and date
  are TBD. **Do not** print a ticker, a fake contract address, or "$MONKIFY".
- **Anti-scam line (edge case):** briefly warn that any "$SPACESTROKER"/token for sale today is **not
  us** — sets expectations without inviting a scam-token hunt.
- Optionally a soft community nudge ("early players get first dibs") linking `#community`.

The Header nav "Token" item and any in-page anchor point at **`#token`**.

---

## 8. Placeholders & flags (AC11)

- **App URL:** single `PLAY_URL` const = `https://PLACEHOLDER-app-url/space-stroker/game`, with a visible
  `// TODO(ship): replace with the real SpaceStroker app URL before launch (domain undecided)` comment.
  Both hero + about "Play now" buttons use it. The **route** `/space-stroker/game` is the app's real route
  (verified on `monkify-front` `origin/main`); only the **domain** is a placeholder.
- **Socials:** the Twitter/Telegram/Discord `href: "#"` placeholders stay; add a
  `// TODO(ship): real socials` comment near the `socials` array so they cannot ship silently.
- **Stale-reference sweep (AC9):** after edits, no built-site `Monkify`/`$MONKIFY`, no
  `app.monkify.net`, and no `/monkify/...` route strings remain.

---

## 9. Out of scope

- Any `monkify-backend` or `monkify-front` (game app) change — already rebranded (#49).
- Launching an actual token / real tokenomics; deciding the future-token name or ticker.
- Deciding the final production app **domain** (flagged placeholder).
- Email/waitlist capture build (socials/Discord only for now).
- New/replacement **art** assets (reuse existing Edson art, coin, cards, roadmap PNGs).
- The live in-browser design iteration / visual polish — happens in the **implement** phase; this card
  scopes it.
- Rewriting the LP repo's own `PRODUCT.md` / `CLAUDE.md` (optional hygiene, §6.8).

---

## 10. File checklist

### 10.1 Landing page (`monkify-lp`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/pages/index.astro` | Hero wordmark→SPACESTROKER, coin `alt`, `steps` USDC, chips → REAL STAKES·ON SOLANA·PROVABLY FAIR, trust line (add instant on-chain payouts), CTAs (`PLAY_URL` placeholder + community secondary), **remove Tokenomics block** → `#token` note, drop `graph` import + `dist`, remove dead Live-Stats comment. |
| 2 | `src/components/Header.vue` | Remove "Buy" (desktop + mobile); "Tokenomics" nav → "Token" → `#token` (both). Wordmark already SpaceStroker. |
| 3 | `src/components/SpaceRoadmap.vue` | Strip `$MONKIFY live` + holder-gated perks; reframe as feature milestones + future/earned token tease; subtitle `Monkify`→`SpaceStroker`. |
| 4 | `src/components/Footer.vue` | Wordmark → SPACESTROKER; copyright → `© 2026 SPACESTROKER …`. |
| 5 | `src/layouts/Layout.astro` | `<title>` drop "coin"; `description` rewrite (USDC/provably-fair; kill "MONKI SHEKELS"). |
| 6 | `src/components/GameModes.vue` | Verify only — no currency/brand mismatch (no change expected). |
| 7 | `src/styles/global.css` | Optional: comment `MONKIFY`→`SPACESTROKER` (non-user-visible). |

### 10.2 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/lp-space-stroker-relaunch.md` | This spec (this run). |
| 2 | `INDEX.md` | KB-map row for this spec + "Feature currently in flight" entry (this run). |

---

## 11. Tests

- **Backend unit/integration tests: N/A** — no backend change (constraints §3 mandate is scoped to
  backend features; this is a marketing/static LP change).
- **Frontend tests: N/A** — the LP has no test suite.
- **Definition of done = build + browser QA (§12):** `npm run build` succeeds and the built site
  contains **no** user-visible `Monkify`/`$MONKIFY`, no `app.monkify.net`, and no `/monkify/...`
  strings (AC9); 0 console errors, existing motion/scroll-reveal/story-typewriter/reduced-motion
  intact (AC10).

---

## 12. Acceptance criteria

Mapped 1:1 from the Trello **Acceptance Criteria** checklist:

1. **AC1** — hero wordmark reads **"SPACESTROKER"** ("OUT" in primary orange) and **no** "Monkify /
   MONKIFY / $MONKIFY" text appears anywhere on the page.
2. **AC2** — any betting / how-to-play copy names the stake as **USDC**; no "$MONKIFY" currency
   reference remains.
3. **AC3** — where Tokenomics was: **no** pie chart, ticker, supply, trading-tax, or "Buy $MONKIFY"
   CTA; instead a short note states there is no token to buy yet and a token is planned for later.
4. **AC4** — no roadmap perk claims a present-tense "$MONKIFY holders" benefit; phase 1 does not
   assert "$MONKIFY live"; any token perk is framed as future / "coming later".
5. **AC5** — hero primary CTA is **"Play now"** → the app launch URL (clearly-marked placeholder);
   secondary CTA is a **community/"Join"** action, not "Buy".
6. **AC6** — header/nav on **desktop and mobile**: logo reads SPACESTROKER, **no "Buy" button**, no live
   "Buy $MONKIFY"/tokenomics purchase link.
7. **AC7** — fairness trust points all still stated: **provably-fair rounds**, **instant on-chain
   (Solana) payouts**, **auto-refund** of bad bets.
8. **AC8** — the Edson story section's **6 slides and art are unchanged**.
9. **AC9** — `npm run build` succeeds and the built site contains **no** user-visible
   "monkify"/"$MONKIFY" and **no** links to `app.monkify.net` or `/monkify/...` routes.
10. **AC10** — LP loads (desktop + mobile) with **0 console errors**; existing layout, scroll-reveal,
    story typewriter, and `prefers-reduced-motion` behavior are **not regressed**.
11. **AC11** — the "Play now" app URL and social links are **visibly flagged** (`TODO(ship)` comment)
    so they cannot ship unresolved.

---

## 13. Manual QA

1. `cd monkify-lp && npm install && npm run dev` → open `http://localhost:4321`.
2. Hero reads **SPACESTROKER**; chips = REAL STAKES · ON SOLANA · PROVABLY FAIR; coin `alt` no longer says
   "coin"; primary "Play now" → placeholder URL, secondary "Join the troop" → `#community`.
3. Scroll: How-it-works says **USDC**; trust line states provably-fair + instant on-chain payouts +
   auto-refund; the old Tokenomics section is now the **"no token (yet)"** note; roadmap has **no**
   "$MONKIFY live"/holder perks; footer reads **SPACESTROKER**.
4. Header (desktop + mobile menu): **no "Buy"**; "Token" nav scrolls to the note.
5. Story: 6 slides + typewriter unchanged; reduced-motion (OS setting) still respected.
6. `npm run build` succeeds; `grep -ri "monkify\|\\$MONKIFY\|app.monkify.net" dist/` → **nothing
   user-visible**. Browser console = **0 errors**.

---

## 14. Trello / pipeline

- **Source card:** [Revisar landing page… SpaceStroker + launch sem token (USDC) #51](https://trello.com/c/Y0PpEDii)
  — list **Specs created**; the **Implementation** block on the card carries the worktree, branch, and spec.
- **Branch (reused):** `feature/rebranding-spacestroker` (the reconciled arcade-overhaul branch, tip
  `755102c`) in `monkify-lp` (off `master`) and `monkify-meta` (off `main`). Worktrees at
  `.worktrees/rebranding-spacestroker/<repo>`.
- **Next:** `/monkify-implement-feature Revisar landing page…` to build it on this branch; then
  `/monkify-ship-feature Revisar landing page…` — which lands the whole branch (overhaul + SpaceStroker +
  token-free) onto `master` in **one coherent merge**, so `master` never gets a half-branded LP.
- **Relationship to #49:** this card finishes `specs/rebrand-chimpout.md` §9.3's **deferred** LP work
  ($MONKIFY ticker, tokenomics, domain) on the overhaul redesign. Its "delete the abandoned
  `feature/rebranding-spacestroker` branch" cleanup note is **superseded** (see §4).

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-07-16 | Initial spec from refined card #51. Base = reconciled arcade-overhaul branch `feature/rebranding-spacestroker` (`755102c`); grounded in the shipped `#49` rebrand (app+KB already SpaceStroker/Game; LP token/domain deferred). Documents the LP branch reconciliation ("one coherent landing") and neutralizes #49's stale "delete the branch" cleanup note. |

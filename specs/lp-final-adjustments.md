# Space Stroker — "LP final adjustments" — Implementation Spec

> **Status: Ready for implementation** — refined via Trello card
> [LP final adjustments (#78)](https://trello.com/c/3dPBofpy/78-lp-final-adjustments) (2026-07-21).
> Repos affected: **`monkify-lp`** (primary, branch **`master`**) and this doc in **`monkify-meta`**.
> **Not** affected: `monkify-front`, `monkify-backend`.
>
> **Purpose:** Final pre-launch honesty + polish pass on the Space Stroker landing page. Correct the
> game-mode copy so it matches the real seeded **Main** production game types, remove stale
> placeholder markers from art that is actually final, inventory every deferred placeholder URL, and
> tidy small link/copy inconsistencies — without inventing the still-undecided app domain and social
> URLs (those stay flagged for ship). Audience: pre-launch degen/arcade players.

---

## 1. Summary

This is a **frontend-only, LP-only** audit-and-fix pass. It splits cleanly into work that ships **now**
and inputs that are **gated on Vitor**.

| Area | What happens this pass | Gate |
| --- | --- | --- |
| **Mode copy** (`GameModes.vue`) | Rename to **Cosmic Sprint / Beast Mode / Space Jackpot** and apply Vitor's final blurbs (§6.1) — **two blurbs corrected** to kill the factual errors they still carried | ✅ **RESOLVED** (copy provided 2026-07-21; §4) |
| **Mode GIFs** (`GameModes.vue`) | Wire the two **new** GIFs — `cosmicsprint.gif` (Sprint) + `spacejackpot.gif` (Space Jackpot); Beast keeps `beastmode.gif` | ✅ **RESOLVED** (art provided; saved to `src/assets/cards/`) |
| **`#token` art** (`index.astro`) | Remove the `TODO(art)` / "placeholder" markers — the dimmed-coin "TBD" visual is **final** | ✅ now |
| **Header logo** (`Header.vue`) | `href="#"` → real home/top anchor (`#hero`) | ✅ now |
| **PLAY_URL + socials** (`index.astro`, `Header.vue`) | **Fill** the real URLs (app + X + Telegram), **remove Discord**; LP self-domain noted (§6.6) | ✅ **RESOLVED** (URLs provided 2026-07-21) |
| **Roadmap HI-SCORE + phases** (`SpaceRoadmap.vue`) | Audit copy reads as labelled **goals/targets**, never live metrics; retain phase-1 "all 3 modes live" | ✅ now (audit) |
| **Image audit** (site-wide) | Confirm every rendered image is final art; nothing reaches the `/placeholder.png` fallback | ✅ now (audit) |
| **Brand** | No user-visible `Monkify` / `$MONKIFY`; brand stays **Space Stroker** | ✅ now (verify) |

No backend, no DB, no migration, no SignalR, no tests (LP has no suite). DoD = `npm run build` clean +
browser QA with 0 console errors + brand sweep clean.

> **All 4 gates were resolved by Vitor on 2026-07-21** (mode copy + names, both new GIFs, real app/LP
> domains + socials). The only judgement applied: **two of the supplied blurbs were corrected** because
> they still carried the exact factual errors this card exists to fix — see §4 and §6.1.

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo (hub stays on its default branch):** work in
  `.worktrees/lp-final-adjustments/<repo>`. The **`monkify-lp` hub stays on `master`** — LP's default
  branch is `master`, **not** `main` (see `constraints.md` → Git; memory: "monkify-lp uses master").
  `monkify-meta` off `main`.
- **Branch:** `feature/lp-final-adjustments` in both affected repos (the merge/PR ref), pushed to `origin`.
- **Secrets/config:** only mock/placeholder values in committed files. The flagged URLs **stay
  placeholders** — never write a real credential or real production URL in this pass.
- **No tests required:** frontend-only LP change; the LP has no unit/integration suite, and there is no
  game-flow change, so the integration-test mandate does **not** apply.
- **No backend touch:** the real Main game-type definitions are a **read-only** source of truth for the
  corrected wording (§5); `monkify-backend` is not modified.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **Scope = audit + fix now, flag URLs.** Fix mode texts, copy/data consistency, and small link fixes this pass; **map** every placeholder URL/art but keep them flagged (app domain + socials still undecided). |
| 2 | **The 3 mode texts in `GameModes.vue` are factually wrong vs the seeded Main types and must be corrected** (see §5 for the truth and §6.1 for the exact strings). |
| 3 | **Final mode copy (names/tags/blurbs for all 3 modes) is a required input from Vitor** ("I'll pass later") and must be delivered **before** the `GameModes.vue` text is edited. The delivered copy must reintroduce **none** of the 3 factual errors. |
| 4 | **New QUICK RACES + LOTTERIES GIFs are pending art from Vitor** → both stay flagged (`TODO(art)`), reusing `beastmode.gif`, this pass. Beast keeps `beastmode.gif`. Not produced here. |
| 5 | **PLAY_URL + Twitter/Telegram/Discord stay flagged `TODO(ship)`** — the app domain and socials are still undecided. This card **inventories** them (§6.6); ship fills them. |
| 6 | **Community wording:** the **nav** label stays **"Community"**; "gang"/"troop" may both appear in CTAs/copy as intentional flavor — **do not** force-unify. |
| 7 | **Roadmap HI-SCORE banner + phase copy must read as labelled goals/targets**, never live/real-time metrics. Fix anything that reads as a current-activity claim; keep phase-1 "all 3 modes live" (accurate); no fabricated live numbers. |
| 8 | **`#token` dimmed-coin "TICKER / TBD / NOTHING TO BUY" visual is the intended final** ("we can keep that") → **remove** its `TODO(art)` / "placeholder" markers. The token-free "TBD ticker" copy stays. |
| 9 | **Header logo `href="#"`** → a real scroll-to-top / home anchor (minor). |
| 10 | **Visual asset audit:** confirm every rendered image is final art and **nothing** renders the `/placeholder.png` fallback. |
| 11 | **Brand stays Space Stroker** — no user-visible `Monkify` / `$MONKIFY` in the built site. |

---

## 4. Resolved gate inputs (Vitor, 2026-07-21)

All four gates are **resolved**. Recorded here verbatim + the two corrections applied.

**Mode names (final):** QUICK RACES → **Cosmic Sprint**; **Beast Mode** (unchanged); LOTTERIES →
**Space Jackpot**.

**Mode blurbs — as supplied, with two factual corrections.** Vitor supplied the blurbs via the game
app's Game Selection screen. Two of them **still carried the exact factual errors this card exists to
fix**, so they are corrected here (voice preserved). AC #4 requires the shipped copy reintroduce none
of the §5 errors, so the raw strings **must not** be pasted as-is:

| Mode | Supplied blurb | Issue | **Corrected blurb (use this)** |
| --- | --- | --- | --- |
| Cosmic Sprint | "Call your **6-letter** run and ape in. Edson jackhammers the keys till a sequence hits — first to print scoops the whole bag. Connect wallet, send it. Min 2 degens." | "6-letter" ⇒ Sprint is **6-character alphanumeric** (A–Z + 0–9), not letters-only (AC #1) | "Call your **6-character** run and ape in. Edson jackhammers the keys till a sequence hits — first to print scoops the whole bag. Connect wallet, send it. Min 2 degens." |
| Beast Mode | "No typing, pure ape energy. **Back a beast — rabbit, monkey, jaguar, falcon —** and the first one Edson spells out moons the entire pot. Connect wallet. Min 2." | The 4-animal list is **invented**; really **back 1 of 8** preset runners (`ZEPHYR…NOVA05`, not animals) (AC #2) | "No typing, pure ape energy. **Back 1 of 8 runners** and the first one Edson spells out moons the entire pot. Connect wallet. Min 2." |
| Space Jackpot | "Grab a ticket, the house seeds the pot, and it fattens all day till the daily draw drops. One degen apes off with the jackpot. Connect wallet. Min 2." | **None** — "daily draw / all day" matches the 24h Millions lottery (AC #3) | *(use as-is)* |

> The corrected Cosmic Sprint / Beast Mode wording is **pending Vitor's final sign-off** (he may prefer
> his own phrasing) — but it may **not** ship with "6-letter" or the "rabbit/monkey/jaguar/falcon" list.
> ⚠️ Note: the app screenshot also shows a **"LOTTERY 1 HOUR"** badge on Space Jackpot — that is the app
> (out of scope) and is itself wrong vs the 24h Main lottery; do **not** copy a "1 hour" claim to the LP.

**GIFs (provided, saved):** `src/assets/cards/cosmicsprint.gif` (sunglasses-typing, 1320×1270) →
**Cosmic Sprint**; `src/assets/cards/spacejackpot.gif` (coin-pile, 840×1200) → **Space Jackpot**; Beast
keeps `beastmode.gif`. All three are animated (NETSCAPE loop). See §6.1 for wiring + posters.

**URLs (provided → fill now, no longer `TODO(ship)`):** see §6.6. `PLAY_URL =
https://app.spacestroker.com/space-stroker/game`; LP self-domain `https://spacestroker.com`; X
`https://x.com/SpaceStroker`; Telegram `https://t.me/spacestroker`; **Discord removed**.

Everything in §6.2–§6.6 (token markers, header logo, URLs, roadmap audit, image audit) can proceed
immediately alongside the mode edit.

---

## 5. Source of truth — the real "Main" (production) game types

Read-only reference (from `monkify-backend` → `SpaceStroker.Consumers/DefaultGameParameters.cs`,
`GenerateMainDefaultParameters`; the three seeded production types). The corrected LP mode copy must be
consistent with these:

| Main type | Reality (authoritative) | LP mode card | Current LP claim (WRONG) |
| --- | --- | --- | --- |
| **6X Main Sprint** | **One** 6-character **alphanumeric** race (A–Z + 0–9); duplicates allowed; min 2 players | QUICK RACES | tag `"6 LETTERS · 6 NUMBERS"` — implies separate letters-only / numbers-only variants; really **one** alphanumeric mode |
| **6X Main Derby** | Preset "horse race": **back 1 of 8** runners — `ZEPHYR, BOREAS, COMET1, BLAZE2, SHADOW, TEMPO3, VOLTZ4, NOVA05`; 6-char alphanumeric; min 2 | BEAST MODE | blurb lists an **invented** 4-animal set `"rabbit, monkey, jaguar, falcon"` — really **8** non-animal runners |
| **6X Main Millions** | Lottery drawn **once a day (24h)**; pot grows all day; min 2 | LOTTERIES | tag `"EVERY 4H · DAILY DRAW"` + blurb `"A draw every 4 hours, plus one fat daily."` — really **once a day / 24h** |

> ⚠️ The `every-4h` and `8-char` lotteries in `DefaultGameParameters.cs` are **DEV-only** defaults
> (`GenerateDevDefaultParameters`), **not** Main. The LP describes the **Main/production** experience,
> so "every 4h" is wrong. Only the three Main types above are the source of truth for LP copy.

The implementer does **not** need to reproduce the runner names or "alphanumeric" verbatim on the LP —
Vitor's final copy decides the surface wording — but the copy must not **contradict** these facts (no
"letters/numbers" split, no invented animals, no sub-daily lottery cadence).

---

## 6. Frontend behaviour (all in `monkify-lp/src`)

### 6.1 `components/GameModes.vue` — final mode copy + new GIFs (§4 resolved)

The three modes are a `Mode[]` literal (`modes`, lines ~25–60). Apply the **final** names, tags, blurbs,
and GIFs below.

**New GIF imports (top of `<script setup>`, replacing the `beastGif`-reuse for two modes):**

```ts
import cosmicSprintGif from "../assets/cards/cosmicsprint.gif"; // new
import spaceJackpotGif  from "../assets/cards/spacejackpot.gif"; // new
// beastGif import stays (Beast Mode keeps it)
```

**Mode `name` / `tag` / `blurb` / `gif` (final):**

| key | `name` | `tag` (replace) | `blurb` (final, §4-corrected) | `gif` |
| --- | --- | --- | --- | --- |
| `races` | `"COSMIC SPRINT"` | `"6 CHARS · A–Z + 0–9"` (was `"6 LETTERS · 6 NUMBERS"`, line ~29) | "Call your 6-character run and ape in. Edson jackhammers the keys till a sequence hits — first to print scoops the whole bag. Connect wallet, send it. Min 2 degens." | `asset(cosmicSprintGif)` |
| `beast` | `"BEAST MODE"` | `"BET ON THE BEAST"` (keep) | "No typing, pure ape energy. Back 1 of 8 runners and the first one Edson spells out moons the entire pot. Connect wallet. Min 2." | `asset(beastGif)` (unchanged) |
| `lottery` | `"SPACE JACKPOT"` | `"DAILY DRAW · 24H"` (was `"EVERY 4H · DAILY DRAW"`, line ~52) | "Grab a ticket, the house seeds the pot, and it fattens all day till the daily draw drops. One degen apes off with the jackpot. Connect wallet. Min 2." | `asset(spaceJackpotGif)` |

- **Remove** the two `// TODO(art): placeholder — reuses the beast GIF …` comments (lines ~33–34 and
  ~56–57) — the real dedicated art now exists, so AC #5's "flagged pending" note no longer applies;
  they are wired to real clips instead.
- **Posters (static `img`, shown when the card is not active).** Beast uses `beastmode-poster.png`. For
  the two new modes, the current `img` is `cardA` (`a.png`) / `cardC` (`c.png`). To avoid a
  poster→GIF art mismatch, generate a **first-frame poster** for each new clip
  (`cosmicsprint-poster.png`, `spacejackpot-poster.png`, e.g. `ffmpeg -i x.gif -frames:v 1 x-poster.png`)
  and set `img` to it. Acceptable fallback if a poster can't be produced: keep `a.png` / `c.png` as the
  static posters. Do **not** set `img` to the GIF itself (it would defeat the reduced-motion static state).
- **Preserve** the reduced-motion GIF-swap logic (`reduceMotion`, the `activeKey` poster→GIF swap at the
  `<img :src>` around line ~183) untouched.
- The two new GIFs are large-ish (325 KB / 610 KB); optionally optimise (e.g. `gifsicle -O3`) — not required.

### 6.2 `pages/index.astro` — `#token` markers (✅ now)

The `#token` panel's right-hand visual is **final** (decision #8). Remove the two "unfinished" markers,
keep the visual:

- Comment `<!-- Right: image (placeholder — TODO art). "No token yet" visual. -->` (line ~380) → reword
  to drop "placeholder — TODO art" (e.g. `<!-- Right: final "no token yet" visual. -->`).
- Comment `<!-- TODO(art): swap for a dedicated "no token yet" illustration -->` (line ~382) → **delete**.
- **Keep** the dimmed coin (`coin.src`, `opacity-20`) + `TICKER` / `TBD` / `NOTHING TO BUY` block
  (lines ~383–388) exactly as-is — that is the intended final look.

### 6.3 `components/Header.vue` — logo anchor (✅ now) + PLAY_URL flag

- The wordmark `<a href="#" @click="… smoothScroll(e, '#hero')" aria-label="Home">` (line ~72) already
  smooth-scrolls to `#hero` on click, but the bare `href="#"` is a dead fallback. Change
  `href="#"` → `href="#hero"` so it is a real home/top anchor even without JS (AC #10). The inline
  `@click` still works.
- `PLAY_URL` (line ~7) is a **duplicate** of `index.astro`'s. **Fill both** with the real app URL
  `https://app.spacestroker.com/space-stroker/game` (§6.6) and drop the `TODO(ship)` comment. Deduping
  the two into one shared source is **optional / low priority**; if not done, keep both values identical.
- Nav label **"Community"** (line ~14) — leave as-is (decision #6).

### 6.4 `components/SpaceRoadmap.vue` — HI-SCORE + phase audit (✅ now)

Audit the "north-star" banner (lines ~207–228) and the phase copy (`phases`, lines ~37–138):

- The banner header already reads **"HI-SCORE WE'RE CHASING"** (line ~211) over `10,000 PLAYERS` /
  `$1M BET` / `250K ROUNDS PLAYED` — i.e. it is **already framed as a target/goal**, not a live metric.
  **Confirm** no figure reads as current real-time activity; if any label is ambiguous (e.g. reads as
  "already played"), tighten it so it stays unambiguously a goal. No fabricated live numbers may be
  introduced.
- **Retain** phase-1 item "All 3 game modes live — races, beast mode & lotteries" (line ~52) — it is
  accurate (all 3 Main types are seeded live). The forward-looking token/airdrop phases (2–5) are the
  **future plan** and are out of this card's scope; leave them.
- Expected outcome: this section likely needs **no functional change** — it is primarily an audit that
  documents the banner already reads as goals (AC #8). Make a change only if an ambiguity is found.

### 6.5 `components/SpaceBackground.vue` — `/placeholder.png` audit (✅ now)

The background spawner renders `:src="el.src || '/placeholder.png'"` (line ~164). The fallback fires
only if `getRandomElement()` returns its empty-`src` safety object, which happens only when the spawn
pool is empty. `SECONDARY_SPACE_ELEMENTS` (`constants/space-elements.ts`) is **fully populated** (every
entry has `rarity ≥ 1` and a real imported asset), so the pool is **never empty** and
`/placeholder.png` is **unreachable** at runtime. Audit finding: **no change required** — document that
the fallback is dead-defensive code. (Do **not** rely on shipping a `/placeholder.png` asset; verify
via browser QA that no background image 404s — AC #11.)

### 6.6 URLs — fill the real values (✅ resolved 2026-07-21)

The app domain and socials are decided, so this pass **fills** them (a scope change from the card's
original "defer to ship" — authorised by Vitor). Drop the `TODO(ship)` comments as each is filled.

| Link | Final value | Location(s) | Action |
| --- | --- | --- | --- |
| App URL (`PLAY_URL`) | `https://app.spacestroker.com/space-stroker/game` | `pages/index.astro` `PLAY_URL` (line ~83; hero CTA ~171, about CTA ~293) **and** `components/Header.vue` `PLAY_URL` (line ~7; nav + mobile CTA) | Replace the placeholder in **both**; remove `TODO(ship)` (lines ~82, ~7) |
| Twitter/X | `https://x.com/SpaceStroker` | `pages/index.astro` `socials[0]` (line ~89) | Set `href`; remove `TODO(ship)` (line ~85) |
| Telegram | `https://t.me/spacestroker` | `pages/index.astro` `socials[1]` (line ~95) | Set `href` |
| Discord | — **remove** | `pages/index.astro` `socials[2]` (line ~101) | **Delete** the Discord entry from the `socials` array |

**Discord removal — layout:** the socials grid renders `sm:grid-cols-3` (`index.astro` ~419). With two
join slots left, either switch that grid to `sm:grid-cols-2` (recommended — balanced) or leave
`grid-cols-3` (two cards + a gap). Pick the one that looks right in browser QA; no dead Discord slot.

**LP self-domain `https://spacestroker.com`:** this is the LP's own production URL. It is **not** a
current on-page placeholder — check `layouts/Layout.astro` (and any `<head>` meta / `astro.config`
`site`) for an `og:url` / canonical / `site:` value and set it to `https://spacestroker.com` if a
placeholder or missing value is found. If nothing references a self-URL, this is a no-op — do **not**
invent new meta tags for this card.

> The keystore/secret rule still applies — these are public marketing URLs, not credentials. No real
> secrets are introduced.

> Note: the hero arcade HUD "`HI-SCORE 999999`" / "`● ONLINE`" (`index.astro` lines ~138–139) is
> decorative arcade-cabinet flavor (a fake high score / cabinet light), not a data or live-activity
> claim — leave it. It is separate from the roadmap banner in §6.4.

### 6.7 Image asset audit (✅ now)

Confirm every rendered image resolves to a final, committed asset — none broken, none the
`/placeholder.png` fallback (AC #11). Covered surfaces and their assets already import cleanly:
backstory `part1–6` + `mobile1–6`, roadmap `1rp–5rp` / `icon1–5` / `bg-roadmap` / `traceline`, mode
cards (`a`, `c`, `beastmode-poster`, `beastmode.gif`), `elements/*` (space background), `edson`,
`monk-coin-main`, team avatars, hero `edson-ship-typing` + `comet`. Verify at build + in the browser
(no 404s, no fallback). This is an **audit**; only replace an asset if one is found missing.

---

## 7. Out of scope

- `monkify-front` (game app) and `monkify-backend` — untouched. (Note: the app's own Game Selection
  copy still carries the "rabbit/monkey/jaguar/falcon" + "LOTTERY 1 HOUR" errors — that is an **app**
  fix, a separate card, not this pass.)
- The forward-looking token/airdrop **roadmap phases 2–5** — they are the future plan, not this pass.
- Stale LP `PRODUCT.md` / `CLAUDE.md` cleanup — out of scope (prior known follow-up).
- A **Discord** community link — explicitly dropped for now (Vitor); may return later.

> Previously deferred, now **in scope** (gates resolved 2026-07-21): the real app/social URLs (§6.6),
> the two new mode GIFs (§6.1), and the final mode copy/names (§4) — no longer "pending Vitor".

---

## 8. File checklist

### 8.1 LP (`monkify-lp`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/components/GameModes.vue` | Final names (Cosmic Sprint / Beast Mode / Space Jackpot), corrected tags + blurbs (§6.1), import + wire `cosmicsprint.gif`/`spacejackpot.gif`, remove the two `TODO(art)` comments; preserve reduced-motion swap |
| 2 | `src/assets/cards/cosmicsprint.gif` | **New asset** (Cosmic Sprint clip) — already saved to the worktree |
| 3 | `src/assets/cards/spacejackpot.gif` | **New asset** (Space Jackpot clip) — already saved to the worktree |
| 4 | `src/assets/cards/cosmicsprint-poster.png`, `spacejackpot-poster.png` | **New (optional)** first-frame posters (§6.1); else reuse `a.png`/`c.png` |
| 5 | `src/pages/index.astro` | Remove `#token` "placeholder — TODO art" markers (lines ~380, ~382), keep the visual; fill `PLAY_URL` (app URL) + X/Telegram `href`s, **delete Discord** + adjust the socials grid (§6.2, §6.6) |
| 6 | `src/components/Header.vue` | Logo `href="#"` → `href="#hero"` (§6.3); fill `PLAY_URL`; nav "Community" unchanged |
| 7 | `src/components/SpaceRoadmap.vue` | Audit HI-SCORE banner + phase copy read as goals/targets; retain phase-1 "all 3 modes live"; change only if an ambiguity is found (§6.4) |
| 8 | `src/components/SpaceBackground.vue` | Audit only — document `/placeholder.png` fallback is unreachable; no change expected (§6.5) |
| 9 | `src/layouts/Layout.astro` / `astro.config.*` (if a self-URL placeholder exists) | Set `og:url`/canonical/`site` to `https://spacestroker.com` **only if** a placeholder/missing value is found (§6.6) |

> `src/constants/space-elements.ts` is **read** for the §6.5 audit, not edited.

### 8.2 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/lp-final-adjustments.md` | This spec |
| 2 | `INDEX.md` | KB-map row for this spec (status **Ready**) + "Feature currently in flight" entry |

No backend / front files.

---

## 9. Tests

**N/A for a mock/verification suite.** The LP has no unit or integration tests, and this pass makes no
game-flow change, so the integration-test mandate does not apply (`constraints.md` → Integration
tests → "Scope of new feature").

**Definition of Done (verification, not automated tests):**

1. `npm run build` (in `monkify-lp`) completes clean.
2. Browser QA of the built LP shows **0 console errors** and no image 404 / `/placeholder.png` render.
3. Brand sweep clean — the built output contains **no** user-visible `Monkify` / `$MONKIFY`.
4. The 3 mode texts match Vitor's delivered copy and contain none of the 3 factual errors (§5).

---

## 10. Acceptance criteria

Mapped 1:1 from the Trello **Acceptance Criteria** checklist:

1. Given the QUICK RACES mode card, when a player reads its tag/blurb, then it describes a **single
   6-character alphanumeric (A–Z + 0–9)** race and no longer implies separate "letters" and "numbers" variants.
2. Given the BEAST MODE card, when a player reads its blurb, then it reflects **backing 1 of the 8
   preset runners** and no longer lists the invented "rabbit/monkey/jaguar/falcon" set.
3. Given the LOTTERIES card, when a player reads its tag/blurb, then it states a **once-a-day (24h)**
   draw with an all-day-growing pot and no longer claims "every 4h".
4. Given the final mode copy has not yet been supplied, when the spec/implementation begins, then the
   card **blocks on Vitor's** Races/Beast/Lottery copy before `GameModes.vue` text is edited, and the
   delivered copy reintroduces **none** of the 3 factual errors.
5. Given the QUICK RACES and LOTTERIES cards lack final art, when the pass ships, then both remain
   **explicitly flagged** as pending dedicated GIFs (documented), not silently shipped reusing
   `beastmode.gif` without a note.
6. Given the built LP, when every `PLAY_URL` and social link is inspected, then each unresolved link is
   a clearly-flagged `TODO(ship)` placeholder and their locations are **inventoried** on this card (§6.6).
7. Given the community section, when the nav and CTAs are reviewed, then the nav label is **"Community"**
   and any "gang"/"troop" usage reads as **intentional flavor** (no broken or duplicated-looking inconsistency).
8. Given the roadmap HI-SCORE banner and phase copy, when a visitor reads them, then they read as
   **labelled goals/targets** (not live metrics), no figure implies current real-time activity, and
   phase-1 "all 3 modes live" is retained.
9. Given the `#token` panel, when the LP is built, then the dimmed-coin "TICKER/TBD/NOTHING TO BUY"
   visual is **kept as final** and its `TODO(art)`/"placeholder" markers are removed.
10. Given the Header logo, when a visitor clicks it, then it **scrolls to top / links home** rather than
    a dead "#".
11. Given all rendered images, when the LP is audited, then every image is a **final asset** and nothing
    falls back to `/placeholder.png`.
12. Given the change is complete, when verifying, then `npm run build` is clean, browser QA shows **0
    console errors**, and the built site contains no user-visible "Monkify"/"$MONKIFY".

---

## 11. Manual QA

1. In `monkify-lp`: `npm install` (if needed) → `npm run dev` → open http://localhost:4321.
2. **Modes:** hover/scroll each mode card — copy matches Vitor's delivered strings; no "6 letters · 6
   numbers", no "rabbit/monkey/jaguar/falcon", no "every 4 hours". Reduced-motion still keeps the static
   poster (toggle OS reduced-motion).
3. **#token:** the dimmed coin + TICKER/TBD/NOTHING TO BUY renders; no visual regression.
4. **Header logo:** click the wordmark → smooth-scrolls to the hero; no `#` jump.
5. **Community:** nav says "Community"; socials + Play now are obvious placeholders (hover shows `#` /
   the placeholder host), nothing looks broken.
6. **Roadmap:** the HI-SCORE banner + phases read as goals we're chasing, not live counters.
7. `npm run build` clean; DevTools console shows 0 errors; no image 404; search built `dist/` for
   `Monkify`/`$MONKIFY` → none user-visible.

---

## 12. Trello / pipeline

- **Source card:** [LP final adjustments (#78)](https://trello.com/c/3dPBofpy/78-lp-final-adjustments)
  — moved to **Specs created**; the **Implementation** section on the card lists the branch + spec.
- **Next:** `/monkify-implement-feature LP final adjustments` — **confirm Vitor's mode copy is in hand
  first** (§4), then implement in the LP worktree, then `/monkify-ship-feature LP final adjustments`
  (reads branch from the card; LP merges into **`master`**, meta into `main`).

---

## 13. Revision log

| Date | Change |
| --- | --- |
| 2026-07-21 | Initial spec from refined card #78 (LP-only pass; mode-copy gated on Vitor's copy). |
| 2026-07-21 | **All 4 gates resolved by Vitor.** Names → Cosmic Sprint / Beast Mode / Space Jackpot; blurbs supplied (Cosmic Sprint + Beast Mode **corrected** to kill the "6-letter" / "rabbit-monkey-jaguar-falcon" errors); two new GIFs (`cosmicsprint.gif`, `spacejackpot.gif`) saved + wired; URLs filled (`app.spacestroker.com/space-stroker/game`, X, Telegram) + **Discord removed**; LP self-domain `spacestroker.com` noted for meta. §1/§4/§6.1/§6.3/§6.6/§7/§8 updated. |

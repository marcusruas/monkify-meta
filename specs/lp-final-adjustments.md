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
| **Mode copy** (`GameModes.vue`) | Replace the 3 factually-wrong mode texts with Vitor's final copy, validated against the real Main types | ⛔ **Blocks on Vitor's copy** (§4) |
| **Mode GIFs** (`GameModes.vue`) | Keep QUICK RACES + LOTTERIES flagged `TODO(art)` (reuse `beastmode.gif`); Beast keeps its own | ⛔ art pending Vitor |
| **`#token` art** (`index.astro`) | Remove the `TODO(art)` / "placeholder" markers — the dimmed-coin "TBD" visual is **final** | ✅ now |
| **Header logo** (`Header.vue`) | `href="#"` → real home/top anchor (`#hero`) | ✅ now |
| **PLAY_URL + socials** (`index.astro`, `Header.vue`) | **Inventory** all placeholders; keep each an obvious `TODO(ship)` | ✅ now (fill at ship) |
| **Roadmap HI-SCORE + phases** (`SpaceRoadmap.vue`) | Audit copy reads as labelled **goals/targets**, never live metrics; retain phase-1 "all 3 modes live" | ✅ now (audit) |
| **Image audit** (site-wide) | Confirm every rendered image is final art; nothing reaches the `/placeholder.png` fallback | ✅ now (audit) |
| **Brand** | No user-visible `Monkify` / `$MONKIFY`; brand stays **Space Stroker** | ✅ now (verify) |

No backend, no DB, no migration, no SignalR, no tests (LP has no suite). DoD = `npm run build` clean +
browser QA with 0 console errors + brand sweep clean.

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

## 4. Blocking input from Vitor (gate on the mode-copy edit)

**The `GameModes.vue` text edit (AC #1–#3) is gated on Vitor supplying the final mode copy** (names /
tags / blurbs for all 3 modes). This is a hard product decision on the card, not an implementation
detail.

- `monkify-implement-feature` **must confirm the copy is in hand** before editing `GameModes.vue`. If
  the copy has not been delivered, **stop and ask the user** (per `INDEX.md` → "Implementing from a
  spec", treat as a blocker) — either wait for Vitor's copy, or get explicit approval to proceed with
  interim **factually-correct** copy that satisfies §5.
- Whatever copy arrives, the implementer **must verify it reintroduces none of the 3 factual errors**
  in §5 before shipping (that verification is AC #4).
- The **art gate** (new Races + Lottery GIFs, decision #4) is independent: the copy can land and ship
  while those two GIFs remain flagged placeholders. Do **not** block the copy edit on the art.

Everything in §6.2–§6.6 (token markers, header logo, URL inventory, roadmap audit, image audit) is
**not** gated and can proceed immediately.

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

### 6.1 `components/GameModes.vue` — mode copy (⛔ gated, §4) + GIF flags

The three modes are a `Mode[]` literal (`modes`, lines ~25–60). Apply Vitor's final copy to the
`name` / `tag` / `blurb` fields, replacing the three wrong strings:

- **`races`** — `tag: "6 LETTERS · 6 NUMBERS"` (line ~29) → Vitor's tag describing **one** 6-character
  alphanumeric race (no letters/numbers split). Blurb (line ~30) already says "6-character run" — keep
  consistent.
- **`beast`** — `blurb` containing `"rabbit, monkey, jaguar, falcon"` (line ~42) → Vitor's blurb
  reflecting **backing 1 of the 8 preset runners** (no invented animals). `tag: "BET ON THE BEAST"`
  may stay.
- **`lottery`** — `tag: "EVERY 4H · DAILY DRAW"` (line ~52) **and** blurb `"A draw every 4 hours,
  plus one fat daily."` (line ~54) → Vitor's copy stating a **once-a-day (24h)** draw with an
  all-day-growing pot.

**GIF flags (keep — decision #4):** `races.gif` and `lottery.gif` both `= asset(beastGif)` with an
existing `// TODO(art): placeholder — reuses the beast GIF …` comment (lines ~33–34 and ~56–57).
**Leave those comments in place** so the reuse stays documented (AC #5). Beast's `beastGif` stays. Do
**not** remove or "silently upgrade" the flags.

**Preserve** the reduced-motion GIF-swap logic (`reduceMotion`, the `activeKey` poster→GIF swap at the
`<img :src>` around line ~183) untouched — copy edits only.

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
- `PLAY_URL` (line ~7) is a **duplicate** of `index.astro`'s. Keep it flagged `TODO(ship)` (see §6.6).
  Deduping the two into one shared source is **optional / low priority** — if not done, both copies
  must carry the `TODO(ship)` comment and the identical placeholder value.
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

### 6.6 Placeholder URL inventory (✅ now — flag, don't fill)

Deliverable of AC #6: every unresolved link stays an obvious `TODO(ship)` placeholder **and** its
locations are inventoried here.

| Placeholder | Value (keep) | Location(s) | Flag |
| --- | --- | --- | --- |
| App URL | `https://PLACEHOLDER-app-url/space-stroker/game` | `pages/index.astro` `PLAY_URL` (line ~83; used at hero CTA ~171 and about CTA ~293) **and** `components/Header.vue` `PLAY_URL` (line ~7; nav + mobile CTA) | `TODO(ship)` — real route is `/space-stroker/game`, domain undecided |
| Twitter/X | `href="#"` | `pages/index.astro` `socials[0]` (line ~89), rendered in `#community` (~420–435) | `TODO(ship)` |
| Telegram | `href="#"` | `pages/index.astro` `socials[1]` (line ~95) | `TODO(ship)` |
| Discord | `href="#"` | `pages/index.astro` `socials[2]` (line ~101) | `TODO(ship)` |

Each already carries a `TODO(ship)` comment (`index.astro` lines ~82 and ~85; `Header.vue` line ~6).
**Verify** those comments remain and the values stay placeholders. Do **not** invent real URLs.

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

- Filling the **real app domain** (`PLAY_URL`) and the **3 social URLs** — deferred to **ship** (`TODO(ship)`).
- **Producing** the new QUICK RACES + LOTTERIES GIF art — pending from Vitor.
- **Authoring** the final mode copy — supplied by Vitor (this card only applies + validates it).
- `monkify-front` (game app) and `monkify-backend` — untouched.
- The forward-looking token/airdrop **roadmap phases 2–5** — they are the future plan, not this pass.
- Stale LP `PRODUCT.md` / `CLAUDE.md` cleanup — out of scope (prior known follow-up).

---

## 8. File checklist

### 8.1 LP (`monkify-lp`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/components/GameModes.vue` | ⛔ Apply Vitor's final `name`/`tag`/`blurb` for all 3 modes, killing the 3 factual errors (§6.1); keep the two `TODO(art)` GIF-reuse comments; preserve reduced-motion swap |
| 2 | `src/pages/index.astro` | Remove `#token` "placeholder — TODO art" markers (lines ~380, ~382), keep the visual; verify `PLAY_URL` + socials stay `TODO(ship)` placeholders (§6.2, §6.6) |
| 3 | `src/components/Header.vue` | Logo `href="#"` → `href="#hero"` (§6.3); keep `PLAY_URL` flagged; nav "Community" unchanged |
| 4 | `src/components/SpaceRoadmap.vue` | Audit HI-SCORE banner + phase copy read as goals/targets; retain phase-1 "all 3 modes live"; change only if an ambiguity is found (§6.4) |
| 5 | `src/components/SpaceBackground.vue` | Audit only — document `/placeholder.png` fallback is unreachable; no change expected (§6.5) |

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

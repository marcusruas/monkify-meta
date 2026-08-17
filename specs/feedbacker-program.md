# SpaceStroker — "Feedbacker Program" Feature — Implementation Spec

> **Status: Shipped — 2026-08-17**, via `feature/feedbacker-program` merged to `master` (`monkify-lp`)
> and `main` (`monkify-front`, `monkify-meta`). The distilled, current-truth summary now lives in
> [`../features/feedbacker.md`](../features/feedbacker.md); this spec stays as the deep design archive.
>
> **What shipped matches this document**, with one deliberate deviation and one open item:
> - **Deviation:** the `YOU ARE HERE ▾` chip renders **inside the current season's column** rather than
>   in the panel eyebrow, so its caret points at the bar it names and `currentSeason` visibly moves it
>   (§6.4.2 proposed this; it was built that way).
> - ⚠️ **Open, and it blocks the next LP deploy:** the **Streamflow treasury lock** (49% of supply on a
>   52-week release) landed on both base branches *while this card was in flight*, and the season
>   budgets are front-loaded against it — Season 1 asks 60M in a 6-week window in which the **whole**
>   locked treasury releases only ~56.5M. See [`../features/feedbacker.md`](../features/feedbacker.md)
>   → "Open, and it blocks the next LP deploy".
>
> Original header follows.
>
> **Status: Ready for implementation** — refined via Trello card
> [Feedbacker Program — LP section (design 4b) below the token + How to play guide section (#173)](https://trello.com/c/lf5uWFrX)
> (2026-08-17), spec written 2026-08-17.
> Repos affected: `monkify-lp` (branch `master`), `monkify-front` (branch `main`), and this doc in
> `monkify-meta`. **`monkify-backend` is NOT affected** — see §2.1.
>
> **Purpose:** announce the Feedbacker Program — the thing the token's 25% community slice actually
> pays for — on the two surfaces a player can reach it from: a new arcade section on the landing page,
> and a new section in the in-app **How to play** guide. It is an **announcement**, not a system:
> nothing here takes a submission, judges one, or pays one out.

---

## 1. Summary

One section of copy, twice, behind the token gate that already exists.

| Surface | Token gate closed (no mint) | Token gate open (mint configured) |
| --- | --- | --- |
| **LP, slot 3** | nothing renders | the `#feedbacker` section: eyebrow, head + season-budget chart, bug-bounty band, two judged-track cards, disclaimer |
| **LP nav** | no `Rewards` item (desktop or mobile) | `Rewards` is the **second** nav item, after `Token` |
| **LP `/terms`** | no Feedbacker clause | one number-free clause: discretion, one person/one wallet, permanent ban |
| **App guide** | no section, no nav chip | `FEEDBACKER`, between the token section and `PLAYER PREFERENCES` |
| **Backend / bets / payouts** | unchanged | **unchanged** — no endpoint, no entity, no migration |

Two things this deliberately costs, both accepted at refinement:

1. **"PAID WEEKLY" is a promise nothing in code enforces.** The section commits to a weekly payout
   cadence that is an operational commitment on the team, with no intake, queue or job behind it (D4).
2. **The section re-introduces token counts to LP copy that was deliberately scrubbed of them.**
   That is an explicit owner reversal, scoped to this section only — see §3.1.

**The design doc is the visual and copy authority:**
<https://claude.ai/design/p/7c2f2916-0976-4429-a30b-c05cd8efca4e?file=Feedbacker+Program+v4.dc.html>
→ option **`#4b`**. Every string in §6.4 is quoted from it verbatim.

⚠️ **`Feedbacker Program v5.dc.html` (`5a`/`5b`/`5c`) is NEWER than v4 and is REJECTED.** Turn 5 is
"the same three layouts with every figure taken out" — a `SMALL / MEDIUM / LARGE` word scale, no
budget chart, no Telegram button. The owner reviewed it during refinement and kept 4b's figures.
**Build 4b.** Do not "update" to the newer file.

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree per repo** (§2): `feature/feedbacker-program` at `.worktrees/feedbacker-program/<repo>`;
  the hubs stay on their default branch. ⚠️ **`monkify-lp`'s default branch is `master`**, so its
  worktree is cut from `origin/master`, not `origin/main`.
- **Push after branch work** — all three branches already exist on `origin`.
- **Secrets/config:** mock-only values in committed files. Nothing here reads a secret.
- **No fabricated live-activity numbers on the LP.** Every figure in this section is a fixed program
  number, not a live metric — no counter, no "paid so far", no ticker.
- **`prefers-reduced-motion`** is honoured by anything that animates.
- **Unit tests (§3):** N/A — neither affected repo has a test suite covering this. `monkify-front`'s
  `package.json` has `build` and `lint` and **no `test` script**; `monkify-lp` has no test suite at
  all. DoD is in §11.

### 2.1 Why `monkify-backend` is not affected — and why the integration-test mandate does not fire

The card's Level is **`Feature`**, and `constraints.md` §3 makes a happy-path integration test in
`SpaceStroker.Tests.Integration` mandatory for every new feature. **It does not apply here, and that is
recorded rather than skipped:** this feature has no endpoint, no entity, no EF mapping, no migration,
no consumer, no event and no SignalR contract. There is no backend behaviour to drive end to end, so
there is nothing for a Testcontainers test to assert. **No `monkify-backend` branch was created.**

If implementation discovers any backend surface at all, that assumption is void — stop and add the
integration test before shipping.

---

## 3. Resolved product decisions

Carried from the card's `## Decisions` (answer mode: short). The card is the source; this table is the
implementable form.

| # | Decision |
| --- | --- |
| D1 | **Both surfaces are gated on the token flag** — `TOKEN_LIVE` on the LP, `tokenLive` in the app. On a mint-less build the LP shows "No token. Yet." plus its anti-scam line; a "250M set aside for players" band directly under that would contradict it. |
| D2 | **The numbers live in a named constants block per repo, mirrored** — `FEEDBACKER` in `monkify-lp/src/constants.ts`, `monkify-front/src/constants/feedbacker.ts`. Includes `currentSeason`. Recorded in the KB as a move-together pair, like `TREASURY_SLICES`. |
| D3 | **LP pitches, the guide is canonical.** The LP band carries the numbers plus a `Full rules →` link to `${GAME_URL}/how-to-play#feedbacker`; the guide carries the full rules and eligibility prose. |
| D4 | **Announcement only.** No intake, endpoint, storage, moderation, payout code, or "paid so far" counter. "PAID WEEKLY" is an operational promise nothing in code enforces. |
| D5 | **A real 7th backdrop step** — `neb` hue ~263 (between `#token`'s 248 and `#about`'s 278) plus a new `.neb-p7`. The other five slots are untouched. |
| D6 | **`art=""`** — no scenery variant. Nebula, starfield, stars, dot grid and vignette still render. |
| D7 | **LP header gets a gated `Rewards` nav entry**, second, inside the same `TOKEN_LIVE` spread as `Token`. |
| D8 | **Slug is `feedbacker` on both surfaces.** The guide fragment is a permanent contract. Guide `navLabel: 'Feedbacker'`; the LP nav says `Rewards` only because of font width. |
| D9 | **A number-free clause in `/terms`** — discretion, one person/one wallet, permanent ban — under the same `TOKEN_LIVE` gate. |
| D10 | **New `feedbacker_click` GA event with a `source` label** (`lp_rules`), kept **out of** `cta_play_click`. |
| D11 | **Mobile: stack everything.** Tier left-border dividers become top dotted rules; the chart keeps 6 bars with VT323 labels. |
| D12 | **Level: `Feature`**, integration test N/A (§2.1). |
| D13 | **Build 4b WITH the token counts** — see §3.1. |
| D14 | **The 250M is the same pot as the 25% "Community & game rewards" slice**, and the section says so in one line. |
| D15 | **No Telegram button on the LP, and no Telegram link or constant in the app.** |

### 3.1 ⚠️ D13 is a deliberate reversal of a shipped rule — do not "fix" it back

`features/token-launch.md` §6 records the owner's call of 2026-08-12: *"don't talk in token units,
only in percentages."* Its example table's **first row is literally this section's headline**:

| Was | Is (per that rule) |
| --- | --- |
| `250,000,000 IS SET ASIDE FOR PLAYERS` | `25% OF THE SUPPLY IS SET ASIDE FOR PLAYERS` |

`tokensForPercent` and `UNLOCK_STEP_TOKENS` were **deleted** from
`monkify-front/src/constants/token.ts` precisely so that re-adding a count is a visible change to that
file rather than a one-line slip in a template.

Two alternatives were put to the owner during refinement and **both were rejected**:

1. **v5/5b** — every figure replaced by a `SMALL / MEDIUM / LARGE` word scale, chart deleted.
2. **Percentages of supply** — the season bars convert cleanly (`60M/50M/40M/30M/20M` →
   `6% / 5% / 4% / 3% / 2%`), but the per-submission prices collapse to
   `0.01% / 0.03% / 0.06% / 0.05% / 0.005%` — three near-identical strings differing in the second
   decimal, in a pixel font.

**Counts ship.** The reversal is **scoped to this section**: `TokenSection.vue`, `HomeTokenCard.vue`
and `GuideToken.vue` keep their percentages, and `tokensForPercent` stays deleted. `formatTokens` is
**not** reintroduced — every figure in this section is a hand-written display string in the
`FEEDBACKER` block (§5), never a count derived from `TOTAL_SUPPLY`.

At ship time this exception must be written into `features/token-launch.md` §6 and the new
`features/feedbacker.md`, or the next agent reading the rule will correct it back.

### 3.2 ⚠️ The 250M is not a second pot (D14)

`monkify-lp/src/constants.ts` → `TREASURY_SLICES[0]` is already:

```ts
{
  label: "Community & game rewards",
  percent: 25,                       // = 250,000,000 of the 1,000,000,000 supply
  tone: "primary",
  note: "Random TG drops, jackpot seed, game feedbackers.",
}
```

and its `monkify-front` mirror's note carries the owner's 2026-08-12 wording — *"the feedback /
bug-hunter programs belong in here … paid out of this slice, not out of marketing."*

So the section's `250M SET ASIDE FOR PLAYERS` and the token section's `25%`, **two slots apart on the
same page**, are the same money. Without an explicit tie-in a reader adds them to 500M. One line in
the section resolves it — and because that slice **also** funds TG drops and the jackpot seed, the
copy says *set aside for players*, never *set aside for this program*.

⚠️ `TREASURY_SLICES` itself does **not** change. Its note already names "game feedbackers".

---

## 4. Data model

**N/A.** No entity, no column, no EF mapping, no migration. Nothing is persisted by this feature.

---

## 5. Configuration — the `FEEDBACKER` constants block (D2)

The one new piece of shared state. It exists so a season roll is an edit in two named places rather
than a hunt through markup, and so the two repos cannot silently disagree.

### 5.1 `monkify-lp/src/constants.ts`

Add below the token block. Mirror the commenting style of `TREASURY_SLICES`.

```ts
// --- Feedbacker Program (#173) ---------------------------------------------------------------
// ⚠️ A MIRROR OF `monkify-front/src/constants/feedbacker.ts`. Both surfaces state the SAME figures
// (spec §3.1 — counts, deliberately, not percentages), so a change here moves in BOTH repos in the
// same change or the LP and the app start promising different programs.
//
// ⚠️ THESE ARE DISPLAY STRINGS, NOT DERIVED COUNTS. `tokensForPercent` was deleted with the
// percentages-only pass and is NOT coming back — nothing here reads TOTAL_SUPPLY.

export type FeedbackerSeason = {
  /** Bar label, first half: `S1`. */
  id: string;
  /** Millions of tokens. Rendered as `${budget}M`, and the bar height derives from it (§6.4.2). */
  budget: number;
  /** Bar label, second half: `6 WKS`, `QTLY`. */
  cadence: string;
};

export const FEEDBACKER = {
  /** The headline figure. The WHOLE community & game-rewards slice — see spec §3.2. */
  totalBudget: "250M",
  /** 1-based. Drives which column is highlighted and where `YOU ARE HERE ▾` sits (§6.4.2). */
  currentSeason: 1,
  seasons: [
    { id: "S1", budget: 60, cadence: "6 WKS" },
    { id: "S2", budget: 50, cadence: "8 WKS" },
    { id: "S3", budget: 40, cadence: "8 WKS" },
    { id: "S4", budget: 30, cadence: "QTLY" },
    { id: "S5", budget: 20, cadence: "QTLY" },
  ] as FeedbackerSeason[],
  /** The bug band's headline range. */
  bugRange: "100k – 600k",
  bugTiers: [
    { amount: "100k", body: "Cosmetic. Typos, a broken layout, a button you have to press twice." },
    { amount: "300k", body: "A flow that fails. You cannot place a bet, or the result never shows." },
    { amount: "600k", body: "Money or round integrity. A missing payout, or access you should not have." },
  ],
  ideaMax: "up to 500k",
  makeMax: "up to 50k",
  /** Per person, per season. Stated on the section; deliberately NOT in the Terms clause (§6.6). */
  seasonCap: "2.5M",
} as const;
```

### 5.2 `monkify-front/src/constants/feedbacker.ts`

A new file next to `token.ts`, carrying the **same values** with the mirror warning pointing the other
way. The app needs `totalBudget`, `seasons` (for prose, not a chart), `bugTiers`, `ideaMax`,
`makeMax`, `seasonCap`; it does not need `bugRange`.

⚠️ **The app does not import from the LP and vice versa** — they are separate repos with separate
builds. This is a hand-maintained mirror, exactly like `TREASURY_SLICES`, and the KB records it as
such at ship time.

### 5.3 No env vars

Nothing new is read from `import.meta.env` (LP) or `process.env` / `VUE_APP_*` (front). The only gate
is the **existing** `TOKEN_LIVE` / `tokenLive`, both already derived from the mint.

---

## 6. Landing page behaviour (`monkify-lp`)

### 6.1 Placement

A new `<ContentSection id="feedbacker">` in `src/pages/index.astro`, **between** `#token` (~line 247)
and `#about` (~line 258).

```
hero → #token → #feedbacker → #about → #modes → #story → #roadmap → #community
```

### 6.2 ⚠️ The backdrop props are POSITIONAL (D5)

`ContentSection.vue`'s comment at `index.astro:241-246` is the rule: `hue` / `neb` / `pattern` /
`nebOpacity` belong to the **slot**, not the section; only `art` travels with the content.

The `neb` hue is a **monotonic sweep down the page**: `248 → 278 → 305 → 332 → 8 → 42`. Inserting a
seventh slot at position 3 therefore takes a hue **between 248 and 278**, which is the one insertion
that keeps the sweep one-way **without rebalancing the other five**.

| Prop | Value | Why |
| --- | --- | --- |
| `hue` | `-4` | Starfield hue-rotate. Between `#token`'s `6` and `#about`'s `-14`. |
| `neb` | `oklch(0.60 0.14 263)` | Midpoint of 248 and 278. Lightness/chroma interpolated from its neighbours (0.62/0.13 and 0.58/0.15). |
| `pattern` | `7` | **A new `.neb-p7`** — see §6.3. |
| `nebOpacity` | `0.66` | Between `#token`'s `0.58` and `#about`'s `0.74`. |
| `art` | `""` | D6. |

⚠️ **Do not reuse `pattern={1}`.** `#token`, directly above, is already `pattern={1}` — two identical
cloud layouts stacked is exactly what the numbered patterns exist to prevent.

### 6.3 The new `.neb-p7` (`src/styles/global.css`, after `.neb-p6` at ~line 737)

Six lines in the shape of its neighbours. Read the block comment at ~line 695 first: blooms stay in
the outer thirds horizontally (centre column clear for copy) and roughly a 32–68% vertical band.

The section sits between the token slot (`.neb-p1`, medium/balanced) and about (`.neb-p2`, big and
strong), so it takes a **medium-large** layout whose dominant bloom sits on the **opposite** side from
`.neb-p1`'s, so the two adjacent sections do not read as one continuous cloud:

```css
.neb-p7 { /* feedbacker — medium-large, dominant bloom mirrored away from p1's */
  background-image:
    radial-gradient(56% 50% at 94% 34%, color-mix(in oklab, var(--neb, var(--color-cyan)) 48%, transparent) 0%, transparent 62%),
    radial-gradient(50% 46% at 4% 62%,  color-mix(in oklab, var(--neb, var(--color-cyan)) 40%, transparent) 0%, transparent 60%),
    radial-gradient(32% 32% at 22% 30%, color-mix(in oklab, var(--neb, var(--color-cyan)) 24%, transparent) 0%, transparent 54%);
}
```

`ContentSection.vue`'s `pattern` prop doc comment (line 26) says "Bloom layout (1–6)" — **update it to
1–7** in the same change, or the next reader trusts a stale range.

### 6.4 The section component

**A new `src/components/FeedbackerSection.vue`, rendered with NO `client:*` directive.**

⚠️ This is deliberate and it matters. After D15 removed the Telegram button, the section has **zero
interactivity** — no clipboard write, no toggle, no timer. Astro renders a Vue SFC with no client
directive to **static HTML with no JS and no island**, which the page already does for `PixelText` and
`PrimaryButton` (`index.astro:153`, `:167`). Consequences:

- No hydration cost on the page's heaviest new markup block.
- **The `scroll-reveal` hydration-mismatch trap cannot occur here** — there is no island to mismatch.
  The class still goes on a static wrapper in `index.astro` (consistent with the token slot), but for
  layout consistency rather than to dodge a bug.

Do **not** reach for a `.astro` component: the repo has none, and every component in `src/components/`
is a `.vue` SFC.

#### 6.4.1 Copy — verbatim from `#4b`

Quoted exactly. Do not rewrite, re-punctuate, or "improve".

| Slot | String |
| --- | --- |
| Eyebrow chip | `THE FEEDBACKER PROGRAM` |
| Eyebrow kicker | `250M SET ASIDE FOR PLAYERS` |
| Headline | `It will never be this good again` — with **`never be this good`** as the coloured fragment |
| Lede | `We will actively reward the community in many ways, find bugs, give suggestions or make banger posts.` |
| Panel kicker | `SEASON BUDGET` |
| Panel chip | `YOU ARE HERE ▾` |
| Band eyebrow | `WHAT IT PAYS FOR` (cyan) · `SUBMIT ON TELEGRAM · PAID WEEKLY` (muted) |
| Bug band | `100k – 600k` · `Find a bug` · chip `FIXED PRICES` |
| Bug tiers | see `FEEDBACKER.bugTiers` (§5.1) — the three bodies are verbatim |
| Idea card | `up to 500k` (cyan) · `Tell us what to build` · badge `JUDGED BY HAND` · `Excited about the project? Tell us what you think we could do to make it even better! We are open to all kinds of feedback, especially about the game and the token.` |
| Make card | `up to 50k` (magenta) · `Make something` · badge `JUDGED BY HAND` · `Your own meme or post on X, TikTok or Reddit, from a real account. Link the site, tag it #ad, and talk about the game rather than the chart.` |
| Disclaimer | `One person, one wallet, 2.5M a season at most. We decide what gets approved and that is the end of it. Anything we pay for has to say we paid for it, and multi-accounting or stolen work gets you banned for good.` |

**Three deltas against the artifact**, all decided at refinement:

1. **No `Join the Telegram` button** (D15). The head's left column ends at the lede.
2. **One added line** tying the budget to the community slice (D14). Suggested, matching the page's
   voice and `TREASURY_SLICES[0].note`: *"It comes out of the same 25% community slice the token
   section above describes — the one that also seeds the jackpot and the Telegram drops."* The
   **claim** is fixed; the wording may be tuned.
3. **A `Full rules →` link** to the guide (D3, §6.5).

#### 6.4.2 The season-budget chart

Six columns: the five `FEEDBACKER.seasons` plus a decorative tail.

- **Bar heights derive, never hand-written**: `height = (season.budget / seasons[0].budget) * 100`.
  This reproduces 4b exactly (`100 / 83 / 66 / 50 / 33`) and, like `TREASURY_SLICES`, means a changed
  figure cannot leave the chart lying.
- **The tail column** is `height: 20%`, `opacity: .45`, label `AND DOWN`, and its amount is a **down
  arrow** — see §6.7 for why that glyph cannot be a bare character. It is decorative and is **not** a
  `seasons` entry.
- **Amount colours** step from `--color-primary-bright` down through progressively muted tones (4b
  uses `#e8890f`, `#b0762a`, `#8a7b63`, `#6f6880`, `#5a5468`). Port these to the LP's own tokens
  (`--color-primary`, `--color-primary-deep`, `--color-ink-mute`, `--color-line`) rather than pasting
  the hexes — see §6.4.3.
- ⚠️ **`currentSeason` must be observable.** The `YOU ARE HERE ▾` chip renders **inside the current
  season's column**, above its bar, and that column's label takes `--color-primary-bright`. This is a
  **deliberate small deviation from 4b**, where the chip sits in the panel eyerow and its `▾` points
  at nothing in particular: anchored to the column, the caret means what it draws, and changing
  `currentSeason` to `2` visibly moves it (AC 8). If the owner prefers the eyerow position, the
  highlight alone still has to move.

#### 6.4.3 Port to the LP's primitives — do not paste the artifact's CSS

The design doc ships a **standalone** token set (`--primary`, `--surface`, `--line`, `.panel`,
`.chip`, `.spec`, `--disp`, `--body`) at a fixed `width: 1180px`. None of it comes across. The LP
already has, in `src/styles/global.css` (there is **no `tailwind.config`** — the theme is `@theme`
inline):

| Artifact | Use instead | Where |
| --- | --- | --- |
| `.panel` | `.arcade-panel` (+ `.arcade-panel--accent`) | `global.css:285` |
| `.chip` | `.chip` | `global.css:365` |
| `.chip` (cyan) | `.chip--cyan` | `global.css:380` |
| `.rule` | `.pixel-rule` | `global.css:492` |
| `--disp` / `--body` | `--font-display` / `--font-body` (`font-display` / `font-body` utilities) | `@theme` |
| `--primary` etc. | `--color-primary`, `--color-primary-bright`, `--color-primary-deep`, `--color-cyan`, `--color-magenta`, `--color-surface`, `--color-line`, `--color-ink`, `--color-ink-dim`, `--color-ink-mute` | `@theme` |
| fixed `1180px` | the page's own `max-w-6xl` container inside `ContentSection` | `ContentSection.vue:121` |

⚠️ **`.chip--magenta` does not exist.** `global.css` has `.chip` (primary) and `.chip--cyan` only, and
4b's "Make something" card is magenta. **Add `.chip--magenta`** next to `.chip--cyan`, in the same
three-property shape (`border-color`, `color`, `background` off `--color-magenta`). Note the LP's own
convention comment calls magenta "live/bet pulse only, never structural" — a badge on a card is
decorative accent, not structure, so this is in bounds; keep it to the badge.

#### 6.4.4 Mobile (D11)

4b is drawn desktop-only at 1180px. Three things do not survive a narrow viewport:

| Desktop | Below `md` |
| --- | --- |
| Two-column head (copy \| budget panel) | one column: headline, lede, then the panel |
| Three tier columns divided by **left borders** (`.tiercol-mid`, `.tiercol-last`) | three stacked full-width rows, dividers become **top** rules — reuse `.pixel-rule` |
| Two equal judged cards side by side | two stacked rows |
| Chart labels in Press Start 2P | keep all six bars (they are narrow); labels drop to `--font-body` (VT323) so `S1 · 6 WKS` fits at 320px |

The section must not introduce horizontal page scroll at 320px.

### 6.5 The `Full rules →` link (D3)

```
href = `${GAME_URL}/how-to-play#feedbacker`
```

⚠️ **This is the LP's first deep link into an app subpage.** Every LP link today is the bare
`GAME_URL` origin (`index.astro:167`, `:338`, `Header.vue:105`, `:154`). Two existing delegated
listeners in `src/layouts/Layout.astro` match on `link.href.indexOf(gameUrl) === 0`, so both will
see it:

1. **Campaign forwarding** (`Layout.astro:214-228`) — rewrites the href via `new URL()` +
   `searchParams.set()`. Verified safe: that puts `?utm_*` **before** the `#feedbacker` fragment, so
   the anchor still resolves. **No change needed.**
2. **`cta_play_click`** (`Layout.astro:272-276`) — see §6.6.

### 6.6 Analytics — a new `feedbacker_click`, kept out of `cta_play_click` (D10)

**The bug this prevents.** `Layout.astro:272-276` fires `cta_play_click` for **any** clicked link
whose href starts with `GAME_URL`, falling back to `cta: 'unknown'` when there is no `data-cta`. So
the `Full rules →` link would, with no code at all, report as `cta_play_click / unknown` — the same
value real unlabelled play buttons fall back to, permanently inflating the LP's play-CTA series with
clicks that never reached the game and cannot be told apart afterwards.

**The fix.** Give the rules link its own event and an opt-out from the play listener:

- Mark the link with a dedicated attribute (e.g. `data-track-event="feedbacker_click"` plus
  `data-track-source="lp_rules"`).
- In the `cta_play_click` listener, **before** it fires: if the link carries `data-track-event`, call
  `window.ssTrack(link.dataset.trackEvent, { source: link.dataset.trackSource })` and `return`.
- Everything without that attribute behaves exactly as today.

⚠️ **Deliberately not folded into `cta_play_click`** — the same reasoning that kept `token_click` out
of `home_link_click` in the app (`features/token-launch.md` §7): that series' documented meaning is
"who reached for the game", and this one counts "who wanted the rules". One source value ships
(`lp_rules`); the `source` shape is kept so a second surface can be added without renaming the event.

Both listeners are inside the `{gaId && (...)}` guard, so with no measurement id nothing fires and the
link still navigates. Preserve that.

### 6.7 ⚠️ Glyph safety — narrower than the card implied

Press Start 2P is **self-hosted with Google's own `latin` + `latin-ext` subsets** (`global.css:9`).

| Glyph | In subset? | Action |
| --- | --- | --- |
| `·` (U+00B7, middle dot) | **Yes** — `latin` covers U+00A0–00FF | use as a bare character |
| `–` (U+2013, en dash, in `100k – 600k`) | **Yes** — `latin` includes U+2013/2014 | bare character |
| `▾` (U+25BE) in `YOU ARE HERE ▾` | **No** | must be drawn |
| `↓` (U+2193), the chart's tail amount | **No** | must be drawn |

Neither missing glyph is tofu — the browser falls back to `system-ui`, so a smooth vector caret lands
in the middle of a pixel-font row. Draw them instead. `src/icons.ts` currently registers only **three**
marks (`b`, `burn`, `jackpot`) rendered by `PixelIcon.vue` (`name`, `size`, and ⚠️ **pixel-exact only
at multiples of 8**). Add a `caretDown` (and reuse it rotated, or add `arrowDown`) to `icons.ts` and
its `IconName` union, or draw both with CSS borders. Either is fine; a bare character is not.

⚠️ **`PixelText` is NOT the tool here.** Its own doc says: *"REACH FOR IT ON DISPLAY TYPE, NOT ON THE
8px STENCIL LABELS"* — the correction is ~0.12em, invisible on a chip and pure DOM cost. Use it on the
**headline** and the **large amount strings** (`100k – 600k`, `up to 500k`, `up to 50k`, the `60M`
column amounts); do **not** wrap the chips, kickers or tier labels in it.

### 6.8 Header nav (D7)

`src/components/Header.vue:13-19`. Desktop and mobile both iterate the one `nav` array, so gating it
there covers both:

```ts
const nav = [
  ...(TOKEN_LIVE
    ? [
        { label: "Token", href: "#token" },
        { label: "Rewards", href: "#feedbacker" },
      ]
    : []),
  { label: "Basics", href: "#about" },
  // …unchanged
];
```

`Rewards`, not `Feedbacker`: the row is Press Start 2P and gets tight at `md` (D7/D8). Nothing else in
the nav is renamed or reordered.

### 6.9 The Terms clause (D9)

`src/pages/terms.astro` is a flat sequence of `<h2>` + prose blocks. Add one new block **after
`PAYOUTS AND THE HOUSE CUT`** (~line 64) and before `WHEN A BET IS REFUNDED` — both concern money
moving from us to a player, which is the section's own organising logic.

- Heading in the file's existing style:
  `<h2 class="mt-10 font-display text-[0.8rem] text-primary">REWARDS FOR FEEDBACK</h2>`
- Content: awards are **discretionary**; **one person, one wallet**; paid work must be disclosed;
  multi-accounting or passing off someone else's work off as your own ends participation
  **permanently**.
- ⚠️ **No amount, cap, tier price or season figure.** They live in `FEEDBACKER` (§5) and change every
  season; `/terms` is hand-maintained and already has a documented history of going stale (the house
  cut sat at 0.5% for a while after the rate moved to 1%). Keeping numbers out means a season roll
  never touches this page.
- Gated: `terms.astro` does **not** import `TOKEN_LIVE` today — add the import and wrap the block, so
  a mint-less build does not carry terms for a program it never advertises.

---

## 7. App guide behaviour (`monkify-front`)

Same content, guide voice. **Not** a port of the LP's full-viewport 4b layout.

### 7.1 Registry entry

`src/constants/guideSections.ts` is the whole contract: `GUIDE_SECTIONS` drives **both** the nav and
the body, and `HowToPlayPage.vue` reads nothing else (`HowToPlayPage.vue:196-203`).

1. Add `'feedbacker'` to the **`GuideSectionId` union** (line 60). ⚠️ That union is a **URL contract** —
   other surfaces deep-link to guide fragments, and as of this card the **LP** does too. Never rename.
2. Add a `FEEDBACKER_SECTION` const beside `TOKEN_SECTION`, gated the same way, and spread it
   **immediately after** the token spread (line 186), before `player-preferences`:

```ts
...(tokenLive ? [TOKEN_SECTION] : []),
...(tokenLive ? [FEEDBACKER_SECTION] : []),
{ id: 'player-preferences', /* …unchanged */ },
```

   (Or one combined spread — either is fine; keep the gate identical.)
3. `navLabel: 'Feedbacker'` — short enough for the phone chip row, and it names the program. ⚠️ Not
   `Rewards`: in the app that collides conceptually with the existing **WHAT YOU WIN** section, and a
   player could not tell which one explains payouts.
4. `title: 'THE FEEDBACKER PROGRAM'` — short all-caps, matching the page.

⚠️ The no-em-dash rule (owner, 2026-08-12) is scoped to `#token`. It does **not** extend to this
section, and neighbouring sections are not swept.

### 7.2 `src/components/guide/sections/GuideFeedbacker.vue`

**`GuideRefunds.vue` is the model** — a `rows` array, a lede `<p>`, and `<guide-fact-list>`. The page
supplies the `GuideSectionCard` shell; the section component renders only its body.

```vue
<template>
  <div class="flex flex-col gap-5">
    <p class="vt323-regular m-0 max-w-[80ch] text-[18px] leading-[1.35] text-ink-dim"> … </p>
    <guide-fact-list :rows="rows" />
  </div>
</template>
```

⚠️ **Two corrections to the card's "reuse `GuideRail`, `GuideFactList`, `GuideTip`":**

- **Do NOT use `GuideTip`.** Its own header says *"AT MOST ONE PER SECTION"* and *"there is exactly
  ONE render of this component on the page"* — the merged HOW THE GAME WORKS section's closing tip,
  which is the **sole consumer of `.arcade-frame--black`**. A second one makes the page's one black
  surface two and reads the rule too loosely.
- **Do NOT use `GuideRail`.** It is documented for **ordered sequences** only (*"Do not copy this
  component into a section whose items are not a sequence"*) and every step requires a `GuideArt`
  that renders into a 172px 4:3 frame. The bug tiers and the two judged tracks are an unordered set,
  and this feature ships no art.

`GuideFactList` with the default `columns: 2` is correct: an unordered set of facts, which is what 5
of the 8 existing sections use. Do not pass `3` — that is reserved for a three-beat timeline.

### 7.3 Content

Rows (titles short and all-caps, bodies saying what the thing DOES — the voice rules at the top of
`guideSections.ts`), covering: what the program is; the three bug tiers and their prices; the two
judged tracks and their ceilings; where submissions go (the community Telegram, **named as a place,
not linked** — D15); the payout cadence; and the eligibility rules that the LP's disclaimer
compresses (one person / one wallet, the `2.5M` season cap, disclosure, the permanent ban).

The guide is the **canonical long-form** (D3), so it may say more than the LP — it must not say
anything the LP contradicts. Same counts as the LP (§3.1); source them from
`@/constants/feedbacker`.

### 7.4 Analytics — nothing new

`HowToPlayPage.vue:37` already fires `trackGuideSectionClick({ section: id })` off the registry id.
The new section contributes a new `section` value for free. **No new event, no new wiring.**

### 7.5 Deep links resolve safely on a mint-less build

`HowToPlayPage.vue:27-28`'s `isKnownSection` matches the hash against `GUIDE_SECTIONS`, so
`/how-to-play#feedbacker` on a build without the mint lands at the top of the guide rather than
throwing on a selector for an element that does not exist — exactly as `#token` already does.

---

## 8. Flow — what a click on `Full rules →` does

```mermaid
sequenceDiagram
    participant V as Visitor (LP)
    participant L as Layout.astro listeners
    participant G as GA4
    participant A as app.spacestroker.com

    V->>L: click "Full rules →" (href = GAME_URL + /how-to-play#feedbacker)
    Note over L: capture-phase listener #1 — campaign forwarding
    L->>L: new URL(href); searchParams.set(utm_*)<br/>⇒ …/how-to-play?utm_source=x#feedbacker
    Note over L: capture-phase listener #2 — analytics
    alt link has data-track-event (NEW)
        L->>G: feedbacker_click { source: "lp_rules" }
    else any other GAME_URL link
        L->>G: cta_play_click { cta: data-cta ?? "unknown" }
    end
    L-->>A: default navigation proceeds
    A->>A: isKnownSection("feedbacker") ⇒ scroll to section
```

---

## 9. Out of scope

- Any intake, submission form, endpoint, storage, moderation queue or payout automation (D4); any
  "paid out so far" counter or live figure.
- Reversing the "percentages only" rule anywhere **other than** this section — `TokenSection.vue`,
  `HomeTokenCard.vue` and `GuideToken.vue` keep their percentages, and `tokensForPercent` stays
  deleted (§3.1).
- Authoring a `feedbacker` space-art scene (D6).
- A dedicated `/feedbacker-terms` LP page (D9 chose a clause in the existing Terms).
- Any change to the four places the LP's Telegram portal handle lives, and any new handle constant in
  `monkify-front` (D15).
- Any change to `TREASURY_SLICES` in either repo (§3.2).
- Fixing the LP repo's stale `PRODUCT.md` / `CLAUDE.md`, which still describe the old memecoin
  product.

---

## 10. File checklist

### 10.1 Landing page (`monkify-lp`, branch `feature/feedbacker-program`, **`master`**, npm)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/constants.ts` | Add the `FEEDBACKER` block + `FeedbackerSeason` type (§5.1), below the token block. |
| 2 | `src/components/FeedbackerSection.vue` | **New.** The whole 4b section, built on LP primitives (§6.4). |
| 3 | `src/pages/index.astro` | Import it; new `<ContentSection id="feedbacker">` between `#token` and `#about` with the §6.2 props, no `client:*` directive, `scroll-reveal` on a static wrapper. |
| 4 | `src/styles/global.css` | Add `.neb-p7` after `.neb-p6` (§6.3); add `.chip--magenta` after `.chip--cyan` (§6.4.3). |
| 5 | `src/components/ContentSection.vue` | Comment only: `pattern` doc says "1–6" → "1–7". |
| 6 | `src/components/Header.vue` | Add the gated `Rewards` nav entry, second (§6.8). |
| 7 | `src/pages/terms.astro` | Import `TOKEN_LIVE`; add the gated, number-free `REWARDS FOR FEEDBACK` clause (§6.9). |
| 8 | `src/layouts/Layout.astro` | `cta_play_click` listener: honour `data-track-event` and return (§6.6). |
| 9 | `src/icons.ts` + `src/components/PixelIcon.vue` | Add the caret/arrow mark(s) + `IconName` member — **or** draw them in CSS and skip this row (§6.7). |

### 10.2 App (`monkify-front`, branch `feature/feedbacker-program`, `main`, yarn)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/constants/feedbacker.ts` | **New.** Mirror of the LP block (§5.2). |
| 2 | `src/constants/guideSections.ts` | `'feedbacker'` into `GuideSectionId`; `FEEDBACKER_SECTION` const; gated spread after the token spread (§7.1). |
| 3 | `src/components/guide/sections/GuideFeedbacker.vue` | **New.** Lede + `GuideFactList` rows (§7.2–7.3). |

### 10.3 Meta (`monkify-meta`, branch `feature/feedbacker-program`, `main`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/feedbacker-program.md` | This document. |
| 2 | `INDEX.md` | Map row + "Feature currently in flight". |
| 3 | `features/feedbacker.md` | **At ship time** (`Feature` ⇒ dedicated doc). |
| 4 | `features/token-launch.md` | **At ship time:** record the §3.1 counts exception in §6, or it gets "fixed" back. |
| 5 | `features/lp.md` · `features/frontend-ux.md` | **At ship time:** new section in the page order; new guide section. |

---

## 11. Tests and definition of done

**There is no automated test to write.** `monkify-lp` has no test suite; `monkify-front`'s
`package.json` exposes `build` and `lint` and **no `test` script**. The backend integration mandate is
N/A and the reason is recorded in §2.1 — do not invent a test to satisfy the letter of the rule.

DoD, all four required:

| # | Gate | Command / check |
| --- | --- | --- |
| 1 | LP builds clean | `npm run build` in the `monkify-lp` worktree |
| 2 | App lints and builds clean | `yarn lint` and `yarn build` in the `monkify-front` worktree |
| 3 | `monkify-backend` untouched | `git status` shows no changes; no branch was created |
| 4 | Browser QA | §13, at desktop **and** 320px portrait, in both gate states |

⚠️ **`monkify-front` build gotchas that have bitten this repo before** — see
`features/frontend-ux.md` and the LP notes: the docker `yarn build` fails on **CRLF** (prettier's
`endOfLine: lf`), and a front worktree can build a **stale** image because `yarn.lock` is gitignored.
If building the front in Docker from the worktree, copy the hub's `yarn.lock` in first and verify the
served bundle actually changed.

---

## 12. Acceptance criteria

Mapped 1:1 from the Trello **Acceptance Criteria** checklist (23 items).

1. Given a build with the token mint configured, when a visitor loads the LP, then a `#feedbacker`
   section renders between `#token` and `#about`, and the page order is hero / token / feedbacker /
   about / modes / story / roadmap / community.
2. Given a build with NO token mint configured, when a visitor loads the LP, then the `#feedbacker`
   section, its `Rewards` nav entry and its Terms clause are all absent from the built HTML, and the
   "No token. Yet." panel and anti-scam line are unchanged.
3. Given a build with NO mint, when the app renders `/how-to-play`, then no `feedbacker` entry exists
   in `GUIDE_SECTIONS`, the nav has no `Feedbacker` chip, and opening `/how-to-play#feedbacker` lands
   at the top of the guide without throwing.
4. Given a mint-configured app build, when a player opens `/how-to-play`, then a `Feedbacker` section
   renders immediately after the token section and before `player-preferences`, its nav chip reads
   `Feedbacker`, and its fragment is exactly `#feedbacker`.
5. Given the LP section is rendered, when its copy is compared to design `#4b`, then every figure
   appears as a token count (250M set aside; 60M/50M/40M/30M/20M season bars; 100k/300k/600k tiers;
   up to 500k; up to 50k; 2.5M season cap) and no figure has been converted to a percentage or a word
   scale.
6. Given the section states 250M, when a reader compares it to the token section above, then one line
   in the section identifies that budget as the same 25% "Community & game rewards" slice, and the
   copy says "set aside for players" rather than claiming the whole slice funds this program.
7. Given the program numbers, when they are located in the source, then they come from a `FEEDBACKER`
   constants block in `monkify-lp/src/constants.ts` and a mirrored
   `monkify-front/src/constants/feedbacker.ts` (including a `currentSeason` index), and no amount is
   hard-coded in markup on either surface.
8. Given `currentSeason` is changed to season 2, when the LP is rebuilt, then the `YOU ARE HERE` chip
   moves to the S2 bar with no markup edit.
9. Given the LP section renders, when a visitor clicks "Full rules →", then they land on
   `${GAME_URL}/how-to-play#feedbacker` scrolled to that guide section, and any `?utm_*` carried by
   the campaign-forwarding script appears before the `#feedbacker` fragment.
10. Given GA is enabled, when a visitor clicks "Full rules →", then exactly one `feedbacker_click`
    event fires with `source: 'lp_rules'` and NO `cta_play_click` event fires for that click.
11. Given GA is enabled, when a visitor clicks any existing "Play now" CTA, then `cta_play_click`
    still fires with its original `data-cta` value — the opt-out added for the rules link has not
    broken the play-CTA series.
12. Given a mint-configured LP build, when the header nav is inspected at desktop and in the mobile
    menu, then a `Rewards` entry pointing at `#feedbacker` sits second (after `Token`), it comes from
    the same one nav array, and clicking it scrolls to the section.
13. Given the new section's `ContentSection`, when its props are inspected, then its `neb` hue sits
    between 248 and 278 (keeping the sweep monotonic), it uses a newly authored `.neb-p7` (no other
    section's pattern number changed), `art` is empty, and the other six slots' `hue`/`neb`/`pattern`/
    `nebOpacity` are byte-identical to before.
14. Given a 320px-wide viewport, when the section is scrolled through, then the head is one column,
    the three bug tiers are stacked full-width rows separated by top dotted rules, the two-card band
    is two rows, no content overflows horizontally, and the page body does not scroll sideways.
15. Given the section is built, when its markup is inspected, then `scroll-reveal` sits on a static
    wrapper rather than on a hydrated island root, and loading the page produces no Vue
    hydration-mismatch error in the console.
16. Given the section renders in a browser, when the down-arrow, the `YOU ARE HERE` caret and the
    mid-dot separators are inspected, then each is drawn via `PixelText`/`PixelIcon`/geometry rather
    than a bare character, and no glyph renders as a tofu box or wrong shape.
    ⚠️ **Read with §6.7:** the mid-dot and en dash **are** in the font's subset and stay bare
    characters; what must be drawn is the `▾` and the `↓`. The AC's intent is "no fallback-font glyph
    lands in a pixel-font row".
17. Given the section is styled, when its CSS is reviewed, then it uses the LP's own primitives
    (`arcade-panel`, `chip`, `pixel-rule`, `font-display`, `@theme` tokens) and none of the design
    doc's standalone `--primary`/`--surface`/`.panel`/`.spec` token set or its fixed 1180px width was
    pasted in.
18. Given a mint-configured LP build, when `/terms` is opened, then it carries a Feedbacker clause
    covering discretion, one person / one wallet, and the permanent-ban condition, and that clause
    contains no amount, cap or season figure.
19. Given the shipped surfaces, when they are searched for Telegram links, then the LP section head
    has no `Join the Telegram` button (the `SUBMIT ON TELEGRAM · PAID WEEKLY` eyebrow remains as
    text), `monkify-front` still contains no `t.me` handle constant, and the LP's four existing
    portal-handle locations are unchanged.
20. Given a player clicks the `Feedbacker` chip in the guide nav, when analytics is inspected, then
    `trackGuideSectionClick` fires with `section: 'feedbacker'` and no new tracking wiring was added
    to do it.
21. Given the guide section is written, when its prose is reviewed against the voice rules at the top
    of `guideSections.ts`, then it uses short all-caps headings and the shared `GuideRail` /
    `GuideFactList` / `GuideTip` blocks, and reads as guide voice rather than a port of the LP's 4b
    layout.
    ⚠️ **Amended by §7.2:** `GuideFactList` **only**. `GuideTip` is capped at one render per page and
    `GuideRail` is for ordered sequences with art; using either here breaks a documented rule. The
    AC's intent — shared primitives, not a bespoke layout — is met by `GuideFactList`.
22. Given the branch is ready to ship, when the builds are run, then `npm run build` is clean in
    `monkify-lp` and `yarn lint` + `yarn build` are clean in `monkify-front`, and `monkify-backend`
    has no changes at all.
23. Given `prefers-reduced-motion: reduce`, when the section is viewed, then nothing in it animates.

---

## 13. Manual QA

Both gate states, since the gate is build-time.

**A — mint configured** (`PUBLIC_TOKEN_MINT` set to any plausible base58 in `.env`):

1. `npm run build && npm run preview` in the LP worktree. Confirm section order, and that the
   `#feedbacker` backdrop reads as its own region — scroll `#token → #feedbacker → #about` and check
   no two adjacent nebulae look like one cloud.
2. Resize to 320px and to landscape phone. Walk AC 14.
3. Click `Rewards` in the desktop nav and in the ☰ menu.
4. With GA on, click `Full rules →` and confirm in the network/dataLayer that `feedbacker_click`
   fired and `cta_play_click` did not; then click a `Play now` and confirm `cta_play_click` still
   fires with its `data-cta`.
5. Open `/terms`; confirm the clause is present and carries no figure.
6. Toggle `prefers-reduced-motion` in devtools; confirm the section is still.
7. `yarn build && yarn serve` the app; open `/how-to-play`, confirm the section's position and that
   `/how-to-play#feedbacker` from a cold load scrolls to it.

**B — mint empty:** rebuild both with the mint unset and confirm AC 2 and AC 3 — the section, the nav
entry, the Terms clause and the guide entry are all absent, and `#feedbacker` deep links land safely.

---

## 14. Trello / pipeline

- **Source card:** [#173](https://trello.com/c/lf5uWFrX) — list **Specs created**; the card's
  **Implementation** section carries branch, spec path, worktrees and Level.
- **Level:** `Feature` ⇒ a dedicated `features/feedbacker.md` at ship time; integration test N/A
  (§2.1).
- **Next:** `/monkify-implement-feature` to build it, then `/monkify-ship-feature` to sync the KB and
  merge. ⚠️ Ship must merge `monkify-lp` into **`master`**, not `main`.

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-08-17 | Initial spec from refined card #173. Adds nine findings from code exploration not present on the card: the missing `.chip--magenta`; the font subset actually covering `·` and `–` but not `▾`/`↓`; `PixelText` being wrong for chips; `GuideTip`'s one-per-page cap and `GuideRail`'s sequence+art requirement (both contradicting the card's suggested primitives); the section being static rather than an island; `terms.astro` not importing `TOKEN_LIVE`; `TREASURY_SLICES[0].note` already naming "game feedbackers"; bar heights deriving from the budget figures; and the exact `cta_play_click` opt-out shape. |

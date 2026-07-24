# SpaceStroker — "Standardize Scrollbars" Feature — Implementation Spec

> **Status: Ready for implementation** — refined via Trello card
> [standardize scroll, chat container has default browser scroll](https://trello.com/c/cGrBi584/91-standardize-scroll-chat-container-has-default-browser-scroll) (2026-07-24).
> Repos affected: `monkify-front`, `monkify-lp`, and this doc in `monkify-meta`. **Backend not affected.**
>
> **Purpose:** Replace the raw default OS/browser scrollbars that show on most scrollable
> containers (the reported case is the in-game chat panel) with **one minimal, on-brand scrollbar**
> applied app-wide as a fallback, so every scroll surface looks consistent with the retro/arcade
> aesthetic — while the game terminal keeps its distinctive green scrollbar and the side tab strip
> stays intentionally hidden.

---

## 1. Summary

This is a **frontend/CSS-only** change across both frontend repos. Today the game terminal has a
custom chunky green scrollbar (`.terminal-scroll`), the side-panel tab strip hides its scrollbar
(`.side-tabstrip`), and **everything else falls back to the native OS/browser scrollbar** — which
looks out of place on the dark arcade UI. The fix is a single **global minimal scrollbar** rule per
repo (WebKit + Firefox, thin, subtle neutral) that every scrollable element inherits by default,
with the terminal and the hidden tab strip preserved as explicit exceptions.

| Surface | Today | After |
| --- | --- | --- |
| Game terminal (`.terminal-scroll`) | Custom 10px green (WebKit only) | **Unchanged** look; Firefox parity added |
| Side tab strip (`.side-tabstrip`) | Scrollbar hidden | **Still hidden** |
| Chat / Stats / Fair / modal / admin / page scroll / LP | Native default scrollbar | **Minimal 8px neutral** scrollbar |

There is **no** JavaScript, no custom scroll component, no new dependency, no backend, and no env var.

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo:** `feature/standardize-scrollbars` in each affected repo, work done in the
  isolated worktree at `.worktrees/standardize-scrollbars/<repo>`; the shared hubs stay on their
  default branch (`main` for front/meta, **`master`** for lp — LP's default branch is `master`).
- **No backend integration test required:** this is a frontend **QOL / styling tweak**, not a new
  feature, so the "every new feature needs a happy-path integration test" rule (constraints §3) does
  **not** apply. There is also no backend change to unit-test.
- **Secrets/config:** none touched.
- **Scope discipline:** do not restyle unrelated UI; do not change the terminal's *appearance* or
  un-hide the tab strip (see §9).

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **Global fallback, not opt-in.** A single global scrollbar rule (WebKit `*::-webkit-scrollbar` + Firefox `scrollbar-*`) so every scrollable element gets the minimal scrollbar by default — no per-container utility class. |
| 2 | **Cross-browser.** Style both **WebKit** (`::-webkit-scrollbar` + `-track` + `-thumb` + `-thumb:hover`) and **Firefox** (`scrollbar-width: thin`, `scrollbar-color: <thumb> transparent`). |
| 3 | **Visual = subtle neutral**, identical in both repos: width **8px** (and `height: 8px` for horizontal bars), **transparent track**, thumb **`rgba(255,255,255,0.18)`**, hover **`rgba(255,255,255,0.30)`**, thumb `border-radius: 4px`. Distinct from the terminal's green. |
| 4 | **Scope = both frontend repos.** `monkify-front` (game SPA) via `src/main.css` **and** `monkify-lp` (landing page) via `src/styles/global.css`. Same visual spec. |
| 5 | **Terminal keeps its green scroll.** `.terminal-scroll` already overrides the global rule via higher CSS specificity; additionally add Firefox `scrollbar-width/color` to it (today it's WebKit-only) and pin its thumb `border-radius: 0` so the global rounding does not leak onto it. |
| 6 | **Hidden tab strip stays hidden.** `.side-tabstrip`'s existing `display:none` (WebKit) + `scrollbar-width:none` (Firefox) win over the global rule by specificity — verify, no change needed. |
| 7 | **Light-graphics mode** (`html.light-graphics`) needs no separate override; the white-alpha thumb stays legible on both animated and flattened backgrounds. Sanity-check only. |

---

## 4. Data model

**N/A** — frontend/CSS-only. No entity, column, EF mapping, or migration.

---

## 5. Configuration

**N/A** — no `GeneralSettings`, no `VUE_APP_*` env var, no seed data. Pure CSS.

---

## 6. Backend behaviour

**N/A** — `monkify-backend` is not touched (no handler, consumer, controller, DTO, or SignalR change).

---

## 7. Frontend behaviour

### 7.1 monkify-front — `src/main.css` (global, non-scoped)

Add the global fallback block. Recommended placement: near the existing global element rules (after
the `* { box-sizing: border-box; }` block). The neutral white-alpha values are self-contained — no
new token in `assets/styles/colors.css` is required.

```css
/* Standardized minimal scrollbar — app-wide fallback.
   The game terminal (.terminal-scroll) and the hidden side tab strip (.side-tabstrip)
   override this via higher specificity, so they are unaffected. */
* {
  scrollbar-width: thin;                                /* Firefox */
  scrollbar-color: rgba(255, 255, 255, 0.18) transparent; /* Firefox: thumb track */
}
*::-webkit-scrollbar {
  width: 8px;
  height: 8px;                                          /* horizontal scrollbars too */
}
*::-webkit-scrollbar-track {
  background: transparent;
}
*::-webkit-scrollbar-thumb {
  background: rgba(255, 255, 255, 0.18);
  border-radius: 4px;
}
*::-webkit-scrollbar-thumb:hover {
  background: rgba(255, 255, 255, 0.30);
}
```

**Why this preserves the exceptions (no code needed beyond §7.2):**
- **Terminal:** `.terminal-scroll::-webkit-scrollbar` (specificity `0,1,1`) beats `*::-webkit-scrollbar`
  (`0,0,1`), so its 10px width, dark track, and green thumb win. The **only** property that would
  leak is `border-radius` (the terminal thumb never declares one) — pinned to `0` in §7.2.
- **Tab strip:** `.side-tabstrip::-webkit-scrollbar { display: none }` and
  `.side-tabstrip { scrollbar-width: none }` both out-specify the `*` rules → scrollbar stays hidden.

### 7.2 monkify-front — `src/components/game/GameTerminal.vue` (terminal parity)

In the existing **non-scoped** `<style>` block (the one holding `.terminal-scroll::-webkit-scrollbar`
around lines 571–589), add Firefox parity on the scroll element and pin the thumb radius so the
global rounding cannot round the terminal thumb:

```css
.terminal-scroll {
  scrollbar-width: thin;                                  /* Firefox */
  scrollbar-color: rgb(19, 137, 52) rgba(0, 0, 0, 0.164); /* green thumb, dark track */
}
.terminal-scroll::-webkit-scrollbar-thumb {
  background: rgb(19, 137, 52);
  border-radius: 0;                                       /* keep it square; block global rounding */
}
```

`.terminal-scroll` is applied to the scrolling terminal element (`GameTerminal.vue` line ~371), so
Firefox `scrollbar-color` applies to the correct scroll container. WebKit appearance is otherwise
unchanged.

### 7.3 monkify-front — verify only

No code change, just confirm after the global rule lands:
- `.side-tabstrip` (`GameSidePanel.vue`) scrollbar is still fully hidden.
- Light-graphics mode (`html.light-graphics`) renders the neutral scrollbar correctly.

Surfaces fixed automatically by the global rule (no per-file edits): `GameChatPanel.vue` (the reported
bug), `GameStatsPanel.vue`, `GameFairPanel.vue`, `PlayerCustomizationModal.vue`, `AdminPage.vue`,
`components/admin/GameTypeList.vue`, and the `App.vue` root page scroll.

### 7.4 monkify-lp — `src/styles/global.css`

LP is Astro 5 + **Tailwind v4** (`@import "tailwindcss"`; theme via `@theme`, no config file). LP has
**no** custom or hidden scrollbars today, so there are **no exceptions** — the same block covers the
whole page. Append the **identical** block used in §7.1 (both `*` Firefox props and the four
`*::-webkit-scrollbar*` rules) to `global.css`, after the `@font-face` declarations / alongside the
other global rules.

> Keep the two copies byte-identical in intent (8px, neutral `rgba(255,255,255,0.18)`→`0.30`,
> transparent track) so front and LP match. LP uses `master`, not `main`.

---

## 8. Flow diagrams

**N/A** — no runtime flow; purely declarative CSS.

---

## 9. Out of scope

- No change to the terminal's green scrollbar **appearance** (only Firefox parity + explicit square
  radius are added).
- No restyling or un-hiding of `.side-tabstrip`.
- No JavaScript, no custom scroll component, no new dependency.
- No backend change; no env var; no new design token.
- No redesign of any panel's layout — scrollbar cosmetics only.

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

**None** — backend not affected.

### 10.2 Frontend (`monkify-front`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/main.css` | Add the global minimal-scrollbar block (WebKit `*::-webkit-scrollbar*` + Firefox `* { scrollbar-width/color } `). |
| 2 | `src/components/game/GameTerminal.vue` | In the non-scoped `<style>`: add `.terminal-scroll` Firefox `scrollbar-width/color`; add `border-radius: 0` to `.terminal-scroll::-webkit-scrollbar-thumb`. |
| 3 | `src/components/game/GameSidePanel.vue` | **Verify only** — `.side-tabstrip` stays hidden (no edit expected). |

### 10.3 Frontend (`monkify-lp`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/styles/global.css` | Append the identical global minimal-scrollbar block (WebKit + Firefox). |

### 10.4 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/standardize-scrollbars.md` | This spec. |
| 2 | `INDEX.md` | Add spec row (status Ready) + "Feature currently in flight". |

---

## 11. Unit tests

**N/A.** No backend change (no `SpaceStroker.Tests` to update). `monkify-front` has no test suite
(`AGENTS.md`/CLAUDE.md: "Sem testes no repo"). Verification is manual (see §13). No integration test
required — this is a QOL/styling tweak, not a new feature (§2, constraints §3).

---

## 12. Acceptance criteria

Mapped 1:1 from the Trello **Acceptance Criteria** checklist (Given / When / Then):

1. Given the game chat panel (`GameChatPanel`) has overflowing messages, when the player views it in
   a WebKit browser (Chrome/Edge/Safari), then it shows the thin 8px neutral minimal scrollbar — not
   the default OS scrollbar and not the green terminal scrollbar.
2. Given any other default-scroll surface in the front (Stats panel, Fair panel,
   `PlayerCustomizationModal`, `AdminPage`, `GameTypeList`, or the `App.vue` page scroll), when it
   overflows, then it shows the same 8px neutral minimal scrollbar.
3. Given the game terminal is scrollable, when viewed in any browser, then it keeps its distinctive
   green `.terminal-scroll` appearance — unchanged in WebKit, and shown as a consistent thin green
   scrollbar in Firefox (via added `scrollbar-width`/`scrollbar-color`).
4. Given the side-panel tab strip (`.side-tabstrip` in `GameSidePanel`) overflows horizontally, when
   viewed after the change, then its scrollbar remains fully hidden (the global rule does not
   resurrect it).
5. Given a scrollable surface is viewed in Firefox, when it overflows, then it shows the thin neutral
   scrollbar via `scrollbar-width: thin` + `scrollbar-color` (matching the WebKit look), not
   Firefox's default scrollbar.
6. Given the minimal scrollbar thumb, when the pointer hovers over it, then it brightens from
   `rgba(255,255,255,0.18)` to `rgba(255,255,255,0.30)` over a transparent track.
7. Given the landing page (`monkify-lp`) scrolls (page or an overflowing section), when viewed in
   WebKit or Firefox, then it shows the same 8px neutral minimal scrollbar as the front, added to
   `src/styles/global.css`.
8. Given the front is in light-graphics mode (`html.light-graphics`), when a surface scrolls, then
   the minimal scrollbar stays legible and correct (no broken or oversized native fallback), with no
   separate override required.

---

## 13. Manual QA

**Front** (`yarn serve`, `localhost:8080`):
1. Open `/space-stroker/game`, send enough chat messages to overflow the chat panel → thin neutral
   8px scrollbar (AC #1). Repeat for **STATS** and **FAIR** tabs (AC #2).
2. Scroll the game terminal → still chunky **green** (AC #3). Overflow the side-panel **tab strip** →
   scrollbar still hidden (AC #4).
3. Open **PLAYER SETTINGS** modal on a short viewport, the admin page, and shrink the window so the
   page/App scroll appears → all neutral 8px (AC #2).
4. Hover a thumb → brightens `0.18 → 0.30` (AC #6).
5. Toggle light-graphics mode → scrollbar still correct (AC #8).
6. Repeat #1–#4 in **Firefox** → thin neutral bar everywhere, green thin bar on the terminal (AC #5, #3).

**LP** (`npm run dev`, `localhost:4321`):
7. Scroll the page (and any overflowing section) in Chrome and Firefox → same 8px neutral scrollbar
   (AC #7).

---

## 14. Trello / pipeline

- **Source card:** [#91 standardize scroll…](https://trello.com/c/cGrBi584/91-standardize-scroll-chat-container-has-default-browser-scroll)
  — moving to **Specs created**; the **Implementation** section on the card carries branch + spec + worktree.
- **Next:** `/monkify-implement-feature standardize scroll` to build it, then
  `/monkify-ship-feature standardize scroll` to sync the KB and merge the branches to their default
  branches (`main` for front/meta, `master` for lp).

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-07-24 | Initial spec from refined card #91 (frontend-only, front + lp). |

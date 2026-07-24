# Feature — Standardized Scrollbars

> Status: **shipped.** Authoritative spec: `../specs/standardize-scrollbars.md`
> (merged via `feature/standardize-scrollbars` on 2026-07-24). Trello #91
> ([cGrBi584](https://trello.com/c/cGrBi584)). Frontend/CSS-only — **no backend change**.

## What it does

Every scrollable surface across both frontends renders **one minimal, on-brand scrollbar** instead of
the raw OS/browser default that clashed with the arcade UI (the reported case was the in-game chat
panel). It's a single global CSS fallback per repo, so no per-container class is needed. The game
terminal keeps its distinctive green scrollbar and the side-panel tab strip stays hidden — both are
preserved as explicit exceptions.

## Core rules

- **One global fallback rule** targets `*` in each repo, so any scrollable element gets the minimal
  scrollbar automatically — no opt-in utility class.
- **Visual** (identical in both repos): width **8px** (`height: 8px` for horizontal bars),
  **transparent track**, thumb **`rgba(255,255,255,0.18)`** brightening to **`rgba(255,255,255,0.30)`**
  on hover, thumb `border-radius: 4px`. Neutral and quiet — deliberately distinct from the terminal's
  green.
- **Cross-browser:** both **WebKit** (`*::-webkit-scrollbar` + `-track`/`-thumb`/`-thumb:hover`) and
  **Firefox** (`scrollbar-width: thin`, `scrollbar-color: <thumb> transparent`).
- **Exception — game terminal keeps green:** `.terminal-scroll` out-specifies the global `*` rule by
  class specificity, so its 10px green look is unchanged in WebKit. Firefox parity
  (`scrollbar-width/color`) was added so the terminal is a consistent thin green bar there too, and its
  thumb is pinned `border-radius: 0` so the global rounding can't leak onto it.
- **Exception — side tab strip stays hidden:** `.side-tabstrip` (scoped style) keeps its
  `display: none` (WebKit) + `scrollbar-width: none` (Firefox); scoped specificity beats the global
  `*` rule, so no edit was needed — verified, not changed.
- **Scope = both frontends:** the game SPA (`monkify-front`) and the landing page (`monkify-lp`) carry
  the same block. The LP has no custom/hidden scrollbars, so it has no exceptions.
- **No JavaScript, no scroll component, no new dependency, no env var, no design token.** Light-graphics
  mode (`html.light-graphics`) needs no separate override — the white-alpha thumb stays legible.

## Key code touchpoints

- `monkify-front/src/main.css` — the global minimal-scrollbar block (WebKit + Firefox), placed after
  the `* { box-sizing: border-box; }` rule.
- `monkify-front/src/components/game/GameTerminal.vue` — non-scoped `<style>`: `.terminal-scroll`
  Firefox `scrollbar-width/color` + `border-radius: 0` on `.terminal-scroll::-webkit-scrollbar-thumb`.
- `monkify-front/src/components/game/GameSidePanel.vue` — `.side-tabstrip` (scoped) hidden-scrollbar
  rule; **unchanged**, verified it still wins.
- `monkify-lp/src/styles/global.css` — the identical global minimal-scrollbar block (Tailwind v4).
- Surfaces fixed automatically by the front global rule (no per-file edits): `GameChatPanel.vue`,
  `GameStatsPanel.vue`, `GameFairPanel.vue`, `PlayerCustomizationModal.vue`, `AdminPage.vue`,
  `components/admin/GameTypeList.vue`, and the `App.vue` root page scroll.

# SpaceStroker — "Near-miss dock opaque background" Fix — Implementation Spec

> **Status: Shipped** — merged via `feature/near-miss-dock-bg` on 2026-07-24. Refined via Trello card
> [fix the "x away" thing at the bottom. the overlay has bg transparent](https://trello.com/c/h2CVRNby) (2026-07-24).
> Repos affected: `monkify-front` (the only code change) and this doc in `monkify-meta`.
> **No `monkify-backend` change.** Shipped behaviour distilled in `../features/characters-away.md`.
>
> **Purpose:** Make the bottom-docked near-miss (`> N AWAY!`) overlay in the game terminal opaque so
> the auto-scrolling character stream stops bleeding through behind it ("letters on top of letters").
> A one-rule CSS visual bug fix for every player watching a running round.

---

## 1. Summary

The near-miss "closest approach" rows (`> N AWAY!`) are part of the **characters-away** feature
(`features/characters-away.md`). They render in a **sticky dock** pinned to the bottom of the
scrolling terminal so the live stream never hides them. The dock's CSS is currently
`background: transparent`, so while the round runs and the terminal stream auto-scrolls **behind** the
dock, the scrolling characters show through the docked rows and overlap them — illegible.

The fix: give the dock a **fully-opaque background** (`#0b0d18`, the terminal / mobile-CRT screen
tone) so the stream is hidden behind it. Nothing else about the near-miss behaviour changes.

| | Before | After |
| --- | --- | --- |
| `.near-miss-dock` background | `transparent` | `#0b0d18` (opaque) |
| Stream visible behind the docked rows | Yes — bleeds through | No — fully hidden |
| Top separator line | `1px solid rgba(34,197,94,0.3)` | unchanged |
| Near-miss text / colors / animation / win type-out | — | unchanged |

This is a **visual bug fix**, not a new feature.

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo:** work lives in `.worktrees/near-miss-dock-bg/<repo>`; the shared checkout of
  each repo stays on `main` (never `git checkout -b` in the hub). Branch `feature/near-miss-dock-bg`
  is the merge/PR ref, pushed to `origin` in each affected repo.
- **No backend change** → **no unit tests** and **no integration test** required. Per
  `constraints.md` → "Integration tests are mandatory for every new feature": this is a **bug fix**,
  explicitly excluded from the new-feature integration-test mandate.
- **Secrets/config:** none touched.
- **Scope:** edit the existing file; do not create new components or restyle unrelated elements.

---

## 3. Resolved product decisions

> **Implementation note (2026-07-24):** decisions 2 & 4 were revised during the build after inspecting
> the running app (the original `#0b0d18` was the *mobile* CRT tone, not the desktop cabinet screen).
> The table below reflects **what shipped**; §7 shows the final CSS.

| # | Decision |
| --- | --- |
| 1 | The dock background becomes **fully opaque** — zero see-through is the whole point. |
| 2 | **Desktop** color = **`#050505`** (near-black), matching the actual cabinet-screen tone sampled from `bg.png` at ~`#040404`. The originally-specced `#0b0d18` was the *mobile* CRT tone and read as a mismatched blue box on desktop. |
| 3 | **Top edge:** keep the existing `border-top: 1px solid rgba(34, 197, 94, 0.3)` hard separator line. **No** gradient fade at the top. |
| 4 | The `.near-miss-dock` class is shared by desktop + mobile, but the two screens have **different tones**, so a scoped **`.mobile-terminal .near-miss-dock`** override keeps `#0b0d18` on mobile (its CRT panel is genuinely that navy). |
| 5 | The top-of-run `> POT:` line is **out of scope** (it scrolls with content, is not sticky, and does not bleed). |
| 6 | **Bottom-edge coverage:** a sticky element can't sit below the scroll container's content box, and the form has `py-6` (24px) bottom padding, so the stream's last lines peeked through in that gap. Covered with a `box-shadow` strip (offset down 24px, same tone) rather than layout changes — box-shadows don't add scrollbars. |
| 7 | **Side-edge coverage:** the dock's left/right align exactly with the stream (both at content-left), but a 1–2px sub-pixel sliver could show; two extra `box-shadow` copies (offset ±6px) over-cover the sides into the terminal's black padding. |

---

## 4. Data model

N/A — no backend, no entity, no EF mapping, no migration.

---

## 5. Configuration

N/A — no `GeneralSettings`, env var, or seed change.

---

## 6. Backend behaviour

N/A — `monkify-backend` is untouched. No API, SignalR, domain, consumer, or seed change.

---

## 7. Frontend behaviour (`monkify-front`)

### 7.1 The change

In `src/components/game/GameTerminal.vue`, the scoped style block, rule `.near-miss-dock`
(currently ~line 595):

```css
/* BEFORE */
.near-miss-dock {
  position: sticky;
  bottom: 0;
  margin-top: 8px;
  padding: 6px 4px 4px;
  max-height: 40%;
  overflow-y: auto;
  background: transparent;                       /* ← the bug: stream bleeds through */
  border-top: 1px solid rgba(34, 197, 94, 0.3);
}
```

```css
/* AFTER (as shipped) */
.near-miss-dock {
  position: sticky;
  bottom: 0;
  margin-top: 8px;
  padding: 6px 4px 4px;
  max-height: 40%;
  overflow-y: auto;
  background: #050505;              /* near-black — matches the desktop cabinet screen (~#040404) */
  box-shadow:
    0 24px 0 6px #050505,           /* covers the form's 24px bottom padding (+6px sides) */
    -6px 0 0 0 #050505,             /* over-covers the LEFT edge (sub-pixel sliver) */
    6px 0 0 0 #050505;              /* over-covers the RIGHT edge */
  border-top: 1px solid rgba(34, 197, 94, 0.3);
}
/* Mobile keeps its actual CRT-panel tone (#0b0d18) for the fill AND the shadow copies. */
.mobile-terminal .near-miss-dock {
  background: #0b0d18;
  box-shadow:
    0 24px 0 6px #0b0d18,
    -6px 0 0 0 #0b0d18,
    6px 0 0 0 #0b0d18;
}
```

Also update the explanatory comment directly above the rule (it claimed *"Transparent so it matches
the terminal…"*) so it describes the opaque near-black fill + the box-shadow edge coverage.

### 7.2 Stacking / paint order

The dock is the **last child** of `#terminal-form` and is `position: sticky`, so it already paints
above the earlier in-flow stream siblings (`<game-typer />` etc.). The opaque background is therefore
sufficient to hide the stream — **no `z-index` change is required**. Only if, during manual QA, a
positioned descendant is still observed peeking through should a defensive `z-index: 1` be added to
`.near-miss-dock`; otherwise leave stacking untouched.

### 7.3 Coverage details (as shipped)

- **Internal scroll:** the dock has `max-height: 40%; overflow-y: auto`. `background` on the dock
  element paints the whole padding box, including the internally-scrolling area.
- **Bottom padding gap:** the form is a scroll container with `py-6` (24px) bottom padding; a sticky
  element pins at the content-box bottom, leaving that 24px strip where the stream shows through. The
  `0 24px 0 6px` box-shadow paints it solid (verified live: no bleed below the last row).
- **Left/right edges:** the dock spans the form's content width (its left/right align with the
  stream), but the `-6px`/`6px` shadow copies over-cover the sides by 6px into the terminal's black
  padding to eliminate a sub-pixel sliver seen at the left edge during QA.
- **Box-shadows don't affect layout**, so none of this adds a horizontal/vertical scrollbar (an
  earlier negative-margin attempt did, and was rejected).

### 7.4 Untouched

Near-miss row markup, the `.near-miss-matched` / `.near-miss-rest` color ramp, the escalating
`data-away` glow, the 2s stall-reset loop, the win type-out, and the `.pot-value` styling all stay
exactly as-is. No change to `useNearMiss.ts`, `nearMiss.ts`, `useGame.ts`, or any type.

---

## 8. Flow diagrams

N/A — single CSS rule change; no flow.

---

## 9. Out of scope

- Restyling near-miss row colors, the matched-prefix glow, animation, or the win type-out.
- The top-of-run `> POT:` line (scrolls with content; not sticky; no bleed).
- Scroll standardization / custom scrollbar work — that is the separate card **#91**
  ("standardize scroll, chat container has default browser scroll").
- Any backend, SignalR, or data change.

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

None.

### 10.2 Frontend (`monkify-front`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/components/game/GameTerminal.vue` | In the scoped `<style scoped>` block: `.near-miss-dock { background: transparent }` → `background: #050505` + the 3-part `box-shadow` (bottom strip + side over-cover); add a scoped `.mobile-terminal .near-miss-dock` override (`#0b0d18` bg + shadows); update the preceding comment. No other change. |

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/near-miss-dock-bg.md` | This spec. |
| 2 | `INDEX.md` | Add the spec row (status Ready) + set "Feature currently in flight". |
| 3 | `features/characters-away.md` | *(At ship time, via `/monkify-ship-feature`.)* Update the "Always visible, bottom-docked" bullet: the dock is now **opaque near-black** (`#050505`; `#0b0d18` on mobile) with a `box-shadow` covering the form's bottom padding + side edges, not transparent. |

---

## 11. Unit tests

N/A — frontend-only CSS change, no backend behaviour. `monkify-front` has no test suite
(per project docs). Verification is manual (§13).

---

## 12. Acceptance criteria

Copied 1:1 from the Trello **Acceptance Criteria** checklist:

1. Given a running round with the player's own bet(s), When the terminal stream auto-scrolls behind
   the docked `> N AWAY!` rows, Then no stream characters are visible through/behind the dock (fully
   opaque near-black `#050505` on desktop).
2. Given the dock is rendered, When compared to the terminal screen, Then its background matches the
   dark cabinet-screen tone (`#050505` ≈ the sampled `#040404`, not a mismatched navy/blue strip) and
   the 1px green top separator line remains.
3. Given many near-miss rows causing the dock to scroll internally (max-height 40%, overflow-y auto),
   When it overflows, Then the opaque background still fully covers the docked area with no
   bleed-through.
4. Given both the desktop (cabinet) and mobile (CRT) layouts, When a round runs, Then the fix applies
   in both (desktop `#050505`, mobile `#0b0d18`) with no regression to near-miss text, colors, or the
   win type-out.
5. Given a running round, When looking at the **bottom and left/right edges** of the dock, Then no
   stream characters peek below the last row (bottom padding covered) or at the side edges (verified
   live).

---

## 13. Manual QA

1. `cd .worktrees/near-miss-dock-bg/monkify-front && yarn install && yarn serve` (dev on `:8080`).
2. With the local stack up, open `/space-stroker/game`, pick a game type, place a bet on a word so a
   near-miss row appears, and start a round.
3. **Desktop:** while the stream auto-scrolls, confirm the `> N AWAY!` dock at the bottom shows a
   solid dark background with the green top line, and **no** scrolling characters are visible behind
   the rows.
4. **Mobile:** resize to `< lg` (or use device emulation), open the GAME segment, and repeat — same
   opaque result, no regression to the CRT panel.
5. Place several own bets (many rows) to make the dock overflow internally; confirm the opaque
   background still covers the scrolled area.
6. Let a round end; confirm the win type-out and near-miss colors are unchanged.
7. `yarn lint` clean (mind the CRLF/prettier trap noted in project memory).

---

## 14. Trello / pipeline

- **Source card:** [#90 — fix the "x away" thing at the bottom](https://trello.com/c/h2CVRNby) —
  moved to **Specs created**; **Implementation** section on the card lists branch + spec + worktree.
- **Next:** `/monkify-implement-feature 90` to build it, then `/monkify-ship-feature 90` to sync the
  KB (update `features/characters-away.md`) and merge to `main`.

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-07-24 | Initial spec from refined card #90 (frontend-only near-miss dock opaque-background bug fix). |
| 2026-07-24 | Implementation reconciliation: color `#0b0d18` → **`#050505`** (matches sampled cabinet screen `#040404`; `#0b0d18` kept for mobile via a scoped override); added a 3-part **`box-shadow`** to cover the form's 24px bottom padding gap + left/right sub-pixel slivers (negative-margin approach rejected — caused a scrollbar). Verified live in the local stack. |

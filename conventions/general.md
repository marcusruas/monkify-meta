# General conventions (every repo)

Rules that bind a change across `monkify-backend`, `monkify-front`, `monkify-lp` and this knowledge
base, rather than one codebase. Code-level conventions stay in [`backend.md`](backend.md) and
[`frontend.md`](frontend.md).

## Every new game mode ships with its how-to-play section and its landing-page card

**Rule (owner, 2026-09-25):** a new game mode is not done until **both** of these ship with it, on the
same feature branch as the mode itself:

1. **A "HOW <MODE> WORKS" section in the player guide** (`monkify-front`, `/how-to-play`).
2. **A landing-page update for the mode** (`monkify-lp`).

"New game mode" means a new kind of table a player can pick in the lobby (a new archetype such as a
race, Hyper Roll or Monkey Duel), not a new board mode, a reprice or a rename of an existing one.

### 1. The how-to-play section (`monkify-front`)

Same format as the existing mode sections: `GuideHowTheGameWorks.vue` (race), `GuideHyperRoll.vue` and
`GuideCoinflip.vue` (the reference for the most recent one).

- A new component in `src/components/guide/sections/`, registered in `GUIDE_SECTIONS`
  (`src/constants/guideSections.ts`) with its own `id` (added to `GuideSectionId`), `navLabel` and a
  `HOW <MODE> WORKS` title, placed with the other mode sections.
- The layout: a small tag chip, one intro paragraph, a **five-step `GuideRail`**, one closing line and
  a `GuideTip` labelled **THE WHOLE GAME**.
- **The step art is the game itself:** crops of the running cabinet at 2x nearest-neighbour, cut from a
  live local round with dev-only controls hidden (for example the free-bet toggle and the HOW TO PLAY
  key), stored in `src/assets/guide/<mode>-*.png`. No drawn stand-ins.
- **Copy rules:** no em dashes, no litotes, and **no figures that go stale** (cuts, ceilings, RTP,
  payouts). Name the mechanic and send the reader to the screen or STATS panel that reads it live.
- The shape-driven `THE MODES` cards (`GuideModeCards`) pick the mode up on their own, **but only if the
  new archetype has an entry in its `BLURB`, `ARCHETYPE_LABEL` and `ARCHETYPE_ACCENT` maps**: a missing
  one crashes the whole block (it happened with the duel). Update the section intro that counts the kinds
  of table too.

### One row of modes on desktop

Every surface that lists the modes shows **all of them on one row on desktop (1024px and up)**, 2x2 on
tablets and one column on phones: the app lobby (`GameTypes.vue`, `lg:grid-cols-4`), the guide's THE
MODES cards (`GuideModeCards.vue`) and the landing page (`GameModes.vue`). Adding a mode means
re-checking that row at 1024, 1280 and 1440px; if it no longer fits, redesign the cards rather than
wrapping to a second row.

### 2. The landing-page update (`monkify-lp`)

- A card for the mode in `src/components/GameModes.vue`, in the **same order as the app lobby**
  (`SHOWN_ORDER` in `monkify-front/src/utils/gameCatalogue.ts`), with its own poster in
  `src/assets/cards/` and an accent.
- Every place that counts or lists the modes is updated: the `#modes` intro copy ("N ways..."), the
  how-it-works steps where they name the modes, and the roadmap's live-modes line.
- **Terms** (`src/pages/terms.astro`): if the mode pays out differently from the modes already
  described (counterparty, cut, multiplier), add a clause and bump `LAST_UPDATED`. The clause is legal
  copy, so the owner reviews it before it ships.
- Verify with `astro build` and a browser check of `#modes` on desktop and phone.

### How this is enforced

- **Refinement / spec:** a spec for a new mode lists both items as acceptance criteria.
- **Implementation:** the branch touches `monkify-front` (guide) and `monkify-lp` as well as the
  mode's own repos. The worktree and branch rules apply to `monkify-lp` too (its base branch is
  `master`, not `main`).
- **Ship:** a new mode is not ready to merge while either item is missing.

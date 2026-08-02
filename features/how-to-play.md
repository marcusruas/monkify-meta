# Feature — How to play (the guide page)

> Status: **shipped.** Authoritative spec: `../specs/how-to-play.md`
> (merged via `feature/how-to-play` on 2026-08-02).
>
> Frontend-only. No backend entity, endpoint, migration, consumer or SignalR change; `SpaceStroker.Tests`
> and `SpaceStroker.Tests.Integration` were not run for this card and are unaffected.

## What it does

`/how-to-play` is one readable, linkable page that explains the game — replacing the **Quick Tour**,
which auto-opened once behind seven guards and was then gone forever. Eight deep-linkable sections
cover the premise, getting in, the round, the payout, the live modes, provable fairness, refunds and
the optional settings. **Nothing auto-opens any more, on any route, for any player.** The same card
converted **Responsible Gaming** from a ☰ modal into its own page at `/responsible-gaming`.

## Core rules

### The page

- **Eight sections, in this order**, ids fixed as a deep-link contract:
  `what-is-this` · `where-to-start` · `how-a-round-goes` · `what-you-win` · `the-modes` ·
  `provably-fair` · `refunds` · `your-setup`.
- **The order is an argument**, not a list: the idea → getting in → the round → the money → which
  table → why believe it → what happens when it goes wrong → the optional knobs. ⚠️ An earlier
  five-section version opened on *"connect a wallet"* and never said what the game **was**; it named
  three of its five sections after **modals** (`Provably Fair`, `Player Settings`), billing a
  display-name field level with the entire game. Do not reintroduce a section named after an app
  surface.
- **Registry-driven** (`constants/guideSections.ts` → `GUIDE_SECTIONS`). The nav **and** the body both
  derive from that one array; nothing downstream reads a hard-coded count or a literal id list, so the
  deferred **Orbit** section is one appended entry plus one component.
- **Sticky section nav** on `lg+`, a horizontally scrolling **chip row** below it. ⚠️ `min-w-0` on both
  grid boxes is load-bearing — without it the chip row's intrinsic width sized the page's grid track
  and cut the right-hand side off every section on a phone.
- **Deep links work on a cold load.** The router is created with **no `scrollBehavior`**, so the page
  reads `route.hash` on mount and scrolls itself; `scroll-mt-[88px]` supplies the sticky header's
  offset. An unknown anchor (`#orbit`) matches nothing in the registry, scrolls nowhere and lands the
  player at the top — never a blank screen, never `/error`. **The raw hash is never interpolated into
  a selector.**

### Which nav entry is highlighted

- **The active section is the one filling most of the screen**, measured inside the reading area
  (below the sticky 64px header, above the viewport bottom).
- **One exception:** a section whose top has just arrived in the reading area (`[64, 184]`) wins
  regardless of area — otherwise clicking a **short** nav entry immediately hands the highlight to its
  taller neighbour, and the control visibly disobeys.
- ⚠️ **Two rules are dead and must not come back**, both of which measured distance to a line instead
  of occupancy. (a) *"the last section whose top passed a 100px band"* — the final section can **never**
  satisfy it, because at maximum scroll its top sits at `viewportHeight − (its height + the page's
  bottom padding)`; the rail stuck on the second-to-last entry forever. (b) the same rule plus an
  at-the-very-bottom clamp — which fixed only the exact end of the document, so a section filling 70%
  of the screen still lost to a two-line sliver above it.
- ⚠️ **This is a rAF-coalesced `scroll` listener, not an `IntersectionObserver`, and that is
  deliberate.** IO reports threshold crossings of each element's *own* ratio; the moment one section
  overtakes another in visible area is not a threshold on either of them, so the observer goes silent
  exactly where the answer is still changing. Denser thresholds shrink the error without removing it.

### Content

- **⚠️ The word is "word".** *You pick a word and you bet on it* — the sentence the whole page is built
  on, and the deleted tours' own wording. **"A run of characters", "make your call" and "back a run"
  were tried and rejected by the owner.**
- **No character count in the prose.** The length is each game's own `choiceRequiredLength`, and a
  player reads the page before choosing a mode, so any number in the prose is wrong for some game. The
  count appears **only** on the mode cards, read live off the catalogue.
- **Preset-agnostic** (Trello #115, unchanged): never "animal", never a numeric cap like "1-8".
- **Voice is the tours': playful, short, arcade.** Two deliberate exceptions run drier — `refunds` and
  `what-you-win` — because a player reading those is asking where their money went.
- **`refunds` is honest and must stay that way:** a **lottery** that is under-subscribed at draw time
  refunds every paid bet; a **race** below its minimum is **not** refunded, it waits. Do not soften
  this into "you always get your money back if a round does not fill" — it is false for the most
  common mode. It also never enumerates *why* a bet was rejected (`constraints.md` §6) and never quotes
  the 12-hour pending-refund sweep interval as a promise.
- **`what-you-win` states no house-cut percentage.** The rate is per-deployment
  (`GeneralSettings:Operator:Token:CommissionPercentage`) and published live on
  `ActiveGame.commissionPercentage`, so the page points at the game screen's STATS panel instead. It
  also does **not** promise "you can never win less than you staked" — `BetRewardBiggerThanThePot` is
  an error branch, not a payout guarantee.

### The modes block

- **One card per live game type, matched by SHAPE** via `utils/gameArchetype.resolveGameArchetype` —
  never by `gameTypeId` (the GUIDs change on every teardown + reseed) and never by name (operator-chosen).
- **A preset lottery matches no archetype and renders no card at all** — not an empty one, not a
  fallback one.
- **Ticket price** comes from `usePlayerPreferences().formatGameAmount`, the same formatter the
  terminal uses. **Draw frequency** uses `formatLotteryFrequency`'s **long** form (`"24 HOURS"`) — the
  compact form is the lobby badge's, and "every DAILY" is broken copy.
- **⚠️ The card art is `utils/gameArt.artFor`, the lobby picker's own illustration** — not a guide art
  slot. The three live terminal recreations (`pick-terminal` / `bet-terminal` / `win-reveal`) all draw
  the same green console, so the one block whose job is showing how the modes **differ** showed three
  near-identical pictures of none of them. Poster only, no hover-GIF swap.
- **This is the only section that depends on a network call.** `getGamesTypes` is `singleFlight`-ed and
  wrapped in `runWithRecovery`, so a transient failure raises the shared blocking `> RECONNECTING…`
  overlay and **rethrows** once the budget is spent; the rethrow is caught inside the component. **The
  other seven sections render fully in every state.** The page never blanks and never routes to `/error`.

### Materials

- `/how-to-play` uses the near-black **`.arcade-frame`** (joining `/`, `/error`, `/soon`);
  `/responsible-gaming` uses **`.arcade-frame .arcade-frame--grey`** (joining `/game/history`,
  `/game/stats`). ⚠️ Those two lists disagree **on purpose** — do not "unify" them.
- **`.arcade-frame--black` is new**, and the guide's `GuideTip` is its only consumer. It inherited the
  black material when the quick tours — the previous only consumer — were deleted, which would
  otherwise have left a documented-but-unused rule. **At most one tip per section**, and the rule is
  unchanged: black needs **both** axes, brief-and-informative **and** light in tone. The page ships
  **two**, bookending it (`what-is-this`, `your-setup`); `refunds` and `what-you-win` have none because
  money is serious in tone.

### Routing, embedding and analytics

- Both routes are registered in production and **redirect to `GameLobby` when embedded**, through the
  single `EMBED_REDIRECT_ROUTES` set in `main.ts`'s existing `beforeEach` — registered **before** the
  analytics `afterEach`, so a framed load never counts as a pageview and the component never mounts.
- **Neither is an `isPlayerRoute()`** — neither renders a display name, so `App.vue` boots nothing for
  them. The guide fetches the catalogue itself; the RG page loads preferences itself.
- **`guide_section_click`** (consent-gated, `{ section }` and no identifiers) fires from the **nav**,
  never from a scroll observer — scroll-triggered would count passive scrolling as interest.
  `quick_tour_open` / `quick_tour_dismiss` are deleted.

### Responsible Gaming as a page

- A **container swap, not a rewrite**: the modal's body on the grey page fill, so the conversion is
  material-preserving. Both states are kept — already-excluded (**permanent**, which cannot be lifted,
  and **dated**, which lifts on its own) and not-excluded (duration `select`, the signing note naming
  the short wallet, and the **two-step Permanent confirm**). Both external links and the honest note
  that **connecting a different wallet bypasses the block** are kept verbatim.
- **No wallet connected** → the reading material still renders and the action explains it needs a
  wallet, rather than failing.
- The footer action **pushes** to `Home` rather than `back()` — a player who pasted the URL has no
  history.
- **The mobile deeplink return is re-pointed at the route.** `rememberReturnTo()` stores
  `pathname + search` before the hop, so a hop from `/responsible-gaming` returns there. ⚠️ A bet
  signature and a self-exclusion signature are **indistinguishable** in the return params — what was
  stashed before leaving is the only discriminator. **Never infer the hop from the return params.**
- ⚠️ **Untested on a physical phone.** `features/legal-docs-self-exclusion.md` records that the
  `signMessage` hop was never exercised on real hardware at ship time; this card re-points it and did
  not change that. Treat a failure there as a blocker.

### Self-exclusion actually loading (a bug fixed in passing)

- `applySelfExclusionPreferences` was a **dead export** — defined but called from nowhere, almost
  certainly lost when `remove-operators` rewrote `usePlayerPreferences`. **Consequence:** after any page
  load `selfExcluded` was `false`, so the RG surface always rendered the not-excluded branch and the
  client pre-bet guards were dead code. The backend still enforced exclusion in `RegisterBetHandler`, so
  it was a UX/trust regression, not a money hole.
- It is now called from `usePlayerPreferences.loadPreferences`. ⚠️ **Side effect on the money screen:**
  the client pre-bet guards (`useGame.ts`, `GameConsole.vue`'s `allowBets`) are **live again** for
  genuinely-excluded wallets. Correct behaviour, but a behaviour change this card would not otherwise
  have made.

## Known gaps / follow-ups

- **The first-visit nudge is gone and nothing replaces it** (knowingly accepted). The app lost its only
  **unprompted** teaching surface; `HomeHowItWorks` on `/` is the onboarding now.
- **The Orbit section is deferred** (#121–#123) — its own Trello card.
- **The copy has not been reviewed by the team** — Trello **"Validate the /how-to-play texts with the
  dev team"**. The UI, section shell and cards are settled; the wording is what is open.
- **`/responsible-gaming`'s mobile deeplink return has never run on a physical phone.**

## Key code touchpoints

**`monkify-front`**

- `src/pages/HowToPlayPage.vue` — the shell, hash handling, and the active-section resolver.
- `src/pages/ResponsibleGamingPage.vue` — the ex-modal on the grey page fill; loads preferences itself.
- `src/constants/guideSections.ts` — `GuideSectionId`, `GuideSection`, `GuideArt`, `GUIDE_SECTIONS`, and
  the ⚠️ VOICE note.
- `src/components/guide/sections/*.vue` — one component per section (8).
- `src/components/guide/` — `GuideSectionNav.vue`, `GuideSectionCard.vue`, `GuideRail.vue`,
  `GuideFactList.vue` (the shared multi-column row list), `GuideTip.vue`, `GuideModeCards.vue`,
  `GuideArt.vue` + the nine adapted art components.
- `src/utils/gameArchetype.ts` — `resolveGameArchetype`, extracted verbatim from the deleted
  `useQuickTour.resolveTourKey`. ⚠️ Distinct from `utils/gameArt.archetypeOf`, which answers a
  different question and never returns `null`.
- `src/constants/guideArtTiming.ts`, `src/utils/guideStream.ts`, `src/assets/guide/game-modes.gif` —
  renamed from their `tour*` originals.
- `src/routes.ts`, `src/main.ts` (`EMBED_REDIRECT_ROUTES`), `src/components/GameHeader.vue`,
  `src/components/home/HomeFooter.vue`, `src/components/home/HomeHowItWorks.vue`,
  `src/pages/HomePage.vue`, `src/pages/ComingSoonPage.vue`, `src/hooks/useHeaderModals.ts`,
  `src/hooks/useAnalytics.ts`, `src/hooks/usePlayerPreferences.ts`,
  `src/assets/styles/arcade.css` (`.arcade-frame--black`).

**Deleted:** `components/game/QuickTourModal.vue`, `hooks/useQuickTour.ts`, `constants/quickTours.ts`,
`components/header/ResponsibleGamingModal.vue`, and the `space-stroker.tour.seen` localStorage key.

## Verification

`monkify-front` has **no test runner**, so this card's gates were `yarn lint` (⚠️ needs uniform **CRLF**;
`sed -i` mangles endings and produces `Insert ␍` failures), `yarn build`, a **grep gate** proving every
deleted symbol returns zero live hits, and browser QA at 1440/1600px and 390px with 0 console errors.
The active-section resolver was verified by script: 61 scroll positions against an independently
computed expectation (0 mismatches), all eight entries activating in document order, and all eight nav
clicks landing on what was clicked.

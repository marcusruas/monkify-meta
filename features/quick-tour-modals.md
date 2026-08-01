# Feature — Quick Tour Modals

> Status: **shipped.** Authoritative spec: `../specs/quick-tour-modals.md`
> (merged via `feature/quick-tour-modals` on 2026-07-30). **Frontend-only** — see
> "Why there is no server half" below; the spec's backend chapters describe a design that was
> dropped before merge.

## What it does

A first-time player used to land in Space Stroker with nothing explaining it: the lobby showed three
arcade cards and the game screen a typing terminal, a lever and a keypad, none of which said "back a
beast and wait for Edson to spell it". A pixel-art **Quick Tour** modal now auto-opens once on the
lobby and once per game-mode archetype, and stays reachable from a **`How to play`** row in the ☰
header menu.

## Core rules

**Which tour a game gets — by SHAPE, never by id or name.** `gameTypeId` GUIDs change on every
teardown + reseed and game types are created in the admin portal, so neither can key content:

| Key | Match against `ActiveGame` | Today's type |
| --- | --- | --- |
| `overview@1` | — (the basics tour) | — |
| `preset@1` | `presetChoices.length > 0` **and** not a lottery | Beast Mode |
| `race@1` | `presetChoices` empty **and** not a lottery | Cosmic Sprint |
| `lottery@1` | `presetChoices` empty **and** `lotteryParameters.lotteryFrequency` set | Space Jackpot |

- A **preset lottery matches no archetype and gets no auto-tour.** Deliberate no-op, not an error: no
  such type is seeded, one can be created, and `How to play` still opens the overview.
- Mode name, ticket price and draw frequency are read **live off the `ActiveGame` DTO**, so the copy
  stays true for any admin-created type. The lottery tour renders its real frequency through the
  shared `formatLotteryFrequency` (`"1.00:00:00"` ⇒ **24 HOURS**, not "1 HOUR").
- Copy is **preset-agnostic**: it never says "animal" and never states a numeric cap like "1-8" —
  preset choices are configured free text and the mobile keypad is name-based and uncapped.

**Auto-open is guarded, and every guard is load-bearing.** `maybeAutoOpen` refuses silently when:
the game matches no archetype; the app is **embedded** (same gate as Stats and Fair); a tour is
already open; the key is already seen; the **boot-recovery**, **idle-disconnect** or embedded
**relaunching** overlay is up; the **18+/Terms gate** has not been accepted; or the **display-name
prompt** is open. A modal teleported to `#app` is a *sibling* of those overlays, not a descendant, so
nothing about the DOM prevents it painting over them — only these checks do.

- `overview` opens on the **first visit to the starting page** (`/`), gated on the game catalogue
  having loaded so it opens over a populated page. ⚠️ It moved off the lobby with `home-dashboard`:
  `/` is where almost everyone now enters, so leaving the watcher on `/game` would have silently
  stopped it firing. **Accepted gap:** a player deep-linking straight to `/game` gets no overview.
- A mode tour opens **after the game type resolves** on `/game/play`, driven by a
  `watch` on `gameTypeId` — not by either `selectedGame` assignment site — so a `?type=` deep link is
  covered. It does **not** re-fire on round rollover (the instance changes, the type does not).
- **Never two at once.** A first-timer who dismisses the overview and walks straight into a game gets
  no mode tour on that entry; it opens on the next one.
- The terms gate is the one overlay that **outranks** the tour (`z-[70]` vs `z-[60]`), so a tour
  opened under it would be invisible yet still own the Esc handler. Both auto-open watchers therefore
  take `termsAccepted` as a **dependency** and fire the moment ACCEPT is clicked — otherwise the one
  player the overview exists for is the only one who never sees it.

**Dismissal and memory.**
- **X, GOT IT!, Esc and a backdrop click all close the modal and all mark it seen** — nobody is
  nagged for closing it "the wrong way".
- Seen keys live in **`localStorage` only** (`space-stroker.tour.seen`, same convention as
  `space-stroker.audio`). Every touch is wrapped in try/catch — private mode degrades to "shows every
  visit", never throws.
- Keys are **versioned**; bumping `TOUR_VERSIONS` is how a future copy or art rewrite deliberately
  re-shows a tour. Everything shipped at `@1`, so nobody saw a tour twice.

### Why there is no server half

The refined design persisted dismissals on `OperatorPlayer.SeenTours` via a silent fire-and-forget
`PATCH /players/preferences`, unioned server-side so two devices could not clobber each other. That
was built and tested — and then **`remove-operators`** landed: `OperatorPlayer` became `Player`, and
the prefs write became **signature-proved and display-name-specific** (ed25519 over a single-use
nonce, because with launch tokens gone a wallet address is public and an unproven write would let
anyone rename anyone).

Under that contract the tour could only persist a dismissal by **prompting the player to sign a
Solana message to close an onboarding modal** — and the overview fires on the lobby, where usually no
wallet is connected at all. The alternative, a new *unsigned* endpoint keyed on a public wallet, buys
cross-device memory at the price of letting anyone suppress a stranger's tutorial. Both were rejected;
the server half was dropped entirely. **The accepted cost:** a dismissed tour can reappear on a
different browser or device. For a once-ever onboarding modal that is cheaper than either alternative.

Consequences worth knowing: there is **no entity, migration, handler change or backend test** for this
feature, and `useQuickTour` makes **no network call at all**.

**Presentation.** The modal is `<Teleport to="#app">` (the header's `backdrop-blur-md` makes it the
containing block for `position: fixed` descendants — see `conventions/frontend.md` → Overlays). It is
the **tutorial/help tier** (black + cyan frame), not the system tier — see the two-tier rule in
`conventions/frontend.md`. Below `lg` the steps stack vertically in a `max-h-[90dvh] overflow-y-auto`
panel, the overview folds PULL THE LEVER into "tap PLACE BET" to match the mobile bet panel (a
**4-step** mobile tour), and every animated art slot freezes under `html.light-graphics` — which every
phone runs. The connect-wallet step is conditional on the header actually showing a wallet; since
operators were removed the cluster is unconditional, so it is always shown today, but the condition
stays so the tour can never promise a control that is not on screen.

**Analytics** (GA4, consent-gated, no identifiers): `quick_tour_open { tour, trigger: auto | menu }`
and `quick_tour_dismiss { tour, how: got_it | x | esc | backdrop }`. `tour` carries the **versioned**
key so a later rewrite reads as a different value.

**Shipped alongside** (found while building the tour): the header is now **sticky**, with the
horizontal-overflow guard moved to `<html>` — `overflow-x` on the App wrapper made that
`min-h-screen` box a dead scrollport and the sticky header anchored to it instead of the viewport.

## Key code touchpoints

**`monkify-front`** (no backend files — see above)
- `src/hooks/useQuickTour.ts` — module singleton: `resolveTourKey`, open state, the localStorage
  seen-set, the auto-open guards, `markSeen`.
- `src/constants/quickTours.ts` — `TOUR_VERSIONS`, `versioned()`, `TOURS`; the one place copy and art
  are edited. `src/constants/tourArtTiming.ts` — shared art timings.
- `src/components/game/QuickTourModal.vue` + `TourStepArt.vue`, `TourModeReel.vue`, `TourCrtScreen.vue`,
  `TourWalletArt.vue`, `TourLeverArt.vue`, `TourTerminalArt.vue`, `TourBetPanelArt.vue`,
  `TourBetTerminalArt.vue`, `TourEdsonSpeedArt.vue`, `TourWinRevealArt.vue`.
- `src/components/GameHeader.vue` — the `How to play` row (hidden when embedded), both auto-open
  watchers, and the tour context (price via `usePlayerPreferences.formatGameAmount`).
- `src/utils/lotteryFrequency.ts` — extracted from `GameTerminal.vue`, now shared with `GameTypeCard`.
  `src/utils/tourStream.ts` — the fake character stream the art uses.
- `src/hooks/useAnalytics.ts` — the two events. `src/main.css` — `html { overflow-x: hidden }`.
- Art: `src/assets/tours/game-modes.gif` plus existing assets (type icons, keypad buttons, lever and
  torpedo gifs, Edson). **Deviation from the spec:** the mode header art is this one sprite reel, not
  three separate gifs copied from `monkify-lp`.

## Known gaps

- `QuickTourModal` sets no `role="dialog"` / `aria-modal`, unlike `LegalGate` which sets both.
- A dismissal does not follow the player to another browser or device (see "Why there is no server
  half"). If that ever matters, the open question is what authorises the write.

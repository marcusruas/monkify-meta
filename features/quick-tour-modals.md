# Feature — Quick Tour Modals

> Status: **shipped.** Authoritative spec: `../specs/quick-tour-modals.md`
> (merged via `feature/quick-tour-modals` on 2026-07-30).

## What it does

A first-time player used to land in Space Stroker with nothing explaining it: the lobby showed three
arcade cards and the game screen a typing terminal, a lever and a keypad, none of which said "back a
beast and wait for Edson to spell it". A pixel-art **Quick Tour** modal now auto-opens once on the
lobby and once per game-mode archetype, and stays reachable from a **`How to play`** row in the ☰
header menu. Dismissals are remembered per player, across devices, and never replayed.

## Core rules

**Which tour a game gets — by SHAPE, never by id or name.** `gameTypeId` GUIDs change on every
teardown + reseed and operators create their own types through the admin portal, so neither can key
content:

| Key | Match against `ActiveGame` | Today's type |
| --- | --- | --- |
| `overview@1` | — (the basics tour) | — |
| `preset@1` | `presetChoices.length > 0` **and** not a lottery | Beast Mode |
| `race@1` | `presetChoices` empty **and** not a lottery | Cosmic Sprint |
| `lottery@1` | `presetChoices` empty **and** `lotteryParameters.lotteryFrequency` set | Space Jackpot |

- A **preset lottery matches no archetype and gets no auto-tour.** That is a deliberate no-op, not an
  error: no such type is seeded, an operator can create one, and `How to play` still opens the
  overview.
- Mode name, ticket price and draw frequency are read **live off the `ActiveGame` DTO**, so the copy
  stays true for any operator-created type. The lottery tour renders its real frequency through the
  shared `formatLotteryFrequency` (`"1.00:00:00"` ⇒ **24 HOURS**, not "1 HOUR").
- Copy is **preset-agnostic**: it never says "animal" and never states a numeric cap like "1-8" —
  preset choices are operator-configured free text and the mobile keypad is name-based and uncapped.

**Auto-open is guarded, and every guard is load-bearing.** `maybeAutoOpen` refuses silently when:
the game matches no archetype; the app is **embedded** (same gate as Stats and Fair); a tour is
already open; the key is already seen; the **boot-recovery**, **idle-disconnect** or embedded
**relaunching** overlay is up; the **18+/Terms gate** has not been accepted; or the **display-name
prompt** is open. A teleported modal is a *sibling* of those overlays, not a descendant, so nothing
about the DOM prevents it painting over them — only these checks do.

- `overview` opens on the **first lobby visit** (`/space-stroker/game`), gated on the game catalogue
  having loaded so the conditional wallet step is decided against a real list.
- A mode tour opens **after the game type resolves** on `/space-stroker/game/play`, driven by a
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
- Seen = **`localStorage` ∪ server**. `localStorage` (`space-stroker.tour.seen`, same convention as
  `space-stroker.audio`) is the only store that works for the common case: production Main mints an
  **anonymous launch token with an empty `sub`**, so a lobby visitor has no server identity at all.
  Every touch is wrapped in try/catch — private mode degrades to "shows every visit", never throws.
- The server half is **`OperatorPlayer.SeenTours`**, a nullable `varchar(200)` CSV of versioned keys
  riding the **existing** `GET`/`PATCH /players/preferences` pair. No new endpoint, and the client
  makes **no extra boot request**: `seenTours` is published out of
  `useDisplayCurrency.loadPreferences`, the single prefs read on the boot path.
- **The PATCH unions, it never replaces.** A null list means "this PATCH did not touch the tours", so
  a display-name-only save keeps them, and a stale device can never drop another device's dismissals.
  Malformed keys (not `^[a-z]+@\d+$`) are **dropped rather than failing the whole PATCH**, and the set
  is capped at **20 keys / 200 characters**.
- An anonymous player never PATCHes (the handler answers an empty `externalPlayerId` with a vague
  validation failure). When a wallet connects, local-only keys are pushed up **once per identity**, so
  a tour dismissed anonymously does not reappear a minute later.
- A null column reads as "has seen nothing" — additive and backward compatible, no backfill.
- Keys are **versioned**; bumping `TOUR_VERSIONS` is how a future copy or art rewrite deliberately
  re-shows a tour. Everything shipped at `@1`, so nobody saw a tour twice.

**Identity** is unchanged from `wallet-display-name`: the wallet in the body wins, else the token
`sub`. The rule lives in one place, `usePrefsWallet` — including the `!getPlayerIdFromToken()` arm for
the anonymous Main token — so the tour's dismissal writes cannot drift from the preferences load.

**Presentation.** The modal is `<Teleport to="#app">` (the header's `backdrop-blur-md` makes it the
containing block for `position: fixed` descendants — see `conventions/frontend.md` → Overlays). It is
the **tutorial/help tier** (black + cyan frame), not the system tier — see the two-tier rule in
`conventions/frontend.md`. Below `lg` the steps stack vertically in a `max-h-[90dvh] overflow-y-auto`
panel, the overview folds PULL THE LEVER into "tap PLACE BET" to match the mobile bet panel (a
**4-step** mobile tour), and every animated art slot freezes under `html.light-graphics` — which every
phone runs. The wallet step appears only where a wallet does, so a Standard/regulated player gets a
4-step overview and a SolanaToken player 5.

**Analytics** (GA4, consent-gated, no identifiers): `quick_tour_open { tour, trigger: auto | menu }`
and `quick_tour_dismiss { tour, how: got_it | x | esc | backdrop }`. `tour` carries the **versioned**
key so a later rewrite reads as a different value.

**Shipped alongside** (found while building the tour, same branch): the header is now **sticky**, with
the horizontal-overflow guard moved to `<html>` — `overflow-x` on the App wrapper made that
`min-h-screen` box a dead scrollport and the sticky header anchored to it instead of the viewport. And
the **wallet cluster now shows on the lobby, history and stats**, not only inside a round, because the
overview's connect step otherwise pointed at a control that was not on screen; the catalogue is
fetched once for History/Stats reached by a hard load, and the token label always comes from the same
game as the balance.

## Key code touchpoints

**`monkify-backend`**
- `SpaceStroker.Domain/Games/Entities/OperatorPlayer.cs` — `SeenTours` (`string?`, CSV).
- `SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` — inline `HasMaxLength(200)`.
- `SpaceStroker.Infrastructure/Migrations/20260729012038_AddOperatorPlayerSeenTours.cs` — additive,
  nullable, no backfill. Applied only by `SpaceStroker.Consumers` at startup.
- `SpaceStroker.Infrastructure/Handlers/Players/SeenToursSerializer.cs` — `Parse` / `Union`, the key
  grammar, the caps. The CSV is a storage detail; the wire format is a JSON array.
- `…/Players/GetPlayerPreferences/GetPlayerPreferencesHandler.cs` — returns the set; defaults empty.
- `…/Players/UpdatePlayerPreferences/UpdatePlayerPreferencesHandler.cs` — unions on write.
- `…/UpdatePlayerPreferencesRequest.cs` (`PlayerPreferencesDto`) + `…RequestBody.cs` — contract.
- Tests: `SpaceStroker.Tests/UnitTests/Handlers/GetPlayerPreferencesHandlerTests.cs` +
  `UpdatePlayerPreferencesHandlerTests.cs`; `SpaceStroker.Tests.Integration/PlayerPreferencesTests.cs`
  (happy-path union).

**`monkify-front`**
- `src/hooks/useQuickTour.ts` — module singleton: `resolveTourKey`, open state, seen union, the
  auto-open guards, `markSeen`, `syncSeenToServer`.
- `src/constants/quickTours.ts` — `TOUR_VERSIONS`, `versioned()`, `TOURS`; the one place copy and art
  are edited. `src/constants/tourArtTiming.ts` — shared art timings.
- `src/components/game/QuickTourModal.vue` + `TourStepArt.vue`, `TourModeReel.vue`, `TourCrtScreen.vue`,
  `TourWalletArt.vue`, `TourLeverArt.vue`, `TourTerminalArt.vue`, `TourBetPanelArt.vue`,
  `TourBetTerminalArt.vue`, `TourEdsonSpeedArt.vue`, `TourWinRevealArt.vue`.
- `src/components/GameHeader.vue` — the `How to play` row (hidden when embedded), both auto-open
  watchers, the tour context, and the `walletGame` cluster rule.
- `src/hooks/usePrefsWallet.ts` — the single wallet-for-prefs rule. `src/hooks/useDisplayCurrency.ts`
  — publishes `seenTours` (cloned; `singleFlight` responses are shared and read-only).
- `src/utils/lotteryFrequency.ts` — extracted from `GameTerminal.vue`, now shared with `GameTypeCard`.
  `src/utils/tourStream.ts` — the fake character stream the art uses.
- `src/types/index.ts` — `seenTours` on `PlayerPreferences` / `PlayerPreferencesBody`.
- `src/hooks/useAnalytics.ts` — the two events. `src/main.css` — `html { overflow-x: hidden }`.
- Art: `src/assets/tours/game-modes.gif` plus existing assets (type icons, keypad buttons, lever and
  torpedo gifs, Edson). **Deviation from the spec:** the mode header art is this one sprite reel, not
  the three separate gifs copied from `monkify-lp` that the card listed.

## Known gaps

- `QuickTourModal` sets no `role="dialog"` / `aria-modal`, unlike `LegalGate` which sets both.
- The Standard/regulated **4-step overview** (wallet step absent) has never been exercised against a
  real Standard game — Main is all-`SolanaToken` in every environment, so no such game exists to
  enter. The 4-step *layout* is verified on mobile.

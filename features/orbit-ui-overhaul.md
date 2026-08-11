# Feature — Orbit UI/UX overhaul

> Status: **shipped.** Authoritative specs: [`../specs/orbit-page-restructure.md`](../specs/orbit-page-restructure.md)
> (**read this first**) and [`../specs/orbit-ui-overhaul.md`](../specs/orbit-ui-overhaul.md)
> (*partially superseded* — see the restructure's §0). Merged via `feature/orbit-ui-overhaul` on
> 2026-08-06. Trello #141 ([knC5QbPJ](https://trello.com/c/knC5QbPJ)).
> ⚠️ The control surface changed again on 2026-08-07
> ([orbit-single-control](orbit-single-control.md)): the page's control trio became a RUN/PAUSE +
> CASH OUT **duo**, the cool-off states are deleted, and the panel gained a game picker, a switch
> guard and the word gate.

## What it does

Orbit ([#121](orbit-custodial-wallet.md) custody → [#122](orbit-auto-bet-engine.md) engine →
[#123](orbit-notifications.md) notifications) shipped functionally complete and visually unfinished:
one 916-line hand-rolled overlay that bypassed the design system, and eight scattered touchpoints on
the game screen with nothing saying what Orbit was doing right now. This card rebuilds the whole
player-facing surface. Orbit is now **two surfaces with one job each** — a compact **panel** for the
money you move mid-round, and an **`/orbit` page** that is the order you set Orbit up in and, once
set up, the dashboard that says it is running. It also gives Orbit a visual identity, puts a module
in the game cabinet, makes deposits free-form whole numbers (the one backend change), and closes
#121's unmet mobile-deeplink sign-in.

Everything here is gated by `VUE_APP_ORBIT_ENABLED` (`orbitEnabled`, fail-closed) — see
[`../specs/orbit-frontend-flag.md`](../specs/orbit-frontend-flag.md). Orbit is dark in production.

## Core rules

### The split

- **The `/orbit` page owns every action. Nothing on the page opens a modal** — deposit, cash-out,
  rule editing, recovery, Telegram and turn-off are all inline.
- **The panel is the short version of the page, not a second copy.** It holds exactly two blocks —
  *turn Orbit on for the game you are already in* (no game `<select>`; the type comes from the route)
  and *add money* — plus a status strip and the seam row.
  ⚠️ **The "no game `<select>`" half is dead** — [orbit-single-control](orbit-single-control.md) gave
  the panel a picker on 2026-08-07, and **#164** (2026-08-10,
  [orbit-modal-improvements](orbit-modal-improvements.md)) made it render on **`!gameParametersId`
  alone**, including on a game page, preselected to the round on screen. Taking the type from the route
  is now the *auto-point*'s job, not an absence of a control: when that silent write does not land, the
  picker is what keeps the panel from being a dead end. Everything else in this rule stands.
- The page is a **four-step collapse-to-dashboard list**: `Game → Money → Stop rules → Telegram`.
  **Exactly one step is open at a time**; a satisfied step shrinks to one line carrying its answer in
  plain ink. **Sign-in is a precondition, not a step** — it disappears once satisfied.
- **Turn on and pick game are one step**, saved in a single patch: the backend refuses an enabled
  enrolment with no target game.
- **Cash out is promoted out of the footer utility strip whenever the balance is above zero**, into
  its own row with the amount on the button — money-out is never harder to find than money-in. It is
  an **outline** control, not amber: cashing out is available, not urged.
  ⚠️ **Since #163 the PANEL has a CASH OUT too**, so money-out is reachable from the round screen
  without leaving it — full balance, straight to the wallet signature, no inline confirm, and
  deliberately **outside** the word gate that ADD MONEY keeps. That is this same rule applied one
  surface further in.
- The **seam row** in the panel is labelled `ORBIT SETTINGS ▸` and states **both live stop-rule
  values**; the whole row, values included, is the tap target to `/orbit`.
  ⚠️ **The cash-out floor stays readable but not editable mid-round.** This is a knowingly accepted
  residue, re-examined and kept — see `orbit-ui-overhaul.md` §3.1 and the restructure's §3.1. If it
  proves wrong in the wild, the sanctioned fix is putting both rule editors in the panel, **not** more
  seam design.
  ⚠️ **RESOLVED, AND IT STAYS RESOLVED.** It did prove wrong, the sanctioned fix was taken, and both
  rule editors are editable in the panel today. **#163 (2026-08-10) tried to undo that** — replacing
  them with links out to `/orbit` — **and the removal was rejected on review**, so §3.1's residue is
  **closed**, not re-opened. Do not "correct" this section toward a read-only panel.
  ⚠️ **The way out is no longer labelled `ADVANCED SETTINGS`** (#163): it reads *"Set up notifications
  and more in Orbit settings"* and lands on `/orbit?tab=telegram` where a Telegram bot is configured,
  or *"More in Orbit settings"* → `/orbit` where none is.

### Colour and material

- **Four hues, one job each.** Green = active/running/satisfied (✓ pips, `● running`, `linked`).
  Violet = Orbit identity **and** "you are here". Red = warning only (`no floor`, `paused`, cool-off,
  failed deposit). Amber = **the active step's primary action button only**.
- **A healthy running Orbit shows no amber and no red at all** — that is what makes either mean
  something when it appears.
- **The weight rule is what prevents the salad:** colour lives only in small marks (a pip, a 3px bar,
  one status tag, one filled button). **Every value is plain ink.** The single exception is `no floor`,
  which is red text inside an otherwise-ink value.

  > ⚠️ **Extended by #151, and the rule above is exactly why.**
  > [money-changing-animations](money-changing-animations.md) adds a floating `+ / −` chip beside a
  > moving money figure: **credit is green (`--color-live`), debit is dim ink (`--color-ink-mute`) —
  > never red.** A red debit chip would fire on a **healthy running Orbit every single round** and burn
  > the warning signal `no floor` / `paused` / a failed deposit depend on. The `−` sign and the colour
  > carry the direction instead. **The value itself is still plain ink** — the chip is a small mark
  > beside it, which is precisely the shape this rule allows.
- The violet/violet clash (identity frame vs open step) is **resolved by intensity, not hue** — the
  frame and mark sit at `--color-orbit-deep`, the open step takes full `--color-orbit`. Do not "fix"
  it by giving the active step another colour.
- **`--color-orbit: oklch(0.72 0.17 292)`** (+ `--color-orbit-deep`) is a documented exception beside
  `GameTypeCard`'s archetype accents. **Cyan is not spent** — it stays the focus ring and Beast Mode's
  archetype accent.
- **`/orbit` renders on `.arcade-frame--orbit`** (void-deep behind a 2px violet-deep frame), a **third
  page material** added rather than bending `--black`'s documented rule (*brief, informative, light in
  tone, and never a warning about money* — `/orbit` carries a stop-loss). `conventions/frontend.md`
  therefore documents **three** panels, not two.

### Deposits (the only backend change)

- **A deposit is any whole number of USDC in `[1, MaxDepositPerTransaction]`**, and the
  per-transaction cap is **100** (was 10). The fixed `1 / 5 / 10` ladder is gone from the UI.
- **Validated on both sides, front first.** `useOrbit.deposit` throws **before** `sendToken` starts a
  transfer, alongside the existing cool-off / Orbit-off / self-deposit guards. This matters because the
  deposit is two-phase: a front-only amount input would let a player move USDC on-chain and *then* be
  refused, with `Recover a deposit` refusing it too. The client message names a **published bound**, so
  it is allowed to be specific — `constraints.md` §6 is untouched.
- **`Recover a deposit` takes the same field.** A ladder select cannot recover the deposit it exists to
  rescue.
- **No quick-fill chips.** `DepositOptions` is still returned by the API (the ⚠️ binder-append hazard on
  `AvailableDepositOptions` is why it is not deleted) but the front renders it nowhere.
- **Nothing else is relaxed** — the daily cap, pool float cap, disable cool-off, idempotency-by-signature
  and one-cash-out-per-day all survive untouched.

### The game screen

- **One console-cabinet module** is the game-screen entrance. It shows balance + the **one** live state
  + a way in, and **carries no inline money action** — a mis-tap beside the bet lever spends real USDC.
- It **absorbs** the previously inert `> ORBIT PLACED YOUR BET FOR THIS ROUND.` line from **both**
  `GameConsole` branches: "covered this round" is a state of the module, not a separate dead line.
- It renders **only** for enrolled, enabled players on Orbit-eligible types. Orbit cannot play
  lotteries, so the module is absent on Space Jackpot.
- It is a **documented design-system exception**, built in the cabinet language (metal, rivets, CRT) —
  not from `.btn-dialog`.
- **The header `ORBIT` row stays on every route**, subordinate to the module. ⚠️ Two live renderings of
  one balance exist and Orbit's balance **moves without the player acting**, so both read the same
  module-level state and never refresh independently.

  > ⚠️ **Corrected by #151 (2026-08-08).** This card's header control was a bare 40px violet **door**
  > carrying no number — *"a DOOR, not a readout"*, the owner's 2026-08-04 call.
  > [money-changing-animations](money-changing-animations.md) **narrowly reversed the number part**:
  > the door is now a **balance-carrying pill**, still one door, still no `SIGN IN` and no text label.
  > The "one module-level state, never refresh independently" rule is untouched and now **load-bearing
  > for the animation** — the pill and Orbit's own `ORBIT HOLDS` figures animate off the same signal.
- **All three #123 toasts still fire, unchanged.** The module additionally carries `paused` and the last
  win **as state** — the toast says "it happened", the module says "it is true".

### Identity, overlays and flow

- **Every Orbit overlay goes through `ArcadeModal`.** The Orbit panel is **grey**; the first-win upsell
  was **black**. That grey/black split was a rule, not a style, and reskinning both did not blur it.
  ⚠️ **Superseded for the upsell by #157 (2026-08-08):** `OrbitFirstWinModal.vue` is now the **Orbit**
  material at `size="compact"` (540), which leaves `variant="black"` with no dialog consumer at all
  (`conventions/frontend.md` records the consumer-less tier). The panel is still grey. The upsell is
  also no longer led by Orbit: its titlebar reads **`CONGRATS!`**, the body shouts **`NICE WIN!`**, and
  the winning word is quoted from the game's terminal inside a miniature CRT in phosphor green. Orbit's
  identity on that surface is now one 30px mark beside the supporting line, plus the amber CTA — the
  celebration leads, the upsell follows. The **conditional** supporting line is unchanged in intent:
  the word-ownership beat renders only on free-text types, with its `while it runs` qualifier.
- Orbit ships **one mark and four action glyphs**, on a 16px/2px grid in `currentColor`. The two
  stop-rule glyphs carry the semantic OK/ERR hues so nothing is stolen from the accents. **There is no
  app-wide icon system** — the glyph set stays Orbit-scoped.
- **Mobile deeplink sign-in works** (#121's AC5, now closed). The pending-hop stash carries a
  **three**-value action discriminator — `session | cashout | disable` — because disabling Orbit signs
  the same `orbit-cashout` message; a two-value stash would ship a dead path. ⚠️ The wallet hop is
  **opt-in per call site**: `ensureSession` has background callers, and an unconditional hop would
  navigate a deeplink player into their wallet app on boot.
- **The cross-type redirect is declinable** — the toast offers `STAY HERE`. The 2s poll cadence and the
  detection logic are deliberately **untouched** (a separate earmarked follow-up).
- **`/orbit` is in `EMBED_REDIRECT_ROUTES`** — a framed player reaching it would leak Orbit — and is
  deliberately **not** an `isPlayerRoute()`.

## Known gaps (shipped knowingly)

- ⚠️ **The mobile deeplink path is verifiable only on a physical phone**, and was not run on one. A wrong
  stash key there breaks the **bet** hop, which is the money path.
- ⚠️ **The floor's edit is off the round** on a phone — see the seam-row rule above.
- `monkify-front` has **no test runner**, so the bar for this work is `yarn lint` + `yarn build` clean
  plus manual QA at desktop and phone widths.

## Key code touchpoints

**`monkify-backend`** (the only backend surface this card touches)

- `SpaceStroker.Domain/Configs/Entities/OrbitSettings.cs` — `MaxDepositPerTransaction` `10m` → `100m`.
- `SpaceStroker.Infrastructure/Handlers/Orbit/CreditOrbitDeposit/CreditOrbitDepositHandler.cs` — the
  denomination check is now a whole-number range check. Idempotency, cool-off, daily cap and pool float
  cap are untouched.
- `SpaceStroker.Infrastructure/Handlers/Orbit/GetOrbit/{GetOrbitHandler,GetOrbitRequest}.cs` — expose
  `MinimumDeposit` and `MaxDepositPerTransaction`; `DepositOptions` still ships.
- `SpaceStroker.Tests.Integration/OrbitFlowTests.cs` — the free-form deposit happy path, end to end.
- `SpaceStroker.Tests/UnitTests/Api/OrbitSettingsBindingTests.cs`,
  `SpaceStroker.Tests/UnitTests/Handlers/Orbit/CreditOrbitDepositTests.cs` — cap + range assertions.

**`monkify-front`**

- Page — `src/pages/OrbitPage.vue`; route + `EMBED_REDIRECT_ROUTES` in `src/routes.ts`.
- Orbit components — `src/components/orbit/`: `OrbitModal.vue` (rebuilt on `ArcadeModal`),
  `OrbitFirstWinModal.vue` (reskinned, `variant="black"`), `OrbitStepRow.vue`, `OrbitStatusBanner.vue`,
  `OrbitStatusTag.vue`, `OrbitAmountField.vue`, `OrbitLauncher.vue`, `OrbitBetLock.vue`,
  `OrbitRedirectToast.vue`, `OrbitMark.vue`, `OrbitGlyph.vue`.
  ⚠️ **`OrbitMark.vue` and `OrbitGlyph.vue` were deleted by #159** — their drawings moved **unchanged**
  into the shared registry (`src/constants/icons.ts`) and every call site now renders `<AppIcon>`
  (`orbit`, `deposit`, `cash-out`, `win-at`, `falls-to`, `run`, `stop`). Zero visual change; the
  off-ladder sizes here (11/13/14/18/20/26/28/30/52px) are deliberately preserved. That also **retires
  the #124 D7 / AC16 rule** those files carried ("NOT the seed of an app-wide icon system") — Orbit's
  technique is now the app-wide one. See `features/icons-and-menu-overhaul.md`.
  ⚠️ `src/components/OrbitBalance.vue` was **deleted** — the header row now renders from the shared state.
- Game screen — `src/components/game/GameConsole.vue` (both branches), `src/components/GameHeader.vue`.
- Styles — `src/assets/styles/colors.css` (`--color-orbit`, `--color-orbit-deep`),
  `src/assets/styles/arcade.css` (`.arcade-frame--orbit`, the Orbit dialog material, the step row).
- Composables — `src/hooks/useOrbit.ts` (deposit bound guard, DTO bounds, `roundsLeft`),
  `useHeaderModals.ts` (`isOrbitOpen` moved here out of `GameHeader`, so `/orbit` can reach it),
  `useMobileWallet.ts` (the three-value Orbit stash), `useOrbitRedirect.ts` (declinable navigation),
  `useOrbitNotifications.ts`, `useFirstWinUpsell.ts`, `useSelfExclusion.ts`, `useGame.ts`.
- Types — `src/types/orbit.ts`, `src/types/mobileWallet.ts`.
- Guide — `src/components/guide/sections/GuideOrbit.vue` + `src/constants/guideSections.ts`.

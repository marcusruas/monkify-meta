# Domain — Orbit (custodial balance + auto-betting)

> **What this covers.** Orbit is the platform's **first custody of real player money**: a house-held
> balance a player funds with on-chain SOL deposits. It has **two spenders**: the **auto-bet engine**,
> which places **one real bet per round** on the player's behalf (Cosmic Sprint's shared rounds) or, since
> #200 (2026-10-06), opens and plays the player's **own** Monkey Duel (VS HOUSE) or Hyper Roll rounds back
> to back (the **solo loop**, [`auto-bet-duel-hyper-roll.md`](auto-bet-duel-hyper-roll.md)), and cashes
> out at a threshold they set; and, since #194 (2026-09-24), **the player, by hand**: manual bets on any paid game, paid from the
> balance with no per-bet signature. The SPA pays from Orbit first whenever it covers the stake. The
> manual path is distilled in [`orbit-wallet.md`](orbit-wallet.md); this doc covers custody, the engine,
> the stop rules and the surfaces.
>
> ⚠️ **Orbit is LIVE in production** (owner-confirmed 2026-09-02). It is still gated by
> `VUE_APP_ORBIT_ENABLED` (`orbitEnabled`, fail-closed, build-time), but production builds now set
> it — the flag is the switch, not a statement that the feature is dark.
>
> ⚠️ *This doc said "DARK in production" until 2026-09-02, long after that stopped being true, and the
> staleness had teeth: while writing the leaderboards copy (#182) it produced a first draft that
> deliberately avoided naming Orbit, on the grounds that naming a dark feature would describe
> something no player could see. **Real player money moves through Orbit in production — reason about
> it as a live money path, not a dormant one.*** Historical entries in `INDEX.md` that say Orbit was
> dark are dated records of what was true when those cards shipped; they are left as history.
>
> Built as a three-card train — custody → engine → notifications — then reworked by four follow-ups.
> Deep design: [`../specs/orbit-custodial-wallet.md`](../specs/orbit-custodial-wallet.md) ·
> [`../specs/orbit-auto-bet-engine.md`](../specs/orbit-auto-bet-engine.md) ·
> [`../specs/orbit-notifications.md`](../specs/orbit-notifications.md) ·
> [`../specs/orbit-new-ui.md`](../specs/orbit-new-ui.md) ·
> [`../specs/orbit-single-control.md`](../specs/orbit-single-control.md) ·
> [`../specs/orbit-modal-improvements.md`](../specs/orbit-modal-improvements.md) ·
> [`../specs/orbit-feedback-adjustments.md`](../specs/orbit-feedback-adjustments.md) ·
> [`../specs/orbit-stop-loss-trigger.md`](../specs/orbit-stop-loss-trigger.md) ·
> [`../specs/orbit-wallet.md`](../specs/orbit-wallet.md) (manual bets, #194) ·
> [`../specs/orbit-improvements.md`](../specs/orbit-improvements.md) (popup + one modal, `/orbit` page removed, #198) ·
> [`../specs/auto-bet-duel-hyper-roll.md`](../specs/auto-bet-duel-hyper-roll.md) (auto-bet on Monkey Duel + Hyper Roll, #200).

---

## 1. Custody — where the money lives

- ⚠️ **The Orbit account is a keypair configured at `GeneralSettings:Operator:Token:Orbit` and is
  DELIBERATELY SEPARATE from the bet/payout keypair.** Sharing them would make "player balances vs
  on-chain balance" uncheckable — **and** an Orbit-funded bet would become a same-account transfer,
  which confirms on-chain as a **no-op**.
- ⚠️ **Every balance and ledger row is stamped with its pool** — `(OrbitAccount, MintAddress)`.
  Rotating the configured account or mint starts a **new pool at zero** and leaves the old one intact
  and withdraw-only, so a balance is never silently re-pointed at a different pot of money.
- ⚠️ **The invariant is `OrbitBalance.Amount == SUM(OrbitTransaction.Amount)`** for that pool and
  player, over **all** rows with **no status filter**. A failed cash-out stays at `−X` and is cancelled
  by an appended compensating row — never by deleting or filtering one.
- Deposits are **free-form** within a published bound — ladder `0.001 / 0.1 / 0.5` SOL, minimum
  **0.001** (lowered from 0.05 on 2026-09-25), maximum **5 per transaction**, minimum **partial** cash-out **0.001**. ⚠️ **THE MINIMUM
  CASH-OUT DOES NOT GATE EMPTYING THE BALANCE:** a request for the *whole* remaining balance is always
  accepted, however small. As an unconditional gate it trapped funds — only the automatic sources
  (threshold, floor, forced) skip the check, so anyone left under it by a losing run, a deposit below
  it, or a floor that never fired had **no path to their own money at all**, and the front disabled the
  button outright. Enforced in `OrbitCashOutReserver` (`amount < MinimumCashOut && amount != available`)
  and mirrored in `OrbitMoneyTab.vue`, which sends **no amount** for a full withdrawal so the escape
  hatch cannot miss by a float hair. ⚠️ **DECIMALS ARE LEGAL:** the
  whole-number rule was policy inherited from the USDC ladder and in SOL meant a minimum deposit worth
  a few hundred dollars. What replaces it is a **precision** check against the asset's decimals —
  crediting an amount finer than the chain can move would write a balance no cash-out could pay back
  out. The deposit-bound message (*"Deposits are between 0.001 and 5 SOL."*) remains a **sanctioned
  exception** to the vague-messaging rule: it states a bound the backend already publishes, reveals
  nothing about a session or another player, and is refused **client-side before the transfer is
  signed** — a player told only "that could not be accepted" after their money left the wallet has no
  way to learn what is allowed. ⚠️ The **chips** on the field are derived from that published ladder,
  never hard-coded; the old literal `+10/+25/+50` survived a currency switch once and pinned a 5-SOL
  field to its ceiling on every tap.
- **Depositing needs no enrolment** (#194). A player who never turned auto-bet on can fund Orbit and
  spend it on manual bets; `CreditOrbitDepositHandler` no longer refuses with `OrbitNotEnabled`. A deposit
  still never starts betting by itself: only an **Enabled** enrolment puts a player in front of the
  planner, and the deposit re-plan finds no candidate without one. Self-excluded players are still
  refused.
- ⚠️ **The low-SOL guard is a SOLVENCY check, not a balance check.** With native settlement the stake
  and the gas are the same lamports, so player deposits sit in the very balance the guard reads: it
  requires `solBalance − Σ(the pool's owed player balances) ≥ MinimumSolBalance`. A flat floor would
  pass forever while the house float was empty. See [`sol-settlement.md`](sol-settlement.md).

## 2. The engine — who bets, and how

For every player who is **enrolled, enabled on a game type, and funded**, the house places one real
bet per round out of their Orbit balance: a house-signed on-chain transfer with a choice derived from
the round's committed `ServerSeed`. ⚠️ **To every other player the bet is indistinguishable from a
human's.**

⚠️ **Two engines since #200.** Everything in this section describes the **shared-round planner**, which
plays **Cosmic Sprint**. **Solo types** (`IsHouseEdge || IsDuel`, i.e. Hyper Roll and Monkey Duel) are
played by a separate **solo loop**: `OrbitSoloRoundDueEvent` → `OrbitSoloAutoBetService` opens the
player's own round, stakes the enrolment's `Stake`, waits for settlement plus
`SoloRoundGapSeconds`, and repeats, with a lease-guarded one-round-in-flight rule. The planner, `IsOrbitEligible`,
`PlaceOrbitBetConsumer` and `BetPlacedConsumer` all **refuse solo rounds**, so a human's Hyper Roll board
can never pull Orbit players in. No `OrbitChoiceClaim` is ever taken on a solo type. Full rules:
[`auto-bet-duel-hyper-roll.md`](auto-bet-duel-hyper-roll.md).

⚠️ **Streamer wallets never auto-bet (#201, 2026-10-07).** An active streamer
([`streamer-wallets.md`](streamer-wallets.md)) plays a play-money balance, not custody money: the Consumers
startup reconciler switches its enrolment off on **every** run (`Enabled = false`, `WaitingForHouseSince`
cleared), the shared planner's `ResolveCandidates`, the solo loop's tick and pre-place re-check, the
`orbit-solo-autobet-sweep` and the startup kick all exclude it, and `PATCH /orbit/settings` refuses it
(`OrbitRequestInvalid`). Its real Orbit balance stays in custody, hidden from `GET /orbit` (which shows the
play balance with `isStreamer: true`), until the wallet is deactivated. A real-money solo round already in
flight when the wallet is registered still settles to custody normally.

- **Every eligible enrolled player bets every round — no cap.** Filling only the gap to
  `MinimumNumberOfPlayers` was rejected: it silently skips rounds a player enrolled to play.
- ⚠️ **The house seed bet counts as an ordinary existing bettor in Orbit's viability check** (#172), so
  on a `MinimumNumberOfPlayers = 2` type one funded Orbit player is enough to start a round with **no
  human in it**. Two defects hid this: the planner runs at t≈0 while the house bet waits a randomised
  0–10s, and `BetPlacedConsumer`'s re-plan trigger sat **below** its under-subscribed early return —
  which also broke the plain 1-human + 1-Orbit case decision 4 promised. Both are fixed in
  [`../specs/house-seed-orbit-conflict.md`](../specs/house-seed-orbit-conflict.md), together with a
  Consumers startup pass that re-plans every open round.
- ⚠️ **A failed placement must not be re-planned into.** `FailPlacement` decrements the gate and
  republishes `BetPlacedEvent` to wake the round, so anything that re-plans on that event will
  re-dispatch the same player forever. `BetPlacedConsumer` distinguishes the two ways a round reaches
  `ExpectedOrbitBets == 0`: **`MarkPlanned`** (expiry `null` — nobody to dispatch, still re-plannable)
  versus **`ReleaseGateSlot`** (expiry set — armed and every placement failed, never re-plan). The
  `BetDebit` claim index does **not** bound this: it only exists after `ClaimAndDebit`, so every
  pre-claim failure (no chosen word, low SOL, no committed seed, no free choice) would loop.
- **Eligible** = enrolment `Enabled`, `GameParametersId` matches the round's type, **not**
  self-excluded, the run has not reached its **bet limit** (#208), and the pool balance covers
  `RequiredAmount` (on a solo type: the effective stake, `OrbitRunStake.Effective` = the stepped
  `RunNextStake ?? Stake ?? RequiredAmount` clamped to the type's bounds, against both the settled and the
  spendable balance). ⚠️ **A player who already has a bet in
  the round is excluded** — Orbit gets no exemption from the one-bet-per-round rules.
- ⚠️ **A `ComingSoon` game type is never Orbit-eligible** (see [`games.md`](games.md) §19). Three reads
  enforce it: `GetOrbitHandler` drops it from `eligibleGameTypes` (that is what shapes the UI),
  `UpdateOrbitSettingsHandler` refuses a PATCH naming it with the existing vague `OrbitRequestInvalid`
  (an old client), and `OrbitBetPlacementService.IsOrbitEligible` refuses to place — **that third one
  is the one that protects money**, because a stale enrolment betting onto a type that refuses bets
  would land in a refund loop.
- ⚠️ **`Bet.FundedByOrbit` exists solely because Orbit's custody account signs on behalf of a
  DIFFERENT player's wallet**, which `SolanaService.ValidateBetPayment` would otherwise reject. It is set
  on **engine bets and manual Orbit-paid bets** (both are custody → treasury transfers) and must never be
  set on any other bet — see the house seed bet in [`games.md`](games.md) §14, where setting it would
  discard a validation that genuinely passes **and** route a win into the custody account.
- ⚠️ **`Bet.OrbitAutoPlaced` means "the engine placed it"** (#194). `FundedByOrbit` now answers only
  *where the money is* (skip on-chain validation, route the win or refund to custody). Every **auto**
  reader uses `OrbitAutoPlaced`: the start gate, the planner's landed-bet count, the house-seed
  viability count, the stop rules, the "Orbit won" toast, the $STROKE exclusion, `BetDto.ViaOrbit` and
  the round-history "via Orbit" chip. The `AddOrbitManualBets` migration backfilled it `true` for every
  earlier `FundedByOrbit` bet. See [`orbit-wallet.md`](orbit-wallet.md).
- The engine is where the `BetDebit` / `WinCredit` / `Refund` ledger types are actually written.
- ⚠️ **An engine-placed bet carries the player’s referral code** (#175) with no code in this path at
  all: the placement consumer sends an in-process `RegisterBetRequest`, and `RegisterBetHandler` stamps
  `Bet.ReferralCode` from the **`Player` row** rather than from the request body — which is precisely
  what makes it work here, since the body the consumer builds has no referral on it. A player who
  enrols in Orbit **before ever betting** is attributed by the `POST /orbit/session` write instead.
  See [`referral-program.md`](referral-program.md).

### Reserved words claim the choice across rounds

⚠️ An Orbit player may set a **fixed word**, and on a free-text type that becomes a **standing claim
on the game type** (`OrbitChoiceClaim`, unique on `(GameParametersId, Choice)`) blocking everyone else
in **every** round — including rounds Orbit does not bet in. It does **not** free on the next `Game`;
it is released when the word is changed, cleared, Orbit is disabled, or the enrolment switches to
another game type (#200). **Preset types and solo types (Monkey Duel, Hyper Roll) are never claimed**: a
standing claim on the duel would block that word in everyone's duels. A blocked human sees `ChoiceAlreadyTaken` and the same refund path, **byte-identical** to a
same-round collision — deliberately, so Orbit stays invisible. See [`games.md`](games.md) §11.

## 3. Control surface — one flag

Orbit once had **four** points of control (`Enabled`, where OFF force-cashed-out everything and started
an hours-long re-enable cool-off; `Stopped`; the re-enable lock; and the manual cash-out gap), and live
testing read the flow as *"sign in → enable? → set up → then hit play (to unpause??)"*.

⚠️ **They collapsed to one.** `Enabled` is the only flag and behaves exactly like play/pause. Cash-outs
are their own thing. **The only cool-off left in Orbit is the rolling manual cash-out gap.**

Also available: **partial cash-out**, a **cash-out floor** and a **stop-loss trigger**. The backend still
serves a per-player bet log (`GET /orbit/history`, each row labelled **MANUAL** or **AUTO** via
`OrbitBetLogDto.Source`, numeric `Auto = 1`, `Manual = 2`, from `Bet.OrbitAutoPlaced`), but ⚠️ **no
surface renders it since #198** — the owner removed the BET HISTORY section; `OrbitBetLog.vue` is deleted
and `useOrbit().loadHistory` has no caller.

⚠️ **Threshold, floor and stop-loss apply to AUTO-bets only** (#194 decision 9). A manual Orbit-paid
win or loss never triggers an automatic cash-out: `OrbitBetSettlementService.CreditWin` evaluates the
threshold (and publishes the win notification) only for an `OrbitAutoPlaced` bet, and
`RewardGameConsumer.EvaluateOrbitFloors` only looks at players with an auto bet in the round. Every
place these settings appear says so (*"These rules only watch auto-bets…"*; since #208 the old *"AUTO CASHOUT —
AUTO-BETS ONLY"* eyebrow gave way to the rules tabs, and its note rides in the fields' info tips).

⚠️ **An Enabled enrolment blocks manual play on that type.** While auto-bet is `Enabled` on a game type,
every manual bet by that player on it, wallet **or** Orbit-paid, is refused with *"Orbit is
auto-playing this game — pause it to bet manually."* (`OrbitAutoPlayActive`). A **paused** enrolment
does not block. The game console disables the lever while the block holds (since #200 the duel console
too). On Hyper Roll and Monkey Duel the screen also **follows** each auto round live (see
[`auto-bet-duel-hyper-roll.md`](auto-bet-duel-hyper-roll.md)). See [`orbit-wallet.md`](orbit-wallet.md).

⚠️ **The floor fires STRICTLY BELOW itself, never at it** (changed 2026-09-07; it was `<=`). At the
floor is still playing. The old boundary made a floor set *equal to* the balance — deposit 0.05, stop
loss 0.05 — read as "already stopped", so `CashOutFloorStoppedPlayers` withdrew the deposit at the next
round's planning and the player never played a game. That sweep runs for **every** enrolled player with
a floor, bet or not, which is why it fired without a round being played. The comparison is duplicated in
three places and they must never disagree: `OrbitBetSettlementService.EvaluateFloor` (`balance >= floor`
⇒ keep playing), `OrbitBetPlacementService.ResolveCandidates` (`Amount >= CashOutFloor` ⇒ still a
candidate) and `CashOutFloorStoppedPlayers` (`Amount < CashOutFloor` ⇒ send it home).

**Accepted cost:** a floor that is an exact multiple of the stake can be stepped straight over —
`0.10 → 0.05 → 0` with a 0.05 stake and a 0.05 floor never fires with anything left to send. At the
stake sizes the game actually runs, landing exactly on the floor is the rare case and setting a floor
you are already at is the common one.

⚠️ **A fourth way auto-bet stops by itself: the bet count** (#208, 2026-10-16). A run = one START; with a
**bet limit** N set, Orbit **pauses** (`Enabled = false`, money stays in custody) once N auto bets of the run
have settled. It does **not** switch threshold / floor off — whichever fires first wins. On Monkey Duel and
Hyper Roll the run can also **step the stake** after each win / loss (back to base, or raise by X%, clamped
at `MaximumStake`). The slot is taken in `OrbitStakePlacer.ClaimAndDebit`, so even Cosmic Sprint's early
successor-round placement can never exceed N. Full rules in
[`new-options-for-orbit.md`](new-options-for-orbit.md).

## 4. Notifications

Orbit bets and settles with nobody watching, so until the third card nothing told the player — a win
only showed up if they opened the modal, and money leaving custody, including forced cash-outs they
never asked for, was **silent**.

- A **private per-player realtime channel** on the existing `ActiveGamesHub` announcing every Orbit
  **auto-bet** win, wherever the player is in the app. A manual Orbit-paid win sends nothing there; it
  gets the ordinary win toast, and the SPA re-reads the balance when the round settles.
- **Telegram messages when money leaves custody** — every cash-out, linked with **no poller and no
  webhook**. Per-event toggles.
- An honest **"paused" state** when Orbit stops betting for lack of funds.
- An in-app toast when a run **finishes its bet count** (`OrbitRunFinished`, #208) — **never Telegram**,
  because a pause moves no money.
- A one-time **invitation into Orbit** for a first-time winner.

## 5. The player-facing surface

Orbit shipped functionally complete and visually unfinished — one 916-line hand-rolled overlay that
bypassed the design system — then spent #141 → #194 as **two surfaces** (a compact in-round panel and a
five-tab `/orbit` page). New players read the two as one confusing feature, so **#198 (2026-09-30)
collapsed them**: ⚠️ **the `/orbit` page is GONE** (route, `OrbitPage.vue`, its tab components and the ☰
ORBIT cell) — **no redirect**, an old link hits the catch-all. Orbit is now:

1. **The quick popup** (`OrbitQuickPanel` + `OrbitQuickBody`, owner-picked mockup **P1**) — what a
   **signed-in** player gets from the header pill: the status tag on its own row, then **ORBIT HOLDS**
   and an amber **WALLET HOLDS** card side by side (wallet icon, white amount — `OrbitStatusBanner`
   `walletLabel`), **ADD MONEY**, **CASH OUT** (full or partial) and **MORE OPTIONS** → the modal. The
   wallet card is there because since #186 the header bar shows **one** balance: while Orbit holds money
   the wallet pill is hidden, and with an empty Orbit the Orbit pill folds to its icon square
   ([`frontend-ux.md`](frontend-ux.md)). No auto-bet control. Anchored under the pill
   from `lg` up (outside click / Esc close it), an `ArcadeModal` bottom sheet below `lg`. Signed out, the
   pill opens the modal's sign-in view instead — the popup never mints a session.
2. **The Orbit modal** (`OrbitModal`, mockup **M4**) — ORBIT HOLDS + WALLET HOLDS + status tag, a **jump strip** of
   chips, and collapsible sections: **WALLET** (add money + cash out) · **LET ORBIT PLAY**
   (game tiles, the chosen game's setup, START / PAUSE AUTO-BET, and the rules as two tabs since #208:
   **WHEN TO CASH OUT** — BALANCE OVER / BALANCE UNDER — and **BETS & STAKE** / **HOW MANY BETS** — the
   bet count and stake steps, see [`new-options-for-orbit.md`](new-options-for-orbit.md)) · **NOTIFICATIONS** (Telegram, only where a
   bot is configured) · **RECOVER A DEPOSIT**, plus a *How Orbit works* link to `/how-to-play#orbit`.

⚠️ **ONE SECTION OPEN AT A TIME, and the modal is PINNED** (owner, after the first local test): no
scrolling, and the lit chip is always the open section. The modal opens on the section a deep link names,
else auto-bet while it is running, else the one the player last left open (`space-stroker.orbit.section`
in localStorage, `none` when all collapsed), else WALLET. `ArcadeModal pinned` hangs it from a fixed top
with `min-height: min(720px, 84dvh)` on desktop and a fixed `90dvh` sheet on mobile, so switching sections
never re-centres or resizes it upward.

⚠️ **The auto-bet section picks its game with tiles** (#200, `OrbitGameTiles`, one per
`eligibleGameTypes` entry). The selected game is the enrolled type when it is still eligible, else the first
tile, and pressing a tile while paused sends `PATCH { gameParametersId }`. The game, the stake, the Hyper Roll
mode and multiplier — and since #208 the bet limit and the stake steps — are **locked while Orbit is enabled**
(pause → pick → START); the word and the cash-out rules stay editable. The auto-pointing at the round on screen and the old "switch guard" stay gone: START sends
`{ enabled, gameParametersId }` from **any** page with **no navigation**. With zero eligible types the
section is hidden. The word still gates the stop rules only — money in and money out never depend on it.
Per-game fields, the stake plate and *"Waiting for the house…"* are in
[`auto-bet-duel-hyper-roll.md`](auto-bet-duel-hyper-roll.md).

⚠️ **Cash-out is "empty = everything".** The field starts empty with placeholder `all <balance>`; empty
(or exactly the balance) sends **no amount**, so the escape hatch cannot miss by a float hair; a partial
must be `>= minimumCashOut`. The field is **not** disabled by the shared `isBusy` flag — every Orbit write
sets it, and a field that locked while a word or rule auto-saved read as "I can't change the amount".

**Entry points:** the header pill (popup / sign-in); the **out-of-funds toast** opens the popup (the modal on
WALLET if the session lapsed); the **first-win invite** opens the modal on the auto-bet section; the **home
card** opens the modal (with no wallet connected its sign-in view says *"Connect a wallet first — Orbit is
tied to the wallet that owns the balance."*). Every one goes through `useHeaderModals()` —
`openOrbit(section?)` / `openOrbitPopup()` — so there is still exactly one modal instance.

**Palette** (from the mockups): ADD amber fill (`OrbitAmountField confirmPrimary`), CASH OUT and RECOVER
amber outline (`.btn-dialog--outline-primary`), START AUTO-BET violet fill (`.btn-dialog--orbit`), PAUSE
and the Telegram buttons violet outline (`.btn-dialog--orbit-outline`). The status tag keeps PAUSED /
RUNNING / OFF / NEEDS MONEY.

On the game screen the header pill that will pay the next bet is lit and the stake LCD takes its colour;
the console itself carries no Orbit control (see [`orbit-wallet.md`](orbit-wallet.md)).

### Three eligible types

Since #200 (2026-10-06) **three** types are Orbit-eligible: **Cosmic Sprint** (shared rounds, fixed price),
**Hyper Roll** and **Monkey Duel** (solo loop, player-set stake; `AllowOrbit: true` in `DefaultGames`, healed
at Consumers startup). Space Jackpot stays `AllowOrbit: false`, because it is a free holder draw with no
stake. *(Between #198 and #200 Cosmic Sprint was the only eligible type and the section was hard-wired to it.)*
Enrolment is still a deliberate press — nothing auto-commits on open (#194).

## Key code touchpoints

**Backend (`monkify-backend`)** — `SpaceStroker.Domain/Orbit/*` (entities `OrbitEnrolment`,
`OrbitBalance`, `OrbitTransaction`, `OrbitChoiceClaim`), the Orbit handlers under
`SpaceStroker.Infrastructure/Handlers/Orbit/`, `OrbitPlacementPlannerConsumer` (on `GameOpenedEvent`,
⚠️ **a separate receive endpoint from `HouseSeedBetConsumer`, so a slow house placement never delays
Orbit planning for the same round**), the cash-out job, and `SpaceStroker.Api/Controllers/OrbitController.cs`.
The solo loop (#200): `OrbitSoloAutoBetService`, `OrbitSoloLoopStore`, `HangfireOrbitSoloScheduler`,
`OrbitSoloRoundConsumer` (its own endpoint), `OrbitSoloAutoBetSweepJob`, `Domain/Orbit/Services/OrbitSoloTypes`,
`OrbitSlotPicker`, `OrbitSoloRoundState`. See [`auto-bet-duel-hyper-roll.md`](auto-bet-duel-hyper-roll.md).
Run limits & stake steps (#208): `OrbitRunService` / `OrbitRunStore`, `Domain/Orbit/Services/OrbitRunStake` +
`OrbitRunRules`. See [`new-options-for-orbit.md`](new-options-for-orbit.md).

**Frontend (`monkify-front`)** — `src/components/orbit/OrbitQuickPanel.vue` + `OrbitQuickBody.vue` (popup),
`OrbitModal.vue` + `OrbitSection.vue` + `OrbitSectionStrip.vue` + `sections/OrbitAutoBetSection.vue`
(state in `src/hooks/useOrbitAutoBet.ts`; `OrbitGameTiles` / `OrbitStakePlate` / `OrbitMultiplierPicker` /
`OrbitRulesTabs` / `OrbitStakeStepRow`) /
`OrbitNotificationsSection.vue` / `OrbitRecoverSection.vue` (modal), `OrbitDepositControl.vue` / `OrbitCashOutControl.vue` (the shared money
controls), `src/hooks/useOrbitSections.ts`, `src/hooks/useHeaderModals.ts`, `src/hooks/useOrbit.ts`,
`src/services/orbitService.ts`, and `ArcadeModal`'s `pinned` option — all behind `orbitEnabled`. Design
record: [`../specs/orbit-improvements.md`](../specs/orbit-improvements.md) (#198, incl. §16 feedback).

**Manual Orbit bets** (`POST /orbit/wagers`, `PlaceManualOrbitBetConsumer`, the shared `OrbitStakePlacer`,
`OrbitStakeReconciliationJob`, `useGame.nextBetSource`) are listed in
[`orbit-wallet.md`](orbit-wallet.md).

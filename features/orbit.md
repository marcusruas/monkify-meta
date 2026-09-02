# Domain — Orbit (custodial balance + auto-betting)

> **What this covers.** Orbit is the platform's **first custody of real player money**: a house-held
> balance a player funds with on-chain SOL deposits, out of which the house places **one real bet per
> round** on their behalf, crediting winnings back and cashing out at a threshold they set.
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
> [`../specs/orbit-stop-loss-trigger.md`](../specs/orbit-stop-loss-trigger.md).

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
- Deposits are **free-form** within a published bound — ladder `0.05 / 0.1 / 0.5` SOL, minimum
  **0.05**, maximum **5 per transaction**, minimum cash-out **0.05**. ⚠️ **DECIMALS ARE LEGAL:** the
  whole-number rule was policy inherited from the USDC ladder and in SOL meant a minimum deposit worth
  a few hundred dollars. What replaces it is a **precision** check against the asset's decimals —
  crediting an amount finer than the chain can move would write a balance no cash-out could pay back
  out. The deposit-bound message (*"Deposits are between 0.05 and 5 SOL."*) remains a **sanctioned
  exception** to the vague-messaging rule: it states a bound the backend already publishes, reveals
  nothing about a session or another player, and is refused **client-side before the transfer is
  signed** — a player told only "that could not be accepted" after their money left the wallet has no
  way to learn what is allowed. ⚠️ The **chips** on the field are derived from that published ladder,
  never hard-coded; the old literal `+10/+25/+50` survived a currency switch once and pinned a 5-SOL
  field to its ceiling on every tap.
- ⚠️ **The low-SOL guard is a SOLVENCY check, not a balance check.** With native settlement the stake
  and the gas are the same lamports, so player deposits sit in the very balance the guard reads: it
  requires `solBalance − Σ(the pool's owed player balances) ≥ MinimumSolBalance`. A flat floor would
  pass forever while the house float was empty. See [`sol-settlement.md`](sol-settlement.md).

## 2. The engine — who bets, and how

For every player who is **enrolled, enabled on a game type, and funded**, the house places one real
bet per round out of their Orbit balance: a house-signed on-chain transfer with a choice derived from
the round's committed `ServerSeed`. ⚠️ **To every other player the bet is indistinguishable from a
human's.**

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
  self-excluded, and the pool balance covers `RequiredAmount`. ⚠️ **A player who already has a bet in
  the round is excluded** — Orbit gets no exemption from the one-bet-per-round rules.
- ⚠️ **A `ComingSoon` game type is never Orbit-eligible** (see [`games.md`](games.md) §19). Three reads
  enforce it: `GetOrbitHandler` drops it from `eligibleGameTypes` (that is what shapes the UI),
  `UpdateOrbitSettingsHandler` refuses a PATCH naming it with the existing vague `OrbitRequestInvalid`
  (an old client), and `OrbitBetPlacementService.IsOrbitEligible` refuses to place — **that third one
  is the one that protects money**, because a stale enrolment betting onto a type that refuses bets
  would land in a refund loop.
- ⚠️ **`Bet.FundedByOrbit` exists solely because Orbit's custody account signs on behalf of a
  DIFFERENT player's wallet**, which `SolanaService.ValidateBetPayment` would otherwise reject. It must
  never be set on any other bet — see the house seed bet in [`games.md`](games.md) §14, where setting
  it would discard a validation that genuinely passes **and** route a win into the custody account.
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
it is released when the word is changed, cleared, or Orbit is disabled. **Preset types are never
claimed.** A blocked human sees `ChoiceAlreadyTaken` and the same refund path, **byte-identical** to a
same-round collision — deliberately, so Orbit stays invisible. See [`games.md`](games.md) §11.

## 3. Control surface — one flag

Orbit once had **four** points of control (`Enabled`, where OFF force-cashed-out everything and started
an hours-long re-enable cool-off; `Stopped`; the re-enable lock; and the manual cash-out gap), and live
testing read the flow as *"sign in → enable? → set up → then hit play (to unpause??)"*.

⚠️ **They collapsed to one.** `Enabled` is the only flag and behaves exactly like play/pause. Cash-outs
are their own thing. **The only cool-off left in Orbit is the rolling manual cash-out gap.**

Also available: **partial cash-out**, a **cash-out floor**, a **stop-loss trigger**, and a per-player
**bet log**.

## 4. Notifications

Orbit bets and settles with nobody watching, so until the third card nothing told the player — a win
only showed up if they opened the modal, and money leaving custody, including forced cash-outs they
never asked for, was **silent**.

- A **private per-player realtime channel** on the existing `ActiveGamesHub` announcing every Orbit
  win, wherever the player is in the app.
- **Telegram messages when money leaves custody** — every cash-out, linked with **no poller and no
  webhook**. Per-event toggles.
- An honest **"paused" state** when Orbit stops betting for lack of funds.
- A one-time **invitation into Orbit** for a first-time winner.

## 5. The player-facing surface

Orbit shipped functionally complete and visually unfinished — one 916-line hand-rolled overlay that
bypassed the design system, plus eight scattered touchpoints on the game screen with nothing saying
what Orbit was doing right now.

It is now **two surfaces with one job each**: a compact **panel** for money moved mid-round, and an
**`/orbit` page** that is the order you set Orbit up in and, once set up, the dashboard that says it is
running. Plus a module in the game cabinet and a resolved mobile-deeplink sign-in.

### One eligible type ⇒ no dropdown

With Beast Mode published as coming soon, exactly **one** eligible type is left (Cosmic Sprint —
Space Jackpot is `AllowOrbit: false`, because an auto-bet on a 24h draw would lock a player's funds for
a day). A dropdown with one option is a control that cannot be used, so **`OrbitGameTab.vue` renders a
read-only line** naming the game and its stake, plus *"More modes coming to Orbit soon."*, and
**auto-commits it on mount** so the enrolment row is still created exactly as picking from the dropdown
did (deposits need it to exist). ⚠️ Guard the auto-commit on the wallet **and** on the value actually
differing, or every reload PATCHes. With **zero** eligible types the empty "Pick a game" select stays —
that is a real condition, not a third state worth inventing.

⚠️ **`OrbitModal.vue` keeps its `<select>`** and must not be collapsed the same way: it is a
*confirm-then-navigate* picker that only renders when nothing is set up yet, so one option is still a
real press — collapsing it would auto-navigate the player the moment the panel opens. It reads the same
`eligibleGameTypes`, so the gated type is already gone from it.

## Key code touchpoints

**Backend (`monkify-backend`)** — `SpaceStroker.Domain/Orbit/*` (entities `OrbitEnrolment`,
`OrbitBalance`, `OrbitTransaction`, `OrbitChoiceClaim`), the Orbit handlers under
`SpaceStroker.Infrastructure/Handlers/Orbit/`, `OrbitPlacementPlannerConsumer` (on `GameOpenedEvent`,
⚠️ **a separate receive endpoint from `HouseSeedBetConsumer`, so a slow house placement never delays
Orbit planning for the same round**), the cash-out job, and `SpaceStroker.Api/Controllers/OrbitController.cs`.

**Frontend (`monkify-front`)** — `src/pages/OrbitPage.vue`, the Orbit panel + cabinet module,
`src/hooks/useOrbit.ts`, `src/services/orbitService.ts`, all behind `orbitEnabled`.

# Domain — Orbit (custodial balance + auto-betting)

> **What this covers.** Orbit is the platform's **first custody of real player money**: a house-held
> balance a player funds with on-chain USDC deposits, out of which the house places **one real bet per
> round** on their behalf, crediting winnings back and cashing out at a threshold they set.
>
> ⚠️ **Orbit is gated by `VUE_APP_ORBIT_ENABLED` (`orbitEnabled`, fail-closed) and is DARK in
> production.**
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
- Deposits are **free-form whole numbers** within a published bound. ⚠️ The deposit-bound message
  (*"Deposits are whole numbers from N to M."*) is a **sanctioned exception** to the vague-messaging
  rule: it states a bound the backend already publishes, it reveals nothing about a session or another
  player, and it is refused **client-side before the transfer is signed** — a player told only "that
  could not be accepted" after their money left the wallet has no way to learn what is allowed.

## 2. The engine — who bets, and how

For every player who is **enrolled, enabled on a game type, and funded**, the house places one real
bet per round out of their Orbit balance: a house-signed on-chain transfer with a choice derived from
the round's committed `ServerSeed`. ⚠️ **To every other player the bet is indistinguishable from a
human's.**

- **Every eligible enrolled player bets every round — no cap.** Filling only the gap to
  `MinimumNumberOfPlayers` was rejected: it silently skips rounds a player enrolled to play.
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

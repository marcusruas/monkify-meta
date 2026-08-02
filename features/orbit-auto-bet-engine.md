# Feature — Orbit auto-bet engine (2/3)

> Status: **on the `release/orbit` train — NOT yet on `main`.** Authoritative spec:
> `../specs/orbit-auto-bet-engine.md` (merged via `feature/orbit-auto-bet-engine` into
> `release/orbit` on 2026-08-02).
>
> ⚠️ Orbit is three cards and is only coherent once all of them land, so `main` deliberately does not
> receive this until #123 (notifications) joins the train. Until then, the behaviour below exists on
> `release/orbit` only. Trello [#122](https://trello.com/c/h7MF4Oky), umbrella
> [#118](https://trello.com/c/yxwdNTrb). Builds on
> [`orbit-custodial-wallet.md`](orbit-custodial-wallet.md) (#121).

## What it does

Spends the balance #121 built. For every player who is **enrolled, enabled on a game type, and
funded**, the house places **one real bet per round** on that type out of their Orbit balance — a
house-signed on-chain transfer with a choice derived from the round's committed `ServerSeed` — then
credits winnings back into the balance and fires the "cash out at" threshold. To every other player
the bet is indistinguishable from a human's.

This is also where the `BetDebit` / `WinCredit` / `Refund` ledger types #121 defined but never wrote
are finally written.

## Core rules

**Who bets, and when**

- Every eligible enrolled player bets **every round — no cap**. Filling only the gap to
  `MinimumNumberOfPlayers` was rejected: it silently skips rounds a player enrolled to play.
- Eligible means: enrolment `Enabled`, `GameParametersId` matches the round's type, **not**
  self-excluded, and the pool balance covers `RequiredAmount`. A player who **already has a bet in
  the round** is excluded — Orbit gets no exemption from the one-bet-per-round rules, so dispatching
  them would produce a guaranteed refusal.
- Lottery types are excluded outright regardless of `AllowOrbit`: a 24h draw would lock an
  auto-bettor's funds for a day, and a start gate is meaningless for a scheduled draw.

**Viability — nothing moves into a round that cannot start**

- Placement happens **only** if existing bettors plus candidates can reach
  `MinimumNumberOfPlayers`. Otherwise **nothing is published at all**.
- The reason is that an under-subscribed **race** never self-refunds (`BetPlacedConsumer` handles
  only lotteries), so a lone Orbit stake would sit in a `WaitingBets` round until a human wandered in
  or an admin refunded it by hand.
- `Game.ExpectedOrbitBets = 0` is the "planning ran, nothing to wait for" marker. It is distinct from
  `null` ("never planned") on purpose — without the distinction, `BetPlacedConsumer` re-ran the
  planner on **every** subsequent bet, re-dispatching players and logging a refusal each time.

**Three triggers re-plan a round**

1. **A round opens** — `OrbitPlacementPlannerConsumer` on `GameOpenedEvent`.
2. **A human bet makes an unviable round viable** — `BetPlacedConsumer`, guarded on a `null` gate so
   it fires once rather than on every bet.
3. **A deposit is credited** — `OrbitDepositCreditedConsumer`, so money that arrives mid-round joins
   **that** round rather than the next one. Without it there was no path at all: trigger 1 stamps
   `ExpectedOrbitBets = 0` when it finds no funded players, and that stamp is exactly what stops
   trigger 2. Published only on a genuine credit — never on a refusal, never on a replay (the client
   resubmits its stash on every boot).

**The start gate**

- `Game.ExpectedOrbitBets` + `OrbitGateExpiresAt`, written **before** the fan-out. If a placement
  landed first, `BetPlacedConsumer` would see no gate and could start the round while the rest were
  still confirming on-chain.
- It arms **only when Orbit is load-bearing**. If humans already satisfy the minimum, Orbit still
  plays but **a paying human never waits on a bot**.
- The expected count is **landed Orbit bets + this run's candidates**, not just the candidates —
  `BetPlacedConsumer` compares it against every `FundedByOrbit` bet on the round, and a mid-round
  re-plan happens with bets already in.
- A separate expiry timestamp is required rather than `CreatedDate + constant`, because the
  human-makes-it-viable and deposit paths arm the gate long after the round was created, where
  `MinimumWaitPeriodForBets` is already negative.
- It is a **ceiling, never a trap**: a definitive failure atomically decrements and republishes
  `BetPlacedEvent`, so the round starts within milliseconds of the survivors landing instead of
  burning the 45s timeout. A Hangfire one-shot at expiry is the hard-crash backstop.
- Losing the placement **claim** is *not* a failure and must not decrement — another consumer is
  placing that player's bet, so decrementing would start the round one bet short.

**Placement — the order of operations is the design**

Every step after the transfer is money already gone, so the consumer is deliberately paranoid:

1. Re-check everything we control **before signing** — round still accepting, player exists, not
   self-excluded, no bet this round, house has SOL. The planner's snapshot can be seconds old, and
   betting for someone who has since asked to stop is the one failure with no undo.
2. **Claim + debit in one transaction, still before signing.** The `BetDebit` ledger row *is* the
   claim: `OrbitTransaction` carries `GameId` and the pool index is **unique** across it. Only
   `BetDebit` rows set `GameId` and Postgres treats NULLs as distinct, so nothing else collides. A
   redelivered message or a double fan-out loses on `23505` having moved no money.
3. Transfer, and **confirm** it.
4. Register the bet through the ordinary MediatR path, so exclusivity, the per-round limits, seed
   commitment and the `TransactionLog` all behave exactly as they do for a human.

- ⚠️ **Each Orbit stake carries a unique memo** (`orbit:<gameId>:<playerId>`). Without it, every Orbit
  stake in a round is the same amount between the same two accounts, so the transactions are
  byte-identical and share **one signature** — the round's second bet was rejected as a replay,
  leaving that player debited with no bet.
- ⚠️ **`ValidateBetPayment` is skipped for Orbit bets, and this is required, not an optimisation.**
  The validator asserts the transfer's sender equals the *player's own wallet*; an Orbit stake is sent
  by the Orbit account, so it would reject every Orbit bet. Two compensating controls are mandatory:
  confirm the transfer on-chain **before** calling MediatR, and keep the signature-reuse guard.
- ⚠️ **The `FundedByOrbit` marker lives on the MediatR `RegisterBetRequest`, never on
  `RegisterBetRequestBody`.** The body is bound from a public unauthenticated POST; a caller who could
  set it would skip payment validation and mint unfunded stakes into a real pot.
- A stake that leaves custody and is then rejected is made whole **here**, by an idempotent
  compensating credit. Not every rejection publishes a `RefundInvalidBetEvent` — a replayed-signature
  refusal deliberately does not, because a genuine replay must never be refunded twice — so relying on
  that path alone left the player short.

**Provable fairness**

- The `ServerSeed` now commits at **round creation**, for every round. It used to commit on the first
  bet, which creation-time placement does not satisfy. `EnsureServerSeedCommitted` stays as the
  idempotent backstop. No visible player change.
- The choice derives from the committed seed: `DeriveChoice(ServerSeed, playerId, attempt, params)`,
  and the client seed from `HMAC(ServerSeed, "orbit-seed|playerId")` with `Nonce = 0`. A player can
  recompute both halves of an Orbit bet offline once the seed is revealed.
- Free-text types have choice exclusivity, so a derived choice can collide; up to **10** attempts,
  then the player sits the round out. Bounded because the alternative — spending a transfer on a
  choice exclusivity will reject — leaves the stake in `NeedsRefunding` until the 12h sweep. Preset
  types share choices, so their first derivation always stands.
- ⚠️ Accepted: an Orbit-only round has no non-house entropy. Already true of the choices, and #118
  accepted the residual grinding risk.

**Settlement**

- An Orbit-funded win pays to the **Orbit account**, never the player's wallet, and appends a
  `WinCredit` net of the shipped 0.5% commission. The `TransactionLog` `Credit` row is still written —
  `CalculateRewardForBet` reads it for idempotency, so skipping it double-pays on retry.
- A refunded Orbit stake returns to the **Orbit account** with a `Refund` row. Paid to the player's
  wallet it would leave the pool short with no ledger row, and reconciliation would report that
  shortfall forever. `RefundInvalidBetEvent` carries the funding source, because pre-insert refunds
  publish the *personal* wallet with no `Bet` row to read a flag from.
- Crossing the "cash out at" threshold fires a `Threshold` cash-out, exempt from the daily limit.

**Deposits and the enable toggle** (extends #121)

- **Orbit must be ON before a deposit is accepted.** Money must not sit in a custodial balance nothing
  is allowed to play with — an unenrolled player would get a parked balance whose only exit is a
  cash-out they have to discover.
- That refusal, and the disable **cool-off** refusal, are the two places Orbit turns away money that
  has **already moved on-chain**, so they are the two that **name their reason** rather than using the
  vague message. Both are **deferrals**: the client keeps the deposit stashed and replays it once the
  door opens. Both sit **after** the idempotency check, so a replay of an already-credited deposit
  still succeeds.
- Turning Orbit **off** force-cashes-out the whole balance, so turning it back **on** waits out a
  cool-off — and deposits are refused for its duration too.
- The enable toggle **prompts for a game type** and saves the type and the flag in **one** patch. It
  used to be greyed out until a type was chosen, with the reason buried in a caption — a dead toggle
  the player had no way to read as "pick a game first".
- The auto-cash-out threshold can be **cleared**. An empty field sends an explicit clear flag; a null
  value reads as "leave it alone" on a PATCH, which is why it used to be impossible to turn back off.

**Frontend**

- Two **owner-only** surfaces, invisible to other players: a private `ORBIT` tag on the owner's own
  rows, and a "round covered" note in the console. Far cheaper than assumed — an Orbit bet carries the
  player's real wallet, so `iHaveBetThisRound` is already true and the panel is already greyed. It is
  a **flag + copy change**, not a new composable.
- Orbit state is **module-level** so the header and the modal always agree on one set of numbers, and
  it is **re-scoped on every wallet change**. Without that, switching wallets left the previous
  wallet's balance on screen under the new address and re-sent the old session token.
- A refused (4xx) session is **dropped**, so the header falls back to `SIGN IN` and one signature
  recovers. A stateless session outliving its `Players` row — a wiped DB locally, a rotated
  `SessionSecret` in production — otherwise produced a 400 on every connect, modal open and header
  mount, each one toasted.

## Known gaps (shipped knowingly)

- ⚠️ **Committed config carries a real DEVNET keypair**, including `TokenOwnerPrivateKey` — a
  deliberate, user-authorised exception to `constraints.md` §1 so the local stack works out of the
  box. Inherited from #121 and widened here. **Production must override it explicitly.**
- ⚠️ **An Orbit bet satisfies `SecondBetNeedsAnotherPlayer`**, so it unblocks a human's second bet
  that round. Exempting it would reveal Orbit.
- A race stuck in `WaitingBets` still has no stale-round timeout — a pre-existing gap with its own
  card, not closed here.
- The gate's expected count is corrected for landed bets, but `ReleaseGateSlot` still floors at zero
  rather than reconciling against the true outstanding set; a pathological interleaving could start a
  round one placement early. The placement consumer's "round no longer accepting bets" check makes
  that a skip, not a loss.
- Notifications, Telegram and the first-win upsell are **#123**.

## Key code touchpoints

**`monkify-backend`**

- Domain — `Domain/Orbit/Services/OrbitChoiceGenerator.cs` (pure, beside `ProvablyFair`);
  `Bet.FundedByOrbit`; `Game.ExpectedOrbitBets` + `Game.OrbitGateExpiresAt`;
  `OrbitTransaction.GameId`.
- Planner — `Infrastructure/Services/Orbit/OrbitBetPlacementService.cs` owns every round-level
  decision (candidates, viability, gate, fan-out) so all three triggers behave identically.
- Settlement — `Infrastructure/Services/Orbit/OrbitBetSettlementService.cs`.
- Consumers — `Infrastructure/Consumers/OrbitPlacementPlanning/`,
  `Infrastructure/Consumers/OrbitBetPlacement/`, `Infrastructure/Consumers/OrbitDepositCredited/`.
  Changed: `BetPlaced/BetPlacedConsumer.cs` (gate + re-plan), `RewardGame`, `RefundInvalidBet`.
- ⚠️ Bus contract — `Infrastructure/Contracts/Games/GameOpenedEvent.cs`, **ids only**. The spec called
  for publishing `GameCreatedEvent`; that record carries the whole `GameParameters` entity, whose
  navigation properties cycle, and MassTransit's serializer throws on it. It only *appears* to work
  for a type with a shallow graph — an empty preset list and no lottery.
- Handler — `Infrastructure/Handlers/Orbit/CreditOrbitDeposit/CreditOrbitDepositHandler.cs` (the
  enabled check and the re-plan publish); `Handlers/Games/RegisterBet/RegisterBetRequest.cs`
  (`FundedByOrbit`).
- Config — `OrbitSettings.OrbitBetGateTimeoutSeconds` (45). No new secret, no new endpoint ⇒ no new
  rate-limit rule.
- Migration `AddOrbitAutoBetEngine`.

**`monkify-front`**

- `src/hooks/useOrbit.ts` — `syncWallet` (wallet re-scoping), `isSelfDeposit`, `isDepositBlocked`,
  `refreshQuietly`.
- `src/components/orbit/OrbitModal.vue` — enable-first layout, the game prompt, deposit gating.
  ⚠️ The threshold field is `<input type="number">`, and Vue's `v-model` casts to a number for that
  input type **on its own** — `.number` is implied. Read it through `thresholdText()`.
- `src/components/GameHeader.vue`, `GameConsole.vue`, `GameTerminal.vue`, `GameHistoryPage.vue`,
  `src/hooks/useGame.ts` — the owner-only markers and copy.

**Tests** — `SpaceStroker.Tests/UnitTests/{Consumers,Services,Domain/Orbit,Handlers/Orbit}/`;
`SpaceStroker.Tests.Integration/OrbitAutoBetFlowTests.cs`.
⚠️ The three writes the design rests on — the placement **claim** (a unique-index violation), the
atomic conditional **debit**, and the **gate** — are all `ExecuteUpdateAsync` or a raw constraint
failure. The EF in-memory provider throws on the first and cannot produce the second, so they have
**no unit coverage by construction** and the integration suite is the only place they execute.

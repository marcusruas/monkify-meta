# Feature — House seed bet

> Status: **shipped.** Authoritative spec: `../specs/house-seed-bet.md`
> (merged via `feature/house-seed-bet` on 2026-08-07). Trello
> [#155](https://trello.com/c/34W17kio).
>
> Supersedes **`SpaceStroker.Seeder`** (`../specs/auto-bet-seeder.md`), the manual 6-hourly console
> app, which is **deleted**. That spec is kept as history only.

## What it does

On production, every newly created round opens with **one paid bet already on it**, from a dedicated
house wallet, on **all three** game types. A round therefore never reads as a dead game, and — because
the seeded bet counts as a distinct player — a **single real player is enough to start a round**
instead of two.

It replaces a console app the operator ran by hand, which polled every 6 hours, covered only Cosmic
Sprint and Beast Mode, and stopped seeding whenever that machine was off.

## Core rules

**The seeded bet is an ORDINARY bet — this is the defining property**

- No marker column, no DTO field, no flag, no migration, no payment-validation bypass, no payout
  redirect, and **no branch anywhere** keyed on it. It is indistinguishable from a bet any player
  could place, in the database and in every API/SignalR payload.
- That is possible because the house wallet is **both the transfer's sender and the bet's own
  `Wallet`**. `SolanaService.ValidateBetPayment` matches the sender on `bet.GetWalletAddress()` and
  the recipient on `token.TokenOwnerPublicKey`, so both lookups resolve and the shipped validator
  passes **untouched**.
- ⚠️ **`Bet.FundedByOrbit` is never set for a seeded bet, and must not be.** That flag exists solely
  because Orbit's *custody account* signs on behalf of a **different** player's wallet, which
  `ValidateBetPayment` would reject. Setting it here would discard a validation that genuinely passes
  **and** route a win into the player-custody account. See
  [`orbit-auto-bet-engine.md`](orbit-auto-bet-engine.md).
- Consequence, accepted deliberately: seeded bets are counted like any other bet in **analytics/DAU**,
  **`game-type-stats`** (including hottest choice) and **games-history**. There is no marker to filter
  on, by design.

**Production only**

- Gated by a hard `IsProduction()` check in `HouseSeedBetConsumer`. **There is no config enable-flag** —
  turning seeding off is a code change and a deploy. Blanking the configured keypair also stops it
  (the resolver fails closed), but that is a secret rotation, not a feature switch.
- The gate lives in the **consumer**, and every decision lives in the **service**, specifically so the
  Testcontainers harness — which pins every host to `Development` across a shared fixture — can drive
  the real placement path without flipping the environment for every other suite.

**When it fires**

- One consumer on `GameOpenedEvent`, which `GameService.CreateGame` already publishes for **all four**
  round-creation paths (startup, rollover, the under-subscribed lottery replacement, and a newly
  activated game type). Its **own** receive endpoint, so its placement delay can never hold up Orbit
  planning for the same round.
- The bet lands after a **randomised delay clamped strictly inside `MinimumWaitPeriodForBets`** (2s
  safety margin for the transfer and confirmation). Free of cost — the round honours its full betting
  window anyway — and it stops the same wallet appearing within seconds of every round, which is the
  pattern that would give it away.
- **It is placed whoever else turned up.** A round that already has human bets, or that could already
  start, is still seeded. The only "the round moved on" check is that it must still be in
  `GameAcceptingBets` after the delay — re-checked **before** signing, so a stake is never bought into
  a dead round.
- **Never twice.** Before signing, the service skips when a bet from the house player already exists
  on that round, so a redelivered `GameOpenedEvent` or a double fan-out cannot spend a second stake.

**Where the money goes**

- One `RequiredAmount` stake, house wallet → the treasury's token account, as an SPL transfer carrying
  a per-round memo `seed:{gameId:N}`. The memo is load-bearing: two rounds' stakes are otherwise
  identical in sender, receiver, amount and fee payer, and a Solana signature is deterministic over
  the message, so they could share one signature and the second would be refused as a replay.
- A win pays the **house wallet** through the ordinary `RewardGameConsumer` path, with the standard
  commission and the `Credit` `TransactionLog` row. No branch was added to `BetDomainService`. The
  wallet is therefore roughly self-sustaining rather than draining.
- Refunds are the existing paths: an under-subscribed lottery draw refunds every bet and immediately
  creates the successor round (which is then seeded once), and deactivating a game type refunds its
  waiting bets.
- A race nobody joins parks **one** house stake indefinitely. Bounded — an unstarted race never rolls
  over, so at most one per race type exists at a time, and it plays as soon as a human arrives.

**Choice selection, and why it is derived**

- The choice is **derived from the round's committed `ServerSeed`** via
  `OrbitChoiceGenerator.DeriveChoice`, and the bet's `Seed` via `DeriveSeed` with `Nonce = 0` — the
  same functions Orbit uses. `ServerSeed` commits at round creation, so the code already knows it at
  placement time; deriving means nothing is ever chosen *after* the commitment.
- Free-text types: a candidate must be free of **both** an existing bet on that round **and** an
  `OrbitChoiceClaim` — #150's cross-round reservation, which `RegisterBetHandler` refuses with
  `ChoiceAlreadyTaken`. Up to 10 attempts, then the round is sat out **before** any transfer. Preset
  types share choices, so their first derivation always stands.

**Two hard refusals**

- ⚠️ **The house wallet must not be the treasury (`TokenOwnerPublicKey`) or the Orbit account.** Either
  collision reports **not configured**, logs `Fatal`, and seeds nothing — while the host keeps
  running, because aborting `SpaceStroker.Consumers` would take down the game loop and every payout
  over a *seeding* misconfiguration. Treasury: a same-ATA transfer confirms as an on-chain **no-op**,
  so the delta is 0 and the bet is refused as underpaid *after* the money moved. Orbit: that is player
  custody money.
- ⚠️ **A game type with `MinimumNumberOfPlayers < 2` is never seeded** (logged at `Warning`). The
  typer's keystream is `HMAC(ServerSeed ‖ gameId ‖ orderedBetSeeds)`, so the house cannot predict the
  outcome only as long as at least one bet seed comes from a human browser. On a min-1 type the house
  bet could run a round alone, where every input is house-controlled. **Do not relax this without
  replacing the entropy argument.**

**Failure behaviour**

- Any failure degrades to exactly the old behaviour — an empty round. `GameService.CreateGame` never
  waits on the placement, and `BaseConsumer` swallows and logs, so nothing can escape into the game
  loop.
- Low SOL skips the round (nobody is waiting on a seeded bet); a **failed** SOL read does **not** —
  null is "unknown", not "empty".
- **Once a transfer signature exists it is never re-sent.** If registration is then rejected, the
  signature, wallet, game and amount are logged as `HOUSE SEED ORPHAN PAYMENT` at `Error` so the
  stranded stake can be reconciled by hand via `POST /admin/bets/refund`
  ([`refund-safeguards.md`](refund-safeguards.md)).
- A `SpaceStroker.Consumers` restart during the delay loses that placement and the round goes
  unseeded. Accepted — there is deliberately no durable scheduler.

**Operator setup (not automatic)**

- `GeneralSettings:Operator:Token:HouseSeed` needs `PublicKey`, `PrivateKey` and `DisplayName`.
  Committed config carries `MOCK_` placeholders, and a `MOCK_`/blank value in **either** key fails
  closed. Real values come from Secret Manager.
- The wallet must be funded with the game token **and** a little SOL, and must be neither the treasury
  nor the Orbit account.

## Key code touchpoints

**`monkify-backend`**

- Consumer — `Infrastructure/Consumers/HouseSeedBet/HouseSeedBetConsumer.cs` (the `IsProduction()`
  gate, and nothing else). Registered in `SpaceStroker.Consumers/Program.cs` with its own
  `ReceiveEndpoint`.
- Service — `Infrastructure/Services/HouseSeed/HouseSeedBetService.cs` (the whole flow),
  `HouseSeedAccountResolver.cs` (identity, ATA derivation, the treasury/Orbit refusals),
  `HouseSeedDelay.cs` (the pure clamped delay), plus both interfaces.
- Config — `Domain/Configs/Entities/HouseSeedSettings.cs`, hung off `TokenSettings.HouseSeed`;
  `SpaceStroker.Common/spacestrokersettings.json` (MOCK values).
- Reused unchanged — `Domain/Orbit/Services/OrbitChoiceGenerator.cs`,
  `ISolanaService.TransferTokensWithMemo` / `GetSolBalance`, `Handlers/Games/RegisterBet/*`,
  `Infrastructure/Contracts/Games/GameOpenedEvent.cs`.
- **Not touched** (pinned by tests): `BetDomainService`, `RewardGameConsumer`, `BetPlacedConsumer`,
  `RefundInvalidBetConsumer`, `SolanaService.ValidateBetPayment`, `GameService.CreateGame`,
  `RegisterBetRequestBody`. **No entity, EF mapping or migration change.**

**`monkify-front`** — **none.** Zero files. The seeded bet reaches players through paths that already
exist and are unaware of it: the `{gameId}/Bets` feed, `> POT:`, the STATS and FAIR tabs, and Games
History. It renders exactly like any other player's paid bet, by design.

**Tests** — `SpaceStroker.Tests/UnitTests/Services/{HouseSeedAccountResolverTests,
HouseSeedDelayTests, HouseSeedBetServiceTests}.cs`,
`SpaceStroker.Tests/UnitTests/Consumers/HouseSeedBetConsumerTests.cs` (48 unit tests);
`SpaceStroker.Tests.Integration/HouseSeedBetFlowTests.cs` (6) with the house-seed configuration in
`Harness/IntegrationTestFixture.cs`.

## Known gaps (shipped knowingly)

- ⚠️ **No kill switch without a deploy** — a deliberate product decision (the config flag was
  rejected). Blanking the secret is the only runtime option.
- ⚠️ **`PlaceOrbitBetConsumer.DeriveAvailableChoice` does not check `OrbitChoiceClaims`** — a
  **pre-existing Orbit defect** found while building this and deliberately left alone. An Orbit bet can
  still derive a word another player has reserved cross-round, pay for it, and be refunded. The house
  seed path *does* check it. Worth its own card.
  > This also corrects Trello #153's note that `OrbitChoiceClaims` is dead schema — #150 wired it into
  > `RegisterBetHandler` (`RegisterBetHandler.cs`, the free-text exclusivity block).
- The house `Player` row is resolved/created by the service **before** the choice is derived, because
  both derivations key on a `Guid playerId` while `RegisterBetHandler.UpsertPlayerAsync` only creates
  the row *during* registration. Not a defect — but it is the ordering an implementer would otherwise
  get wrong.

# SpaceStroker — Monkey Duel: the treasury is the house — Implementation Spec

> **Status:** approved design (owner, 2026-09-25); **implemented** on `feature/duel-treasury-house` in
> `monkify-backend`, `monkify-front`, `monkify-meta` — **not yet shipped**. **Nothing ships to production until this lands**
> (owner: "Don't ship anything for now") — `main` already carries Monkey Duel + `duel-lobby-fixes`, never
> deployed.
>
> Builds on [`monkey-duel.md`](monkey-duel.md) (#184). Where the two disagree about a **House** duel, this
> spec wins; everything about **OpenRoom** (player-vs-player) duels is unchanged.

## 1. Summary

Today a House duel's counterparty is a **real bet**: after the creator's bet lands, `DuelHouseBetService`
(Consumers) signs a transfer of the same stake from the **HouseSeed wallet** to the treasury and registers
it as a second bet. That wallet held 0.448 SOL in prod — the owner wants the **treasury** to be the house,
**the way Hyper Roll works**: only the player pays, the treasury pays a winner, a loss simply stays in the
treasury.

After this change a House duel has **no house bet row**:

- The house word is drawn from the committed seed (unchanged `DuelHouseWordGenerator`) **when the creator's
  bet registers**, and stored on the round in a new `Game.HouseWord` column.
- The round is full as soon as that bet lands; its countdown starts at once. No consumer, no transfer.
- The typer races the creator's word against `Game.HouseWord`.
- Player's word first → the treasury pays **1.98 × stake** (the same figure as today: the two-stake pot less
  the 2% cut on the profit). House word first → nothing is paid.
- The treasury check is **shared with Hyper Roll**: one exposure budget, one advisory lock, one
  `HouseEdgeEnabled` maintenance switch.

Cosmic Sprint's house seed bet keeps its HouseSeed wallet. OpenRoom duels are untouched.

## 2. Resolved product decisions (owner, 2026-09-25)

| # | Decision |
|---|---|
| D1 | The treasury funds House duels, "same idea as Hyper Roll". The HouseSeed wallet is no longer used by duels. |
| D2 | **No house bet row** (not a no-transfer "virtual" bet). The house word lives on the round. |
| D3 | A winning player is paid **1.98 × stake** (pot 2S less 2% of the S profit), from the treasury. |
| D4 | **Shared exposure** with Hyper Roll: one budget, so neither can promise the same SOL twice. |
| D5 | **Same maintenance switch**: `GeneralSettings:Games:HouseEdgeEnabled = false` closes the VS HOUSE tab too. |
| D6 | Where the word is stored: a `Game.HouseWord` column (option A). Rejected: reusing `GameBoardChoices` (B — leaks duel cases into every Hyper Roll board path) and re-deriving everywhere (C — three copies of the draw that must never drift). |

## 3. Constraints (MANDATORY)

- `constraints.md` applies. Money moves only in Consumers; the treasury signs every payout and refund.
- **Provably fair stays reproducible offline.** The house word is still a pure function of
  `(ServerSeed, gameId, attempt, DuelPoolSnapshot)` skipping the creator's word; `verify.html` and
  `src/utils/provablyFair.ts` keep deriving it. The typer's keystream prefix becomes `gameId + creator seed`
  only — that falls out of there being one bet, and the verifier already builds the prefix from the bets.
- **Fail closed.** No treasury balance read ⇒ not coverable ⇒ the bet is refused and refunded (Hyper Roll's rule).
- Every vague refusal stays the existing `HouseEdgeUnderMaintenance` sentence (`constraints.md` §6).
- Backend integration tests are mandatory (`constraints.md` → "Integration tests"): the happy paths below.

## 4. Data model

### 4.1 `Game.HouseWord` (`SpaceStroker.Domain/Games/Entities/Game.cs`)

`string? HouseWord` — `varchar(20)`, nullable. Set once, on a **House** round only, in the same transaction
that binds the creator's bet. Null on every other round.

Migration `DuelHouseWordOnRound` — additive column. No backfill: no House duel has ever run in production;
local rounds from before this change keep their old shape and are never re-run.

### 4.2 Round state

- A House round is **full** when `HouseWord != null` (`ActiveGameDto.IsFull`). `ClaimedByPlayerId` stays null
  on House rounds (it remains the PvP claim).
- A House round races / starts **only once `HouseWord` is set**. The typer (races the word, sizes the pool),
  the padded pot (`BetDomainService.SetPotAmount`) and `BetPlacedConsumer`'s start gate all read the one
  computed, **unmapped** property `Game.HasDrawnHouseWord` (`IsDuel && DuelKind == House && HouseWord` set),
  so start, race and pot can never disagree.
- A **second bet** on a House round (a direct API call — nothing legitimate joins one) is refused with the
  existing `ChoiceAlreadyTaken` and refunded.
- `CancelDuelRoom` is only reachable for OpenRoom rooms (a House round starts on the creator's bet), unchanged.

## 5. Backend behaviour

### 5.1 Opening a House room — `POST /games/{type}/duels {kind: House}`

Refused with the existing vague `GameNotValidForBets` when `HouseEdgeEnabled` is false, or when the **treasury**
cannot cover the type's minimum stake (the shared check, §5.3). Replaces the old HouseSeed coverage call.
Availability (`CheckBetAvailability`) answers `HouseEdgeUnderMaintenance` for the VS HOUSE tab when the switch
is off.

### 5.2 The creator's bet (`RegisterBetHandler.RegisterDuelBet`, House round)

Runs under the **duel lock** (`IDuelLockService`, §5.6), like every duel bet. A **paid** bet on a House round
(switch on) also takes the **shared treasury lock** (`HouseEdgeCoverageService.AcquireLockAsync`) **right after
the duel lock and before the bind**; it joins the duel lock's open transaction.

- **Lock order is always duel → treasury.** Hyper Roll (and `HouseEdgeBoardCleanupJob`) only ever take the
  treasury lock, never the duel lock, so the order cannot deadlock.
- **Treasury lock before the bind**, because the bind is an `UPDATE` that holds the `Games` row lock until the
  commit, and `HouseEdgeBoardCleanupJob` holds the treasury lock while it deletes unbound shells: binding first
  and then waiting on the treasury lock is a lock cycle Postgres breaks by killing a valid bet (refunded). The
  round's kind comes from the pre-lock read — `DuelKind` never changes after open.
- **One commit**, through the duel lock: bind + house word + bet land together or not at all.
- A paid House bet may hold the duel lock for up to **5 s** while it waits on the treasury lock.

Steps:

1. Reuse guard, bind the round (`OwnerPlayerId`, `Stake`) — unchanged.
2. `HouseEdgeEnabled` false → refused with the maintenance sentence (paid: refunded). The switch applies to
   **free** bets too (reject-only).
3. Paid bet: the shared treasury check (§5.3) for this round's worst case. Not coverable or no decision → refund
   with the maintenance sentence.
4. Draw the house word — `DuelHouseWordGenerator.DrawAvailableWord`: `DeriveWord` attempts
   `0..MaxWordAttempts-1` (`MaxWordAttempts = 10`), skipping the creator's word. No drawable word (or the
   conditional `TrySetDuelHouseWord` writes 0 rows) → refused with `GameNotValidForBets` and refunded. The bind
   rolls back, so the round stays an **unbound shell** for `HouseEdgeBoardCleanupJob` (it is not moved to
   `NotEnoughPlayersToStart`).
5. Write `Game.HouseWord`, insert the bet, commit, then publish `BetPlacedEvent` (and notify).

A free House duel (dev) skips step 3's money check (and the treasury lock) and is never counted as exposure.

### 5.3 Shared treasury exposure (`HouseEdgeCoverage` / `HouseEdgeCoverageService`)

`ExposureRounds` also selects **House duel rounds** that are unsettled (the existing `UnsettledStatuses`) and
**hold an unsettled paid bet (or are the incoming bet's own round)** — exposure is built from bet rows. A House
duel's worst case is its payout: `Stake × (2 − rate)` with `rate = CommissionPercentage / 100` (1.98 × Stake at
2%), plus one fee reserve. The incoming round has no bet row yet (the bet is inserted after the decision), so it
is priced **once, from the new bet** — no exclusion parameter. `coverable ⇔ balance − feeReserve ≥ total
exposure`, unchanged.

Availability's `maxStake` for the VS HOUSE tab is
`min(MaximumStake, (balance − feeReserve − exposure) / (2 − rate))`, truncated to 9 dp
(`DuelHouseRules.MaxStakeFromHeadroom`), and `0` when the treasury balance is unreadable.

### 5.4 Start, run, reward

- `BetPlacedConsumer`: a House duel needs **one** player (as `IsHouseEdge` does), and keeps
  `DuelTimeUntilGameStarts`.
- `SpaceStrokerTyper`: for a House duel, `SetBets` adds `HouseWord` with count 0 (Hyper Roll's board-word
  pattern) and `SetCharactersOnTyper` adds its characters to the pool. A house win therefore has
  `NumberOfWinners = 0`.
- `RewardGameConsumer`: the House duel winner's reward is **`Stake × (2 − rate)`**, with **no dedicated
  reward function**: `BetDomainService.SetPotAmount` adds `Game.Stake` to the pot when `HasDrawnHouseWord`, and
  the shared commission formula on that 2S pot yields `Stake × (2 − rate)`. Paid from the treasury — to Orbit
  custody when `FundedByOrbit`. The treasury pre-check applies when `treasuryFunded` (a house-edge type **or** a
  House duel). A house win pays nobody. `netRewardPerWinner` is published as today.

### 5.5 Verification (`GetGameVerificationHandler`)

`HouseWord` = `Game.HouseWord` (no more "the bet matching `ClaimedByPlayerId`"). `DuelKind`,
`DuelPoolSnapshot`, `ServerSeed` unchanged.

### 5.6 Removed

`DuelHouseBetService`, `DuelHouseBetConsumer`, `DuelCreatorBetPlacedEvent` (+ its receive endpoint),
`DuelHouseCoverage`, `DuelHouseCoverageService` and their DI/tests. House duels stop reading HouseSeed config.

**Kept:** the duel advisory lock, extracted from the retired coverage service as
`IDuelLockService` / `DuelLockService` (key `"MONKDUEL"`) — every duel bind/claim/cancel still serialises on it.

## 6. Frontend behaviour (`monkify-front`)

- `GameInstance.houseWord` (new, from the round DTO). For a House round the rival word is `houseWord` and the
  rival label stays `THE HOUSE` (terminal header, VS splash, result lines); OpenRoom keeps reading the rival bet.
- Win/loss: `winnerChoice === myBet.choice` → won; otherwise lost — unchanged logic, the house word is simply
  not a bet any more.
- `provablyFair.ts` / `verify.html`: **no change needed** — the server's `orderedChoices` (built from
  `typer.Bets`) already include the house word, and the draw is unchanged.
- The VS HOUSE ceiling is whatever availability quotes (now treasury-based); no new constant.

## 7. Tests

**Unit:** the house word drawn and stored in the bet transaction (collision skip); a House round full after
one bet; shared exposure (Hyper Roll exposure refuses a duel and vice versa); `HouseEdgeEnabled` off refuses
the House tab, bet and open; reward = 1.98 × stake, house win pays nobody; typer includes `HouseWord`;
verification reads `Game.HouseWord`; availability `maxStake = headroom / (2 − rate)`.

**Integration (Testcontainers):** House duel, player wins → one treasury Credit of `1.98 × stake`, no
HouseSeed transfer, one bet row; House duel, house wins → no Credit; an Orbit-funded House win lands in Orbit;
the verification payload reproduces the house word and the race from a one-bet prefix; a Hyper Roll round's
exposure blocking a House duel (and the reverse).

## 8. Out of scope

OpenRoom duels, Cosmic Sprint's house seed, the lobby / Orbit work already on `main`, the prod deploy.

## 9. Known follow-ups

- **History / BiggestPot** show a House duel's pot as **1× stake** (the persisted bet sum) — display only; the
  payout is correct.
- **Pre-existing:** `HouseEdgeBoardCleanupJob` deletes shells by `Id`; a bind that commits between its read and
  its delete would lose the round (and, via the `Bets` FK cascade, the bet). Deserves its own card.

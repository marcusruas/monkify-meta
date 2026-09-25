# SpaceStroker — Monkey Duel: the treasury is the house — Implementation Spec

> **Status:** approved design (owner, 2026-09-25), not yet implemented. Branch `feature/duel-treasury-house`
> in `monkify-backend`, `monkify-front`, `monkify-meta`. **Nothing ships to production until this lands**
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
- `CancelDuelRoom` is only reachable for OpenRoom rooms (a House round starts on the creator's bet), unchanged.

## 5. Backend behaviour

### 5.1 Opening a House room — `POST /games/{type}/duels {kind: House}`

Refused (existing vague message) when `HouseEdgeEnabled` is false, or when the **treasury** cannot cover the
type's minimum stake (the shared check, §5.3). Replaces today's HouseSeed coverage call.

### 5.2 The creator's bet (`RegisterBetHandler.RegisterDuelBet`, House round)

Runs under the **shared treasury lock** (`HouseEdgeCoverageService.AcquireLockAsync`) instead of the duel lock
(a House round has no join race):

1. Reuse guard, bind the round (`OwnerPlayerId`, `Stake`) — unchanged.
2. `HouseEdgeEnabled` false → refund with the maintenance sentence.
3. Paid bet: the shared treasury check (§5.3) for this round's worst case. Not coverable or no decision → refund
   with the maintenance sentence.
4. Draw the house word — `DuelHouseWordGenerator.DeriveWord` attempts 0..9, skipping the creator's word (the
   existing `DeriveAvailableWord` logic moves here). None free → refund, round `NotEnoughPlayersToStart`.
5. Write `Game.HouseWord`, insert the bet, commit, then publish `BetPlacedEvent` (and notify).

A free House duel (dev) skips step 3's money check and is never counted as exposure.

### 5.3 Shared treasury exposure (`HouseEdgeCoverage` / `HouseEdgeCoverageService`)

`ExposureRounds` also selects **House duel rounds** that are unsettled (the existing `UnsettledStatuses`),
bound (`Stake` set) and paid. A House duel's worst case is its payout: `Stake × (2 − rate)` with
`rate = CommissionPercentage / 100` (1.98 × Stake at 2%), plus one fee reserve. The incoming bet's own round is
priced the same way. `coverable ⇔ balance − feeReserve ≥ total exposure`, unchanged.

Availability's `maxStake` for the VS HOUSE tab becomes `min(MaximumStake, headroom / (2 − rate))`.

### 5.4 Start, run, reward

- `BetPlacedConsumer`: a House duel needs **one** player (as `IsHouseEdge` does), and keeps
  `DuelTimeUntilGameStarts`.
- `SpaceStrokerTyper`: for a House duel, `SetBets` adds `HouseWord` with count 0 (Hyper Roll's board-word
  pattern) and `SetCharactersOnTyper` adds its characters to the pool. A house win therefore has
  `NumberOfWinners = 0`.
- `RewardGameConsumer`: the House duel winner's reward is **`Stake × (2 − rate)`** (a dedicated
  `CalculateDuelHouseReward`, like `CalculateHouseEdgeReward`), paid from the treasury — to Orbit custody when
  `FundedByOrbit`. The treasury pre-check (today `IsHouseEdge` only) covers House duels. A house win pays
  nobody. `netRewardPerWinner` is published as today.

### 5.5 Verification (`GetGameVerificationHandler`)

`HouseWord` = `Game.HouseWord` (no more "the bet matching `ClaimedByPlayerId`"). `DuelKind`,
`DuelPoolSnapshot`, `ServerSeed` unchanged.

### 5.6 Removed

`DuelHouseBetService`, `DuelHouseBetConsumer`, `DuelCreatorBetPlacedEvent` (+ its receive endpoint),
`DuelHouseCoverage`, `DuelHouseCoverageService` and their DI/tests. House duels stop reading HouseSeed config.

## 6. Frontend behaviour (`monkify-front`)

- `GameInstance.houseWord` (new, from the round DTO). For a House round the rival word is `houseWord` and the
  rival label stays `THE HOUSE` (terminal header, VS splash, result lines); OpenRoom keeps reading the rival bet.
- Win/loss: `winnerChoice === myBet.choice` → won; otherwise lost — unchanged logic, the house word is simply
  not a bet any more.
- `provablyFair.ts` / `verify.html`: no change to the draw; confirm the recompute passes with a one-bet prefix.
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

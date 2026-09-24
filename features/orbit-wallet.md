# Feature — Orbit wallet (manual bets from the Orbit balance)

> Status: **shipped.** Authoritative spec: [`../specs/orbit-wallet.md`](../specs/orbit-wallet.md)
> (merged via `feature/orbit-wallet` on 2026-09-24). Trello #194
> ([AXAtCEcN](https://trello.com/c/AXAtCEcN)). Level **`Feature`**.
> Orbit's custody, auto-bet engine, stop rules and notifications are in [`orbit.md`](orbit.md). This doc
> covers the second spender of the Orbit balance: the player, by hand.

## What it does

Orbit is the player's **in-game wallet**. A player deposits into Orbit once, then places **manual** bets on
**any paid game** (Cosmic Sprint, Space Jackpot, Hyper Roll) straight from that balance, with no wallet
transaction to sign per bet. The SPA picks the source by itself: Orbit pays when its balance covers the
stake, otherwise the connected wallet pays. Wins and refunds of an Orbit-paid bet land back in the Orbit
balance. Wallet-signed bets and Orbit **auto-bet** keep working, but a game type Orbit is auto-playing
cannot also be played by hand.

| | Wallet bet | Orbit **auto** bet | Orbit **manual** bet |
| --- | --- | --- | --- |
| Who decides | Player | Engine, every round | Player |
| Game types | All paid | `AllowOrbit` only (Cosmic Sprint) | **All paid** |
| Authorisation | Wallet-signed transfer | Enrolment `Enabled` | `X-Orbit-Session` only |
| Transfer | Player → treasury (SPA-built) | Orbit account → treasury, house-signed | Orbit account → treasury, house-signed |
| Choice / seed | Player / client seed | Enrolment word or derived / `DeriveSeed` | Player / client seed |
| Win / refund lands in | Player wallet | Orbit balance | Orbit balance |
| Threshold / floor / stop-loss | n/a | Evaluated | **Never evaluated** |
| "Orbit won" private toast | n/a | Fires | Does not fire (normal win toast) |
| Lottery 100,000 $STROKE bonus | Yes | No | **Yes**, to the connected wallet |
| `Bet.FundedByOrbit` / `Bet.OrbitAutoPlaced` | false / false | true / **true** | true / false |

## Core rules

### Who can bet, where, and how it is authorised

- **Every paid game type accepts manual Orbit-paid bets.** `AllowOrbit` still means *auto-bet allowed* and
  is **not** read on the manual path. Inactive, `ComingSoon` and not-accepting-bets rounds refuse with the
  vague `GameNotValidForBets`. Hyper Roll's maintenance switch (`Games:HouseEdgeEnabled = false`) refuses
  with `HouseEdgeUnderMaintenance` before any money moves. Free bets never go through Orbit.
- **The Orbit session alone authorises a bet** (`X-Orbit-Session`, the 7-day stateless HMAC). There is no
  per-bet `signMessage`. This amends Orbit's "money never moves on a session alone" rule **for bets only**:
  cash-outs still need their `orbit-cashout` wallet signature. Accepted risk: a stolen session can gamble
  the balance but never withdraw it.
- **Self-exclusion is re-checked server-side on every manual bet**, in the API and again in Consumers.
- **Manual Orbit bets follow the wallet-bet rules exactly**: stake rules (fixed `RequiredAmount`, or Hyper
  Roll's `RequiredAmount ≤ amount ≤ MaximumStake` at 9 dp), choice validation, choice exclusivity, the
  cross-round `OrbitChoiceClaim` reservations, and the per-round limits (`OneBetPerPresetRound`,
  `SecondBetNeedsAnotherPlayer`). A second bet on the same choice by the same player is refused
  `DuplicateBetRefundedError`.
- **Auto-play block.** While a player's enrolment is `Enabled` on a game type, **every manual bet** by that
  player on that type is refused with *"Orbit is auto-playing this game — pause it to bet manually."*
  (`OrbitAutoPlayActive`). This covers wallet bets and manual Orbit bets. A **paused** enrolment does not
  block. The engine's own bets (`OrbitAutoPlaced`) are exempt. Enforced in `PlaceOrbitWagerHandler`,
  `PlaceManualOrbitBetConsumer` and `RegisterBetHandler` (shared `OrbitAutoPlay.IsActive`). A paid wallet bet
  refused here has already moved money and been verified, so it is refunded. A free bet is rejected with no
  refund.
- **Two sanctioned clear messages** (`constraints.md` §6), both about the caller's own state:
  *"Not enough Orbit balance for that bet."* (`OrbitInsufficientBalance`) and the auto-play line above.
  Every other refusal reuses existing vague copy.

### Where the money goes after the bet

- **Wins and refunds return to the Orbit balance.** `RewardGameConsumer` routes every `FundedByOrbit` winner
  to `CreditWin` (treasury → Orbit account + a `WinCredit` row). `RefundInvalidBetConsumer` routes every
  `FundedByOrbit` refund to `RefundToOrbit`. Cash-out is the only way from Orbit to the wallet.
- **Hyper Roll:** a manual Orbit win pays `stake × multiplier` of the board's mode (AVERAGE / SUPER /
  HYPER) into custody. `AddHouseEdgePayout`'s wallet is display-only; the transfer destination is decided
  by the `FundedByOrbit` branch.
- **Stop rules are auto-bet only.** Threshold, floor and stop-loss run only for `OrbitAutoPlaced` bets:
  `OrbitBetSettlementService.CreditWin` evaluates the threshold and publishes the "Orbit won" notification
  only for an auto bet, and `RewardGameConsumer.EvaluateOrbitFloors` looks only at players with an auto bet
  in the round. A manual win or loss never triggers an automatic cash-out.
- **A manual Orbit-paid Space Jackpot winner gets the 100,000 $STROKE bonus** at their own wallet
  (`bet.GetWalletAddress()`; $STROKE is never custodied). Only auto-placed bets are excluded.

### `OrbitAutoPlaced` vs `FundedByOrbit`

- `FundedByOrbit` means **the money is in custody**: skip the on-chain half of payment validation (the house
  made the transfer; the reuse guard still runs) and route the win or refund to the Orbit account.
- `OrbitAutoPlaced` means **the engine placed it**. Every "auto" reader uses it: the start gate
  (`BetPlacedConsumer`), the planner's landed-bet count (`OrbitBetPlacementService`), the house-seed
  viability count (`HouseSeedBetService.CountOrbitPlayers`), stop rules, the win toast, the $STROKE
  exclusion, `BetDto.ViaOrbit` and the round-history "via Orbit" chip (`GameHistoryService.MyBetViaOrbit`).
  A manual Orbit bet renders as an ordinary own bet (`YOU`, not `YOU · ORBIT`) and never satisfies a start
  gate slot armed for the engine.
- Both flags live only on the internal `RegisterBetRequest` (`init`-only, never bound from the public body).

## Money invariants

- **Ledger invariant:** `OrbitBalance.Amount == SUM(OrbitTransaction.Amount)` per pool and player, over all
  rows, no status filter. Corrections are appended rows, never edits or deletes.
- **All checks run before the debit.** A refusal before the claim moves no money and writes no ledger row.
- **The claim is the `BetDebit` row.** Claim and `TryDebit` are one DB transaction, written before any
  transfer is signed. A manual claim carries `PlacementRequestId` (the client idempotency key) and `GameId`,
  and is unique per request (`IX_OrbitTransactions_PlacementRequestId_BetDebit`, filtered `Type = 4`). The
  auto claim keeps "one per player per round" through the composite unique index, now filtered to
  `PlacementRequestId IS NULL`. A duplicate request loses on `23505` having moved nothing.
- **Claim state.** New claims are written `ClaimState = Open`. Every decision on an open claim is a
  **conditional UPDATE** in `OrbitStakeClaimStore` (stamp, link, settle, return), each guarded on `Open`, no
  `BetId` and the signature state it was decided on. Legacy claims (null state) are never touched.
- **Stamp before send.** `ISolanaService.TransferStakeWithMemo` builds and signs locally, and a `beforeSend`
  callback writes `Signature` and `LastValidBlockHeight` onto the claim before anything is sent. An open
  claim without a signature provably moved no money. Memo: `orbit-manual:{requestId:N}` (manual) or
  `orbit:{gameId:N}:{playerId:N}` (auto). The memo keeps two identical custody → treasury transfers from
  sharing a signature.
- **Tri-state transfer outcome:** `NotSent` → case A. `Confirmed(signature)` → register the bet.
  `Unknown(signature, lastValidBlockHeight)` → no credit, no bet, claim left `Open` for the reconciler
  (auto releases its start-gate slot; a manual wager stays `Pending`).
- **Exactly-once make-whole:**
  - **Case A (the transfer never left custody):** a ledger-only `Refund` row, exactly once. The manual one
    carries the `PlacementRequestId` (`IX_OrbitTransactions_PlacementRequestId_Refund`, filtered `Type = 6`),
    and the credit settles the claim inside the same transaction.
  - **Case B (the stake landed in the treasury, registration refused):** never a ledger-only credit. The
    return is **taken before it is published**: `TryClaimReturn` settles the claim and writes a `Requested`
    `PaymentRefundClaim` (`FundedByOrbit`) in one transaction, and only the winner publishes
    `RefundInvalidBetEvent(FundedByOrbit: true)`. `RefundToOrbit` moves the lamports back on-chain and
    credits the ledger. The handler's refusal path, the placer's case B and the reconciler share this call,
    so a stake's return is taken at most once between them, and never after a ledger credit.
- **Claim-linked bets.** The placer passes its claim id on `RegisterBetRequest.OrbitClaimId`, and
  `RegisterBetHandler` links the claim **inside the bet insert's transaction** (`TryLinkBet`: `Open`, no
  `BetId`, signature = the bet's). Pari-mutuel bets open their own transaction; house-edge bets join the
  coverage lock's. If the link matches nothing, the insert rolls back and the bet is refused
  `FailedToRegisterBet` with **no refund event**: whoever settled the claim owns the stake. A stake can
  never both back a riding bet and be credited or returned.
- **Unknown-outcome reconciler — `OrbitStakeReconciliationJob`** (Consumers, Hangfire
  `orbit-stake-reconciliation`, every minute, `[DisableConcurrentExecution]`, no automatic retry). It walks
  open claims of the configured pool with no `BetId`, older than 2 minutes, oldest first, in keyset pages of
  100, and stops taking claims after 45 s. Each run first republishes stale `Requested` returns (2 min to
  1 h old, on a settled claim). Per claim:
  1. Already compensated → settle, credit nothing.
  2. No signature → never sent → case A credit.
  3. A bet carries the signature → link it and settle; no money moves.
  4. A `PaymentRefundClaim` exists for the signature → the refund path owns it; settle.
  5. Otherwise ask the chain (primary RPC only, finalized height first, then `getSignatureStatuses` with
     history). **Landed** (and stamped ≥ 5 min ago) → take the return (case B). **Failed** → case A.
     **Expired** (not found and finalized height > `LastValidBlockHeight` + 150 blocks) → recorded in
     `FirstSeenExpiredAt` on the first sighting, credited (case A) only when a later run sees it expired
     again. **Undetermined** → retry next run.
  Manual claims then record `Refused` (`FailedToRegisterBet`) or `Placed`. A stored `Placed` is never
  downgraded.
- **A landed stake whose on-chain return fails ends `Settled` and needs a human**, like any failed case-B
  refund (the existing `RefundInvalidBetConsumer` refund-failure alerts apply).

<!-- MINORS: pending -->

### Alert log lines (set these up in Cloud Logging)

| Level | Message (prefix) | Meaning / action |
| --- | --- | --- |
| Error | `Orbit claim {ClaimId} ... is still unresolved after` | Open for over an hour, not credited. Check it against the chain. Logged every run. |
| Error | `Orbit claim {ClaimId} has a signature but no last valid block height` | Cannot be decided automatically. |
| Error | `Orbit claim {ClaimId} landed but has no player wallet or game` | A landed stake with nowhere to return it. |
| Error | `Orbit stake reconciliation failed for claim` | One claim threw; retried next run. |
| Warning | `Orbit bet ... NOT registered: its claim was already decided by the stake reconciler` | The stalled-placer race was caught; the reconciler credited or returned the stake once. No action. |
| Warning | `the in-doubt stake LANDED; requested its on-chain return` | Follow the refund to completion. |
| Warning | `Republished the on-chain return of Orbit payment` | A taken return's publish had failed. Repeats for up to an hour mean the refund consumer is not taking it. |
| Warning | `Orbit stake reconciliation stopped after ...: the run's time budget is spent` | Many claims or a slow RPC. Persistent means the RPC is degraded. |
| Warning | `changed signature since it was judged unsent; NOT crediting` | Race caught. No action. |

⚠️ Production logs are Warning and above only, so all of these reach Cloud Logging.

## API

Both routes are on `OrbitController`, require `X-Orbit-Session` (a missing or invalid session returns the
vague not-found), and are routed at `/orbit/wagers`, **not** `/orbit/bets`: `RateLimitSettingsTests` forbids
any rate-limit rule on a `/bets` path, and this path is house-signed and house-gassed, so it is a spend
vector and must be limited.

| Route | Rate limit | Behaviour |
| --- | --- | --- |
| `POST /orbit/wagers` | 30 / min | Body `{ requestId, gameId, choice, amount, seed, nonce }` (`requestId` is the client idempotency key; Hyper Roll's `gameId` is the solo round from `POST /games/{type}/boards`). Pre-checks in order: Orbit configured → player → **idempotency** (a stored result for `requestId` is returned, nothing is re-published) → self-exclusion → game → auto-play block → house-edge maintenance → accepting bets / stake / board freshness (a Hyper Roll board expiring in < 10 s is refused) / choice / per-round limits (`BetPreChecks`) → balance. Then it stores `Pending`, publishes `PlaceManualOrbitBetEvent` and returns **200** `{ requestId, status: 1, betId: null, message: null }`. A refusal is a **400** with the message. A failed publish stores `Refused` and returns 400 `FailedToRegisterBet`. It never signs and never debits. |
| `GET /orbit/wagers/{requestId}` | 120 / min | `{ requestId, status, betId?, message? }`, scoped to the session's player (another player's id reads as not found). |

`status` is **numeric**: `Pending = 1`, `Placed = 2`, `Refused = 3`, `NotFound = 4`. Results live in Redis
at `OrbitWager:{playerId}:{requestId}` for **15 minutes**. The ledger is authoritative for `Placed`: a
`BetDebit` with this `PlacementRequestId` and a `BetId` reads `Placed` whatever the store holds. A stored
`Pending` becomes `Refused` when the ledger shows a case-A refund. With the key gone, the ledger answers:
stamped debit → `Placed`, debit + refund → `Refused`, unstamped debit → `Pending`, no debit → `NotFound`.
Accepted gap: a case-B refusal reads `Pending` after the 15-minute TTL, because the on-chain refund does not
carry the request id (the Orbit history shows the refund).

`GET /orbit/history` bet rows carry `source` (`Auto = 1`, `Manual = 2`, numeric), taken from
`Bet.OrbitAutoPlaced`. Deposits (`POST /orbit/deposits`) no longer need an enrolment.

## Frontend behaviour

- **Automatic source, no toggle.** `useGame().nextBetSource` (`'orbit' | 'wallet' | null`) is read by both
  `runBet` and the header, so they cannot disagree. It is `'orbit'` when Orbit is usable (`orbitEnabled`, not
  embedded, not free-bet mode, wallet connected), the loaded Orbit state belongs to that wallet, it is not
  auto-playing this type, and the **server** balance covers the stake (fixed, or the Hyper Roll LCD stake).
  Otherwise it is `'wallet'`, and `null` off the game page or when no paid bet is possible. A short Orbit
  balance disables nothing. There is no opt-out. The orphaned `space-stroker.paySource` key from the
  removed PAY FROM switch is deleted once on load.
- **Lapsed session.** A durable per-device marker `space-stroker.orbit.used.<wallet>` is written with every
  Orbit session and never removed. When the wallet would pay, Orbit is usable, no Orbit state is loaded for
  that wallet and the marker is present, `runBet` asks for **one** Orbit sign-in (`ensureSession`), reloads,
  and re-reads the source. A declined or failed sign-in → the wallet pays, and that wallet is not prompted
  again for the rest of the page's life. A deeplink hop stops the bet. No marker → no prompt, the wallet
  pays.
- **Placing:** `placeFromOrbit` sends no transfer and opens no wallet. It shows `> PLACING BET…`, generates
  `requestId` with `utils/uuid.ts` (`randomUuid`, never throws in an insecure context), posts, and polls
  `GET /orbit/wagers/{id}` every 1 s for up to 60 s. `Placed` → *"Your bet was successfully placed"*, Orbit
  pill debit via `useOrbit().applyOrbitDebit`. `Refused` → the message. Timeout / `NotFound` → *"Still
  placing — check your Orbit history."*
- **Race fallback:** if Orbit refuses with exactly *"Not enough Orbit balance for that bet."* (POST 4xx or
  polled `Refused`), nothing is shown and the **same bet** is retried from the wallet in the same `runBet`
  (fee-headroom check, fresh seed/nonce, wallet signature prompt). Any other refusal is shown with no
  fallback.
- **Settle refresh:** after a manual bet is `Placed`, the SPA re-reads the Orbit balance when that round
  finishes (≈5 s after `Ended`, and on `RewardForWinnersCompleted` / `NotEnoughPlayersToStart` /
  `ErrorWhenProcessingRewards`), because the private win channel only fires for auto bets.
- **Which balance pays is always visible.** The header pill that will pay (violet `OrbitLauncher` or amber
  `WalletButtons`) gets the `.lit` state; the other is unchanged. The stake LCD digits in `GameConsole` are
  **violet when Orbit pays and green when the wallet pays** (the LCD's own green; null keeps the default).
- **Out-of-Orbit-funds signal** (`useOrbitOutOfFunds.ts`): when the Orbit balance **drops** and stops covering
  the current game's stake (same wallet and game type), the Orbit pill's digits turn red
  (`oklch(0.8 0.16 25)`) for exactly 5 s and a toast reads *"You've run out of Orbit funds. You can deposit
  more into Orbit or use your own wallet funds."* (~6 s; a click opens `/orbit?tab=wallet`). The race
  fallback fires the same trigger, deduplicated. It fires once per episode and re-arms only when Orbit
  covers the stake again. It never fires when the player merely raises the stake, on first load, on a wallet
  switch, off the game page, during auto-play, or when embedded or in free-bet mode.
- **The game console has no Orbit control or Orbit copy.** During the auto-play block the lever and inputs
  are disabled. Pulling the lever prints the auto-play line, and `runBet` refuses with it.
- **`/orbit` is wallet-first:** tabs **WALLET** (default; `?tab=money` is an alias) → **AUTO-BET** →
  **AUTO CASH-OUT** → recover → telegram. Depositing needs no game. The single-eligible-type auto-commit is
  gone: enrolling is an explicit *"Use Orbit auto-bet on Cosmic Sprint"* action. `OrbitModal` no longer dims
  ADD MONEY until a word is saved.
- **"Auto-bets only" copy** appears wherever threshold, floor or stop-loss appear (stop-rules tab,
  `OrbitModal` heading *"AUTO CASHOUT — AUTO-BETS ONLY"*, guide, home card, `/orbit` pitch).
- **Bet log** (`OrbitBetLog.vue`) has a **TYPE** column (`AUTO` / `MANUAL`). The private "Orbit won" toast
  fires for auto wins only; a manual win gets the ordinary win toast.

## Edge cases

- Duplicate POSTs with one `requestId`: one claim wins on the unique index. Before any re-check, the consumer
  looks up the claim: stamped → records `Placed`; unstamped → the other run owns it and it stops without
  touching the store.
- A Hyper Roll board expiring mid-placement is refused up front (10 s margin), because a refusal after the
  transfer costs a refund.
- Low house SOL (the pool-aware solvency guard) refuses a manual bet with the vague `GameNotValidForBets`
  before any claim.
- A stale SPA that sends a wallet bet on an auto-played type is refused server-side; the verified stake is
  refunded.
- A plain preflight rejection on the first send reads as `Unknown`, not `NotSent`: that player waits for
  expiry plus one reconciler run (a few minutes) instead of an immediate credit.

## Out of scope

Ledger-only bets (every bet is still an on-chain custody → treasury transfer), an inline deposit inside the
bet control, auto-bet on more types (`AllowOrbit` unchanged), session revocation or a shorter betting token,
and pushing wager outcomes over SignalR.

## Key code touchpoints

**Backend (`monkify-backend/src`)**

- `SpaceStroker.Api/Controllers/OrbitController.cs` — `POST /orbit/wagers`, `GET /orbit/wagers/{requestId}`.
- `SpaceStroker.Infrastructure/Handlers/Orbit/PlaceOrbitWager/`, `.../GetOrbitWager/` — API pre-checks and
  the result read.
- `SpaceStroker.Infrastructure/Services/Games/BetPreChecks.cs`, `BetRoundRules.cs` — read-only replicas of
  `RegisterBetHandler`'s rules, shared with it.
- `SpaceStroker.Infrastructure/Consumers/OrbitBetPlacement/PlaceManualOrbitBetConsumer.cs` (+ `…Event`) — its
  own receive endpoint in `SpaceStroker.Consumers/Program.cs`.
- `SpaceStroker.Infrastructure/Services/Orbit/OrbitStakePlacer.cs` — claim → transfer → register →
  make-whole, shared by auto and manual. `OrbitStakeClaimStore.cs` — the conditional claim updates.
  `OrbitWagerResultStore.cs` — Redis results. `OrbitAutoPlay.cs` — the block.
- `SpaceStroker.Infrastructure/Services/Solana/SolanaService.cs` — `TransferStakeWithMemo`,
  `CheckStakeTransfer`, `StakeExpiryMarginBlocks`; `StakeTransferOutcome.cs`.
- `SpaceStroker.Infrastructure/Jobs/OrbitStakeReconciliationJob.cs` — the reconciler.
- `SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs` — auto-play block,
  `OrbitAutoPlaced`, claim-linked insert (`RegisterOrbitClaimBet`), Orbit return on refusal.
- Entities: `Bet.OrbitAutoPlaced`; `OrbitTransaction.PlacementRequestId`, `ClaimState`,
  `LastValidBlockHeight`, `FirstSeenExpiredAt`; `OrbitBetSource`, `OrbitClaimState`. Mapping in
  `SpaceStrokerDbContext.OnModelCreating`.
- `SpaceStroker.Common/Resources/ErrorMessages.resx` — `OrbitInsufficientBalance`, `OrbitAutoPlayActive`;
  `spacestrokersettings.json` — the two `/orbit/wagers` rate-limit rules.

**Frontend (`monkify-front/src`)**

- `hooks/useGame.ts` — `nextBetSource`, `placeFromOrbit`, race fallback, lapsed-session re-mint, settle
  refresh, out-of-funds watcher.
- `hooks/useOrbitOutOfFunds.ts`, `hooks/useOrbit.ts` (`orbit.used` marker, deposit without a game),
  `services/orbitService.ts` (`placeWager`, `getWager`), `types/orbit.ts`, `utils/uuid.ts`.
- `components/game/GameConsole.vue` (auto-play lock, stake LCD colour), `components/GameHeader.vue`,
  `components/orbit/OrbitLauncher.vue`, `components/header/WalletButtons.vue`, `assets/styles/arcade.css`
  (`.btn-door.lit`).
- `pages/OrbitPage.vue`, `components/orbit/tabs/OrbitGameTab.vue`, `OrbitMoneyTab.vue`,
  `OrbitStopRulesTab.vue`, `components/orbit/OrbitModal.vue`, `OrbitTabBar.vue`, `OrbitBetLog.vue`,
  `components/guide/sections/GuideOrbit.vue`, `components/home/HomeOrbitCard.vue`.

## Tests

- **Integration (`SpaceStroker.Tests.Integration`):** `OrbitWalletFlowTests` (happy path: deposit with no
  enrolment → manual wager → win credited to Orbit with no auto cash-out; auto-play block for Orbit and
  wallet bets until paused; Hyper Roll manual Orbit win credits `stake × multiplier` to custody; pre-debit
  refusal moves no money; same `requestId` twice places one bet; transfer failure restores the balance
  once), `OrbitStakeReconciliationTests` (landed-unknown returned on-chain once, never-landed credited once,
  late-stamp and snapshot races, the stalled-placer interleave where the reconciler credits and the bet is
  then refused, `TryLinkBet` / `TrySettle` signature guards), `OrbitStakeCompensationTests`.
- **Unit (`SpaceStroker.Tests`):** `PlaceOrbitWagerTests`, `GetOrbitWagerTests`,
  `PlaceManualOrbitBetConsumerTests`, `PlaceOrbitBetConsumerTests`, `OrbitStakePlacerTests`,
  `OrbitStakeReconciliationJobTests`, `OrbitWagerResultStoreTests`, `BetPreChecksTests`,
  `RegisterBetHandlerTests`, `SolanaServiceTests`, `RateLimitSettingsTests`, `BetDtoTests`,
  `GameHistoryServiceTests`, `JackpotRewardServiceTests`, `OrbitBetSettlementServiceTests`,
  `RewardGameConsumerTests`, `HouseSeedBetServiceTests`, `CreditOrbitDepositTests`, `GetOrbitHistoryTests`.
- `monkify-front` has no test runner: `yarn lint` + `yarn build` + browser QA (spec §13).

## Ops notes

- **Migration `20260924160141_AddOrbitManualBets`** (applied by Consumers at startup): adds
  `GameBets.OrbitAutoPlaced` (default false) and **backfills it `true` for every `FundedByOrbit` bet** (all
  earlier Orbit bets were engine-placed); adds `OrbitTransactions.PlacementRequestId`, `ClaimState`,
  `LastValidBlockHeight`, `FirstSeenExpiredAt`; recreates the auto claim index filtered to
  `PlacementRequestId IS NULL`; adds the two filtered `PlacementRequestId` unique indexes and
  `IX_OrbitTransactions_OpenClaims`. Small tables, no long lock. ⚠️ **Forward-only once manual bets exist:**
  `Down()` recreates the unfiltered index and fails once a player has two Orbit rows in one round.
- **Deploy API and Consumers together** (the API publishes an event only the new Consumers consumes, and
  the backfill must land with the start-gate change). Deploy the **front after** the backend is live (check
  the running Cloud Run revision, not just `origin/main`).
- No new secret, env var or `VUE_APP_*` key; the feature rides `VUE_APP_ORBIT_ENABLED`.
- **Alerts to set up:** log-based alerts on the Error lines in the table above (unresolved after an hour,
  missing block height, landed with nowhere to return, per-claim failure) and on repeated
  `Republished the on-chain return` / `time budget is spent` warnings, plus the existing
  `RefundInvalidBetConsumer` refund-failure alerts for landed stakes whose return fails.

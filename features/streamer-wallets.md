# Feature — Streamer wallets (play-money PVE bets for streamers)

> Status: **shipped.** Authoritative spec: [`../specs/streamer-wallets.md`](../specs/streamer-wallets.md)
> (merged via `feature/streamer-wallets` on 2026-10-07). Trello
> [#201](https://trello.com/c/t0toZxny/201-streamer-wallets) (`t0toZxny`). Level **`Feature`**.
> ⚠️ The spec body predates several as-built departures — its banner lists them. **The code wins.**

## What it does

Lets a streamer demo the game live without the house giving away real SOL. A small set of **registered
streamer wallets** (config, not an admin UI) gets a fixed **3.7 SOL play-money balance**, shown in the
violet Orbit pill exactly like an Orbit balance, and spends it on the two player-vs-house games —
**Hyper Roll** and **Monkey Duel VS HOUSE**. The balance moves up and down on screen; **no on-chain
transfer ever happens**, the treasury is never read or touched, and play money can never be cashed out.
To everyone else a streamer round looks like any other round (history, public feed), but it is kept out of
every money aggregate.

Rode along (decision 12, **all players**): Monkey Duel and Hyper Roll now **remember the last paid stake**
per wallet and game type, like the duel word carry-over.

## Core rules

### Registration — config → DB reconciler (Consumers startup)

- Config: `GeneralSettings:Operator:Streamers` → `InitialBalance` (committed **3.7**), `Wallets[]`
  (`{ Wallet, Active = true }`), `TopUps[]` (`{ Id, Wallet, Amount }`). The committed
  `spacestrokersettings.json` carries **empty** lists; real wallets are **public keys** and go in
  `deploy/secrets.<env>.json` → **`appConfig`** (plain env vars, no Terraform binding). ⚠️ The binder
  **appends** to pre-initialised lists, so defaults live in config, never in an initializer.
- `StreamerWalletReconciler.Reconcile` runs once in Consumers' `ApplicationStarted` block, right after
  `CreateDefaultReferralPartners`; a failure is logged at Error and never stops the rest of startup.
  1. Normalise: trim, dedupe (first entry wins). **`MOCK_`-prefixed values are skipped silently**
     (the HouseWallets convention); blanks skipped; non-base58 / not 32–44 chars →
     `Log.Error("STREAMERS — skipping invalid wallet entry …")`.
  2. Every existing row is flipped to match config: listed `Active: true` → active; absent or
     `Active: false` → **deactivated, never deleted** (the FK `StreamerTransactions → StreamerWallets` is
     **`Restrict`**: a wallet with history can never be deleted).
  3. Missing wallets are inserted with `Balance = 0` (a racing instance's `23505` adopts its row).
  4. **Only ACTIVE wallets are seeded**, once: one transaction inserting a `Seed` row + `Balance +=
     InitialBalance`; the filtered unique index `IX_StreamerTransactions_Seed` makes a second seed lose on
     `23505`. A wallet listed `Active: false` gets no play money until it is first activated; a
     **reactivated** wallet that was seeded before is **not** re-seeded. `InitialBalance ≤ 0` → Error, no seed.
  5. Top-ups: each `Id` applies **exactly once** (unique `TopUpId`), only to a known wallet and only for
     `Amount > 0` (else Error + skipped). ⚠️ A reused id is silently skipped — ids must be unique forever.
  6. A **newly inserted** wallet that already holds a real Orbit balance > 0 → `Log.Warning("STREAMERS —
     wallet … has a real Orbit balance; it is hidden while streaming, cash it out first")`. The real balance
     is untouched, just invisible while the wallet streams.
  7. **Every run, for every active streamer**, any `Enabled` `OrbitEnrollment` is switched off
     (`Enabled = false`, `WaitingForHouseSince = null` — the #200 solo loop's waiting flag), with a Warning.
     Idempotent; it also self-heals an enrolment re-enabled by any other path.
- **No admin endpoint, no in-app registration.** Adding, removing or topping up a streamer is a config
  change + a **full** deploy (see Ops below).

### The play ledger

- `StreamerWallet` (`Wallet` unique, `Active`, `Balance` 18,9) and the append-only `StreamerTransaction`
  (`Type`, signed `Amount`, `TopUpId`, `PlacementRequestId`, `BetId`). Types: `Seed 1`, `TopUp 2`,
  `BetDebit 3` (negative), `WinCredit 4`, `Refund 5`.
- **Invariant:** `StreamerWallet.Balance == SUM(StreamerTransaction.Amount)` per wallet. Corrections are
  appended rows, never edits. Every balance move is one transaction: insert the row + conditional
  `ExecuteUpdate` (the context is NoTracking — load-mutate-save would persist nothing).
- Idempotency lives in the indexes: one `Seed` per wallet, one row per `TopUpId`, one `BetDebit` per
  `PlacementRequestId`, one row per `(BetId, Type)` — a redelivered settlement or refund credits nothing.
- `IStreamerLedger` (`Services/Streamers/StreamerLedger.cs`, scoped) is the only writer outside the
  reconciler: `FindActive`, `IsActiveStreamer`, `TryDebit` (joins the caller's transaction, conditional
  `Balance >= amount`, stamps the `BetId` on the debit row itself), `CreditWin`, `Refund`, `WonFor`.

### Placing a play bet — `POST /orbit/wagers` streamer branch (API, synchronous)

- Auth is the ordinary **Orbit session** (`X-Orbit-Session`). `PlaceOrbitWagerHandler` resolves wallet →
  player → `FindActive` **before** the Orbit-configured gate, so a streamer plays even on a stack with no
  Orbit custody configured (a non-streamer still gets `OrbitUnavailable` there).
- Order: stored result for `requestId` → a `BetDebit` with that `PlacementRequestId` (→ `Placed`) →
  self-exclusion (`SelfExclusionActive`) → game (`GameNotValidForBets`) → **type gate: house-edge, or
  `IsDuel && DuelKind == House`; anything else `GameNotValidForBets`** → `HouseEdgeEnabled` false →
  `HouseEdgeUnderMaintenance` (both modes) → `BetPreChecks` (stake bounds, board freshness < 10 s refused,
  choice, per-round rules) → `Balance < amount` → `OrbitInsufficientBalance` → in-process
  `RegisterBetRequest { StreamerPlay = true, StreamerWalletId, PlacementRequestId }` (no signature).
- **Synchronous:** no `PlaceManualOrbitBetEvent`, no claim, no transfer, no `Pending`. The POST answers
  `Placed` (status 2, `betId` read back from the debit row) or a 400 with the handler's message; both are
  stored in the 15-min Redis result store so a retry answers the same. A concurrent retry that lost the
  unique debit answers `Placed` from the ledger.
- `GET /orbit/wagers/{requestId}` for a streamer answers from the play ledger (scoped to the session's own
  streamer wallet), else the stored result, else `NotFound`.

### `RegisterBetHandler`

- `StreamerPlay`, `StreamerWalletId`, `PlacementRequestId` are **init-only flags on the request**, never on
  the public body (like `FundedByOrbit`). A malformed streamer request (Orbit-funded / claim id / missing
  wallet or request id / holder-only type / `amount ≤ 0` / a signature) → `FailedToRegisterBet` + Error,
  nothing moved.
- A play bet gets `EntryKind = Streamer (4)`, `ReferralCode = null`, `Signature = null`; it skips
  `ValidatePayment` and the signature-reuse guard (a null signature would match every null row), skips
  `WritePlacementLedgerRow` (**no `TransactionLog` row**), skips Hyper Roll coverage pricing and the
  fail-closed coverage refusal, and never takes the treasury lock on a House duel. Maintenance,
  self-exclusion, stake/board rules, the duel lock and the **house-word draw** all still apply.
  `BetPlacedEvent` is published as usual, so the round starts normally.
- The play debit runs **inside the lock's transaction, after the bet insert, before commit**
  (`DebitStreamerStakeBeforeCommit`): short balance → `OrbitInsufficientBalance`, rolled back. The bet
  exists **iff** the debit exists. Every streamer refusal is **reject-only** (no refund event).
- **Streamer-wallet guard (every non-play request):** an active streamer wallet placing anything else —
  a wallet-signed bet on any type, an OPEN DUELS room or join, a manual or automatic Orbit bet — is
  refused `GameNotValidForBets` **after** payment validation, so a verified wallet stake is **refunded**
  through the existing path (free bet: reject-only; an Orbit stake is returned by its claim's reconciler).
  A Space Jackpot holder entry → `JackpotEntryInvalid`, nothing moved.

### Quotes for the two house games (session-gated — anti-probing)

- `POST /games/{id}/availability` and `POST /games/{id}/duels` take an optional body `wallet`, **honoured
  ONLY when the request also carries a valid `X-Orbit-Session` for that same wallet**
  (`IStreamerLedger.IsVerifiedActiveStreamer`; the API resolves `SessionWallet` from the optional header).
  A bare `wallet` field, or a mismatched/invalid session, gets **exactly any other wallet's answer** with no
  lookup — nothing tells a third party a wallet is a streamer (`constraints.md` §6). The header never fails
  the request.
- Verified streamer, Hyper Roll: the open-board check, expiry refresh and maintenance still apply, then
  `Open` **without** coverage. House duel: floor/ceiling and maintenance still apply, then
  `OpenWithMaxStake(type MaximumStake)` — the **type** ceiling, never the treasury-clamped quote.
- Verified streamer, `POST /games/{id}/duels {kind: House}`: maintenance still refuses; the coverage
  refusal is skipped. **OpenRoom has no streamer branch** on either route — it opens/answers like anyone's,
  and the bet guard refuses the play bet on it.

### Settlement and refunds

- `RewardGameConsumer`: streamer winners are excluded from `paidWinners` (no blockhash pre-check, no fee
  reserve, no treasury balance), so **a streamer-only round never reads the treasury**. Reward =
  `CalculateRewardForBet` (Hyper Roll `stake × multiplier`; House duel `S × (2 − rate)` via the pot padded
  by `SetPotAmount`) → `CreditWin` into the play ledger → `Rewarded` (+ a house-edge `payouts[]` entry for
  the broadcast). Ledger failure → `NeedsManualAnalysis`. **No** treasury check, `settlement.Credit`,
  transfer or `TransactionLog` row. Losers: nothing moves.
- `RefundInvalidBetConsumer`: a streamer bet is refunded to the **play ledger only** (`Refund` row) →
  `Refunded`; never `settlement.Refund` / `RefundToOrbit`. A failure leaves the status for the
  pending-refund sweep to retry.
- `HouseEdgeCoverageService` excludes `EntryKind == Streamer` from exposure, so an unsettled streamer round
  never shrinks a real player's max stake.

### Orbit surfaces for a streamer

- `GET /orbit` (answered **before** the Orbit-configured gate): `isStreamer: true`, `balance` = the **play
  balance** (the real Orbit balance is never returned), `enabled: false`, `gameParametersId: null`,
  `eligibleGameTypes: []`, totals 0, Telegram off, and every #200 solo field defaulted (`stake`,
  `boardMode`, `multiplier`, `currentAutoRoundId` null; `waitingForHouse`, `autoStakeInFlight` false).
  Mint / receiver are blank when Orbit custody is unconfigured. Non-streamers get `isStreamer: false`.
- Refused with the vague `OrbitRequestInvalid` (400) + `Log.Warning("Orbit {Action} refused: player … is an
  active streamer.")`: `POST /orbit/deposits` (also recover-a-deposit), `POST /orbit/cash-outs`,
  `PATCH /orbit/settings`, `POST /orbit/telegram/link` and `/verify`. `GET /orbit/history` returns an
  **empty page**. `POST /orbit/session` and the wager routes work.
- Auto-bet: besides the reconciler's disable, active streamers are excluded from the shared planner's
  `ResolveCandidates`, the #200 solo loop (`OrbitSoloAutoBetService` tick + its pre-place re-check), the
  `OrbitSoloAutoBetSweepJob` and the startup `KickOrbitSoloLoops`.

### Read paths (keyed on `Bet.EntryKind == Streamer`, never the wallet list)

- **Excluded:** leaderboards (`PaidBetsIn` / `WinningBets` → gamble race, weekly PnL, hall of fame and the
  caller's own row); every **money** figure in global and per-type stats (paid bets, credited bets, biggest
  pot, hottest choice); referral `EarningBets` and `deploy/sql/referral-payout-report.sql`
  (`AND "EntryKind" <> 4`, kept byte-equivalent). The existing `Amount > 0` filters stay alongside.
- **Partner roster** (`GetReferralPlayersHandler`) has **no** EntryKind predicate: play bets always carry
  `ReferralCode = null`, so the code match excludes them and the covering index
  `IX_GameBets_ReferralCode_CreatedDate_Id` still serves the query.
- **Counted:** participation (rounds played, bets placed, unique players). Round history, the public feed
  and the history Pot show streamer bets **unchanged** — no DEMO marking.
- **Own view:** history `MyPayout` and personal stats (`TotalWon` / `Net` / biggest win) read the play
  ledger via `WonFor` for streamer bets (they have no `Credit` rows); their stakes count in `TotalWagered`.
- A deactivated streamer reverts to a normal wallet; the play balance is frozen (kept, invisible); past bets
  keep `EntryKind = Streamer`.

### Frontend

- `OrbitState.isStreamer`. `useOrbit` writes a **durable per-device marker**
  `space-stroker.orbit.streamer.<wallet>` (`'1'`) whenever the state loads `isStreamer: true` and removes
  it when it loads `false`; `isStreamerFor` / `orbitIsStreamer` = loaded state for the session's wallet
  **or** the marker, so a lapsed session still never reaches a wallet transfer.
- `nextBetSource` for a streamer: Hyper Roll or duel VS HOUSE → `'orbit'` regardless of balance (checked
  before the free-bet branch); everything else `null`. **No wallet fallback anywhere** — no silent fallback,
  no race fallback; a short balance shows *"Not enough Orbit balance for that bet."* and stops; a lapsed
  session asks for one Orbit sign-in and on decline stops with the generic refusal. The out-of-Orbit-funds
  toast never fires for a streamer.
- Availability / duel-open calls send `wallet` **plus** the `X-Orbit-Session` header
  (`orbitSessionTokenFor`); the duel re-quotes the VS HOUSE ceiling once the session token arrives (the
  marker can flag a streamer before the token is loaded).
- Monkey Duel: VS HOUSE only — the OPEN DUELS tab is hidden, the tab switch is dead, `loadBoard` never lands
  on the board, take/open-own refuse up front. ⚠️ The duel's streamer watchers are installed on the **first
  `useDuel()` call**, never at module import: their getters reach `useWallet()`, which throws before
  `App.vue`'s `initWallet`, and a module-level watch evaluates immediately — it blanked the app.
- Cosmic Sprint / Space Jackpot: lever inert, `placeBet` refuses with the generic `GameNotValidForBets`
  copy (`GAME_NOT_AVAILABLE_MESSAGE`). No streamer-specific copy anywhere.
- Orbit surfaces: the quick popup hides ADD MONEY / CASH OUT; the modal shows **WALLET only** (no LET ORBIT
  PLAY, NOTIFICATIONS or RECOVER) without deposit / cash-out; `HomeOrbitCard` is hidden. The #200
  follow-auto-round / rollover hold / lever lock logic is off for a streamer.
- **No DEMO badge or marking** (decision 8).

### Last-stake memory (decision 12 — all players)

- `src/utils/lastStake.ts`, key `space-stroker.last-stake.<gameTypeId>.<wallet>`: written when a **paid**
  bet is placed (wallet, Orbit or streamer play; free bets never), restored on entering the game, after
  every round/bet and on a late wallet connect — **only** over the default (type minimum / empty) or a value
  it filled itself, never over the player's typing. Clamped by `setStake`: Hyper Roll
  `[RequiredAmount, MaximumStake]`, duel `[min, current tab ceiling]`. **Paused while Orbit auto-play drives
  the round** (#200). Device-local, storage failures ignored, no backend.

### Accepted risks (owner)

- ⚠️ **No in-app or Terms disclosure** that a streamer plays play money; disclosure is left to the streamer.
- ⚠️ A streamer on a **first-ever device with no Orbit session** (no marker yet) can still send a
  wallet-signed bet: the guard refuses and refunds it, but the network fee is lost.
- ⚠️ A real-money #200 solo round **already running** when the wallet is registered still settles to Orbit
  custody normally; only the next tick is stopped.
- A real Orbit balance is hidden, not moved — ops cash it out before registering the wallet.

## Ops

- **Add / remove / top up** (any env): edit `deploy/secrets.<env>.json` → `appConfig`:
  `GeneralSettings:Operator:Streamers:Wallets:<n>:Wallet` (+ optional `…:<n>:Active: false`),
  `GeneralSettings:Operator:Streamers:TopUps:<n>:{Id,Wallet,Amount}` (a new, never-reused `Id`), then run a
  **full** `dotnet run -- deploy --env <env>`. ⚠️ A component deploy (`deploy consumers` / `deploy api`) only
  pushes an image — it does not `terraform apply`, so **appConfig changes never reach Cloud Run**. Removing a
  wallet = delete its entry (or `Active: false`) → deactivated on the next Consumers start.
- Local stack: put the env vars (`GeneralSettings__Operator__Streamers__Wallets__0__Wallet=…`) on **both**
  `api` and `consumers` in the untracked `docker-compose.override.yml` and recreate those containers.
- Migration `20261007033746_AddStreamerWallets` (two new tables + indexes only; regenerated on top of #200's
  `OrbitSoloAutoBet`): forward-safe, deploy Consumers → API → front.

## Key code touchpoints

**Backend (`monkify-backend/src`)**

- Domain: `Bets/ValueObjects/BetEntryKind.cs` (`Streamer = 4`), `Bets/Services/BetExtensions.cs`
  (`IsStreamerPlay`), `Streamers/Entities/StreamerWallet.cs`, `StreamerTransaction.cs`,
  `Streamers/ValueObjects/StreamerTransactionType.cs`, `Configs/Entities/StreamerSettings.cs`,
  `OperatorSettings.cs` (`Streamers`).
- Infrastructure: `Data/Context/SpaceStrokerDbContext.cs`, `Migrations/20261007033746_AddStreamerWallets.cs`,
  `Services/Streamers/{IStreamerLedger,StreamerLedger,StreamerWalletReconciler}.cs`,
  `Handlers/Orbit/{PlaceOrbitWager,GetOrbitWager,GetOrbit,GetOrbitHistory,CreditOrbitDeposit,RequestOrbitCashOut,UpdateOrbitSettings,Telegram}`,
  `Handlers/Games/RegisterBet/*`, `Handlers/Games/CheckBetAvailability/*`, `Handlers/Games/OpenDuelRoom/*`,
  `Consumers/RewardGame/RewardGameConsumer.cs`, `Consumers/RefundInvalidBet/RefundInvalidBetConsumer.cs`,
  `Services/Games/{HouseEdgeCoverageService,GlobalStatsService,GameStatsService,GameHistoryService}.cs`,
  `Services/Leaderboards/LeaderboardService.cs`, `Services/Players/PlayerStatsService.cs`,
  `Services/Referrals/ReferralEarningsService.cs`,
  `Services/Orbit/{OrbitBetPlacementService,OrbitSoloAutoBetService}.cs`, `Jobs/OrbitSoloAutoBetSweepJob.cs`.
- Api: `Controllers/GamesController.cs` (optional `X-Orbit-Session` on availability + open duel).
  Consumers: `Program.cs` (`ReconcileStreamerWallets`, streamer filter in `KickOrbitSoloLoops`).
  Config: `SpaceStroker.Common/spacestrokersettings.json`.

**Frontend (`monkify-front/src`)** — `types/orbit.ts`, `types/index.ts`, `hooks/useOrbit.ts`
(`isStreamerFor`, `orbitIsStreamer`, `orbitSessionTokenFor`), `hooks/useGame.ts`, `hooks/useDuel.ts`,
`services/mainService.ts`, `components/game/{GameConsole,DuelConsole,DuelTabs}.vue`,
`components/orbit/{OrbitQuickBody,OrbitModal}.vue`, `components/home/HomeOrbitCard.vue`,
`utils/lastStake.ts`.

**Meta** — `deploy/secrets.example.json` (`_comment_streamers` + `MOCK_` placeholders),
`deploy/sql/referral-payout-report.sql`.

## Tests

- Unit (new): `StreamerWalletReconcilerTests`, `StreamerLedgerTests`, `StreamerSettingsBindingTests`.
  Extended: `PlaceOrbitWagerTests`, `GetOrbitWagerTests`, `GetOrbitEligibleTypesTests`,
  `GetOrbitHistoryTests`, `CreditOrbitDepositTests`, `RequestOrbitCashOutTests`, `UpdateOrbitSettingsTests`,
  `TelegramLinkTests`, `RegisterBetHandlerTests`, `CheckBetAvailabilityHandlerTests`,
  `OpenDuelRoomHandlerTests`, `RewardGameConsumerTests`, `RefundInvalidBetConsumerTests`,
  `HouseEdgeCoverageServiceTests`, `LeaderboardServiceTests`, `LeaderboardHallOfFameTests`,
  `GlobalStatsServiceTests`, `GameStatsServiceTests`, `GameHistoryServiceTests`, `PlayerStatsServiceTests`,
  `ReferralEarningsServiceTests`, `GetReferralPlayersHandlerTests`, `OrbitBetPlacementServiceTests`,
  `OrbitSoloAutoBetServiceTests`, `OrbitSoloAutoBetSweepJobTests`.
- Integration (mandatory): `StreamerWalletsFlowTests` — Hyper Roll happy path (play win credited, zero
  transfers, no `TransactionLog`, leaderboard/stats money unchanged, round in history), House duel settling
  to the ledger, refusals (wallet bet refunded, balance unchanged), reconciler idempotency + deactivation,
  and a streamer's solo auto-bet enrolment disabled and never staking.

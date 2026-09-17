# SpaceStroker — "Hyper Roll Solo Rounds" — Implementation Spec

> **Status: Ready for implementation** — designed with the owner on 2026-09-16/17 (in-session brainstorm,
> after the decision-34 UI redesign). Extends [`hyper-roll.md`](hyper-roll.md) as its **decision 35**.
> Repos affected: `monkify-backend`, `monkify-front`, and this doc in `monkify-meta`.
> Branch / worktree: `feature/hyper-roll` → `.worktrees/hyper-roll/<repo>` (the unshipped Hyper Roll
> feature branch — this is part of the same feature, not a separate card).
> Change level: **Feature** — mandatory happy-path integration test plus integration tests for the
> one-bettor race and the board cleanup (§9.2).
>
> **Purpose:** Hyper Roll is a player-vs-house game, but today its rounds are shared: one round per type,
> one board every player bets on, a countdown others can join. The owner's rule: **"there is no case where
> more than one player should ever bet in the same round for this game."** Every player gets their **own
> round and own board**, can **reroll** the board with the lever before betting, and their bet runs
> **their** round immediately.

---

## 1. Summary

| | Today (shared) | Solo rounds |
|---|---|---|
| Who opens a round | Backend rollover (`RunGameConsumer`), startup, type creation — one per type | The player: `POST /games/{gameTypeId}/boards` |
| Bettors per round | Any number | **Exactly one** — enforced server-side |
| Round owner | None | `Game.OwnerPlayerId`, set by the first (only) bet |
| Board | Drawn once at creation, write-once | Drawn at open; **rerolled in place** while unbet |
| Start | 5 s after the first bet | **Immediately** |
| Next board | Auto rollover 9 s after the result | **Lever pull or page reload** |
| Unbet boards | n/a | Expire after 30 min idle; a job deletes them |
| Chat | Per round group | **Per game type** for house-edge (players still share a room) |
| Lobby card pill | FILLING / STARTING / LIVE | None for house-edge (a board is always ready) |

Run, typer, rewards, payout-time solvency, refunds, verification and the treasury-coverage math are
**unchanged** — they are already keyed by round id.

## 2. Constraints (MANDATORY)

- Follow `monkify-meta/AGENTS.md` and `constraints.md`. EF config inline in `OnModelCreating`; migrations
  applied only by `SpaceStroker.Consumers`; never edit an existing migration or hand-edit the snapshot.
- **Money path first.** Every rule that decides whether a paid bet is accepted runs **inside the
  house-edge coverage advisory lock**, in the same transaction as the insert. A refused paid bet always
  goes through the existing `RefundInvalidBet` path.
- Legacy (non-house-edge) types are **byte-identical**: every change is gated on
  `GameParameters.IsHouseEdge`.
- Player-facing refusals stay vague (`constraints.md` §6): a taken round and an expired board reuse the
  existing generic bet-refusal messaging; the maintenance message (decision 33) is unchanged.
- Run `run_tests.bat` (never a bare `dotnet test`) and the integration suite; report changed tests.

## 3. Resolved product decisions (owner, 2026-09-16/17)

| # | Decision |
|---|---|
| S1 | **Independent rounds.** Every Hyper Roll round belongs to one player. A second bet on a round that already has a bet is refused (paid → refunded). |
| S2 | **Board handle before the wallet (option B).** A board is opened anonymously and its round id kept by the browser; the first bet binds the round to that wallet. Chosen over wallet-bound boards, which would need a signed session before a player could even see a board, and would let anyone reroll someone else's board through the unproven `?wallet=` parameter. |
| S3 | **Start immediately** after the bet lands (`HouseEdgeTimeUntilGameStarts = 0`) — nobody can join, so the countdown had no purpose. |
| S4 | **Reroll = redraw in place.** Same round id; the 8 board rows are replaced and the server seed rotated. Allowed only while the round has no bet. |
| S5 | **Shared room, private rounds.** House-edge chat is per game type; completed rounds stay in the public feed/history (each is a real bet); Hyper Roll stats say "biggest win" instead of "biggest pot"; the lobby card drops the live pill. |
| S6 | **Next board on lever pull or page reload.** No automatic rollover for house-edge. Reload with an unbet board restores it; reload mid-round re-attaches to the player's running round; reload after a result opens a fresh board. |
| S7 | **Architecture: private rounds on the existing `Game` table** (approach 1) — chosen over a separate board table created at bet time (moves round creation into the money path) and a synchronous in-API pipeline (forks payouts/refunds/recovery). |

### 3.1 Spec-time decisions (flag at implementation start)

| # | Decision | Why |
|---|---|---|
| T1 | **The coverage lock stays global** (one advisory key), and `hyper-roll.md` §6.13's "per-round lock" wording is corrected. | Coverage sums the worst case over **every** unsettled house-edge round. Per-round locks would let two bets on different rounds both pass while together exceeding the treasury. With many concurrent solo rounds the global key is what keeps coverage correct. |
| T2 | The treasury **balance read stays under the lock, after the exposure query** (unchanged). *(Revised during planning: an earlier draft moved it before the lock.)* | `HouseEdgeCoverageService.EvaluateAsync` reads exposure first and balance second on purpose: a round that pays in between is then still counted against a balance that already paid it (conservative). Reading the balance first reverses that and overstates headroom by the payout. The read is already the fast-fail request-path RPC (`GetSolBalanceForRequest`), so the lock hold stays short. |
| T3 | Board TTL **30 min**, refreshed by every read/pre-check/reroll of that board; cleanup runs every **5 min** and deletes boards past TTL. | Long enough that an idle player never loses a board mid-decision; short enough that abandoned tabs don't accumulate. |
| T4 | A wallet **may** have more than one unfinished round (e.g. two tabs). The UI shows one. | Each bet is already priced by the coverage check; a hard one-round-per-wallet limit adds a new refusal on the money path for no safety gain. |
| T5 | Opening a board does **not** broadcast `GameReceived` and does **not** publish `GameOpenedEvent`. | Nothing else consumes a solo board (Orbit and the house seed already exit for house-edge); broadcasting every reroll-scale open to all clients is noise. |
| T6 | Per-type chat reuses `SendChatMessage`/`JoinGame` with the **game type id** as the group and the persisted `GameChatMessage.GameId`. | `GameChatMessages.GameId` has no FK to `Games` and GUIDs don't collide with round ids, so no schema change; retention/purge is unchanged. |

## 4. Data model

### 4.1 `Game` (`SpaceStroker.Domain/Games/Entities/Game.cs`)

- `Guid? OwnerPlayerId` + `Player? Owner` — null until the first bet; set in the bet's transaction; never
  changes afterwards. Always null for non-house-edge rounds.
- `DateTime? BoardExpiresAt` — set only on unowned house-edge rounds; cleared when the bet binds the owner.

### 4.2 EF configuration (inline)

- `Game.OwnerPlayerId` → FK to `Players` (`OnDelete: Restrict`).
- Index `(ParametersId, OwnerPlayerId, Status)` — backs the "my unfinished round" lookup.
- Index `BoardExpiresAt` filtered `WHERE "BoardExpiresAt" IS NOT NULL` — backs the cleanup job.

### 4.3 Migration

One new migration (`HyperRollSoloRounds`), additive columns + indexes, no backfill. Existing rows keep
`OwnerPlayerId = null`, `BoardExpiresAt = null`. A shared Hyper Roll round left open by the old code (local
databases only — Hyper Roll is unshipped) is unowned with no expiry: the cleanup job treats an **unowned,
unbet house-edge round in `WaitingBets` with `BoardExpiresAt IS NULL`** as expired.

## 5. Configuration (`spacestrokersettings.json` → `GeneralSettings:Games`)

- `HouseEdgeTimeUntilGameStarts`: **5 → 0**.
- New `HouseEdgeBoardTtlMinutes`: **30** (code default 30).
- New `HouseEdgeBoardCleanupIntervalMinutes`: **5** (code default 5).
- `IpRateLimiting` rules: `POST:/games/*/boards` **30/min**, `POST:/games/rounds/*/reroll` **60/min**,
  `GET:/games/rounds/*` **120/min**. (None contain `/bets`, so `RateLimitSettingsTests` accepts them.)

## 6. Backend behaviour

### 6.1 Opening a board — `POST /games/{gameTypeId:guid}/boards`

New handler `Handlers/Games/OpenHouseEdgeBoard`. Refuses (generic not-found/unavailable, `constraints.md`
§6) when the type is unknown, inactive, coming-soon or not house-edge. Maintenance does **not** block
opening (players can still look; betting is what the switch refuses).

`GameService.CreateGame(parameters, carry, isSoloBoard: true)` — the existing method gains a flag that, for
a house-edge type: sets `BoardExpiresAt = now + TTL`, and **skips** the `GameReceived` broadcast and the
`GameOpenedEvent` publish (T5). Board draw, validation and seed commit are the existing code.

Response: `ActiveGameDto` of the new round (board, `ServerSeedHash`, `Status = WaitingBets`, no bets).

### 6.2 Rerolling — `POST /games/rounds/{roundId:guid}/reroll`

New handler `Handlers/Games/RerollHouseEdgeBoard`, in one transaction **holding the coverage advisory lock**
(`HouseEdgeCoverageService.AcquireLockAsync`):

1. Load the round; refuse (generic) unless house-edge, `OwnerPlayerId IS NULL`, no bets, status
   `WaitingBets`.
2. Delete its `GameBoardChoices`; draw a new board (`HouseEdgeBoard.Draw` + `HouseEdgeRules` guards, the
   same code path `CreateGame` uses — extract it to a shared `DrawBoard` helper, no duplication).
3. Rotate the seed: new `ServerSeed` / `ServerSeedHash` (a new `GameService.RotateServerSeed`, the
   counterpart of `EnsureServerSeedCommitted` that overwrites instead of first-writer-wins — only valid on
   an unbet round).
4. `BoardExpiresAt = now + TTL`. Commit.

Response: the updated `ActiveGameDto`. A refusal leaves the board untouched; the front keeps what it has.

### 6.3 Reading a round — `GET /games/rounds/{roundId:guid}`

Returns `ActiveGameDto` for a house-edge round in `ActiveGamesStatus` (waiting, starting, in progress). For an
unowned round it refreshes `BoardExpiresAt`. Anything else is refused with the existing vague
`GameNotValidForBets` validation failure (the API's standard 400 envelope). A finished round is not returned:
the front opens a fresh board and the result stays in history.

### 6.4 `GET /games/{gameTypeId}?wallet=` (`GetActiveGameForParameterHandler`)

House-edge only: returns the wallet's **newest unfinished owned round** (`OwnerPlayerId` = that wallet's
player, status in `GameStarting`, `InProgress`, `Ended` or `RewardForWinnersInProgress` — a round still
running or still paying), or the existing `ParameterHasNoActiveGames` validation failure. A completed or errored round is not returned: the player gets a fresh
board and the result stays in their history. Without `wallet`, the same
failure (the lobby skips house-edge types anyway, §7.5). Legacy types unchanged.

### 6.5 Bet registration (`RegisterBetHandler`, house-edge only)

- **Round must exist.** A bet whose round is missing (deleted by cleanup) is refunded via
  `RefundInvalidBet` — never a 500, never a kept payment.
- `OneBetPerPresetRound` and `SecondBetNeedsAnotherPlayer` are **skipped** for house-edge (superseded).
- **Inside the lock**, in this order (the existing replay re-check stays first):
  1. **`HouseEdgeRoundTaken`** — the round has any bet → refuse (paid → refund; free → reject).
  2. **Choice still on the board** — re-validate `ChoiceIsValidForGame` against the board **as loaded
     under the lock** (a reroll can no longer interleave, but a stale client could still send an old word).
  3. Coverage (paid bets only), unchanged math and read order (T2).
  4. Insert the bet; `UPDATE "Games" SET "OwnerPlayerId" = @player, "BoardExpiresAt" = NULL WHERE "Id" =
     @round AND "OwnerPlayerId" IS NULL` — 0 rows affected → treat as taken (refund) and roll back.
- **Free house-edge bets now take the lock** for steps 1, 2 and 4 (they still skip coverage).

### 6.6 Start, run, reward

- `BetPlacedConsumer`: unchanged code; `SecondsUntilGameStarts(isHouseEdge)` now returns 0, so
  `TryStartGame` sets `StartDate = now` and the `Task.Delay` is zero.
- `RunGameConsumer`: **skip the successor `CreateGame` for house-edge** (the player opens the next board).
- `RewardGameConsumer`, verification, typer: unchanged. The keystream prefix is `gameId|<the one bet seed>`.

### 6.7 Startup and background

- `OpenNewGames`, `GameParametersCreatedConsumer`: skip house-edge types.
- `ReplanOpenRounds`: skip house-edge rounds.
- `CloseOngoingGames`: unchanged (only starting/in-progress rounds).
- New recurring job `HouseEdgeBoardCleanupJob` (Hangfire, `ScheduleRecurringJobs`): deletes rounds that are
  house-edge, `OwnerPlayerId IS NULL`, have **no bets**, status `WaitingBets`, and
  (`BoardExpiresAt < now` **or** `BoardExpiresAt IS NULL`) — board rows cascade. Takes the coverage lock per
  batch so it can never race a bet claim.
- `GameTypeDeactivatedConsumer`: unchanged (already cancels every `WaitingBets` round of the type).

### 6.8 Availability pre-check — `POST /games/{gameTypeId}/availability`

Body gains **`gameId`**. `FindOpenRoundAsync(parametersId)` is replaced by loading that round. A missing,
owned, already-bet or non-waiting round → the existing `GameNotValidForBets` validation failure (a board that
is gone or taken is not a maintenance condition). The front checks the board first (§7.2), so this is a
backstop. The maintenance switch and
coverage refusals keep returning `{ available: false, message }` exactly as decision 33 defines. A found
round's `BoardExpiresAt` is refreshed.

### 6.9 Contracts

- `ActiveGameDto`: + `BoardExpiresAt?` (informational; null once the round is owned).
- `CheckBetAvailabilityRequest`: + `Guid GameId`.

### 6.10 Stats

No backend change. `GameTypeStatsDto` already carries `BiggestSinglePayout`; for a house-edge type the front's
stats panel shows **BIGGEST WIN** from that field instead of "biggest pot" (§7.6). "Rounds played" already
counts completed rounds, which are exactly the bet rounds.

## 7. Frontend behaviour (`monkify-front`)

### 7.1 Round loading — `src/hooks/useGame.ts` (house-edge branch of `getBets`)

1. **Wallet known** → `GET /games/{typeId}?wallet=` → attach to that running round if found.
2. Else **stored board** (`localStorage['hyperRoll:board:<typeId>']`, try/catch) → `GET
   /games/rounds/{id}` → attach if it is unowned and `WaitingBets`.
3. Else **open** → `POST /games/{typeId}/boards` → store its id → attach.

Attach = the existing `hub.attachToGame(gameId, …)` path. A stored id that 404s is dropped silently.

### 7.2 Lever

- **No word picked, board unbet** → **reroll**: `POST /games/rounds/{id}/reroll`, replace
  `gameInstance.boardChoices` + `serverSeedHash`, clear `userWord`, play the reel.
- **Word + valid stake** → bet (existing `placeBet`). Before the availability pre-check (which now sends
  `gameId`), the front re-reads its board with `GET /games/rounds/{id}` (this also refreshes the TTL); if that
  fails, or the round is no longer waiting and unbet, it opens a fresh board and prints
  `> YOUR BOARD EXPIRED. HERE'S A FRESH ONE.` — **no transfer is started**.
- **Result shown** (`RewardForWinnersCompleted` / `ErrorWhenProcessingRewards`) → open a fresh board (§7.1
  step 3) and play the reel.
- The automatic 9 s rollover (`armRollover`/`evaluateRollover`) is **disabled for house-edge**.
- **Lever tag** (desktop + phone): a small LCD tag on the lever plate — `REROLL` (no pick), `PLAY 0.05`
  (pick + stake), `···` (betting/running), `NEW BOARD` (result shown).

### 7.3 Terminal copy (`GameTerminal.vue`, decision 34's lines, adjusted)

- Nothing picked: `> PICK A KEY [1-8] TO LOCK A WORD.` / `> OR PULL THE LEVER TO REROLL THE BOARD.`
- Picked: unchanged (`LOCKED: …` / `PULL THE LEVER: PLAY …, WIN … SOL`).
- Own bet placed: `> YOUR BET IS IN. EDSON IS WARMING UP...` (no countdown, no `THIS ROUND STARTS IN`).
- Rerolling: `> REROLLING THE BOARD...` while the request is in flight.
- The reel-in plays on a new round id **and** on a reroll (watch the board's word list, not only `gameId`).

### 7.4 Chat

House-edge: `JoinGame(<gameTypeId>)` alongside the round group; send with the type id; listen on
`<gameTypeId>/ChatMessage`. Legacy types unchanged.

### 7.5 Lobby

`useLiveGames` skips house-edge types (no live snapshot → idle card, no FILLING/LIVE pill).

### 7.6 Stats panel

`GameStatsPanel.vue`: for a house-edge type the biggest-pot row reads **BIGGEST WIN** and shows
`biggestSinglePayout`.

## 8. Out of scope

Signed wallet sessions for board ownership; per-player reroll limits; hiding solo rounds from the public
feed; Orbit/auto-bet on Hyper Roll (`AllowOrbit` stays false).

## 9. Tests

### 9.1 Unit (`SpaceStroker.Tests`)

- `OpenHouseEdgeBoardHandler`: opens with expiry, no broadcast/event; refuses non-house-edge/inactive.
- `RerollHouseEdgeBoardHandler`: redraws + rotates seed + refreshes expiry; refuses owned / bet / non-waiting.
- `RegisterBetHandler`: taken round refunds; missing round refunds; stale choice refunds; owner bound;
  legacy one-bet rules unchanged for legacy types.
- `GetActiveGameForParameterHandler`: house-edge by wallet / 404 without wallet; legacy unchanged.
- `CheckBetAvailabilityHandler`: uses `gameId`; gone/owned round → unavailable.
- `HouseEdgeBoardCleanupJob`: deletes expired/null-expiry unowned unbet boards; never owned or bet rounds.
- `GameSettings.SecondsUntilGameStarts(true)` = 0; `RunGameConsumer` opens no successor for house-edge.

### 9.2 Integration (`SpaceStroker.Tests.Integration`, Testcontainers)

1. **Happy path (mandatory):** open board → reroll (board + seed hash change) → paid bet on a board word →
   round owned by the wallet → starts without delay → typer ends → winner paid `stake × multiplier` →
   verification reproduces the draw → no successor round was opened.
2. **One bettor:** two paid bets race on one round → exactly one accepted, the other refunded.
3. **Cleanup:** an expired unowned board is deleted; an owned finished round and a fresh board survive.

## 10. File checklist

### 10.1 Backend

| File | Change |
|---|---|
| `Domain/Games/Entities/Game.cs` | `OwnerPlayerId`, `Owner`, `BoardExpiresAt` |
| `Infrastructure/Data/Context/SpaceStrokerDbContext.cs` | FK + 2 indexes |
| `Infrastructure/Migrations/<ts>_HyperRollSoloRounds.cs` | New |
| `Domain/Configs/Entities/GameSettings.cs` | TTL + cleanup interval |
| `Common/spacestrokersettings.json` | start delay 0, TTL, interval, rate-limit rules |
| `Infrastructure/Services/Games/GameService.cs` | `CreateGame(isSoloBoard)`, shared `DrawBoard`, `RotateServerSeed` |
| `Infrastructure/Handlers/Games/OpenHouseEdgeBoard/*` | New |
| `Infrastructure/Handlers/Games/RerollHouseEdgeBoard/*` | New |
| `Infrastructure/Handlers/Games/GetHouseEdgeRound/*` | New (`GET /games/rounds/{id}`) |
| `Infrastructure/Handlers/Games/GetActiveGameForParameter/*` | house-edge by wallet |
| `Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs` | §6.5 |
| `Infrastructure/Handlers/Games/CheckBetAvailability/*` | `gameId` |
| `Infrastructure/Services/Games/HouseEdgeCoverageService.cs` | balance before lock; load round by id |
| `Infrastructure/Consumers/RunGame/RunGameConsumer.cs` | no successor for house-edge |
| `Infrastructure/Consumers/GameParametersCreated/*` | skip house-edge |
| `Consumers/Program.cs` | `OpenNewGames`/`ReplanOpenRounds` skip; schedule cleanup job |
| `Infrastructure/Jobs/HouseEdgeBoardCleanupJob.cs` | New |
| `Api/Controllers/GamesController.cs` | 3 new routes |

### 10.2 Front

| File | Change |
|---|---|
| `src/services/mainService.ts` | `openHouseEdgeBoard`, `rerollHouseEdgeBoard`, `getRound`, availability `gameId` |
| `src/hooks/useGame.ts` | §7.1, reroll action, rollover disabled, expired-board handling |
| `src/components/game/GameConsole.vue` | lever routing + lever tag |
| `src/components/game/GameTerminal.vue` | §7.3 copy, reel on reroll |
| `src/hooks/useGameChat.ts` / `useGameHub.ts` | per-type chat group |
| `src/hooks/useLiveGames.ts` | skip house-edge |
| `src/types/index.ts` | `boardExpiresAt?`, availability `gameId` |
| `src/components/game/GameStatsPanel.vue` | BIGGEST WIN for house-edge |

## 11. Acceptance criteria

1. Opening Hyper Roll with no wallet shows a board without any wallet prompt; a reload shows the same board.
2. Pulling the lever with no word picked replaces the 8 words (reel plays) and changes the published seed hash.
3. A paid bet runs the player's round immediately; no other player can place a bet on that round (refunded).
4. After the result, the board does not change until the lever is pulled or the page is reloaded.
5. Two players on Hyper Roll at the same time never see each other's board, countdown or bets, but share chat.
6. An abandoned board is deleted after its TTL; a bet can never be taken on a deleted board without a refund.
7. Treasury coverage still refuses a bet whose worst case, summed with every other unsettled Hyper Roll
   round, exceeds the treasury.
8. Legacy types (Cosmic Sprint, lotteries) behave exactly as before.

## 12. Changelog

| Date | Change |
|---|---|
| 2026-09-17 | Spec written from the owner brainstorm (S1–S7, approach 1). Recorded as decision 35 in `hyper-roll.md`. |

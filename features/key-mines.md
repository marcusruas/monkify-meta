# Feature — Key Mines

> Status: **shipped.** Authoritative spec: `../specs/key-mines.md`
> (merged via `feature/key-mines` on 2026-11-03; card [#204](https://trello.com/c/KQyT1Ini)).
> Builds on the house-edge machinery in `hyper-roll.md` / `hyper-roll-word-lines.md`.

## What it does

A solo player-vs-house **Mines** game on Edson's 26-key QWERTY. The player picks a stake and how many keys
hide a **jam** (1–10), pulls the lever, then presses keys one at a time to climb a multiplier ladder and
cashes out whenever they want. A jam loses the stake. The treasury pays at **97% RTP, exact at every step**.
It is the first mode where the player decides when to stop.

## Core rules

- **Type:** `IsHouseEdge = true` **+ `IsKeyMines = true`** (seeded `DefaultGames["Key Mines"]`). `IsKeyMines`
  is checked **first** in every typer / weighted-draw / Word Lines branch, which steps aside: no typer, no
  `RunGameEvent`, no `GameBoardChoices`. Rounds carry `Game.BoardMode = "KEYS"`. `AllowOrbit = false`.
- **Bet:** open a solo board (`POST /games/{typeId}/boards`, seed hash committed, returns a one-time
  `playToken` — only its SHA-256 is stored), then bet `Choice = "J{M}"` with `M` in the type's
  `KeyMinesJamOptions` (1–10). Stake **0.002 – 0.1 SOL**. Sources: wallet, Orbit manual (`POST /orbit/wagers`)
  and streamer play bets; a win or refund lands where the stake came from. The bet starts the round in the
  same transaction (`WaitingBets → InProgress`, jams derived and stored).
- **Ladder:** `pay(stake, M, k) = Truncate9(stake × RTP × C(26,k) / (100 × C(26−M,k)))` — exact rational
  `decimal`, the same formula in `KeyMinesLadder.cs` and `utils/keyMines.ts`. **Floor ×1.01** on every step
  (only 1 jam step 1, ×1.0088, is lifted), so every cash-out is a win; a jam is a loss.
- **Truncation:** the last step is the last `k` with `stake × ladder[k] ≤ MaxPayoutPerRound` (**12 SOL**,
  create-only) **and** `ladder[k] ≤ 120×` (`KeyMinesLadder.MaxMultiplier`). At 97% the 120× ceiling ends every
  ladder (top: 117.75× for 8 and 10 jams), so the worst case is **0.1 × 117.75 = 11.775 SOL**. Reaching the
  last step, or clearing every safe key, auto-cashes.
- **Play:** `POST /games/rounds/{id}/press` `{key}` and `POST /games/rounds/{id}/cashout`, both requiring
  header **`X-Round-Token`** (a mismatch, a repeated key, an ended or non-Key-Mines round → the vague 400).
  Each end is **one conditional update** on `(Id, InProgress, PressedKeys = expected)`, so press vs press vs
  cash-out vs sweep is race-safe: exactly one wins, money never moves twice. Rate limits 240/min (press),
  30/min (cash-out) per IP.
- **Provably fair:** jams = `M` picks without replacement over `qwertyuiopasdfghjklzxcvbnm` from
  `HMAC-SHA256(serverSeed, gameId|orderedBetSeeds|jams)` (rejection sampling). Seed revealed at the end;
  `GET /games/{id}/verification`, the FAIR panel and `verify.html` recompute the set. Jams are never returned
  before the round ends.
- **Inactivity (5 min, Hangfire `key-mines-inactivity-sweep`, every minute):** ≥ 1 safe press ⇒ auto-cash at
  the current step (a win, history shows **AUTO CASH**); 0 presses ⇒ refund (`GameEndedAbruptely`,
  `KeyMinesEnd = "refund"`). The sweep also re-drives rounds stuck `Ended`.
- **Survives a Consumers restart:** Key Mines rounds are excluded from `CloseOngoingGames`.
- **Coverage:** each unsettled Key Mines round reserves `stake × ladder[lastStep]` (a flat book) next to
  Hyper Roll, House duels and reserved jackpot pots; `/availability` and the bet backstop price a new bet
  the same way. **One live round per wallet** (`KeyMinesOneLiveRound`): a wallet with a KEYS round still
  `InProgress` cannot start another (message `KeyMinesRoundAlreadyLive`, paid ⇒ refunded).
- **Payout:** `RewardGameConsumer` → `CalculateHouseEdgeReward` Key Mines branch (`stake × ladder[k] −
  AlreadyCreditedAmount`, no commission). A jam closes `RewardForWinnersCompleted` with the bet left `Made`.
- **Low-treasury pause (production only):** when the treasury wallet is seen **under 8 SOL**
  (`GeneralSettings:Games:KeyMinesPauseWhenTreasuryBelow`, 0 = off) — on a Key Mines availability check, a
  Key Mines bet, or after **any** treasury-paid round settles — every Key Mines type gets
  `AutoPausedAt`/`AutoPausedReason` (once). From then on every new-bet path refuses with the vague
  maintenance message and the lobby shows MAINTENANCE; live rounds finish and pay. **Nothing in code clears
  it**, refilling included: re-open with
  `UPDATE "GameParameters" SET "AutoPausedAt" = NULL, "AutoPausedReason" = NULL WHERE "IsKeyMines";` then
  drop `ActiveParameters:v5`.
- **Mid-round:** self-exclusion, `HouseEdgeEnabled = false` and the pause refuse **new bets only**; press and
  cash-out on a bet round always work.
- **Profile / leaderboards:** `GameModeClassifier` returns `keyMines` (checked before `IsHouseEdge`), so a
  Key Mines win never counts as Hyper Roll (`firstHyperRollWin`, `hyperHit`, explorer). Hall of Fame and the
  win streaks count it. Explorer stays the four original modes.
- **Lobby:** order Hyper Roll · Key Mines · Monkey Duel · Space Jackpot (**Cosmic Sprint is `ComingSoon`**);
  card badges `1P · VS HOUSE` / `UP TO 100X`, poster + 3 s GIF.
- **Front:** console with the 26 keys, jam −/+ stepper, XP bar (LAST / NOW / NEXT), red pixel **CASH OUT**
  keycap showing the live pay (fixed red, `utils/cashKeyArt.ts`, 45% when off). SPACE pulls the lever, ENTER
  cashes out. A live round restores on reload from the stored board + token. Practice mode
  `/game/practice?mode=key-mines` (front-only, `Math.random` jams, 97% RTP). Guide section `HOW KEY MINES
  WORKS`. LP: mode card, "4 game modes live", terms clause (97%, ×1.01 floor, 12 SOL / 120× cap, inactivity
  auto-cash).

## Key code touchpoints

- **Domain:** `SpaceStroker.Domain/Games/Services/KeyMinesLadder.cs`, `KeyMinesRules.cs`, `KeyMinesMoves.cs`;
  `Games/Entities/GameParameters.cs` (`IsKeyMines`, `KeyMinesJamOptions`, `MaxPayoutPerRound`,
  `AutoPausedAt`/`AutoPausedReason`) and `Game` (`KeyMines*` columns); `Configs/Entities/GameSettings.cs`.
- **Infrastructure:** `Services/Games/KeyMinesRoundService.cs` (press / cash-out / end), `KeyMinesTokens.cs`,
  `KeyMinesOneLiveRound.cs`, `KeyMinesPause.cs`, `KeyMinesTreasuryGuard.cs`;
  `Handlers/Games/PressKeyMinesKey/*`, `CashOutKeyMines/*`; `Jobs/KeyMinesInactivitySweepJob.cs`;
  branches in `RegisterBetHandler`, `CheckBetAvailabilityHandler`, `PlaceOrbitWagerHandler`,
  `PlaceManualOrbitBetConsumer`, `RewardGameConsumer`, `HouseEdgeCoverageService`, `GameService`.
- **Migrations:** `20261027225633_KeyMines`, `20261029190402_KeyMinesAutoPause` (additive).
- **API:** `GamesController` — `rounds/{roundId}/press`, `rounds/{roundId}/cashout`; config
  `spacestrokersettings.json` (`HouseEdge` block of Key Mines, rate limits, `KeyMines*` game settings).
- **Front:** `components/game/KeyMinesConsole.vue`, `KeyMinesScreen.vue`, `KeyMinesKeyboard.vue`,
  `KeyMinesXpBar.vue`, `keyMines.ts`; `hooks/useKeyMines.ts`, `useLiveKeyMines.ts`, `usePracticeKeyMines.ts`;
  `utils/keyMines.ts`, `cashKeyArt.ts`, `keyMinesSfx.ts`; `components/guide/sections/GuideKeyMines.vue`.
- **LP:** `components/GameModes.vue`, `SpaceRoadmap.vue`, `pages/terms.astro`.
- **Tests:** unit `KeyMinesLadderTests`, `KeyMinesRulesTests`, `KeyMinesTreasuryGuardTests`, Key Mines cases
  in `RegisterBetHandlerTests` / `CheckBetAvailabilityHandlerTests` / `RewardGameConsumerTests`; integration
  `KeyMinesFlowTests`, `KeyMinesSettlementTests`, `KeyMinesInactivitySweepTests`, `KeyMinesReconcileTests`,
  `KeyMinesRoundServiceSmokeTests`.

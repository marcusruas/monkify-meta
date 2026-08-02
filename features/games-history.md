# Feature — Games history

> Status: **shipped.** Authoritative spec: `../specs/games-history.md`
> (merged via `feature/games-history` on 2026-07-23).

## What it does

Players can open a standalone **Games History** page (`/game/history`, reached from a new
header **menu button**) to browse the completed rounds of one game type: when each round ended, its
winning choice, pot, number of bets, winners, and a short server-seed fingerprint — plus, on rounds
they bet in, their own choice / stake / win / payout. A **"mine only"** toggle narrows the list to
rounds the caller bet in, and a per-row **Verify** button hands the round's verification payload off to
the offline `verify.html`. This is the round-history follow-up the live FAIR tab deferred (which only
covers the current round). See `features/provably-fair-final.md`.

## Core rules

- **Endpoint.** `GET /games/{parametersId}/history?pageNumber&pageSize&mineOnly&wallet` returns a
  `PaginatedList<GameHistoryDto>` (newest first). Public — no auth header.
- **One game type at a time.** The page is scoped to a single `GameParameters` via a dropdown (defaults
  to the first active type from `getGamesTypes`); there is no cross-type "all rounds" view.
- **Paginated, lazy.** `pageSize` is clamped to `[1, 30]`, `pageNumber` to `>= 1`; an out-of-range page
  returns an empty page, not an error. Exactly one page is fetched per view/action — the client never
  fans out across game types, and a request-sequence guard drops stale/overlapping responses.
- **Which rounds appear.** Only games that reached `RewardForWinnersCompleted`. Refunded /
  under-subscribed (`NotEnoughPlayersToStart`), `ErrorWhenProcessingRewards`, and `GameEndedAbruptely`
  rounds are excluded entirely.
- **Row fields.** Ended date/time (`Game.EndDate`, fallback `CreatedDate`), game-type name, winning
  choice, pot, **bet count**, winner count, and the first 12 chars of `ServerSeedHash`. The full typed
  character stream is never returned or shown.
- **Money rules mirror game-type-stats.** Pot is reconstructed per round as `Σ paid Bet.Amount`
  (+ `LotteryParameters.LotteryPot` + `Game.CarriedExtra` for lottery types). Free bets (`Amount == 0`)
  are excluded from pot and winner count. Every game settles in the one configured token, so the DTO
  carries that token's symbol/decimals and the client renders amounts token-native.
- **Bet count includes free bets.** `BetCount` is the total number of bets placed in the round (free
  bets included) — a participation metric, unlike pot/winners which are paid-only.
- **Own-bet columns (nullable).** On rounds the caller bet in, the row carries `MyChoice`, `MyStake`,
  `MyWon` (choice == winning choice), and `MyPayout` (`Σ` of the bet's `Credit` `TransactionLog` rows;
  0 when lost, and 0 for a free-bet win). Null on rounds they didn't bet in. If the caller placed
  multiple bets in one round, the winning bet is surfaced, else the first by `CreatedDate` (one row per
  round).
- **"Mine" identity.** The connected `wallet` is the only identity: the handler resolves a `Player`
  by `ExternalPlayerId` and filters bets by `Bet.PlayerId`
  (`Bet` carries no raw wallet). `mineOnly` with no identifiable player returns an empty page while the
  public list still loads.
- **Caching.** The **public** page (player-independent facts) is cached in Redis for **5 minutes** per
  `(parametersId, pageNumber, pageSize)` (`CacheHelper.GenerateGamesHistoryCacheSettings`),
  with no invalidation (completed rounds are immutable). The caller's **own-bet columns are joined
  live** on every request and never cached under another player's key (the service clones cached rows
  before stamping own columns, so it never poisons a shared cache entry). Accepted trade: a
  just-completed round can lag on page 1 by up to the 5-min TTL; own columns are always current.
- **On-demand, no new table.** History is aggregated on demand from existing `Game` / `Bet` /
  `TransactionLog` / `Player` rows — **no new entity and no migration**. Read-only on the
  stateless API; no Consumers/typer change.
- **Vague errors.** The handler resolves the type via
  `IGameService.GetGameParametersById(parametersId)`. An unknown or deactivated type collapses to the same vague
  `ParameterNotFound` — existence and contract status are never revealed.
- **Verify = link-out only.** The per-row Verify button fetches the round's payload from the existing
  `GET /games/{id}/verification`, copies + downloads the JSON, and points the player at
  `public/verify.html`. There is no in-app FAIR panel or in-browser replay on the history page (the live
  FAIR tab keeps that); zero new verification logic.
- **Header menu.** The old header settings gear is folded into a **menu button** (gated by
  context) holding **Settings** (the customization modal) and **History** (+ the dev-only **Admin**
  shortcut). Audio (mute/volume) and the wallet cluster stay directly on the header. The menu opens on
  click and closes on an item click or an outside click (no hover). (`fair-stats-menu` later added
  **Stats** and **Fair** rows to this same menu — order **Settings · Stats · Fair · History** — both
  hidden when embedded; see `features/fair-stats-menu.md`.)
- **Empty state.** A type with zero completed rounds shows "No rounds yet".

## Key code touchpoints

**Backend (`monkify-backend`):**
- `SpaceStroker.Api/Controllers/GamesController.cs` — `GET {parametersId}/history` action.
- `SpaceStroker.Infrastructure/Handlers/Games/GetGamesHistory/` — request + handler (paging clamp,
  caller-identity resolution, empty short-circuit, vague `ParameterNotFound`).
- `SpaceStroker.Infrastructure/Services/Games/GameHistoryService.cs` (+ `IGameHistoryService`) —
  public-page aggregation + Redis cache + live own-columns join; `CachedGamesHistoryPage`.
- `SpaceStroker.Infrastructure/Contracts/Games/GameHistoryDto.cs` — row DTO (public facts + currency
  descriptor + nullable `My*` block).
- `SpaceStroker.Infrastructure/Services/Cache/CacheHelper.cs` — `GenerateGamesHistoryCacheSettings`
  (5-min TTL).
- Tests: `SpaceStroker.Tests/UnitTests/Services/GameHistoryServiceTests.cs`,
  `SpaceStroker.Tests/UnitTests/Handlers/GetGamesHistoryHandlerTests.cs`,
  `SpaceStroker.Tests.Integration/GamesHistoryFlowTests.cs` (happy path + mineOnly).

**Frontend (`monkify-front`):**
- `src/pages/GameHistoryPage.vue` — the history page (since #124: `.arcade-frame .arcade-frame--grey`,
  the grey panel material, **not** `.arcade-panel`; type dropdown, mine-only toggle,
  paginated table + mobile cards, per-row Verify link-out, request-sequence guard).
- `src/components/GameHeader.vue` — the header menu button (Settings + History + dev Admin), click-away
  close; `src/components/header/DevAdminButton.vue` renders as a menu row.
- `src/routes.ts` — the `GameHistory` route at `/game/history`.
- `src/App.vue` — loads player preferences for the `GameHistory` route once a wallet is connected.
- `src/services/mainService.ts` — `getGamesHistory(parametersId, opts)`; `src/types/index.ts` —
  `GameHistoryRow` + `PaginatedList<T>`.

# Feature — Per-game game-type stats

> Status: **shipped.** Authoritative spec: `../specs/game-type-stats.md`
> (merged via `feature/game-type-stats` on 2026-07-10).

## What it does

Players can open a **STATS** tab on the live `/game` screen to see casino-style all-time stats for the
game type they're playing: biggest pot ever split, biggest single payout, rounds played, total paid
out, and (for preset-choice types) the hottest choice — plus a "0% house cut" trust tile. Stats are
scoped to one game type (`GameParameters`) and aggregated across every round it has ever run.

## Core rules

- **Endpoint.** `GET /games/{parametersId}/stats` returns a `GameTypeStatsDto`. Token-gated by
  `X-Launch-Token` like the other game reads; operator-scoped.
- **On-demand, cached — no new table.** Stats are computed by aggregating the type's `Game` / `Bet`
  / `TransactionLog` rows on demand, then cached in Redis per type for ~120 s
  (`CacheHelper.GenerateGameStatsCacheSettings`). There is **no** persistent stats entity and **no
  migration** — this is a deliberate, documented reversal of the card's original maintained-counter
  idea, acceptable because the panel is lazy-loaded (the query fires only when a player opens the Stats
  tab, never on the lobby) and the cache absorbs repeat opens.
- **Which rounds count.** Only games that reached `RewardForWinnersCompleted` (a real winner was
  paid) count toward rounds played and money stats. Refunded/under-subscribed lotteries
  (`NotEnoughPlayersToStart`) and `GameEndedAbruptely` are excluded.
- **Money source.** `TotalPaidOut` and `BiggestSinglePayout` come from `TransactionLog` rows of type
  `Credit`. `BiggestPot` is reconstructed per completed round as `Σ Bet.Amount` (+
  `LotteryParameters.LotteryPot` base for lottery types), since `Game` has no persisted pot column.
- **Free bets excluded.** Bets with `Amount == 0` never contribute to pot, payout, total-paid, or the
  hottest-choice tally. A completed round consisting only of free bets still increments rounds played.
- **Single currency per type.** Because `PaymentMethod` is a per-`GameParameters` concern, a type is
  either all-`Standard` (USD) or all-`SolanaToken` (one registered token) — the DTO carries the
  currency descriptor (`PaymentMethod` + token symbol/decimals) and no cross-method summing ever
  happens. The client renders `Standard` amounts in the player's preferred display currency and
  `SolanaToken` amounts token-native (shared `useAmountFormatter`, same as the `> POT:` line).
- **Hottest choice = most-picked, preset types only.** For preset-choice types it is the single choice
  with the most paid bets (all-time), with an ordinal tiebreak. Free-type races return no hottest
  choice.
- **Empty state.** A type with zero completed reward rounds returns zeroed/null stats; the panel shows
  "No rounds yet".
- **Operator scoping / vague errors.** The handler resolves the type via
  `IGameService.GetGameParametersById(parametersId, operatorId)`, which filters by operator and
  requires an active contract. A cross-operator, expired, unknown, or deactivated type all collapse to
  the same vague `ParameterNotFound` — the type's existence and contract status are never revealed.
- **Type recreation resets stats.** The operator admin portal has no in-place edit (deactivate then
  create → a new `GameParameters` Id), so a recreated type starts its stats fresh; historical rounds
  of the old id are not carried over.
- **UI: one tabbed side panel.** The `/game` screen's right-docked panel now has **CHAT** and **STATS**
  tabs (`GameSidePanel`). It is collapsible and open by default. Both tab bodies stay mounted so chat
  keeps its live subscription while Stats is shown; the Stats tab fetches only on its first activation.

> **Update (in flight, `feature/side-panel-cabinet`).** The side panel was re-housed as a **diegetic
> arcade-cabinet module** to match the terminal (metal bezel + corner rivets + a recessed CRT screen
> with scanlines) instead of a translucent floating web box, and moved back on-palette — orange
> (`#ffa214`) active tab + labels on blue-grey chrome, with terminal green kept **only** inside the
> chat log where it reads as real screen text. The collapse control is folded into the header caret
> and, when collapsed, becomes a vertical metal **dock rail** (CHAT/STATS) that expands straight to a
> tab (replacing the old floating green "Hide" pill). A new header **maximize** control gives the panel
> a third state: it blows up into a **wide landscape CRT** (`w-[40vw]`/max-720, `h-50%`, kept to the
> upper band so its bottom clears the monkey) pinned to the **top-right play area**, where the maximize
> icon swaps to a **restore** icon; collapse drops maximize so the dock rail always restores to the
> docked size. In STATS, the "0% house cut" trust tile is
> **promoted to the top** as a badge and the readouts read as a hierarchy (muted Silkscreen labels
> over VT323 values, the biggest-pot value in orange). Presentation-only — no stats logic, endpoint,
> caching, or data shape changed. See INDEX "Feature currently in flight".

## Known issue (surfaced, not caused, by this feature)

The stats integration test exercised a **multi-winner payout** (two players betting the same winning
choice) for the first time and revealed that such a payout can be **double-credited under RabbitMQ
redelivery** — a pre-existing idempotency race in `RewardGameConsumer`, orthogonal to this
read-only feature (the stats correctly report whatever the ledger holds). Tracked for a separate fix.

## Key code touchpoints

**Backend (`monkify-backend`):**
- `ChimpOut.Api/Controllers/GamesController.cs` — `GET {parametersId}/stats` action.
- `ChimpOut.Infrastructure/Handlers/Games/GetGameTypeStats/` — request + handler (scoping + vague
  `ParameterNotFound`).
- `ChimpOut.Infrastructure/Services/Games/GameStatsService.cs` (+ `IGameStatsService`) —
  on-demand aggregation + Redis cache; `GameTypeStats` result model.
- `ChimpOut.Infrastructure/Contracts/Games/GameTypeStatsDto.cs` — DTO + `HottestChoiceDto`.
- `ChimpOut.Infrastructure/Services/Cache/CacheHelper.cs` — `GenerateGameStatsCacheSettings`.
- `ChimpOut.Infrastructure/Services/Games/GameService.cs` — `GetGameParametersById` now
  `.Include`s `OperatorToken` (for the currency descriptor).
- Tests: `ChimpOut.Tests/UnitTests/Services/GameStatsServiceTests.cs`,
  `ChimpOut.Tests/UnitTests/Handlers/GetGameTypeStatsHandlerTests.cs`,
  `ChimpOut.Tests.Integration/GameStatsFlowTests.cs`.

**Frontend (`monkify-front`):**
- `src/components/game/GameSidePanel.vue` — the right-docked CHAT/STATS tabbed panel.
- `src/components/game/GameStatsPanel.vue` — stats tiles (content-only; lazy-fetch via `active` prop).
- `src/components/game/GameChatPanel.vue` — now content-only (framing moved to the side panel).
- `src/hooks/useAmountFormatter.ts` — shared game-aware money formatter (reused by `GameTerminal`).
- `src/services/mainService.ts` — `getGameTypeStats(parametersId)`; `src/types/index.ts` —
  `GameTypeStats` / `HottestChoice`.
- `src/pages/GamePage.vue` — mounts `<game-side-panel />`.

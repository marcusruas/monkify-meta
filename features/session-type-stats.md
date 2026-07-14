# Feature — Per-game session-type stats

> Status: **shipped.** Authoritative spec: `../specs/session-type-stats.md`
> (merged via `feature/session-type-stats` on 2026-07-10).

## What it does

Players can open a **STATS** tab on the live `/game` screen to see casino-style all-time stats for the
game type they're playing: biggest pot ever split, biggest single payout, rounds played, total paid
out, and (for preset-choice types) the hottest choice — plus a "0% house cut" trust tile. Stats are
scoped to one session type (`SessionParameters`) and aggregated across every round it has ever run.

## Core rules

- **Endpoint.** `GET /sessions/{parametersId}/stats` returns a `SessionTypeStatsDto`. Token-gated by
  `X-Launch-Token` like the other session reads; operator-scoped.
- **On-demand, cached — no new table.** Stats are computed by aggregating the type's `Session` / `Bet`
  / `TransactionLog` rows on demand, then cached in Redis per type for ~120 s
  (`CacheHelper.GenerateSessionStatsCacheSettings`). There is **no** persistent stats entity and **no
  migration** — this is a deliberate, documented reversal of the card's original maintained-counter
  idea, acceptable because the panel is lazy-loaded (the query fires only when a player opens the Stats
  tab, never on the lobby) and the cache absorbs repeat opens.
- **Which rounds count.** Only sessions that reached `RewardForWinnersCompleted` (a real winner was
  paid) count toward rounds played and money stats. Refunded/under-subscribed lotteries
  (`NotEnoughPlayersToStart`) and `SessionEndedAbruptely` are excluded.
- **Money source.** `TotalPaidOut` and `BiggestSinglePayout` come from `TransactionLog` rows of type
  `Credit`. `BiggestPot` is reconstructed per completed round as `Σ Bet.Amount` (+
  `LotteryParameters.LotteryPot` base for lottery types), since `Session` has no persisted pot column.
- **Free bets excluded.** Bets with `Amount == 0` never contribute to pot, payout, total-paid, or the
  hottest-choice tally. A completed round consisting only of free bets still increments rounds played.
- **Single currency per type.** Because `PaymentMethod` is a per-`SessionParameters` concern, a type is
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
  `ISessionService.GetSessionParametersById(parametersId, operatorId)`, which filters by operator and
  requires an active contract. A cross-operator, expired, unknown, or deactivated type all collapse to
  the same vague `ParameterNotFound` — the type's existence and contract status are never revealed.
- **Type recreation resets stats.** The operator admin portal has no in-place edit (deactivate then
  create → a new `SessionParameters` Id), so a recreated type starts its stats fresh; historical rounds
  of the old id are not carried over.
- **UI: one tabbed side panel.** The `/game` screen's right-docked panel now has **CHAT** and **STATS**
  tabs (`GameSidePanel`). It is collapsible and open by default. Both tab bodies stay mounted so chat
  keeps its live subscription while Stats is shown; the Stats tab fetches only on its first activation.

## Known issue (surfaced, not caused, by this feature)

The stats integration test exercised a **multi-winner payout** (two players betting the same winning
choice) for the first time and revealed that such a payout can be **double-credited under RabbitMQ
redelivery** — a pre-existing idempotency race in `RewardSessionConsumer`, orthogonal to this
read-only feature (the stats correctly report whatever the ledger holds). Tracked for a separate fix.

## Key code touchpoints

**Backend (`monkify-backend`):**
- `Monkify.Api/Controllers/SessionsController.cs` — `GET {parametersId}/stats` action.
- `Monkify.Infrastructure/Handlers/Sessions/GetSessionTypeStats/` — request + handler (scoping + vague
  `ParameterNotFound`).
- `Monkify.Infrastructure/Services/Sessions/SessionStatsService.cs` (+ `ISessionStatsService`) —
  on-demand aggregation + Redis cache; `SessionTypeStats` result model.
- `Monkify.Infrastructure/Contracts/Sessions/SessionTypeStatsDto.cs` — DTO + `HottestChoiceDto`.
- `Monkify.Infrastructure/Services/Cache/CacheHelper.cs` — `GenerateSessionStatsCacheSettings`.
- `Monkify.Infrastructure/Services/Sessions/SessionService.cs` — `GetSessionParametersById` now
  `.Include`s `OperatorToken` (for the currency descriptor).
- Tests: `Monkify.Tests/UnitTests/Services/SessionStatsServiceTests.cs`,
  `Monkify.Tests/UnitTests/Handlers/GetSessionTypeStatsHandlerTests.cs`,
  `Monkify.Tests.Integration/SessionStatsFlowTests.cs`.

**Frontend (`monkify-front`):**
- `src/components/game/GameSidePanel.vue` — the right-docked CHAT/STATS tabbed panel.
- `src/components/game/GameStatsPanel.vue` — stats tiles (content-only; lazy-fetch via `active` prop).
- `src/components/game/GameChatPanel.vue` — now content-only (framing moved to the side panel).
- `src/hooks/useAmountFormatter.ts` — shared session-aware money formatter (reused by `GameTerminal`).
- `src/services/mainService.ts` — `getSessionTypeStats(parametersId)`; `src/types/index.ts` —
  `SessionTypeStats` / `HottestChoice`.
- `src/pages/GamePage.vue` — mounts `<game-side-panel />`.

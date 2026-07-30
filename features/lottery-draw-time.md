# Feature — Lottery draw time (UTC anchor)

> Status: **shipped.** Authoritative spec: `../specs/lottery-draw-time.md`
> (merged via `feature/lottery-draw-time` on 2026-07-30). Trello #132
> ([cGlkxiYo](https://trello.com/c/cGlkxiYo)).

## What it does

A lottery game type can pin its draw to a **fixed UTC time of day** instead of drawing one period
after whenever the previous round happened to be created. **Space Jackpot** now draws every day at
**22:00 UTC** and does not drift, no matter how long a round takes to run and settle.

Shipped alongside the fix for the bug that exposed the need: Space Jackpot was configured
`"LotteryFrequency": "24:00:00"`, and .NET's `TimeSpan` parser reads a leading component **above 23**
as **days** — so the "daily" jackpot was drawing every **24 days** while the lobby, the terminal and
the landing page all advertised a daily draw.

## Core rules

- **`LotteryParameters.DrawTimeUtc` is a nullable time of day, in UTC.** Null means the legacy
  rolling schedule (`StartDate = UtcNow + Frequency` at round creation). Every lottery that existed
  before this feature has `NULL` and behaves exactly as it did.
- **Anchor and period are both kept.** `Frequency` stays the period; the anchor pins the clock time.
  Draw slots are the fixed grid `{ epoch + DrawTimeUtc + k·Frequency }` where the epoch is
  `DateTime.MinValue` — **not** "today" — so the grid is identical across restarts, hosts and
  rollovers. 24 h + 22:00 ⇒ 22:00 UTC daily; 4 h + 22:00 ⇒ 02:00/06:00/10:00/14:00/18:00/22:00 UTC.
- **A round draws at the earliest slot at least `GeneralSettings:Games:MinimumLotteryDrawLeadSeconds`
  away** (default `600`). A slot closer than that is skipped for the next one, so a round never opens
  with a uselessly short betting window.
- **That lead must sit strictly between `MinimumWaitPeriodForBets` and the lottery's `Frequency`.**
  Below the wait period, `BetPlacedConsumer` returns before the under-subscribed refund branch and —
  because a lottery gets no other `BetPlacedEvent` — the round is **stuck forever holding real
  money**. At or above `Frequency`, every slot is "too close" and the lottery skips forever.
- **UTC only.** There is no per-lottery timezone. A DST jump can duplicate or skip a local wall-clock
  hour, which is unacceptable on a draw that moves money.
- **The configured schedule self-heals at startup.** Seeding is create-only by name, so a corrected
  `Frequency` or a newly-added `DrawTimeUtc` would otherwise never reach an environment that already
  has the row. On boot, `SpaceStroker.Consumers` patches **only** those two scheduling fields on
  config-named default types. `LotteryPot` and every other field stay create-only.
- **Open rounds are realigned at startup.** Any `WaitingBets` round of an **anchored** type whose
  `StartDate` is off its grid gets `StartDate` recomputed **and its Hangfire job rescheduled** — both,
  because the scheduled job is a lottery's only draw trigger. It corrects in **both** directions, so it
  also re-arms a draw missed while the host was down. Unanchored rounds are deliberately excluded:
  their "expected" time is `UtcNow + Frequency`, which differs on every boot, so correcting them would
  push the draw out forever. A round already on the grid is skipped, making the step idempotent.
- **Nothing cancels the previous Hangfire job.** It is harmless: `BetPlacedConsumer` ignores any game
  whose status is outside `GameAcceptingBets`, and `GameService.TryStartGame` is a conditional
  `UPDATE … WHERE "Status" = WaitingBets`, so only the first caller ever starts a round.
- **The anchor is config-only.** The admin portal has no draw-time input, so a lottery created through
  `/space-stroker/admin` cannot be anchored and keeps the rolling schedule.
- **There is no seed-time validation.** `GameParametersValidation`'s 1 min – 7 day bound still applies
  only to admin-created types. Because the self-heal makes config authoritative, a wrong value now
  overwrites previously-correct rows on every boot — a knowingly accepted trade. Write the anchor as
  `"22:00:00"`; anything at or above `"24:00:00"` binds to *days* and recreates the original bug.
- **Players see the anchor.** The lobby badge carries it on its own line (`LOTTERY / DAILY /
  22:00 UTC`) and the terminal reads `> FREQUENCY: 24 HOURS AT 22:00 UTC`. The `> EDSON WILL START
  TYPING IN hh:mm:ss` countdown needed no change — it already derives from `StartDate`.

## Key code touchpoints

**`monkify-backend`**

- `src/SpaceStroker.Domain/Games/Services/LotterySchedule.cs` — `NextDrawTime(nowUtc, frequency,
  drawTimeUtc, minimumLead)`. Pure and static: no DbContext, no ambient clock, no Hangfire. Uses
  **integer tick maths** — ticks since year 1 are ~6.4e17, past `double`'s exact-integer range
  (~9e15), so a `Math.Ceiling` quotient would silently land on the wrong slot.
- `src/SpaceStroker.Domain/Games/Entities/LotteryParameters.cs` — `DrawTimeUtc` (`TimeSpan?`).
- `src/SpaceStroker.Domain/Configs/Entities/OperatorSettings.cs` — `DefaultGameSettings.DrawTimeUtc`.
  `IsLottery` still keys off `LotteryPot` + `LotteryFrequency`, so the anchor stays optional.
- `src/SpaceStroker.Domain/Configs/Entities/GameSettings.cs` — `MinimumLotteryDrawLeadSeconds`.
- `src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` — inline mapping.
- `src/SpaceStroker.Infrastructure/Migrations/20260730201254_AddLotteryDrawTimeUtc.cs` — one nullable
  `interval` column.
- `src/SpaceStroker.Infrastructure/Services/Games/GameService.cs` — `CreateGame` computes `StartDate`
  from the grid (the single call site covering startup, under-subscription rollover and post-draw
  rollover); `CorrectScheduledLotteryDraws()` is the startup realignment.
- `src/SpaceStroker.Consumers/Program.cs` — the schedule self-heal inside
  `CreateDefaultGameParametersAsync`, then `CorrectScheduledLotteryDraws` **between** seeding and
  `OpenNewGames`. The order is load-bearing: slots must be recomputed from the healed values.
- `src/SpaceStroker.Infrastructure/Contracts/Games/GameParametersDto.cs` — `LotteryParametersDto.DrawTimeUtc`.
- `src/SpaceStroker.Common/spacestrokersettings.json` — Space Jackpot `"1.00:00:00"` + `"22:00:00"`;
  `GeneralSettings:Games:MinimumLotteryDrawLeadSeconds: 600`.

**`monkify-front`**

- `src/utils/lotteryFrequency.ts` — `formatDrawTime`, plus an optional `drawTimeUtc` argument on
  `formatLotteryFrequency` that appends ` AT 22:00 UTC`. The period wording is deliberately **not**
  collapsed to `DAILY` when anchored: the same helper feeds the quick tour's "Edson types every
  {frequency}", where "every DAILY" would be broken copy.
- `src/components/game-selection/GameTypeCard.vue` — the anchor as an optional third badge line.
- `src/components/game/GameTerminal.vue` — the `> FREQUENCY:` line and the `NEXT DRAW:` labels.
- `src/types/index.ts` — `LotteryParameters.drawTimeUtc`.

**Tests**

- `SpaceStroker.Tests/UnitTests/Domain/Games/LotteryScheduleTests.cs` — 14 cases pinning the grid.
- `SpaceStroker.Tests/UnitTests/Contracts/GameParametersDtoTests.cs` — anchored vs unanchored DTO.
- `SpaceStroker.Tests.Integration/LotteryDrawTimeFlowTests.cs` — the anchored round lands on the grid
  and settles; a 24-day round is pulled back onto the grid with its bets untouched, idempotently.
- `SpaceStroker.Tests.Integration/DefaultGameSeedingTests.cs` — Space Jackpot seeds as 1 day + 22:00.

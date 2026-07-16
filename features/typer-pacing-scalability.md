# Feature — Typer pacing scalability

> Status: **shipped.** Authoritative spec: `../specs/typer-pacing-scalability.md`
> (merged via `feature/typer-pacing-scalability` on 2026-06-29).

## What it does

Keeps game wall-clock duration within agreed statistical bounds across choice length, charset
size, bet count, and server hardware — **without** changing RNG, seed derivation, character order,
or winning outcomes. Players see consistent race and lottery run lengths; ops get predictable game
flow. The frontend `DisplaySpeed` ramp and `GameTyper` refresh cadence stay in sync with the new
pacing.

## Core rules

- **Pacing-only:** same game seed + bets → same character stream and winner; only wall-clock
  timing changes.
- **Duration targets (wall-clock):**
  - **Race** (free-type + preset): mean **8–18 s**, **p95 ≤ 30 s**
  - **Lottery draw** (typer run, not Hangfire schedule): mean **10–30 s**, **p95 ≤ 45 s**
- **No minimum duration** — instant natural wins are allowed.
- **Time-budget pacing:** `ChimpOutTyper` schedules ms-per-character from mode targets and a
  search-space horizon (`min(expectedCharsToWin, MaxTyperCharacters)`). A `Stopwatch` drives delays:
  sleep only when **ahead** of schedule (≥ 4 ms surplus); never slow further when behind →
  hardware- and load-stable.
- **`DisplaySpeed` ramp (1→2→3):** tied to elapsed fractions of target mean (⅓ and ⅔), not character
  counts. Visual only; does not change the character emission rate.
- **Fallback winner:** when `CharactersTypedCount` reaches `_maxCharacters` without a natural
  sliding-window match, the typer picks the winner with `new Random(GameSeed)` over
  ordinally-sorted choices. Trigger is a **fixed character count**, so same seed + bets → same
  stream **and** same winner on any host. Exposed as `WinnerDecidedByFallback`.
- **Config:** `GameSettings` in `chimpoutsettings.json` — race/lottery mean and p95 targets,
  `MaxTyperCharacters` (default 5,000,000). No admin UI.
- **Production metrics gate:** `ProductionGames` tests (one class per default game parameter,
  2–400 bet sweep) run via `scripts/run_production_game_tests.bat`; excluded from
  `scripts/run_tests.bat`. xUnit collection parallelization disabled (`xunit.runner.json`).

## Key code touchpoints

- `ChimpOut.Domain/Games/Services/ChimpOutTyper.cs` — `CalculatePacing`, `ApplyPacingDelay`,
  `DecideFallbackWinner`, `UpdateTypingSpeed`
- `ChimpOut.Domain/Configs/Entities/GameSettings.cs` — pacing target properties
- `ChimpOut.Common/chimpoutsettings.json` — default pacing values
- `ChimpOut.Infrastructure/Services/Games/GameService.cs` — passes settings + lottery flag
- `ChimpOut.Tests/UnitTests/Domain/ProductionGameTests.cs` — per-game Monte Carlo metrics
- `ChimpOut.Tests/UnitTests/Domain/Games/ChimpOutTyperTests.cs` — reproducibility + fallback
- `monkify-front/src/components/game/GameTyper.vue` — refresh 50/35/25 ms by `DisplaySpeed`

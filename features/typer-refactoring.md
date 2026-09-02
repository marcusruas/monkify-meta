# Feature — Typer Refactoring (fast rounds, bet-derived pool, persisted verification inputs)

> Status: **shipped.** Authoritative spec: [`../specs/typer-refactoring.md`](../specs/typer-refactoring.md)
> (merged via `feature/typer-refactoring` on 2026-09-02, Trello
> [#181](https://trello.com/c/Xim01Mvw/181-space-stroker-typer-refactoring)).

## What it does

A round finishes in **~10 s average, 15 s scheduled max** (was ~21 s deployed), free-text races rely
**less on the fallback draw** to decide the winner, and every finished round **persists the exact
character pool and cap it ran with** so provably-fair verification of any historical round keeps
reproducing forever. A fallback-bound worst case costs ~500K HMAC characters and a few hundred
SignalR messages instead of 5M and ~25,000.

## Core rules

- **Bet-derived pool, races only.** A free-text **race** builds its character pool from the union of
  the placed bets' choices' characters, topped up to the existing ⌊alphabet/5⌋ diversity floor in
  alphabet order, sorted. Invariant: the pool always contains every character of every placed bet's
  choice. Preset races are unchanged (pool from preset chars, as before). ⚠️ **Lottery pools never
  shrink** — full alphabet, deliberately: a smaller lottery pool raises how often *anyone* wins,
  which is pot-payout/money behavior, not pacing (`SpaceStrokerTyper.SetCharactersOnTyper` gates on
  `IsLottery`).
- **Pacing targets** (config, code defaults): race **and** lottery draw run mean 8–12 s, p95 15 s
  (× the 0.9 safety ⇒ 13.5 s schedule ceiling). `MaxTyperCharacters` ceiling **500,000** (was 5M).
  ⚠️ For small pools the time-derived cap (`capByTime`) binds **below** 500K — persisted caps like
  358,723 / 176,948 / 79,414 are expected, not bugs.
- **Fallback stays count-triggered and deterministic** — `CharactersTypedCount >= MaxCharacters`,
  never wall-clock, so the same seed + bets reproduce the same stream and winner on any host.
- **Measured fallback share** (2-bet Cosmic Sprint, local, 83 rounds): **~43%** overall — pool 8–9:
  ~30%, pool 10: ~43%, pool 11: ~60% (two random 6-letter words usually union to 10–11 chars, whose
  expected win point 500K–886K sits at/past the cap). Was ~100% before this card. Owner accepted;
  the config-only lever for a lower share is raising `MaxTyperCharacters` (1M ⇒ pool 10 ≈ 14%).
- **Time-based terminal flush.** `GameService.RunGame` flushes the terminal buffer every
  **50/40/30 ms keyed to the current `DisplaySpeed`** (config: `TerminalFlushSpeed1/2/3Ms`; the old
  `TerminalBatchLimit` is deleted). Variable batch sizes; **every character is still delivered in
  order** — the frontend `N AWAY!` row replays the full stream. The final drain and the
  fallback-reveal batch (winning word typed last) are unchanged.
- **Persisted verification inputs.** When a run finishes, `Game.CharacterPool` and
  `Game.MaxCharacters` are written in the **same `SaveChanges`** as `WinningChoice`/`CharactersTyped`
  (migration `TyperRunSnapshot`, both nullable). `GET /games/{id}/verification` publishes:
  - not yet run → the live derivation (what the run *will* use);
  - finished → the **persisted** values, verbatim;
  - finished with NULL columns (pre-migration "legacy" round) → the **pinned old derivation**
    (full alphabet / old cap math with constants race 8/18/30, lottery 10/30/45, ceiling 5M,
    safety 0.9) — so historical recomputes never break, and future settings changes can never shift
    a settled round's published inputs. `WinnerDecidedByFallback` derives against the same published
    cap; NULL `CharactersTyped` still means *unknown*, never *false*.
- **No frontend change.** Same SignalR contract; the FAIR panel / `verify.html` / `provablyFair.ts`
  consume the published `characterPool`/`maxCharacters` verbatim (the KEEP-IN-SYNC triad does not
  encode pool derivation).
- **Prod config:** nothing in `deploy/secrets.*.json` names any `GeneralSettings:Games:*` key — the
  values ship in the image via the committed `spacestrokersettings.json`. To tune pacing per
  environment later, use the deploy `appConfig` block (injected wholesale as env vars, **no
  Terraform secret binding needed**); never add these to the `secrets` block.

## Key code touchpoints

- `monkify-backend/src/SpaceStroker.Domain/Games/Services/SpaceStrokerTyper.cs` — pool branch,
  `DEFAULT_MAX_CHARACTERS`
- `monkify-backend/src/SpaceStroker.Domain/Configs/Entities/GameSettings.cs` — retuned defaults +
  flush-interval keys
- `monkify-backend/src/SpaceStroker.Infrastructure/Services/Games/GameService.cs` — time-based flush
  (`RunGame`), snapshot persist (`UpdateGameStatus`)
- `monkify-backend/src/SpaceStroker.Infrastructure/Handlers/Games/GetGameVerification/GetGameVerificationHandler.cs`
  — persisted-first sourcing + `LegacyCharacterPool`/`LegacyMaxCharacters`
- `monkify-backend/src/SpaceStroker.Infrastructure/Migrations/20260901042816_TyperRunSnapshot.cs`
- Tests: `SpaceStrokerTyperTests` (pool/cap/fallback-share), `GameServiceTests` (flush + persist),
  `GetGameVerificationHandlerTests` (sourcing + legacy), integration
  `TyperRefactoringFlowTests.HappyPath_LowBetFreeTextRace_ResolvesNaturallyWithBetDerivedPool`
- ⚠️ Test-infra rule that shipped with this: any integration test seeding a funded
  `OrbitEnrollment` must quiesce it (`IAsyncLifetime.DisposeAsync =>
  _fixture.DisableAllOrbitEnrollmentsAsync()`) — fast rounds turn a leftover enrollment into
  continuous background churn that flakes unrelated suites.

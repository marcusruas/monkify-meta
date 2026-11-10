# Monkey Duel Quick Flip, Round 3 (seed-settled VS HOUSE) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** VS HOUSE on Monkey Duel settles from the round seed the instant the bet lands (no typing), and the screen shows only the $STROKE coin between the two fixed words PLAYER (heads, you) and STROKE (tails, the house), every round done in about 2 s.

**Architecture:** Backend: `SpaceStrokerTyper` detects a House duel with a drawn house word and decides the winner at construction through the existing fallback draw (`ProvablyFair.FallbackIndex` over the ordinal-sorted {creator word, house word}), with `MaxCharacters = 0`; `GameService.RunGame` then types nothing and emits no stream. The verifier (`GetGameVerificationHandler`, `verify.html`, `provablyFair.ts`) already reproduces a capped round from `MaxCharacters`, so nothing new is published. Front: the VS HOUSE screen mounts a new pure `DuelFlipArena.vue` (PLAYER | 3D-flipping `DuelCoin` + 2 s clock | STROKE); the bet word is the fixed `player`; the proof strip, header words and outcome sentences go. Practice, guide, LP and the smoke follow. Builds on the round-1/2 plan `2026-11-10-duel-quick-flip.md` (already implemented on `feature/duel-quick-flip`).

**Tech Stack:** .NET 8 (`SpaceStroker.Domain`, `SpaceStroker.Infrastructure`, xUnit + Shouldly, Testcontainers), Vue 3 `<script setup>` + TypeScript, Playwright (`E:/Monkify/.promo-work/wordlines/node_modules/playwright`, `channel: 'msedge'`).

## Global Constraints

- Worktrees only: `E:/Monkify/.worktrees/duel-quick-flip/{monkify-backend,monkify-front,monkify-lp,monkify-meta}` on `feature/duel-quick-flip` (LP off `master`). Hubs stay on `main`. Push after every commit.
- Card #219 is now Level **Feature**: a happy-path integration test in `SpaceStroker.Tests.Integration` is mandatory.
- Backend: EF inline, no migration (no schema change), mock secrets only, `scripts/run_tests.bat` for units (never the raw full suite), `scripts/run_integration_tests.bat` for the Testcontainers suite.
- The face is the server's: `winnerChoice` decides heads/tails, never a timer. Minimum spin 1400 ms, land 450 ms, clock bar 2000 ms.
- Coin art: heads `monk-coin-main.png`, tails `monk-coin-tails.png`. Never `monk-coin.png`. The spin GIF stays only in `DuelVsSplash` (PvP).
- Player copy: no em dashes, no litotes, no figures that go stale. The only two words on the arena are PLAYER and STROKE.
- Front gates: `yarn lint` + `yarn build` (Docker), `grep -rn "monk-coin.png" src/` empty, Playwright smoke green on the local stack.
- Prettier `endOfLine: lf`: write LF files.

---

### Task 1: Typer settles a House duel from the seed (backend, TDD)

**Files:**
- Modify: `src/SpaceStroker.Domain/Games/Services/SpaceStrokerTyper.cs`
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/GameService.cs` (RunGame final batch)
- Test: `src/SpaceStroker.Tests/UnitTests/Domain/Games/SpaceStrokerTyperTests.cs`

**Interfaces:**
- Produces: `SpaceStrokerTyper.IsHouseDuel : bool` (true = settled at construction; `HasWinners` true, `CharactersTypedCount` 0, `MaxCharacters` 0, `WinnerDecidedByFallback` true, `FirstChoiceTyped` = the fallback draw).

- [ ] **Step 1: Failing tests** (append inside the "Monkey Duel against the house" region)

```csharp
[Fact]
public void HouseDuel_SettlesFromTheSeedAtConstruction_NothingIsTyped()
{
    var game = HouseDuelGame("player", "meteor");
    var typer = new SpaceStrokerTyper(game, new GameSettings());

    typer.IsHouseDuel.ShouldBeTrue();
    typer.HasWinners.ShouldBeTrue();
    typer.WinnerDecidedByFallback.ShouldBeTrue();
    typer.CharactersTypedCount.ShouldBe(0);
    typer.MaxCharacters.ShouldBe(0);
    typer.TargetMsPerChar.ShouldBe(0);

    var ordered = new[] { "meteor", "player" };
    var expected = ordered[ProvablyFair.FallbackIndex(game.ServerSeed, OraclePrefix(game), 2)];
    typer.FirstChoiceTyped.ShouldBe(expected);
    typer.NumberOfWinners.ShouldBe(expected == "player" ? 1 : 0);
}

[Fact]
public void HouseDuel_IsAFairCoin_AcrossSeeds()
{
    var random = new Random(219);
    int playerWins = 0;
    const int rounds = 4000;
    for (int i = 0; i < rounds; i++)
    {
        var seed = string.Concat(Enumerable.Range(0, 64).Select(_ => "0123456789abcdef"[random.Next(16)]));
        var game = HouseDuelGame("player", "meteor", creatorSeed: seed);
        if (new SpaceStrokerTyper(game, new GameSettings()).FirstChoiceTyped == "player") playerWins++;
    }
    (playerWins / (double)rounds).ShouldBeInRange(0.46, 0.54);
}

[Fact]
public void OpenRoomDuel_StillRacesItsStream()
{
    var game = HouseDuelGame("quirky", null, kind: DuelKind.OpenRoom);
    game.Bets.Add(new Bet(game.Id, Faker.Random.String2(64, "0123456789abcdef"), Guid.NewGuid(), "meteor", 0.1m));
    var typer = new SpaceStrokerTyper(game, InstantPacing());

    typer.IsHouseDuel.ShouldBeFalse();
    typer.HasWinners.ShouldBeFalse();
    typer.MaxCharacters.ShouldBeGreaterThan(0);
}
```

Also: `HouseDuel_WithoutAHouseWordYet_RacesOnlyTheBets` adds `typer.IsHouseDuel.ShouldBeFalse(); typer.HasWinners.ShouldBeFalse();`. `ADuel_PacesToItsOwnWindow_WithTheRaceCapRatioOrBetter` switches to `HouseDuelGame("quirky", null, kind: DuelKind.OpenRoom)` (a House duel no longer paces). `HouseDuel_KeystreamPrefix_IsGameIdPlusTheCreatorSeedOnly` asserts the draw instead of typing 50 characters: `typer.FirstChoiceTyped.ShouldBe(new[]{"meteor","quirky"}[ProvablyFair.FallbackIndex(game.ServerSeed, ProvablyFair.BuildPrefix(game.Id, new[]{ game.Bets.Single().Seed }), 2)])`.

- [ ] **Step 2: Run** `scripts\run_tests.bat --filter "FullyQualifiedName~SpaceStrokerTyperTests"` → the three new tests FAIL (`IsHouseDuel` does not exist).

- [ ] **Step 3: Implement** in `SpaceStrokerTyper`:

```csharp
/// <summary>
/// #219 round 3 (specs/duel-quick-flip.md §6): a House duel with a drawn house word is SETTLED FROM THE SEED AT
/// CONSTRUCTION. Nothing is typed: the winner is the same fallback draw a capped round uses
/// (ProvablyFair.FallbackIndex over the ordinal-sorted {creator word, house word}), published with MaxCharacters = 0,
/// so GetGameVerificationHandler / verify.html / provablyFair.ts reproduce it with no new field. An open room (PvP)
/// still races its stream.
/// </summary>
public bool IsHouseDuel { get; }
```
constructor: `IsHouseDuel = IsDuel && game.HasDrawnHouseWord;` right after `IsDuel`, and after `CalculatePacing();` → `if (IsHouseDuel) SettleFromTheSeed();`

```csharp
private void SettleFromTheSeed()
{
    // 0 typed >= 0 cap reads as "decided by the fallback" everywhere (GetGameVerificationHandler derives
    // WinnerDecidedByFallback from CharactersTyped >= MaxCharacters), which is exactly what happened.
    MaxCharacters = 0;
    _targetMsPerChar = 0;
    DecideFallbackWinner();
}
```

In `GameService.RunGame`, the final "type the winning choice" batch gets `&& !monkey.IsHouseDuel` with the comment: a seed-settled House duel typed nothing, so nothing is shown as typed (the coin is the whole result on screen).

- [ ] **Step 4: Run** the typer tests → PASS. Then `scripts\run_tests.bat` (whole fast suite) → PASS.
- [ ] **Step 5: Commit** `feat(duel): settle a House duel from the seed at the pull, no typing (#219 round 3)` and push.

---

### Task 2: Integration test (mandatory happy path) + coverage test adjustment

**Files:**
- Modify: `src/SpaceStroker.Tests.Integration/MonkeyDuelFlowTests.cs`
- Modify: `src/SpaceStroker.Tests.Integration/MonkeyDuelCoverageTests.cs` (step 3 of `SharedExposure_AHyperRollRoundBlocksAHouseDuel_AndTheReverse`)

- [ ] **Step 1: New test** in `MonkeyDuelFlowTests`:

```csharp
/// <summary>
/// #219 round 3 — THE MANDATORY HAPPY PATH of the seed-settled flip: the front bets the fixed word "player"; the round
/// settles the instant it runs (nothing typed, MaxCharacters 0), the winner is the fallback draw over
/// {house word, "player"} from the one-bet prefix, the verifier publishes it as fallback-decided, and the money
/// path is unchanged (a win pays stake × (2 − rate) once; a loss pays nobody).
/// </summary>
[Fact]
public async Task HouseDuel_SettlesFromTheSeedAtThePull_NothingIsTyped()
{
    var parameters = await _fixture.GetParameterAsync(IntegrationGameParameters.MonkeyDuel);
    var rate = _fixture.CommissionPercentage / 100m;
    const decimal stake = 1m;
    const string playerWord = "player";
    IntegrationGameParameters.DuelWords.ShouldNotContain(playerWord);
    var wallet = $"duel-flip-{Guid.NewGuid():N}";

    var round = await _fixture.OpenDuelRoomAsync(parameters.Id, DuelKind.House);
    var bet = PaidBet(playerWord, wallet, stake);
    await _fixture.PlaceBetAsync(round.GameId, bet);

    var game = await _fixture.WaitForStatusAsync(round.GameId, GameStatus.RewardForWinnersCompleted, Timeout);
    game.HouseWord.ShouldNotBeNullOrWhiteSpace();
    game.CharactersTyped.ShouldBe(0, "a seed-settled House duel types nothing");
    game.MaxCharacters.ShouldBe(0);
    (game.EndDate!.Value - game.StartDate!.Value).TotalSeconds.ShouldBeLessThan(1.5, "the run itself is instant");

    var ordered = new[] { playerWord, game.HouseWord! }.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    var prefix = ProvablyFair.BuildPrefix(round.GameId, new[] { bet.Seed! });
    game.WinningChoice.ShouldBe(ordered[ProvablyFair.FallbackIndex(game.ServerSeed!, prefix, 2)]);

    var verification = await _fixture.GetGameVerificationAsync(round.GameId);
    verification.WinnerDecidedByFallback.ShouldBe(true);
    verification.MaxCharacters.ShouldBe(0);
    verification.CharactersTyped.ShouldBe(0);
    verification.OrderedChoices.ShouldBe(ordered);
    verification.HouseWord.ShouldBe(game.HouseWord);

    var placed = (await _fixture.GetBetsAsync(round.GameId)).ShouldHaveSingleItem();
    var logs = await _fixture.GetTransactionLogsAsync(new[] { placed.Id });
    logs.Count(x => x.Type == TransactionLogType.Debit).ShouldBe(1);
    if (game.WinningChoice == playerWord)
    {
        placed.Status.ShouldBe(BetStatus.Rewarded);
        var expectedPayout = Math.Round(stake * (2m - rate), _fixture.TokenDecimals, MidpointRounding.ToZero);
        logs.Where(x => x.Type == TransactionLogType.Credit).Select(x => x.Amount).ShouldBe(new[] { expectedPayout });
    }
    else
    {
        placed.Status.ShouldBe(BetStatus.Made);
        logs.ShouldNotContain(x => x.Type == TransactionLogType.Credit);
    }
    _fixture.LogSink.GetErrorMessages().ShouldBeEmpty();
}
```

- [ ] **Step 2: Coverage test.** In `SharedExposure_…`, steps 3–4 become: wait for the duel to settle, then open the next Hyper Roll board and assert its availability is TRUE. Doc comment: a House duel settles from the seed at the pull (#219 round 3), so there is no in-flight window to observe; the duel side of the shared book is pinned by `HouseEdgeCoverageServiceTests` (an `InProgress` House duel row in the book).
- [ ] **Step 3: Run** `scripts\run_integration_tests.bat --filter "FullyQualifiedName~MonkeyDuel"` → PASS (Docker running).
- [ ] **Step 4: Commit** `test(duel): seed-settled House duel happy path; shared-exposure test without the in-flight duel window` and push.

---

### Task 3: `DuelCoin.vue` becomes a 3D flip between the two real faces

**Files:**
- Create: `src/constants/duelCoin.ts`
- Modify: `src/components/game/DuelCoin.vue` (rewrite)

**Interfaces:**
- Produces: `DUEL_COIN_MIN_SPIN_MS = 1400`, `DUEL_COIN_LAND_MS = 450`, `DUEL_COIN_ROUND_MS = 2000`; `DuelCoin` props `{ phase, face, roundKey?, mobile?, reducedMotion? }` (no `stamp`); DOM `data-coin-phase`, `data-coin-face` kept for the smoke.

- [ ] Step 1: constants file; Step 2: rewrite the component (faces `.duel-coin__face--heads/--tails` with `backface-visibility: hidden`, tails pre-rotated 180°, `.duel-coin__disc--spin` 0.28 s infinite rotateY, `--land-heads`/`--land-tails` 0.45 s `both`, `--tails-up`; reduced motion = `?` disc while spinning and an instant face). Step 3: `yarn lint` (junctioned `node_modules`). Step 4: commit `feat(duel): the coin flips in 3D between the heads and tails frames`.

---

### Task 4: `DuelFlipArena.vue` + `GameTerminal.vue` VS HOUSE screen

**Files:**
- Create: `src/components/game/DuelFlipArena.vue` (pure view: PLAYER side, coin + 2 s clock, STROKE side; winner glow / loser dim once landed)
- Modify: `src/components/game/GameTerminal.vue` (house idle + race blocks, `duelShowResult` for House rounds = `status > InProgress && !!winnerChoice`, result lines `HEADS. PLAYER WINS.` / `TAILS. STROKE TAKES IT.`, header `> FLIP · PLAYER vs STROKE`, prompts, `raceFxTrigger` guard, constants 1400/450, styles + mode-intro rules for the arena)
- Modify: `src/utils/duelQuickWord.ts` (`DUEL_HOUSE_PLAYER_WORD = 'player'`), `src/components/game/DuelConsole.vue` (`pickForHouse` bets the fixed word)
- Modify: `scripts/smoke-duel-coin.mjs` (new copy, spin→landed timing ≤ 2600 ms logged and asserted)

- [ ] Step 1: update the smoke first (fails against the deployed build: the old copy); Step 2: arena + terminal + console; Step 3: `yarn lint`; Step 4: commit `feat(duel): VS HOUSE is the coin between PLAYER and STROKE, settled in 2 s` and push.

---

### Task 5: Practice duel, guide copy, LP blurb

**Files:**
- Modify: `src/constants/practiceGame.ts` (`PRACTICE_DUEL_TIMINGS` → `{0, 0, 1400, 450}`, `PRACTICE_DUEL_HOUSE_LABEL = 'STROKE'`, four reworded bubbles), `src/hooks/usePracticeDuel.ts` (header beats; pull bets `player`), `src/components/practice/PracticeDuelScreen.vue` (arena, coin lands at `Ended`, result at `RewardForWinnersCompleted`)
- Modify: `src/components/guide/sections/GuideCoinflip.vue` (intro, steps 2/4/5, closing line, tip)
- Modify: `monkify-lp/src/components/GameModes.vue` (blurb)

- [ ] Commit `feat(duel): practice, guide and LP follow the seed-settled flip` (front) and `Monkey Duel blurb: PLAYER vs STROKE, two-second flip` (LP), push both.

---

### Task 6: Spec, INDEX, card

- `specs/duel-quick-flip.md`: round-3 addendum (decisions 14–17, §6 backend, Level Feature, tests, ACs 16–19, file checklist), header + revision log. `INDEX.md` row + in-flight entry. Trello #219: description (Level Feature, backend impact, round-3 decisions, repos), ACs 16–19, worktree comment for `monkify-backend`.

---

### Task 7: Deploy + verify (implement-feature Steps 9b–11)

- Backend in place: `docker compose -p monkify-backend build api consumers && docker compose -p monkify-backend up -d --no-deps api consumers` from the backend worktree (no migration; volumes kept).
- Front: dev server from the worktree (junction) against `localhost:6001` → smoke + captures → re-crop `coinflip-coin.png` (`crop-guide.mjs`) → commit → remove junction, copy hub `yarn.lock`, `docker build -t monkify-front -f dockerfile .`, `docker rm -f monkify-front && docker run -d --name monkify-front --network monkify-backend_spacestroker-network -p 6003:80 monkify-front` → smoke on :6003 → update `.worktrees/.deployed-stack.json`.
- Compare links for backend, front, lp, meta; hand-off; card stays in Specs created until the owner's go.

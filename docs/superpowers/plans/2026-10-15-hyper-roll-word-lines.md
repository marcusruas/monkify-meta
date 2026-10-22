# Hyper Roll Word Lines Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rework Hyper Roll in place into Word Lines — one provably-fair draw over a published 13-row outcome table, a server-built 5×3 grid that shows that row on up to 10 paylines, every spin paid `stake × row pay` (0.3×–10×), 98.00% RTP.

**Architecture:** The 13 outcome rows become the round's `GameBoardChoices` (Choice = key), so the existing weighted draw, verification and coverage pricing work unchanged. `Game.BoardMode = "LINES"` discriminates new rounds from legacy word rounds everywhere. A pure Domain generator (`WordLinesGrid`) builds and validates the grid in the Ended arm; settlement rewards every bet from the drawn row.

**Tech Stack:** .NET 8 (EF Core/Postgres, MassTransit, SignalR, xUnit + Shouldly + Moq, Testcontainers), Vue 3 + TS (no test runner — gate = `yarn lint` + `yarn build` + headless boot), Astro LP.

**Authoritative design:** `monkify-meta/specs/hyper-roll-word-lines.md` (cited below as "spec §x"). Line numbers in the spec are approximate — locate by symbol.

## Global Constraints

- Work only in `E:\Monkify\.worktrees\hyper-roll-word-lines\<repo>` on `feature/hyper-roll-word-lines`; never touch the hubs.
- Backend unit tests: `scripts\run_tests.bat` (optionally `--filter` appended). Never a bare `dotnet test` over the whole solution.
- Integration tests: `scripts\run_integration_tests.bat` (Docker).
- EF mapping inline in `SpaceStrokerDbContext.OnModelCreating`; migration `HyperRollWordLines` created with `dotnet ef migrations add` from `src/SpaceStroker.Infrastructure` with `--startup-project ../SpaceStroker.Consumers`; never hand-edit the snapshot.
- `DbContext` is NoTracking by default — mutate via `ExecuteUpdate` or `context.Update()`.
- Outcome table config string (LOCKED): `bad:0.3:436574;double:6:4633;down:0.5:70000;echo:5.6:9266;edson:8:2317;five:2.5:65635;four:1.4:100382;full:10:1544;spacefive:5:16988;spacefour:2.8:27026;three:0.5:200000;twolines:3:27026;wild:1.4:38609`
- Constants: `WordLinesMode = "LINES"`, `SpinChoice = "spin"`, filler `"QXZJVK"`, repeat guard 20 rounds, max pay 10, RTP 98.0, tolerance 0.25.
- Vague refusals only ("Mode is under maintenance. Please try again later." / existing vague failures).
- Legacy rounds (`BoardMode != "LINES"`) must keep settling and verifying exactly as today.
- Ops-visible events (generator fallback, cutover sweep count) log at **Warning**.
- Mock secrets only. CRLF files: don't bulk-`sed` backend `.cs` files.
- Commits end with `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`; push after committing.

## Fixed wire contracts (backend ⇄ front)

All JSON camelCase.

```ts
type OutcomeRow   = { key: string; pay: number; weight: number }          // draw order = ordinal key sort
type WordLinesHit = { kind: 'line' | 'down'; index: number; word: string; shown: string; space: boolean }
// line: index 1..10 ; down: index 0..4 (reel). word lowercase; shown lowercase cells, may contain '?'
// grid: 15 chars, row-major, top row first, uppercase A–Z and '?'
GameParametersDto  += outcomes: OutcomeRow[]            // Hyper Roll only, else []; rtpPercent = 98; modes = []; presetChoices = [] for Hyper Roll
ActiveGameDto      += wordLinesGrid: string | null, wordLinesHits: WordLinesHit[] | null ; boardMode 'LINES'; boardChoices = 13 rows {choice,multiplier,weight,...}
GameResult (SignalR Ended) += outcomeKey?: string, outcomePay?: number, grid?: string, hits?: WordLinesHit[]
GameVerificationDto += wordLinesGrid, wordLinesHits (boardMode 'LINES')
GameHistory row     += outcomeKey?: string, outcomePay?: number ; won = payout > stake for LINES
POST /games/{type}/boards : mode ignored ; POST .../reroll : removed (404)
POST bets / availability / orbit wagers : choice optional for Hyper Roll (server stores "spin")
```

---

## Backend (`monkify-backend`)

### Task B1: Outcome table rules

**Files:**
- Create: `src/SpaceStroker.Domain/Games/ValueObjects/HouseEdgeOutcome.cs`
- Modify: `src/SpaceStroker.Domain/Games/Services/HouseEdgeRules.cs`
- Modify: `src/SpaceStroker.Domain/Games/Entities/GameParameters.cs` (add `OutcomeTable`)
- Test: `src/SpaceStroker.Tests/UnitTests/Domain/Games/HouseEdgeRulesTests.cs`, new `WordLinesOddsTests.cs`

**Interfaces — Produces:**
```csharp
public readonly record struct HouseEdgeOutcome(string Key, decimal Pay, int Weight);
// HouseEdgeRules
public const string WordLinesMode = "LINES";
public const string SpinChoice = "spin";
public const decimal MinOutcomePay = 0.1m;
public const int OutcomeTotalWeight = 1_000_000;
public const int WordLinesMinLength = 3, WordLinesMaxLength = 5;
public const int WordLinesMinWordsPerTier = 50, WordLinesMinSpaceWordsPerTier = 5;
public static readonly IReadOnlyList<string> OutcomeKeys; // bad double down echo edson five four full spacefive spacefour three twolines wild
public static bool IsWordLines(string? boardMode);           // == "LINES"
public static IReadOnlyList<HouseEdgeOutcome> ParseOutcomeTable(string? table);   // FormatException on bad shape; empty for null/blank
public static string FormatOutcomeTable(IEnumerable<HouseEdgeOutcome> rows);       // "key:pay:weight;…" invariant, pay "0.##"
public static decimal RtpPercentOf(IReadOnlyList<HouseEdgeOutcome> rows);          // Σ w·pay / 10,000
public static HouseEdgeValidation ValidateOutcomeTable(IReadOnlyList<HouseEdgeOutcome> rows, decimal rtpPercent);
public static HouseEdgeValidation ValidateWordLinesPool(IReadOnlyCollection<PresetChoice> active, IReadOnlyCollection<string> blocked);
// Validate(parameters, pool): when parameters.OutcomeTable is non-blank → parse+ValidateOutcomeTable(rows, parameters.RtpPercent ?? 0)
//   + stake checks (MaximumStake present, ≥ RequiredAmount, RequiredAmount ≥ rent floor) + ValidateWordLinesPool(pool, blocked: empty)
//   — blocked list is not in the DB; Consumers validates it separately at startup from options. Else legacy mode path unchanged.
```

Rules (spec §6.3): `ValidateOutcomeTable` fails when a key ∉ `OutcomeKeys`, duplicated or missing; pay outside `[0.1, 10]` or > 2 decimals; weight ≤ 0; Σ weight ≠ 1,000,000; RTP ≥ 100; |RTP − rtpPercent| > 0.25. `ValidateWordLinesPool` fails when: word length ∉ 3..5 or not `[a-z]`; duplicate; `Multiplier` ∉ {1, 2}; a space word (2) not of length 4/5; fewer than 50 normal words of any length 3/4/5; fewer than 5 space words of length 4 or of length 5; any word in `blocked`; any word containing `edson`.

- [ ] **Step 1: Failing tests** in `HouseEdgeRulesTests.cs` (append a `// Word Lines` region):
```csharp
private const string Table = "bad:0.3:436574;double:6:4633;down:0.5:70000;echo:5.6:9266;edson:8:2317;five:2.5:65635;four:1.4:100382;full:10:1544;spacefive:5:16988;spacefour:2.8:27026;three:0.5:200000;twolines:3:27026;wild:1.4:38609";

[Fact] public void OutcomeTable_RoundTrips()
    => HouseEdgeRules.FormatOutcomeTable(HouseEdgeRules.ParseOutcomeTable(Table)).ShouldBe(Table);

[Fact] public void OutcomeTable_ShippedTableIsValid()
    => HouseEdgeRules.ValidateOutcomeTable(HouseEdgeRules.ParseOutcomeTable(Table), 98m).IsValid.ShouldBeTrue();

[Theory]
[InlineData("bad:0.3:436574", "missing")]                          // keys missing
[InlineData(Table + ";bad:0.3:1", "twice")]                         // duplicate
[InlineData("bogus:1:1;" + Table, "unknown")]
public void OutcomeTable_KeyErrors(string table, string reasonFragment)
{
    var r = HouseEdgeRules.ValidateOutcomeTable(HouseEdgeRules.ParseOutcomeTable(table), 98m);
    r.IsValid.ShouldBeFalse(); r.Reason!.ToLowerInvariant().ShouldContain(reasonFragment);
}

[Theory]
[InlineData("full:10:1544", "full:10.5:1544")]        // pay above cap
[InlineData("bad:0.3:436574", "bad:0.05:436574")]     // pay below floor
[InlineData("bad:0.3:436574", "bad:0.3:436573")]      // Σ ≠ 1,000,000
public void OutcomeTable_RejectsBrokenRows(string from, string to)
    => HouseEdgeRules.ValidateOutcomeTable(HouseEdgeRules.ParseOutcomeTable(Table.Replace(from, to)), 98m).IsValid.ShouldBeFalse();

[Fact] public void OutcomeTable_RejectsRtpMismatch()
    => HouseEdgeRules.ValidateOutcomeTable(HouseEdgeRules.ParseOutcomeTable(Table), 97.7m).IsValid.ShouldBeFalse();

[Fact] public void OutcomeTable_ParseRejectsGarbage()
    => Should.Throw<FormatException>(() => HouseEdgeRules.ParseOutcomeTable("bad:x:1"));
```
Pool tests: build a pool helper `Pool(n3, n4, n5, s4, s5)` generating distinct a–z words (e.g. from an index → base-26 letters, length fixed) and assert: valid at (50,50,50,5,5); invalid at 49 threes; a 6-letter word; `"Cat"`; duplicate; multiplier 3; a space word of length 3; a blocked overlap; the word `"edson"`.

- [ ] **Step 2:** `scripts\run_tests.bat --filter "FullyQualifiedName~HouseEdgeRulesTests"` → FAIL (compile).
- [ ] **Step 3:** Implement (record struct, constants, parse/format modelled on `TryParseLadder`/`FormatLadder`, validators, `Validate` branch). `GameParameters.OutcomeTable` (`string?`) with a `///` comment.
- [ ] **Step 4:** New `WordLinesOddsTests.cs` pins the table (Σ = 1,000,000; RTP 98.00 ± 0.01; max pay 10; 13 rows; keys = `OutcomeKeys`) — mirrors `HouseEdgeModeOddsTests` header warning ("If one of these fails, the CONFIG is wrong").
- [ ] **Step 5:** Tests green → commit `Word Lines: outcome table rules.`

### Task B2: `WordLinesGrid` generator

**Files:**
- Create: `src/SpaceStroker.Domain/Games/Services/WordLinesGrid.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Domain/Games/WordLinesGridTests.cs`

**Interfaces — Produces:**
```csharp
public sealed record WordLinesHit(string Kind, int Index, string Word, string Shown, bool Space);
public sealed record WordLinesResult(string Grid, IReadOnlyList<WordLinesHit> Hits);
public sealed record WordLinesPool(IReadOnlyList<string> Words3, IReadOnlyList<string> Words4, IReadOnlyList<string> Words5,
    IReadOnlyList<string> Space4, IReadOnlyList<string> Space5, IReadOnlySet<string> Blocked, string FillerLetters)
{
    // active PresetChoices: Multiplier 2 → space (len 4/5), else normal by length; words lowercased.
    public static WordLinesPool From(IEnumerable<PresetChoice> active, IEnumerable<string> blocked, string fillerLetters);
}
public static class WordLinesGrid
{
    public const string LineKind = "line", DownKind = "down";
    public const int Rows = 3, Reels = 5, MaxAttempts = 50;
    public static readonly IReadOnlyList<int[]> Lines; // spec §6.6, Lines[i] = line i+1
    public static IReadOnlyList<WordLinesHit> Scan(string grid, WordLinesPool pool);  // playable hits only (normal ∪ space ∪ "edson")
    public static bool ContainsBlocked(string grid, WordLinesPool pool);               // any line prefix 3..5 or reel spells a blocked word
    public static decimal PayOf(IReadOnlyList<WordLinesHit> hits);                     // implied pay
    public static WordLinesResult Build(string outcomeKey, WordLinesPool pool, IReadOnlySet<string> recentWords, Random rng, out bool usedFallback);
    public static WordLinesResult Fallback(string outcomeKey);                         // hard-coded grids
    public static string Serialize(IReadOnlyList<WordLinesHit> hits);                  // camelCase JSON (System.Text.Json)
    public static IReadOnlyList<WordLinesHit> Deserialize(string? json);               // empty on null/blank
}
```

Semantics:
- Grid stored uppercase; `Scan` is case-insensitive. Cell index = `row * 5 + reel`.
- **Line scan:** for each line, test prefixes of length 5, 4, 3 (longest first); the first prefix that matches a playable word yields one hit (`Index` = line number 1–10). `?` matches any letter; for a `?` prefix `Word` = the ordinal-first matching word and `Space` = true if **any** matching word is a space word.
- **Down scan:** each reel top→bottom (3 cells) matching a 3-letter playable word → hit `down`, `Index` = reel 0–4, `Space = false`.
- `PayOf`: no hits → 0.3; any hit word `edson` → 8; 3 line hits all length 5 on lines {1,2,3} → 10; exactly 2 line hits both length 5, no down hits → 6; otherwise Σ line pays (3→0.5, 4→1.4, 5→2.5, ×2 if `Space`, ×2 if another line hit has the same `Shown`) + 0.5 per down hit.
- **Build** (spec §6.6 table). Picks exclude `recentWords`, falling back to the full tier when exclusion empties it. Placement never overwrites a cell with a different letter (conflict ⇒ attempt fails). Remaining cells ← random `FillerLetters`. Valid iff `Scan` equals the intended hits on `(Kind, Index, Shown, Space)` as sets and `!ContainsBlocked`. After `MaxAttempts` failures → `Fallback(key)`, `usedFallback = true`.
  - `wild`: a normal 4-letter word, one random cell among positions 1..3 → `?`; intended `Shown` contains `?`, `Word` = the picked word.
  - `echo`: same normal 4-letter word on two lines whose first-4 cells are disjoint.
  - `twolines`: normal 5-letter word on line A + normal 3-letter word on line B, used cells disjoint.
  - `double`: two distinct normal 5-letter words on two distinct lines of {1,2,3}.
  - `full`: three distinct normal 5-letter words on lines 2, 1, 3.
  - `edson`: `edson` on a random line. `bad`: all filler.
- **Fallback grids** (uppercase rows top→bottom; words must be in the shipped pool — Task B3 test asserts `Scan(Fallback(k).Grid, shippedPool)` equals `Fallback(k).Hits` and `!ContainsBlocked`):
  `bad QXZJV/KZQXJ/VJKZQ` · `three CATXQ/XZQJV/VKQZJ` (line 2 cat) · `down QXFVZ/JZOQX/VQXKJ` (reel 2 fox) · `four KXZVJ/BOLTQ/JQXKZ` (line 1 bolt) · `wild KXZVJ/B?LTQ/JQXKZ` (line 1 bolt, shown b?lt) · `five QXZKV/XJQZK/BRAVE` (line 3) · `spacefour KXQZV/STARQ/JVXKQ` (line 1 star) · `spacefive QXZKV/ORBIT/JVXKQ` (line 1 orbit) · `echo BOLTX/ZXQJV/BOLTQ` (lines 2,3) · `twolines CATQX/XZQJV/TIGER` (line 2 cat, line 3 tiger) · `double TIGER/XZQVK/BRAVE` (lines 2,3) · `full TIGER/BRAVE/PLANT` (lines 2,1,3) · `edson QXZVK/EDSON/JKXQZ` (line 1).
  If B3's shipped-pool test finds an accidental word in a fallback, change that fallback's filler letters (never the key's words) until it passes.

- [ ] **Step 1: Failing tests** `WordLinesGridTests.cs`, with a test pool built from a fixed word list (≥ 60 per tier: use real words so accidental hits are realistic; include `cat tiger brave bolt fox plant star orbit`) + blocked `["lube","damn"]`:
```csharp
public static TheoryData<string> Keys => new(HouseEdgeRules.OutcomeKeys);

[Theory, MemberData(nameof(Keys))]
public void EveryKey_BuildsAGridShowingExactlyThatRow(string key)
{
    var outcomes = HouseEdgeRules.ParseOutcomeTable(Table).ToDictionary(x => x.Key, x => x.Pay);
    for (var seed = 0; seed < 500; seed++)
    {
        var result = WordLinesGrid.Build(key, Pool, new HashSet<string>(), new Random(seed), out _);
        result.Grid.Length.ShouldBe(15);
        result.Grid.ShouldMatch("^[A-Z?]{15}$");
        Normalize(WordLinesGrid.Scan(result.Grid, Pool)).ShouldBe(Normalize(result.Hits), $"{key} seed {seed}");
        WordLinesGrid.ContainsBlocked(result.Grid, Pool).ShouldBeFalse();
        WordLinesGrid.PayOf(result.Hits).ShouldBe(outcomes[key], $"{key} seed {seed}");
    }
}
private static string Normalize(IEnumerable<WordLinesHit> hits)
    => string.Join("|", hits.Select(h => $"{h.Kind}:{h.Index}:{h.Shown.ToLowerInvariant()}:{h.Space}").OrderBy(x => x, StringComparer.Ordinal));
```
Also: `Bad_ScanFindsNothing`; `FallbackRate_IsLow` (per key, < 2% of 500 seeds use the fallback); `RecentWords_AreNotUsed` (exclude all but 3 five-letter words for `five` → result word ∈ the 3); `RecentWords_ThatEmptyATier_FallBackToFullTier` (exclude all 5-letter words → still builds, no throw); `ImpossiblePool_UsesFallback` (pool whose `FillerLetters = "A"` and words `aaa…` ⇒ `usedFallback` true, hits = `Fallback(key).Hits`); `Scan_WildPattern` (`KXZVJ/B?LTQ/JQXKZ` → one line-1 hit shown `b?lt`); `Scan_LongestWordWins` (line with `CATER` where both `cat` and `cater` exist → one 5-letter hit); `PayOf` table for each fallback; `Serialize`/`Deserialize` round-trip with camelCase keys (`"kind"`).
- [ ] **Step 2:** run → FAIL. **Step 3:** implement. **Step 4:** green (whole file < ~10 s). **Step 5:** commit `Word Lines: grid generator.`

### Task B3: Config, entity, migration, reconciler, seeding

**Files:** `src/SpaceStroker.Common/spacestrokersettings.json`; `OperatorSettings.cs`; `GameSettings.cs`; `Game.cs`; `SpaceStrokerDbContext.cs`; new migration; `HouseEdgeReconciler.cs`; `Consumers/Program.cs` (`ToGameParameters`, startup word-pool/blocked validation); tests `WordLinesOddsTests.cs` (config-bound), `HouseEdgeReconcilerTests.cs` (or the existing reconciler test file), `OrbitSettingsBindingTests.cs`, `DefaultGame*` tests that assert the old ladder.

**Interfaces — Produces:**
```csharp
public class HouseEdgeOutcomeSettings { public string Key { get; set; } public decimal Pay { get; set; } public int Weight { get; set; } }
// HouseEdgeSettings: + List<HouseEdgeOutcomeSettings> Outcomes, List<string> BlockedWords, string FillerLetters = "QXZJVK", int RepeatGuardRounds = 20,
//                    + IReadOnlyList<HouseEdgeOutcome> OutcomeRows()  ; Ladder/Modes/LadderSlots removed when no reader is left (else keep, unused by config)
// Game: string? WordLinesGrid (max 15), string? WordLinesHits (text)
// GameParameters.OutcomeTable mapped HasMaxLength(1024)
```

Steps:
- [ ] Config (spec §6.1): Hyper Roll `Description`, `ChoiceRequiredLength: 5`, `HouseEdge.RtpPercent: 98.0`, `Outcomes` (13 objects, §4 order), `Words` (20 space words Multiplier 2 + ~150 each of 3/4/5-letter everyday family-friendly words Multiplier 1; lowercase; no proper nouns; must include every word used by the B2 fallbacks: `cat fox bolt brave tiger plant` and the space words `star orbit`), `BlockedWords` (3–5-letter profanity/slurs/sexual/drug terms incl. `lube`), `FillerLetters: "QXZJVK"`, `RepeatGuardRounds: 20`; remove `Ladder`/`Modes`; `GeneralSettings:Games` HouseEdgeTarget* → 2/2/2 and the `GameSettings.cs` defaults.
- [ ] Config-bound tests: load `spacestrokersettings.json` the way `OrbitSettingsBindingTests` does; assert `ValidateOutcomeTable(rows, 98)` valid, `ValidateWordLinesPool(words as PresetChoice, BlockedWords)` valid, each tier ≥ 140, space words = exactly the 20 listed; **fallback grids scan clean against the shipped pool** (every key).
- [ ] Entity + EF + `dotnet ef migrations add HyperRollWordLines` (3 nullable columns only — inspect the generated migration, it must contain nothing else).
- [ ] Reconciler (spec §6.4): patch `OutcomeTable` (formatted from `Outcomes`), `RtpPercent`, `ChoiceRequiredLength` NULL-aware; stop writing `BoardLadder` for a type with `Outcomes`; orphaned modes deleted (existing path, config now has none); words unchanged; **cutover sweep**: delete `Games` of this type with `Status == WaitingBets`, `OwnerPlayerId != null` or null (any), **no bets**, `BoardMode == null || BoardMode != "LINES"`, plus their `GameBoardChoices` — same deletion shape as `HouseEdgeBoardCleanupJob.Run`, inside the coverage advisory lock it uses; log count at Warning (reconciler is static → return the count via an out/second value or a small result record, Program.cs logs it).
- [ ] `Program.cs`: `ToGameParameters` seeds `OutcomeTable`, `RtpPercent`, words; no board modes. At startup, after reconcile, run `ValidateWordLinesPool` with config `BlockedWords` and log Error on failure (the type then fails `Validate`? — no: the pool check without blocked already runs in `Validate`; the startup one adds the blocked overlap and only logs Error).
- [ ] Reconciler tests: OutcomeTable patched; Hyper Roll modes deleted; a 7-letter word deactivated; sweep deletes only unbet non-LINES WaitingBets rounds (a bet one, a LINES one and an Ended one survive).
- [ ] Fix any test that pinned the old Hyper Roll config (ladder/modes/74 words) — retarget, don't delete coverage. `HouseEdgeModeOddsTests` stays (it pins strings, not config) unless it reads config.
- [ ] Green → commit `Word Lines: config, schema and reconciler.`

### Task B4: Round open, draw and settlement

**Files:** `GameService.cs`, `IGameService.cs`, `GamesController.cs`, delete `Handlers/Games/RerollHouseEdgeBoard/*` (+ tests), `SpaceStrokerTyper.cs`, `RunGameConsumer.cs`, `BetDomainService.cs`, `GameStatusUpdatedEvent.cs` (`GameResult` fields), `OrbitBetSettlementService.cs`; tests listed in spec §11.1 for these files.

**Interfaces — Consumes:** B1 `ParseOutcomeTable`, `IsWordLines`, `SpinChoice`; B2 `WordLinesGrid.Build/Serialize`, `WordLinesPool.From`; B3 settings `BlockedWords/FillerLetters/RepeatGuardRounds`.
**Produces:** `GameResult` gains `string? OutcomeKey, decimal? OutcomePay, string? Grid, List<WordLinesHitDto>? Hits`; `public sealed record WordLinesHitDto(string Kind, int Index, string Word, string Shown, bool Space)` in `Contracts/Games/`.

Behaviour (spec §6.5, §6.7):
- [ ] `OpenSoloBoard` / `CreateGame`: for a type with `OutcomeTable`, `BoardMode = "LINES"`, 13 `GameBoardChoice` rows (`Choice = key`, `Multiplier = pay`, `Weight`, `Position` = index in ordinal key order); mode argument ignored; validation failure → refuse + Error log as today. Test: 13 rows, LINES, seed hash committed, `mode: "HYPER"` ignored.
- [ ] Remove reroll end to end (route, handler/request, `RerollSoloBoard`, interface member, their tests).
- [ ] Typer: for LINES boards `NumberOfWinners` = real bet count; character pool = `abcdefghijklmnopqrstuvwxyz`. Test.
- [ ] `RunGame`: don't emit `FirstChoiceTyped` as the closing terminal batch for LINES.
- [ ] Ended arm of `UpdateGameStatusCore`: for LINES load active `PresetChoices` of the type, options' blocked/filler/guard; recent words = `Word`s from `WordLinesHits` of the owner's last N **ended LINES** rounds (excluding this one, `CreatedDate desc`, one query, only when `OwnerPlayerId != null`); `Build(WinningChoice, …, Random.Shared, out fallback)`; persist `WordLinesGrid`/`WordLinesHits` in the same save (NoTracking ⇒ verify it really persists in the test); fallback → Warning `"Word Lines grid fallback for round {GameId} key {Key}"`; `GameResult` filled (OutcomeKey, OutcomePay = row multiplier, Grid, Hits); `Winners` = all bets.
- [ ] `RunGameConsumer`: LINES ⇒ every non-voided bet `NeedsRewarding`.
- [ ] `BetDomainService`: LINES ⇒ `HouseEdgeChoiceIsValid` iff `Choice == "spin"`; `CalculateHouseEdgeReward` uses `_boardMultipliers[game.WinningChoice]`; tests for `bad` 0.3 / `three` 0.5 / `full` 10 and redelivery.
- [ ] `OrbitBetSettlementService.CreditWin`: publish `OrbitWinNotificationEvent` only when credited amount > the bet's stake; ledger credit always. Tests both ways.
- [ ] `RewardGameConsumerTests`: LINES bad-word round pays 0.3× via settlement; Orbit-funded → `CreditWin`; streamer → play ledger.
- [ ] Green → commit `Word Lines: open, draw and settle.`

### Task B5: Bets, availability, pre-checks, Orbit paths

**Files:** `RegisterBetHandler.cs`, `RegisterBetRequestBody.cs`, `CheckBetAvailability/*`, `BetPreChecks.cs`, `PlaceOrbitWager/*`, `PlaceManualOrbitBetConsumer.cs`, `OrbitSoloAutoBetService.cs`, delete `OrbitSlotPicker.cs` (+tests) if unused, `UpdateOrbitSettings/*`, `GetOrbit/*`; tests per spec §11.1.

- [ ] Choice optional on `RegisterBetRequestBody`, `PlaceOrbitWagerRequest` (keep `[MaxLength(20)]`); non-house-edge types without a choice still refused (vague, in handler).
- [ ] Normalise to `"spin"` for LINES rounds in `RegisterBetHandler`, `BetPreChecks` (skip `FindSameChoiceBet`), `PlaceOrbitWagerHandler` (incl. streamer path), `PlaceManualOrbitBetConsumer`.
- [ ] `ValidateSoloRoundUnderLock`: LINES skips the board-word check.
- [ ] Coverage: `HouseEdgeNewBet(GameId, "spin", Amount, Multiplier: board max)`; `HouseEdgeCoverageTests`: `spin` on a 13-row board ⇒ exposure = stake × 10.
- [ ] `/availability`: ignore choice for Hyper Roll, price stake × max outcome pay (10).
- [ ] Orbit solo loop: no `ResolveMode`; worst multiplier = max pay of `OutcomeTable`; `OpenSoloBoard(parameters, null)`; choice `"spin"`. Test with an enrollment carrying `BoardMode = "HYPER"`, `Multiplier = 9`.
- [ ] `UpdateOrbitSettingsHandler`: accept and ignore mode/multiplier for Hyper Roll; `GetOrbitHandler`: `Modes` empty for Hyper Roll.
- [ ] Green → commit `Word Lines: stake-only bets and Orbit.`

### Task B6: Read side — parameters, rounds, verification, history, stats, provably-fair vector

**Files:** `GameParametersDto.cs`, `CacheHelper.cs` (`ActiveParameters:v4`), `AdminGameTypeHandlerTests.cs`, `ActiveGameDto.cs` (+ its mapping), `GameVerificationDto.cs` + `GetGameVerificationHandler.cs`, `GameHistoryService.cs` + `GameHistoryDto.cs`, `PlayerStatsService.cs`, `GlobalStatsService.cs`, `GameStatsService.cs`, `ProvablyFairTests.cs`.

- [ ] `GameParametersDto`: `Outcomes` (`OutcomeRowDto(Key, Pay, Weight)` ordinal order), RTP 98, `Modes` and `PresetChoices` empty for Hyper Roll.
- [ ] `ActiveGameDto` / `GET /games/rounds/{id}`: `WordLinesGrid`, `WordLinesHits` (deserialised) once ended.
- [ ] Verification: grid + hits; draw unchanged.
- [ ] History: LINES won = credited > stake; `OutcomeKey`, `OutcomePay` (+ `ClonePublic`).
- [ ] Stats: LINES win/loss per the same rule.
- [ ] **V4 vector:** in `ProvablyFairTests`, server seed = the existing V3 seed constant, prefix `"11111111-1111-1111-1111-111111111111|seedA|seedB"`, weights = §4 in ordinal key order → run once, freeze the returned index, assert it. Record `(seed, prefix, index, key)` in the commit message for the front task.
- [ ] Green → commit `Word Lines: read side and V4 vector.`

### Task B7: Integration tests

**Files:** new `src/SpaceStroker.Tests.Integration/.../HyperRollWordLinesFlowTests.cs`; retarget/retire `HyperRollFlowTests`, `HyperRollModeFlowTests`, `HyperRollSoloRoundsTests`, `HyperRollBoardWeightGuardTests`, `HyperRollMisconfigurationTests`, `OrbitSoloAutoBetFlowTests`, `StreamerWalletsFlowTests`, `DefaultGameSeedingTests`.

- [ ] Happy path (spec §11.2): open → 13 rows LINES + seed hash; paid wallet bet 0.01 SOL with no choice → stored `spin`; steer the draw to `five` (harness's existing steer) → `WinningChoice = five`, 15-char grid, one 5-letter line hit, treasury credit 0.025 SOL, payout in `RewardForWinnersCompleted`; repeat steered to `bad` → 0.003 SOL, grid with no hits.
- [ ] Orbit solo loop with legacy enrollment fields → bets `spin`, credited to Orbit balance.
- [ ] Cutover: unbet seeded `HYPER` board deleted by the reconciler; a seeded **bet** `HYPER` round settles via the legacy path.
- [ ] Misconfiguration: broken outcome table ⇒ no round opens.
- [ ] `scripts\run_integration_tests.bat` green → commit `Word Lines: integration tests.`

---

## Frontend (`monkify-front`)

Gate for every task: `yarn lint` and `yarn build` clean (copy hub `yarn.lock` and junction hub `node_modules` into the worktree first — memory). Don't introduce module-scope `watch()` that reaches `useWallet()`.

### Task F1: Types, utils, services, state
`src/types/index.ts`, `src/types/orbit.ts`, new `src/utils/wordLines.ts`, `src/services/mainService.ts`, `src/services/orbitService.ts`, `src/hooks/useGame.ts` — spec §7.1, §7.2, §7.4, §7.5, using the wire contracts above. `wordLines.ts` exports `LINES`, `OUTCOME_LABELS`, `isWordLinesRound`, `tierForPay`, `bannerKind`, `linePay(len, space)`. Commit.

### Task F2: Game screen
New `WordLinesGrid.vue`, `WordLinesPlate.vue`; `GameConsole.vue`, `GameTerminal.vue`, `HyperRollWinFx.vue` (`variant` prop), `HyperRollModeDial.vue` — spec §7.3. Visual reference: the artifact `hr/wordlines-98.html` (reels, lamps, paths). Whole spin ≤ 2 s. Commit.

### Task F3: Orbit UI, verify, history, stats
spec §7.6, §7.7 (V4 vector comment from B6), §7.8. Commit.

### Task F4: Practice demo, guide, lobby
spec §7.9, §7.10. Commit.

---

## Landing page (`monkify-lp`)

### Task L1: LP copy
spec §10.3 — `GameModes.vue`, `index.astro`, `terms.astro` (+ `LAST_UPDATED`). `npm run build`; check `#modes` at desktop and 390 px. Commit. Terms clause flagged for owner review.

---

## Execution order

B1 → B2 → B3 → B4 → B5 → B6 → B7 sequential (shared files). F1–F4 and L1 run in parallel with the backend against the fixed wire contracts; F3's vector comment waits for B6.

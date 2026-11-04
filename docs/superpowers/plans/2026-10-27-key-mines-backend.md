# Key Mines — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A solo player-vs-house Mines game ("Key Mines", #204) on the existing house-edge machinery: open a board (seed hash + play token), bet stake + `J{M}`, press keys / cash out over HTTP, payout `stake × ladder[k]` through `RewardGameConsumer`, a 5-min inactivity sweep, restart survival, provably-fair jams.

**Architecture:** `IsHouseEdge = true` + new `IsKeyMines` flag. Pure domain units (`KeyMinesLadder`, `KeyMinesRules`, `KeyMinesMoves`, `ProvablyFair.KeyMinesJams`) carry every rule and are unit-tested; one Infrastructure service (`KeyMinesRoundService`) turns their decisions into conditional `ExecuteUpdate`s and is tested in the Testcontainers suite (EF InMemory can't run `ExecuteUpdate`). No typer, no `RunGameEvent`.

**Tech Stack:** .NET 8, EF Core (Npgsql), MediatR, MassTransit (Postgres transport), Hangfire, xUnit + Shouldly + Moq, Testcontainers.

**Spec:** `monkify-meta/specs/key-mines.md` (authoritative — read §3/§3.1/§6 before every task). Worktree: `E:\Monkify\.worktrees\key-mines\monkify-backend`. Branch `feature/key-mines`.

## Global Constraints

- Work ONLY in `E:\Monkify\.worktrees\key-mines\monkify-backend`; never touch the hub `E:\Monkify\monkify-backend`.
- Unit tests: `scripts\run_tests.bat` (never a raw full `dotnet test`). Integration: `scripts\run_integration_tests.bat` (Docker).
- EF mapping inline in `SpaceStrokerDbContext.OnModelCreating`. One migration named `KeyMines`, generated with `dotnet ef migrations add KeyMines -p SpaceStroker.Infrastructure -s SpaceStroker.Consumers` from `src/`. Never hand-edit the snapshot.
- DbContext is NoTracking: every state change is a conditional `ExecuteUpdate`. Unit tests (InMemory) cannot run `ExecuteUpdate` — keep decisions in pure domain code, test SQL paths in integration.
- Don't run `sed -i` over many `.cs` files (CRLF flips repo-wide). Use Edit.
- Constants exactly: `KeyMinesRules.BoardMode = "KEYS"`, `KeyMinesRules.Keys = "qwertyuiopasdfghjklzxcvbnm"`, choice `J{M}` (uppercase J), jam options `1,3,5,10`, RTP 98, stake 0.002–0.05, `MaxPayoutPerRound` 1.0, inactivity 5 min, sweep 1 min, header `X-Round-Token`, end values `cashout|cap|clear|idle|jam|refund`, `WinningChoice` `C{k}` / `JAM`.
- Every press/cash-out refusal = one resource string `KeyMinesRequestInvalid` = *"That move could not be processed."*
- Money arithmetic: `Pay = Math.Round(stake × rtp × C(26,k) / (100 × C(26−m,k)), 9, MidpointRounding.ToZero)` — multiply first, divide last.
- `ActiveParameters` cache key `:v4` → `:v5`.
- Logs ops must see → `Log.Warning` (prod is Warning-only).

---

### Task 1: Pure domain — ladder, rules, moves, jam draw

**Files:**
- Create: `src/SpaceStroker.Domain/Games/Services/KeyMinesLadder.cs`
- Create: `src/SpaceStroker.Domain/Games/Services/KeyMinesRules.cs`
- Create: `src/SpaceStroker.Domain/Games/Services/KeyMinesMoves.cs`
- Modify: `src/SpaceStroker.Domain/Games/Services/ProvablyFair.cs` (add `KeyMinesJams` after `NextIndex`)
- Test: `src/SpaceStroker.Tests/UnitTests/Domain/Games/KeyMinesLadderTests.cs`, `KeyMinesRulesTests.cs`, `KeyMinesMovesTests.cs`, extend `ProvablyFairTests.cs`

**Interfaces — Produces:**
```csharp
namespace SpaceStroker.Domain.Games.Services
{
    public static class KeyMinesLadder
    {
        public const int KeyCount = 26;
        public static long Binomial(int n, int k);
        public static decimal Multiplier(int jams, int step, decimal rtpPercent);           // exact, unrounded
        public static decimal Pay(decimal stake, int jams, int step, decimal rtpPercent);   // 9 dp ToZero
        public static int LastStep(decimal stake, int jams, decimal rtpPercent, decimal cap); // 0 if even step 1 > cap
        public static decimal WorstCaseMultiplier(decimal stake, int jams, decimal rtpPercent, decimal cap);
    }
    public static class KeyMinesRules
    {
        public const string BoardMode = "KEYS";
        public const string Keys = "qwertyuiopasdfghjklzxcvbnm";
        public const string JamChoicePrefix = "J";
        public const string JamWinningChoice = "JAM";
        public static string CashWinningChoice(int step) => "C" + step;
        public static class Ends { public const string CashOut="cashout", Cap="cap", Clear="clear", Idle="idle", Jam="jam", Refund="refund"; }
        public static IReadOnlyList<int> ParseJamOptions(string? csv);
        public static string FormatJamOptions(IEnumerable<int> options);
        public static bool TryParseChoice(string? choice, IReadOnlyCollection<int> options, out int jams);
        public static string? Validate(decimal rtpPercent, IReadOnlyCollection<int> jamOptions, decimal minStake, decimal? maxStake, decimal? cap);
    }
    public enum KeyMinesMoveKind { Refused, Safe, Jam, CashOut }
    public sealed record KeyMinesRoundState(string JamKeys, string PressedKeys, int Jams, decimal Stake, decimal RtpPercent, decimal Cap);
    public sealed record KeyMinesDecision(KeyMinesMoveKind Kind, string Pressed, int Step, string? End);
    public static class KeyMinesMoves
    {
        public static KeyMinesDecision Press(KeyMinesRoundState state, string? key);
        public static KeyMinesDecision CashOut(KeyMinesRoundState state, string end = KeyMinesRules.Ends.CashOut);
    }
    // ProvablyFair
    public static string KeyMinesJams(string serverSeed, string prefix, int jams);
}
```

- [ ] **Step 1: Write the failing ladder tests** (`KeyMinesLadderTests.cs`)

```csharp
public class KeyMinesLadderTests
{
    [Theory]
    [InlineData(1, 1, "1.0192")] [InlineData(1, 8, "1.4156")] [InlineData(1, 24, "12.7400")] [InlineData(1, 25, "25.4800")]
    [InlineData(3, 1, "1.1078")] [InlineData(3, 3, "1.4387")] [InlineData(3, 15, "15.4424")] [InlineData(3, 21, "254.8000")]
    [InlineData(5, 1, "1.2133")] [InlineData(5, 10, "14.7583")] [InlineData(5, 16, "255.8111")]
    [InlineData(10, 1, "1.5925")] [InlineData(10, 3, "4.5500")] [InlineData(10, 8, "118.9611")] [InlineData(10, 9, "267.6625")]
    public void Multiplier_MatchesHandComputedTable(int jams, int step, string expected)
        => Math.Round(KeyMinesLadder.Multiplier(jams, step, 98m), 4).ShouldBe(decimal.Parse(expected, CultureInfo.InvariantCulture));

    [Fact]
    public void Pay_TruncatesTo9Dp() => KeyMinesLadder.Pay(0.01m, 3, 3, 98m).ShouldBe(0.014387351m);

    [Theory]
    [InlineData(1, "0.002", 25)] [InlineData(1, "0.02", 25)] [InlineData(1, "0.05", 24)]
    [InlineData(3, "0.002", 21)] [InlineData(3, "0.005", 20)] [InlineData(3, "0.01", 19)] [InlineData(3, "0.02", 18)] [InlineData(3, "0.05", 15)]
    [InlineData(5, "0.002", 16)] [InlineData(5, "0.005", 15)] [InlineData(5, "0.01", 14)] [InlineData(5, "0.02", 12)] [InlineData(5, "0.05", 10)]
    [InlineData(10, "0.002", 9)] [InlineData(10, "0.005", 8)] [InlineData(10, "0.01", 7)] [InlineData(10, "0.02", 6)] [InlineData(10, "0.05", 5)]
    public void LastStep_AtOneSolCap(int jams, string stake, int expected)
        => KeyMinesLadder.LastStep(decimal.Parse(stake, CultureInfo.InvariantCulture), jams, 98m, 1m).ShouldBe(expected);

    [Fact]
    public void EveryStep_ReturnsExactly98PercentInExpectation()
    {
        foreach (var jams in new[] { 1, 3, 5, 10 })
            for (var k = 1; k <= 26 - jams; k++)
            {
                var survive = (decimal)KeyMinesLadder.Binomial(26 - jams, k) / KeyMinesLadder.Binomial(26, k);
                Math.Round(KeyMinesLadder.Multiplier(jams, k, 98m) * survive, 12).ShouldBe(0.98m);
            }
    }

    [Fact]
    public void WorstCase_NeverExceedsCap()
    {
        for (var lamports = 2_000_000L; lamports <= 50_000_000L; lamports += 1_000_000L)
            foreach (var jams in new[] { 1, 3, 5, 10 })
            {
                var stake = lamports / 1_000_000_000m;
                (stake * KeyMinesLadder.WorstCaseMultiplier(stake, jams, 98m, 1m)).ShouldBeLessThanOrEqualTo(1m);
            }
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `scripts\run_tests.bat` → compile error (types missing).

- [ ] **Step 3: Implement `KeyMinesLadder.cs`**

```csharp
namespace SpaceStroker.Domain.Games.Services
{
    /// Key Mines ladder (specs/key-mines.md §6.1, S7): ladder[k] = rtp/100 × C(26,k) / C(26−jams,k).
    /// Exact rational in decimal: multiply first, divide last, truncate payouts to 9 dp.
    public static class KeyMinesLadder
    {
        public const int KeyCount = 26;

        public static long Binomial(int n, int k)
        {
            if (k < 0 || k > n) return 0;
            k = Math.Min(k, n - k);
            long result = 1;
            for (var i = 1; i <= k; i++) result = result * (n - k + i) / i;
            return result;
        }

        public static decimal Multiplier(int jams, int step, decimal rtpPercent)
            => rtpPercent * Binomial(KeyCount, step) / (100m * Binomial(KeyCount - jams, step));

        public static decimal Pay(decimal stake, int jams, int step, decimal rtpPercent)
            => Math.Round(stake * rtpPercent * Binomial(KeyCount, step) / (100m * Binomial(KeyCount - jams, step)),
                9, MidpointRounding.ToZero);

        public static int LastStep(decimal stake, int jams, decimal rtpPercent, decimal cap)
        {
            var last = 0;
            for (var k = 1; k <= KeyCount - jams; k++)
            {
                if (Pay(stake, jams, k, rtpPercent) > cap) break;
                last = k;
            }
            return last;
        }

        public static decimal WorstCaseMultiplier(decimal stake, int jams, decimal rtpPercent, decimal cap)
        {
            var last = LastStep(stake, jams, rtpPercent, cap);
            return last == 0 ? 0m : Multiplier(jams, last, rtpPercent);
        }
    }
}
```

- [ ] **Step 4: Write failing rules + moves tests**

`KeyMinesRulesTests.cs`:
```csharp
[Theory]
[InlineData("J3", true, 3)] [InlineData("J10", true, 10)] [InlineData("J2", false, 0)] [InlineData("j3", false, 0)]
[InlineData("J", false, 0)] [InlineData("spin", false, 0)] [InlineData(null, false, 0)] [InlineData("J30", false, 0)]
public void TryParseChoice(string? choice, bool ok, int jams)
{
    KeyMinesRules.TryParseChoice(choice, new[] { 1, 3, 5, 10 }, out var m).ShouldBe(ok);
    if (ok) m.ShouldBe(jams);
}
[Fact] public void ParseAndFormat_RoundTrip() => KeyMinesRules.FormatJamOptions(KeyMinesRules.ParseJamOptions("1,3,5,10")).ShouldBe("1,3,5,10");
[Fact] public void Validate_Shipped_IsValid() => KeyMinesRules.Validate(98m, new[] { 1, 3, 5, 10 }, 0.002m, 0.05m, 1m).ShouldBeNull();
[Theory]
[InlineData(100)] [InlineData(0)]
public void Validate_BadRtp(decimal rtp) => KeyMinesRules.Validate(rtp, new[] { 3 }, 0.002m, 0.05m, 1m).ShouldNotBeNull();
[Fact] public void Validate_EmptyJams() => KeyMinesRules.Validate(98m, Array.Empty<int>(), 0.002m, 0.05m, 1m).ShouldNotBeNull();
[Fact] public void Validate_DuplicateJams() => KeyMinesRules.Validate(98m, new[] { 3, 3 }, 0.002m, 0.05m, 1m).ShouldNotBeNull();
[Fact] public void Validate_JamOutOfRange() => KeyMinesRules.Validate(98m, new[] { 26 }, 0.002m, 0.05m, 1m).ShouldNotBeNull();
[Fact] public void Validate_MissingMaxOrCap() => KeyMinesRules.Validate(98m, new[] { 3 }, 0.002m, null, 1m).ShouldNotBeNull();
[Fact] public void Validate_CapBelowFirstStepAtMax() => KeyMinesRules.Validate(98m, new[] { 10 }, 0.002m, 0.05m, 0.07m).ShouldNotBeNull(); // 0.05 × 1.5925 > 0.07
```

`KeyMinesMovesTests.cs` (jams `"abc"`, stake 0.01, rtp 98, cap 1, 3 jams):
```csharp
private static KeyMinesRoundState S(string pressed, decimal stake = 0.01m, string jams = "abc", decimal cap = 1m)
    => new(jams, pressed, jams.Length, stake, 98m, cap);

[Fact] public void SafePress_AdvancesStep() => KeyMinesMoves.Press(S(""), "q").ShouldBe(new KeyMinesDecision(KeyMinesMoveKind.Safe, "q", 1, null));
[Fact] public void JamPress_EndsJam() => KeyMinesMoves.Press(S("q"), "b").ShouldBe(new KeyMinesDecision(KeyMinesMoveKind.Jam, "qb", 1, "jam"));
[Fact] public void RepeatedKey_Refused() => KeyMinesMoves.Press(S("q"), "q").Kind.ShouldBe(KeyMinesMoveKind.Refused);
[Theory] [InlineData("Q")] [InlineData("1")] [InlineData("qq")] [InlineData("")] [InlineData(null)]
public void InvalidKey_Refused(string? key) => KeyMinesMoves.Press(S(""), key).Kind.ShouldBe(KeyMinesMoveKind.Refused);
[Fact] public void PressReachingCap_AutoCashesCap()
{   // 0.05 / 10 jams: last step 5
    var d = KeyMinesMoves.Press(new KeyMinesRoundState("abcdefghij", "qwrty", 10, 0.05m, 98m, 1m) with { PressedKeys = "qwrt" }, "y");
    d.ShouldBe(new KeyMinesDecision(KeyMinesMoveKind.CashOut, "qwrty", 5, "cap"));
}
[Fact] public void FullClear_AutoCashesClear()
{   // 1 jam "a", 24 safe already pressed, 25th clears
    var safe = new string(KeyMinesRules.Keys.Where(c => c != 'a').ToArray());
    var d = KeyMinesMoves.Press(new KeyMinesRoundState("a", safe[..24], 1, 0.002m, 98m, 1m), safe[24].ToString());
    d.ShouldBe(new KeyMinesDecision(KeyMinesMoveKind.CashOut, safe, 25, "clear"));
}
[Fact] public void CashOut_AtCurrentStep() => KeyMinesMoves.CashOut(S("qw")).ShouldBe(new KeyMinesDecision(KeyMinesMoveKind.CashOut, "qw", 2, "cashout"));
[Fact] public void CashOut_WithNoPresses_Refused() => KeyMinesMoves.CashOut(S("")).Kind.ShouldBe(KeyMinesMoveKind.Refused);
[Fact] public void CashOut_IdleEnd() => KeyMinesMoves.CashOut(S("qw"), "idle").End.ShouldBe("idle");
```

- [ ] **Step 5: Implement `KeyMinesRules.cs` and `KeyMinesMoves.cs`**

```csharp
public static class KeyMinesRules
{
    public const string BoardMode = "KEYS";
    public const string Keys = "qwertyuiopasdfghjklzxcvbnm";
    public const string JamChoicePrefix = "J";
    public const string JamWinningChoice = "JAM";
    public static string CashWinningChoice(int step) => "C" + step.ToString(CultureInfo.InvariantCulture);

    public static class Ends
    {
        public const string CashOut = "cashout", Cap = "cap", Clear = "clear", Idle = "idle", Jam = "jam", Refund = "refund";
    }

    public static IReadOnlyList<int> ParseJamOptions(string? csv)
        => string.IsNullOrWhiteSpace(csv) ? Array.Empty<int>()
         : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
              .Select(x => int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : -1).ToList();

    public static string FormatJamOptions(IEnumerable<int> options)
        => string.Join(",", options.Select(o => o.ToString(CultureInfo.InvariantCulture)));

    public static bool TryParseChoice(string? choice, IReadOnlyCollection<int> options, out int jams)
    {
        jams = 0;
        if (choice is null || choice.Length < 2 || !choice.StartsWith(JamChoicePrefix, StringComparison.Ordinal)) return false;
        if (!int.TryParse(choice.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var m)) return false;
        if (!options.Contains(m)) return false;
        jams = m;
        return true;
    }

    public static string? Validate(decimal rtpPercent, IReadOnlyCollection<int> jamOptions, decimal minStake, decimal? maxStake, decimal? cap)
    {
        if (rtpPercent <= 0 || rtpPercent >= 100) return "RTP must be in (0, 100).";
        if (jamOptions.Count == 0) return "Jam options are empty.";
        if (jamOptions.Distinct().Count() != jamOptions.Count) return "Jam options repeat a value.";
        if (jamOptions.Any(m => m < 1 || m > KeyMinesLadder.KeyCount - 1)) return "A jam option is outside 1..25.";
        if (maxStake is null || cap is null) return "MaximumStake and MaxPayoutPerRound are required.";
        if (minStake <= 0 || minStake > maxStake) return "Stake bounds are invalid.";
        foreach (var m in jamOptions)
            if (KeyMinesLadder.Pay(maxStake.Value, m, 1, rtpPercent) > cap.Value)
                return $"The cap does not allow a single step with {m} jams at the maximum stake.";
        return null;
    }
}

public enum KeyMinesMoveKind { Refused, Safe, Jam, CashOut }
public sealed record KeyMinesRoundState(string JamKeys, string PressedKeys, int Jams, decimal Stake, decimal RtpPercent, decimal Cap);
public sealed record KeyMinesDecision(KeyMinesMoveKind Kind, string Pressed, int Step, string? End);

public static class KeyMinesMoves
{
    private static KeyMinesDecision Refuse(KeyMinesRoundState s) => new(KeyMinesMoveKind.Refused, s.PressedKeys, s.PressedKeys.Length, null);

    public static KeyMinesDecision Press(KeyMinesRoundState s, string? key)
    {
        if (key is null || key.Length != 1 || !KeyMinesRules.Keys.Contains(key[0]) || s.PressedKeys.Contains(key[0]))
            return Refuse(s);
        var pressed = s.PressedKeys + key;
        if (s.JamKeys.Contains(key[0]))
            return new(KeyMinesMoveKind.Jam, pressed, s.PressedKeys.Length, KeyMinesRules.Ends.Jam);
        var step = pressed.Length;
        var last = KeyMinesLadder.LastStep(s.Stake, s.Jams, s.RtpPercent, s.Cap);
        if (step >= last)
            return new(KeyMinesMoveKind.CashOut, pressed, step,
                step == KeyMinesLadder.KeyCount - s.Jams ? KeyMinesRules.Ends.Clear : KeyMinesRules.Ends.Cap);
        return new(KeyMinesMoveKind.Safe, pressed, step, null);
    }

    public static KeyMinesDecision CashOut(KeyMinesRoundState s, string end = KeyMinesRules.Ends.CashOut)
        => s.PressedKeys.Length == 0 ? Refuse(s) : new(KeyMinesMoveKind.CashOut, s.PressedKeys, s.PressedKeys.Length, end);
}
```

Note in `Press` a jam's `Step` is the safe count **before** the jam press (pressed length − 1).

- [ ] **Step 6: Jam draw — failing test in `ProvablyFairTests.cs`**

```csharp
// V5 known-answer vector (specs/key-mines.md §11.1). Quote these in provablyFair.ts and verify.html.
private const string V5Seed = /* the same 64-hex seed the V3 vector uses in this file */;
private const string V5Prefix = "11111111-1111-1111-1111-111111111111|seedA";

[Fact]
public void KeyMinesJams_AreDistinctKeysAndDeterministic()
{
    foreach (var m in new[] { 1, 3, 5, 10 })
    {
        var jams = ProvablyFair.KeyMinesJams(V5Seed, V5Prefix, m);
        jams.Length.ShouldBe(m);
        jams.Distinct().Count().ShouldBe(m);
        jams.All(c => KeyMinesRules.Keys.Contains(c)).ShouldBeTrue();
        ProvablyFair.KeyMinesJams(V5Seed, V5Prefix, m).ShouldBe(jams);
    }
}

[Fact]
public void KeyMinesJams_V5Vector() // freeze: run once, paste the two outputs, re-run
{
    ProvablyFair.KeyMinesJams(V5Seed, V5Prefix, 3).ShouldBe("<frozen-3>");
    ProvablyFair.KeyMinesJams(V5Seed, V5Prefix, 10).ShouldBe("<frozen-10>");
}
```

Freezing procedure (the only allowed "fill in"): implement Step 7, run the test, copy the actual strings from the failure message into the two `ShouldBe`s, re-run green. Record both strings in the task report — the frontend plan needs them.

- [ ] **Step 7: Implement `KeyMinesJams` in `ProvablyFair.cs`**

```csharp
/// Key Mines jam draw (specs/key-mines.md §6.3): `jams` picks without replacement over
/// KeyMinesRules.Keys from the "|jams"-labelled keystream. KEEP-IN-SYNC with
/// monkify-front/src/utils/provablyFair.ts keyMinesJams and public/verify.html.
public static string KeyMinesJams(string serverSeed, string prefix, int jams)
{
    var keystream = new Keystream(serverSeed, prefix + "|jams");
    var pool = KeyMinesRules.Keys.ToList();
    var result = new StringBuilder(jams);
    for (var i = 0; i < jams; i++)
    {
        var index = NextIndex(keystream, pool.Count);
        result.Append(pool[index]);
        pool.RemoveAt(index);
    }
    return result.ToString();
}
```

- [ ] **Step 8: Run** `scripts\run_tests.bat` → all new tests green.
- [ ] **Step 9: Commit** `git add -A src/SpaceStroker.Domain src/SpaceStroker.Tests && git commit -m "Key Mines: ladder, rules, moves and jam draw (domain)."`

---

### Task 2: Data model, config, seeding, reconcile, parameters DTO

**Files:**
- Modify: `src/SpaceStroker.Domain/Games/Entities/GameParameters.cs` (by `IsHouseEdge` :50): `IsKeyMines`, `KeyMinesJamOptions`, `MaxPayoutPerRound`
- Modify: `src/SpaceStroker.Domain/Games/Entities/Game.cs` (by `WordLinesGrid` :198): `KeyMinesJamCount`, `KeyMinesJamKeys`, `KeyMinesPressedKeys`, `KeyMinesCashedStep`, `KeyMinesEnd`, `KeyMinesLastActionAt`, `KeyMinesTokenHash` (types per spec §4.1)
- Modify: `src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` (:74-79, :187-190) per spec §4.2, incl. the filtered index `"KeyMinesJamCount" IS NOT NULL AND "Status" = 4`
- Create: migration `KeyMines` (command in Global Constraints)
- Modify: `src/SpaceStroker.Domain/Configs/Entities/OperatorSettings.cs` — `HouseEdgeSettings` (:158) gains `public KeyMinesSettings? KeyMines { get; set; }` and `public bool IsKeyMines => KeyMines != null;`; new `public class KeyMinesSettings { public List<int>? JamOptions { get; set; } public decimal MaxPayoutPerRound { get; set; } }` (⚠️ do NOT initialise `JamOptions` — the binder appends to pre-initialised lists)
- Modify: `src/SpaceStroker.Domain/Configs/Entities/GameSettings.cs`: `public int KeyMinesInactivityMinutes { get; set; } = 5;` `public int KeyMinesSweepIntervalMinutes { get; set; } = 1;`
- Modify: `src/SpaceStroker.Common/spacestrokersettings.json`: the "Key Mines" `DefaultGames` entry (spec §5; copy the remaining required fields from the "Hyper Roll" entry at :276), `GeneralSettings:Games` `KeyMinesInactivityMinutes: 5`, `KeyMinesSweepIntervalMinutes: 1`, and two `IpRateLimiting:GeneralRules` (`post:/games/rounds/*/press` 240/1m, `post:/games/rounds/*/cashout` 30/1m) next to the boards rule (:101)
- Modify: `src/SpaceStroker.Consumers/Program.cs` `ToGameParameters` (:790, house-edge block :811-845): Key Mines branch **before** the Word Lines/ladder branches
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/HouseEdgeReconciler.cs` `Reconcile` (:47): Key Mines types heal only `RtpPercent`, `KeyMinesJamOptions`, `IsKeyMines`, `Description`; skip word/ladder/outcome/mode branches and the Word Lines cutover sweep; validate with `KeyMinesRules.Validate` and log Error on failure
- Modify: the place that refuses to open rounds for an invalid house-edge type (Word Lines validation at `Program.cs` :662-669 / `GameService.DrawHouseEdgeBoard`) so an invalid Key Mines type opens no round
- Modify: `src/SpaceStroker.Infrastructure/Contracts/Games/GameParametersDto.cs`: `IsKeyMines`, `JamOptions` (`IReadOnlyList<int>`), `MaxPayoutPerRound`; bump the `ActiveParameters:v4` key to `:v5` wherever it is defined (grep `ActiveParameters:v4`)
- Test: `UnitTests/Api/*Binding*Tests.cs` (the Hyper Roll config binding test file — grep `IsWordLines`), `DefaultGameSeedingTests`, `HouseEdgeReconcilerTests`, `GameParametersDtoTests`, `AdminGameTypeHandlerTests` (cache key)

**Interfaces — Produces:** the entity properties above (exact names), `HouseEdgeSettings.IsKeyMines`, `GameSettings.KeyMinesInactivityMinutes/KeyMinesSweepIntervalMinutes`, DTO fields `IsKeyMines`, `JamOptions`, `MaxPayoutPerRound`.

- [ ] **Step 1: Failing tests**
  - Binding: the committed config binds a "Key Mines" default with `HouseEdge.IsKeyMines`, `JamOptions == [1,3,5,10]`, `MaxPayoutPerRound == 1.0`, `RequiredAmount == 0.002`, `MaximumStake == 0.05`, `RtpPercent == 98`, `AllowOrbit == false`, and `KeyMinesRules.Validate(...)` on those values is null.
  - Seeding: `ToGameParameters` for that default yields `IsHouseEdge && IsKeyMines`, `KeyMinesJamOptions == "1,3,5,10"`, `MaxPayoutPerRound == 1`, no `PresetChoices`, `OutcomeTable == null`, `BoardLadder == null`.
  - Reconciler: a Key Mines row with `RtpPercent 90`, `KeyMinesJamOptions "3"`, `MaximumStake 0.2`, `MaxPayoutPerRound 5` → after `Reconcile`: RTP 98, jams `1,3,5,10`, **MaximumStake 0.2 and cap 5 untouched**, no `PresetChoices` / `GameBoardModes` rows added.
  - DTO: a Key Mines parameters row maps `IsKeyMines`, `JamOptions [1,3,5,10]`, `MaxPayoutPerRound`, `RtpPercent 98`, empty `Outcomes`/`Modes`/`PresetChoices`, `CommissionPercentage 0`.
  - Cache key test expects `ActiveParameters:v5`.
- [ ] **Step 2: Run** → fail.
- [ ] **Step 3: Implement** the files listed. `ToGameParameters` Key Mines branch:

```csharp
if (settings.HouseEdge?.IsKeyMines == true)
{
    var km = settings.HouseEdge.KeyMines!;
    parameters.IsHouseEdge = true;
    parameters.IsKeyMines = true;
    parameters.MaximumStake = settings.HouseEdge.MaximumStake;
    parameters.RtpPercent = settings.HouseEdge.RtpPercent;
    parameters.MaxPayoutPerRound = km.MaxPayoutPerRound;
    parameters.KeyMinesJamOptions = KeyMinesRules.FormatJamOptions(km.JamOptions ?? new List<int>());
    // no PresetChoices, no OutcomeTable, no ladder, no BoardModes
}
else if (/* existing house-edge branch */) { … }
```
- [ ] **Step 4: Generate the migration** (command above); confirm it only adds columns + the index, and the `Status = 4` filter matches `GameStatus.InProgress` (WaitingBets=1 … InProgress=4).
- [ ] **Step 5: Run** `scripts\run_tests.bat` → green (fix any existing seeding/binding tests that count default games: there is now one more).
- [ ] **Step 6: Commit** `"Key Mines: entities, migration, config, seeding and reconcile."`

---

### Task 3: Open a board — KEYS round + play token; round DTO

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/GameService.cs` `DrawHouseEdgeBoard` (:615, branch first on `parameters.IsKeyMines`): no `GameBoardChoices`, `BoardMode = KeyMinesRules.BoardMode`
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/OpenHouseEdgeBoard/OpenHouseEdgeBoardHandler.cs`: for `IsKeyMines`, after `OpenSoloBoard`, generate the token and store its hash:

```csharp
string? playToken = null;
if (parameters!.IsKeyMines)
{
    playToken = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32)); // Microsoft.IdentityModel.Tokens or a local helper
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(playToken))).ToLowerInvariant();
    await Context.Games.Where(g => g.Id == gameId!.Value)
        .ExecuteUpdateAsync(s => s.SetProperty(g => g.KeyMinesTokenHash, hash), cancellationToken);
}
var game = await LoadRound(Context, gameId!.Value, cancellationToken);
return new ActiveGameDto(game!, null) { PlayToken = playToken };
```
(If `Base64UrlEncoder` is not referenced by Infrastructure, use `Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_')`. Put the hash in a static `KeyMinesTokens.Hash(string token)` in `Services/Games/KeyMinesTokens.cs` with `Matches(string? token, string? storedHash)` using `CryptographicOperations.FixedTimeEquals`; Task 5 reuses it.)
- Modify: `src/SpaceStroker.Infrastructure/Contracts/Games/ActiveGameDto.cs`: `PlayToken` (init-only, null except on board open), `KeyMinesJamCount`, `KeyMinesPressedKeys`, `KeyMinesStep` (= pressed length, minus 1 when `KeyMinesEnd == "jam"`), `KeyMinesEnd`, `KeyMinesCashedStep`, `KeyMinesJamKeys` (**only** when `Status ∈ Game.GameEndedStatus`). Never map `KeyMinesTokenHash`.
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/GetHouseEdgeRound/GetHouseEdgeRoundHandler.cs` (:44): a `KEYS` round stays servable in every status (live and settled), like a settled LINES round.
- Test: `OpenHouseEdgeBoardHandlerTests` (Key Mines: no board rows, `boardMode KEYS`, `serverSeedHash` set, `PlayToken` non-null 43 chars, a Hyper Roll board returns `PlayToken == null`), `GetHouseEdgeRoundHandlerTests` (live KEYS round: pressed keys + step, `KeyMinesJamKeys == null`; ended: jams present), new `KeyMinesTokensTests` (hash stable, `Matches` true/false/null).

⚠️ InMemory can't run `ExecuteUpdateAsync`: in the handler unit test, assert the returned token + DTO; assert the stored hash in the integration test (Task 9).

- [ ] Steps: failing tests → implement → `scripts\run_tests.bat` green → commit `"Key Mines: open a KEYS board with a play token."`

---

### Task 4: Placing the bet — choice, coverage, round start, Orbit passthrough

**Files:**
- Modify: `src/SpaceStroker.Domain/Bets/Services/BetDomainService.cs`: at the **top** of `ChoiceIsValidForGame` (:81) — for `Parameters.IsKeyMines`: stake via `PaidStakeIsValid` (free bet: amount 0) and `KeyMinesRules.TryParseChoice(choice, ParseJamOptions(...))` else `InvalidChoice`; return before the length check. `_isWordLines` (:27) must be false for Key Mines.
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs`:
  - :153-157 — the empty-choice/`spin` rewrite runs only for Word Lines; a Key Mines choice passes through untouched (an empty choice is refused).
  - :736 — the new bet's coverage multiplier for Key Mines: `KeyMinesLadder.WorstCaseMultiplier(amount, jams, rtp, cap)` instead of `WordLinesMaxPay()`.
  - after `ClaimSoloRound` (:1335) in the same transaction — **round start (spec S4)**:

```csharp
if (parameters.IsKeyMines)
{
    var prefix = ProvablyFair.BuildPrefix(game.Id, new[] { bet.Seed });
    var jamKeys = ProvablyFair.KeyMinesJams(game.ServerSeed!, prefix, jams);
    var now = DateTime.UtcNow;
    var started = await Context.Games
        .Where(g => g.Id == game.Id && g.Status == GameStatus.WaitingBets)
        .ExecuteUpdateAsync(s => s
            .SetProperty(g => g.Status, GameStatus.InProgress)
            .SetProperty(g => g.StartDate, now)
            .SetProperty(g => g.KeyMinesJamCount, jams)
            .SetProperty(g => g.KeyMinesJamKeys, jamKeys)
            .SetProperty(g => g.KeyMinesPressedKeys, "")
            .SetProperty(g => g.KeyMinesLastActionAt, now), cancellationToken);
    if (started == 0) /* roll back + the vague refusal path used by ClaimSoloRound failures (paid ⇒ refunded) */;
}
```
  Write a `GameStatusLogs` row for InProgress the way `TryStartGame`/`RunGameConsumer` does (grep `GameStatusLog`), and send the status frame through the same hub call `UpdateGameStatus` uses so the SPA sees InProgress.
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/BetPreChecks.cs` (:52/:84/:92): no `spin` rewrite for Key Mines; skip duplicate-choice and per-round checks as for house-edge; validate `J{M}`.
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/CheckBetAvailability/CheckBetAvailabilityHandler.cs` (:60/:105/:118): Key Mines prices `amount × WorstCaseMultiplier`; invalid choice ⇒ `{ available: false }`.
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/HouseEdgeCoverageService.cs` `LoadAsync` (~:105): for a `KEYS` round, the book's `FlatMultiplier = KeyMinesLadder.WorstCaseMultiplier(stake, jamCount, rtp, cap)` (rounds still `WaitingBets` with no bet contribute nothing).
- Modify: `src/SpaceStroker.Infrastructure/Consumers/BetPlaced/BetPlacedConsumer.cs`: early `return` for `IsKeyMines` before :126.
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Orbit/PlaceOrbitWager/PlaceOrbitWagerHandler.cs` `BetChoiceFor` (:267) and `src/SpaceStroker.Infrastructure/Consumers/OrbitBetPlacement/PlaceManualOrbitBetConsumer.cs` (:181): Key Mines keeps the body's choice.
- Test: `BetDomainServiceTests` (J3 valid, spin/word/J2 invalid, bounds), `RegisterBetHandlerTests` (J3 passes, invalid M refused + refund event for paid), `BetPreChecksTests`, `CheckBetAvailabilityHandlerTests` (0.05 J10 priced at 14.758…× → exposure ≤ 1), `HouseEdgeCoverageTests`/`HouseEdgeCoverageServiceTests` (KEYS book), `BetPlacedHandlerTests` (Key Mines ⇒ no `TryStartGame`, no `RunGameEvent`), `PlaceOrbitWagerTests` / `PlaceManualOrbitBetConsumerTests` (J3 kept).

⚠️ The round-start `ExecuteUpdate` cannot run on InMemory: in `RegisterBetHandlerTests` assert up to the refusal/acceptance decision and leave the persisted start to Task 9.

- [ ] Steps: failing tests → implement → green → commit `"Key Mines: bets start the round with derived jams."`

---

### Task 5: Press and cash-out — round service, handlers, routes

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Services/Games/IKeyMinesRoundService.cs`, `KeyMinesRoundService.cs` (register scoped in `DependencyInjection.cs` beside `IGameService`)
- Create: `src/SpaceStroker.Infrastructure/Handlers/Games/PressKeyMinesKey/{PressKeyMinesKeyRequest,PressKeyMinesKeyHandler}.cs`, `Handlers/Games/CashOutKeyMines/{CashOutKeyMinesRequest,CashOutKeyMinesHandler}.cs`
- Create: `src/SpaceStroker.Infrastructure/Contracts/Games/KeyMinesMoveDto.cs`
- Modify: `src/SpaceStroker.Api/Controllers/GamesController.cs` (after `duel-cancel` :121)
- Modify: `src/SpaceStroker.Common/Resources/ErrorMessages.resx` + `ErrorMessages.Designer.cs`: `KeyMinesRequestInvalid` = `That move could not be processed.`
- Test: `UnitTests/Handlers/PressKeyMinesKeyHandlerTests.cs`, `CashOutKeyMinesHandlerTests.cs` (service mocked)

**Interfaces — Produces:**
```csharp
public sealed record KeyMinesMoveDto(string Result, int Step, decimal Multiplier, decimal? Payout,
    bool Ended, string? End, string? JamKeys, string? ServerSeed);   // Result: "safe" | "jam" | "cashed"

public interface IKeyMinesRoundService
{
    /// null = refused (caller returns the vague 400).
    Task<KeyMinesMoveDto?> Press(Guid roundId, string? key, string? playToken, CancellationToken ct);
    Task<KeyMinesMoveDto?> CashOut(Guid roundId, string? playToken, CancellationToken ct);
    /// Sweep entry: no token check, end = "idle".
    Task<bool> AutoCashIdle(Guid roundId, CancellationToken ct);
    /// Sweep entry: 0-press refund (spec S8).
    Task<bool> RefundIdle(Guid roundId, CancellationToken ct);
}
public record PressKeyMinesKeyRequest(Guid RoundId, string? Key, string? PlayToken) : IRequest<KeyMinesMoveDto>;
public record CashOutKeyMinesRequest(Guid RoundId, string? PlayToken) : IRequest<KeyMinesMoveDto>;
```

Controller:
```csharp
public record KeyMinesPressBody(string? Key);

/// Key Mines (#204): press one key on your own live round. Header X-Round-Token = the board's play token. Rate-limited.
[HttpPost("rounds/{roundId:guid}/press")]
public async Task<IActionResult> PressKeyMinesKey(Guid roundId, [FromBody] KeyMinesPressBody? body,
    [FromHeader(Name = "X-Round-Token")] string? playToken)
    => await ProcessRequest(new PressKeyMinesKeyRequest(roundId, body?.Key, playToken));

[HttpPost("rounds/{roundId:guid}/cashout")]
public async Task<IActionResult> CashOutKeyMines(Guid roundId, [FromHeader(Name = "X-Round-Token")] string? playToken)
    => await ProcessRequest(new CashOutKeyMinesRequest(roundId, playToken));
```

Handlers: call the service; `null` ⇒ `Messaging.ReturnValidationFailureMessage(ErrorMessages.KeyMinesRequestInvalid)`.

`KeyMinesRoundService` algorithm (spec §6.4/S5):

1. Load `Game` + `Parameters` + its single `Bet` (`AsNoTracking`). Refuse (null) unless `Parameters.IsKeyMines`, `BoardMode == "KEYS"`, `KeyMinesJamCount != null`, and (for Press/CashOut) `KeyMinesTokens.Matches(playToken, game.KeyMinesTokenHash)`.
2. **Ended round:** if `Status ∈ GameEndedStatus` and the request is an exact repeat (Press: `key == last char of KeyMinesPressedKeys`; CashOut: `KeyMinesEnd ∈ {cashout}`), return the stored outcome DTO (built from columns); else null.
3. `Status != InProgress` ⇒ null.
4. `state = new KeyMinesRoundState(JamKeys, PressedKeys, JamCount, bet.Amount, Parameters.RtpPercent!.Value, Parameters.MaxPayoutPerRound!.Value)`; `decision = KeyMinesMoves.Press(state, key)` or `.CashOut(state)`.
5. `Refused` ⇒ null. `Safe` ⇒

```csharp
var rows = await _context.Games
    .Where(g => g.Id == roundId && g.Status == GameStatus.InProgress && g.KeyMinesPressedKeys == state.PressedKeys)
    .ExecuteUpdateAsync(s => s.SetProperty(g => g.KeyMinesPressedKeys, decision.Pressed)
                              .SetProperty(g => g.KeyMinesLastActionAt, DateTime.UtcNow), ct);
return rows == 0 ? null : new KeyMinesMoveDto("safe", decision.Step, Math.Round(KeyMinesLadder.Multiplier(jams, decision.Step, rtp), 4), null, false, null, null, null);
```
6. `Jam` / `CashOut` ⇒ `EndRound(game, bet, decision, state.PressedKeys, ct)` in one DB transaction:
   - conditional update on `(Id, Status InProgress, KeyMinesPressedKeys == expected)` setting `Status = Ended`, `EndDate = now`, `KeyMinesPressedKeys = decision.Pressed`, `KeyMinesEnd = decision.End`, `KeyMinesCashedStep = (Jam ? null : decision.Step)`, `WinningChoice = Jam ? "JAM" : CashWinningChoice(step)`; 0 rows ⇒ rollback, null.
   - CashOut: `Bets.Where(b => b.Id == bet.Id).ExecuteUpdate(Status = NeedsRewarding)`.
   - write the `GameStatusLogs` Ended row and send the status frame through the same path `GameService.UpdateGameStatus` uses (read `UpdateGameStatusCore` :308 — call the part that logs + broadcasts, not the typer part; add an internal method `GameService.NotifyKeyMinesEnded(Guid gameId)` if needed).
   - commit, then `_bus.Publish(new RewardGameEvent(roundId))`.
   - Return `KeyMinesMoveDto(Jam ? "jam" : "cashed", step, multiplier, Jam ? null : KeyMinesLadder.Pay(stake, jams, step, rtp), true, end, JamKeys, ServerSeed)`.
7. `AutoCashIdle`: same as CashOut with `end = "idle"`, no token, and only if `KeyMinesLastActionAt < now − inactivity` (re-checked in the WHERE).
8. `RefundIdle` (S8): conditional update `(Id, Status InProgress, KeyMinesPressedKeys == "", KeyMinesLastActionAt < cutoff)` → `Status = GameEndedAbruptely`, `EndDate`, `KeyMinesEnd = "refund"`; bet → `NeedsRefunding`; publish `RefundInvalidBetEvent(bet.Id, wallet, bet.Amount, "Key Mines round idle with no key pressed.", game.Id, bet.Signature, bet.FundedByOrbit)` — copy how an existing persisted-bet refund publisher fills `Wallet` (grep `new RefundInvalidBetEvent(` in `GameTypeDeactivatedConsumer`); a streamer bet is refunded by `RefundInvalidBetConsumer`'s streamer branch automatically.

Unit tests (mock `IKeyMinesRoundService`): handler returns the DTO; null ⇒ validation failure with `KeyMinesRequestInvalid`. The service itself is covered by Task 9.

- [ ] Steps: failing handler tests → implement service + handlers + controller + resource → `scripts\run_tests.bat` green → commit `"Key Mines: press and cash-out endpoints."`

---

### Task 6: Settlement

**Files:**
- Modify: `src/SpaceStroker.Domain/Bets/Services/BetDomainService.cs` `CalculateHouseEdgeReward` (:313): branch first on Key Mines:

```csharp
if (_game.Parameters.IsKeyMines)
{
    if (_game.KeyMinesCashedStep is not int step || _game.KeyMinesJamCount is not int jams) return /* no reward result as for a loser */;
    var pay = KeyMinesLadder.Pay(bet.Amount, jams, step, _game.Parameters.RtpPercent!.Value);
    return pay - alreadyCredited; // same result shape as the existing branch
}
```
  and the winner set (:53) for Key Mines = bets with status `NeedsRewarding` (a jam round has none).
- Verify `RewardGameConsumer` (:68) sets `RewardForWinnersCompleted` when there is no `NeedsRewarding` bet; if not, add it for `KEYS` rounds.
- Test: `BetDomainServiceTests` (cash-out at 3 with 0.01/J3 ⇒ 0.014387351; redelivery with a prior credit of that amount ⇒ 0; jam ⇒ no winner), `RewardGameConsumerTests` (KEYS cash-out: wallet `settlement.Credit`, Orbit-funded `CreditWin`, streamer play ledger; jam round ⇒ `RewardForWinnersCompleted`, nothing paid).

- [ ] Steps: failing tests → implement → green → commit `"Key Mines: settle cash-outs at stake × ladder[k]."`

---

### Task 7: Inactivity sweep + restart survival

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Jobs/KeyMinesInactivitySweepJob.cs` (shape: `HouseEdgeBoardCleanupJob.cs`)
- Modify: `src/SpaceStroker.Consumers/Program.cs` (:91 `AddScoped<KeyMinesInactivitySweepJob>()`; :355 `recurringJobs.AddOrUpdate<KeyMinesInactivitySweepJob>("key-mines-inactivity-sweep", job => job.Run(), KeyMinesInactivitySweepJob.CronFor(games.KeyMinesSweepIntervalMinutes));`)
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/GameService.cs` `CloseOngoingGames` (:578): `.Where(g => !g.Parameters.IsKeyMines)` on the selection
- Test: `UnitTests/Jobs/KeyMinesInactivitySweepJobTests.cs`, `GameServiceTests` (CloseOngoingGames)

```csharp
public class KeyMinesInactivitySweepJob
{
    public const int BatchSize = 100;
    private readonly SpaceStrokerDbContext _context;
    private readonly IKeyMinesRoundService _rounds;
    private readonly GameSettings _settings;

    public static string CronFor(int minutes) => minutes <= 1 ? Cron.Minutely() : $"*/{minutes} * * * *";

    /// Rounds a sweep should act on (pure, unit-tested): KEYS + InProgress + idle past the window.
    public static IQueryable<Game> Due(IQueryable<Game> games, DateTime cutoff)
        => games.Where(g => g.KeyMinesJamCount != null && g.Status == GameStatus.InProgress && g.KeyMinesLastActionAt < cutoff);

    public async Task<int> Run()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-_settings.KeyMinesInactivityMinutes);
        var due = await Due(_context.Games.AsNoTracking(), cutoff)
            .Select(g => new { g.Id, Pressed = g.KeyMinesPressedKeys }).Take(BatchSize).ToListAsync();
        var acted = 0;
        foreach (var round in due)
        {
            try
            {
                var ok = string.IsNullOrEmpty(round.Pressed)
                    ? await _rounds.RefundIdle(round.Id, CancellationToken.None)
                    : await _rounds.AutoCashIdle(round.Id, CancellationToken.None);
                if (ok) { acted++; Log.Warning("KEY MINES — idle round {RoundId} {Action}", round.Id, string.IsNullOrEmpty(round.Pressed) ? "refunded" : "auto-cashed"); }
            }
            catch (Exception ex) { Log.Error(ex, "KEY MINES — sweep failed for round {RoundId}", round.Id); }
        }
        return acted;
    }
}
```
(Match how the existing job reads `GameSettings` — `IOptions<GeneralSettings>` or similar; grep the cleanup job constructor.)

Unit tests: `Due` over an in-memory list — idle KEYS InProgress ⇒ included; fresh ⇒ excluded; Ended ⇒ excluded; non-KEYS ⇒ excluded. `Run` with a mocked `IKeyMinesRoundService`: pressed "" ⇒ `RefundIdle`, "qw" ⇒ `AutoCashIdle`. `CloseOngoingGames`: an InProgress Key Mines round stays InProgress, an InProgress Hyper Roll round becomes `GameEndedAbruptely`.

- [ ] Steps: failing tests → implement → green → commit `"Key Mines: inactivity sweep and restart survival."`

---

### Task 8: Read paths — verification, history, stats, profile

**Files:**
- Modify: `Handlers/Games/GetGameVerification/GetGameVerificationHandler.cs` + `Contracts/Games/GameVerificationDto.cs`: `KeyOrder` (= `KeyMinesRules.Keys`), `JamCount`, `PressedKeys`, `KeyMinesEnd`, `CashedStep`, and `JamKeys` only after the reveal (same gate as `ServerSeed`, :62). Cache gate (:169): for `KEYS` require `KeyMinesEnd != null` instead of `CharactersTyped.HasValue`.
- Modify: `Services/Games/GameHistoryService.cs` + `GameHistoryDto` (+ `ClonePublic`): `KeyMinesJamCount`, `KeyMinesCashedStep`, `KeyMinesEnd`; the "won = credited > stake" rule currently scoped to `LINES` extends to `KEYS` (grep `WordLinesMode` in this file).
- Modify: `Services/Players/PlayerStatsService.cs`, `Services/Games/GlobalStatsService.cs`, `GameStatsService.cs`: same won-rule extension (grep `WordLinesMode` / `"LINES"`).
- Modify: `Services/Players/GameModeClassifier.cs` (:8-27): `public const string KeyMines = "keyMines";`, expression checks `IsKeyMines` before `IsHouseEdge`; `ProfileModes` includes it.
- Modify: `Services/Players/PlayerProfileService.cs` (:150-160, :183-193, :246): `allModes` for explorer stays exactly sprint/hyperRoll/duel/jackpot; `firstHyperRollWin`/`hyperHit` never match `keyMines`; streaks include Key Mines rounds (won = credited > stake for `KEYS`).
- Test: `GetGameVerificationHandlerTests` (KEYS live: no jams/seed; ended: jams + seed; cacheable when ended), `GameHistoryServiceTests` (KEYS won/lost), stats tests (KEYS won rule), `GameModeClassifierTests`, `PlayerProfileServiceTests` (Key Mines win: no `firstHyperRollWin`, explorer unchanged with the four classic modes played, `winStreak3` counts a Key Mines win).

- [ ] Steps: failing tests → implement → green → commit `"Key Mines: verification, history, stats and profile."`

---

### Task 9: Integration tests (mandatory) — `KeyMinesFlowTests`

**Files:**
- Modify: `src/SpaceStroker.Tests.Integration/Harness/IntegrationGameParameters.cs`: `public const string KeyMines = "Key Mines Test";` + a factory added to `All()`:

```csharp
private static GameParameters KeyMinesType(string name) => new()
{
    Name = name, Active = true, IsHouseEdge = true, IsKeyMines = true, AllowOrbit = false,
    RequiredAmount = 0.002m, MaximumStake = 0.05m, MaxPayoutPerRound = 1m, RtpPercent = 98m,
    KeyMinesJamOptions = "1,3,5,10", MinimumNumberOfPlayers = 1,
    /* copy the remaining required fields from the HouseEdge(...) factory at :136 */
};
```
- Create: `src/SpaceStroker.Tests.Integration/KeyMinesFlowTests.cs` (`[Collection(IntegrationCollection.Name)]`, pattern `HyperRollWordLinesFlowTests.cs`). Helpers: `OpenBoard()` → `(roundId, playToken)`; `PlaceWalletBet(roundId, "J3", 0.01m)`; `JamKeys(roundId)` read from the DB; `SafeKeys(roundId)` = `KeyMinesRules.Keys` minus jams; `Press(roundId, key, token)`; `CashOut(roundId, token)`; `WaitForStatus(roundId, RewardForWinnersCompleted)`.

Cases (each its own `[Fact]`), with exact assertions:
1. `HappyPath_ThreeSafePressesThenCashOut_PaysStakeTimesLadder` — token hash stored = `KeyMinesTokens.Hash(token)`; after the bet the round is InProgress with 3 jams; 3 presses each `"safe"` with steps 1/2/3; cash-out `"cashed"`, payout 0.014387351; `RewardForWinnersCompleted`; exactly one `FakeSolanaService.Transfers` credit of 0.014387351 to the wallet; one `Credit` `TransactionLog` row; history row won.
2. `Jam_EndsRound_PaysNothing_RevealsJams` — press a jam ⇒ `"jam"`, `JamKeys` + `ServerSeed` in the response; `RewardForWinnersCompleted`; no credit transfer.
3. `CapStep_AutoCashes` — 0.05 `J10`, 5 safe presses ⇒ 5th returns `"cashed"`, end `cap`, payout `KeyMinesLadder.Pay(0.05m, 10, 5, 98m)`.
4. `Idle_WithPresses_AutoCashes` / `Idle_WithoutPresses_Refunds` — set `KeyMinesLastActionAt` back 6 min via SQL, run `KeyMinesInactivitySweepJob.Run()` from the Consumers host scope ⇒ (a) end `idle`, paid `Pay(stake,3,2)`; (b) `GameEndedAbruptely`, bet `Refunded`, a refund transfer of the stake.
5. `CloseOngoingGames_LeavesKeyMinesRoundLive` — 2 presses, call `IGameService.CloseOngoingGames()` ⇒ still InProgress, bet not `NeedsManualAnalysis`; press + cash out ⇒ paid.
6. `OrbitManualBet_CashOut_CreditsCustody` — funded Orbit player (copy the Orbit-wager setup from the Hyper Roll Orbit integration test), `POST /orbit/wagers` with `J3`, poll placed, presses, cash out ⇒ a `WinCredit` Orbit ledger row = payout, no wallet credit transfer.
7. `StreamerPlay_CashOut_CreditsPlayLedger` — copy the streamer setup from `StreamerWalletsFlowTests`; ⇒ play-ledger `WinCredit`, no transfer, no `TransactionLog` row.
8. `ConcurrentCashOuts_PayOnce` — two cash-outs via `Task.WhenAll` ⇒ one 200 `"cashed"` and one 200 with the same stored result (exact repeat) **or** 400; exactly one credit transfer. Plus `PressWithoutToken_IsRefused` and `PressWithWrongToken_IsRefused` (400, `KeyMinesRequestInvalid`, nothing changed).
9. `Verification_RoundTrip` — before the end `GET /games/{id}/verification` and `GET /games/rounds/{id}` carry no jams; after a cash-out, verification `JamKeys == ProvablyFair.KeyMinesJams(serverSeed, BuildPrefix(id, [betSeed]), 3)`.

Also update `DefaultGameSeedingTests`/any integration test that counts game types.

- [ ] **Step 1:** Write the tests. **Step 2:** `scripts\run_integration_tests.bat` → fail where code is missing; fix the production code (not the tests) until green. **Step 3:** run the full unit suite again. **Step 4:** commit `"Key Mines: integration tests."`

---

## Self-review notes

- Spec §6.2 token → Task 3; §6.3 → Task 4; §6.4 → Task 5; §6.5 → Task 6; §6.6/§6.7 → Task 7; §6.9 → Tasks 2/3/8; §6.8 step-asides → Tasks 4/5/7 (typer never constructed because no `RunGameEvent` is ever published for KEYS — Task 9 case 1 asserts no `RunGameEvent`-driven status `GameStarting` appears in `GameStatusLogs`).
- The two frozen V5 jam strings from Task 1 are an input to the frontend plan Task 1.

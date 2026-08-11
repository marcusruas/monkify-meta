# Coinbase Wallet Funding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `spacestroker-fund-wallets`, a standalone .NET 8 console app that funds a pool of Solana wallets with SOL out of a Coinbase account — dry-run by default, double-send-proof, resumable, on-chain-confirmed and time-spread.

**Architecture:** A new `monkify-meta/tools/` solution (sibling of `deploy/`) holding one console project and one xunit test project. The design splits **pure logic** (pool parsing, plan + idem derivation, preflight, receipts/resume, spread schedule) from **I/O** (Coinbase HTTP, Solana RPC, console). All money-safety rules live in the pure half so they are unit-testable **without any fake Coinbase sender** — which the card explicitly rules out. The key move: `Preflight` accepts the account balance as a **decimal value**, not a client, so the balance-shortfall rule is a pure function.

**Tech Stack:** .NET 8 (`net8.0`), System.Text.Json, `BouncyCastle.Cryptography` 2.4.0 (Ed25519 — absent from the .NET 8 BCL), `Solnet.Rpc` + `Solnet.Wallet` 6.1.0 (address validation + balance reads, matching `monkify-backend`), xunit 2.4.2 + Shouldly 4.2.1 (matching `deploy/tools/SpaceStroker.Deploy.Tests`).

**Spec:** [`specs/coinbase-wallet-funding.md`](../../../specs/coinbase-wallet-funding.md) · **Trello:** [#165](https://trello.com/c/9OoHAuEa) · **Level:** `Feature` (backend integration test **waived** — zero backend surface; unit tests required).

## Global Constraints

- **Target framework `net8.0`** on both projects. `ImplicitUsings enable`, `Nullable enable`, `InvariantGlobalization true` on the console project.
- **Console project:** `RootNamespace SpaceStroker.WalletFunder`, `AssemblyName spacestroker-fund-wallets`, `OutputType Exe`.
- **Package versions are exact:** `BouncyCastle.Cryptography` 2.4.0, `Solnet.Rpc` 6.1.0, `Solnet.Wallet` 6.1.0, `Microsoft.NET.Test.Sdk` 17.6.0, `Shouldly` 4.2.1, `xunit` 2.4.2, `xunit.runner.visualstudio` 2.4.5.
- **`tools/.gitignore` is mandatory and must land in Task 1.** The repo-root `.gitignore` ignores only `sales/pdf/`; without the nested file a live Coinbase withdrawal credential becomes committable.
- **Committed config carries placeholders only** (`REPLACE_ME…`). Never write a real credential into a tracked file.
- **The CDP API secret and the minted JWT are never printed, logged, or written to receipts.**
- **All decimal→string formatting uses `CultureInfo.InvariantCulture`.** A `pt-BR` machine would otherwise emit `"2,54"` in the outbound `amount`.
- **Money amounts are `decimal`, never `double`.** Integer lamports (`× 1_000_000_000`) are the comparison and hashing unit.
- **No fake Coinbase sender and no `--fake` mode** (card decision). Live paths are verified by dry run; tests cover pure logic only.
- **Work happens in `.worktrees/coinbase-wallet-funding/monkify-meta`.** The hub stays on `main`.

---

### Task 1: Solution scaffold, gitignore, config template

**Files:**
- Create: `tools/SpaceStroker.Tools.sln`
- Create: `tools/.gitignore`
- Create: `tools/secrets.example.json`
- Create: `tools/SpaceStroker.WalletFunder/SpaceStroker.WalletFunder.csproj`
- Create: `tools/SpaceStroker.WalletFunder/FunderException.cs`
- Create: `tools/SpaceStroker.WalletFunder.Tests/SpaceStroker.WalletFunder.Tests.csproj`
- Create: `tools/SpaceStroker.WalletFunder.Tests/ScaffoldTests.cs` (deleted in Task 2 once real tests exist)

**Interfaces:**
- Consumes: nothing.
- Produces: `FunderException(string message) : Exception` — the fatal, user-facing error type thrown by every later component and caught once in `Program.cs`.

- [ ] **Step 1: Create the gitignore FIRST (before any secrets file exists)**

`tools/.gitignore`:

```gitignore
# Local secrets — the operator's source of truth. NEVER commit (only the template is tracked).
secrets*.json
!secrets.example.json

# Run state / receipts — contains addresses, amounts and Coinbase transaction ids.
receipts*.json

# .NET build output for the console apps
**/bin/
**/obj/
```

- [ ] **Step 2: Verify the ignore rule actually works**

Run:
```bash
cd <workspace-root>/.worktrees/coinbase-wallet-funding/monkify-meta
echo '{}' > tools/secrets.json
git check-ignore -v tools/secrets.json
rm tools/secrets.json
```
Expected: prints `tools/.gitignore:2:secrets*.json	tools/secrets.json`. If it prints nothing, the ignore is not in effect — **stop and fix before continuing**.

- [ ] **Step 3: Create the console csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>SpaceStroker.WalletFunder</RootNamespace>
    <AssemblyName>spacestroker-fund-wallets</AssemblyName>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BouncyCastle.Cryptography" Version="2.4.0" />
    <PackageReference Include="Solnet.Rpc" Version="6.1.0" />
    <PackageReference Include="Solnet.Wallet" Version="6.1.0" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Create `FunderException.cs`**

```csharp
namespace SpaceStroker.WalletFunder;

/// <summary>Fatal, user-facing error. Caught once in Program.cs and printed as "error: {message}".</summary>
public sealed class FunderException(string message) : Exception(message);
```

- [ ] **Step 5: Create the test csproj** (copy of `deploy/tools/SpaceStroker.Deploy.Tests/SpaceStroker.Deploy.Tests.csproj` with the ProjectReference repointed)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.6.0" />
    <PackageReference Include="Shouldly" Version="4.2.1" />
    <PackageReference Include="xunit" Version="2.4.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.4.5">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\SpaceStroker.WalletFunder\SpaceStroker.WalletFunder.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 6: Add a scaffold test so the harness is proven before real work**

```csharp
using Shouldly;
using Xunit;

namespace SpaceStroker.WalletFunder.Tests;

public class ScaffoldTests
{
    [Fact]
    public void FunderException_carries_its_message()
        => new FunderException("boom").Message.ShouldBe("boom");
}
```

- [ ] **Step 7: Create the solution and add both projects**

```bash
cd <workspace-root>/.worktrees/coinbase-wallet-funding/monkify-meta/tools
dotnet new sln -n SpaceStroker.Tools --force
dotnet sln add SpaceStroker.WalletFunder/SpaceStroker.WalletFunder.csproj
dotnet sln add SpaceStroker.WalletFunder.Tests/SpaceStroker.WalletFunder.Tests.csproj
```

- [ ] **Step 8: Restore, build and test — proves the exact package versions resolve**

Run: `dotnet test SpaceStroker.Tools.sln`
Expected: restore succeeds for BouncyCastle 2.4.0 + Solnet 6.1.0, build succeeds, 1 test passes. A restore failure here is a **blocker** — resolve the version before writing any logic.

- [ ] **Step 9: Create `secrets.example.json` (placeholders only)**

```json
{
  "coinbase": {
    "apiKeyId": "organizations/REPLACE_ME_ORG_ID/apiKeys/REPLACE_ME_KEY_ID",
    "apiKeySecret": "REPLACE_ME_BASE64_ED25519_PRIVATE_KEY",
    "keyAlgorithm": "ed25519",
    "accountId": "",
    "apiHost": "api.coinbase.com",
    "apiVersion": "2024-01-01"
  },
  "solana": {
    "rpcUrl": "https://api.mainnet-beta.solana.com"
  },
  "funding": {
    "spreadMinutes": 45,
    "networkFeeHeadroomSol": 0.05,
    "minAmountSol": 0.001,
    "sendPollTimeoutSeconds": 300,
    "chainConfirmTimeoutSeconds": 120
  },
  "wallets": [
    { "Label": "W01", "PublicKey": "REPLACE_ME_BASE58_ADDRESS", "SOLAmount": 2.54 }
  ]
}
```

- [ ] **Step 10: Commit**

```bash
git add tools/
git commit -m "Scaffold tools solution for the Coinbase wallet funder."
```

---

### Task 2: Wallet pool parsing

**Files:**
- Create: `tools/SpaceStroker.WalletFunder/WalletPool.cs`
- Create: `tools/SpaceStroker.WalletFunder.Tests/WalletPoolTests.cs`
- Delete: `tools/SpaceStroker.WalletFunder.Tests/ScaffoldTests.cs`

**Interfaces:**
- Consumes: `FunderException`.
- Produces:
  - `sealed record PoolEntry(string Label, string PublicKey, decimal SolAmount)`
  - `static class WalletPool`
    - `IReadOnlyList<PoolEntry> Parse(string json)` — array form, for `--pool`
    - `IReadOnlyList<PoolEntry> FromEntries(IEnumerable<RawPoolEntry> raw)`
    - `long ToLamports(decimal sol)` — throws `FunderException` when not integral
    - `string FormatSol(decimal sol)` — invariant, trailing zeros trimmed
  - `sealed class RawPoolEntry { string? Label; string? PublicKey; decimal SOLAmount; }` — the JSON binding type used by both `WalletPool.Parse` and `SecretsFile.Wallets`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Globalization;
using Shouldly;
using Xunit;

namespace SpaceStroker.WalletFunder.Tests;

public class WalletPoolTests
{
    [Fact]
    public void Ignores_unknown_fields_including_PrivateKey()
    {
        var json = """
        [ { "PublicKey": "AAA", "PrivateKey": "SUPER_SECRET", "SupplyPercentage": 0.45, "SOLAmount": 2.54 } ]
        """;

        var pool = WalletPool.Parse(json);

        pool.Count.ShouldBe(1);
        pool[0].PublicKey.ShouldBe("AAA");
        pool[0].SolAmount.ShouldBe(2.54m);
        // The type has no PrivateKey member at all — a spend key can never be bound.
        typeof(PoolEntry).GetProperty("PrivateKey").ShouldBeNull();
    }

    [Fact]
    public void Defaults_the_label_by_index_when_absent()
    {
        var json = """[ { "PublicKey": "A", "SOLAmount": 1 }, { "PublicKey": "B", "SOLAmount": 2 } ]""";

        var pool = WalletPool.Parse(json);

        pool[0].Label.ShouldBe("W01");
        pool[1].Label.ShouldBe("W02");
    }

    [Fact]
    public void Keeps_an_explicit_label()
        => WalletPool.Parse("""[ { "Label": "W10", "PublicKey": "A", "SOLAmount": 1 } ]""")[0]
            .Label.ShouldBe("W10");

    [Fact]
    public void Converts_sol_to_lamports()
    {
        WalletPool.ToLamports(2.54m).ShouldBe(2_540_000_000L);
        WalletPool.ToLamports(0.000000001m).ShouldBe(1L);
    }

    [Fact]
    public void Rejects_an_amount_finer_than_one_lamport()
        => Should.Throw<FunderException>(() => WalletPool.ToLamports(0.0000000001m));

    [Fact]
    public void Formats_amounts_invariantly_even_under_a_comma_decimal_culture()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("pt-BR");
            WalletPool.FormatSol(2.54m).ShouldBe("2.54");
        }
        finally { Thread.CurrentThread.CurrentCulture = original; }
    }

    [Fact]
    public void Rejects_malformed_json()
        => Should.Throw<FunderException>(() => WalletPool.Parse("not json"));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/SpaceStroker.Tools.sln --filter WalletPoolTests`
Expected: FAIL — `WalletPool` / `PoolEntry` do not exist.

- [ ] **Step 3: Implement `WalletPool.cs`**

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpaceStroker.WalletFunder;

/// <summary>
/// One funding target. Deliberately carries NO private-key member: #63's pool entries include a
/// PrivateKey this tool must never bind, hold or log. Unknown JSON fields are ignored by default.
/// </summary>
public sealed record PoolEntry(string Label, string PublicKey, decimal SolAmount);

/// <summary>JSON binding shape. Shared by --pool files and secrets.json's "wallets" array.</summary>
public sealed class RawPoolEntry
{
    [JsonPropertyName("Label")] public string? Label { get; set; }
    [JsonPropertyName("PublicKey")] public string? PublicKey { get; set; }
    [JsonPropertyName("SOLAmount")] public decimal SOLAmount { get; set; }
}

public static class WalletPool
{
    public const long LamportsPerSol = 1_000_000_000L;

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Unknown members are ignored (the default). Do NOT set UnmappedMemberHandling.Disallow:
        // #63's pool file carries PrivateKey/SupplyPercentage and must load unchanged.
    };

    public static IReadOnlyList<PoolEntry> Parse(string json)
    {
        List<RawPoolEntry>? raw;
        try { raw = JsonSerializer.Deserialize<List<RawPoolEntry>>(json, JsonOpts); }
        catch (JsonException ex) { throw new FunderException($"Could not parse the wallet pool: {ex.Message}"); }

        if (raw is null) throw new FunderException("The wallet pool is empty or null.");
        return FromEntries(raw);
    }

    public static IReadOnlyList<PoolEntry> FromEntries(IEnumerable<RawPoolEntry> raw)
    {
        var result = new List<PoolEntry>();
        var index = 0;
        foreach (var entry in raw)
        {
            index++;
            var label = string.IsNullOrWhiteSpace(entry.Label) ? $"W{index:00}" : entry.Label.Trim();
            var address = (entry.PublicKey ?? "").Trim();
            result.Add(new PoolEntry(label, address, entry.SOLAmount));
        }
        return result;
    }

    /// <summary>SOL → integer lamports. Integer math is the comparison and hashing unit; decimal in, never double.</summary>
    public static long ToLamports(decimal sol)
    {
        var scaled = sol * LamportsPerSol;
        if (scaled != decimal.Truncate(scaled))
            throw new FunderException($"Amount {FormatSol(sol)} SOL is finer than one lamport.");
        return (long)scaled;
    }

    /// <summary>Invariant decimal rendering for the outbound Coinbase "amount" string.</summary>
    public static string FormatSol(decimal sol)
        => sol.ToString("0.#########", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tools/SpaceStroker.Tools.sln --filter WalletPoolTests`
Expected: PASS (7 tests).

- [ ] **Step 5: Delete the scaffold test and commit**

```bash
rm tools/SpaceStroker.WalletFunder.Tests/ScaffoldTests.cs
git add -A tools/
git commit -m "Add wallet pool parsing that ignores unknown fields."
```

---

### Task 3: Funding plan — plan hash and deterministic idem

**Files:**
- Create: `tools/SpaceStroker.WalletFunder/FundingPlan.cs`
- Create: `tools/SpaceStroker.WalletFunder.Tests/FundingPlanTests.cs`

**Interfaces:**
- Consumes: `PoolEntry`, `WalletPool.ToLamports`.
- Produces:
  - `sealed record PlannedTransfer(string Label, string Address, decimal SolAmount, long Lamports, Guid Idem)`
  - `sealed class FundingPlan { string PlanHash; IReadOnlyList<PlannedTransfer> Transfers; decimal TotalSol; }`
  - `static FundingPlan Build(IReadOnlyList<PoolEntry> pool)`
  - `static Guid DeriveIdem(string planHash, string address, long lamports)`

- [ ] **Step 1: Write the failing tests**

```csharp
using Shouldly;
using Xunit;

namespace SpaceStroker.WalletFunder.Tests;

public class FundingPlanTests
{
    private static IReadOnlyList<PoolEntry> Pool(params (string addr, decimal sol)[] items)
        => items.Select((x, i) => new PoolEntry($"W{i + 1:00}", x.addr, x.sol)).ToList();

    [Fact]
    public void Same_pool_produces_identical_idem_tokens()
    {
        var a = FundingPlan.Build(Pool(("AAA", 2.54m), ("BBB", 1m)));
        var b = FundingPlan.Build(Pool(("AAA", 2.54m), ("BBB", 1m)));

        a.PlanHash.ShouldBe(b.PlanHash);
        a.Transfers.Select(t => t.Idem).ShouldBe(b.Transfers.Select(t => t.Idem));
    }

    [Fact]
    public void Reordering_the_pool_does_not_change_the_plan_hash()
        => FundingPlan.Build(Pool(("AAA", 1m), ("BBB", 2m))).PlanHash
            .ShouldBe(FundingPlan.Build(Pool(("BBB", 2m), ("AAA", 1m))).PlanHash);

    [Fact]
    public void Changing_an_amount_changes_that_wallets_idem_and_the_plan_hash()
    {
        var before = FundingPlan.Build(Pool(("AAA", 1m), ("BBB", 2m)));
        var after = FundingPlan.Build(Pool(("AAA", 1m), ("BBB", 3m)));

        after.PlanHash.ShouldNotBe(before.PlanHash);
        after.Transfers.Single(t => t.Address == "BBB").Idem
            .ShouldNotBe(before.Transfers.Single(t => t.Address == "BBB").Idem);
    }

    [Fact]
    public void Changing_an_address_changes_the_plan_hash()
        => FundingPlan.Build(Pool(("AAA", 1m))).PlanHash
            .ShouldNotBe(FundingPlan.Build(Pool(("ZZZ", 1m))).PlanHash);

    [Fact]
    public void Renaming_a_label_does_NOT_change_the_plan_hash_or_idem()
    {
        var a = FundingPlan.Build(new List<PoolEntry> { new("W01", "AAA", 1m) });
        var b = FundingPlan.Build(new List<PoolEntry> { new("RENAMED", "AAA", 1m) });

        b.PlanHash.ShouldBe(a.PlanHash);
        b.Transfers[0].Idem.ShouldBe(a.Transfers[0].Idem);
    }

    [Fact]
    public void Idem_is_shaped_like_a_uuid_v4()
    {
        var idem = FundingPlan.Build(Pool(("AAA", 1m))).Transfers[0].Idem;
        var bytes = idem.ToByteArray();
        // Guid.ToByteArray is little-endian for the first three groups; index 7 holds the version nibble
        // and index 8 the variant, matching how the derivation writes them back.
        (bytes[7] >> 4).ShouldBe(4);
        (bytes[8] >> 6).ShouldBe(2);
    }

    [Fact]
    public void Total_is_the_sum_of_the_pool()
        => FundingPlan.Build(Pool(("AAA", 2.54m), ("BBB", 1.5m))).TotalSol.ShouldBe(4.04m);

    [Fact]
    public void Preserves_the_authored_order_of_the_pool()
        => FundingPlan.Build(Pool(("ZZZ", 1m), ("AAA", 1m))).Transfers.Select(t => t.Address)
            .ShouldBe(new[] { "ZZZ", "AAA" });
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/SpaceStroker.Tools.sln --filter FundingPlanTests`
Expected: FAIL — `FundingPlan` does not exist.

- [ ] **Step 3: Implement `FundingPlan.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace SpaceStroker.WalletFunder;

public sealed record PlannedTransfer(string Label, string Address, decimal SolAmount, long Lamports, Guid Idem);

/// <summary>
/// The immutable funding plan. The PLAN HASH covers exactly what a resend would change — addresses
/// and amounts — and nothing that must not invalidate a resume (authored order, labels, formatting).
/// </summary>
public sealed class FundingPlan
{
    public required string PlanHash { get; init; }
    public required IReadOnlyList<PlannedTransfer> Transfers { get; init; }
    public decimal TotalSol => Transfers.Sum(t => t.SolAmount);

    public static FundingPlan Build(IReadOnlyList<PoolEntry> pool)
    {
        var lamports = pool.ToDictionary(e => e, e => WalletPool.ToLamports(e.SolAmount));

        // Canonical form: sorted by address (ordinal) so re-ordering the file is not a plan change.
        var canonical = new StringBuilder();
        foreach (var entry in pool.OrderBy(e => e.PublicKey, StringComparer.Ordinal))
            canonical.Append(entry.PublicKey).Append(':').Append(lamports[entry]).Append(';');

        var planHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();

        var transfers = pool
            .Select(e => new PlannedTransfer(e.Label, e.PublicKey, e.SolAmount, lamports[e],
                DeriveIdem(planHash, e.PublicKey, lamports[e])))
            .ToList();

        return new FundingPlan { PlanHash = planHash, Transfers = transfers };
    }

    /// <summary>
    /// Deterministic, UUIDv4-SHAPED idempotency token. Coinbase asks for a UUIDv4; deriving it from the
    /// plan means a repeat run reuses the same token and Coinbase dedupes SERVER-SIDE even when the local
    /// receipts file is gone. Change an amount or address and the token changes — which is a real new send,
    /// and exactly why Receipts guards against plan drift.
    /// </summary>
    public static Guid DeriveIdem(string planHash, string address, long lamports)
    {
        var material = $"{planHash}|{address}|{lamports}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var bytes = new byte[16];
        Array.Copy(digest, bytes, 16);

        // Stamp RFC 4122 version 4 + variant. Guid(byte[]) reads the first three groups little-endian,
        // so the version nibble lands at index 7 and the variant at index 8 in Guid.ToByteArray() terms.
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tools/SpaceStroker.Tools.sln --filter FundingPlanTests`
Expected: PASS (8 tests). If the UUID-shape test fails, print `idem.ToByteArray()` and re-check which index carries the version nibble before adjusting — do not weaken the assertion.

- [ ] **Step 5: Commit**

```bash
git add tools/
git commit -m "Add funding plan with deterministic plan hash and idem derivation."
```

---

### Task 4: Preflight validation

**Files:**
- Create: `tools/SpaceStroker.WalletFunder/Preflight.cs`
- Create: `tools/SpaceStroker.WalletFunder.Tests/PreflightTests.cs`

**Interfaces:**
- Consumes: `PoolEntry`, `FundingPlan`, `WalletPool.FormatSol`.
- Produces:
  - `sealed record PreflightResult(bool IsEmpty, IReadOnlyList<string> Errors) { bool Ok => !IsEmpty && Errors.Count == 0; }`
  - `static PreflightResult Check(IReadOnlyList<PoolEntry> pool, decimal availableSol, decimal minAmountSol, decimal feeHeadroomSol)`
  - `static bool IsValidSolanaAddress(string address)`

**Design note (why this is a pure function):** the card rules out a fake Coinbase sender, so preflight takes the **balance as a value**. The caller fetches it from `CoinbaseClient` and passes it in. That is what makes AC3 a unit test with no Coinbase call.

- [ ] **Step 1: Write the failing tests**

```csharp
using Shouldly;
using Xunit;

namespace SpaceStroker.WalletFunder.Tests;

public class PreflightTests
{
    // A real, well-formed 32-byte base58 Solana address (the SPL Token program id).
    private const string ValidA = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA";
    private const string ValidB = "11111111111111111111111111111111";

    private static PreflightResult Check(IReadOnlyList<PoolEntry> pool, decimal available = 1000m)
        => Preflight.Check(pool, available, minAmountSol: 0.001m, feeHeadroomSol: 0.05m);

    private static List<PoolEntry> Pool(params (string addr, decimal sol)[] items)
        => items.Select((x, i) => new PoolEntry($"W{i + 1:00}", x.addr, x.sol)).ToList();

    [Fact]
    public void Accepts_a_valid_pool()
        => Check(Pool((ValidA, 1m), (ValidB, 2m))).Ok.ShouldBeTrue();

    [Fact]
    public void Empty_pool_is_flagged_as_empty_and_not_an_error()
    {
        var result = Check(new List<PoolEntry>());
        result.IsEmpty.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("not-a-real-address")]      // invalid base58 characters (0, l, I, O excluded)
    [InlineData("")]                        // missing
    [InlineData("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5D")]   // one char short → not 32 bytes
    public void Rejects_a_malformed_address(string address)
    {
        var result = Check(Pool((address, 1m)));
        result.Ok.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Contains("W01"));
    }

    [Fact]
    public void Rejects_a_duplicate_address()
    {
        var result = Check(Pool((ValidA, 1m), (ValidA, 2m)));
        result.Ok.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.0001)]   // below minAmountSol
    public void Rejects_a_bad_amount(decimal sol)
        => Check(Pool((ValidA, sol))).Ok.ShouldBeFalse();

    [Fact]
    public void Rejects_a_balance_that_does_not_cover_the_fee_headroom()
    {
        // Total is exactly 10; available is exactly 10; headroom 0.05 makes it short.
        var result = Check(Pool((ValidA, 10m)), available: 10m);
        result.Ok.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Contains("balance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Accepts_a_balance_that_covers_total_plus_headroom()
        => Check(Pool((ValidA, 10m)), available: 10.05m).Ok.ShouldBeTrue();

    [Fact]
    public void Reports_every_problem_at_once_rather_than_the_first()
        => Check(Pool(("bad", 1m), (ValidA, -5m))).Errors.Count.ShouldBeGreaterThanOrEqualTo(2);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/SpaceStroker.Tools.sln --filter PreflightTests`
Expected: FAIL — `Preflight` does not exist.

- [ ] **Step 3: Implement `Preflight.cs`**

```csharp
using Solnet.Wallet;

namespace SpaceStroker.WalletFunder;

public sealed record PreflightResult(bool IsEmpty, IReadOnlyList<string> Errors)
{
    public bool Ok => !IsEmpty && Errors.Count == 0;
}

public static class Preflight
{
    /// <summary>
    /// Every check runs before send #1; any failure aborts the run. Collects ALL problems rather than
    /// throwing on the first, so one dry run fixes the whole file.
    /// The balance arrives as a VALUE (not a Coinbase client) to keep this pure and unit-testable.
    /// </summary>
    public static PreflightResult Check(
        IReadOnlyList<PoolEntry> pool, decimal availableSol, decimal minAmountSol, decimal feeHeadroomSol)
    {
        if (pool.Count == 0) return new PreflightResult(IsEmpty: true, Array.Empty<string>());

        var errors = new List<string>();

        foreach (var entry in pool)
        {
            if (!IsValidSolanaAddress(entry.PublicKey))
                errors.Add($"{entry.Label}: '{entry.PublicKey}' is not a valid Solana address.");

            if (entry.SolAmount <= 0)
                errors.Add($"{entry.Label}: amount must be greater than zero (was {WalletPool.FormatSol(entry.SolAmount)}).");
            else if (entry.SolAmount < minAmountSol)
                errors.Add($"{entry.Label}: amount {WalletPool.FormatSol(entry.SolAmount)} SOL is below the minimum {WalletPool.FormatSol(minAmountSol)} SOL.");
        }

        foreach (var duplicate in pool.GroupBy(e => e.PublicKey, StringComparer.Ordinal).Where(g => g.Count() > 1))
            errors.Add($"Duplicate address {duplicate.Key} appears {duplicate.Count()} times ({string.Join(", ", duplicate.Select(e => e.Label))}).");

        var total = pool.Sum(e => e.SolAmount);
        var required = total + feeHeadroomSol;
        if (availableSol < required)
            errors.Add($"Coinbase SOL balance {WalletPool.FormatSol(availableSol)} does not cover the batch total " +
                       $"{WalletPool.FormatSol(total)} plus the {WalletPool.FormatSol(feeHeadroomSol)} network-fee headroom " +
                       $"(short by {WalletPool.FormatSol(required - availableSol)} SOL).");

        return new PreflightResult(IsEmpty: false, errors);
    }

    /// <summary>
    /// Solnet's `new PublicKey(string)` does NOT validate — monkify-backend only ever calls it on values
    /// already known to be addresses. Validate explicitly, and assert the 32-byte decode: a truncated
    /// base58 string is precisely the mistyped-address case, and it is irreversible if it gets through.
    /// </summary>
    public static bool IsValidSolanaAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        if (!PublicKey.IsValid(address)) return false;

        try { return new PublicKey(address).KeyBytes.Length == 32; }
        catch { return false; }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tools/SpaceStroker.Tools.sln --filter PreflightTests`
Expected: PASS. If `PublicKey.IsValid` accepts the one-char-short address, the 32-byte assertion is what catches it — confirm that test still passes and keep both checks.

- [ ] **Step 5: Commit**

```bash
git add tools/
git commit -m "Add preflight validation with explicit Solana address checking."
```

---

### Task 5: Receipts, resume and the plan-drift guard

**Files:**
- Create: `tools/SpaceStroker.WalletFunder/Receipts.cs`
- Create: `tools/SpaceStroker.WalletFunder.Tests/ReceiptsTests.cs`

**Interfaces:**
- Consumes: `FundingPlan`, `PlannedTransfer`, `FunderException`.
- Produces:
  - `enum TransferStatus { Planned, Sent, Pending, Confirmed, Unconfirmed, Failed }`
  - `sealed class ReceiptEntry { Label, Address, AmountSol, Lamports, Idem, CoinbaseTransactionId, Status, ChainConfirmed, Error, SentUtc, SettledUtc }`
  - `sealed class ReceiptsFile { PlanHash, CreatedUtc, UpdatedUtc, List<ReceiptEntry> Entries }`
  - `sealed class Receipts`
    - `static Receipts LoadOrCreate(string path, FundingPlan plan, bool forcePlanChange)`
    - `ReceiptEntry For(PlannedTransfer transfer)`
    - `void Update(ReceiptEntry entry, Action<ReceiptEntry> mutate)` — mutates then **saves immediately**
    - `IReadOnlyList<PlannedTransfer> Outstanding(FundingPlan plan)` — excludes `Confirmed`
    - `static bool NeedsSend(ReceiptEntry entry)` — false when a Coinbase transaction id exists
    - `bool AllConfirmed`

- [ ] **Step 1: Write the failing tests**

```csharp
using Shouldly;
using Xunit;

namespace SpaceStroker.WalletFunder.Tests;

public class ReceiptsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"receipts-{Guid.NewGuid():N}.json");

    private static FundingPlan Plan(params (string addr, decimal sol)[] items)
        => FundingPlan.Build(items.Select((x, i) => new PoolEntry($"W{i + 1:00}", x.addr, x.sol)).ToList());

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }

    [Fact]
    public void Creates_a_file_seeded_from_the_plan()
    {
        var plan = Plan(("AAA", 1m), ("BBB", 2m));
        var receipts = Receipts.LoadOrCreate(_path, plan, forcePlanChange: false);

        receipts.Outstanding(plan).Count.ShouldBe(2);
        File.Exists(_path).ShouldBeTrue();
    }

    [Fact]
    public void Skips_a_confirmed_wallet_on_resume()
    {
        var plan = Plan(("AAA", 1m), ("BBB", 2m));
        var first = Receipts.LoadOrCreate(_path, plan, false);
        first.Update(first.For(plan.Transfers[0]), e => { e.Status = TransferStatus.Confirmed; e.ChainConfirmed = true; });

        var resumed = Receipts.LoadOrCreate(_path, plan, false);

        resumed.Outstanding(plan).Select(t => t.Address).ShouldBe(new[] { "BBB" });
    }

    [Fact]
    public void Retries_a_failed_wallet_on_resume()
    {
        var plan = Plan(("AAA", 1m));
        var first = Receipts.LoadOrCreate(_path, plan, false);
        first.Update(first.For(plan.Transfers[0]), e => { e.Status = TransferStatus.Failed; e.Error = "boom"; });

        Receipts.LoadOrCreate(_path, plan, false).Outstanding(plan).Count.ShouldBe(1);
    }

    [Fact]
    public void An_entry_holding_a_transaction_id_is_never_re_sent()
    {
        var plan = Plan(("AAA", 1m));
        var receipts = Receipts.LoadOrCreate(_path, plan, false);
        var entry = receipts.For(plan.Transfers[0]);
        receipts.Update(entry, e => { e.Status = TransferStatus.Pending; e.CoinbaseTransactionId = "tx-1"; });

        Receipts.NeedsSend(receipts.For(plan.Transfers[0])).ShouldBeFalse();
    }

    [Fact]
    public void A_planned_entry_still_needs_a_send()
        => Receipts.NeedsSend(Receipts.LoadOrCreate(_path, Plan(("AAA", 1m)), false)
            .For(Plan(("AAA", 1m)).Transfers[0])).ShouldBeTrue();

    [Fact]
    public void Refuses_to_resume_when_the_plan_drifted()
    {
        Receipts.LoadOrCreate(_path, Plan(("AAA", 1m)), false);

        var ex = Should.Throw<FunderException>(() => Receipts.LoadOrCreate(_path, Plan(("AAA", 2m)), false));

        ex.Message.ShouldContain("--force-plan-change");
    }

    [Fact]
    public void Force_plan_change_re_keys_the_file()
    {
        Receipts.LoadOrCreate(_path, Plan(("AAA", 1m)), false);
        var changed = Plan(("AAA", 2m));

        var receipts = Receipts.LoadOrCreate(_path, changed, forcePlanChange: true);

        receipts.Outstanding(changed).Count.ShouldBe(1);
        Receipts.LoadOrCreate(_path, changed, false).ShouldNotBeNull();  // now matches, no throw
    }

    [Fact]
    public void Round_trips_every_field_and_stores_no_credential()
    {
        var plan = Plan(("AAA", 1.25m));
        var receipts = Receipts.LoadOrCreate(_path, plan, false);
        receipts.Update(receipts.For(plan.Transfers[0]), e =>
        {
            e.Status = TransferStatus.Confirmed;
            e.CoinbaseTransactionId = "tx-9";
            e.ChainConfirmed = true;
        });

        var reloaded = Receipts.LoadOrCreate(_path, plan, false).For(plan.Transfers[0]);
        reloaded.CoinbaseTransactionId.ShouldBe("tx-9");
        reloaded.AmountSol.ShouldBe(1.25m);
        reloaded.Label.ShouldBe("W01");
        reloaded.ChainConfirmed.ShouldBeTrue();

        var raw = File.ReadAllText(_path);
        raw.ShouldNotContain("apiKeySecret");
        raw.ShouldNotContain("PrivateKey");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/SpaceStroker.Tools.sln --filter ReceiptsTests`
Expected: FAIL — `Receipts` does not exist.

- [ ] **Step 3: Implement `Receipts.cs`**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpaceStroker.WalletFunder;

public enum TransferStatus { Planned, Sent, Pending, Confirmed, Unconfirmed, Failed }

public sealed class ReceiptEntry
{
    public string Label { get; set; } = "";
    public string Address { get; set; } = "";
    public decimal AmountSol { get; set; }
    public long Lamports { get; set; }
    public string Idem { get; set; } = "";
    public string? CoinbaseTransactionId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TransferStatus Status { get; set; } = TransferStatus.Planned;

    public bool ChainConfirmed { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? SentUtc { get; set; }
    public DateTimeOffset? SettledUtc { get; set; }
}

public sealed class ReceiptsFile
{
    public string PlanHash { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public List<ReceiptEntry> Entries { get; set; } = [];
}

/// <summary>
/// The receipts file IS the resume state — written after every state change, never only at the end,
/// so a crash cannot lose a Coinbase transaction id.
/// </summary>
public sealed class Receipts
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private readonly string _path;
    private readonly ReceiptsFile _file;

    private Receipts(string path, ReceiptsFile file) { _path = path; _file = file; }

    public static Receipts LoadOrCreate(string path, FundingPlan plan, bool forcePlanChange)
    {
        ReceiptsFile file;

        if (File.Exists(path))
        {
            file = JsonSerializer.Deserialize<ReceiptsFile>(File.ReadAllText(path), JsonOpts)
                   ?? throw new FunderException($"Could not parse the receipts file {path}.");

            if (!string.Equals(file.PlanHash, plan.PlanHash, StringComparison.OrdinalIgnoreCase))
            {
                if (!forcePlanChange)
                    throw new FunderException(
                        $"The wallet pool changed since these receipts were written.{Environment.NewLine}" +
                        $"  receipts plan: {file.PlanHash}{Environment.NewLine}" +
                        $"  current plan:  {plan.PlanHash}{Environment.NewLine}" +
                        "Each wallet's idempotency token is derived from the plan, so the changed entries would " +
                        "be sent AGAIN rather than deduplicated by Coinbase. Re-run with --force-plan-change only " +
                        "if you accept that risk.");

                file = NewFile(plan);
            }
            else
            {
                Reconcile(file, plan);
            }
        }
        else
        {
            file = NewFile(plan);
        }

        var receipts = new Receipts(path, file);
        receipts.Save();
        return receipts;
    }

    private static ReceiptsFile NewFile(FundingPlan plan) => new()
    {
        PlanHash = plan.PlanHash,
        CreatedUtc = DateTimeOffset.UtcNow,
        UpdatedUtc = DateTimeOffset.UtcNow,
        Entries = plan.Transfers.Select(Seed).ToList(),
    };

    private static ReceiptEntry Seed(PlannedTransfer t) => new()
    {
        Label = t.Label, Address = t.Address, AmountSol = t.SolAmount,
        Lamports = t.Lamports, Idem = t.Idem.ToString("D"), Status = TransferStatus.Planned,
    };

    /// <summary>Same plan hash but an entry missing (hand-edited file) ⇒ seed it rather than crash.</summary>
    private static void Reconcile(ReceiptsFile file, FundingPlan plan)
    {
        foreach (var transfer in plan.Transfers)
            if (file.Entries.All(e => !string.Equals(e.Address, transfer.Address, StringComparison.Ordinal)))
                file.Entries.Add(Seed(transfer));
    }

    public ReceiptEntry For(PlannedTransfer transfer)
        => _file.Entries.FirstOrDefault(e => string.Equals(e.Address, transfer.Address, StringComparison.Ordinal))
           ?? throw new FunderException($"No receipt entry for {transfer.Address}.");

    public void Update(ReceiptEntry entry, Action<ReceiptEntry> mutate)
    {
        mutate(entry);
        Save();
    }

    /// <summary>A wallet holding a Coinbase transaction id is POLLED, never re-sent.</summary>
    public static bool NeedsSend(ReceiptEntry entry)
        => string.IsNullOrWhiteSpace(entry.CoinbaseTransactionId) && entry.Status != TransferStatus.Confirmed;

    public IReadOnlyList<PlannedTransfer> Outstanding(FundingPlan plan)
        => plan.Transfers.Where(t => For(t).Status != TransferStatus.Confirmed).ToList();

    public bool AllConfirmed => _file.Entries.All(e => e.Status == TransferStatus.Confirmed);

    public IReadOnlyList<ReceiptEntry> Entries => _file.Entries;

    public string Path => _path;

    private void Save()
    {
        _file.UpdatedUtc = DateTimeOffset.UtcNow;
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(_path, JsonSerializer.Serialize(_file, JsonOpts));
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tools/SpaceStroker.Tools.sln --filter ReceiptsTests`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add tools/
git commit -m "Add receipts with resume filtering and a plan-drift guard."
```

---

### Task 6: Spread schedule

**Files:**
- Create: `tools/SpaceStroker.WalletFunder/SendSchedule.cs`
- Create: `tools/SpaceStroker.WalletFunder.Tests/SendScheduleTests.cs`

**Interfaces:**
- Consumes: `PlannedTransfer`.
- Produces:
  - `sealed record ScheduledTransfer(PlannedTransfer Transfer, TimeSpan Offset)`
  - `static IReadOnlyList<ScheduledTransfer> Build(IReadOnlyList<PlannedTransfer> transfers, TimeSpan window, Random rng)`

- [ ] **Step 1: Write the failing tests**

```csharp
using Shouldly;
using Xunit;

namespace SpaceStroker.WalletFunder.Tests;

public class SendScheduleTests
{
    private static IReadOnlyList<PlannedTransfer> Transfers(int count)
        => Enumerable.Range(1, count)
            .Select(i => new PlannedTransfer($"W{i:00}", $"ADDR{i}", 1m, 1_000_000_000L, Guid.NewGuid()))
            .ToList();

    [Fact]
    public void Schedules_every_transfer_exactly_once()
    {
        var transfers = Transfers(10);

        var schedule = SendSchedule.Build(transfers, TimeSpan.FromMinutes(30), new Random(1));

        schedule.Count.ShouldBe(10);
        schedule.Select(s => s.Transfer.Address).OrderBy(a => a)
            .ShouldBe(transfers.Select(t => t.Address).OrderBy(a => a));
    }

    [Fact]
    public void Offsets_stay_inside_the_window_and_ascend()
    {
        var window = TimeSpan.FromMinutes(30);

        var schedule = SendSchedule.Build(Transfers(10), window, new Random(7));

        schedule.ShouldAllBe(s => s.Offset >= TimeSpan.Zero && s.Offset < window);
        schedule.Select(s => s.Offset).ShouldBe(schedule.Select(s => s.Offset).OrderBy(o => o));
    }

    [Fact]
    public void A_zero_window_sends_back_to_back()
        => SendSchedule.Build(Transfers(5), TimeSpan.Zero, new Random(3))
            .ShouldAllBe(s => s.Offset == TimeSpan.Zero);

    [Fact]
    public void The_same_seed_produces_the_same_schedule()
    {
        var transfers = Transfers(8);

        var a = SendSchedule.Build(transfers, TimeSpan.FromMinutes(20), new Random(42));
        var b = SendSchedule.Build(transfers, TimeSpan.FromMinutes(20), new Random(42));

        a.Select(s => s.Transfer.Address).ShouldBe(b.Select(s => s.Transfer.Address));
        a.Select(s => s.Offset).ShouldBe(b.Select(s => s.Offset));
    }

    [Fact]
    public void Different_seeds_generally_reorder_the_pool()
    {
        var transfers = Transfers(12);

        var a = SendSchedule.Build(transfers, TimeSpan.FromMinutes(20), new Random(1));
        var b = SendSchedule.Build(transfers, TimeSpan.FromMinutes(20), new Random(2));

        a.Select(s => s.Transfer.Address).ShouldNotBe(b.Select(s => s.Transfer.Address));
    }

    [Fact]
    public void An_empty_pool_schedules_nothing()
        => SendSchedule.Build(Array.Empty<PlannedTransfer>(), TimeSpan.FromMinutes(5), new Random(1)).ShouldBeEmpty();
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/SpaceStroker.Tools.sln --filter SendScheduleTests`
Expected: FAIL — `SendSchedule` does not exist.

- [ ] **Step 3: Implement `SendSchedule.cs`**

```csharp
namespace SpaceStroker.WalletFunder;

public sealed record ScheduledTransfer(PlannedTransfer Transfer, TimeSpan Offset);

/// <summary>
/// Spreads the batch over a window in randomized order with randomized gaps.
/// A Coinbase send leaves Coinbase's shared hot wallets, so the SENDER does not identify the account —
/// CLUSTERING does. Twelve deposits in the same minute that then all buy the same token is the tell.
/// `Random` is injected so the schedule is unit-testable; production passes Random.Shared.
/// </summary>
public static class SendSchedule
{
    public static IReadOnlyList<ScheduledTransfer> Build(
        IReadOnlyList<PlannedTransfer> transfers, TimeSpan window, Random rng)
    {
        if (transfers.Count == 0) return Array.Empty<ScheduledTransfer>();

        var order = transfers.ToArray();
        for (var i = order.Length - 1; i > 0; i--)          // Fisher-Yates
        {
            var j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        var offsets = new TimeSpan[order.Length];
        if (window > TimeSpan.Zero)
        {
            var ticks = Enumerable.Range(0, order.Length)
                .Select(_ => (long)(rng.NextDouble() * window.Ticks))
                .OrderBy(t => t)
                .ToArray();
            for (var i = 0; i < order.Length; i++) offsets[i] = TimeSpan.FromTicks(ticks[i]);
        }

        return order.Select((t, i) => new ScheduledTransfer(t, offsets[i])).ToList();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tools/SpaceStroker.Tools.sln --filter SendScheduleTests`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add tools/
git commit -m "Add the randomized spread schedule."
```

---

### Task 7: Configuration loading

**Files:**
- Create: `tools/SpaceStroker.WalletFunder/SecretsFile.cs`
- Create: `tools/SpaceStroker.WalletFunder.Tests/SecretsFileTests.cs`

**Interfaces:**
- Consumes: `RawPoolEntry`, `FunderException`.
- Produces:
  - `sealed class SecretsFile { CoinbaseSection Coinbase; SolanaSection Solana; FundingSection Funding; List<RawPoolEntry> Wallets; static SecretsFile Load(string path); }`
  - `sealed class CoinbaseSection { ApiKeyId, ApiKeySecret, KeyAlgorithm, AccountId, ApiHost, ApiVersion }`
  - `sealed class SolanaSection { RpcUrl }`
  - `sealed class FundingSection { SpreadMinutes, NetworkFeeHeadroomSol, MinAmountSol, SendPollTimeoutSeconds, ChainConfirmTimeoutSeconds }`

- [ ] **Step 1: Write the failing tests**

```csharp
using Shouldly;
using Xunit;

namespace SpaceStroker.WalletFunder.Tests;

public class SecretsFileTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"secrets-{Guid.NewGuid():N}.json");

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }

    private void WriteFile(string apiKeySecret = "real-secret-value", string apiKeyId = "organizations/o/apiKeys/k")
        => File.WriteAllText(_path, $$"""
        {
          "coinbase": { "apiKeyId": "{{apiKeyId}}", "apiKeySecret": "{{apiKeySecret}}",
                        "keyAlgorithm": "ed25519", "apiHost": "api.coinbase.com", "apiVersion": "2024-01-01" },
          "solana": { "rpcUrl": "https://api.mainnet-beta.solana.com" },
          "funding": { "spreadMinutes": 45, "networkFeeHeadroomSol": 0.05, "minAmountSol": 0.001,
                       "sendPollTimeoutSeconds": 300, "chainConfirmTimeoutSeconds": 120 },
          "wallets": [ { "Label": "W01", "PublicKey": "AAA", "PrivateKey": "LEAK", "SOLAmount": 2.54 } ]
        }
        """);

    [Fact]
    public void Loads_every_section()
    {
        WriteFile();

        var secrets = SecretsFile.Load(_path);

        secrets.Coinbase.ApiHost.ShouldBe("api.coinbase.com");
        secrets.Funding.SpreadMinutes.ShouldBe(45);
        secrets.Funding.NetworkFeeHeadroomSol.ShouldBe(0.05m);
        secrets.Solana.RpcUrl.ShouldBe("https://api.mainnet-beta.solana.com");
        secrets.Wallets.Count.ShouldBe(1);
        secrets.Wallets[0].SOLAmount.ShouldBe(2.54m);
    }

    [Fact]
    public void Rejects_a_placeholder_secret()
    {
        WriteFile(apiKeySecret: "REPLACE_ME_BASE64_ED25519_PRIVATE_KEY");

        Should.Throw<FunderException>(() => SecretsFile.Load(_path))
            .Message.ShouldContain("placeholder");
    }

    [Fact]
    public void Rejects_a_placeholder_key_id()
    {
        WriteFile(apiKeyId: "organizations/REPLACE_ME_ORG_ID/apiKeys/REPLACE_ME_KEY_ID");

        Should.Throw<FunderException>(() => SecretsFile.Load(_path)).Message.ShouldContain("placeholder");
    }

    [Fact]
    public void Reports_a_missing_file_with_the_fix()
        => Should.Throw<FunderException>(() => SecretsFile.Load(Path.Combine(Path.GetTempPath(), "nope.json")))
            .Message.ShouldContain("secrets.example.json");

    [Fact]
    public void Rejects_an_unknown_key_algorithm()
    {
        File.WriteAllText(_path, """
        { "coinbase": { "apiKeyId": "o/k", "apiKeySecret": "s", "keyAlgorithm": "rsa" }, "wallets": [] }
        """);

        Should.Throw<FunderException>(() => SecretsFile.Load(_path)).Message.ShouldContain("keyAlgorithm");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/SpaceStroker.Tools.sln --filter SecretsFileTests`
Expected: FAIL — `SecretsFile` does not exist.

- [ ] **Step 3: Implement `SecretsFile.cs`** (mirrors `deploy/tools/SpaceStroker.Deploy/SecretsFile.cs`)

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpaceStroker.WalletFunder;

/// <summary>The unversioned local tools/secrets.json — the single source of truth for a funding run.</summary>
public sealed class SecretsFile
{
    [JsonPropertyName("coinbase")] public CoinbaseSection Coinbase { get; set; } = new();
    [JsonPropertyName("solana")] public SolanaSection Solana { get; set; } = new();
    [JsonPropertyName("funding")] public FundingSection Funding { get; set; } = new();
    [JsonPropertyName("wallets")] public List<RawPoolEntry> Wallets { get; set; } = [];

    public static SecretsFile Load(string path)
    {
        if (!File.Exists(path))
            throw new FunderException($"Secrets file not found: {path}. Copy tools/secrets.example.json and fill it in.");

        SecretsFile parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<SecretsFile>(File.ReadAllText(path), WalletPool.JsonOpts)
                     ?? throw new FunderException($"Could not parse {path}.");
        }
        catch (JsonException ex) { throw new FunderException($"Could not parse {path}: {ex.Message}"); }

        if (IsPlaceholder(parsed.Coinbase.ApiKeyId))
            throw new FunderException($"{path}: coinbase.apiKeyId is still a placeholder — fill in the real key id.");
        if (IsPlaceholder(parsed.Coinbase.ApiKeySecret))
            throw new FunderException($"{path}: coinbase.apiKeySecret is still a placeholder — fill in the real key secret.");

        var algorithm = (parsed.Coinbase.KeyAlgorithm ?? "").Trim().ToLowerInvariant();
        if (algorithm is not ("ed25519" or "ecdsa"))
            throw new FunderException($"{path}: coinbase.keyAlgorithm must be 'ed25519' or 'ecdsa' (was '{parsed.Coinbase.KeyAlgorithm}').");
        parsed.Coinbase.KeyAlgorithm = algorithm;

        return parsed;
    }

    private static bool IsPlaceholder(string? value)
        => string.IsNullOrWhiteSpace(value) || value.Contains("REPLACE_ME", StringComparison.OrdinalIgnoreCase);
}

public sealed class CoinbaseSection
{
    [JsonPropertyName("apiKeyId")] public string ApiKeyId { get; set; } = "";
    [JsonPropertyName("apiKeySecret")] public string ApiKeySecret { get; set; } = "";
    [JsonPropertyName("keyAlgorithm")] public string KeyAlgorithm { get; set; } = "ed25519";
    [JsonPropertyName("accountId")] public string AccountId { get; set; } = "";
    [JsonPropertyName("apiHost")] public string ApiHost { get; set; } = "api.coinbase.com";
    [JsonPropertyName("apiVersion")] public string ApiVersion { get; set; } = "2024-01-01";
}

public sealed class SolanaSection
{
    [JsonPropertyName("rpcUrl")] public string RpcUrl { get; set; } = "https://api.mainnet-beta.solana.com";
}

public sealed class FundingSection
{
    [JsonPropertyName("spreadMinutes")] public int SpreadMinutes { get; set; } = 45;
    [JsonPropertyName("networkFeeHeadroomSol")] public decimal NetworkFeeHeadroomSol { get; set; } = 0.05m;
    [JsonPropertyName("minAmountSol")] public decimal MinAmountSol { get; set; } = 0.001m;
    [JsonPropertyName("sendPollTimeoutSeconds")] public int SendPollTimeoutSeconds { get; set; } = 300;
    [JsonPropertyName("chainConfirmTimeoutSeconds")] public int ChainConfirmTimeoutSeconds { get; set; } = 120;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tools/SpaceStroker.Tools.sln --filter SecretsFileTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add tools/
git commit -m "Add secrets loading with placeholder rejection."
```

---

### Task 8: Coinbase authentication (JWT)

**Files:**
- Create: `tools/SpaceStroker.WalletFunder/CoinbaseJwt.cs`
- Create: `tools/SpaceStroker.WalletFunder.Tests/CoinbaseJwtTests.cs`

**Interfaces:**
- Consumes: `CoinbaseSection`, `FunderException`.
- Produces: `static class CoinbaseJwt`
  - `string Create(CoinbaseSection coinbase, string method, string host, string path, DateTimeOffset now, string nonce)`
  - `static string Base64Url(byte[] bytes)`

**Three traps that silently produce a 401 — all three are why this task has its own tests:**
1. **.NET 8 has no Ed25519 in the BCL.** Build the JWS by hand and sign with BouncyCastle's `Ed25519Signer`.
2. **The CDP Ed25519 secret decodes to 64 bytes** — 32-byte seed ‖ 32-byte public key. `Ed25519PrivateKeyParameters` wants the **first 32 only**.
3. **ECDSA JWS needs raw `r‖s`** (`DSASignatureFormat.IeeeP1363FixedFieldConcatenation`), not .NET's default DER.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Shouldly;
using Xunit;

namespace SpaceStroker.WalletFunder.Tests;

public class CoinbaseJwtTests
{
    private static (CoinbaseSection coinbase, Ed25519PublicKeyParameters pub) NewEd25519Key()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        var priv = (Ed25519PrivateKeyParameters)pair.Private;
        var pub = (Ed25519PublicKeyParameters)pair.Public;

        // CDP hands out seed ‖ publicKey, base64 — reproduce that exact 64-byte shape.
        var combined = priv.GetEncoded().Concat(pub.GetEncoded()).ToArray();
        combined.Length.ShouldBe(64);

        return (new CoinbaseSection
        {
            ApiKeyId = "organizations/o/apiKeys/k",
            ApiKeySecret = Convert.ToBase64String(combined),
            KeyAlgorithm = "ed25519",
        }, pub);
    }

    private static (string header, string payload, byte[] signature, byte[] signingInput) Split(string jwt)
    {
        var parts = jwt.Split('.');
        parts.Length.ShouldBe(3);
        static byte[] Decode(string s)
        {
            var padded = s.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            return Convert.FromBase64String(padded);
        }
        return (Encoding.UTF8.GetString(Decode(parts[0])), Encoding.UTF8.GetString(Decode(parts[1])),
                Decode(parts[2]), Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"));
    }

    [Fact]
    public void Produces_three_base64url_segments_with_no_padding()
    {
        var (coinbase, _) = NewEd25519Key();

        var jwt = CoinbaseJwt.Create(coinbase, "POST", "api.coinbase.com", "/v2/accounts/x/transactions",
            DateTimeOffset.UtcNow, "abc123");

        jwt.Split('.').Length.ShouldBe(3);
        jwt.ShouldNotContain("=");
        jwt.ShouldNotContain("+");
        jwt.ShouldNotContain("/");
    }

    [Fact]
    public void Header_carries_EdDSA_kid_and_nonce()
    {
        var (coinbase, _) = NewEd25519Key();

        var (header, _, _, _) = Split(CoinbaseJwt.Create(coinbase, "GET", "api.coinbase.com", "/v2/accounts",
            DateTimeOffset.UtcNow, "nonce-1"));

        using var doc = JsonDocument.Parse(header);
        doc.RootElement.GetProperty("alg").GetString().ShouldBe("EdDSA");
        doc.RootElement.GetProperty("typ").GetString().ShouldBe("JWT");
        doc.RootElement.GetProperty("kid").GetString().ShouldBe(coinbase.ApiKeyId);
        doc.RootElement.GetProperty("nonce").GetString().ShouldBe("nonce-1");
    }

    [Fact]
    public void Claims_carry_sub_iss_aud_and_a_two_minute_expiry()
    {
        var (coinbase, _) = NewEd25519Key();
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

        var (_, payload, _, _) = Split(CoinbaseJwt.Create(coinbase, "GET", "api.coinbase.com", "/v2/accounts", now, "n"));

        using var doc = JsonDocument.Parse(payload);
        doc.RootElement.GetProperty("sub").GetString().ShouldBe(coinbase.ApiKeyId);
        doc.RootElement.GetProperty("iss").GetString().ShouldBe("cdp");
        doc.RootElement.GetProperty("aud")[0].GetString().ShouldBe("cdp_service");
        doc.RootElement.GetProperty("nbf").GetInt64().ShouldBe(1_700_000_000);
        doc.RootElement.GetProperty("exp").GetInt64().ShouldBe(1_700_000_120);
    }

    [Fact]
    public void Uri_claim_is_METHOD_HOST_PATH_with_no_scheme_or_query()
    {
        var (coinbase, _) = NewEd25519Key();

        var (_, payload, _, _) = Split(CoinbaseJwt.Create(coinbase, "POST", "api.coinbase.com",
            "/v2/accounts/abc/transactions", DateTimeOffset.UtcNow, "n"));

        JsonDocument.Parse(payload).RootElement.GetProperty("uri").GetString()
            .ShouldBe("POST api.coinbase.com/v2/accounts/abc/transactions");
    }

    [Fact]
    public void Signature_verifies_against_the_public_key()
    {
        var (coinbase, pub) = NewEd25519Key();

        var (_, _, signature, signingInput) = Split(CoinbaseJwt.Create(coinbase, "GET", "api.coinbase.com",
            "/v2/accounts", DateTimeOffset.UtcNow, "n"));

        var verifier = new Ed25519Signer();
        verifier.Init(false, pub);
        verifier.BlockUpdate(signingInput, 0, signingInput.Length);
        verifier.VerifySignature(signature).ShouldBeTrue();
    }

    [Fact]
    public void Rejects_a_secret_that_is_not_a_valid_ed25519_key()
    {
        var coinbase = new CoinbaseSection
        {
            ApiKeyId = "o/k", ApiKeySecret = Convert.ToBase64String(new byte[10]), KeyAlgorithm = "ed25519",
        };

        Should.Throw<FunderException>(() => CoinbaseJwt.Create(coinbase, "GET", "h", "/p", DateTimeOffset.UtcNow, "n"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/SpaceStroker.Tools.sln --filter CoinbaseJwtTests`
Expected: FAIL — `CoinbaseJwt` does not exist.

- [ ] **Step 3: Implement `CoinbaseJwt.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace SpaceStroker.WalletFunder;

/// <summary>
/// Builds the short-lived CDP bearer token. Hand-rolled JWS because .NET 8 has NO Ed25519 in the BCL
/// and Microsoft.IdentityModel will not sign EdDSA without a custom crypto provider.
/// A fresh token is minted per request: they live ~2 minutes and the `uri` claim binds method + path.
/// </summary>
public static class CoinbaseJwt
{
    private const int ExpirySeconds = 120;

    public static string Create(CoinbaseSection coinbase, string method, string host, string path,
        DateTimeOffset now, string nonce)
    {
        var algorithm = coinbase.KeyAlgorithm.Equals("ecdsa", StringComparison.OrdinalIgnoreCase) ? "ES256" : "EdDSA";

        var header = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["alg"] = algorithm, ["typ"] = "JWT", ["kid"] = coinbase.ApiKeyId, ["nonce"] = nonce,
        });

        var issued = now.ToUnixTimeSeconds();
        var claims = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["sub"] = coinbase.ApiKeyId,
            ["iss"] = "cdp",
            ["aud"] = new[] { "cdp_service" },
            ["nbf"] = issued,
            ["exp"] = issued + ExpirySeconds,
            ["uri"] = $"{method.ToUpperInvariant()} {host}{path}",
        });

        var signingInput = $"{Base64Url(header)}.{Base64Url(claims)}";
        var signature = algorithm == "EdDSA"
            ? SignEd25519(coinbase.ApiKeySecret, signingInput)
            : SignEs256(coinbase.ApiKeySecret, signingInput);

        return $"{signingInput}.{Base64Url(signature)}";
    }

    private static byte[] SignEd25519(string secret, string signingInput)
    {
        byte[] decoded;
        try { decoded = Convert.FromBase64String(secret); }
        catch (FormatException) { throw new FunderException("coinbase.apiKeySecret is not valid base64 for an ed25519 key."); }

        // CDP hands out 64 bytes: 32-byte seed ‖ 32-byte public key. Ed25519PrivateKeyParameters wants
        // the FIRST 32 ONLY — passing all 64 throws, and the wrong 32 yields a token Coinbase rejects.
        if (decoded.Length is not (32 or 64))
            throw new FunderException($"coinbase.apiKeySecret decodes to {decoded.Length} bytes; an ed25519 key is 32 or 64.");

        var privateKey = new Ed25519PrivateKeyParameters(decoded, 0);
        var bytes = Encoding.ASCII.GetBytes(signingInput);

        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(bytes, 0, bytes.Length);
        return signer.GenerateSignature();
    }

    private static byte[] SignEs256(string pem, string signingInput)
    {
        using var ecdsa = ECDsa.Create();
        try { ecdsa.ImportFromPem(pem); }
        catch (Exception ex) { throw new FunderException($"coinbase.apiKeySecret is not a valid EC private key PEM: {ex.Message}"); }

        // JWS requires raw r‖s. The .NET default is DER, which every JWT verifier rejects.
        return ecdsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    public static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tools/SpaceStroker.Tools.sln --filter CoinbaseJwtTests`
Expected: PASS (6 tests). The round-trip signature test is the one that proves the seed-slicing is right.

- [ ] **Step 5: Commit**

```bash
git add tools/
git commit -m "Add CDP bearer-token construction for ed25519 and ES256 keys."
```

---

### Task 9: Coinbase HTTP client and Solana confirmation

**Files:**
- Create: `tools/SpaceStroker.WalletFunder/CoinbaseClient.cs`
- Create: `tools/SpaceStroker.WalletFunder/SolanaConfirmer.cs`

**Interfaces:**
- Consumes: `CoinbaseJwt`, `CoinbaseSection`, `SolanaSection`, `WalletPool.FormatSol`, `FunderException`.
- Produces:
  - `sealed record CoinbaseAccount(string Id, string Currency, decimal Balance)`
  - `sealed record SendResult(string TransactionId, string Status)`
  - `sealed class CoinbaseClient : IDisposable`
    - `Task<CoinbaseAccount> ResolveSolAccountAsync(string? accountIdOverride, CancellationToken ct)`
    - `Task<SendResult> SendAsync(string accountId, string toAddress, decimal sol, Guid idem, CancellationToken ct)`
    - `Task<string> GetTransactionStatusAsync(string accountId, string transactionId, CancellationToken ct)`
    - `static string MaskKeyId(string apiKeyId)`
  - `sealed class SolanaConfirmer`
    - `Task<long> GetBalanceLamportsAsync(string address, CancellationToken ct)`
    - `Task<bool> WaitForCreditAsync(string address, long snapshotLamports, long expectedLamports, TimeSpan timeout, CancellationToken ct)`
  - `sealed class CoinbaseAuthException(string message) : Exception(message)` — thrown on 401/403 so the orchestrator aborts the whole run instead of retrying per wallet.

**No unit tests for this task** (card decision #17: no fake Coinbase sender). It is covered by `dotnet build` and the operator's dry run. Keep every rule that *can* be pure out of here — it belongs in Tasks 3–6.

- [ ] **Step 1: Implement `CoinbaseClient.cs`**

```csharp
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace SpaceStroker.WalletFunder;

public sealed record CoinbaseAccount(string Id, string Currency, decimal Balance);
public sealed record SendResult(string TransactionId, string Status);

/// <summary>Auth/scope failure — fatal for the WHOLE run: every later wallet would fail identically.</summary>
public sealed class CoinbaseAuthException(string message) : Exception(message);

public sealed class CoinbaseClient : IDisposable
{
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(20)];

    private readonly CoinbaseSection _coinbase;
    private readonly HttpClient _http;

    public CoinbaseClient(CoinbaseSection coinbase)
    {
        _coinbase = coinbase;
        _http = new HttpClient { BaseAddress = new Uri($"https://{coinbase.ApiHost}"), Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<CoinbaseAccount> ResolveSolAccountAsync(string? accountIdOverride, CancellationToken ct)
    {
        var accounts = new List<CoinbaseAccount>();
        var path = "/v2/accounts?limit=100";

        while (path is not null)
        {
            using var doc = await SendJsonAsync(HttpMethod.Get, path, body: null, ct);
            var root = doc.RootElement;

            foreach (var item in root.GetProperty("data").EnumerateArray())
            {
                var currency = item.GetProperty("balance").GetProperty("currency").GetString() ?? "";
                var amount = decimal.Parse(item.GetProperty("balance").GetProperty("amount").GetString() ?? "0",
                    CultureInfo.InvariantCulture);
                accounts.Add(new CoinbaseAccount(item.GetProperty("id").GetString() ?? "", currency, amount));
            }

            path = root.TryGetProperty("pagination", out var page)
                   && page.TryGetProperty("next_uri", out var next)
                   && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
        }

        if (!string.IsNullOrWhiteSpace(accountIdOverride))
            return accounts.FirstOrDefault(a => a.Id == accountIdOverride)
                   ?? throw new FunderException($"Coinbase account '{accountIdOverride}' was not found on this key.");

        var sol = accounts.Where(a => a.Currency.Equals("SOL", StringComparison.OrdinalIgnoreCase)).ToList();
        return sol.Count switch
        {
            1 => sol[0],
            0 => throw new FunderException("No SOL account found on this Coinbase key. Add coinbase.accountId to secrets.json."),
            _ => throw new FunderException($"{sol.Count} SOL accounts found — set coinbase.accountId in secrets.json to disambiguate."),
        };
    }

    public async Task<SendResult> SendAsync(string accountId, string toAddress, decimal sol, Guid idem, CancellationToken ct)
    {
        var body = new Dictionary<string, string>
        {
            ["type"] = "send",
            ["to"] = toAddress,
            ["amount"] = WalletPool.FormatSol(sol),   // invariant — never "2,54"
            ["currency"] = "SOL",
            ["network"] = "solana",
            ["idem"] = idem.ToString("D"),
        };

        using var doc = await SendJsonAsync(HttpMethod.Post, $"/v2/accounts/{accountId}/transactions", body, ct);
        var data = doc.RootElement.GetProperty("data");
        return new SendResult(data.GetProperty("id").GetString() ?? "", data.GetProperty("status").GetString() ?? "");
    }

    public async Task<string> GetTransactionStatusAsync(string accountId, string transactionId, CancellationToken ct)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, $"/v2/accounts/{accountId}/transactions/{transactionId}", null, ct);
        return doc.RootElement.GetProperty("data").GetProperty("status").GetString() ?? "";
    }

    /// <summary>
    /// Retries transport errors, 429 and 5xx only. Retrying the send POST is SAFE because the request
    /// carries its deterministic idem — Coinbase deduplicates it. Any other 4xx is terminal;
    /// 401/403 escalates to CoinbaseAuthException so the orchestrator aborts the entire run.
    /// </summary>
    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var uriPath = path.Split('?')[0];   // the uri claim excludes the query string
        Exception? last = null;

        for (var attempt = 0; attempt <= Backoff.Length; attempt++)
        {
            using var request = new HttpRequestMessage(method, path);
            var jwt = CoinbaseJwt.Create(_coinbase, method.Method, _coinbase.ApiHost, uriPath,
                DateTimeOffset.UtcNow, Convert.ToHexString(RandomNonce()));

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            request.Headers.TryAddWithoutValidation("CB-VERSION", _coinbase.ApiVersion);
            if (body is not null)
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try { response = await _http.SendAsync(request, ct); }
            catch (HttpRequestException ex) when (attempt < Backoff.Length)
            {
                last = ex;
                await Task.Delay(Jitter(Backoff[attempt]), ct);
                continue;
            }

            using (response)
            {
                var payload = await response.Content.ReadAsStringAsync(ct);

                if (response.IsSuccessStatusCode) return JsonDocument.Parse(payload);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new CoinbaseAuthException(
                        $"Coinbase rejected the request ({(int)response.StatusCode}). Check that the API key carries the " +
                        $"'wallet:transactions:send' scope and that the destination is allowlisted if address allowlisting " +
                        $"is enabled on the account. Key: {MaskKeyId(_coinbase.ApiKeyId)}. Response: {Truncate(payload)}");

                var retryable = response.StatusCode is HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                if (!retryable || attempt == Backoff.Length)
                    throw new FunderException($"Coinbase {method.Method} {uriPath} failed ({(int)response.StatusCode}): {Truncate(payload)}");

                last = new FunderException($"Coinbase returned {(int)response.StatusCode}.");
                await Task.Delay(Jitter(Backoff[attempt]), ct);
            }
        }

        throw new FunderException($"Coinbase {method.Method} {uriPath} failed after retries: {last?.Message}");
    }

    private static byte[] RandomNonce()
    {
        var bytes = new byte[16];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }

    private static TimeSpan Jitter(TimeSpan baseDelay)
        => baseDelay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));

    private static string Truncate(string value) => value.Length <= 400 ? value : value[..400] + "…";

    /// <summary>Key ids may be printed MASKED; the secret and the minted JWT never are.</summary>
    public static string MaskKeyId(string apiKeyId)
    {
        if (string.IsNullOrWhiteSpace(apiKeyId)) return "(unset)";
        var tail = apiKeyId.Split('/').LastOrDefault() ?? apiKeyId;
        return tail.Length <= 4 ? "****" : $"****{tail[^4..]}";
    }

    public void Dispose() => _http.Dispose();
}
```

- [ ] **Step 2: Implement `SolanaConfirmer.cs`**

```csharp
using Solnet.Rpc;
using Solnet.Rpc.Types;

namespace SpaceStroker.WalletFunder;

/// <summary>
/// Independent proof the SOL actually landed. Coinbase reporting `completed` is Coinbase's view of a
/// withdrawal it has broadcast; #63 cannot snipe with SOL that has not arrived.
/// </summary>
public sealed class SolanaConfirmer(SolanaSection solana)
{
    private readonly IRpcClient _rpc = ClientFactory.GetClient(solana.RpcUrl);

    public async Task<long> GetBalanceLamportsAsync(string address, CancellationToken ct)
    {
        var result = await _rpc.GetBalanceAsync(address, Commitment.Confirmed);
        if (!result.WasSuccessful)
            throw new FunderException($"Solana RPC could not read the balance of {address}: {result.Reason}");

        return (long)result.Result.Value;
    }

    /// <summary>
    /// Waits for a DELTA against the preflight snapshot, not an absolute balance: the wallet may already
    /// hold SOL, and `>=` tolerates an unrelated concurrent deposit.
    /// </summary>
    public async Task<bool> WaitForCreditAsync(
        string address, long snapshotLamports, long expectedLamports, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                if (await GetBalanceLamportsAsync(address, ct) - snapshotLamports >= expectedLamports) return true;
            }
            catch (FunderException) { /* transient RPC trouble — keep polling until the deadline */ }

            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }

        return false;
    }
}
```

- [ ] **Step 3: Build and run the whole suite (no new tests — I/O layer)**

Run: `dotnet test tools/SpaceStroker.Tools.sln`
Expected: build succeeds, all existing tests still pass.

- [ ] **Step 4: Commit**

```bash
git add tools/
git commit -m "Add the Coinbase HTTP client and on-chain confirmation."
```

---

### Task 10: Console report, orchestrator and CLI

**Files:**
- Create: `tools/SpaceStroker.WalletFunder/ConsoleReport.cs`
- Create: `tools/SpaceStroker.WalletFunder/Funder.cs`
- Create: `tools/SpaceStroker.WalletFunder/Program.cs`
- Create: `tools/SpaceStroker.WalletFunder.Tests/ConsoleReportTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces:
  - `static class ConsoleReport` — `string ShortAddress(string address)`, `void PrintPlan(...)`, `bool ConfirmTotal(decimal total, int count, TextReader input)`, `void PrintSummary(Receipts receipts)`
  - `sealed class Funder` — `Task<int> RunAsync(CliOptions options, CancellationToken ct)`
  - `Program.cs` — top-level statements + `file sealed class CliOptions`

- [ ] **Step 1: Write the failing tests for the pure console helpers**

```csharp
using Shouldly;
using Xunit;

namespace SpaceStroker.WalletFunder.Tests;

public class ConsoleReportTests
{
    [Fact]
    public void Shortens_a_long_address_but_keeps_both_ends_verifiable()
        => ConsoleReport.ShortAddress("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA")
            .ShouldBe("Tokenk…VQ5DA");

    [Fact]
    public void Leaves_a_short_address_alone()
        => ConsoleReport.ShortAddress("ABC").ShouldBe("ABC");

    [Fact]
    public void Confirms_when_the_typed_total_matches()
        => ConsoleReport.ConfirmTotal(12.75m, 3, new StringReader("12.75")).ShouldBeTrue();

    [Fact]
    public void Tolerates_surrounding_whitespace()
        => ConsoleReport.ConfirmTotal(12.75m, 3, new StringReader("  12.75  ")).ShouldBeTrue();

    [Theory]
    [InlineData("12.7")]
    [InlineData("yes")]
    [InlineData("")]
    [InlineData("12,75")]     // comma decimal is NOT accepted — the prompt prints the invariant form
    public void Refuses_anything_that_is_not_the_exact_total(string typed)
        => ConsoleReport.ConfirmTotal(12.75m, 3, new StringReader(typed)).ShouldBeFalse();
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/SpaceStroker.Tools.sln --filter ConsoleReportTests`
Expected: FAIL — `ConsoleReport` does not exist.

- [ ] **Step 3: Implement `ConsoleReport.cs`**

```csharp
namespace SpaceStroker.WalletFunder;

public static class ConsoleReport
{
    public static string ShortAddress(string address)
        => address.Length <= 14 ? address : $"{address[..6]}…{address[^5..]}";

    public static void PrintPlan(FundingPlan plan, Receipts receipts, decimal availableSol,
        decimal feeHeadroomSol, TimeSpan window, bool live)
    {
        Console.WriteLine();
        Console.WriteLine(live ? "FUNDING PLAN (LIVE)" : "FUNDING PLAN (dry run — nothing will be sent)");
        Console.WriteLine(new string('-', 78));
        Console.WriteLine($"{"#",-4}{"LABEL",-10}{"ADDRESS",-18}{"AMOUNT (SOL)",16}{"IDEM",-12}STATUS");

        var index = 0;
        foreach (var transfer in plan.Transfers)
        {
            index++;
            var entry = receipts.For(transfer);
            Console.WriteLine($"{index,-4}{transfer.Label,-10}{ShortAddress(transfer.Address),-18}" +
                              $"{WalletPool.FormatSol(transfer.SolAmount),16}{transfer.Idem.ToString("D")[..8],-12}{entry.Status}");
        }

        Console.WriteLine(new string('-', 78));
        Console.WriteLine($"wallets        : {plan.Transfers.Count}");
        Console.WriteLine($"batch total    : {WalletPool.FormatSol(plan.TotalSol)} SOL");
        Console.WriteLine($"fee headroom   : {WalletPool.FormatSol(feeHeadroomSol)} SOL");
        Console.WriteLine($"coinbase bal.  : {WalletPool.FormatSol(availableSol)} SOL");
        Console.WriteLine($"spread window  : {(window == TimeSpan.Zero ? "none (back to back)" : $"{window.TotalMinutes:0} min")}");
        Console.WriteLine($"plan hash      : {plan.PlanHash[..16]}…");
        Console.WriteLine($"receipts       : {receipts.Path}");
        Console.WriteLine();
    }

    /// <summary>Two deliberate acts before money moves: the --execute flag AND typing the exact total.</summary>
    public static bool ConfirmTotal(decimal total, int walletCount, TextReader input)
    {
        var expected = WalletPool.FormatSol(total);
        Console.WriteLine($"About to send {expected} SOL to {walletCount} wallet(s). This CANNOT be undone.");
        Console.Write($"Type the exact total ({expected}) to proceed: ");

        var typed = (input.ReadLine() ?? "").Trim();
        return string.Equals(typed, expected, StringComparison.Ordinal);
    }

    public static void PrintSummary(Receipts receipts)
    {
        Console.WriteLine();
        Console.WriteLine("SUMMARY");
        Console.WriteLine(new string('-', 78));
        Console.WriteLine($"{"LABEL",-10}{"ADDRESS",-18}{"AMOUNT",14}  {"STATUS",-14}TRANSACTION");

        foreach (var entry in receipts.Entries)
            Console.WriteLine($"{entry.Label,-10}{ShortAddress(entry.Address),-18}" +
                              $"{WalletPool.FormatSol(entry.AmountSol),14}  {entry.Status,-14}" +
                              $"{entry.CoinbaseTransactionId ?? "-"}{(entry.Error is null ? "" : $"  ({entry.Error})")}");

        Console.WriteLine(new string('-', 78));
        foreach (var group in receipts.Entries.GroupBy(e => e.Status).OrderBy(g => g.Key))
            Console.WriteLine($"{group.Key,-14}{group.Count()}");
        Console.WriteLine($"receipts       : {receipts.Path}");
        Console.WriteLine();
    }
}
```

- [ ] **Step 4: Run the console tests to verify they pass**

Run: `dotnet test tools/SpaceStroker.Tools.sln --filter ConsoleReportTests`
Expected: PASS (7 tests).

- [ ] **Step 5: Implement `Funder.cs` (the orchestrator)**

```csharp
namespace SpaceStroker.WalletFunder;

public sealed class Funder(SecretsFile secrets, FunderOptions options)
{
    /// <summary>0 = success · 1 = any failure/unconfirmed/pending or a fatal error.</summary>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var pool = options.PoolPath is null
            ? WalletPool.FromEntries(secrets.Wallets)
            : WalletPool.Parse(await File.ReadAllTextAsync(options.PoolPath, ct));

        if (pool.Count == 0)
        {
            Console.WriteLine("The wallet pool is empty — nothing to do.");
            return 0;
        }

        var plan = FundingPlan.Build(pool);
        var receipts = Receipts.LoadOrCreate(options.ReceiptsPath, plan, options.ForcePlanChange);

        using var coinbase = new CoinbaseClient(secrets.Coinbase);
        var confirmer = new SolanaConfirmer(secrets.Solana);

        var account = await coinbase.ResolveSolAccountAsync(secrets.Coinbase.AccountId, ct);

        // status: read-only. It can never send — that is the point of having it.
        if (options.StatusOnly)
        {
            await SettleOutstandingAsync(coinbase, confirmer, account, plan, receipts, snapshots: null, ct);
            ConsoleReport.PrintSummary(receipts);
            return receipts.AllConfirmed ? 0 : 1;
        }

        var preflight = Preflight.Check(pool, account.Balance, secrets.Funding.MinAmountSol,
            secrets.Funding.NetworkFeeHeadroomSol);

        if (!preflight.Ok)
        {
            foreach (var error in preflight.Errors) Console.Error.WriteLine($"preflight: {error}");
            throw new FunderException($"Preflight failed with {preflight.Errors.Count} problem(s) — nothing was sent.");
        }

        // Snapshot every destination BEFORE sending; confirmation is a delta against this.
        var snapshots = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var transfer in plan.Transfers)
            snapshots[transfer.Address] = await confirmer.GetBalanceLamportsAsync(transfer.Address, ct);

        var window = TimeSpan.FromMinutes(options.SpreadMinutes ?? secrets.Funding.SpreadMinutes);
        ConsoleReport.PrintPlan(plan, receipts, account.Balance, secrets.Funding.NetworkFeeHeadroomSol, window, options.Execute);

        if (!options.Execute)
        {
            Console.WriteLine("Dry run — nothing was sent. Re-run with --execute to fund the pool.");
            return 0;
        }

        if (!ConsoleReport.ConfirmTotal(plan.TotalSol, plan.Transfers.Count, Console.In))
        {
            Console.Error.WriteLine("error: confirmation did not match the batch total — nothing was sent.");
            return 1;
        }

        var outstanding = receipts.Outstanding(plan);
        var schedule = SendSchedule.Build(outstanding, window, Random.Shared);
        var started = DateTimeOffset.UtcNow;

        foreach (var scheduled in schedule)
        {
            var delay = scheduled.Offset - (DateTimeOffset.UtcNow - started);
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);

            var entry = receipts.For(scheduled.Transfer);

            if (Receipts.NeedsSend(entry))
            {
                try
                {
                    var sent = await coinbase.SendAsync(account.Id, scheduled.Transfer.Address,
                        scheduled.Transfer.SolAmount, scheduled.Transfer.Idem, ct);

                    // Persist the transaction id IMMEDIATELY — a crash here must never lose it.
                    receipts.Update(entry, e =>
                    {
                        e.CoinbaseTransactionId = sent.TransactionId;
                        e.Status = TransferStatus.Sent;
                        e.SentUtc = DateTimeOffset.UtcNow;
                        e.Error = null;
                    });
                    Console.WriteLine($"{scheduled.Transfer.Label}: sent {WalletPool.FormatSol(scheduled.Transfer.SolAmount)} SOL ({sent.TransactionId}).");
                }
                catch (CoinbaseAuthException) { throw; }   // fatal for the whole run
                catch (FunderException ex)
                {
                    receipts.Update(entry, e => { e.Status = TransferStatus.Failed; e.Error = ex.Message; });
                    Console.Error.WriteLine($"{scheduled.Transfer.Label}: FAILED — {ex.Message}");
                    continue;   // one wallet's failure never stops the batch
                }
            }

            await SettleAsync(coinbase, confirmer, account, scheduled.Transfer, entry, receipts,
                snapshots.GetValueOrDefault(scheduled.Transfer.Address, 0), ct);
        }

        ConsoleReport.PrintSummary(receipts);
        return receipts.AllConfirmed ? 0 : 1;
    }

    private async Task SettleOutstandingAsync(CoinbaseClient coinbase, SolanaConfirmer confirmer,
        CoinbaseAccount account, FundingPlan plan, Receipts receipts, Dictionary<string, long>? snapshots,
        CancellationToken ct)
    {
        foreach (var transfer in receipts.Outstanding(plan))
        {
            var entry = receipts.For(transfer);
            if (string.IsNullOrWhiteSpace(entry.CoinbaseTransactionId)) continue;   // never sends

            // Without a pre-send snapshot, require the wallet to hold at least the amount outright.
            await SettleAsync(coinbase, confirmer, account, transfer, entry, receipts,
                snapshots?.GetValueOrDefault(transfer.Address, 0) ?? 0, ct);
        }
    }

    private async Task SettleAsync(CoinbaseClient coinbase, SolanaConfirmer confirmer, CoinbaseAccount account,
        PlannedTransfer transfer, ReceiptEntry entry, Receipts receipts, long snapshotLamports, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entry.CoinbaseTransactionId)) return;

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(secrets.Funding.SendPollTimeoutSeconds);
        var status = "";

        while (DateTimeOffset.UtcNow < deadline)
        {
            status = await coinbase.GetTransactionStatusAsync(account.Id, entry.CoinbaseTransactionId!, ct);
            if (status is "completed" or "failed" or "canceled" or "expired") break;

            receipts.Update(entry, e => e.Status = TransferStatus.Pending);
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }

        if (status is "failed" or "canceled" or "expired")
        {
            receipts.Update(entry, e => { e.Status = TransferStatus.Failed; e.Error = $"Coinbase reported '{status}'."; });
            Console.Error.WriteLine($"{transfer.Label}: Coinbase reported '{status}'.");
            return;
        }

        if (status != "completed")
        {
            receipts.Update(entry, e => { e.Status = TransferStatus.Pending; e.Error = "Still pending when the run ended."; });
            Console.Error.WriteLine($"{transfer.Label}: still pending — re-run to resume polling (it will not be re-sent).");
            return;
        }

        var credited = await confirmer.WaitForCreditAsync(transfer.Address, snapshotLamports, transfer.Lamports,
            TimeSpan.FromSeconds(secrets.Funding.ChainConfirmTimeoutSeconds), ct);

        receipts.Update(entry, e =>
        {
            e.ChainConfirmed = credited;
            e.Status = credited ? TransferStatus.Confirmed : TransferStatus.Unconfirmed;
            e.SettledUtc = DateTimeOffset.UtcNow;
            e.Error = credited ? null : "Coinbase reported completed but no on-chain credit was observed.";
        });

        Console.WriteLine(credited
            ? $"{transfer.Label}: confirmed on-chain."
            : $"{transfer.Label}: UNCONFIRMED — Coinbase says completed but the balance did not rise.");
    }
}

public sealed record FunderOptions(
    string ReceiptsPath, string? PoolPath, bool Execute, bool StatusOnly, bool ForcePlanChange, int? SpreadMinutes);
```

- [ ] **Step 6: Implement `Program.cs`** (mirrors `deploy/tools/SpaceStroker.Deploy/Program.cs`)

```csharp
using SpaceStroker.WalletFunder;

// spacestroker-fund-wallets — funds a pool of Solana wallets with SOL from a Coinbase account.
// Usage:
//   spacestroker-fund-wallets fund              build the plan; with --execute, fund the pool
//   spacestroker-fund-wallets status            read-only: poll pending sends + re-check on-chain. Never sends.
// Options:
//   --secrets <path>       secrets file (default: tools/secrets.json next to the executable's repo path)
//   --pool <path>          load the wallet array from a standalone JSON file instead of secrets.wallets
//   --receipts <path>      receipts file (default: tools/receipts.json)
//   --execute              actually send. Without it, `fund` is a dry run.
//   --spread-minutes <n>   override funding.spreadMinutes for this run
//   --force-plan-change    resume against receipts whose plan hash differs (DANGEROUS — can double-fund)

try
{
    var opts = CliOptions.Parse(args);
    if (opts.ShowHelp)
    {
        CliOptions.PrintUsage();
        return 0;
    }

    var secrets = SecretsFile.Load(opts.SecretsPath);
    var funder = new Funder(secrets, new FunderOptions(
        ReceiptsPath: opts.ReceiptsPath,
        PoolPath: opts.PoolPath,
        Execute: opts.Execute,
        StatusOnly: opts.Command == "status",
        ForcePlanChange: opts.ForcePlanChange,
        SpreadMinutes: opts.SpreadMinutes));

    return await funder.RunAsync(CancellationToken.None);
}
catch (CoinbaseAuthException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}
catch (FunderException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

file sealed class CliOptions
{
    public string Command { get; private set; } = "fund";
    public string SecretsPath { get; private set; } = Path.Combine(AppContext.BaseDirectory, "secrets.json");
    public string ReceiptsPath { get; private set; } = Path.Combine(AppContext.BaseDirectory, "receipts.json");
    public string? PoolPath { get; private set; }
    public bool Execute { get; private set; }
    public bool ForcePlanChange { get; private set; }
    public int? SpreadMinutes { get; private set; }
    public bool ShowHelp { get; private set; }

    private static readonly string[] Commands = ["fund", "status"];

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();

        // Default the config paths to tools/ when running from the repo (bin/<cfg>/net8.0 → up 4).
        var toolsRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        if (Directory.Exists(toolsRoot))
        {
            o.SecretsPath = Path.Combine(toolsRoot, "secrets.json");
            o.ReceiptsPath = Path.Combine(toolsRoot, "receipts.json");
        }

        if (args.Length > 0 && args[0] is "-h" or "--help" or "help")
        {
            o.ShowHelp = true;
            return o;
        }

        var i = 0;
        if (args.Length > 0 && Commands.Contains(args[0]))
        {
            o.Command = args[0];
            i = 1;
        }

        for (; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--secrets": o.SecretsPath = Next(args, ref i, "--secrets"); break;
                case "--receipts": o.ReceiptsPath = Next(args, ref i, "--receipts"); break;
                case "--pool": o.PoolPath = Next(args, ref i, "--pool"); break;
                case "--execute": o.Execute = true; break;
                case "--force-plan-change": o.ForcePlanChange = true; break;
                case "--spread-minutes":
                    var raw = Next(args, ref i, "--spread-minutes");
                    if (!int.TryParse(raw, out var minutes) || minutes < 0)
                        throw new FunderException($"--spread-minutes must be a non-negative integer (was '{raw}').");
                    o.SpreadMinutes = minutes;
                    break;
                default: throw new FunderException($"Unknown option '{args[i]}'. Run with --help.");
            }
        }

        return o;
    }

    private static string Next(string[] args, ref int i, string option)
    {
        if (i + 1 >= args.Length) throw new FunderException($"{option} needs a value.");
        return args[++i];
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
        spacestroker-fund-wallets — fund Solana wallets with SOL from a Coinbase account.

        Commands:
          fund     (default) build the plan; with --execute, fund the pool. Without --execute it is a
                   dry run: plan table + full preflight, nothing sent.
          status   read-only: poll pending transactions + re-check on-chain confirmation. Never sends.

        Options:
          --secrets <path>       secrets file (default: tools/secrets.json)
          --pool <path>          wallet array from a standalone JSON file instead of secrets.wallets
          --receipts <path>      receipts file (default: tools/receipts.json)
          --execute              actually send (also requires typing the batch total to confirm)
          --spread-minutes <n>   override funding.spreadMinutes for this run
          --force-plan-change    resume against drifted receipts (DANGEROUS — can double-fund)
          -h, --help             this message
        """);
    }
}
```

- [ ] **Step 7: Build, run the full suite, and exercise the CLI**

```bash
dotnet test tools/SpaceStroker.Tools.sln
dotnet run --project tools/SpaceStroker.WalletFunder -- --help
dotnet run --project tools/SpaceStroker.WalletFunder -- fund
```
Expected: all tests pass; `--help` prints usage and exits 0; `fund` with no `secrets.json` exits 1 with
`error: Secrets file not found: …. Copy tools/secrets.example.json and fill it in.`

- [ ] **Step 8: Commit**

```bash
git add tools/
git commit -m "Add the funding orchestrator, console report and CLI."
```

---

### Task 11: README and final verification

**Files:**
- Create: `tools/README.md`

- [ ] **Step 1: Write `tools/README.md`**

Cover, in this order: what the tool is; the **operator preconditions** (CDP key with
`wallet:transactions:send`; destinations allowlisted if Coinbase address allowlisting is on; the
account already holds enough SOL); configuring `secrets.json` from the template; the **dry-run-first**
workflow; what `--execute` requires; resume + the plan-drift guard (**editing the pool between runs
can double-fund**); the network-fee headroom (`SOLAmount` is what the destination *receives*); exit
codes; and the relationship to card #63.

- [ ] **Step 2: Verify nothing secret is committable**

```bash
cd <workspace-root>/.worktrees/coinbase-wallet-funding/monkify-meta
printf '{"coinbase":{"apiKeySecret":"REAL"}}' > tools/secrets.json
git status --porcelain tools/          # must NOT list secrets.json
git check-ignore -v tools/secrets.json # must print the rule
rm tools/secrets.json
```
Expected: `git status` shows nothing for `secrets.json`; `check-ignore` prints the matching rule.

- [ ] **Step 3: Full green run**

```bash
dotnet build tools/SpaceStroker.Tools.sln
dotnet test tools/SpaceStroker.Tools.sln
```
Expected: build clean (no warnings-as-errors), every test passes.

- [ ] **Step 4: Commit and push**

```bash
git add tools/
git commit -m "Document the wallet funder."
git push origin feature/coinbase-wallet-funding
```

---

## Self-review

**Spec coverage** — §4.1 layout → Task 1; §4.2 csprojs → Task 1; §4.3 secrets model → Tasks 1 + 7;
§4.4 gitignore → Task 1 (+ verified again in Task 11); §5 CLI → Task 10; §6.1 JWT → Task 8;
§6.2 pool → Task 2; §6.3 plan/idem/preflight → Tasks 3 + 4; §6.4 send/settle → Tasks 9 + 10;
§6.5 schedule → Task 6; §6.6 receipts/drift → Task 5; §6.7 console → Task 10; §10.3 file checklist →
all tasks; §11 unit tests → Tasks 2–8, 10; §13 AC1–AC20 → see mapping below; §14 manual QA → README
(Task 11) and the hand-off.

**AC mapping** — AC1/AC15/AC16 Task 10 + 6 · AC2 Task 10 (`ConfirmTotal`) · AC3/AC4/AC5 Task 4 ·
AC6 Task 2 · AC7/AC8/AC9/AC12 Tasks 3 + 5 · AC10/AC11/AC13 Tasks 9 + 10 · AC14 Task 9
(`CoinbaseAuthException`) · AC17 Tasks 5 + 10 · AC18 Tasks 1 + 11 · AC19 Tasks 5 + 9 (`MaskKeyId`) ·
AC20 the whole test project.

**Placeholder scan** — no TBD/TODO; every code step carries complete code; no "add error handling"
hand-waves.

**Type consistency** — `PoolEntry(Label, PublicKey, SolAmount)` is used identically in Tasks 3, 4
and the test helpers; `PlannedTransfer(Label, Address, SolAmount, Lamports, Idem)` in Tasks 5, 6, 10;
`Receipts.For/Update/Outstanding/NeedsSend/Entries/Path/AllConfirmed` as consumed by Task 10;
`WalletPool.FormatSol/ToLamports/JsonOpts` in Tasks 4, 7, 9, 10; `CoinbaseSection` fields in Tasks 8
and 9. `FunderOptions` is defined in Task 10 alongside its only consumer.

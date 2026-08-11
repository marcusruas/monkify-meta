# Tools Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the `monkify-meta` console tools consistent (one shared secrets file, one `Receipts/`
folder, DbConsole moved in, dead Seeder removed, Deploy alone in `deploy/`), write the resulting
layout down as a convention, and shrink the knowledge base.

**Architecture:** All work happens in one repo, `monkify-meta`, inside the worktree
`E:/Monkify/.worktrees/tools-refactor/monkify-meta` on branch `feature/tools-refactor`. Tasks 1–9 are
mechanical code/doc changes to `tools/` and `deploy/`, each independently testable via the existing
xUnit suites. Tasks 10–12 are the knowledge-base sweep, which is **gated**: Task 10 produces a
proposal and **stops**; Tasks 11–12 only run after the user approves it.

**Tech Stack:** .NET 8 console apps (xUnit + Shouldly), `System.Text.Json` source-less binding with
`[JsonPropertyName]`, Markdown docs. Local SDK is **.NET 10** — see Global Constraints.

## Global Constraints

Copied verbatim from `specs/tools-refactor.md` §2. Every task's requirements implicitly include these.

- **Repo:** `monkify-meta` only. `monkify-backend` and `monkify-front` are **not touched** — no
  entity, handler, consumer, EF mapping, migration, endpoint, SignalR contract, composable, component.
- **Worktree-first:** all work in `E:/Monkify/.worktrees/tools-refactor/monkify-meta`. The
  `monkify-meta` hub at `E:/Monkify/monkify-meta` stays on `main` — never `git checkout -b` /
  `git switch` there. Push `feature/tools-refactor` to `origin` after every commit.
- **Secrets — mock only in committed files.** `tools/secrets.example.json` carries `REPLACE_ME`
  placeholders only. **Never open, `cat`, `echo`, or read the operator's real
  `E:/Monkify/monkify-meta/tools/secrets.json`, `tools/receipts.json`, `tools/snipe-receipts.json`,
  `tools/nightfall-receipts.json`, or `deploy/secrets.*.json`** — they hold live mainnet private keys,
  live Coinbase withdrawal credentials, and real transaction ids. Work only against the tracked
  template and the C# binding types.
- **Level = `Minor change`:** **no** integration test in `SpaceStroker.Tests.Integration`, **no** new
  `features/<slug>.md` doc. The `tools/` solution's unit tests must stay green.
- **`SpaceStroker.Tests` (backend) is NOT run** — the backend is untouched.
- **No database change:** no EF mapping, no migration, no snapshot.
- **`sed -i` is banned for bulk edits.** Files check out CRLF here; a repo-wide `sed` flips line
  endings and produces hundreds of phantom-modified files. Use targeted edits only.
- **Byte-offset marker scans are banned.** CRLF breaks them; scan line-by-line.
- **`git mv` on a directory fails with "Permission denied" while a `dotnet` process holds it open.**
  Kill stray `dotnet` processes before any move; if it still fails, move the children and then remove
  the empty parent.
- **The local SDK is .NET 10.** `dotnet new sln` writes `.slnx` unless given `--format sln`. The
  projects target `net8.0` and must stay on `net8.0`.
- **Item G (Tasks 10–12) is gated.** No `git rm` of any `features/` or `specs/` file before the user
  approves the Task 10 inventory.

**Working directory for every command below:**
`E:/Monkify/.worktrees/tools-refactor/monkify-meta`

---

### Task 1: Unified secrets template + `Receipts/` gitignore

**Files:**
- Modify: `tools/secrets.example.json` (add the `nightfall` block)
- Delete: `tools/secrets.nightfall.example.json`
- Modify: `tools/.gitignore`

**Interfaces:**
- Consumes: nothing (first task).
- Produces: the on-disk JSON shape every later task binds against — top-level `solana`, `coinbase`,
  `travelRule`, `funding`, `snipe`, **`nightfall` (containing `token`, `jupiter`, `sell`)**, and
  `wallets[]`.

- [ ] **Step 1: Add the `nightfall` block to the shared template**

Edit `tools/secrets.example.json`. Keep every existing key exactly as it is; insert the `nightfall`
object between `snipe` and `wallets`:

```json
  "nightfall": {
    "token": {
      "mint": "REPLACE_ME_TOKEN_MINT_ADDRESS"
    },
    "jupiter": {
      "apiBaseUrl": "https://lite-api.jup.ag/swap/v1",
      "slippageBps": 300,
      "maxPriceImpactPercent": 10,
      "prioritizationFeeLamports": 0,
      "quoteCacheSeconds": 10
    },
    "sell": {
      "maxConcurrentSells": 4,
      "minSolForFeeSol": 0.003,
      "refreshIntervalSeconds": 15,
      "confirmTimeoutSeconds": 90,
      "closeEmptiedTokenAccounts": true
    }
  },
```

⚠️ Do **not** merge `snipe.mint` and `nightfall.token.mint` into one key. They are the same CA in
practice, but the sniper refuses a graduated bonding curve while Nightfall requires a tradable route —
one key would let a single edit silently retarget both the buy and the sell side.

- [ ] **Step 2: Verify the template is valid JSON and has both mint keys**

Run:
```bash
python -c "import json;d=json.load(open('tools/secrets.example.json'));print(sorted(d));print('snipe.mint',d['snipe']['mint']);print('nightfall.token.mint',d['nightfall']['token']['mint'])"
```
Expected: the key list contains `nightfall`, and both mint lines print `REPLACE_ME_TOKEN_MINT_ADDRESS`.

- [ ] **Step 3: Delete the Nightfall-only template**

```bash
git rm tools/secrets.nightfall.example.json
```

- [ ] **Step 4: Rewrite `tools/.gitignore`**

Replace the whole file with:

```gitignore
# Local secrets — the operator's source of truth. NEVER commit (only the template is tracked).
secrets*.json
!secrets.example.json

# Run state, receipts and reports — addresses, amounts, transaction ids and signatures.
/Receipts/

# Pre-refactor leftovers: run state that predates tools/Receipts/. Keep these until tools/ is clean,
# otherwise a `git status` would offer to commit real addresses and transaction ids.
*receipts*.json
/reports/
/snipe-receipts*.json

# .NET build output for the console apps
**/bin/
**/obj/
```

- [ ] **Step 5: Verify the ignore rules cover both the new folder and the legacy files**

Run:
```bash
mkdir -p tools/Receipts && touch tools/Receipts/receipts.json tools/Receipts/nightfall-receipts.json
git check-ignore -v tools/Receipts/receipts.json tools/Receipts/nightfall-receipts.json tools/secrets.json
git check-ignore tools/secrets.example.json; echo "example ignored? exit=$? (want 1)"
rm tools/Receipts/receipts.json tools/Receipts/nightfall-receipts.json
```
Expected: the three ignored paths each print a matching rule; `tools/secrets.example.json` prints
nothing and reports `exit=1` (**not** ignored — it is the tracked template).

- [ ] **Step 6: Commit**

```bash
git add tools/secrets.example.json tools/.gitignore
git commit -m "Fold the Nightfall secrets template into the shared one; add tools/Receipts to gitignore."
git push origin feature/tools-refactor
```

---

### Task 2: Bind Nightfall's config from a `nightfall` section

**Files:**
- Modify: `tools/SpaceStroker.Nightfall/Infrastructure/SecretsFile.cs`
- Test: `tools/SpaceStroker.Nightfall.Tests/Infrastructure/SecretsFileTests.cs`

**Interfaces:**
- Consumes: the JSON shape from Task 1.
- Produces: `SecretsFile.Nightfall` of type `NightfallSection`, with members
  `NightfallSection.Token` (`TokenSection`), `.Jupiter` (`JupiterSection`), `.Sell` (`SellSection`).
  `SecretsFile.Solana` (`SolanaSection`), `SecretsFile.Wallets`, and `SecretsFile.Pool` are
  **unchanged**. `TokenSection` / `JupiterSection` / `SellSection` keep their current member names and
  `[JsonPropertyName]` values — only their position in the tree moves.

- [ ] **Step 1: Write the failing tests**

Append to `tools/SpaceStroker.Nightfall.Tests/Infrastructure/SecretsFileTests.cs`. The file already has
a `_directory` temp dir, a `Write(string json)` helper, and `TestPublicKey` / `TokenMint` constants —
reuse them. Add a private helper next to `Write` first:

```csharp
    /// <summary>
    /// A shared-shape secrets file carrying the other tools' sections alongside Nightfall's.
    /// The pool is EMPTY on purpose: WalletPool.ValidateKeyPair proves a wallet's key against its
    /// address at load time, and this test file has no valid keypair constant (every existing pool
    /// test either uses [] or asserts a deliberate rejection). An empty pool loads cleanly — see
    /// An_empty_pool_loads_cleanly_so_the_host_can_report_it_and_exit_zero — which is exactly what
    /// these tests need: the section binding, not the pool.
    /// </summary>
    private static string SharedShape(string nightfallBlock) => $$"""
    {
      "solana": { "rpcUrl": "https://rpc.example/?api-key=test" },
      "coinbase": { "apiKeyId": "id", "apiKeySecret": "secret", "keyAlgorithm": "ed25519" },
      "funding": { "spreadMinutes": 45 },
      "snipe": { "mint": "{{TokenMint}}" },
      {{nightfallBlock}}
      "wallets": []
    }
    """;

    private static string NightfallBlock(string mint = TokenMint, int slippageBps = 300,
                                         decimal maxImpact = 10m, int concurrency = 4) => $$"""
      "nightfall": {
        "token": { "mint": "{{mint}}" },
        "jupiter": { "apiBaseUrl": "https://lite-api.jup.ag/swap/v1", "slippageBps": {{slippageBps}},
                     "maxPriceImpactPercent": {{maxImpact}}, "prioritizationFeeLamports": 0,
                     "quoteCacheSeconds": 10 },
        "sell": { "maxConcurrentSells": {{concurrency}}, "minSolForFeeSol": 0.003,
                  "refreshIntervalSeconds": 15, "confirmTimeoutSeconds": 90,
                  "closeEmptiedTokenAccounts": true }
      },
    """;
```

⚠️ **Do not invent a `PrivateKey` for these fixtures.** `WalletPool.ValidateKeyPair` derives the
address from the key and rejects a mismatch, so a made-up base58 string fails the load and the test
would assert the wrong thing. The pool's tolerance of the funder's `SOLAmount` and the sniper's
`SupplyPercentage` is already covered by the existing `WalletPoolTests` and is **unchanged by this
card** — `RawPoolEntry` is not touched.

Then the tests:

```csharp
    [Fact]
    public void The_shared_file_binds_nightfall_config_from_its_own_section()
    {
        var path = Write(SharedShape(NightfallBlock()));

        var secrets = SecretsFile.Load(path);

        secrets.Nightfall.Token.Mint.ShouldBe(TokenMint);
        secrets.Nightfall.Jupiter.SlippageBps.ShouldBe(300);
        secrets.Nightfall.Sell.MaxConcurrentSells.ShouldBe(4);
        secrets.Nightfall.Jupiter.ApiBaseUrl.ShouldBe("https://lite-api.jup.ag/swap/v1");
        secrets.Solana.RpcUrl.ShouldBe("https://rpc.example/?api-key=test");
    }

    [Fact]
    public void The_other_tools_sections_in_the_shared_file_are_ignored()
    {
        // The file is SHARED. Nightfall must load cleanly past the funder's coinbase/funding blocks
        // and the sniper's snipe block, exactly as those tools ignore the nightfall block.
        var path = Write(SharedShape(NightfallBlock()));

        Should.NotThrow(() => SecretsFile.Load(path));
    }

    [Fact]
    public void A_missing_nightfall_section_is_named_explicitly()
    {
        // Without this branch the operator gets "nightfall.token.mint is still a placeholder",
        // which sends them looking for the wrong problem: the section simply is not there yet.
        var path = Write(SharedShape(nightfallBlock: ""));

        var ex = Should.Throw<NightfallException>(() => SecretsFile.Load(path));

        ex.Message.ShouldContain("nightfall");
        ex.Message.ShouldContain("secrets.example.json");
    }

    [Fact]
    public void Validation_messages_carry_the_nightfall_prefix()
    {
        // A message naming a key that no longer exists is worse than no message.
        var badMint = Write(SharedShape(NightfallBlock(mint: "REPLACE_ME_TOKEN_MINT_ADDRESS")));
        Should.Throw<NightfallException>(() => SecretsFile.Load(badMint))
              .Message.ShouldContain("nightfall.token.mint");

        var badSlippage = Write(SharedShape(NightfallBlock(slippageBps: 10_001)));
        Should.Throw<NightfallException>(() => SecretsFile.Load(badSlippage))
              .Message.ShouldContain("nightfall.jupiter.slippageBps");

        var badImpact = Write(SharedShape(NightfallBlock(maxImpact: 101m)));
        Should.Throw<NightfallException>(() => SecretsFile.Load(badImpact))
              .Message.ShouldContain("nightfall.jupiter.maxPriceImpactPercent");

        var badConcurrency = Write(SharedShape(NightfallBlock(concurrency: 0)));
        Should.Throw<NightfallException>(() => SecretsFile.Load(badConcurrency))
              .Message.ShouldContain("nightfall.sell.maxConcurrentSells");
    }
```

**Then migrate the existing tests in this file — they will otherwise all fail**, because every fixture
in it uses the old **flat** shape (`"token": {...}`, `"jupiter": {...}`, `"sell": {...}` at the top
level) and will now trip the new "the `nightfall` section is missing" branch:

1. `Write` helper — change the filename from `"secrets.nightfall.json"` to `"secrets.json"`.
2. `A_missing_file_names_both_the_file_and_the_template` — change
   `ShouldContain("secrets.nightfall.example.json")` to `ShouldContain("secrets.example.json")`.
3. **Every fixture that nests config** (`A_placeholder_token_mint_is_rejected_by_name`,
   `A_placeholder_wallet_key_is_rejected_by_alias`,
   `An_empty_pool_loads_cleanly_so_the_host_can_report_it_and_exit_zero`, the slippage / impact /
   concurrency range tests, the duplicate-address and truncated-address tests, …) — wrap its
   `token` / `jupiter` / `sell` objects in a `"nightfall": { … }` block. `solana` and `wallets` stay at
   the top level and must **not** be moved.
4. The one fixture that deliberately omits `solana.rpcUrl` to assert its message keeps working
   unchanged — that check runs **before** the `nightfall` branch, which is why the ordering in Step 3
   puts `solana.rpcUrl` first.

Run the file's tests after migrating and before adding the new ones, so you can tell a migration
mistake apart from a real regression.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/SpaceStroker.Nightfall.Tests -v q`
Expected: FAIL — `'SecretsFile' does not contain a definition for 'Nightfall'` (compile error).

- [ ] **Step 3: Restructure `SecretsFile`**

In `tools/SpaceStroker.Nightfall/Infrastructure/SecretsFile.cs`:

Replace the class doc comment. The current one asserts the file is "deliberately its OWN file …
mixing them with spend keys puts two very different blast radii in one place" — that is now false and
would contradict the code. Use:

```csharp
/// <summary>
/// The unversioned local tools/secrets.json — SHARED with spacestroker-fund-wallets and
/// spacestroker-snipe-token. This tool reads the top-level `solana` section, its own `nightfall`
/// section (token / jupiter / sell) and the shared `wallets` pool; the other tools' sections are
/// ignored here, exactly as this tool's are ignored there.
///
/// ⚠️ This file holds LIVE WALLET PRIVATE KEYS and live Coinbase withdrawal credentials. It is
/// git-ignored (tools/.gitignore) and nothing from it beyond public addresses may reach console
/// output, receipts or a report.
///
/// Note: card #166 originally kept these settings in their own secrets.nightfall.json, reasoning that
/// tools/secrets.json held withdrawal credentials — "a different blast radius". Card #167 merged them
/// because that had already stopped being true: card #63's sniper binds wallet PrivateKeys out of this
/// same file, so spend keys and withdrawal credentials were already co-located.
/// </summary>
```

Change the property block:

```csharp
    [JsonPropertyName("solana")]    public SolanaSection Solana { get; set; } = new();
    [JsonPropertyName("nightfall")] public NightfallSection? Nightfall { get; set; }
    [JsonPropertyName("wallets")]   public List<RawPoolEntry> Wallets { get; set; } = [];
```

⚠️ `Nightfall` is declared **nullable with no initializer on purpose** — that is what makes "the
section is absent" distinguishable from "the section is present but empty". It is assigned a
non-null value before `Load` returns, so every call site can use it without a null check.

Add the section type at the bottom of the file, next to the existing sections:

```csharp
/// <summary>
/// spacestroker-nightfall's own slice of the shared secrets file. The `solana` and `wallets` keys
/// live at the TOP level because every tool reads them; everything in here is Nightfall's alone.
/// </summary>
public sealed class NightfallSection
{
    [JsonPropertyName("token")]   public TokenSection Token { get; set; } = new();
    [JsonPropertyName("jupiter")] public JupiterSection Jupiter { get; set; } = new();
    [JsonPropertyName("sell")]    public SellSection Sell { get; set; } = new();
}
```

In `Load`, replace the not-found message and re-path every validation. The current body validates
`parsed.Solana.RpcUrl`, `parsed.Token.Mint`, `parsed.Jupiter.*`, `parsed.Sell.*`. Rewrite that region
as:

```csharp
        if (!File.Exists(path))
            throw new NightfallException(
                $"Secrets file not found: {path}. Copy tools/secrets.example.json and fill it in.");
```

```csharp
        if (IsPlaceholder(parsed.Solana.RpcUrl))
            throw new NightfallException($"{path}: solana.rpcUrl is still a placeholder — fill in the real RPC endpoint.");

        if (parsed.Nightfall is null)
            throw new NightfallException(
                $"{path}: the 'nightfall' section is missing. This tool now shares tools/secrets.json " +
                $"with spacestroker-fund-wallets and spacestroker-snipe-token — copy the 'nightfall' " +
                $"block from tools/secrets.example.json and fill it in.");

        var nightfall = parsed.Nightfall;

        if (IsPlaceholder(nightfall.Token.Mint))
            throw new NightfallException($"{path}: nightfall.token.mint is still a placeholder — fill in the Token CA.");

        WalletPool.ValidateAddress(nightfall.Token.Mint.Trim(), "nightfall.token.mint");
        nightfall.Token.Mint = nightfall.Token.Mint.Trim();

        if (nightfall.Jupiter.SlippageBps is < 0 or > 10_000)
            throw new NightfallException($"{path}: nightfall.jupiter.slippageBps must be between 0 and 10000.");

        if (nightfall.Jupiter.MaxPriceImpactPercent is < 0m or > 100m)
            throw new NightfallException($"{path}: nightfall.jupiter.maxPriceImpactPercent must be between 0 and 100.");

        if (nightfall.Sell.MaxConcurrentSells < 1)
            throw new NightfallException($"{path}: nightfall.sell.maxConcurrentSells must be at least 1.");
```

Leave the pool block below it (`parsed.Pool = WalletPool.FromEntries(parsed.Wallets);`, the
placeholder/address/keypair loop and the duplicate-address check) **exactly as it is** — it is shared
config and its messages already name `wallet '<alias>'`, not a section path.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tools/SpaceStroker.Nightfall.Tests -v q`
Expected: FAIL — the project still does not compile, because `Program.cs` and the Application/
Infrastructure classes read `secrets.Token` / `.Jupiter` / `.Sell`. That is Task 3. If the **only**
errors are `'SecretsFile' does not contain a definition for 'Token'/'Jupiter'/'Sell'` at those call
sites, this step is done — proceed to Task 3 and re-run there.

- [ ] **Step 5: Do not commit yet**

The solution does not compile until Task 3 lands. Task 2 and Task 3 share one commit, made at the end
of Task 3.

---

### Task 3: Repoint Nightfall's call sites and its default secrets path

**Files:**
- Modify: `tools/SpaceStroker.Nightfall/Program.cs:40,47,48,51,52,53,54,55,60`
- Modify: `tools/SpaceStroker.Nightfall/Application/ApiEndpoints.cs:50,51`
- Modify: `tools/SpaceStroker.Nightfall/Application/ConsoleReport.cs:18,22,23,24,25`
- Modify: `tools/SpaceStroker.Nightfall/Application/PollLoop.cs:55,70`
- Modify: `tools/SpaceStroker.Nightfall/Application/SellRunner.cs:52,124,136,143,147,165,181,204`
- Modify: `tools/SpaceStroker.Nightfall/Application/CliOptions.cs:21` and its `PrintUsage()`

**Interfaces:**
- Consumes: `SecretsFile.Nightfall` from Task 2.
- Produces: a compiling `SpaceStroker.Nightfall` whose `CliOptions.SecretsPath` defaults to
  `<toolsRoot>/secrets.json`. `CliOptions.ReceiptsPath` is **not** changed here — that is Task 4.

- [ ] **Step 1: Repoint every config read**

These are the only 18 sites (verified by grep). Apply mechanically — **do not `sed`**, edit each file:

| File | Was | Becomes |
| --- | --- | --- |
| `Program.cs:40` | `secrets.Token.Mint` | `secrets.Nightfall.Token.Mint` |
| `Program.cs:47` | `secrets.Jupiter` | `secrets.Nightfall.Jupiter` |
| `Program.cs:48` | `secrets.Jupiter.QuoteCacheSeconds` | `secrets.Nightfall.Jupiter.QuoteCacheSeconds` |
| `Program.cs:51` | `secrets.Token.Mint` | `secrets.Nightfall.Token.Mint` |
| `Program.cs:52` | `secrets.Sell.MinSolForFeeSol` | `secrets.Nightfall.Sell.MinSolForFeeSol` |
| `Program.cs:53` | `secrets.Jupiter.SlippageBps` | `secrets.Nightfall.Jupiter.SlippageBps` |
| `Program.cs:54` | `secrets.Jupiter.MaxPriceImpactPercent` | `secrets.Nightfall.Jupiter.MaxPriceImpactPercent` |
| `Program.cs:55` | `secrets.Sell.MaxConcurrentSells` | `secrets.Nightfall.Sell.MaxConcurrentSells` |
| `Program.cs:60` | `secrets.Token.Mint` | `secrets.Nightfall.Token.Mint` |
| `ApiEndpoints.cs:50` | `secrets.Token.Mint`, `secrets.Jupiter.SlippageBps` | `secrets.Nightfall.Token.Mint`, `secrets.Nightfall.Jupiter.SlippageBps` |
| `ApiEndpoints.cs:51` | `secrets.Token.Mint` | `secrets.Nightfall.Token.Mint` |
| `ConsoleReport.cs:18` | `secrets.Token.Mint` | `secrets.Nightfall.Token.Mint` |
| `ConsoleReport.cs:22` | `secrets.Jupiter.ApiBaseUrl` | `secrets.Nightfall.Jupiter.ApiBaseUrl` |
| `ConsoleReport.cs:23` | `secrets.Jupiter.SlippageBps` | `secrets.Nightfall.Jupiter.SlippageBps` |
| `ConsoleReport.cs:24` | `secrets.Jupiter.MaxPriceImpactPercent` | `secrets.Nightfall.Jupiter.MaxPriceImpactPercent` |
| `ConsoleReport.cs:25` | `secrets.Sell.MaxConcurrentSells` | `secrets.Nightfall.Sell.MaxConcurrentSells` |
| `PollLoop.cs:55` | `secrets.Token.Mint` | `secrets.Nightfall.Token.Mint` |
| `PollLoop.cs:70` | `secrets.Sell.RefreshIntervalSeconds` | `secrets.Nightfall.Sell.RefreshIntervalSeconds` |
| `SellRunner.cs:52` | `secrets.Sell.MaxConcurrentSells` | `secrets.Nightfall.Sell.MaxConcurrentSells` |
| `SellRunner.cs:124` | `secrets.Sell.MinSolForFeeSol` | `secrets.Nightfall.Sell.MinSolForFeeSol` |
| `SellRunner.cs:136` | `secrets.Token.Mint` | `secrets.Nightfall.Token.Mint` |
| `SellRunner.cs:143` | `secrets.Jupiter.MaxPriceImpactPercent` | `secrets.Nightfall.Jupiter.MaxPriceImpactPercent` |
| `SellRunner.cs:147` | `secrets.Jupiter.MaxPriceImpactPercent` | `secrets.Nightfall.Jupiter.MaxPriceImpactPercent` |
| `SellRunner.cs:165` | `secrets.Token.Mint` | `secrets.Nightfall.Token.Mint` |
| `SellRunner.cs:181` | `secrets.Sell.ConfirmTimeoutSeconds` | `secrets.Nightfall.Sell.ConfirmTimeoutSeconds` |
| `SellRunner.cs:204` | `secrets.Sell.CloseEmptiedTokenAccounts` | `secrets.Nightfall.Sell.CloseEmptiedTokenAccounts` |

⚠️ The **guardrail semantics must not change**. `SellRunner.cs:124` (low-SOL exclusion),
`:143`/`:147` (price-impact abort before broadcast), `:52` (concurrency cap) and `:181` (confirm
timeout) are money-safety gates. This task moves where the number is *read from* and nothing else — no
default, no comparison, no ordering changes.

- [ ] **Step 2: Change the default secrets path and the usage text**

In `tools/SpaceStroker.Nightfall/Application/CliOptions.cs`, line 21:

```csharp
        o.SecretsPath = Path.Combine(toolsRoot, "secrets.json");
```

In the same file's `PrintUsage()`, change the `--secrets` line:

```
          --secrets <path>    secrets file (default: tools/secrets.json)
```

- [ ] **Step 3: Build and run the tests**

Run:
```bash
dotnet build tools/SpaceStroker.Tools.sln
dotnet test tools/SpaceStroker.Nightfall.Tests -v q
```
Expected: build succeeds; all Nightfall tests PASS, including the four added in Task 2.

- [ ] **Step 4: Verify no stale reference survives**

Run:
```bash
grep -rn "secrets\.Token\|secrets\.Jupiter\|secrets\.Sell\|secrets\.nightfall\.json\|secrets\.nightfall\.example" --include=*.cs tools/ | grep -v "secrets\.Nightfall\."
```
Expected: **no output**.

- [ ] **Step 5: Commit**

```bash
git add tools/SpaceStroker.Nightfall tools/SpaceStroker.Nightfall.Tests
git commit -m "Read Nightfall config from a nightfall section of the shared tools/secrets.json."
git push origin feature/tools-refactor
```

---

### Task 4: Move all run state into `tools/Receipts/`

**Files:**
- Modify: `tools/SpaceStroker.WalletFunder/Application/CliOptions.cs:26` + `PrintUsage()`
- Modify: `tools/SpaceStroker.TokenSniper/Application/CliOptions.cs:27-28` + `PrintUsage()`
- Modify: `tools/SpaceStroker.Nightfall/Application/CliOptions.cs:21` (receipts line) + `PrintUsage()`
- Create: `tools/SpaceStroker.WalletFunder.Tests/Application/CliOptionsTests.cs`
- Create: `tools/SpaceStroker.Nightfall.Tests/Application/CliOptionsTests.cs`
- Modify: `tools/SpaceStroker.TokenSniper.Tests/Application/CliOptionsTests.cs`

**Interfaces:**
- Consumes: the `/Receipts/` ignore rule from Task 1.
- Produces: `CliOptions.ReceiptsPath` defaults of `<toolsRoot>/Receipts/<name>.json` in all three
  tools, and `CliOptions.ReportsRootPath` of `<toolsRoot>/Receipts/reports` in the sniper. The
  `--receipts` / `--reports` override flags keep their current behaviour.

- [ ] **Step 1: Write the failing tests**

Create `tools/SpaceStroker.WalletFunder.Tests/Application/CliOptionsTests.cs`:

```csharp
using Shouldly;
using SpaceStroker.WalletFunder.Application;
using Xunit;

namespace SpaceStroker.WalletFunder.Tests.Application;

public class CliOptionsTests
{
    [Fact]
    public void Run_state_defaults_into_the_Receipts_folder()
    {
        // One git-ignored folder for every tool's run state, instead of four loose files in tools/.
        var options = CliOptions.Parse([]);

        options.ReceiptsPath.Replace('\\', '/').ShouldEndWith("/Receipts/receipts.json");
        options.SecretsPath.Replace('\\', '/').ShouldEndWith("/secrets.json");
    }

    [Fact]
    public void An_explicit_receipts_path_still_wins()
    {
        var options = CliOptions.Parse(["fund", "--receipts", "/tmp/custom.json"]);

        options.ReceiptsPath.ShouldBe("/tmp/custom.json");
    }
}
```

Create `tools/SpaceStroker.Nightfall.Tests/Application/CliOptionsTests.cs`:

```csharp
using Shouldly;
using SpaceStroker.Nightfall.Application;
using Xunit;

namespace SpaceStroker.Nightfall.Tests.Application;

public class CliOptionsTests
{
    [Fact]
    public void Config_defaults_to_the_shared_secrets_file_and_the_Receipts_folder()
    {
        var options = CliOptions.Parse([]);

        options.SecretsPath.Replace('\\', '/').ShouldEndWith("/secrets.json");
        options.SecretsPath.ShouldNotContain("nightfall.json");
        options.ReceiptsPath.Replace('\\', '/').ShouldEndWith("/Receipts/nightfall-receipts.json");
    }

    [Fact]
    public void Explicit_paths_still_win()
    {
        var options = CliOptions.Parse(["--secrets", "/tmp/s.json", "--receipts", "/tmp/r.json"]);

        options.SecretsPath.ShouldBe("/tmp/s.json");
        options.ReceiptsPath.ShouldBe("/tmp/r.json");
    }
}
```

In the **existing** `tools/SpaceStroker.TokenSniper.Tests/Application/CliOptionsTests.cs`, add:

```csharp
    [Fact]
    public void Run_state_and_reports_default_into_the_Receipts_folder()
    {
        var options = CliOptions.Parse([]);

        options.ReceiptsPath.Replace('\\', '/').ShouldEndWith("/Receipts/snipe-receipts.json");
        options.ReportsRootPath.Replace('\\', '/').ShouldEndWith("/Receipts/reports");
    }
```

If that file already asserts the **old** defaults (`/snipe-receipts.json`, `/reports` directly under
`toolsRoot`), update those existing assertions to the new paths rather than leaving two contradictory
tests.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/SpaceStroker.Tools.sln -v q`
Expected: FAIL — the new assertions report paths ending in `/receipts.json`,
`/nightfall-receipts.json`, `/snipe-receipts.json` and `/reports` without the `Receipts/` segment.

- [ ] **Step 3: Change the defaults**

`tools/SpaceStroker.WalletFunder/Application/CliOptions.cs` line 26:

```csharp
        o.ReceiptsPath = Path.Combine(toolsRoot, "Receipts", "receipts.json");
```

`tools/SpaceStroker.TokenSniper/Application/CliOptions.cs` lines 27–28:

```csharp
        options.ReceiptsPath = Path.Combine(toolsRoot, "Receipts", "snipe-receipts.json");
        options.ReportsRootPath = Path.Combine(toolsRoot, "Receipts", "reports");
```

`tools/SpaceStroker.Nightfall/Application/CliOptions.cs` line 21:

```csharp
        o.ReceiptsPath = Path.Combine(toolsRoot, "Receipts", "nightfall-receipts.json");
```

Update the `--receipts` (and the sniper's `--reports`) default text in all three `PrintUsage()` bodies
to `tools/Receipts/...`.

- [ ] **Step 4: Make each writer create the folder**

The sniper already creates its `reports/<timestamp>/` directory. The funder and Nightfall write a file
directly, so add a directory guard immediately before the first write in each store.

In `tools/SpaceStroker.WalletFunder/Infrastructure/ReceiptsStore.cs`, at the top of the method that
writes the file to disk (the one calling `File.WriteAllText`):

```csharp
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
```

Do the same in `tools/SpaceStroker.Nightfall/Infrastructure/ReceiptsStore.cs`, and in
`tools/SpaceStroker.TokenSniper/Infrastructure/ReceiptsStore.cs` if it writes without creating first.

⚠️ The funder's class has a `Path` **property** that shadows `System.IO.Path`; that is why the snippet
qualifies the static call. Check the surrounding code and match whichever form compiles in each file.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tools/SpaceStroker.Tools.sln -v q`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add tools/
git commit -m "Default all tools run state and reports into tools/Receipts/."
git push origin feature/tools-refactor
```

---

### Task 5: Refuse to start on an unseen pre-refactor receipts file

**Files:**
- Create: `tools/SpaceStroker.WalletFunder/Domain/LegacyReceipts.cs`
- Create: `tools/SpaceStroker.WalletFunder.Tests/Domain/LegacyReceiptsTests.cs`
- Modify: `tools/SpaceStroker.WalletFunder/Program.cs` (call the check before `Funder.RunAsync`)
- Modify: `tools/SpaceStroker.TokenSniper/Program.cs` (warning only)
- Modify: `tools/SpaceStroker.Nightfall/Program.cs` (warning only)

**Interfaces:**
- Consumes: the new default paths from Task 4.
- Produces: `LegacyReceipts.Describe(bool currentExists, bool legacyExists, string currentPath,
  string legacyPath)` → `string?` — a message when the operator must act, `null` otherwise. Pure and
  IO-free, so it is unit-testable without touching the filesystem, per the repo's
  Domain/Infrastructure/Application rule.

- [ ] **Step 1: Write the failing test**

Create `tools/SpaceStroker.WalletFunder.Tests/Domain/LegacyReceiptsTests.cs`:

```csharp
using Shouldly;
using SpaceStroker.WalletFunder.Domain;
using Xunit;

namespace SpaceStroker.WalletFunder.Tests.Domain;

public class LegacyReceiptsTests
{
    private const string Current = "/tools/Receipts/receipts.json";
    private const string Legacy = "/tools/receipts.json";

    [Fact]
    public void A_legacy_file_with_no_current_one_is_reported()
    {
        // THE money case. Each wallet's Coinbase idem is derived from the plan, so a receipts file
        // the tool cannot see reads as "no prior run" — and a resend is a REAL second transfer.
        var message = LegacyReceipts.Describe(currentExists: false, legacyExists: true, Current, Legacy);

        message.ShouldNotBeNull();
        message.ShouldContain(Legacy);
        message.ShouldContain(Current);
    }

    [Fact]
    public void A_current_file_wins_even_when_a_legacy_one_lingers()
    {
        LegacyReceipts.Describe(currentExists: true, legacyExists: true, Current, Legacy).ShouldBeNull();
    }

    [Fact]
    public void A_first_run_is_silent()
    {
        LegacyReceipts.Describe(currentExists: false, legacyExists: false, Current, Legacy).ShouldBeNull();
    }

    [Fact]
    public void A_current_file_alone_is_silent()
    {
        LegacyReceipts.Describe(currentExists: true, legacyExists: false, Current, Legacy).ShouldBeNull();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tools/SpaceStroker.WalletFunder.Tests -v q`
Expected: FAIL — `The name 'LegacyReceipts' does not exist in the current namespace`.

- [ ] **Step 3: Write the implementation**

Create `tools/SpaceStroker.WalletFunder/Domain/LegacyReceipts.cs`:

```csharp
namespace SpaceStroker.WalletFunder.Domain;

/// <summary>
/// Card #167 moved run state from tools/*.json into tools/Receipts/. This detects the one dangerous
/// consequence of that move.
///
/// ⚠️ The funder derives each wallet's Coinbase idempotency token from the plan, so a receipts file it
/// cannot see is indistinguishable from "no prior run" — and a re-run then issues a genuinely NEW
/// transfer rather than being deduplicated. The pre-refactor file is deliberately left where it is
/// (moving live run state automatically is its own hazard), so the tool must notice it and stop.
///
/// IO-free on purpose: the caller does the two File.Exists calls, which keeps the money decision
/// unit-testable without a filesystem.
/// </summary>
public static class LegacyReceipts
{
    public static string? Describe(bool currentExists, bool legacyExists, string currentPath, string legacyPath)
    {
        if (currentExists || !legacyExists) return null;

        return $"Found a pre-refactor receipts file at {legacyPath} but nothing at {currentPath}. " +
               $"Move it there (or pass --receipts {legacyPath}) before running — starting fresh could " +
               $"re-send a transfer.";
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tools/SpaceStroker.WalletFunder.Tests -v q`
Expected: PASS.

- [ ] **Step 5: Wire it into the funder as a fatal stop**

In `tools/SpaceStroker.WalletFunder/Program.cs`, after `CliOptions.Parse` / the `ShowHelp` early
return and **before** `SecretsFile.Load`:

```csharp
    var legacyReceipts = Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")), "receipts.json");
    var legacyWarning = LegacyReceipts.Describe(
        File.Exists(opts.ReceiptsPath), File.Exists(legacyReceipts), opts.ReceiptsPath, legacyReceipts);

    if (legacyWarning is not null)
        throw new FunderException(legacyWarning);
```

`FunderException` is already caught in `Program.cs` and returns exit code 1, so no new catch is needed.
Add `using SpaceStroker.WalletFunder.Domain;` if it is not already there.

- [ ] **Step 6: Wire it into the other two as a warning**

The sniper and Nightfall resume from **on-chain state**, not from receipts, so a missing file cannot
double-spend there — but a silently orphaned audit trail is still worth naming. In
`tools/SpaceStroker.TokenSniper/Program.cs` and `tools/SpaceStroker.Nightfall/Program.cs`, at the same
point, print instead of throwing:

```csharp
    // Sniper: legacy name "snipe-receipts.json"; Nightfall: "nightfall-receipts.json".
    var legacyPath = Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")), "<legacy-name>.json");
    if (!File.Exists(options.ReceiptsPath) && File.Exists(legacyPath))
        Console.WriteLine($"note: a pre-refactor receipts file is still at {legacyPath}; " +
                          $"this run writes to {options.ReceiptsPath}. On-chain state is the resume truth, " +
                          $"so this is an audit-trail note, not a money risk.");
```

Substitute the real legacy filename in each. Do **not** duplicate `LegacyReceipts` into those
projects — the message differs and the funder's type carries a money-specific warning.

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test tools/SpaceStroker.Tools.sln -v q`
Expected: all PASS.

- [ ] **Step 8: Commit**

```bash
git add tools/
git commit -m "Stop the funder when a pre-refactor receipts file would be missed."
git push origin feature/tools-refactor
```

---

### Task 6: Move `SpaceStroker.DbConsole` into the tools solution

**Files:**
- Move: `deploy/tools/SpaceStroker.DbConsole/` → `tools/SpaceStroker.DbConsole/`
- Modify: `tools/SpaceStroker.Tools.sln`
- Modify: `deploy/tools/SpaceStroker.DeployTools.sln` (drop the DbConsole entry — the file itself is
  renamed in Task 7)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `tools/SpaceStroker.DbConsole/SpaceStroker.DbConsole.csproj` inside
  `SpaceStroker.Tools.sln`. **No code change** — `Program.FindSecretsFile` keeps resolving
  `deploy/secrets.<env>.json`.

- [ ] **Step 1: Confirm the walk-up still resolves before moving anything**

`FindSecretsFile` walks up from `AppContext.BaseDirectory` looking for
`<dir>/monkify-meta/deploy/secrets.<env>.json`. From `deploy/tools/SpaceStroker.DbConsole/bin/<cfg>/net8.0`
it climbs to `E:/Monkify`; from `tools/SpaceStroker.DbConsole/bin/<cfg>/net8.0` it also climbs to
`E:/Monkify`. Both reach the workspace root, so the lookup is unaffected.

Run, to see the two path depths for yourself:
```bash
python -c "
import os
for p in ['deploy/tools/SpaceStroker.DbConsole/bin/Debug/net8.0','tools/SpaceStroker.DbConsole/bin/Debug/net8.0']:
    d=os.path.abspath(p); hops=[]
    while d and os.path.dirname(d)!=d:
        hops.append(os.path.basename(d)); d=os.path.dirname(d)
    print(p,'->',len(hops),'hops to root')
"
```
Expected: both lists terminate at the drive root, so `E:/Monkify` is on both walks. Record this — it is
the justification for "no code change".

- [ ] **Step 2: Kill stray `dotnet` processes, then move the project**

A running `dotnet` locks worktree directories and makes `git mv <dir>` fail with Permission denied.

```bash
powershell -NoProfile -Command "Get-Process dotnet -ErrorAction SilentlyContinue | Stop-Process -Force"
git mv deploy/tools/SpaceStroker.DbConsole tools/SpaceStroker.DbConsole
```

If it still fails, move the tracked children individually and then delete the empty parent.

- [ ] **Step 3: Register it in the tools solution and unregister it from the deploy one**

```bash
dotnet sln tools/SpaceStroker.Tools.sln add tools/SpaceStroker.DbConsole/SpaceStroker.DbConsole.csproj
dotnet sln deploy/tools/SpaceStroker.DeployTools.sln remove deploy/tools/SpaceStroker.DbConsole/SpaceStroker.DbConsole.csproj
```

⚠️ The local SDK is .NET 10. Confirm both commands edited the existing `.sln` files and did **not**
create a `.slnx`: `ls tools/*.sln* deploy/tools/*.sln*` must show only `.sln`.

- [ ] **Step 4: Build both solutions**

Run:
```bash
dotnet build tools/SpaceStroker.Tools.sln
dotnet build deploy/tools/SpaceStroker.DeployTools.sln
```
Expected: both succeed. The tools solution now builds 4 apps + 3 test projects; the deploy solution
builds 1 app + 1 test project.

- [ ] **Step 5: Verify nothing still points at the old location**

Run:
```bash
grep -rn "deploy/tools/SpaceStroker.DbConsole\|deploy\\\\tools\\\\SpaceStroker.DbConsole" --include=*.cs --include=*.sln --include=*.md . | grep -v "/bin/\|/obj/"
```
Expected: only hits in `deploy/README.md` and `deploy/docs/FIRST-DEV-DEPLOY.*.md`, which Task 8 fixes.

- [ ] **Step 6: Commit**

```bash
git add -A tools deploy
git commit -m "Move SpaceStroker.DbConsole into the tools solution."
git push origin feature/tools-refactor
```

---

### Task 7: Remove the Seeder remnants and leave Deploy alone in `deploy/`

**Files:**
- Delete: `deploy/tools/SpaceStroker.Seeder/`, `deploy/tools/SpaceStroker.Seeder.Tests/` (untracked
  `bin`/`obj` only), `deploy/secrets.seeder.json` (untracked)
- Move: `deploy/tools/SpaceStroker.Deploy/` → `deploy/SpaceStroker.Deploy/`
- Move: `deploy/tools/SpaceStroker.Deploy.Tests/` → `deploy/SpaceStroker.Deploy.Tests/`
- Create: `deploy/SpaceStroker.Deploy.sln`
- Delete: `deploy/tools/SpaceStroker.DeployTools.sln`

**Interfaces:**
- Consumes: the DbConsole-free deploy solution from Task 6.
- Produces: `deploy/SpaceStroker.Deploy.sln` containing exactly `SpaceStroker.Deploy` and
  `SpaceStroker.Deploy.Tests`; `deploy/tools/` gone.

- [ ] **Step 1: Confirm the Seeder is untracked build residue, not source**

Run:
```bash
git ls-files deploy/tools/SpaceStroker.Seeder deploy/tools/SpaceStroker.Seeder.Tests deploy/secrets.seeder.json
find deploy/tools/SpaceStroker.Seeder deploy/tools/SpaceStroker.Seeder.Tests -type f -name "*.cs" -o -name "*.csproj" 2>/dev/null | head
```
Expected: **no output from either** — nothing tracked, no source. If either prints anything, stop and
report: the assumption behind this task is wrong.

- [ ] **Step 2: Delete the residue**

```bash
rm -rf deploy/tools/SpaceStroker.Seeder deploy/tools/SpaceStroker.Seeder.Tests
rm -f deploy/secrets.seeder.json
git status --short
```
Expected: `git status` shows **nothing** for these paths — they were untracked, so this is a
working-tree cleanup with nothing to commit.

⚠️ Do **not** touch `architecture.md` §8e's line *"Replaces `SpaceStroker.Seeder`, the manual 6-hourly
console app, which is deleted"* — it is accurate historical context for the house seed bet. Likewise
leave `specs/auto-bet-seeder.md`, `specs/house-seed-bet.md` and `features/house-seed-bet.md` alone:
their "seeder" is the **backend auto-bet seeder**, a different thing sharing the word.

- [ ] **Step 3: Move the deploy projects up one level**

```bash
powershell -NoProfile -Command "Get-Process dotnet -ErrorAction SilentlyContinue | Stop-Process -Force"
git mv deploy/tools/SpaceStroker.Deploy deploy/SpaceStroker.Deploy
git mv deploy/tools/SpaceStroker.Deploy.Tests deploy/SpaceStroker.Deploy.Tests
```

- [ ] **Step 4: Create the renamed solution and drop the old one**

```bash
git rm deploy/tools/SpaceStroker.DeployTools.sln
dotnet new sln --name SpaceStroker.Deploy --output deploy --format sln
dotnet sln deploy/SpaceStroker.Deploy.sln add deploy/SpaceStroker.Deploy/SpaceStroker.Deploy.csproj
dotnet sln deploy/SpaceStroker.Deploy.sln add deploy/SpaceStroker.Deploy.Tests/SpaceStroker.Deploy.Tests.csproj
rmdir deploy/tools 2>/dev/null || ls -la deploy/tools
```

⚠️ `--format sln` is required — the .NET 10 SDK writes `.slnx` by default and nothing else in this
workspace uses that format.

- [ ] **Step 5: Build and test the moved deploy solution**

Run:
```bash
dotnet build deploy/SpaceStroker.Deploy.sln
dotnet test deploy/SpaceStroker.Deploy.sln -v q
```
Expected: build succeeds; `BuildManifestTests`, `EnvNamingTests`, `StaticSiteCacheTests` and
`TeardownDecisionsTests` all PASS. `RepoPaths` composes `MetaRepo/deploy/terraform/...` and is
unaffected by the project's own depth — but if `MetaRepo` is derived from `AppContext.BaseDirectory`
by counting parent hops, the move changes that count by one. **Read `RepoPaths.cs` and confirm**; fix
the hop count if it is depth-based, and add a test asserting the resolved terraform path.

- [ ] **Step 6: Verify `deploy/` holds only the deploy app**

Run:
```bash
ls deploy
find deploy -name "*.csproj" -not -path "*/bin/*" -not -path "*/obj/*"
```
Expected: `deploy/` lists `SpaceStroker.Deploy`, `SpaceStroker.Deploy.Tests`,
`SpaceStroker.Deploy.sln`, `secrets.example.json`, `terraform`, `docs`, `README.md`, `.gitignore` —
and **no `tools`**. Exactly two `.csproj` files.

- [ ] **Step 7: Commit**

```bash
git add -A deploy
git commit -m "Leave SpaceStroker.Deploy alone in deploy/; drop the Seeder remnants."
git push origin feature/tools-refactor
```

---

### Task 8: Update the tooling docs

**Files:**
- Modify: `tools/README.md`
- Modify: `deploy/README.md` (Layout block ~line 14-22; `## DB console` ~line 249-252)
- Modify: `deploy/docs/FIRST-DEV-DEPLOY.human.md` (~line 259)
- Modify: `deploy/docs/FIRST-DEV-DEPLOY.agent.md` (~line 42)

**Interfaces:**
- Consumes: the final layout from Tasks 6–7.
- Produces: docs whose paths match the tree, so Task 12's audit passes.

- [ ] **Step 1: Update `tools/README.md`**

In the project table, add a fourth row and correct the intro:

```markdown
| `SpaceStroker.DbConsole` | `spacestroker-db` | Read-write REPL over the deployed Postgres databases, through the Cloud SQL Auth Proxy. |
```

Change the intro sentence "Standalone console tools for token-launch and treasury work" to cover the
DB console too, and replace the "Sibling of `monkify-meta/deploy/`" note with the current split:

```markdown
> Sibling of `monkify-meta/deploy/`, which now holds **only** the deploy/teardown app
> (`SpaceStroker.Deploy.sln`). A real-money withdrawal CLI does not belong in the deploy solution or
> in a repo built into container images.
```

In the **Configure** section, replace the `cp secrets.example.json secrets.json` block's surrounding
prose so it describes **one shared file for the whole solution**: shared `solana.rpcUrl` and
`wallets[]` at the top level, one named section per tool (`coinbase`/`travelRule`/`funding`, `snipe`,
`nightfall`), and the documented exception:

```markdown
> **`SpaceStroker.DbConsole` is the one exception** — it reads `deploy/secrets.<env>.json`, because
> Cloud SQL connection strings are environment-scoped and owned by the deploy tool. Duplicating them
> here would create drift and a second place to leak from.
```

Add a short **Receipts** section:

```markdown
## Receipts

Every tool writes its run state and reports into **`tools/Receipts/`**, which is git-ignored. Override
per run with `--receipts <path>` (and `--reports <path>` for the sniper).

⚠️ Run state that predates card #167 is still loose in `tools/`. `spacestroker-fund-wallets` **refuses
to start** if it finds `tools/receipts.json` with nothing at `tools/Receipts/receipts.json` — its
Coinbase idempotency tokens are derived from the plan, so a receipts file it cannot see reads as "no
prior run" and a re-run would issue real second transfers. Move the file, or pass `--receipts`.
```

Fix the `spacestroker-nightfall` references to `secrets.nightfall.json` / `secrets.nightfall.example.json`
so they name `tools/secrets.json` and its `nightfall` section.

- [ ] **Step 2: Update `deploy/README.md`**

Layout block — replace the `tools/` subtree with:

```
deploy/
  secrets.example.json         # template — copy to secrets.<env>.json (git-ignored) and fill in
  SpaceStroker.Deploy/         # `spacestroker-deploy deploy [api|consumers|front|lp]` + `teardown` + `validate`
  SpaceStroker.Deploy.Tests/
  SpaceStroker.Deploy.sln
  terraform/
    persistent/                # LAYER 1 — DNS zone + state bucket (production: prevent_destroy; dev: destroyable)
    workload/                  # LAYER 2 — everything teardown may destroy
      modules/{network,data,registry,iam,secrets,cloud_run_api,cloud_run_consumers,static_site,edge}
```

`## DB console` section — change the `cd` and add a pointer:

```bash
cd tools/SpaceStroker.DbConsole
dotnet run -- --db game --env development     # or: --db messaging
```

```markdown
> The DB console now lives in `monkify-meta/tools/` (card #167). It still reads
> `deploy/secrets.<env>.json` — that is a documented exception to the tools solution's shared
> `secrets.json`, because these connection strings are environment-scoped.
```

- [ ] **Step 3: Update both FIRST-DEV-DEPLOY docs**

`deploy/docs/FIRST-DEV-DEPLOY.human.md` ~line 259:

```bash
  cd monkify-meta/tools/SpaceStroker.DbConsole
  dotnet run -- --db game --env development      # or: --db messaging
```

`deploy/docs/FIRST-DEV-DEPLOY.agent.md` ~line 42 — keep the sentence, correct the location so it reads
`SpaceStroker.DbConsole` (now in `monkify-meta/tools/`).

- [ ] **Step 4: Verify every path named in the docs exists**

Run:
```bash
python - <<'PY'
import re, os
for doc in ['tools/README.md','deploy/README.md','deploy/docs/FIRST-DEV-DEPLOY.human.md','deploy/docs/FIRST-DEV-DEPLOY.agent.md']:
    for line in open(doc, encoding='utf-8'):
        for m in re.findall(r'(?:monkify-meta/)?((?:tools|deploy)/[A-Za-z0-9._/-]+)', line):
            p = m.rstrip('.,)')
            if ('.' in os.path.basename(p) or p.count('/')<2) and not os.path.exists(p):
                print(f'{doc}: MISSING {p}')
PY
```
Expected: no `MISSING` lines for real files. Ignore hits that are placeholders like
`secrets.<env>.json` or `tools/Receipts/...` (created at runtime).

- [ ] **Step 5: Commit**

```bash
git add tools/README.md deploy/README.md deploy/docs
git commit -m "Update the tooling docs for the new tools/ and deploy/ layout."
git push origin feature/tools-refactor
```

---

### Task 9: Write the layout down as a convention

**Files:**
- Modify: `conventions/backend.md` (append a new top-level section)
- Modify: `AGENTS.md` (extend **Quick context**)

**Interfaces:**
- Consumes: the final layout from Tasks 1–8.
- Produces: the standing rules a future agent reads before touching either solution.

- [ ] **Step 1: Append the section to `conventions/backend.md`**

Insert **before** the trailing `<!-- TODO: confirm naming conventions... -->` comment:

```markdown
## Meta-repo console tools (`monkify-meta/tools`, `monkify-meta/deploy`)

Neither solution is part of the deployed application. **Nothing here is built into a container image,
deployed to Cloud Run, or run by the local Docker stack.** They are built and run by hand, on purpose —
three of them move real money.

| Solution | Holds | Binary |
| --- | --- | --- |
| `tools/SpaceStroker.Tools.sln` | `SpaceStroker.WalletFunder` · `SpaceStroker.TokenSniper` · `SpaceStroker.Nightfall` · `SpaceStroker.DbConsole` | `spacestroker-fund-wallets` · `spacestroker-snipe-token` · `spacestroker-nightfall` · `spacestroker-db` |
| `deploy/SpaceStroker.Deploy.sln` | `SpaceStroker.Deploy` + its tests, next to the Terraform stack | `spacestroker-deploy` |

- **One shared, git-ignored `tools/secrets.json` for the whole tools solution.** Shared keys
  (`solana.rpcUrl`, the `wallets[]` pool) sit at the **top level**; each tool gets one **named
  section** (`coinbase`/`travelRule`/`funding`, `snipe`, `nightfall`). Every tool **ignores unknown
  fields** in a pool entry — that is what lets one array serve all three. The only tracked config is
  the placeholder-only `secrets.example.json`.
- **Documented exception: `SpaceStroker.DbConsole` reads `deploy/secrets.<env>.json`,** not the
  shared file. Cloud SQL connection strings are environment-scoped and owned by the deploy tool;
  copying production credentials into a second untracked file would create drift and a second place
  to leak from.
- **All run state, receipts and reports go to the git-ignored `tools/Receipts/`.** Never write run
  state to the solution root. ⚠️ Changing a receipts **default path** is a money-safety change for
  `spacestroker-fund-wallets`: its Coinbase idempotency tokens are derived from the plan, so a
  receipts file the tool cannot see reads as "no prior run" and a re-run issues genuinely new
  transfers. It refuses to start when a pre-refactor file is about to be missed
  (`Domain/LegacyReceipts.cs`).
- **The Domain / Infrastructure / Application split applies here too** — see "The folder tree must
  show the architecture" above. `Domain/` stays IO-free, which is what makes the money-safety rules
  unit-testable without a fake sender.
- **Secrets never reach output.** Nothing beyond public addresses may appear in console output,
  receipts or a report; a keyed RPC URL is masked to host-only wherever it is logged.
```

- [ ] **Step 2: Extend `AGENTS.md` Quick context**

Replace the existing `monkify-meta` bullet with:

```markdown
- **monkify-meta** — the knowledge base (this guidance's source of truth). It also holds two
  **standalone .NET console solutions that are never deployed**: `tools/SpaceStroker.Tools.sln`
  (wallet funder, token sniper, Nightfall, DB console — one shared git-ignored `tools/secrets.json`,
  all run state in `tools/Receipts/`) and `deploy/SpaceStroker.Deploy.sln` (the GCP deploy/teardown
  app, next to the Terraform stack, driven by `deploy/secrets.<env>.json`). See
  `conventions/backend.md` → "Meta-repo console tools". Three of these tools move real money — never
  run one with `--execute`.
```

- [ ] **Step 3: Verify both files render and reference real paths**

Run:
```bash
grep -n "Meta-repo console tools" conventions/backend.md AGENTS.md
ls tools/SpaceStroker.Tools.sln deploy/SpaceStroker.Deploy.sln tools/SpaceStroker.DbConsole/SpaceStroker.DbConsole.csproj
```
Expected: the heading is found in `conventions/backend.md`, the cross-reference in `AGENTS.md`, and
all three paths exist.

- [ ] **Step 4: Commit**

```bash
git add conventions/backend.md AGENTS.md
git commit -m "Document the meta-repo console tool layout in conventions and AGENTS."
git push origin feature/tools-refactor
```

---

### Task 10: Knowledge-base inventory — then STOP for approval

**Files:**
- Create: `docs/kb-reduction-inventory.md` (temporary review artifact, deleted in Task 12)

**Interfaces:**
- Consumes: nothing from Tasks 1–9.
- Produces: the approved delete/merge list Tasks 11–12 execute. **Nothing is deleted in this task.**

- [ ] **Step 1: Enumerate and measure**

Run:
```bash
python - <<'PY'
import os
for d in ('features','specs'):
    rows=[(os.path.getsize(os.path.join(d,f)),f) for f in sorted(os.listdir(d)) if f.endswith('.md')]
    print(f'== {d}: {len(rows)} files, {sum(s for s,_ in rows)/1024:.0f} KB')
    for s,f in rows: print(f'  {s/1024:7.1f} KB  {f}')
print(f"== INDEX.md: {os.path.getsize('INDEX.md')/1024:.0f} KB")
PY
```

- [ ] **Step 2: Classify every file**

Read enough of each doc to judge it — **do not classify from the filename alone.** Apply the criteria
in spec §9.2, in this order:

1. `DELETE` — the capability no longer exists (the operator/multi-tenant stack:
   `specs/operator-feature.md`, `operator-admin-page.md`, `operator-player-identity.md`,
   `operator-wallet-api.md`, `launch-token-validation.md`, `rg-pre-bet-hook.md`,
   `main-production-operator.md`, `remove-operators-backfill.md`).
2. `DELETE` — a one-off bug fix / QOL tweak that never warranted a standing doc
   (`features/dev-qol-stuff.md`; the small `specs/` entries such as `bet-choice-casing`,
   `fair-modal-off-screen`, `near-miss-dock-bg`, `home-hero-trim`, `robots-txt`,
   `idle-websocket-disconnect`).
3. `DELETE` — superseded by a later doc that fully replaces it (`specs/provably-fair.md` →
   `provably-fair-final.md`; `audio-overhaul{,-generation,-wiring}`; the `rebrand-*` chain; the
   `lp-*` chain; the `orbit-*` iterations).
4. `MERGE` — a `features/` doc that is near-duplicate prose of its `specs/` sibling, or one of several
   docs covering one domain.
5. `KEEP` — everything else.

⚠️ **`features/remove-operators.md` is `MERGE`, not `DELETE`.** It is the only doc that explains *why*
there are no operators and what replaced them (`GeneralSettings:Operator`, wallet identity, the single
ledger). Deleting it loses today's architecture, not a dead one.

Write `docs/kb-reduction-inventory.md` with one row per file:

```markdown
| File | Verdict | Domain | Reason |
| --- | --- | --- | --- |
| `features/dev-qol-stuff.md` | DELETE | — | One-off dev QOL tweaks; behaviour is just how the code reads now. |
| `features/remove-operators.md` | MERGE | `remove-operators.md` | Explains today's operator-free architecture — kept as its own doc. |
```

Follow it with the proposed `features/` target set (spec §9.2's table: `games`, `money`,
`provably-fair`, `player`, `orbit`, `frontend-ux`, `stats-and-history`, `embedding`, `infra`,
`token-ops`, `lp`, `remove-operators`), and a summary line: counts per verdict, and the projected
before/after file counts and byte totals.

- [ ] **Step 3: Sanity-check the inventory covers everything**

Run:
```bash
python - <<'PY'
import os,re
listed=set(re.findall(r'`((?:features|specs)/[^`]+\.md)`', open('docs/kb-reduction-inventory.md',encoding='utf-8').read()))
actual={f'{d}/{f}' for d in ('features','specs') for f in os.listdir(d) if f.endswith('.md')}
print('missing from inventory:', sorted(actual-listed) or 'none')
print('in inventory but not on disk:', sorted(listed-actual) or 'none')
PY
```
Expected: both lines print `none`. If files are missing, classify them before continuing.

- [ ] **Step 4: STOP — present the inventory and wait for approval**

**This is a hard gate. Do not proceed to Task 11 without an explicit approval.**

Present to the user: counts per verdict, the **full `DELETE` list**, the proposed `features/` target
set, and the projected size reduction. Ask them to approve, amend, or reject. Deleting ~100
knowledge-base files is their call, not the criteria's — the criteria produce the proposal.

- [ ] **Step 5: Commit the inventory only**

```bash
git add docs/kb-reduction-inventory.md
git commit -m "Add the knowledge-base reduction inventory for review."
git push origin feature/tools-refactor
```

---

### Task 11: Execute the approved knowledge-base sweep

**Files:**
- Create/Modify: the `features/*.md` domain docs from the approved target set
- Delete: every `features/*.md` and `specs/*.md` marked `DELETE` **in the approved inventory**
- Modify: any doc containing an inbound link to a removed file

**Interfaces:**
- Consumes: the **approved** `docs/kb-reduction-inventory.md` from Task 10.
- Produces: a `features/`+`specs/` tree matching the approved inventory, with no dangling inbound
  links. `INDEX.md` is **not** touched here — that is Task 12.

- [ ] **Step 1: Write the merged domain docs**

For each target doc, open with a one-paragraph "what this covers", then keep one subsection per
behaviour. **Merging must not turn twelve precise docs into one vague one** — where a merged doc drops
detail, link the surviving `specs/` file for it. Preserve the ⚠️ callouts: they are the
hard-won gotchas, and they are the highest-value lines in the knowledge base.

- [ ] **Step 2: Delete the approved files**

```bash
git rm features/<each-approved>.md specs/<each-approved>.md
```

Only files the user approved in Task 10. If the inventory and the approval disagree, follow the
approval and note the difference.

- [ ] **Step 3: Repoint every inbound reference**

Run, for each removed filename, and fix each hit — repoint it to the merged doc or drop the sentence:
```bash
python - <<'PY'
import os,re,subprocess
removed=[l[3:].strip() for l in subprocess.run(['git','status','--short'],capture_output=True,text=True).stdout.splitlines() if l.startswith(' D ') or l.startswith('D ')]
targets=['architecture.md','glossary.md','constraints.md','AGENTS.md','INDEX.md']+ \
        [f'{d}/{f}' for d in ('features','specs','conventions') if os.path.isdir(d) for f in os.listdir(d) if f.endswith('.md')]
for t in targets:
    if not os.path.exists(t): continue
    for i,line in enumerate(open(t,encoding='utf-8',errors='replace'),1):   # line-by-line: CRLF breaks offset scans
        for r in removed:
            if os.path.basename(r) in line: print(f'{t}:{i}: references {os.path.basename(r)}')
PY
```
Expected after fixing: no output **except** hits in `INDEX.md` (Task 12) and in
`docs/superpowers/plans/` (out of scope — historical plans).

- [ ] **Step 4: Commit**

```bash
git add -A features specs architecture.md glossary.md constraints.md AGENTS.md conventions
git commit -m "Consolidate the knowledge base into domain docs and delete superseded ones."
git push origin feature/tools-refactor
```

---

### Task 12: Repoint `INDEX.md`, audit the links, drop the inventory

**Files:**
- Modify: `INDEX.md` (**map table rows only**)
- Delete: `docs/kb-reduction-inventory.md`

**Interfaces:**
- Consumes: the executed sweep from Task 11.
- Produces: an `INDEX.md` with zero dead links, its structure and its "Feature currently in flight"
  section unchanged.

- [ ] **Step 1: Update the map table**

Remove the rows for deleted docs; repoint rows for merged ones; add one row per new domain doc in the
existing style (backtick-quoted path · description · status).

⚠️ **Only the "Map of this knowledge base" table changes.** Every other section — and in particular
**"Feature currently in flight"** — stays **byte-identical**. That is decision 3 on the card: the
~210 KB of shipped history is knowingly kept.

- [ ] **Step 2: Audit every link in the repo's Markdown**

Run:
```bash
python - <<'PY'
import os,re
bad=0
for root,dirs,files in os.walk('.'):
    dirs[:] = [d for d in dirs if d not in {'.git','bin','obj','node_modules','.terraform'}]
    for f in files:
        if not f.endswith('.md'): continue
        p=os.path.join(root,f)
        for i,line in enumerate(open(p,encoding='utf-8',errors='replace'),1):  # line-by-line: CRLF
            for link in re.findall(r'\]\(([^)#]+\.md)(?:#[^)]*)?\)', line):
                if link.startswith(('http://','https://')): continue
                if not os.path.exists(os.path.normpath(os.path.join(root,link))):
                    print(f'DEAD {p}:{i} -> {link}'); bad+=1
print('dead links:',bad)
PY
```
Expected: `dead links: 0`. Fix any hit and re-run.

- [ ] **Step 3: Verify the in-flight section is untouched**

Run:
```bash
python - <<'PY'
import subprocess
def section(rev):
    txt = subprocess.run(['git','show',f'{rev}:INDEX.md'],capture_output=True,text=True).stdout if rev else open('INDEX.md',encoding='utf-8').read()
    marker='## Feature currently in flight'
    i=txt.index(marker); j=txt.find('\n## ', i+len(marker))
    return txt[i: j if j>0 else len(txt)]
print('in-flight section identical to the spec commit:', section('0cf85dd')==section(None))
PY
```
Expected: `True`. `0cf85dd` is the commit that added the spec, i.e. before any sweep.

- [ ] **Step 4: Report the size reduction**

Run:
```bash
python - <<'PY'
import os
for d in ('features','specs'):
    n=[f for f in os.listdir(d) if f.endswith('.md')]
    print(f'{d}: {len(n)} files, {sum(os.path.getsize(os.path.join(d,f)) for f in n)/1024:.0f} KB')
print(f"INDEX.md: {os.path.getsize('INDEX.md')/1024:.0f} KB")
PY
```
Compare against Task 10 Step 1 (before: `features` 65 files / 824 KB, `specs` 117 files / 4.6 MB,
`INDEX.md` 522 KB). Record the deltas for the final report — the card's whole point is this number
going down.

- [ ] **Step 5: Delete the review artifact and commit**

```bash
git rm docs/kb-reduction-inventory.md
git add INDEX.md
git commit -m "Repoint the INDEX map at the consolidated knowledge base."
git push origin feature/tools-refactor
```

---

### Task 13: Full verification

**Files:** none (verification only).

**Interfaces:**
- Consumes: everything.
- Produces: the evidence for the final report.

- [ ] **Step 1: Build and test both solutions**

Run:
```bash
dotnet build tools/SpaceStroker.Tools.sln
dotnet build deploy/SpaceStroker.Deploy.sln
dotnet test tools/SpaceStroker.Tools.sln -v q
dotnet test deploy/SpaceStroker.Deploy.sln -v q
```
Expected: both build clean; every test PASSES. Record the counts — the tools solution had **191**
tests before this card, plus the ~12 added in Tasks 2, 4 and 5.

- [ ] **Step 2: Exercise the CLI surfaces (read-only — never `--execute`)**

Run:
```bash
dotnet run --project tools/SpaceStroker.Nightfall -- --help
dotnet run --project tools/SpaceStroker.WalletFunder -- --help
dotnet run --project tools/SpaceStroker.TokenSniper -- --help
dotnet run --project tools/SpaceStroker.DbConsole -- --help
```
Expected: each prints usage and exits 0; the Nightfall `--secrets` line reads `tools/secrets.json` and
its `--receipts` line reads `tools/Receipts/nightfall-receipts.json`.

- [ ] **Step 3: Prove the new secrets shape binds, using the TEMPLATE only**

⚠️ **Never touch the operator's real `tools/secrets.json`.** Work in a temp copy:

```bash
cp tools/secrets.example.json /tmp/ss-secrets.json
dotnet run --project tools/SpaceStroker.Nightfall -- --secrets /tmp/ss-secrets.json --no-open
```
Expected: exits non-zero with `nightfall.token.mint is still a placeholder` — proving the section binds
and the message carries the new path.

```bash
python -c "
import json;d=json.load(open('/tmp/ss-secrets.json'));d.pop('nightfall');json.dump(d,open('/tmp/ss-nonf.json','w'))"
dotnet run --project tools/SpaceStroker.Nightfall -- --secrets /tmp/ss-nonf.json --no-open
```
Expected: exits non-zero naming the **missing `nightfall` section** and `secrets.example.json`.

```bash
rm -f /tmp/ss-secrets.json /tmp/ss-nonf.json
```

- [ ] **Step 4: Confirm the working tree is clean and pushed**

Run:
```bash
git status --short
git log --oneline origin/main..feature/tools-refactor
git rev-parse HEAD; git ls-remote --heads origin feature/tools-refactor
```
Expected: no unexpected modified/untracked files; the commit list matches the tasks; local `HEAD`
equals the remote SHA.

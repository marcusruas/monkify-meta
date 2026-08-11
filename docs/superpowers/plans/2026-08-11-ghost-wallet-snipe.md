# Ghost wallet snipe app — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `spacestroker-snipe-token`, a console app that drives a pool of wallets we control to a configured share of our own pump.fun token's supply — one buy per wallet, alias order, jittered across a one-hour window — then reports achieved-vs-target.

**Architecture:** A second project in the existing `monkify-meta/tools/SpaceStroker.Tools.sln`, split `Domain/` (IO-free: curve math, targets, schedule, preflight, ledger) · `Infrastructure/` (Solana RPC, pump.fun instruction building, secrets, receipts, reports) · `Application/` (the run, CLI, console output). Every money rule lives in `Domain/` so it is unit-testable with no network — the same reason the sibling `SpaceStroker.WalletFunder` is shaped this way.

**Tech Stack:** .NET 8 console app, Solnet.Rpc / Solnet.Wallet / Solnet.Programs 6.1.0, xUnit + Shouldly.

**Spec:** [`specs/ghost-wallet-snipe.md`](../../../specs/ghost-wallet-snipe.md) · Trello [#63](https://trello.com/c/nokWHm7z)

## Global Constraints

- **Layer rule:** `Domain/` has no IO, no HTTP, no filesystem, no clock. External values (balances, supply, reserves, time) are **passed in**. Namespaces match folders. Tests mirror the folders.
- **Money gates:** dry run is the default; `--execute` **plus** a typed confirmation of the quoted total SOL; preflight aborts before buy #1 and reports **all** problems at once.
- **Resume truth is on-chain:** never re-buy supply a wallet already holds; the receipts file is an audit trail, never the authority.
- **Secrets:** `tools/secrets.json` is git-ignored and holds live private keys. Only `secrets.example.json` is tracked, placeholders only. No key, keyed RPC URL, or secrets value beyond public addresses may reach console output, receipts or reports.
- **Every buy carries `maxSolCost`.** The instruction cannot be built without it.
- **Solnet pinned at 6.1.0** for all three packages (matches `monkify-backend`). Do **not** add `BouncyCastle.Cryptography` — it collides with Solnet's transitive `Portable.BouncyCastle` 1.9.0 (CS0433).

### Verified protocol facts (from mainnet on 2026-08-11 — do not "correct" these from memory or blog posts)

Source: the program's own on-chain Anchor IDL (`AYgC53tU5BbP2NAnv5nConJxAdpQZctvmZK88pu69xRs`) plus decoded live `buy` transactions.

- **Program:** `6EF8rrecthR5Dkzon8Nwu78hRvfCKubJ14M5uBEwF6P` · **fee program:** `pfeeUxB6jkeY1Hxd7CsFCAjcbHA9rWtchMGdZ6VojVZ`
- **`buy` discriminator:** `SHA256("global:buy")[0..8]` = `66063d1201daebea` (compute it, never paste it)
- **`buy` args:** `amount: u64`, `max_sol_cost: u64`, `track_volume: OptionBool`. Live traffic shows **three** encodings all landing: 24 bytes (arg omitted, most common), 25 bytes (`00`), 26 bytes (`0101`). We omit it by default and expose a knob.
- **`buy` accounts (IDL order, 16):** `global` PDA`["global"]` · `fee_recipient` (w) · `mint` · `bonding_curve` PDA`["bonding-curve", mint]` (w) · `associated_bonding_curve` = ATA(curve, mint, token program) (w) · `associated_user` = ATA(user, mint, token program) (w) · `user` (signer, w) · `system_program` · `token_program` · `creator_vault` PDA`["creator-vault", curve.creator]` (w) · `event_authority` PDA`["__event_authority"]` · `program` · `global_volume_accumulator` PDA`["global_volume_accumulator"]` · `user_volume_accumulator` PDA`["user_volume_accumulator", user]` (w) · `fee_config` PDA`["fee_config", <program>]` under the **fee program** · `fee_program`
- ⚠️ **Live buys carry 18 accounts** — two unnamed trailing remaining-accounts the IDL does not declare (one owned by the fee program, one not yet created). We build the 16 the IDL declares and expose `snipe.extraAccounts` for forward-compatibility. Failure mode if the program later requires them is a **reverted transaction, not lost SOL**.
- **`token_program` varies per mint** — live buys use both SPL Token (`Tokenkeg…`) and Token-2022 (`TokenzQd…`). Read the mint account's **owner** and use it; the ATA derivation depends on it.
- **`BondingCurve`** (disc `[23,183,248,55,96,216,172,96]`): `virtual_token_reserves u64`, `virtual_quote_reserves u64`, `real_token_reserves u64`, `real_quote_reserves u64`, `token_total_supply u64`, `complete bool`, `creator pubkey`, `is_mayhem_mode bool`, `is_cashback_coin bool`, `quote_mint pubkey`. ⚠️ A curve can be quoted in a **non-SOL** mint; `quote_mint == 11111111111111111111111111111111` means SOL. We refuse anything else.
- **`Global`** (disc `[167,232,232,177,200,108,114,127]`) supplies the fees: `fee_basis_points` (**95** today) and `creator_fee_basis_points` (**5**) — read at runtime, never hardcoded — plus `fee_recipients[7]` and the initial curve constants (`30 SOL` virtual, `793.1M` real tokens).

---

### Task 1: Project scaffolding

**Files:**
- Create: `tools/SpaceStroker.TokenSniper/SpaceStroker.TokenSniper.csproj`, `tools/SpaceStroker.TokenSniper.Tests/SpaceStroker.TokenSniper.Tests.csproj`
- Modify: `tools/SpaceStroker.Tools.sln`, `tools/.gitignore`, `tools/secrets.example.json`

**Interfaces:**
- Produces: an `AssemblyName` of `spacestroker-snipe-token`; both projects in the solution.

- [ ] **Step 1:** Copy `SpaceStroker.WalletFunder.csproj`'s shape; swap `RootNamespace`/`AssemblyName`; reference `Solnet.Rpc`, `Solnet.Wallet`, `Solnet.Programs` 6.1.0 (no BouncyCastle).
- [ ] **Step 2:** Copy the funder's test csproj; repoint the `ProjectReference`.
- [ ] **Step 3:** `dotnet sln tools/SpaceStroker.Tools.sln add` both projects.
- [ ] **Step 4:** ⚠️ Append to `tools/.gitignore` — the existing globs cover **neither** artifact:

```gitignore
# Run reports — contain wallet addresses, holdings and signatures.
reports/

# The sniper's run state (does NOT match the anchored receipts*.json glob above).
snipe-receipts*.json
```

- [ ] **Step 5:** Add the `snipe` section + `alias`/`PrivateKey`/`SupplyPercentage` placeholders to `secrets.example.json`.
- [ ] **Step 6:** `dotnet build tools/SpaceStroker.Tools.sln` → succeeds. Commit.

---

### Task 2: `Domain/SnipePool.cs` — pool parsing

**Files:** Create `…/Domain/SnipePool.cs`; Test `…Tests/Domain/SnipePoolTests.cs`

**Interfaces:**
- Produces: `PoolEntry(string Alias, string PublicKey, string PrivateKey, decimal SupplyPercentage)`, `RawPoolEntry`, `SnipePool.Parse(string json)`, `SnipePool.FromEntries(IEnumerable<RawPoolEntry>)`, `SnipePool.JsonOpts`.

- [ ] **Step 1: Write the failing tests** — an entry carrying the funder's `Label`/`SOLAmount` parses (unknown fields ignored); a blank `alias` is preserved as blank (**not** synthesised — that is the funder's behavior and would silently reorder the run); malformed JSON throws `SniperException`.
- [ ] **Step 2:** Run → fails (type missing).
- [ ] **Step 3:** Implement with `JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = Skip, AllowTrailingCommas = true }` and a comment stating why `UnmappedMemberHandling` must stay default.
- [ ] **Step 4:** Run → passes. **Step 5:** Commit.

---

### Task 3: `Domain/BondingCurve.cs` — pricing

**Files:** Create `…/Domain/BondingCurve.cs`; Test `…Tests/Domain/BondingCurveTests.cs`

**Interfaces:**
- Produces: `CurveState` record (fields per Global Constraints), `CurveFees(ulong FeeBasisPoints, ulong CreatorFeeBasisPoints)`, and on `BondingCurve`: `IsGraduated(CurveState)`, `CostForTokens(CurveState, CurveFees, ulong tokens)`, `TokensForBudget(CurveState, CurveFees, ulong lamports)`, `AfterBuy(CurveState, ulong tokens, ulong solIn)`.

- [ ] **Step 1: Write the failing tests:**

```csharp
[Fact] public void CostForTokens_UsesConstantProductOverVirtualReserves()
{
    var c = Curve(virtualTokens: 1_073_000_000_000_000, virtualQuote: 30_000_000_000,
                  realTokens: 793_100_000_000_000);
    // cost = ceil(k / (vT - t)) - vQ, then + ceil(1% of that), both rounded UP.
    // Derive the expectation by hand for the numbers you pick — do not copy a literal from this plan.
    var solIn = BondingCurve.SolInForTokens(c, 10_730_000_000_000);
    BondingCurve.CostForTokens(c, new CurveFees(95, 5), 10_730_000_000_000)
        .ShouldBe(solIn + BondingCurve.FeeOn(solIn, new CurveFees(95, 5)));
}
[Fact] public void TokensForBudget_IsTheInverseOfCostForTokens() { /* round-trip within 1 base unit */ }
[Fact] public void TokensForBudget_NeverExceedsRealTokenReserves() { /* the curve cannot sell what it does not hold */ }
[Fact] public void IsGraduated_WhenCompleteOrNoRealTokensLeft() { /* both forms */ }
```

- [ ] **Step 2:** Run → fails. 
- [ ] **Step 3: Implement.** All integer math on `UInt128`, never `double`:

```csharp
public static ulong CostForTokens(CurveState c, CurveFees f, ulong tokens)
{
    if (tokens == 0) return 0;
    if (tokens > c.RealTokenReserves) throw new SniperException("The curve cannot sell that many tokens.");
    UInt128 k = (UInt128)c.VirtualTokenReserves * c.VirtualQuoteReserves;
    UInt128 newQuote = CeilDiv(k, (UInt128)c.VirtualTokenReserves - tokens);
    var solIn = (ulong)(newQuote - c.VirtualQuoteReserves);
    return solIn + Fees(solIn, f);                       // fees ride ON TOP, rounded up
}
```

Round **cost up** and **tokens down** — the operator is never quoted less than they pay.
- [ ] **Step 4:** Run → passes. **Step 5:** Commit.

---

### Task 4: `Domain/SnipePlan.cs` — targets, shortfall, affordability

**Files:** Create `…/Domain/SnipePlan.cs`; Test `…Tests/Domain/SnipePlanTests.cs`

**Interfaces:**
- Produces: `WalletTarget(string Alias, string PublicKey, decimal SupplyPercentage, ulong TargetBaseUnits, ulong HeldBaseUnits, ulong ShortfallBaseUnits)`, `PlannedBuy(WalletTarget Target, ulong TokensToBuy, ulong QuotedCostLamports, ulong MaxSolCostLamports, bool CappedByBalance)`, `SnipePlan.TargetFor(decimal percent, ulong totalSupply)`, `SnipePlan.Build(...)`.

- [ ] **Step 1: Write the failing tests** — `0.4504` ⇒ 0.4504% of supply (**not** 45.04%); zero shortfall when already at target; a wallet that cannot afford its shortfall buys the inverse-priced maximum and is flagged `CappedByBalance`; `maxSolCost = ceil(quote × (1 + tolerance))` capped at spendable; the ladder is quoted **sequentially** so later wallets cost more than the first.
- [ ] **Step 2:** Run → fails. **Step 3:** Implement; `Build` takes the curve, fees, tolerance, per-wallet held/SOL balances and the ATA-rent flag **as values**. **Step 4:** Run → passes. **Step 5:** Commit.

---

### Task 5: `Domain/BuySchedule.cs`

**Files:** Create `…/Domain/BuySchedule.cs`; Test `…Tests/Domain/BuyScheduleTests.cs`

**Interfaces:** Produces `ScheduledBuy(PlannedBuy Buy, TimeSpan Offset)`, `BuySchedule.Build(IReadOnlyList<PlannedBuy>, TimeSpan window, Random rng)`.

- [ ] **Step 1: Failing tests** — order is **alias-ascending** for shuffled input (⚠️ unlike the funder's `SendSchedule`, which shuffles); offsets ascend; `window == 0` ⇒ all zero; a seeded `Random` reproduces.
- [ ] **Step 2:** fails → **Step 3:** implement (sort `StringComparer.Ordinal`, n random offsets sorted ascending) → **Step 4:** passes → **Step 5:** commit.

---

### Task 6: `Domain/SnipePreflight.cs` + `Domain/SniperException.cs` + `Domain/RunLedger.cs`

**Files:** Create all three; Tests `…Tests/Domain/SnipePreflightTests.cs`, `RunLedgerTests.cs`

**Interfaces:** Produces `SniperException`, `UsageException`, `PreflightResult(bool IsEmpty, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)`, `SnipePreflight.Check(...)`, `WalletStatus` enum (`Pending/Filled/Short/Skipped/Failed/Unconfirmed`), `WalletOutcome`, `RunLedger` with `ExitCode`.

- [ ] **Step 1: Failing tests** — blank/duplicate alias, invalid + non-32-byte address, mismatched keypair, non-positive percent, targets exceeding sellable supply, graduated curve, non-SOL `quote_mint`, empty pool (⇒ `IsEmpty`, not an error), padded/unpadded alias mix (⇒ **warning**). **All errors returned together.** Exit code is 0 only when every targeted wallet filled / dry run / empty pool.
- [ ] **Step 2:** fails → **Step 3:** implement (address validation = `PublicKey.IsValid` **plus** a 32-byte decode; keypair check derives the public key from the secret) → **Step 4:** passes → **Step 5:** commit.

---

### Task 7: `Infrastructure/SecretsFile.cs` + `Infrastructure/ReceiptsStore.cs`

**Files:** Create both; Tests `…Tests/Infrastructure/SecretsFileTests.cs`, `ReceiptsStoreTests.cs`

**Interfaces:** Produces `SecretsFile.Load(path)` → `Secrets(SolanaSection Solana, SnipeSection Snipe, IReadOnlyList<RawPoolEntry> Wallets)`; `ReceiptsStore.Load/Save`.

- [ ] **Step 1: Failing tests** — `REPLACE_ME` placeholders rejected with an actionable message; a missing `snipe` section fails fast; the funder's fields coexist; receipts survive a partial run and a corrupt file is reported, not swallowed.
- [ ] **Step 2:** fails → **Step 3:** implement → **Step 4:** passes → **Step 5:** commit.

---

### Task 8: `Infrastructure/Solana/PumpFunProgram.cs` — the instruction

**Files:** Create `…/Infrastructure/Solana/PumpFunProgram.cs`; Test `…Tests/Infrastructure/PumpFunProgramTests.cs`

**Interfaces:** Produces `PumpFunProgram.ProgramId/FeeProgramId/EventAuthority/…`, `DeriveBondingCurve(mint)`, `DeriveCreatorVault(creator)`, `DeriveGlobal()`, `DeriveGlobalVolumeAccumulator()`, `DeriveUserVolumeAccumulator(user)`, `DeriveFeeConfig()`, `BuildBuyInstruction(BuyAccounts, ulong amount, ulong maxSolCost, bool? trackVolume, IEnumerable<PublicKey> extra)`.

- [ ] **Step 1: Write the failing tests against the recorded live fixture** (this is what keeps the account list honest):

```csharp
[Fact] public void Discriminator_IsSha256OfGlobalBuy()
    => PumpFunProgram.BuyDiscriminator.ShouldBe(Convert.FromHexString("66063d1201daebea"));

[Fact] public void BuildBuyInstruction_MatchesTheAccountOrderOfALiveMainnetBuy()
{
    // fixture: sig 2kHXxyAqoiZfy3ucR8TaM7PSPms2HuBpHTagiAWBmCSCDiZ1F6zfrgZAMNLwTHJ85FuKnFxN1NtFGPYkNnYSLjLo
    var ix = PumpFunProgram.BuildBuyInstruction(accounts, amount: 7_404_182_659_944, maxSolCost: 540_000_000, trackVolume: null, extra: []);
    ix.Keys.Select(k => k.PublicKey).Take(16).ShouldBe(LiveFixtureAccounts.Take(16));
    ix.Data.ShouldBe(Convert.FromHexString("66063d1201daebea" + "68b2d1e3bb06" + …));  // disc ‖ u64 ‖ u64
}
```

- [ ] **Step 2:** fails → **Step 3:** implement the 16 IDL accounts in order, with the derivations from Global Constraints; `trackVolume` null ⇒ omit the byte, `false` ⇒ `00`, `true` ⇒ `0101`; append `extra` after the 16 → **Step 4:** passes → **Step 5:** commit.

---

### Task 9: `Infrastructure/Solana/CurveAccountReader.cs` + `SolanaGateway.cs`

**Files:** Create both; Test `…Tests/Infrastructure/CurveAccountReaderTests.cs`

**Interfaces:** Produces `CurveAccountReader.Decode(byte[])` → `CurveState`, `GlobalAccountReader.Decode(byte[])` → `CurveFees`; `SolanaGateway` (mint supply + owner program, SOL/token balances, account fetch, blockhash, submit, confirm).

- [ ] **Step 1: Failing test** — decode a **recorded real account dump** (base64 of the live curve read on 2026-08-11) and assert the exact field values, including `complete == false`, `is_mayhem_mode == true`, `quote_mint == 11111111111111111111111111111111`; a wrong discriminator throws.
- [ ] **Step 2:** fails → **Step 3:** implement (offsets straight from the Global Constraints layout) → **Step 4:** passes → **Step 5:** commit.

---

### Task 10: `Infrastructure/Reports/` — JSON + HTML

**Files:** Create `JsonReportWriter.cs`, `HtmlReportWriter.cs`; Tests for both.

**Interfaces:** Produces `JsonReportWriter.Write(RunReport, dir)`, `HtmlReportWriter.Write(RunReport, dir)`, both returning the written path.

- [ ] **Step 1: Failing tests** — every per-wallet field and every run total present in each artifact; short/failed/skipped wallets appear with their reason; **no private key and no keyed RPC URL anywhere in either output**; the HTML references no `http(s)://` asset (self-contained; Solscan links are anchors, not assets).
- [ ] **Step 2:** fails → **Step 3:** implement (two independent writers — the card chose this over rendering one from the other) → **Step 4:** passes → **Step 5:** commit.

---

### Task 11: `Application/` — CLI, console report, the run

**Files:** Create `CliOptions.cs`, `ConsoleReport.cs`, `Sniper.cs`, `Program.cs`; Test `…Tests/Application/CliOptionsTests.cs`, `ConsoleReportTests.cs`

**Interfaces:** Produces the `snipe`/`status` commands and options from spec §5; `Sniper.RunAsync` → exit code.

- [ ] **Step 1: Failing tests** — unknown option ⇒ `UsageException` (exit 2); `--wallet` repeats accumulate; the typed-total prompt rejects a comma decimal under a `pt-BR` culture (⚠️ the funder's invariant-formatting finding); the ladder table prints target/held/shortfall/quote/maxSolCost/offset.
- [ ] **Step 2:** fails → **Step 3:** implement; `Sniper` orchestrates load → curve/fees/balances → plan → preflight → confirm → schedule → per-wallet buy (re-quote against current reserves, submit, confirm, **require a token-balance delta**) → receipts after every state change → reports → exit code. On a `Failed`/`Unconfirmed` wallet, continue. On graduation mid-run, stop and record the remainder as skipped → **Step 4:** passes → **Step 5:** commit.

---

### Task 12: Docs

**Files:** Modify `tools/README.md`; verify `specs/ghost-wallet-snipe.md` §12 records the live findings.

- [ ] **Step 1:** Add the sniper's README section: preconditions, the dry-run-first workflow, every option, exit codes, and the ⚠️ protocol-drift note (16 IDL accounts vs 18 live; `track_volume`; non-SOL quote curves).
- [ ] **Step 2:** `dotnet test tools/SpaceStroker.Tools.sln` → all green. Commit.

---

## Self-review

- **Spec coverage:** §4.1 layout → Task 1; §6.1 → 2; §6.2 → 3, 9; §6.3 → 4, 6; §6.4 → 8; §6.5 → 5; §6.6 → 11; §6.7 → 10; §6.8 → 11; §5 CLI → 11; §11 unit tests → every task; §12 findings → Task 12 + Global Constraints.
- **Spec corrections this plan carries** (verified on-chain, superseding the spec's assumptions): curve field names are `*_quote_reserves` not `*_sol_reserves`; the curve has `creator`, `is_mayhem_mode`, `is_cashback_coin`, `quote_mint`; fees come from the `global` account (95 + 5 bps) rather than being folded into a constant; `buy` has a third arg; the token program varies per mint; live buys carry two undeclared trailing accounts.
- **Type consistency:** `CurveState`, `CurveFees`, `PoolEntry`, `WalletTarget`, `PlannedBuy`, `ScheduledBuy`, `WalletOutcome`, `RunReport` are defined once and used with the same names throughout.

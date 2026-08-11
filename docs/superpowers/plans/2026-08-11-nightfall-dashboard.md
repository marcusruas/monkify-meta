# Nightfall Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `spacestroker-nightfall` — a localhost-only dashboard that shows a Solana wallet pool's SOL and token positions and sells them, one wallet or many concurrently, behind a confirmation.

**Architecture:** A `Microsoft.NET.Sdk.Web` app in `monkify-meta/tools/`, split Domain / Infrastructure / Application per `conventions/backend.md`. `Domain/` is IO-free so every money rule is unit-testable with plain values. The host holds the private keys and does all quoting, signing and broadcasting in-process; the browser gets a static `wwwroot/` page and public-data JSON only.

**Tech Stack:** .NET 8 · Solnet.Rpc/Wallet/Programs 6.1.0 · Portable.BouncyCastle 1.9.0 (pinned) · xUnit 2.4.2 + Shouldly 4.2.1 · Jupiter Swap API · plain HTML/CSS/JS (no build step).

## Global Constraints

- **Target framework `net8.0`.** `InvariantGlobalization=true`, `Nullable=enable`, `ImplicitUsings=enable`.
- **`Portable.BouncyCastle` 1.9.0 — NOT `BouncyCastle.Cryptography` 2.4.0.** Solnet drags 1.9.0 in transitively; both on the graph makes every `Ed25519*` type ambiguous (CS0433).
- **`Domain/` is IO-free.** No `HttpClient`, no `File`, no `IRpcClient`. Values in, values out.
- **Never route a money value through a `double`.** Decimal loops for scaling, `CultureInfo.InvariantCulture` for formatting.
- **A private key never leaves the process.** Not in a log, not in a receipt, not in an HTTP response, not in an exception message. HTTP DTOs have no `PrivateKey` member.
- **The host binds `127.0.0.1` only.**
- **Mock/placeholder values only** in anything tracked (`REPLACE_ME`).
- **Solnet 6.1.0 has no versioned-transaction support** — confirmed by inspecting the package. Use the spec's documented fallback (Task 9).
- Assembly name `spacestroker-nightfall`; root namespace `SpaceStroker.Nightfall`.

---

### Task 1: Scaffolding — projects, solution, `.gitignore`, secrets template

**Files:**
- Modify: `tools/.gitignore`
- Modify: `tools/SpaceStroker.Tools.sln`
- Create: `tools/secrets.nightfall.example.json`
- Create: `tools/SpaceStroker.Nightfall/SpaceStroker.Nightfall.csproj`
- Create: `tools/SpaceStroker.Nightfall.Tests/SpaceStroker.Nightfall.Tests.csproj`

**Interfaces:**
- Consumes: nothing.
- Produces: two buildable projects on the solution; `tools/secrets.nightfall.example.json` as the tracked template.

- [ ] **Step 1: Fix `tools/.gitignore` (both gaps are confirmed, spec §4.4)**

Append:

```gitignore
# Nightfall (#166) — its receipts file does not start with "receipts", so the rule above misses it.
*receipts*.json

# ...and the tracked Nightfall template must survive the secrets*.json rule above.
!secrets.nightfall.example.json
```

- [ ] **Step 2: Verify the ignore rules**

Run from `tools/`:
```bash
for f in secrets.nightfall.json secrets.nightfall.example.json nightfall-receipts.json; do
  git check-ignore -q "$f" && echo "$f IGNORED" || echo "$f TRACKED"; done
```
Expected: `IGNORED`, `TRACKED`, `IGNORED`.

- [ ] **Step 3: Write both csproj files** (exact contents in spec §4.2; the app uses `Microsoft.NET.Sdk.Web`, the test project copies `SpaceStroker.WalletFunder.Tests.csproj` with the reference repointed).

- [ ] **Step 4: Add both to the solution**

```bash
dotnet sln tools/SpaceStroker.Tools.sln add tools/SpaceStroker.Nightfall/SpaceStroker.Nightfall.csproj tools/SpaceStroker.Nightfall.Tests/SpaceStroker.Nightfall.Tests.csproj
```

- [ ] **Step 5: Write `secrets.nightfall.example.json`** exactly as spec §4.3, `REPLACE_ME` placeholders only.

- [ ] **Step 6: Build** — `dotnet build tools/SpaceStroker.Tools.sln`. Expected: succeeds (both new projects empty).

---

### Task 2: `Domain/WalletPool.cs` + `Domain/TokenAmount.cs` + `Domain/NightfallException.cs`

**Files:**
- Create: `tools/SpaceStroker.Nightfall/Domain/NightfallException.cs`, `Domain/TokenAmount.cs`, `Domain/WalletPool.cs`
- Test: `tools/SpaceStroker.Nightfall.Tests/Domain/WalletPoolTests.cs`

**Interfaces:**
- Produces:
  - `NightfallException(string)`, `UsageException(string)`
  - `PoolEntry(string Alias, string PublicKey, string PrivateKey)` — record
  - `RawPoolEntry { Alias, PublicKey, PrivateKey }` — JSON binding class
  - `WalletPool.JsonOpts` — `JsonSerializerOptions` (case-insensitive, comments skipped, trailing commas, unknown members ignored)
  - `WalletPool.FromEntries(IEnumerable<RawPoolEntry>) : IReadOnlyList<PoolEntry>`
  - `WalletPool.ValidateAddress(string) : void` — throws on invalid/non-32-byte
  - `TokenAmount.ToUi(ulong raw, int decimals) : decimal`
  - `TokenAmount.FromPercent(ulong raw, decimal? percent) : ulong` *(moves to SellPlan in Task 3 — define it there, not here)*
  - `TokenAmount.Format(decimal) : string` — invariant `0.#########`
  - `TokenAmount.LamportsToSol(long) : decimal`

- [ ] **Step 1: Failing tests** — unknown fields ignored; blank alias defaults to `W01`; address trimmed; invalid base58 rejected; 31-byte address rejected; `ToUi(12345678900, 6) == 12345.6789m`; `Format` invariant.
- [ ] **Step 2:** `dotnet test tools/SpaceStroker.Tools.sln --filter WalletPoolTests` → FAIL (types missing).
- [ ] **Step 3:** Implement, copying `SpaceStroker.WalletFunder/Domain/WalletPool.cs`'s shape and `SolanaService.ToUiAmount`'s decimal loop.
- [ ] **Step 4:** Re-run → PASS.
- [ ] **Step 5:** Commit `Add the Nightfall wallet pool and token amount maths.`

---

### Task 3: `Domain/SellPlan.cs` + `Domain/SellPreflight.cs`

**Files:**
- Create: `Domain/SellPlan.cs`, `Domain/SellPreflight.cs`
- Test: `Tests/Domain/SellPlanTests.cs`, `Tests/Domain/SellPreflightTests.cs`

**Interfaces:**
- Produces:
  - `SellPlan.AmountFor(ulong rawBalance, decimal? percent) : ulong` — null/blank ⇒ full; else `floor(raw × percent / 100)`; throws `NightfallException` when percent ≤ 0, > 100, or the result is 0.
  - `SellExclusion` enum: `None, ZeroBalance, NotEnoughSolForFee, PriceImpactTooHigh`
  - `SellPreflight.Check(ulong tokenRaw, long solLamports, decimal minSolForFee) : SellExclusion`
  - `SellPreflight.CheckPriceImpact(decimal priceImpactPct, decimal maxPercent) : SellExclusion`
  - `SellPreflight.Describe(SellExclusion) : string` — the operator-facing reason

- [ ] **Step 1: Failing tests** — the full matrix from spec §11 (`null`/`""`/`100` ⇒ full; `40` on `1000` ⇒ `400`; `40` on `1` ⇒ throws "rounds to zero"; `0`/`-5`/`101` ⇒ throw; zero balance ⇒ `ZeroBalance`; SOL below floor ⇒ `NotEnoughSolForFee`; SOL exactly at the floor ⇒ `None`; impact under/equal/over the cap).
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement with integer math (`raw * pct / 100` in `decimal`, then `floor`). **Step 4:** Run → PASS. **Step 5:** Commit.

---

### Task 4: `Domain/SellError.cs` + `Domain/RealizedSol.cs`

**Files:**
- Create: `Domain/SellError.cs`, `Domain/RealizedSol.cs`
- Test: `Tests/Domain/SellErrorTests.cs`, `Tests/Domain/RealizedSolTests.cs`

**Interfaces:**
- Produces:
  - `SellErrorClass` enum: `Retryable, Terminal`
  - `SellError.Classify(int? httpStatus, string? message, bool isTransport) : SellErrorClass`
  - `RealizedSol.From(long[] preBalances, long[] postBalances, long fee, int accountIndex) : long` — lamports; `(post - pre) + fee`; throws when the arrays are too short.

- [ ] **Step 1: Failing tests** — 429/503/transport/"blockhash not found"/"block height exceeded"/timeout ⇒ `Retryable`; slippage/no route/insufficient funds/400 ⇒ `Terminal`; realized `(post-pre)+fee`; short array throws.
- [ ] **Step 2–5:** run/implement/run/commit as above.

---

### Task 5: `Domain/SwapInstructionsGuard.cs`

**Files:**
- Create: `Domain/SwapInstructionsGuard.cs`
- Test: `Tests/Domain/SwapInstructionsGuardTests.cs`

**Interfaces:**
- Produces:
  - `GuardedInstruction(string ProgramId, IReadOnlyList<string> Accounts, string DataBase64)` — record
  - `SwapInstructionsGuard.AllowedProgramIds` — the allowlist (Jupiter v6, SPL Token, Token-2022, ATA, ComputeBudget, System)
  - `SwapInstructionsGuard.Assert(IEnumerable<GuardedInstruction> instructions, string userPublicKey, string inputMint, string outputMint, ulong plannedAmount, string quotedInputMint, string quotedOutputMint, ulong quotedInAmount) : void` — throws `NightfallException` naming the offending program id or field.

- [ ] **Step 1: Failing tests** — clean set passes; unknown program id, `inputMint` mismatch, `inAmount` mismatch, `outputMint` mismatch each throw with the offending value in the message.
- [ ] **Step 2–5:** run/implement/run/commit.

---

### Task 6: `Domain/SellReceipt.cs` + `Infrastructure/ReceiptsStore.cs`

**Files:**
- Create: `Domain/SellReceipt.cs`, `Infrastructure/ReceiptsStore.cs`
- Test: `Tests/Domain/SellReceiptTests.cs`, `Tests/Infrastructure/ReceiptsStoreTests.cs`

**Interfaces:**
- Produces:
  - `SellStatus` enum: `Planned, Quoted, Simulated, Sent, Sold, Unconfirmed, Failed`
  - `SellReceipt` class — `Alias, PublicKey, BatchId, Percent, TokenAmountRaw, TokenAmount, QuotedSol, RealizedSol, Signature, CloseSignature, Status, Error, StartedUtc, SettledUtc`; `MoveTo(SellStatus)` throws on an illegal transition.
  - `ReceiptsDocument` — `UpdatedUtc`, `TokenMint`, `Entries`; `Upsert(SellReceipt)`
  - `ReceiptsStore.LoadOrCreate(string path, string tokenMint) : ReceiptsStore`; `.Record(SellReceipt)` — mutate **then `Save()` immediately**, never batched.

- [ ] **Step 1: Failing tests** — every legal transition; `Sold → Sent` throws; three successive records rewrite the file each time; reload round-trips; a missing directory is created.
- [ ] **Step 2–5:** run/implement (copy `WalletFunder/Infrastructure/ReceiptsStore.cs`'s save-on-every-change shape, **without** the plan-hash/drift gate — spec §6.7)/run/commit.

---

### Task 7: `Infrastructure/SecretsFile.cs`

**Files:**
- Create: `Infrastructure/SecretsFile.cs`
- Test: `Tests/Infrastructure/SecretsFileTests.cs`

**Interfaces:**
- Produces: `SecretsFile` with `Solana`, `Token`, `Jupiter`, `Sell`, `Wallets`; section classes `SolanaSection{RpcUrl}`, `TokenSection{Mint}`, `JupiterSection{ApiBaseUrl,SlippageBps,MaxPriceImpactPercent,PrioritizationFeeLamports,QuoteCacheSeconds}`, `SellSection{MaxConcurrentSells,MinSolForFeeSol,RefreshIntervalSeconds,ConfirmTimeoutSeconds,CloseEmptiedTokenAccounts}`; `SecretsFile.Load(string path) : SecretsFile`.
- `Load` throws `NightfallException` for: missing file (names the file **and** the example template), unparsable JSON, `REPLACE_ME` in `rpcUrl` / `token.mint` / any wallet key, an invalid address, and a private key that does not derive to its stated public key.
- An **empty** `wallets` array is **not** an error here — `Program` reports it and exits 0.

- [ ] **Step 1: Failing tests** — each case above; defaults applied when a section is absent.
- [ ] **Step 2–5:** run/implement/run/commit.

---

### Task 8: `Infrastructure/Jupiter/` — client, DTOs, quote cache

**Files:**
- Create: `Infrastructure/Jupiter/JupiterQuote.cs`, `JupiterClient.cs`, `QuoteCache.cs`
- Test: `Tests/Infrastructure/QuoteCacheTests.cs`

**Interfaces:**
- Produces:
  - `JupiterQuote` — `InAmount`, `OutAmount`, `PriceImpactPct`, `InputMint`, `OutputMint`, `Raw` (the verbatim `JsonElement` to echo back to `/swap`)
  - `JupiterClient.QuoteAsync(string inputMint, string outputMint, ulong amount, int slippageBps, CancellationToken) : Task<JupiterQuote>`
  - `JupiterClient.SwapInstructionsAsync(JupiterQuote, string userPublicKey, long priorityFeeLamports, CancellationToken) : Task<IReadOnlyList<GuardedInstruction>>`
  - `JupiterClient.SwapTransactionAsync(JupiterQuote, string userPublicKey, long priorityFeeLamports, CancellationToken) : Task<byte[]>` — the base64 wire transaction from `/swap` (the Task 9 signing path)
  - `QuoteCache.GetOrAddAsync(string key, Func<Task<JupiterQuote>>) : Task<JupiterQuote>` — TTL + single-flight coalescing

- [ ] **Step 1: Failing tests** for `QuoteCache` only (the client is IO and has no fake, matching #165): two identical requests inside the TTL ⇒ one upstream call; after expiry ⇒ refetch; two concurrent identical requests ⇒ one call; a different key ⇒ its own call.
- [ ] **Step 2–5:** run/implement/run/commit.

---

### Task 9: `Infrastructure/Solana/` — reader, signer, sender, closer

**Files:**
- Create: `Infrastructure/Solana/SolanaReader.cs`, `TransactionSigner.cs`, `SolanaSender.cs`, `TokenAccountCloser.cs`
- Test: `Tests/Infrastructure/TransactionSignerTests.cs`

> ⚠️ **This task resolves spec §12 finding 1.** Solnet.Rpc 6.1.0 exposes only legacy `Message`/`Transaction`/`TransactionBuilder` — there is **no** `VersionedTransaction`, no `MessageV0`, no lookup-table support (verified against the package's own XML docs). The spec's primary path is therefore **not available**, and we take the **documented fallback**: Jupiter's `/swap` returns a ready **v0** wire transaction, which we sign **without parsing** — the signature covers the message bytes, so splicing an ed25519 signature into the signature array is format-agnostic. `SwapInstructionsGuard` still runs over `/swap-instructions` first, so the "assert what we sign" property of the design survives intact.

**Interfaces:**
- Produces:
  - `TransactionSigner.Sign(byte[] wireTransaction, Account signer) : byte[]` — pure; asserts the shortvec signature count is exactly 1, signs everything after the signature array, writes the 64-byte signature into slot 0, returns the transaction.
  - `WalletBalances(long SolLamports, ulong TokenRaw)` — record
  - `SolanaReader.GetMintAsync(string mint, CancellationToken) : Task<(ulong Supply, int Decimals)>`
  - `SolanaReader.GetBalancesAsync(IReadOnlyList<PoolEntry>, string mint, CancellationToken) : Task<IReadOnlyDictionary<string, WalletBalances>>` — two batched calls (`getMultipleAccounts` for lamports, then over derived ATAs)
  - `SolanaReader.GetTokenBalanceAsync(string owner, string mint, CancellationToken) : Task<ulong>` — the fresh single read used at execution
  - `SolanaSender.SendAndConfirmAsync(byte[] signedTx, TimeSpan timeout, CancellationToken) : Task<string>` — returns the signature; throws classified by `SellError`
  - `SolanaSender.GetRealizedLamportsAsync(string signature, string wallet, CancellationToken) : Task<long>`
  - `TokenAccountCloser.CloseAsync(PoolEntry, string mint, CancellationToken) : Task<string?>` — the separate follow-up transaction; returns null on failure (never throws into the sale)
  - `RpcUrlMask.HostOnly(string url) : string`

- [ ] **Step 1: Failing tests** for `TransactionSigner` — a synthetic wire transaction with one empty signature slot gets a valid signature that `PublicKey.Verify`s over the message bytes; a transaction declaring 2 signatures throws; a truncated buffer throws.
- [ ] **Step 2–5:** run/implement/run/commit.

---

### Task 10: `Application/` — state, runner, poll loop, endpoints, CLI, Program

**Files:**
- Create: `Application/CliOptions.cs`, `NightfallState.cs`, `SellRunner.cs`, `PollLoop.cs`, `ApiEndpoints.cs`, `ConsoleReport.cs`; `Program.cs`
- Test: `Tests/Application/NightfallStateTests.cs`

**Interfaces:**
- Produces:
  - `CliOptions.Parse(string[]) : CliOptions` — `--secrets`, `--receipts`, `--port`, `--no-open`, `-h`; `UsageException` ⇒ exit 2
  - `NightfallState` — `IsLive`, `TryBeginSell(string publicKey) : bool`, `EndSell(string)`, `Snapshot()`, `Apply(balances)`
  - `SellRunner.RunAsync(IReadOnlyList<string> wallets, decimal? percent, CancellationToken) : Task<string>` (batchId)
  - `ApiEndpoints.Map(WebApplication)` — the six routes of spec §7.1
- The wallet DTO served to the browser is `WalletView` and has **no** private-key member.

- [ ] **Step 1: Failing tests** — a sell while `SIMULATE` broadcasts nothing; `TryBeginSell` returns false for a wallet already in flight; `EndSell` always releases (even after a failure); a poll does not overwrite an in-flight row.
- [ ] **Step 2–5:** run/implement/run/commit.

---

### Task 11: `wwwroot/` — the dashboard (AFTER mockup approval)

**Files:** Create `wwwroot/index.html`, `wwwroot/app.css`, `wwwroot/app.js`

⚠️ **Gate:** produce **3 Claude Design mockups** and get the user's pick **before** writing any of these files (card #166, decision #21).

Behaviour is fixed regardless of which mockup wins — spec §7.2: card face (alias · SOL · % supply · token amount · est. SOL), hover quote panel (+ realized SOL and signature once sold), click-selects with ctrl/shift/marquee/select-all/clear, `SELL %` field (blank ⇒ 100%), `SELL SELECTED` → yes/no dialog with per-wallet amounts + quoted SOL total, SIMULATE/LIVE switch, SSE live progress, refresh.

- [ ] **Step 1:** 3 mockups → user approves one.
- [ ] **Step 2:** Implement to the approved mockup.
- [ ] **Step 3:** `dotnet run` and walk spec §14 steps 2–6 (all safe, nothing broadcast).
- [ ] **Step 4:** Commit.

---

### Task 12: Docs

**Files:** Modify `tools/README.md` (project table + a `spacestroker-nightfall` section: preconditions, SIMULATE-first workflow, config table, exit codes); update `specs/nightfall-dashboard.md` §15 revision log with the Task 9 path taken.

- [ ] **Step 1:** Write. **Step 2:** Commit + push.

---

## Self-review

- **Spec coverage:** §4.1 layout → Tasks 1–10; §4.3 config → Task 7; §4.4 gitignore → Task 1; §5 CLI → Task 10; §6.2 reads → Task 9; §6.3 amount → Task 3; §6.4 run → Task 10; §6.5 guard → Task 5; §6.6 realized → Task 4; §6.7 receipts → Task 6; §7.1 endpoints → Task 10; §7.2 UI → Task 11; §11 tests → distributed; §12 finding 1 → Task 9; finding 2 → Task 1.
- **Placeholders:** none — every task names exact files and exact produced signatures.
- **Type consistency:** `SellExclusion`, `SellStatus`, `GuardedInstruction`, `PoolEntry`, `WalletBalances` are each defined once and referenced by those names downstream.

# SpaceStroker — "Ghost wallet snipe app" — Implementation Spec

> **Status: Shipped** — merged via `feature/ghost-wallet-snipe` on **2026-08-11** (`monkify-meta`
> only). Refined + specced from Trello card
> [Create ghost wallet snipe app #63](https://trello.com/c/nokWHm7z) (2026-08-11).
> Distilled in [`features/ghost-wallet-snipe.md`](../features/token-ops.md).
>
> **Post-ship note — §6.2 and §6.4 were rewritten during implementation**, because the protocol facts
> read off mainnet contradicted what this spec had assumed in five ways (curve field names, a third
> `buy` argument, 18 live accounts vs the IDL's 16, a per-mint token program, and fee rates that are
> `global`-account config). §12 finding 4 also resolved against us: Solnet.Programs 6.1.0 has **no**
> `ComputeBudgetProgram`. See the revision log.
>
> **Not executed at ship time:** a live `--execute` buy remains an **operator** step (§14), so the
> submit/confirm path, the ATA-creation branch, the slippage revert and the mid-run graduation stop
> have never run against real money.
> Repos affected: **`monkify-meta` only** (a second project in the existing `tools/` solution). No
> `monkify-backend`, no `monkify-front`, no `monkify-lp` change.
>
> **Purpose:** a standalone console app that drives a pool of Solana wallets we control to a
> configured share of our own pump.fun token's supply, one buy per wallet, spread across a one-hour
> window, then reports exactly what happened and what the pool ended up holding. It is the **buy**
> half of the pair whose **funding** half already shipped as
> [`spacestroker-fund-wallets`](coinbase-wallet-funding.md) (#165) — same solution, same pool file.
>
> ⚠️ **This tool spends real SOL and its buys are irreversible.** Every guardrail below that looks
> like ceremony (dry-run default, typed confirmation, preflight abort, on-chain-truth resume,
> per-transaction `maxSolCost`) exists because there is no undo on a landed buy. Do not simplify them
> away.
>
> ⚠️ **One thing in here must not be taken on trust: the pump.fun account layout** (§6.4). It has
> changed before. Decode a recent successful buy on mainnet and match it before writing the
> instruction — a stale account list is a failed transaction at best and a wrong-account transfer at
> worst.

---

## 1. Summary

One new console app, `spacestroker-snipe-token`, joining the existing
`monkify-meta/tools/SpaceStroker.Tools.sln` alongside `spacestroker-fund-wallets`.

| Stage | What happens |
| --- | --- |
| **Load** | Read the git-ignored `tools/secrets.json` — Solana RPC + snipe options + the **shared** wallet pool. The sniper binds `alias`, `PublicKey`, `PrivateKey`, `SupplyPercentage`; the funder's `Label`/`SOLAmount` are ignored (and vice versa). |
| **Plan** | Read the mint's on-chain supply and the bonding-curve reserves. For each wallet compute **target tokens** (`SupplyPercentage`% of total supply), its **current token balance**, and the **shortfall** to buy. Quote the SOL cost of each shortfall against the curve, in alias order, compounding as the price rises. |
| **Preflight** | Validate aliases, addresses, keypairs, percentages and curve state. Snapshot each wallet's SOL and token balance. **Any failure aborts before buy #1.** |
| **Confirm** | Dry run prints the ladder and stops. `--execute` additionally requires a **typed confirmation** echoing the quoted total SOL. |
| **Buy** | One transaction per wallet, dispatched in **ascending `alias` order** at **randomized offsets** across the window (default 60 min). Each carries a `maxSolCost` ceiling and a priority fee. |
| **Settle** | Confirm the signature, then require an **observed token-balance increase** at the wallet. The balance — not the receipt — is the truth. |
| **Report** | A receipts JSON written after every state change (audit + resume aid), plus an **HTML summary** and a **JSON summary** under `tools/reports/<timestamp>/`. |

### Before / after

| Aspect | Today | After |
| --- | --- | --- |
| Acquiring supply across a wallet pool | Manual buys, one wallet at a time, under launch-hour pressure | One command over the whole pool |
| Knowing what each wallet should hold | Mental arithmetic against a moving price | `SupplyPercentage` per wallet, resolved against on-chain supply |
| A killed run | Reconcile by hand, guess what landed | Resume tops up to target from on-chain balances; never double-buys |
| Overpaying on a spike | Whatever the tx lands at | Per-transaction `maxSolCost` ceiling |
| Buying into a graduated curve | Raw program error | Preflight refusal; mid-run graduation stops cleanly and says where |
| Record of the operation | None | Receipts + HTML/JSON reports with achieved vs target per wallet |

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo:** work lives in `.worktrees/ghost-wallet-snipe/monkify-meta`; the
  `monkify-meta` hub stays clean on `main`. Branch `feature/ghost-wallet-snipe` is pushed to `origin`
  (§2 Git).
- **Secrets — mock only in committed files.** `tools/secrets.example.json` carries placeholders only.
  `tools/secrets.json` holds **live wallet private keys** and is git-ignored. ⚠️ **`tools/.gitignore`
  needs two new rules** — the reports directory and this tool's receipts file are **not** covered by
  the existing globs (§12 finding 1). No private key, keyed RPC URL, or any secrets value beyond
  public addresses may appear in console output, receipts or either report.
- **No production queries / no live external calls during implementation.** A live mainnet run is an
  **operator step** (`constraints.md` §5). Dry runs against a real RPC are fine; the agent never
  executes `--execute`.
- **Backend:** unchanged ⇒ **no `SpaceStroker.Tests` change** and **no integration test.** The card's
  `## Level` is **`Feature`**, and the mandatory happy-path integration test in
  `SpaceStroker.Tests.Integration` is **explicitly waived on the card** — zero backend surface, so
  there is no code path for it to drive (waiver precedent:
  [`coinbase-wallet-funding.md`](coinbase-wallet-funding.md),
  `main-production-operator.md`). The **`features/` doc is still
  required** at ship time.
- **Unit tests are required** for the pure logic — curve math, target/shortfall, affordability,
  schedule, preflight, report models (§11). There is **no fake RPC**: the live path is verified by
  dry-running against a real endpoint, same as the funder.
- **Project layout:** `conventions/backend.md` → "The folder tree must show the architecture" —
  `Domain/` (IO-free) · `Infrastructure/<external system>/` · `Application/`, matching namespaces,
  mirrored in the test project, `Program.cs` at the root.
- **Knowledge base stays current:** `tools/README.md` gains this tool's section, `INDEX.md` is
  updated, and `features/ghost-wallet-snipe.md` is written at ship time.
- **Feature-specific:** the tool must never be able to buy without `--execute` **and** the typed
  confirmation; it must never submit a second buy for supply a wallet already holds; and it must never
  build a buy instruction without a `maxSolCost`.

---

## 3. Resolved product decisions

Carried from the refined card (answer mode: **short**). Items 15–22 are spec-level additions made
while exploring the code and are marked as such.

| # | Decision |
| --- | --- |
| 1 | **Buy-only.** The token and its CA already exist when the tool runs; the tool never creates a mint, never uploads metadata, never bundles with a create transaction. Consequence accepted: no block-0 protection — the one-hour spread abandons block-0 sniping anyway. |
| 2 | **No creator-fee key in this tool's secrets.** Buy-only means nothing signs or claims with it. |
| 3 | **Execution path: our own RPC + a hand-built pump.fun `buy` instruction** via Solnet. No PumpPortal, no Jupiter — a one-shot real-money window should not depend on a third party's uptime or take a per-trade fee. |
| 4 | ⚠️ **The account list and instruction discriminator are verified against a live recent buy transaction at implementation time** (§6.4), not copied from documentation or memory. |
| 5 | **Keyed RPC required** (Helius, as `SpaceStroker.Infrastructure/Services/Solana` already uses). `api.mainnet-beta.solana.com` rate-limits and silently drops sends; with ~40 real-SOL buys in an hour that is a money bug. |
| 6 | **`SupplyPercentage` is a percent number of TOTAL supply.** `0.4504` ⇒ 0.4504%, resolved against the mint's on-chain supply read at preflight. Per-wallet values may vary freely; non-uniform sizes are expected. |
| 7 | **Supply target wins over budget.** No global SOL cap: each wallet buys until it holds its target or its SOL runs out. Accepted consequence: a hot launch can drain the pool and later wallets fall short. |
| 8 | **Every transaction still carries `maxSolCost`** = quoted cost × (1 + `slippageTolerance`). Not a budget cap — it is what stops one buy being sandwiched into paying many times the quote, and the instruction cannot be built without it. |
| 9 | **One buy per wallet, in ascending `alias` order** (⚠️ a deliberate change from the card's original "largest supply first"), at **randomized offsets** across `spreadMinutes` (default 60). Order is fixed; only the gaps jitter. Slicing a wallet into k buys was rejected: k× the fees, k× later prices, k× the half-finished states. |
| 10 | **One pool file, shared with `spacestroker-fund-wallets`.** The funder already ignores unknown JSON fields specifically so this file loads in both tools. The sniper is the only tool that ever binds a `PrivateKey`. |
| 11 | **A new `alias` field per pool entry**, required and unique — it is the ordering key, so blank or duplicated is a preflight failure, not a tiebreak. The funder's `Label` is untouched; keep them identical by convention. |
| 12 | **Bonding curve only; no AMM path.** Preflight refuses a graduated CA; mid-run graduation skips the remaining wallets and reports where it stopped. A post-graduation top-up is a separate card. |
| 13 | **On-chain state is the resume truth; receipts are an audit trail.** Solana has no server-side idempotency token, so a receipts-driven resume could double-buy after a crash between confirmation and write. Resume reads each wallet's token balance and buys only the shortfall. |
| 14 | **Two independently produced artifacts** — an HTML summary and a JSON summary. The drift risk between two code paths is knowingly accepted; there is **no** `report` re-render command. |
| 15 | **(Spec addition)** The discriminator is **computed at runtime** as `SHA256("global:buy")[0..8]`, not pasted as a hex constant. Rationale: decision 4 forbids trusting a memorized constant, and computing it is three lines that can never be mistyped. The account list still has to be verified by hand — it cannot be derived. |
| 16 | **(Spec addition)** A read-only **`status`** command prints each wallet's current holding vs target from on-chain state and **structurally cannot submit a transaction**. Rationale: the same reason the funder has one — the safest way to answer "where did the run get to" is a command with no send path. |
| 17 | **(Spec addition)** A per-wallet **`reserveLamports`** (default covering base fee + priority fee + ATA rent + a margin) is withheld from the affordability calculation. Rationale: decision 7 spends "everything the wallet holds", and a wallet with zero lamports left cannot pay for its own transaction — the buy fails at the last wallet, which is exactly where it costs most. |
| 18 | **(Spec addition)** `--wallet <alias>` (repeatable) limits a run to named wallets. Rationale: the top-up path (decision 13) is only useful if a single short wallet can be retried without re-walking the pool. |
| 19 | **(Spec addition)** The pool file may be supplied with **`--pool <path>`**, exactly as the funder allows, so the same standalone file feeds both tools without being pasted into `secrets.json`. |
| 20 | **(Spec addition)** A wallet already at or above its target is **skipped with a `Skipped` status**, not treated as an error. Rationale: it is the normal state of every resumed run. |
| 21 | **(Spec addition)** Retrying the **submit** is permitted only after re-reading the wallet's token balance (§6.6). Unlike the funder's Coinbase `idem`, nothing dedupes a resubmitted Solana transaction, so the balance read *is* the idempotency mechanism. |
| 22 | **Level: `Feature`** — `features/ghost-wallet-snipe.md` required at ship time; backend integration test **waived** (no backend surface). |

---

## 4. Solution layout and configuration

There is **no application data model change** — no entity, no EF mapping, no migration. This section
replaces §4 of the template with the file layout and config model.

### 4.1 New project in the existing `tools/` solution

```
tools/
├── SpaceStroker.Tools.sln                      # EXISTS — add the two new projects
├── .gitignore                                  # EXISTS — ⚠️ needs two new rules (§4.4)
├── README.md                                   # EXISTS — add the sniper's section
├── secrets.example.json                        # EXISTS — add snipe + pool fields (placeholders only)
├── SpaceStroker.WalletFunder/                  # EXISTS — untouched
├── SpaceStroker.WalletFunder.Tests/            # EXISTS — untouched
├── SpaceStroker.TokenSniper/
│   ├── SpaceStroker.TokenSniper.csproj         # AssemblyName: spacestroker-snipe-token
│   ├── Program.cs                              # entry point: parse → load → run → map exit code
│   ├── Domain/                                 # pure; no IO, no HTTP, no filesystem, no clock
│   │   ├── SnipePool.cs                        # PoolEntry (alias, address, key, percent), parsing
│   │   ├── BondingCurve.cs                     # curve state record + constant-product pricing
│   │   ├── SnipePlan.cs                        # target/shortfall/affordability, the quoted ladder
│   │   ├── BuySchedule.cs                      # alias order + jittered offsets (injectable Random)
│   │   ├── SnipePreflight.cs                   # validation; takes balances as values
│   │   ├── RunLedger.cs                        # WalletOutcome/RunDocument + status transitions
│   │   └── SniperException.cs                  # SniperException + UsageException
│   ├── Infrastructure/
│   │   ├── SecretsFile.cs                      # config model + Load() with placeholder detection
│   │   ├── ReceiptsStore.cs                    # receipts persistence (save after every change)
│   │   ├── Reports/
│   │   │   ├── JsonReportWriter.cs             # the JSON summary
│   │   │   └── HtmlReportWriter.cs             # the HTML summary (self-contained, no assets)
│   │   └── Solana/
│   │       ├── SolanaGateway.cs                # RPC: supply, balances, accounts, submit, confirm
│   │       ├── PumpFunProgram.cs               # PDAs, discriminator, account list, instruction build
│   │       └── CurveAccountReader.cs           # decode the bonding-curve account layout
│   └── Application/
│       ├── Sniper.cs                           # the run: preflight → confirm → buy → settle → report
│       ├── CliOptions.cs                       # argument parsing + usage banner
│       └── ConsoleReport.cs                    # ladder table, typed confirmation, summary table
└── SpaceStroker.TokenSniper.Tests/
    ├── SpaceStroker.TokenSniper.Tests.csproj
    ├── Domain/                                 # SnipePoolTests, BondingCurveTests, SnipePlanTests,
    │   …                                       # BuyScheduleTests, SnipePreflightTests, RunLedgerTests
    ├── Infrastructure/                         # SecretsFileTests, ReceiptsStoreTests,
    │   …                                       # PumpFunProgramTests, JsonReportWriterTests, HtmlReportWriterTests
    └── Application/                            # ConsoleReportTests
```

Namespaces follow the folders exactly: `SpaceStroker.TokenSniper.Domain`,
`….Infrastructure`, `….Infrastructure.Solana`, `….Infrastructure.Reports`, `….Application`.

### 4.2 Project files

`SpaceStroker.TokenSniper.csproj` — copy the shape of `SpaceStroker.WalletFunder.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>SpaceStroker.TokenSniper</RootNamespace>
    <AssemblyName>spacestroker-snipe-token</AssemblyName>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Solnet.Rpc" Version="6.1.0" />
    <PackageReference Include="Solnet.Wallet" Version="6.1.0" />
    <PackageReference Include="Solnet.Programs" Version="6.1.0" />
  </ItemGroup>
</Project>
```

`Solnet.Programs` is new relative to the funder (the funder only reads balances); it supplies
`AssociatedTokenAccountProgram` and `SystemProgram`. All three are pinned to **6.1.0**, the version
`monkify-backend` already uses. Tests project: copy `SpaceStroker.WalletFunder.Tests.csproj` and
repoint the `ProjectReference`. Add both projects to `SpaceStroker.Tools.sln` (`dotnet sln add`).

> ⚠️ Do **not** add `Portable.BouncyCastle` here. The funder needs it for Coinbase's Ed25519 JWS; the
> sniper signs Solana transactions, which Solnet does natively. Adding the newer
> `BouncyCastle.Cryptography` alongside Solnet's transitive 1.9.0 is what produces the CS0433
> ambiguity recorded in [`coinbase-wallet-funding.md`](coinbase-wallet-funding.md) §12.

### 4.3 Configuration model (`SecretsFile.cs`)

The sniper reads the **same** `tools/secrets.json` the funder does, adding a `snipe` section and
extra fields on the shared `wallets[]` entries. Follow the funder's `SecretsFile`: `[JsonPropertyName]`
on every member, static `Load(path)` throwing `SniperException` with an actionable message, and
`REPLACE_ME*` placeholder detection that fails fast.

`secrets.example.json` (tracked — placeholders only) gains:

```jsonc
{
  "solana": {
    "rpcUrl": "https://mainnet.helius-rpc.com/?api-key=REPLACE_ME_HELIUS_KEY"
  },
  "snipe": {
    "mint": "REPLACE_ME_TOKEN_MINT_ADDRESS",
    "spreadMinutes": 60,
    "slippageTolerance": 0.25,
    "priorityFeeMicroLamports": 200000,
    "computeUnitLimit": 250000,
    "reserveLamports": 15000000,
    "confirmTimeoutSeconds": 90,
    "balancePollSeconds": 5
  },
  "wallets": [
    {
      "alias": "W01",
      "Label": "W01",
      "PublicKey": "REPLACE_ME_BASE58_ADDRESS",
      "PrivateKey": "REPLACE_ME_BASE58_SECRET_KEY",
      "SOLAmount": 2.54,
      "SupplyPercentage": 0.4504
    }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `solana.rpcUrl` | Keyed RPC endpoint. Shared with the funder (which uses it for balance reads only). **Masked in all output** — reuse the backend's `SolanaRpcClientProvider.MaskRpcUrl` approach. |
| `snipe.mint` | The token's CA. Must be a valid 32-byte base58 address that resolves to an SPL mint. |
| `snipe.spreadMinutes` | Buy window; `0` ⇒ back to back. Default 60. |
| `snipe.slippageTolerance` | Fraction added to the quote to form `maxSolCost` (0.25 ⇒ +25%). |
| `snipe.priorityFeeMicroLamports` / `computeUnitLimit` | Compute-budget instructions prepended to every buy. |
| `snipe.reserveLamports` | Withheld per wallet from the affordability calculation (decision 17). |
| `snipe.confirmTimeoutSeconds` / `balancePollSeconds` | Signature-confirmation and balance-delta polling bounds. |
| `wallets[].alias` | **Required, unique.** The ordering key (§6.5). |
| `wallets[].PrivateKey` | Base58 secret key. **Bound only by this tool; never printed, logged, or written to any artifact.** |
| `wallets[].SupplyPercentage` | Percent number of total supply (`0.4504` ⇒ 0.4504%). |
| `wallets[].Label` / `SOLAmount` | The funder's fields — parsed and ignored here (§6.1). |

### 4.4 ⚠️ `tools/.gitignore` — two new rules are MANDATORY

**Code-exploration finding (§12 finding 1).** The existing file ignores `secrets*.json`,
`receipts*.json`, `**/bin/`, `**/obj/`. Neither of this tool's new artifact paths is covered:

- `tools/reports/**` — the HTML and JSON summaries carry every ghost wallet's address, holdings and
  signatures. Committing them publishes the pool.
- A receipts file named `snipe-receipts.json` would **not** match `receipts*.json` — the glob is
  anchored at the start of the filename. Either name the file `receipts.snipe.json` (matches) **or**
  add an explicit rule. This spec chooses the explicit rule, because a filename that must keep a
  particular prefix to stay secret is a trap for the next person.

Append:

```gitignore
# Run reports — contain wallet addresses, holdings and signatures.
/reports/

# The sniper's run state (does not match the receipts*.json glob above).
/snipe-receipts*.json
```

⚠️ **Both rules must carry the leading `/`.** Shipped as unanchored `reports/` first, which matches a
directory of that name **at any depth** — it silently swallowed the tool's own
`Infrastructure/Reports/` source folder (and its tests), so `git add -A` skipped four real source
files and the omission only surfaced as a build break when the branch was merged.

---

## 5. CLI surface

```
spacestroker-snipe-token [command] [options]

Commands:
  snipe       (default) build the plan and — with --execute — buy the pool up to target.
              Without --execute it is a dry run: ladder table + full preflight, nothing submitted.
  status      read-only: print each wallet's holding vs target from on-chain state. Never submits.

Options:
  --secrets <path>        secrets file (default: tools/secrets.json)
  --pool <path>           load the wallet array from a standalone JSON file instead of secrets.wallets
  --receipts <path>       receipts file (default: tools/snipe-receipts.json)
  --reports <dir>         report output root (default: tools/reports)
  --execute               actually submit buys. Without it, `snipe` is a dry run.
  --spread-minutes <n>    override snipe.spreadMinutes for this run
  --wallet <alias>        limit the run to this wallet (repeatable)
  -h | --help             usage
```

Follow `SpaceStroker.WalletFunder`'s `Program.cs` and `Application/CliOptions.cs`: top-level
statements, hand-rolled parsing (no `System.CommandLine`), config paths defaulted four levels above
`AppContext.BaseDirectory`, `try/catch` printing `error: {message}`.

**Exit codes:** `0` every targeted wallet reached its target, or a dry run, or an empty pool ·
`1` any wallet short, failed or unresolved, or a fatal error · `2` usage error.

---

## 6. Behaviour

### 6.1 Pool loading — `Domain/SnipePool.cs`

Mirrors `WalletFunder`'s `WalletPool`: a `RawPoolEntry` binding shape with `[JsonPropertyName]`,
`PropertyNameCaseInsensitive`, comments and trailing commas allowed, and **unknown members ignored**
(never set `UnmappedMemberHandling.Disallow` — the funder's `Label`/`SOLAmount` must load unchanged,
and AC18 asserts the round trip both ways).

```csharp
public sealed record PoolEntry(string Alias, string PublicKey, string PrivateKey, decimal SupplyPercentage);
```

⚠️ **Do not copy the funder's alias fallback.** `WalletPool.FromEntries` synthesises
`W{index:00}` when `Label` is blank; here a blank `alias` is a **preflight error** (AC3) because it is
the ordering key — silently inventing one would reorder the run.

### 6.2 Curve state and pricing — `Domain/BondingCurve.cs`, `Infrastructure/Solana/CurveAccountReader.cs`

`CurveAccountReader` fetches the bonding-curve account and decodes it into the pure record; all
arithmetic lives in `Domain` and is unit-tested with no network.

```csharp
public sealed record CurveState(
    ulong VirtualTokenReserves, ulong VirtualQuoteReserves,
    ulong RealTokenReserves,    ulong RealQuoteReserves,
    ulong TokenTotalSupply,     bool  Complete,
    string Creator,             bool  IsMayhemMode,
    bool  IsCashbackCoin,       string QuoteMint);
```

- **Layout (VERIFIED 2026-08-11 against the program's own on-chain IDL and a real account dump; this
  replaces the pre-implementation guess of `…_sol_reserves` + optional creator):** 8-byte discriminator
  `[23,183,248,55,96,216,172,96]`, then `virtual_token_reserves`, `virtual_quote_reserves`,
  `real_token_reserves`, `real_quote_reserves`, `token_total_supply` (all `u64`), `complete` (bool),
  `creator` (pubkey), `is_mayhem_mode` (bool), `is_cashback_coin` (bool), `quote_mint` (pubkey).
- ⚠️ **A curve is not necessarily SOL-quoted.** `quote_mint == 11111111111111111111111111111111` (the
  default pubkey) means SOL; anything else is priced in another token and is **refused** at preflight
  rather than mispriced as lamports.
- ⚠️ **Fees are program config, not constants.** `fee_basis_points` (**95**) and
  `creator_fee_basis_points` (**5**) are read from the `global` PDA at runtime — they have changed
  before, and a stale constant misquotes every wallet.
- **Price for a buy of `t` tokens** (constant product over the *virtual* reserves):

  ```
  k       = VirtualSolReserves × VirtualTokenReserves
  solIn   = ceil( k / (VirtualTokenReserves − t) ) − VirtualSolReserves
  cost    = solIn + fees          // platform + creator fee, read from the live program config
  ```

  All integer math on `ulong`/`UInt128` lamports and base units — **never `double`**. Round **up** on
  cost and **down** on tokens: the operator should be quoted no less than they pay.
- **Inverse (how many tokens `s` lamports buys)**, needed for the affordability path in §6.3:

  ```
  t = VirtualTokenReserves − ceil( k / (VirtualSolReserves + sNetOfFees) )
  ```

- **Sequential quoting:** the ladder is quoted **in execution order**, each wallet's quote taken
  against the reserves *after* the previous wallet's simulated buy. A ladder quoted against the
  opening price understates every wallet after the first.
- **Graduated** ⇔ `Complete == true`, the curve account is missing/closed, or `RealTokenReserves == 0`.
  Any of these at preflight ⇒ refuse (AC5); mid-run ⇒ stop (AC6).

### 6.3 Targets, shortfall and affordability — `Domain/SnipePlan.cs`

For each wallet, in alias order:

```
targetBaseUnits    = floor( mintTotalSupply × SupplyPercentage / 100 )
shortfallBaseUnits = max( 0, targetBaseUnits − currentTokenBalance )
spendableLamports  = max( 0, walletSolBalance − reserveLamports − ataRentIfMissing )
```

- `shortfallBaseUnits == 0` ⇒ status **`Skipped`** (decision 20), no transaction.
- If the quoted cost of the shortfall exceeds `spendableLamports`, buy the **inverse-priced maximum
  the wallet can afford** (§6.2) and record the wallet as **`Short`** with the gap in both base units
  and percent (AC9). Decision 7: the target loses to the wallet's balance, never to a budget knob.
- `maxSolCost = ceil( quotedCost × (1 + slippageTolerance) )`, capped at `spendableLamports` — the
  ceiling can never authorise more than the wallet can actually pay.
- **Preflight** (`Domain/SnipePreflight.cs`, pure — balances and supply passed **as values**, mirroring
  `Preflight.Check(pool, availableSol, …)`): every alias present and unique; every `PublicKey` valid
  **and** 32 bytes on decode (⚠️ `new PublicKey(string)` is *not* validation — use `PublicKey.IsValid`
  plus a decode assertion, per the funder's finding); every `PrivateKey` derives its `PublicKey`
  (AC16); every `SupplyPercentage > 0`; the sum of targets ≤ what the curve can still sell (AC15);
  the mint resolves and is not graduated. **All failures are collected and reported together** (AC3),
  never the first one only. An empty pool is a no-op, exit 0 (AC4).

### 6.4 The buy instruction — `Infrastructure/Solana/PumpFunProgram.cs`

⚠️ **This was the one part of the spec that had to be re-derived rather than trusted — and doing so
changed it.** ✅ **RESOLVED at implementation time (2026-08-11)** from the program's **own on-chain
Anchor IDL** (`AYgC53tU5BbP2NAnv5nConJxAdpQZctvmZK88pu69xRs`) plus decoded live buys. Every derivation
below is pinned in `PumpFunProgramTests` against a real mainnet transaction; if pump.fun redeploys,
that test fails and the fix is to re-derive from a fresh transaction, never to patch from documentation.

| Element | Verified value |
| --- | --- |
| Program id | `6EF8rrecthR5Dkzon8Nwu78hRvfCKubJ14M5uBEwF6P` · fee program `pfeeUxB6jkeY1Hxd7CsFCAjcbHA9rWtchMGdZ6VojVZ` |
| Discriminator | **Computed** as `SHA256("global:buy")[0..8]` = `66063d1201daebea` (decision 15) |
| Data | discriminator ‖ `amount: u64` ‖ `max_sol_cost: u64` ‖ **`track_volume: OptionBool`** — ⚠️ a **third arg the pre-implementation spec did not know about**. Live traffic carries all three encodings and all land: omitted (24 bytes, the commonest — our default), `0x00`, `0x01 0x01`. |
| Accounts (16, IDL order) | `global` PDA`["global"]` · `fee_recipient` (w, from the global account) · `mint` · `bonding_curve` PDA`["bonding-curve", mint]` (w) · `associated_bonding_curve` (w) · `associated_user` (w) · `user` (signer, w) · `system_program` · `token_program` · `creator_vault` PDA`["creator-vault", curve.creator]` (w) · `event_authority` PDA`["__event_authority"]` · `program` · `global_volume_accumulator` PDA`["global_volume_accumulator"]` · `user_volume_accumulator` PDA`["user_volume_accumulator", user]` (w) · `fee_config` PDA`["fee_config", <program>]` **under the fee program** · `fee_program` |

⚠️ **Live buys carry 18 accounts, not 16.** The two extras are trailing remaining-accounts the IDL does
not declare (one owned by the fee program, one not yet created). We send the 16 the IDL declares and
expose `snipe.extraAccounts` for forward-compatibility; if a future deployment *requires* them, the
failure mode is a **reverted transaction — no SOL moves**.

⚠️ **The token program varies per mint.** Live pump.fun mints use both SPL Token and Token-2022, and
both the buyer's and the curve's ATA derivations depend on which. Solnet's
`AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount` hardcodes SPL Token, so the tool derives
ATAs itself from `[owner, tokenProgram, mint]`.

Transaction assembly, following `SolanaService.TransferTokens` in `monkify-backend`
(`TransactionBuilder` → `SetRecentBlockHash` → `SetFeePayer` → `AddInstruction` → `Build(accounts)`):

1. Compute-budget instructions first (`computeUnitLimit`, `priorityFeeMicroLamports`). ⚠️ Verify
   Solnet 6.1.0 exposes `ComputeBudgetProgram`; if it does not, build the two instructions by hand
   (§12 finding 4) rather than dropping the priority fee.
2. `AssociatedTokenAccountProgram.CreateAssociatedTokenAccount` **only when the ATA is missing**
   (AC14) — idempotent creation is not free, and an unconditional create makes every resumed buy fail.
3. The buy instruction.
4. Sign with the wallet's `Account`, submit via `SendTransactionAsync`.

Each wallet signs its own transaction and pays its own fee, so the funder's signature-collision
gotcha (identical messages ⇒ identical signatures, `SolanaService.cs`'s memo workaround) cannot arise
here: the fee payer differs per transaction. Do not add a memo.

### 6.5 Schedule — `Domain/BuySchedule.cs`

Same shape as the funder's `SendSchedule` (injectable `Random`, `Random.Shared` in production) with
**one deliberate difference: the order is not shuffled.**

```csharp
public static IReadOnlyList<ScheduledBuy> Build(IReadOnlyList<PlannedBuy> buys, TimeSpan window, Random rng)
```

Entries are sorted by `alias` (ordinal `StringComparer.Ordinal`), then given `n` random offsets in
`[0, window)`, **sorted ascending** so the offsets rise with the order. `window == 0` ⇒ all offsets
zero, back to back. The process must stay alive for the window; being killed is safe (§6.6).

⚠️ Ordinal sorting means `W10` precedes `W2`. Preflight **warns** (does not fail) when the pool mixes
zero-padded and unpadded aliases (AC19), and the executed order must match the printed ladder exactly.

### 6.6 Submit, settle and resume — `Application/Sniper.cs`, `Infrastructure/Solana/SolanaGateway.cs`

1. **Snapshot** the wallet's token balance before submitting.
2. **Submit**, then confirm the signature (`confirmTimeoutSeconds`).
3. **Require a balance increase** at the wallet before the buy counts as `Filled` — the signature
   alone is not enough (the funder's rule, transplanted: Coinbase `completed` did not mean funded
   either). Poll every `balancePollSeconds` until the delta appears or the timeout expires; on
   timeout, record `Unconfirmed` and continue.
4. **Retry policy.** Transport errors, timeouts and a lost response ⇒ **re-read the token balance
   first** (AC12), and only resubmit when the balance shows the buy did not land. A blockhash expiry
   counts as "not landed" **only** after that read. A program error (slippage breach on `maxSolCost`,
   AC13) is terminal for that wallet: record `Failed` with the reason and move on — one wallet never
   stops the batch.
5. **Receipts** (`snipe-receipts.json`) are written after every state change, so a crash loses at most
   the in-flight wallet's record. They are an **audit trail, not the resume authority**.
6. **Resume** is simply re-running: §6.3 recomputes the shortfall from live balances, so an already
   filled wallet is `Skipped` (AC10) and a wallet whose buy landed before the process died is not
   bought again (AC11). This is the whole reason for decision 13 — there is nothing else to implement.

### 6.7 Artifacts — `Infrastructure/Reports/`

Two writers, deliberately independent (decision 14), both fed from `RunLedger`'s outcome records,
written to `tools/reports/<yyyyMMdd-HHmmss>/`:

| Artifact | Contents |
| --- | --- |
| `snipe-report.json` | Per wallet: `alias`, `publicKey`, `targetPercent`, `achievedPercent`, `targetBaseUnits`, `tokensReceived`, `solSpent`, `averagePrice`, `signature`, `status`, `timestamp`, `note`. Run totals: `poolPercentOfSupply`, `totalSolSpent`, `blendedAveragePrice`, `mint`, `startedAt`, `finishedAt`, `walletsFilled/short/skipped/failed`. |
| `snipe-report.html` | The same data as a **self-contained** page — no external CSS, JS, fonts or images. A summary block (pool % of supply, total SOL, blended price), a per-wallet table with signatures linked to Solscan, and a timeline of the buys in execution order. Every wallet that fell short appears with its reason (AC16). |

⚠️ **Neither artifact, nor the receipts file, nor any console line may contain a private key or the
keyed RPC URL** (AC17). Mask the RPC URL in the same way `SolanaRpcClientProvider.MaskRpcUrl` does; the
`PrivateKey` field must never be assigned into a report model at all — the report types simply do not
carry it.

### 6.8 Console output — `Application/ConsoleReport.cs`

Mirror the funder's `ConsoleReport`: a ladder table before anything happens (alias, target %, current
holding, shortfall, quoted SOL, `maxSolCost`, scheduled offset), the typed-total confirmation prompt
(invariant-formatted — ⚠️ a `pt-BR` machine formats `2.54` as `2,54`, the funder's finding), a live
line per wallet as it executes, and a summary table with the same totals as the JSON report.

---

## 7. Frontend behaviour

**None.** No `monkify-front` or `monkify-lp` change: no composable, service, type, view, route, env
var or copy. Nothing about this tool is exposed in any app.

---

## 8. Flow diagram

```mermaid
sequenceDiagram
    autonumber
    participant Op as Operator
    participant CLI as spacestroker-snipe-token
    participant RPC as Solana RPC (keyed)
    participant Curve as pump.fun curve

    Op->>CLI: snipe (dry run)
    CLI->>RPC: mint supply, curve account, per-wallet SOL + token balances
    RPC-->>CLI: state
    CLI->>CLI: targets, shortfalls, sequential quotes, preflight
    CLI-->>Op: ladder table (nothing submitted)
    Op->>CLI: snipe --execute
    CLI-->>Op: type the quoted total SOL
    Op-->>CLI: 12.4193
    loop each wallet, alias order, jittered offset
        CLI->>CLI: re-quote against current reserves
        alt curve graduated
            CLI-->>Op: stop; remaining wallets skipped
        else
            CLI->>RPC: submit buy (maxSolCost, priority fee)
            RPC->>Curve: buy
            CLI->>RPC: confirm signature
            CLI->>RPC: token balance delta?
            RPC-->>CLI: increased ⇒ Filled
        end
        CLI->>CLI: write receipts
    end
    CLI->>CLI: write reports/<timestamp>/{json,html}
    CLI-->>Op: summary + exit code
```

---

## 9. Out of scope

- **Creating the token** on pump.fun — done by hand, outside this tool.
- **Selling / exit tooling** — explored during refinement and explicitly dropped; its own card, and it
  would need the AMM venue this spec excludes.
- **AMM / PumpSwap support**, in either direction.
- **Jito bundling / block-0 sniping.**
- **Funding the wallets** — that is `spacestroker-fund-wallets` (#165).
- **Any detection-evasion feature** — funding-graph obfuscation, or amount/timing patterns tuned to
  defeat cluster detection. The per-wallet split and the one-hour spread are the extent of it; this is
  a boundary on the card, not a "not yet".
- **Disclosure/publishing automation.**
- **A `report` re-render command** (decision 14).

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

**No changes.** Reference only: `src/SpaceStroker.Infrastructure/Services/Solana/SolanaService.cs`
(transaction assembly, ATA derivation) and `SolanaRpcClientProvider.cs` (RPC masking).

### 10.2 Frontend (`monkify-front`, `monkify-lp`)

**No changes.**

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `tools/SpaceStroker.Tools.sln` | Add the sniper + its test project |
| 2 | `tools/SpaceStroker.TokenSniper/SpaceStroker.TokenSniper.csproj` | New — net8.0 exe, `spacestroker-snipe-token`, Solnet 6.1.0 ×3 |
| 3 | `tools/SpaceStroker.TokenSniper/Program.cs` | New — parse → load → run → exit code mapping (1 / 2) |
| 4 | `…/Domain/SnipePool.cs` | New — `PoolEntry`, parsing, unknown fields ignored, **no alias fallback** |
| 5 | `…/Domain/BondingCurve.cs` | New — `CurveState`, forward/inverse constant-product pricing, graduation test |
| 6 | `…/Domain/SnipePlan.cs` | New — target, shortfall, affordability, `maxSolCost`, sequential ladder |
| 7 | `…/Domain/BuySchedule.cs` | New — alias ordering + ascending jittered offsets |
| 8 | `…/Domain/SnipePreflight.cs` | New — all-at-once validation, balances passed as values |
| 9 | `…/Domain/RunLedger.cs` | New — `WalletOutcome` (`Pending/Filled/Short/Skipped/Failed/Unconfirmed`), run totals |
| 10 | `…/Domain/SniperException.cs` | New — `SniperException`, `UsageException` |
| 11 | `…/Infrastructure/SecretsFile.cs` | New — `snipe` section + shared `wallets[]`, `REPLACE_ME` detection |
| 12 | `…/Infrastructure/ReceiptsStore.cs` | New — write after every state change |
| 13 | `…/Infrastructure/Solana/SolanaGateway.cs` | New — supply, balances, account fetch, submit, confirm, retry |
| 14 | `…/Infrastructure/Solana/PumpFunProgram.cs` | New — PDAs, computed discriminator, verified account list, instruction |
| 15 | `…/Infrastructure/Solana/CurveAccountReader.cs` | New — decode the curve account into `CurveState` |
| 16 | `…/Infrastructure/Reports/JsonReportWriter.cs` | New — the JSON summary |
| 17 | `…/Infrastructure/Reports/HtmlReportWriter.cs` | New — self-contained HTML summary |
| 18 | `…/Application/Sniper.cs` | New — the run, settlement and resume orchestration |
| 19 | `…/Application/CliOptions.cs` | New — commands `snipe`/`status` + options from §5 |
| 20 | `…/Application/ConsoleReport.cs` | New — ladder table, typed confirmation, summary |
| 21 | `tools/SpaceStroker.TokenSniper.Tests/**` | New — mirrored folders, §11 |
| 22 | `tools/.gitignore` | ⚠️ Add `reports/` and `snipe-receipts*.json` (§4.4) |
| 23 | `tools/secrets.example.json` | Add `snipe` section + `alias`/`PrivateKey`/`SupplyPercentage` placeholders |
| 24 | `tools/README.md` | Add the sniper's section: preconditions, dry-run-first workflow, exit codes |
| 25 | `specs/ghost-wallet-snipe.md` | This spec |
| 26 | `INDEX.md` | Map row + "Feature currently in flight" |

---

## 11. Unit tests

Project: `monkify-meta/tools/SpaceStroker.TokenSniper.Tests` (xUnit + Shouldly, mirroring
`SpaceStroker.WalletFunder.Tests`). No network, no fake RPC.

| Test file | Test cases | Expected |
| --- | --- | --- |
| `Domain/SnipePoolTests.cs` | Parse an entry carrying the funder's `Label`/`SOLAmount`; missing/blank `alias`; duplicate alias; malformed JSON | Unknown fields ignored (AC18); blank alias surfaces as an error, **not** a synthesised `W01`; duplicates detected; parse errors are actionable |
| `Domain/BondingCurveTests.cs` | Forward quote; inverse quote; round-trip forward∘inverse; rounding direction; `Complete`/zero-reserve graduation | Integer-exact costs, cost rounds up, tokens round down, graduation detected in all three forms |
| `Domain/SnipePlanTests.cs` | Target from percent + supply (AC8: `0.4504` ⇒ 0.4504%); shortfall when partially held (AC10); zero shortfall ⇒ `Skipped`; unaffordable target ⇒ `Short` at the affordable maximum (AC9); `maxSolCost` = quote × (1+tolerance) capped at spendable; sequential ladder pricing | Exact base-unit and lamport values; the ladder's later wallets are quoted higher than the first |
| `Domain/BuyScheduleTests.cs` | Order is alias-ascending for a shuffled input; offsets ascend; `window == 0` ⇒ all zero; seeded `Random` reproducibility | Deterministic order (AC7), never shuffled |
| `Domain/SnipePreflightTests.cs` | Blank/duplicate alias, invalid + non-32-byte address, mismatched keypair (AC16), non-positive percent, targets exceeding sellable supply (AC15), graduated curve (AC5), empty pool (AC4), padded/unpadded alias mix (AC19) | Every failure listed in one result (AC3); the alias-padding case is a **warning**, not an error; empty pool is `IsEmpty`, not an error |
| `Domain/RunLedgerTests.cs` | Status transitions; totals across mixed outcomes; exit-code derivation (AC20) | `0` only when every targeted wallet filled, dry run, or empty pool |
| `Infrastructure/SecretsFileTests.cs` | `REPLACE_ME` placeholders; missing `snipe` section; bad tolerance/percent types; a pool shared with the funder's fields | Fails fast with an actionable message before any RPC call |
| `Infrastructure/ReceiptsStoreTests.cs` | Write-after-every-change; reload a partial run; corrupt file | State survives a crash; corruption is reported, not swallowed |
| `Infrastructure/PumpFunProgramTests.cs` | Discriminator equals `SHA256("global:buy")[0..8]`; PDA derivations; instruction data byte layout; account count/order/flags against a **recorded fixture** of a real buy | Byte-for-byte match with the fixture (this is what keeps §6.4 honest) |
| `Infrastructure/Reports/JsonReportWriterTests.cs` | Full field set per wallet + totals; a run with short/failed/skipped wallets; **no private key or RPC key anywhere in the output** (AC17) | Serialized shape matches §6.7 |
| `Infrastructure/Reports/HtmlReportWriterTests.cs` | Self-containment (no `http(s)://` asset references); every wallet row present; totals present; no secrets (AC17) | AC15/AC16 satisfied from the same ledger |
| `Application/ConsoleReportTests.cs` | Ladder table formatting; invariant number formatting under a `pt-BR` culture; typed-total match/mismatch (AC2) | A comma decimal never passes the confirmation |

---

## 12. Code-exploration findings

1. ⚠️ **`tools/.gitignore` does not cover this tool's artifacts.** It ignores `secrets*.json`,
   `receipts*.json`, `**/bin/`, `**/obj/` — so `tools/reports/` (wallet addresses, holdings,
   signatures) is committable, and a receipts file named `snipe-receipts.json` **does not match**
   `receipts*.json` because the glob is anchored at the filename start. §4.4 adds both rules. The
   `monkify-meta` root `.gitignore` ignores only `sales/pdf/` and will not save you.
2. ⚠️ **`WalletPool.FromEntries` synthesises a label** (`W{index:00}`) when `Label` is blank. Copying
   that pattern for `alias` would silently invent the ordering key; §6.1 makes it an error instead.
3. **The funder's pool tolerance is already in place** — `WalletPool.JsonOpts` deliberately leaves
   `UnmappedMemberHandling` at the default *with a comment naming card #63*, so the shared file
   (AC18) works today without touching the funder.
4. ✅ **RESOLVED — `ComputeBudgetProgram` does NOT exist in Solnet.Programs 6.1.0.** Verified against
   the package; `monkify-backend` never sets a priority fee, so there was no helper to borrow. Both
   instructions are hand-built against `ComputeBudget111111111111111111111111111111`:
   `[2, u32 units]` = SetComputeUnitLimit, `[3, u64 microLamports]` = SetComputeUnitPrice.
5. **Transaction assembly precedent:** `SolanaService.TransferTokens`
   (`monkify-backend/src/SpaceStroker.Infrastructure/Services/Solana/SolanaService.cs:386-424`) is the
   in-house pattern — `TransactionBuilder` + `SetRecentBlockHash` + `SetFeePayer` + `Build(accounts)`,
   with Polly retry around `SendTransactionAsync`. Its **memo workaround does not apply here**: that
   exists because Orbit sends identical stake transfers from one signer, whereas each ghost wallet
   signs and pays for its own distinct transaction.
6. **`new PublicKey(string)` is not validation** (funder finding, still true) — preflight must use
   `PublicKey.IsValid` **plus** a 32-byte decode assertion. A truncated address is the irreversible
   failure mode.
7. **Invariant formatting is mandatory** for any number echoed or compared (the typed confirmation).
   A `pt-BR` machine renders `2.54` as `2,54`; the funder hit exactly this.
8. **The .NET 10 SDK writes `.slnx`.** `dotnet sln add` against the existing classic `.sln` is fine;
   do not let a tool rewrite the solution format.
9. **`Solnet.Programs` is not currently referenced by the funder** but *is* used by
   `monkify-backend` at 6.1.0 — keep all three Solnet packages in step at 6.1.0 to avoid the
   BouncyCastle ambiguity recorded in the funder's spec §12.

---

## 13. Acceptance criteria

Copied 1:1 from the Trello **Acceptance Criteria** checklist (21 items).

1. Given a pool file whose entries are valid, when `snipe` runs without `--execute`, then the full buy
   ladder (alias order, target %, quoted SOL cost, quoted price impact, scheduled offset) is printed
   and no transaction is submitted.
2. Given a reviewed dry run, when `snipe --execute` is invoked, then the operator must type the exact
   quoted total SOL before anything is submitted, and any mismatch (including a comma decimal) exits
   non-zero with nothing spent.
3. Given a pool containing a blank or duplicated `alias`, a duplicated or non-32-byte `PublicKey`, a
   `PrivateKey` that does not derive its `PublicKey`, or percentages summing beyond what the curve can
   sell, when preflight runs, then the run aborts before the first buy and reports every offending
   entry at once, not just the first.
4. Given an empty pool, when `snipe` runs, then it is a no-op with a clear message and exit code 0, and
   no RPC call that could spend anything is made.
5. Given the configured CA has already graduated from the bonding curve, when preflight runs, then the
   run refuses to start, names graduation as the reason, spends nothing and exits non-zero.
6. Given the token graduates part-way through a run, when the next wallet's buy is attempted, then all
   remaining wallets are skipped and the report names the last wallet bought plus the unfilled share
   of the target.
7. Given a pool of N wallets, when a run executes, then exactly one buy transaction is submitted per
   wallet, in ascending `alias` order, at randomized offsets across the configured window (default 60
   minutes).
8. Given a wallet whose `SupplyPercentage` is 0.4504, when its target is computed, then it resolves to
   0.4504% of the mint's on-chain total supply read at preflight, not 45.04% and not a share of any
   other base.
9. Given a wallet holds less SOL than its target requires, when its buy executes, then it buys the
   maximum its balance allows after transaction fee, ATA rent and priority-fee headroom, is reported
   short in both % and SOL, and the run continues with the next wallet.
10. Given a wallet already holds part of its target (a resumed run), when the run executes, then it
    buys only the shortfall computed from that wallet's on-chain token balance and never re-buys the
    full target.
11. Given a buy confirmed on-chain but the process was killed before its receipt was written, when the
    run is resumed, then the wallet's on-chain balance is treated as the truth and no second buy is
    submitted for the amount already filled.
12. Given the RPC times out or drops the response after a transaction was submitted, when the tool
    retries, then it first re-reads the wallet's token balance and only resubmits if the buy did not
    land.
13. Given the price moves beyond the configured slippage tolerance between quote and execution, when
    the transaction lands, then it reverts on `maxSolCost` with no partial overspend, the wallet is
    reported failed, and the remaining wallets still execute.
14. Given a wallet has no associated token account for the mint, when its buy executes, then the ATA is
    created in the same transaction and its rent was already accounted for in the preflight balance
    check.
15. Given a run completes, when the artifacts are written, then `tools/reports/<timestamp>/` holds an
    HTML summary and a JSON file, each listing per wallet: alias, full public address, target %,
    achieved %, tokens received, SOL spent, average entry price, transaction signature and status.
16. Given a run completes, when the artifacts are written, then both report the run totals: the pool's
    total share of supply held, total SOL spent, blended average entry price, the buy timeline, and
    every wallet that fell short with its reason.
17. Given any run, when console output, receipts and both reports are inspected, then no private key,
    keyed RPC URL or other secrets-file value appears in any of them — public addresses only.
18. Given the pool file carries the funder's fields (`Label`, `PublicKey`, `SOLAmount`) plus the
    sniper's (`alias`, `PrivateKey`, `SupplyPercentage`), when either tool loads it, then each reads
    its own fields and ignores the other's without failing.
19. Given the pool mixes zero-padded and unpadded aliases (`W2` and `W10`), when preflight runs, then
    it warns that ordinal sorting will not match numeric intent, and the executed order matches the
    printed ladder exactly.
20. Given a completed run, when the process exits, then the exit code is 0 only if every wallet reached
    its target (or the run was a dry run / empty pool), and non-zero if any wallet is short, failed or
    unresolved.
21. Given the project is built, when `find . -name "*.cs"` is run, then the tool's files sit under
    `Domain/`, `Infrastructure/<external system>/` and `Application/` with matching namespaces and a
    mirrored test project, and every `Domain/` rule (curve math, target/shortfall, schedule, preflight)
    is unit-tested with no network.

---

## 14. Manual QA (operator, against a real RPC)

The agent stops at step 3. Steps 4+ spend real SOL and are the operator's.

1. `dotnet test tools/SpaceStroker.Tools.sln` — both projects green.
2. `dotnet run --project tools/SpaceStroker.TokenSniper -- status` against a **live token** you do not
   intend to buy, with the ghost pool loaded: confirms RPC access, supply read, curve decode and the
   per-wallet holding view. Structurally cannot submit.
3. `… -- snipe` (dry run) — read the ladder. Check that the quoted cost **rises down the ladder**
   (sequential quoting, §6.2); a flat ladder means the reserves are not being advanced per wallet.
4. **Devnet rehearsal** (recommended before mainnet): a devnet pump.fun-style curve if one is
   available, or at minimum a single-wallet mainnet buy with a tiny `SupplyPercentage` to prove the
   account list (§6.4) is right. **This is the step that validates the one part of the spec that
   cannot be unit-tested against reality.**
5. Kill the process mid-window and re-run: the filled wallets must come back `Skipped`, and the total
   spent must not double.
6. Full run with `--execute` and the typed total.
7. Open `tools/reports/<timestamp>/snipe-report.html` — verify achieved vs target, the pool's total
   share of supply, and that **no key material appears anywhere** in either artifact.

---

## 15. Trello / pipeline

- **Source card:** [#63 Create ghost wallet snipe app](https://trello.com/c/nokWHm7z) — list
  **Specs created**; the **Implementation** section on the card carries branch, worktree, spec and
  level.
- **Next:** `/monkify-implement-feature Create ghost wallet snipe app`, then
  `/monkify-ship-feature Create ghost wallet snipe app` (which writes
  `features/ghost-wallet-snipe.md`, required by **Level: `Feature`**).
- **Sibling:** [`coinbase-wallet-funding.md`](coinbase-wallet-funding.md) — the funding half, shipped
  2026-08-11; same solution, same pool file, same money-gate philosophy.

---

## 16. Revision log

| Date | Change |
| --- | --- |
| 2026-08-11 | Initial spec from the refined card (#63), after a from-scratch re-refinement. |
| 2026-08-11 | **Implemented** on `feature/ghost-wallet-snipe`. §6.2 and §6.4 rewritten with the protocol facts read off mainnet (the IDL + live buys) — the pre-implementation guesses they replace were wrong in five ways: the curve's quote-side fields are `*_quote_reserves` (not `*_sol_reserves`) and it now also carries `is_mayhem_mode`, `is_cashback_coin` and a **`quote_mint` that need not be SOL**; `buy` takes a **third arg** (`track_volume`); live buys carry **18 accounts against the IDL's 16**; the **token program varies per mint** (SPL Token *and* Token-2022 are both live), so ATAs must be derived by hand; and the **fee rates are `global`-account config** (95 + 5 bps), not constants. §12 finding 4 resolved: Solnet.Programs 6.1.0 has **no** `ComputeBudgetProgram`, so the priority-fee instructions are hand-built. 118 unit tests, the funder's 79 untouched and still green. |

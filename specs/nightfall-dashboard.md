# SpaceStroker — "Nightfall dashboard" — Implementation Spec

> **Status: Shipped** — merged via `feature/nightfall-dashboard` on **2026-08-11**
> (`monkify-meta` only). Refined + specced from Trello card
> [Nightfall dashboard #166](https://trello.com/c/mUDhAdBu) (2026-08-11).
> Repos affected: **`monkify-meta` only** (a new project in the existing `tools/` solution + the
> knowledge base). No `monkify-backend`, no `monkify-front`, no `monkify-lp` change.
> Distilled in [`features/nightfall-dashboard.md`](../features/nightfall-dashboard.md).
>
> **Post-ship notes — what shipped differs from this spec in one significant way**, recorded in §15:
> **§6.4 step 7 and §12 finding 1's primary path did not survive contact with Solnet 6.1.0.** The
> swap is **not** assembled locally from Jupiter's instructions — that version has no versioned
> transaction or address-lookup-table support at all. Jupiter's `/swap` supplies the ready v0 wire
> transaction and `TransactionSigner` signs it without parsing, while `/swap-instructions` is still
> fetched purely to feed `SwapInstructionsGuard`. The "assert what we sign" property of decision #6
> is preserved; the assembly step is not. Three implementation-time bugs (a no-op keypair check, a
> batch cancelled by the request token, a semaphore disposed mid-batch) are also logged in §15.
>
> ⚠️ **Seven acceptance criteria have never run against a wallet holding tokens** — AC07, AC14, AC15,
> AC19, AC23, AC24, AC26 each need a real mainnet sale, which is an **operator** step (§14).
>
> **Purpose:** the ghost-wallet pool has a **buy** side ([#63](https://trello.com/c/nokWHm7z)) and a
> **funding** side ([#165](https://trello.com/c/9OoHAuEa), shipped) but **no exit**. Today, seeing
> what each wallet holds and selling it means opening a wallet app per wallet — slow, error-prone,
> and impossible to do concurrently. **Nightfall** is a local operator dashboard over that same pool:
> every wallet's alias, SOL and token position at a glance, and a sell of one wallet or many
> concurrently behind a confirmation.
>
> ⚠️ **This tool sells real holdings and the sale cannot be undone.** Everything below that looks
> like ceremony — `SIMULATE` defaulting on, the confirmation dialog, the price-impact abort, the
> low-SOL exclusion, click-never-sells — exists because there is no recovery from a bad batch. Do not
> simplify them away.

---

## 1. Summary

One new app, **`spacestroker-nightfall`**, joining the existing `monkify-meta/tools/` solution beside
`SpaceStroker.WalletFunder`. It is a **localhost-only ASP.NET host** that serves a static dashboard
and does all key handling, quoting, signing and broadcasting **in-process**.

| Stage | What happens |
| --- | --- |
| **Load** | Read the git-ignored `tools/secrets.nightfall.json` — Solana RPC + Jupiter options + sell options + the Token CA + the wallet pool (`Alias`, `PublicKey`, `PrivateKey`). Unknown JSON fields (e.g. #63's `SupplyPercentage`) are **ignored**. |
| **Serve** | Bind `http://127.0.0.1:<port>`, serve `wwwroot/`, open the browser. The browser receives **only public data** — never a private key. |
| **Observe** | Poll the pool on an interval: SOL balances in one `getMultipleAccounts`, token balances in a second over derived ATAs, mint supply once. |
| **Select** | Click toggles, ctrl/cmd-click adds, shift-click ranges, drag-marquee selects a block, Select all / Clear. **Clicking never sells.** |
| **Quote** | Hovering a wallet asks Jupiter for a live quote of the current `SELL %` — SOL out, price impact, exact token amount. Debounced, cached, coalesced. |
| **Confirm** | `SELL SELECTED` opens a yes/no dialog listing each wallet's amount and the batch's quoted SOL total. `SIMULATE` (the startup default) stops here. |
| **Sell** | Per wallet: fresh balance read → quote → price-impact gate → Jupiter swap instructions → **assert what we are about to sign** → sign locally → broadcast + confirm through **our own RPC**. Concurrency capped. |
| **Settle** | Realized SOL is read from the confirmed transaction's balance deltas. A 100% sell then closes the emptied token account in a **separate follow-up transaction**. |
| **Record** | `tools/nightfall-receipts.json` is rewritten after **every** state change. It is the audit trail. |

### Before / after

| Aspect | Today | After |
| --- | --- | --- |
| Seeing the pool's position | One wallet app per wallet | One screen, auto-refreshing |
| Exiting the pool | Manual swap per wallet | Select many, one confirmation, concurrent |
| Knowing what a sale pays | Guess, or do it and see | Live per-wallet quote on hover, batch total on confirm |
| Selling into thin liquidity | Discovered afterwards | Per-wallet price-impact abort before broadcast |
| A wallet with no fee SOL | Transaction fails on-chain | Flagged and excluded before you spend a confirmation |
| Rehearsing a batch | Impossible | `SIMULATE` builds everything and broadcasts nothing |
| Record of an exit | None | `nightfall-receipts.json` — quoted vs realized SOL, signatures |

### What this is not

Nightfall does **not** buy (that is #63), does **not** fund (that is #165), and has **no automation**
— nothing sells without a human clicking a confirmation. There are no limit orders, no schedules and
no price triggers.

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo (§2):** all work lives in `.worktrees/nightfall-dashboard/monkify-meta`; the
  `monkify-meta` hub stays clean on `main`. Branch `feature/nightfall-dashboard` is pushed to
  `origin`.
- **Secrets — mock only in committed files (§1).** `tools/secrets.nightfall.example.json` carries
  `REPLACE_ME` placeholders only. `tools/secrets.nightfall.json` and `tools/nightfall-receipts.json`
  must be git-ignored — **this requires editing `tools/.gitignore`; today's rules do not cover
  either file correctly** (see §4.4, finding 2). A wallet **private key** must never be printed,
  logged, written to receipts, returned over HTTP, or included in an error message.
- **No production queries / no live external API calls during implementation (§5).** Real sells are
  an **operator step** the user performs. This card delivers code + docs; the live sequence is §14.
- **Backend:** unchanged ⇒ **no `SpaceStroker.Tests` change** and **no integration test**. The card's
  `## Level` is **`Feature`**, and the mandatory happy-path integration test in
  `SpaceStroker.Tests.Integration` is **explicitly waived on the card** — the tool has zero backend
  surface, so the Testcontainers suite (which boots the API + Consumers against Postgres/Redis) has
  no code path to drive. Waiver precedent: `specs/coinbase-wallet-funding.md`,
  `specs/main-production-operator.md`. The **`features/` doc is still required** at ship time.
- **Unit tests are required** for the pure, network-free logic — sell-plan math, the price-impact and
  low-SOL gates, the instruction guard, error classification, receipts, and the realized-SOL parse
  (§11). There is **no fake Jupiter and no fake RPC sender**, matching #165's decision.
- **Layering (`conventions/backend.md` → "The folder tree must show the architecture"):**
  **Domain / Infrastructure / Application** folders with matching namespaces, mirrored in the test
  project. `Domain/` is **IO-free** — that is what makes the money-safety rules testable without a
  fake sender.
- **Knowledge base stays current:** `tools/README.md`, `INDEX.md`, and a new
  `features/nightfall-dashboard.md` at ship time.
- **Feature-specific:**
  - The host binds **`127.0.0.1` only**. No authentication is added and none is needed *because* of
    that bind — if the bind ever widens, the tool becomes a remote wallet-drainer.
  - Nothing sells while `SIMULATE` is on, regardless of what the browser sends.
  - `mainnet-beta` only; the token mint comes from config and is never taken from the browser.

---

## 3. Resolved product decisions

Carried from the refined card (answer mode: **short**). Items 22–29 are spec-level additions made
while exploring the code; they are called out as such.

| # | Decision |
| --- | --- |
| 1 | **Shape:** a **localhost-only ASP.NET host + browser UI**, not a pure static frontend. A browser that can sell must hold the pool's private keys; a static app puts them in the served bundle and in devtools. The host keeps keys in-process and the browser only ever receives public data and sell commands. |
| 2 | **Location:** a new **`SpaceStroker.Nightfall`** project joining the existing `tools/SpaceStroker.Tools.sln`, sibling of `SpaceStroker.WalletFunder`. Same Domain/Infrastructure/Application layout, same `tools/.gitignore`, same Solnet dependency. |
| 3 | **UI stack:** **static HTML/CSS/JS from `wwwroot/`, no build step.** `tools/` stays a pure .NET solution and `dotnet run` remains the only command. One screen of cards + a marquee + a hover panel is well within plain JS. |
| 4 | **Config:** its **own** git-ignored `tools/secrets.nightfall.json` with a tracked `secrets.nightfall.example.json` template. Not shared with `tools/secrets.json`, which holds live Coinbase **withdrawal** credentials — a different blast radius and a different set of wallet fields. |
| 5 | **Pool format:** binds `Alias` / `PublicKey` / `PrivateKey` and **ignores unknown fields**, so #63's ghost-wallet pool file pastes in verbatim (`SupplyPercentage` and all) — the same rule that lets #165 read it. |
| 6 | **Swap construction:** **Jupiter** builds the swap. Nightfall calls Jupiter for a **quote** (SOL out + price impact) and for **swap instructions**, assembles and signs locally, and broadcasts through **our own keyed RPC**. Jupiter never sees a private key and never touches the chain. Chosen over hand-rolling pump.fun/PumpSwap, which would need two paths and would break the day the token migrates; chosen over Jupiter's prebuilt `/swap` transaction so the tool can **assert instruction-by-instruction what it signs** (§6.5). |
| 7 | **Transport:** all reads (SOL, token balances, mint supply) and all broadcasting/confirmation go through `solana.rpcUrl` — the operator's keyed endpoint, the same one `GeneralSettings:Token:ClusterUrl` carries (`specs/solana-rpc-api-key.md`). **Mainnet only, hard-coded.** |
| 8 | **Sell amount:** one **`SELL %`** field on the sell action; **blank ⇒ 100%**. The percentage applies to **each selected wallet's own token balance**, not to total mint supply — a wallet holding 2% of supply at `40` sells 0.8% of supply. Chosen over per-wallet inputs (too many fields to fat-finger at spend time) and over % of mint supply (needs allocation rules and makes a wallet's share depend on what else is selected). |
| 9 | **Concurrency:** all selected wallets sell **concurrently**, capped at `maxConcurrentSells` (default 4). Jupiter and the RPC both rate-limit; an unbounded burst becomes 429 failures. Each wallet is an independent transaction — one failure never cancels the others. |
| 10 | **Price protection:** `slippageBps` (default 300 = 3%) plus `maxPriceImpactPercent` (default 10). A wallet whose quote is worse than the cap **refuses to broadcast**, per wallet, without cancelling the batch. Chosen over slippage alone, which still lets a wallet dump into near-zero liquidity at a terrible-but-tolerated price. |
| 11 | **Account close:** on a **100% sell only**, once the sell is confirmed, a **separate follow-up transaction** closes the emptied token account and reclaims its rent. Chosen over appending `closeAccount` to the swap: an appended close that fails takes the entire sale down with it, whereas a failed follow-up costs only the rent. Partial sells leave the account alone. |
| 12 | **`SIMULATE` toggle, ON at startup.** Quotes and builds the real transaction but does not broadcast; switching to `LIVE` is deliberate. Mirrors `spacestroker-fund-wallets` being dry-by-default and lets a drag-select be rehearsed at zero risk. |
| 13 | **Click selects; an explicit `SELL SELECTED` action deals.** Chosen over the card's literal "click = sell" because click would otherwise mean two different things depending on whether the mouse moved, and a stray click would open a live sell dialog for real money. One code path for 1 or N wallets. |
| 14 | **Confirmation:** a **yes/no dialog for both single and batch** — as written on the card — showing per-wallet token amounts and the batch's quoted SOL total. No typed confirmation. ⚠️ **Knowingly accepted:** unlike `spacestroker-fund-wallets`, nothing but the dialog catches a bad drag-select. That is precisely why `SIMULATE` defaults on. |
| 15 | **Low SOL:** a wallet below `minSolForFeeSol` is flagged, **excluded** from `SELL SELECTED` with the reason named, and pointed at `spacestroker-fund-wallets` (#165) to top up. Chosen over letting it try and fail on-chain. |
| 16 | **Failure policy:** retryable errors (429, transport, expired blockhash) retry with exponential backoff + jitter; terminal errors (slippage exceeded, no route, insufficient balance, price-impact abort) are surfaced with the reason. **The batch never stops**, and a failed wallet is simply re-selectable. |
| 17 | **Card face:** alias · SOL balance · **% of total mint supply** · raw token amount · a compact **estimated** SOL value, so the pool is scannable without hovering every card. |
| 18 | **Hover:** the **live Jupiter quote for the current `SELL %`** — SOL out, price impact, exact token amount — and, once that wallet has sold, the **realized SOL** and the transaction signature. |
| 19 | **Refresh:** auto-poll on a configurable interval (default 15s) **plus** a manual refresh, plus an immediate refresh of any wallet right after its sell confirms. |
| 20 | **Records:** `tools/nightfall-receipts.json`, rewritten **after every state change** (never only at the end): per wallet the token amount sold, **quoted vs realized SOL**, signature, status and timestamp. Signatures are copyable in the UI. **No HTML report** — that is #63's artifact. |
| 21 | **Level: `Feature`** — `features/nightfall-dashboard.md` required at ship time; backend integration test **waived** (no backend surface). Mockups: **3 Claude Design dashboard mockups at implementation time**, approved before any UI code is written. |
| 22 | **(Spec addition)** The **estimated** SOL value on the card face is `tokenAmount × unitPriceSol`, where `unitPriceSol` comes from **one** Jupiter quote per refresh cycle against a reference amount — **not** a quote per wallet. Rationale: a per-wallet quote every 15s across a 20-wallet pool would exhaust Jupiter's rate limit within minutes. The card face is explicitly labelled an estimate (it ignores per-wallet price impact); the **hover** quote is the real one and the confirm dialog uses real quotes. |
| 23 | **(Spec addition)** Quotes are **debounced (300 ms), cached (`quoteCacheSeconds`, default 10) and coalesced** (one in-flight request per cache key). Rationale: hover fires on every mouse move; without this the dashboard rate-limits itself out of Jupiter on idle mouse drift. |
| 24 | **(Spec addition)** The sell amount is **recomputed from a fresh balance read at execution**, not from the polled value the browser last saw. Rationale: a 100% sell must sell exactly what the wallet holds at that moment, and the poll is up to `refreshIntervalSeconds` stale. |
| 25 | **(Spec addition)** Before signing, the assembled instruction set is checked by a pure **`SwapInstructionsGuard`** (§6.5): every program id is on an allowlist, the swap's authority **is** this wallet, the input mint and amount match the plan, and there is no `SetAuthority` or unexpected `CloseAccount`. Rationale: decision #6 chose swap-*instructions* over a prebuilt transaction specifically so the tool can assert what it signs — the guard is the part that makes that choice worth its cost, and it is IO-free so it is unit-testable. |
| 26 | **(Spec addition)** A wallet already in flight **cannot be re-submitted**; the host tracks in-flight wallets and rejects a duplicate `POST /api/sell` for one. Rationale: the browser can double-fire, and a double sell is a real second sale, not a no-op. |
| 27 | **(Spec addition)** Live progress reaches the browser over **Server-Sent Events** (`text/event-stream`), not polling. Rationale: dependency-free in both directions and it keeps a running batch honest on screen; a WebSocket would add a library for one-way traffic. |
| 28 | **(Spec addition)** Wallets holding **zero** of the token are excluded from a sell and never quoted. Rationale: Jupiter has no route for a zero amount, so quoting them burns rate limit to produce an error. |
| 29 | **(Spec addition)** Realized SOL is derived from the **confirmed transaction's own balance deltas** (`postBalances - preBalances + fee` for the wallet), not from the quote. Rationale: the quote is what we were promised; the deltas are what we got, and the gap between them is the number an operator actually wants after a concurrent batch. |

---

## 4. Solution layout and configuration

There is **no application data model change** — no entity, no EF mapping, no migration, no endpoint
on `SpaceStroker.Api`. This section replaces §4 of the template with the file layout and config model.

### 4.1 New project — `tools/SpaceStroker.Nightfall/`

```
tools/
├── SpaceStroker.Tools.sln                      # EXISTING — add both new projects
├── .gitignore                                  # EXISTING — MUST be edited, see §4.4
├── README.md                                   # EXISTING — add a spacestroker-nightfall section
├── secrets.example.json                        # EXISTING — the funder's template, untouched
├── secrets.nightfall.example.json              # NEW — tracked; placeholders only
├── SpaceStroker.WalletFunder/                  # EXISTING — untouched
├── SpaceStroker.WalletFunder.Tests/            # EXISTING — untouched
├── SpaceStroker.Nightfall/
│   ├── SpaceStroker.Nightfall.csproj           # Sdk="Microsoft.NET.Sdk.Web" (static files + minimal API)
│   ├── Program.cs                              # entry point: parse args → load secrets → build host → run
│   ├── Domain/                                 # pure; no IO, no HTTP, no filesystem
│   │   ├── WalletPool.cs                       # PoolEntry (Alias/PublicKey/PrivateKey), parsing, unknown fields ignored
│   │   ├── TokenAmount.cs                      # raw base units ⇄ UI amount, exact decimal math (never double)
│   │   ├── SellPlan.cs                         # SELL % → per-wallet raw amount; blank ⇒ 100%
│   │   ├── SellPreflight.cs                    # zero-balance, low-SOL and price-impact gates (values in, not clients)
│   │   ├── SwapInstructionsGuard.cs            # asserts the instruction set before signing (§6.5)
│   │   ├── SellError.cs                        # Retryable vs Terminal classification + reason text
│   │   ├── RealizedSol.cs                      # balance-delta parse: post - pre + fee
│   │   ├── SellReceipt.cs                      # SellReceipt + ReceiptsDocument + state transitions
│   │   └── NightfallException.cs               # NightfallException + UsageException
│   ├── Infrastructure/                         # everything that touches the outside world
│   │   ├── SecretsFile.cs                      # config model + Load() with placeholder detection
│   │   ├── ReceiptsStore.cs                    # persistence; save after every change
│   │   ├── Jupiter/
│   │   │   ├── JupiterClient.cs                # quote + swap-instructions, retry policy, rate-limit aware
│   │   │   ├── JupiterQuote.cs                 # wire DTOs (quote response is echoed back verbatim)
│   │   │   └── QuoteCache.cs                   # TTL cache + in-flight coalescing (decision #23)
│   │   └── Solana/
│   │       ├── SolanaReader.cs                 # batched SOL + token balances, mint supply
│   │       ├── SolanaSender.cs                 # assemble, sign, sendTransaction, confirm, read realized SOL
│   │       └── TokenAccountCloser.cs           # the follow-up closeAccount transaction
│   ├── Application/
│   │   ├── CliOptions.cs                       # argument parsing + usage banner
│   │   ├── NightfallState.cs                   # the in-memory pool snapshot + SIMULATE/LIVE mode + in-flight set
│   │   ├── SellRunner.cs                       # the batch: gate → quote → guard → sign → send → settle → close
│   │   ├── PollLoop.cs                         # background refresh on refreshIntervalSeconds
│   │   ├── ApiEndpoints.cs                     # the localhost HTTP surface (§7.1) — public data only
│   │   └── ConsoleReport.cs                    # startup banner, mode changes, per-sell console lines
│   └── wwwroot/                                # the static dashboard — NO build step
│       ├── index.html
│       ├── app.js                              # state, selection/marquee, hover quotes, SSE, confirm dialog
│       └── app.css                             # built to the approved Claude Design mockup
└── SpaceStroker.Nightfall.Tests/               # mirrors the same folders
    ├── SpaceStroker.Nightfall.Tests.csproj
    ├── Domain/
    │   ├── WalletPoolTests.cs
    │   ├── SellPlanTests.cs
    │   ├── SellPreflightTests.cs
    │   ├── SwapInstructionsGuardTests.cs
    │   ├── SellErrorTests.cs
    │   ├── RealizedSolTests.cs
    │   └── SellReceiptTests.cs
    ├── Infrastructure/
    │   ├── SecretsFileTests.cs
    │   ├── ReceiptsStoreTests.cs
    │   └── QuoteCacheTests.cs
    └── Application/
        └── NightfallStateTests.cs
```

Namespaces follow the folders exactly: `SpaceStroker.Nightfall.Domain`, `….Infrastructure`,
`….Infrastructure.Jupiter`, `….Infrastructure.Solana`, `….Application`.

### 4.2 Project files

`SpaceStroker.Nightfall.csproj` — note the **Web SDK**, which is what `SpaceStroker.WalletFunder`
does *not* use. It is what provides static-file serving and the minimal-API host:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>SpaceStroker.Nightfall</RootNamespace>
    <AssemblyName>spacestroker-nightfall</AssemblyName>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>

  <ItemGroup>
    <!--
      Same pinning discipline as SpaceStroker.WalletFunder: Portable.BouncyCastle 1.9.0 is what
      Solnet.Wallet drags in transitively, and putting BouncyCastle.Cryptography 2.4.0 on the graph
      alongside it makes every Ed25519* type ambiguous (CS0433).
    -->
    <PackageReference Include="Portable.BouncyCastle" Version="1.9.0" />
    <PackageReference Include="Solnet.Programs" Version="6.1.0" />
    <PackageReference Include="Solnet.Rpc" Version="6.1.0" />
    <PackageReference Include="Solnet.Wallet" Version="6.1.0" />
  </ItemGroup>

</Project>
```

`SpaceStroker.Nightfall.Tests.csproj` — copy `SpaceStroker.WalletFunder.Tests.csproj` verbatim
(xUnit 2.4.2 + Shouldly 4.2.1 + Microsoft.NET.Test.Sdk 17.6.0), repointing the `ProjectReference`.

Add both projects to `tools/SpaceStroker.Tools.sln`. ⚠️ **The .NET 10 SDK writes `.slnx`** — use
`dotnet sln tools/SpaceStroker.Tools.sln add …`, which appends to the existing classic-format file
rather than creating a new one.

### 4.3 `secrets.nightfall.json` model (`SecretsFile.cs`)

Mirrors `SpaceStroker.WalletFunder/Infrastructure/SecretsFile.cs` — same
`JsonPropertyName` style, same `Load()` with fail-fast placeholder detection, same
`WalletPool.JsonOpts` (case-insensitive, comments skipped, trailing commas allowed, **unknown members
ignored**).

```jsonc
{
  "solana": {
    "rpcUrl": "https://mainnet.helius-rpc.com/?api-key=REPLACE_ME_RPC_KEY"
  },
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
  },
  "wallets": [
    { "Alias": "DEV Wallet", "PublicKey": "REPLACE_ME_BASE58_ADDRESS", "PrivateKey": "REPLACE_ME_BASE58_PRIVATE_KEY" }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `solana.rpcUrl` | The keyed endpoint. Used for **every** read and every broadcast. **Masked to host-only wherever logged** (`specs/solana-rpc-api-key.md` set this rule). |
| `token.mint` | The Token CA. Config-only — the browser never supplies it. |
| `jupiter.apiBaseUrl` | Base for `/quote` and `/swap-instructions`. ⚠️ Jupiter moved off `quote-api.jup.ag/v6` to `lite-api.jup.ag/swap/v1` (free tier) and `api.jup.ag/swap/v1` (keyed); the implementer **must confirm the current base against Jupiter's docs** before wiring, and the field exists so a move needs no code change. |
| `jupiter.slippageBps` | Applied to every sell. `300` = 3%. |
| `jupiter.maxPriceImpactPercent` | A quote worse than this **aborts that wallet before broadcasting**. |
| `jupiter.prioritizationFeeLamports` | Passed to swap-instructions; `0` disables. Same knob as the frontend's `sendToken` (`features/solana-connection-rpc.md`). |
| `jupiter.quoteCacheSeconds` | TTL of the hover-quote cache (decision #23). |
| `sell.maxConcurrentSells` | Max sells in flight at once. |
| `sell.minSolForFeeSol` | Below this a wallet is flagged and excluded. |
| `sell.refreshIntervalSeconds` | Auto-poll interval. |
| `sell.confirmTimeoutSeconds` | How long to wait for a transaction to reach `confirmed`. |
| `sell.closeEmptiedTokenAccounts` | Whether the follow-up `closeAccount` runs after a 100% sell. |
| `wallets[]` | `Alias` + `PublicKey` + `PrivateKey`. **Unknown fields are ignored**, so #63's pool file pastes in verbatim. |

`Load()` rejects a leftover `REPLACE_ME` in `solana.rpcUrl`, `token.mint`, and any wallet's
`PublicKey` / `PrivateKey`, and validates that every `PublicKey` decodes to a **32-byte** address
(`PublicKey.IsValid` **plus** a decode assertion — `new PublicKey(string)` is *not* validation; #165
learned this) and that each `PrivateKey` derives to its stated `PublicKey`. A mismatch is a
load-time error: signing with the wrong key produces an opaque on-chain failure at spend time.

### 4.4 `tools/.gitignore` — MUST be edited (two confirmed gaps)

The existing file is:

```gitignore
secrets*.json
!secrets.example.json
receipts*.json
```

Verified with `git check-ignore` against the planned filenames:

| File | Today | Required |
| --- | --- | --- |
| `secrets.nightfall.json` | ignored ✅ | ignored |
| `secrets.nightfall.example.json` | **ignored ❌** — the tracked template could never be committed | tracked |
| `nightfall-receipts.json` | **not ignored ❌** — `receipts*.json` anchors at the start, so the audit trail (addresses + signatures) is committable | ignored |

Add to `tools/.gitignore`:

```gitignore
# Nightfall (#166) — its receipts file does not start with "receipts", so the rule above misses it.
*receipts*.json

# ...and the tracked Nightfall template must survive the secrets*.json rule above.
!secrets.nightfall.example.json
```

Re-verify after editing:

```bash
cd tools
for f in secrets.nightfall.json secrets.nightfall.example.json nightfall-receipts.json; do
  git check-ignore -q "$f" && echo "$f IGNORED" || echo "$f TRACKED"
done
# expect: IGNORED, TRACKED, IGNORED
```

---

## 5. CLI surface

```
spacestroker-nightfall [options]

Options:
  --secrets <path>    secrets file (default: tools/secrets.nightfall.json)
  --receipts <path>   receipts file (default: tools/nightfall-receipts.json)
  --port <n>          localhost port (default: 5177)
  --no-open           do not launch the browser on start
  -h, --help          this message
```

`CliOptions.Parse` copies `SpaceStroker.WalletFunder/Application/CliOptions.cs` exactly, including
the `AppContext.BaseDirectory` + four `..` walk that defaults paths to the `tools/` folder, and
`UsageException` → **exit 2**.

There is **no `--execute` flag.** The `SIMULATE`/`LIVE` switch lives in the UI (decision #12): unlike
the funder, this tool is a session you sit in front of, and a launch-time flag would mean restarting
the host to change your mind mid-exit.

| Exit code | Meaning |
| --- | --- |
| `0` | Clean shutdown (Ctrl-C). |
| `1` | Fatal error — unreadable secrets, invalid pool, port in use, RPC unreachable at startup. |
| `2` | Usage error (unknown option, missing value). |

---

## 6. Behaviour

### 6.1 Startup

1. Parse args; `--help` prints and exits `0`.
2. `SecretsFile.Load` — fail fast on placeholders, invalid addresses, or a key/address mismatch.
3. An **empty pool** prints a clear message and **exits 0** without reaching any external service
   (matches the funder's empty-pool rule).
4. Read the mint once: `getTokenSupply(token.mint)` → total supply + `decimals`. Failure here is
   fatal — every percentage on screen depends on it.
5. Bind `http://127.0.0.1:<port>`, serve `wwwroot/`, start `PollLoop`, print the banner (URL, wallet
   count, token mint, **`MODE: SIMULATE`**, RPC host **masked**), and open the browser unless
   `--no-open`.

Mode starts at **SIMULATE** on every launch. It is never persisted — a fresh process is never
armed.

### 6.2 Reading the pool — `SolanaReader.cs`

Two RPC calls per refresh cycle, regardless of pool size:

1. **SOL:** `getMultipleAccounts(pool addresses)` → `lamports` per wallet. An account that does not
   exist reads as **0 lamports**, not an error (the backend's `SolBalanceFailed` predicate makes the
   same distinction).
2. **Token:** derive each wallet's ATA locally with
   `AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(owner, mint)` — pure PDA math, zero
   RPC, exactly as `SolanaService.GetTokenBalance` does — then one
   `getMultipleAccounts(atas, encoding: jsonParsed)`. A missing account is a **zero balance**, not an
   error (same lesson as the backend's `TokenBalanceFailed`).

Then, once per cycle, **one** Jupiter quote against a reference amount gives `unitPriceSol` for the
card-face estimate (decision #22).

Amounts are carried as **raw base units (`ulong`)** everywhere and converted for display with exact
decimal math — `TokenAmount.ToUiAmount` copies `SolanaService.ToUiAmount`'s loop and **never** routes
a money value through a `double`.

`supplyPercent = rawAmount / totalSupplyRaw × 100`, computed in decimal.

Retries reuse the backend's shape: Polly exponential backoff **+ jitter**, 429/5xx-aware, transport
failures only — a successful response carrying an unwelcome fact (an empty account) is **not**
retried.

### 6.3 Selection and the sell amount

Selection is entirely client-side (§7.2). The **amount** is server-side and pure:

```
SellPlan.AmountFor(rawBalance, percent):
    percent is null or blank  ⇒ rawBalance                       // 100%
    percent outside (0, 100]  ⇒ terminal: "SELL % must be > 0 and <= 100"
    otherwise                 ⇒ floor(rawBalance × percent / 100) // integer math, never double
    result == 0               ⇒ terminal: "the requested percentage rounds to zero tokens"
```

`floor` matters: rounding up would try to sell more than the wallet holds and fail on-chain.

### 6.4 The sell run — `SellRunner.cs`

Per selected wallet, independently, at most `maxConcurrentSells` at a time (`SemaphoreSlim`):

1. **Reject a duplicate.** If the wallet is already in `NightfallState`'s in-flight set, refuse
   (decision #26). Add it on entry, remove it in a `finally`.
2. **Fresh balance read** (decision #24) → `SellPlan.AmountFor`.
3. **Gates** — `SellPreflight`, all pure, all taking values rather than clients:
   - token balance `0` ⇒ terminal, excluded, never quoted (decision #28);
   - `solLamports < minSolForFeeSol` ⇒ terminal, "not enough SOL to pay the transaction fee — top it
     up with `spacestroker-fund-wallets`";
   - computed amount `0` ⇒ terminal.
4. **Quote** — `GET {apiBaseUrl}/quote?inputMint={token.mint}&outputMint=So111…11112&amount={raw}&slippageBps={slippageBps}&restrictIntermediateTokens=true`.
   No route ⇒ terminal.
5. **Price-impact gate** — `priceImpactPct × 100 > maxPriceImpactPercent` ⇒ **terminal, before
   anything is signed or sent.** The other wallets continue.
6. **Simulate stop.** If mode is `SIMULATE`, record the quote, emit `simulated`, and stop here. The
   check is **server-side**: a browser that posts a sell while the host is in SIMULATE gets nothing
   sold.
7. **Swap instructions** — `POST {apiBaseUrl}/swap-instructions` with the **verbatim quote response**
   plus `{ userPublicKey, wrapAndUnwrapSol: true, dynamicComputeUnitLimit: true, prioritizationFeeLamports }`.
   `wrapAndUnwrapSol` is what makes the proceeds land as **native SOL** in the wallet rather than as
   wrapped SOL in a new account.
8. **Guard** — `SwapInstructionsGuard.Assert(...)` (§6.5). A guard failure is **terminal** and is
   never retried: it means the instruction set is not what we asked for.
9. **Assemble + sign locally** — recent blockhash at `Commitment.Confirmed`, fee payer = the wallet,
   sign with `new Account(privateKey, publicKey)` (the pattern `SolanaService.TransferTokens` uses).
   See §12 finding 1 for the versioned-transaction/ALT decision.
10. **Broadcast** — `sendTransaction(bytes, skipPreflight: false, Commitment.Confirmed)`. The
    preflight commitment must match the blockhash commitment, or the node rejects a blockhash it has
    not seen — the exact lesson `specs/solana-connection-rpc.md` recorded on the frontend.
11. **Confirm** — poll `getSignatureStatuses` until `confirmed` or `confirmTimeoutSeconds`. Timeout is
    **retryable**, not terminal: the transaction may still land, so the receipt keeps the signature
    and the wallet is reported `Unconfirmed` rather than failed.
12. **Realized SOL** — `getTransaction(signature, Commitment.Confirmed)` → `RealizedSol.From(meta)`
    (§6.6).
13. **Close the emptied account** — only when the sell was **100%**, `closeEmptiedTokenAccounts` is
    on, and the post-sell token balance reads **zero**. A separate transaction; failure is **logged
    and recorded but never fails the sale** (decision #11).
14. **Refresh** that wallet immediately and write the receipt.

`SellError.Classify` is the single place that decides retryable vs terminal:

| Class | Cases |
| --- | --- |
| **Retryable** (exponential backoff + jitter) | HTTP `429`, HTTP `5xx`, transport/socket errors, `BlockhashNotFound` / "block height exceeded", confirmation timeout |
| **Terminal** (surfaced, batch continues, wallet re-selectable) | slippage exceeded, no route, insufficient funds, price-impact abort, low SOL, zero balance, guard failure, any other `4xx` |

### 6.5 `SwapInstructionsGuard` — what we assert before signing

Decision #6 chose swap-*instructions* over a prebuilt transaction so the tool can assert what it
signs. This is that assertion, and it is **IO-free** so it is unit-tested directly:

1. **Program allowlist.** Every instruction's program id ∈ { Jupiter aggregator v6
   `JUP6LkbZbjS1jKKwapdHNy74zcZ3tLUZoi5QNyVTaV4`, SPL Token, SPL Token-2022, Associated Token
   Account, ComputeBudget, System }. Anything else ⇒ terminal.
2. **Authority is us.** The `userPublicKey` echoed in the instruction accounts is this wallet, and
   this wallet is the only required signer.
3. **Input matches the plan.** The quote we are acting on names `inputMint == token.mint` and
   `inAmount == the planned raw amount`.
4. **Output is SOL.** `outputMint == So11111111111111111111111111111111111111112`.
5. **No authority transfer.** No SPL `SetAuthority`, and no `CloseAccount` other than the
   `wrapAndUnwrapSol` cleanup of the wallet's *own* wrapped-SOL account.

A guard failure aborts **that wallet only**, with the offending program id or field in the message.

### 6.6 `RealizedSol` — what we actually got

```
RealizedSol.From(meta, walletAccountIndex):
    (postBalances[i] - preBalances[i]) + meta.fee     // lamports; fee added back so the number is the SALE, not the net
```

The wallet is the fee payer, so it sits at index `0` of the account keys. Parsing mirrors
`SolanaService.GetAccountBalances` — read the **raw** RPC JSON rather than a convenience conversion,
and treat an unparseable value as an error, never as zero.

Quoted and realized are both stored, and the UI shows both on hover: the gap between them is the
slippage a concurrent batch actually paid.

### 6.7 Receipts — `nightfall-receipts.json`

Same discipline as `SpaceStroker.WalletFunder/Infrastructure/ReceiptsStore.cs`: the domain document
owns the rules, the store owns the disk, and `Save()` runs **inside** `Update` — never batched, never
only at the end.

```jsonc
{
  "updatedUtc": "2026-08-11T20:00:00Z",
  "tokenMint": "…",
  "entries": [
    {
      "alias": "DEV Wallet",
      "publicKey": "…",
      "batchId": "…",
      "percent": null,
      "tokenAmountRaw": "12345678900",
      "tokenAmount": "12345.6789",
      "quotedSol": "1.284",
      "realizedSol": "1.271",
      "signature": "…",
      "closeSignature": "…",
      "status": "Sold",
      "error": null,
      "startedUtc": "…",
      "settledUtc": "…"
    }
  ]
}
```

Statuses: `Planned` → `Simulated` | `Quoted` → `Sent` → `Sold` | `Unconfirmed` | `Failed`.

There is **no plan hash and no resume gate** — unlike funding, a sell is never "resumed": the wallet
either still holds tokens (sell again) or does not (nothing to do). On-chain state is the resume
state; the receipts file is the **audit trail**. This is a deliberate divergence from #165 and the
implementer should not port `MatchesPlan` / `--force-plan-change` across.

### 6.8 Auto-poll and in-flight wallets

`PollLoop` refreshes every `refreshIntervalSeconds`. A wallet **currently in flight is not
overwritten** by a poll — its row keeps the live sell status until the sell settles, then takes a
fresh read. Without this, a mid-sell poll makes a row flicker between "selling" and a stale balance
at exactly the moment the operator is watching it.

---

## 7. The local surface

### 7.1 HTTP endpoints (`Application/ApiEndpoints.cs`)

Bound to `127.0.0.1` only. **Every response carries public data only** — the DTOs have no
`PrivateKey` member at all, which is what makes AC27 structural rather than a review item.

| Method + path | Purpose |
| --- | --- |
| `GET /api/state` | Mode (`SIMULATE`/`LIVE`), token mint, decimals, total supply, `unitPriceSol`, and per wallet: alias, publicKey, solLamports, tokenAmountRaw, supplyPercent, estSolValue, status, `excludedReason`. |
| `GET /api/quote?wallet=…&percent=…` | Live Jupiter quote for that wallet at that percentage → `{ tokenAmount, outSol, priceImpactPercent }`. Debounced client-side, cached + coalesced server-side. |
| `POST /api/mode` `{ "live": true }` | Switch `SIMULATE` ⇄ `LIVE`. Logged to the console. |
| `POST /api/sell` `{ "wallets": ["…"], "percent": 40 \| null }` | Start a batch → `{ batchId }`. Rejects wallets already in flight. |
| `POST /api/refresh` | Force an immediate poll. |
| `GET /api/events` | **SSE** stream of per-wallet status changes (`quoted`, `simulated`, `sent`, `sold`, `unconfirmed`, `failed`, `closed`) plus balance refreshes. |

### 7.2 The dashboard (`wwwroot/`)

Built to the **approved Claude Design mockup** — 3 mockups are produced at implementation time and
approved before any UI code is written (decision #21). The behaviour below is fixed regardless of
which mockup wins:

- **Card face:** alias · SOL balance · **% of total mint supply** · raw token amount · compact
  **est.** SOL value. Excluded wallets (zero balance, low SOL) render in a distinct state with the
  reason visible.
- **Hover panel:** live quote for the current `SELL %` — SOL out, price impact, exact token amount.
  Once the wallet has sold: realized SOL + the copyable signature. Hover requests are **debounced
  300 ms**; leaving before the debounce fires sends nothing.
- **Selection:** click toggles · ctrl/cmd-click adds · shift-click ranges · drag-marquee selects
  every enclosed card · **Select all** / **Clear**. **Click never opens a sell dialog.**
- **`SELL %` field** next to `SELL SELECTED`; blank ⇒ 100%. Non-numeric or out-of-range input
  disables the sell button with an inline message.
- **`SELL SELECTED`** → a yes/no dialog listing each wallet's alias + token amount and the batch's
  **quoted SOL total**, with excluded wallets shown as skipped and why. Declining changes nothing.
- **Mode switch** — a prominent `SIMULATE` / `LIVE` control. `LIVE` is visually unmistakable; the
  page never starts in it.
- **Live progress** via SSE: per-card status, and a batch strip showing sold / failed / remaining.
- **Refresh** button beside the auto-poll indicator.

---

## 8. Flow diagram

```mermaid
sequenceDiagram
    participant U as Operator (browser)
    participant H as Nightfall host (127.0.0.1)
    participant J as Jupiter API
    participant R as Solana RPC (keyed)

    U->>H: drag-select wallets, SELL % = 40, SELL SELECTED
    H-->>U: confirm dialog (per-wallet amounts + quoted SOL total)
    U->>H: POST /api/sell
    loop per wallet, capped at maxConcurrentSells
        H->>R: fresh SOL + token balance
        H->>H: SellPlan.AmountFor + SellPreflight gates
        H->>J: GET /quote
        J-->>H: outAmount, priceImpactPct
        alt price impact > max, or a gate failed
            H-->>U: SSE failed (reason) — batch continues
        else SIMULATE
            H-->>U: SSE simulated (nothing broadcast)
        else LIVE
            H->>J: POST /swap-instructions
            H->>H: SwapInstructionsGuard.Assert
            H->>H: assemble + sign locally (key never leaves the process)
            H->>R: sendTransaction
            H->>R: getSignatureStatuses until confirmed
            H->>R: getTransaction → RealizedSol
            opt 100% sell and balance now zero
                H->>R: closeAccount (separate tx; failure never fails the sale)
            end
            H-->>U: SSE sold (realized SOL + signature)
        end
        H->>H: write nightfall-receipts.json
    end
```

---

## 9. Out of scope

- Buying or sniping — card [#63](https://trello.com/c/nokWHm7z).
- Funding wallets with SOL — [#165](https://trello.com/c/9OoHAuEa) `spacestroker-fund-wallets`.
- More than one Token CA per secrets file.
- Consolidating or sweeping SOL out of the pool afterwards.
- Limit orders, laddered or scheduled exits, price triggers, or any automation that sells without a
  click.
- An HTML run summary (that is #63's artifact) — Nightfall records JSON only.
- Any change to `monkify-backend`, `monkify-front`, `monkify-lp`, the deployed stack or
  `monkify-meta/deploy/`.
- Remote or hosted operation, multi-user access, authentication — the host binds to localhost only.
- Resume semantics for an interrupted batch (§6.7 — on-chain state is the resume state).

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

**None.** No entity, enum, handler, consumer, EF mapping, migration, endpoint or SignalR contract.
No branch is created in this repo.

### 10.2 Frontend (`monkify-front`, `monkify-lp`)

**None.** No branch is created in either repo.

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `tools/.gitignore` | **Edit** — add `*receipts*.json` and `!secrets.nightfall.example.json` (§4.4; both gaps verified with `git check-ignore`) |
| 2 | `tools/SpaceStroker.Tools.sln` | **Edit** — add `SpaceStroker.Nightfall` + `SpaceStroker.Nightfall.Tests` |
| 3 | `tools/secrets.nightfall.example.json` | **New** — tracked template, placeholders only (§4.3) |
| 4 | `tools/SpaceStroker.Nightfall/SpaceStroker.Nightfall.csproj` | **New** — `Microsoft.NET.Sdk.Web`, net8.0, Solnet 6.1.0, pinned Portable.BouncyCastle 1.9.0 |
| 5 | `tools/SpaceStroker.Nightfall/Program.cs` | **New** — parse → load → mint read → build host → run; maps `NightfallException`→1, `UsageException`→2 |
| 6 | `…/Domain/WalletPool.cs` | **New** — `PoolEntry`, parsing, unknown fields ignored, address + key/address-match validation |
| 7 | `…/Domain/TokenAmount.cs` | **New** — raw ⇄ UI exact decimal conversion, invariant formatting |
| 8 | `…/Domain/SellPlan.cs` | **New** — `SELL %` → raw amount; blank ⇒ 100%; floor; zero/out-of-range rejection |
| 9 | `…/Domain/SellPreflight.cs` | **New** — zero-balance, low-SOL, price-impact gates (values in, not clients) |
| 10 | `…/Domain/SwapInstructionsGuard.cs` | **New** — the pre-signing assertion (§6.5) |
| 11 | `…/Domain/SellError.cs` | **New** — Retryable vs Terminal classification + reason text |
| 12 | `…/Domain/RealizedSol.cs` | **New** — `post - pre + fee` balance-delta parse |
| 13 | `…/Domain/SellReceipt.cs` | **New** — `SellReceipt` + `ReceiptsDocument` + state transitions |
| 14 | `…/Domain/NightfallException.cs` | **New** — `NightfallException` + `UsageException` |
| 15 | `…/Infrastructure/SecretsFile.cs` | **New** — config model + `Load()` with placeholder detection (§4.3) |
| 16 | `…/Infrastructure/ReceiptsStore.cs` | **New** — save after every state change |
| 17 | `…/Infrastructure/Jupiter/JupiterClient.cs` | **New** — `/quote` + `/swap-instructions`, retry, rate-limit aware |
| 18 | `…/Infrastructure/Jupiter/JupiterQuote.cs` | **New** — wire DTOs; the quote response is echoed back verbatim |
| 19 | `…/Infrastructure/Jupiter/QuoteCache.cs` | **New** — TTL cache + in-flight coalescing |
| 20 | `…/Infrastructure/Solana/SolanaReader.cs` | **New** — batched SOL + ATA balances, `getTokenSupply`, masked-URL logging |
| 21 | `…/Infrastructure/Solana/SolanaSender.cs` | **New** — assemble, sign, send, confirm, read realized SOL |
| 22 | `…/Infrastructure/Solana/TokenAccountCloser.cs` | **New** — the follow-up `closeAccount` transaction |
| 23 | `…/Application/CliOptions.cs` | **New** — args + usage (copies the funder's shape) |
| 24 | `…/Application/NightfallState.cs` | **New** — pool snapshot, SIMULATE/LIVE mode, in-flight set |
| 25 | `…/Application/SellRunner.cs` | **New** — the batch (§6.4), `SemaphoreSlim` cap |
| 26 | `…/Application/PollLoop.cs` | **New** — auto-refresh, skips in-flight wallets |
| 27 | `…/Application/ApiEndpoints.cs` | **New** — the six endpoints + SSE (§7.1); public-data DTOs only |
| 28 | `…/Application/ConsoleReport.cs` | **New** — banner, mode changes, per-sell lines, masked RPC host |
| 29 | `…/wwwroot/index.html` · `app.js` · `app.css` | **New** — the dashboard (§7.2), built to the approved mockup |
| 30 | `tools/SpaceStroker.Nightfall.Tests/*` | **New** — 11 test files mirroring the folders (§11) |
| 31 | `tools/README.md` | **Edit** — add a `spacestroker-nightfall` section: preconditions, SIMULATE-first workflow, config table, exit codes; update the project table at the top |
| 32 | `INDEX.md` | **Edit** — spec row + "Feature currently in flight" |
| 33 | `specs/nightfall-dashboard.md` | **New** — this file |
| 34 | `features/nightfall-dashboard.md` | **New at ship time** (`/monkify-ship-feature`) |

---

## 11. Unit tests

Project: `monkify-meta/tools/SpaceStroker.Nightfall.Tests` (xUnit + Shouldly, mirroring
`SpaceStroker.WalletFunder.Tests`). Run with `dotnet test tools/SpaceStroker.Tools.sln`.

There is **no fake Jupiter and no fake RPC sender** — the same decision #165 made. The `Domain/`
layer being IO-free is what lets every money rule below be tested with plain values.

| Test file | Test cases | Expected |
| --- | --- | --- |
| `Domain/WalletPoolTests.cs` | entry with `SupplyPercentage` + other unknown fields; missing `Alias`; whitespace-padded address; invalid base58; 31-byte address; private key that derives to a different public key | unknown fields ignored (AC02); alias defaults to `W01`-style; address trimmed; invalid/short address rejected at load; key/address mismatch rejected at load |
| `Domain/SellPlanTests.cs` | percent `null`; `""`; `100`; `40` on a balance of `1000`; `40` on `1` (rounds to 0); `0`; `-5`; `101`; `0.5` on a large balance | null/blank/100 ⇒ full balance (AC11); `40`⇒`400` floored (AC12); rounds-to-zero, `0`, negative and `>100` all rejected with their own message |
| `Domain/SellPreflightTests.cs` | zero token balance; SOL below `minSolForFeeSol`; SOL exactly at the floor; price impact under, equal to and over `maxPriceImpactPercent` | zero excluded and never quoted (AC17); low SOL excluded with the top-up reason (AC16); "at the floor" allowed; over-impact aborts that wallet only (AC18) |
| `Domain/SwapInstructionsGuardTests.cs` | clean Jupiter instruction set; an unknown program id; authority ≠ wallet; wrong `inputMint`; `inAmount` ≠ plan; wrong `outputMint`; a `SetAuthority`; a `CloseAccount` on a foreign account; the legitimate wrapped-SOL cleanup close | clean passes; each violation is terminal and names the offending program id or field; the wrapped-SOL cleanup is allowed |
| `Domain/SellErrorTests.cs` | HTTP 429; 503; socket exception; "blockhash not found"; "block height exceeded"; confirm timeout; slippage exceeded; no route; insufficient funds; HTTP 400; guard failure | first six ⇒ Retryable (AC20); the rest ⇒ Terminal with a human reason (AC21) |
| `Domain/RealizedSolTests.cs` | normal sale meta; a sale where the wallet also paid the fee; zero delta; unparseable balance array | `post - pre + fee`; fee added back; zero delta reported as zero; unparseable ⇒ error, never silently zero |
| `Domain/SellReceiptTests.cs` | every legal transition; `Sold` → `Sent` (illegal); an entry updated twice | legal transitions apply; illegal transitions throw; both quoted and realized SOL retained |
| `Infrastructure/SecretsFileTests.cs` | missing file; unparsable JSON; `REPLACE_ME` in `rpcUrl` / `token.mint` / a wallet key; empty `wallets`; defaults when a section is absent | missing file names the file **and** the example template (AC01); each placeholder rejected by name; empty pool is a clean no-op (AC03); documented defaults applied |
| `Infrastructure/ReceiptsStoreTests.cs` | write after each of three status changes; reload from disk; a directory that does not exist yet | file rewritten every time, not batched (AC25); round-trips; directory created |
| `Infrastructure/QuoteCacheTests.cs` | two identical requests inside the TTL; after expiry; two concurrent identical requests; different percent | one upstream call; refetch after expiry; concurrent requests coalesce to one; different key ⇒ separate call |
| `Application/NightfallStateTests.cs` | sell while `SIMULATE`; a wallet already in flight; in-flight cleared after failure; poll during a sell | SIMULATE broadcasts nothing server-side (AC13); duplicate rejected (AC22); in-flight always released; poll does not overwrite an in-flight row |

---

## 12. Code-exploration findings

Documented here so the implementer does not rediscover them at cost.

1. ⚠️ **Solnet 6.1.0 and versioned transactions / address lookup tables — resolve this first.**
   Every Solana transaction in this workspace is built with the **legacy** `TransactionBuilder`
   (`SpaceStroker.Infrastructure/Services/Solana/SolanaService.cs:406`), and nothing here has ever
   assembled a **v0** transaction. Jupiter routes routinely return
   `addressLookupTableAddresses`, which a legacy transaction **cannot** carry.
   - **Primary path:** assemble a v0 transaction from Jupiter's instructions, resolving the lookup
     tables via `getMultipleAccounts`, and sign it locally.
   - **Documented fallback** if Solnet 6.1.0 cannot assemble v0 + ALT: fetch the wire bytes from
     Jupiter's **`POST /swap`** (which returns a ready base64 versioned transaction), **and still**
     call `/swap-instructions` and run `SwapInstructionsGuard` over the instruction set before
     signing. Signing does not require parsing: sign the message bytes with ed25519 and splice the
     signature into the signature array. The "assert what we sign" property of decision #6 is
     preserved either way.
   - **Record which path was taken in §15** — it is the single biggest implementation unknown here.
2. ⚠️ **`tools/.gitignore` misses both new filenames.** Verified with `git check-ignore`:
   `nightfall-receipts.json` is **not** ignored (`receipts*.json` anchors at the start of the name),
   and `secrets.nightfall.example.json` **is** ignored (so the tracked template could never be
   committed). §4.4 has the fix and the re-verification command. The root `monkify-meta/.gitignore`
   covers only `sales/pdf/` — `tools/.gitignore` is load-bearing, exactly as
   `features/coinbase-wallet-funding.md` warns.
3. **`Microsoft.NET.Sdk.Web`, not `Microsoft.NET.Sdk`.** `SpaceStroker.WalletFunder` is a plain
   console app; Nightfall needs static-file serving and a minimal-API host, which the Web SDK
   provides. This is the one place it deliberately diverges from its sibling.
4. **`new PublicKey(string)` is not validation.** It is only ever called on known-good values in
   `monkify-backend`. Use `PublicKey.IsValid` **plus** a 32-byte decode assertion — #165 hit this,
   and a truncated address is the irreversible failure.
5. **Never route a money value through a `double`.** `SolanaService.ToUiAmount` (line 255) builds its
   divisor with a decimal loop *specifically* to avoid `Math.Pow`. `TokenAmount` copies that, and
   `WalletPool.FormatSol`'s invariant formatting (`0.#########`, `CultureInfo.InvariantCulture`)
   applies to everything rendered — the project sets `InvariantGlobalization`, but being explicit is
   what stopped a `pt-BR` machine emitting `"2,54"` in #165.
6. **A missing account is a zero balance, not an error.** Both `TokenBalanceFailed` and
   `SolBalanceFailed` in `SolanaService` (lines 94–100) deliberately exclude "could not find account"
   from the retry predicate — a wallet that never held the token must read as `0`, not as a failure.
   `SolanaReader` must make the same distinction or every fresh wallet looks broken.
7. **`preflightCommitment` must match the blockhash commitment.** `specs/solana-connection-rpc.md`
   recorded this on the frontend's `sendToken`: fetch the blockhash at `confirmed` and send with
   `Commitment.Confirmed`, or the node rejects a blockhash it has not seen yet.
8. **Mask the RPC URL wherever it is logged.** The keyed Helius endpoint carries the API key in the
   query string. `specs/solana-rpc-api-key.md` established host-only masking (`MaskRpcUrl`); reuse
   the same rule here, including in error messages surfaced to the browser.
9. **Retry shape to copy:** `SolanaService.BuildPolicy` / `DelayFor` (lines 108–142) — Polly,
   exponential backoff with a 250 ms jitter, a rate-limit multiplier on 429/5xx, a max-delay clamp,
   and predicates that describe **transport** failure only.
10. **Jupiter's free tier is genuinely rate-limited.** This is why decisions #22 (one price quote per
    cycle, not one per wallet), #23 (debounce + cache + coalesce) and #9 (concurrency cap) exist. All
    three are load-bearing on a 20-wallet pool, not micro-optimisations.
11. **`dotnet sln add`, not `dotnet new sln`.** The .NET 10 SDK writes `.slnx`; adding to the existing
    classic-format `SpaceStroker.Tools.sln` avoids creating a second solution file (#165's finding).

---

## 13. Acceptance criteria

Copied 1:1 from the Trello **Acceptance Criteria** checklist on
[#166](https://trello.com/c/mUDhAdBu).

1. **AC01** — Given no `tools/secrets.nightfall.json`, when the host starts, then it stops with a clear message naming the missing file and the tracked example template, and no RPC or Jupiter call is made.
2. **AC02** — Given a `secrets.nightfall.json` whose wallet entries carry extra fields (e.g. #63's `SupplyPercentage`), when the pool is loaded, then the unknown fields are ignored and every wallet loads from `Alias` / `PublicKey` / `PrivateKey`.
3. **AC03** — Given an empty wallet pool, when the host starts, then it reports the empty pool clearly, reaches no external service, and exits without error.
4. **AC04** — Given the host is running, when the dashboard loads, then every wallet card shows its alias, SOL balance, % of total mint supply, raw token amount and a compact estimated SOL value.
5. **AC05** — Given the dashboard is open, when the configured poll interval elapses, then all wallet balances refresh through batched RPC reads with no user action; and when Refresh is clicked, then they refresh immediately.
6. **AC06** — Given a wallet card, when it is hovered, then a panel shows the live Jupiter quote for the current `SELL %` setting: SOL out, price impact and the exact token amount that would be sold.
7. **AC07** — Given a wallet that has already sold in this session, when it is hovered, then the panel additionally shows the realized SOL received and the copyable transaction signature.
8. **AC08** — Given a wallet card, when it is clicked, then it only toggles selection: no sell dialog opens and nothing is signed or broadcast.
9. **AC09** — Given several wallet cards, when a drag-marquee is drawn across them, then every enclosed wallet becomes selected; ctrl/cmd-click toggles a single wallet, shift-click selects a range, and Select all / Clear affect the whole pool.
10. **AC10** — Given a selection of one or many wallets, when SELL SELECTED is clicked, then a confirmation dialog lists each wallet's token amount and the batch's quoted SOL total; declining sends nothing and changes nothing.
11. **AC11** — Given the `SELL %` field is left blank, when a sell is confirmed, then each selected wallet sells 100% of its own token balance.
12. **AC12** — Given `SELL %` is 40 and a selected wallet holds 2% of total mint supply, when the sell is confirmed, then that wallet sells 40% of its own balance (0.8% of supply) — not 40% of supply.
13. **AC13** — Given SIMULATE is on (the startup default), when a sell is confirmed, then quotes and transactions are built and displayed but nothing is broadcast and no receipt records a signature.
14. **AC14** — Given the operator has deliberately switched to LIVE, when a sell is confirmed, then the transaction is signed locally with the wallet's key and broadcast and confirmed through the configured RPC.
15. **AC15** — Given N selected wallets and `maxConcurrentSells` = 4, when the batch runs, then no more than 4 sells are in flight at any moment and all N eventually complete or fail independently.
16. **AC16** — Given a wallet whose SOL balance is below `minSolForFeeSol`, when a sell is triggered, then that wallet is flagged, excluded from the batch with the reason named, and the remaining wallets still sell.
17. **AC17** — Given a wallet holding zero of the token, when a sell is triggered, then it is excluded and is never quoted, signed or broadcast.
18. **AC18** — Given a wallet whose Jupiter quote reports price impact above `maxPriceImpactPercent`, when the batch runs, then that wallet aborts before broadcasting with the reason shown, and every other selected wallet still sells.
19. **AC19** — Given a wallet's token balance changed between the hover quote and execution, when the sell runs, then the amount is recomputed from a fresh balance read so a 100% sell sells exactly what the wallet actually holds.
20. **AC20** — Given a sell that fails with a 429, a transport error or an expired blockhash, when the batch runs, then it is retried with exponential backoff + jitter and no other wallet is affected.
21. **AC21** — Given a sell that fails terminally (slippage exceeded, no route, insufficient balance), when the batch runs, then it is marked failed with the reason, the batch continues to completion, and that wallet can be re-selected and sold again.
22. **AC22** — Given a wallet whose sell is already in flight, when it is selected again and a sell is triggered, then it is not submitted a second time.
23. **AC23** — Given a confirmed 100% sell, when it settles, then a separate follow-up `closeAccount` transaction reclaims the token account rent; and given that follow-up fails, then the failure is logged and the sale still counts as successful.
24. **AC24** — Given a confirmed partial sell (`SELL %` < 100), when it settles, then the token account is not closed and the wallet's remaining position is still shown and still quotable.
25. **AC25** — Given any state change during a run, when it occurs, then `nightfall-receipts.json` is rewritten immediately with, per wallet, the token amount sold, the quoted and the realized SOL, the transaction signature, the status and a timestamp.
26. **AC26** — Given the process is killed mid-batch, when the host is restarted, then every signature already obtained is present in `nightfall-receipts.json` and the dashboard reloads current on-chain balances.
27. **AC27** — Given a completed run, when the console output, the HTTP responses served to the browser and `nightfall-receipts.json` are inspected, then no wallet private key appears in any of them.
28. **AC28** — Given the tool has been configured and run, when `git status` is checked in `monkify-meta`, then `secrets.nightfall.json` and `nightfall-receipts.json` are ignored and the only tracked config file is the placeholder-only `secrets.nightfall.example.json`.

### Coverage at implementation time

| Verified by | Criteria |
| --- | --- |
| Unit tests (§11) | AC01, AC02, AC03, AC11, AC12, AC13, AC16, AC17, AC18, AC20, AC21, AC22, AC25 |
| `git check-ignore` (§4.4) | AC28 |
| Local UI walkthrough in **SIMULATE** (§14 steps 1–6) | AC04, AC05, AC06, AC08, AC09, AC10, AC27 |
| **Operator step — a real live sell** (§14 steps 7–11) | AC07, AC14, AC15, AC19, AC23, AC24, AC26 |

⚠️ The last row cannot be verified by the implementer: every one of those criteria requires a real
mainnet sale of real tokens (`constraints.md` §5). Report them as **unverified against a live
wallet** at implementation time, exactly as #165 did.

---

## 14. Manual QA

**Steps 1–6 are safe** (nothing is broadcast). **Steps 7–11 spend real money** and are an operator
step.

1. `cp tools/secrets.nightfall.example.json tools/secrets.nightfall.json`, fill in the RPC URL, the
   Token CA and **one** low-value wallet. Confirm `git status` shows neither the secrets file nor a
   receipts file (AC28).
2. `dotnet run --project tools/SpaceStroker.Nightfall` → the banner reads **`MODE: SIMULATE`**, the
   RPC host is masked, and the browser opens.
3. Check the card face: alias, SOL, % of supply, token amount, est. SOL value (AC04). Wait one
   interval and click Refresh (AC05).
4. Hover a wallet — a live quote appears (AC06). Move the mouse across several cards quickly and
   confirm the request count stays low (the debounce + cache are working).
5. Click a card: it selects and **nothing else happens** (AC08). Drag a marquee, ctrl-click,
   shift-click, Select all, Clear (AC09).
6. `SELL SELECTED` with the field blank → the dialog shows 100% amounts and a quoted SOL total
   (AC10). Accept it: the UI reports **simulated** and `nightfall-receipts.json` records **no
   signature** (AC13). Decline once and confirm nothing changes.
7. **Live single wallet.** Switch to `LIVE`, select one low-value wallet, set `SELL %` to a small
   number, confirm. Verify the signature on a block explorer, and that realized SOL appears on hover
   (AC07, AC14) and the token account is **not** closed (AC24).
8. **Percentage math.** Note the wallet's balance before and after: the sale is that percentage of
   the wallet's own balance (AC12).
9. **Full exit + close.** Sell the same wallet with the field blank; confirm the balance reaches zero
   and a **second** signature closes the token account (AC23).
10. **Concurrency.** With ≥5 wallets funded, select all and sell. Watch that no more than
    `maxConcurrentSells` are in flight and that all complete independently (AC15).
11. **Crash resilience.** Start a batch and kill the process mid-run. Restart: every signature already
    obtained is in the receipts file and the dashboard shows current on-chain balances (AC26).

---

## 15. Trello / pipeline

- **Source card:** [Nightfall dashboard #166](https://trello.com/c/mUDhAdBu) — list **Specs created**;
  the **Implementation** section on the card carries branch, worktree, spec path and **Level**.
- **Branch:** `feature/nightfall-dashboard` (`monkify-meta` only).
- **Worktree:** `.worktrees/nightfall-dashboard/monkify-meta`.
- **Level:** `Feature` — `features/nightfall-dashboard.md` required at ship time; the backend
  integration test is **waived on the card** (no backend surface).
- **Next:** `/monkify-implement-feature Nightfall dashboard` — which produces **3 Claude Design
  mockups for approval before any UI code**, then implements. Then
  `/monkify-ship-feature Nightfall dashboard`.

---

## 16. Revision log

| Date | Change |
| --- | --- |
| 2026-08-11 | Initial spec from the refined card + the refinement conversation. Two `.gitignore` gaps confirmed with `git check-ignore` (§4.4, finding 2); the Solnet versioned-transaction/ALT question raised as finding 1 with a primary path and a documented fallback. |
| 2026-08-11 | **Implemented.** Finding 1 resolved: **the fallback was taken.** Solnet.Rpc 6.1.0 exposes only `Message` / `Transaction` / `TransactionBuilder` / `MessageBuilder` — there is **no** `VersionedTransaction`, no `MessageV0` and no lookup-table type (verified against the package's own XML docs), so the primary path of assembling a v0 transaction locally is not available at this version. Jupiter's `/swap` supplies the ready v0 wire transaction and `Infrastructure/Solana/TransactionSigner` signs it **without parsing** — a Solana signature covers the message bytes and the wire format puts the signature array in front of them, so splicing an ed25519 signature into slot 0 is correct for legacy and v0 alike. `SwapInstructionsGuard` still runs over `/swap-instructions` first, so the "assert what we sign" property of decision #6 is preserved: the guard vets the semantics, the signer vets the shape. |
| 2026-08-11 | **Three implementation-time corrections**, each caught by a test or a live smoke run rather than by review. (1) **`ValidateKeyPair` was a no-op.** It compared `new Account(privateKey, publicKey).PublicKey` against the configured address — but Solnet's `Account(string, string)` merely *stores* the address it is handed and never derives one, so the check passed for any pairing. It now derives the public key from the seed with `Ed25519PrivateKeyParameters` (the first **32** of the 64 secret bytes — the same lesson #165 recorded for the CDP key). (2) **The batch was cancelled the instant it started.** `SellRunner.RunAsync` ran its fire-and-forget work under the endpoint's `CancellationToken`, which is `HttpContext.RequestAborted` and fires the moment the response completes; the API returned a batch id and nothing ever happened. The runner now takes the **application's** lifetime token. (3) **The concurrency semaphore was disposed mid-batch** — `using var gate` inside `RunAsync` released it when the method returned, while the background work still held it. Both now live inside the background task. |
| 2026-08-11 | Spec §6.4 step 7 said the swap is assembled from Jupiter's instructions; per finding 1 above the shipped code assembles nothing — it signs Jupiter's `/swap` transaction and uses `/swap-instructions` purely as the input to the guard. `JupiterClient` exposes both calls (`SwapInstructionsAsync`, `SwapTransactionAsync`) for exactly that split. Also worth recording: **Jupiter's `priceImpactPct` is a fraction, not a percentage** (`"0.0123"` = 1.23%), so it is multiplied by 100 on the way in — reading it as a percentage would have made the price-impact abort roughly 100× too permissive, i.e. it would never have fired. |

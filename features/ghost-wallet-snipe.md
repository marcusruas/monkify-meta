# Feature — Ghost wallet snipe app

> Status: **shipped.** Authoritative spec: [`../specs/ghost-wallet-snipe.md`](../specs/ghost-wallet-snipe.md)
> (merged via `feature/ghost-wallet-snipe` on 2026-08-11). Trello
> [#63](https://trello.com/c/nokWHm7z).
>
> ⚠️ **Ops tooling, not part of the deployed app.** Nothing here runs in Cloud Run, in the local
> Docker stack, or in any request path. It is a console app an operator runs by hand, and **it spends
> real SOL on irreversible buys.**

## What it does

`spacestroker-snipe-token` drives a pool of wallets we control to a configured share of **our own
pump.fun token's** supply: one buy per wallet, in **ascending `alias` order**, at randomized offsets
across a window (default 60 minutes), then writes an HTML and a JSON report of achieved-vs-target. It
is the **buy** half of the pair whose funding half is
[`coinbase-wallet-funding.md`](coinbase-wallet-funding.md) (#165) — same solution, **same pool file**,
same money-gate philosophy.

A run is **dry by default**, **preflighted**, **resumable without double-buying**, **slippage-bounded
per transaction**, and **spread over time**.

## Core rules

**Targets and pricing**
- `SupplyPercentage` is a **percent number of TOTAL supply**: `0.4504` means **0.4504%**, never
  45.04%. It resolves against the mint's on-chain supply read at preflight, truncated so a wallet is
  never planned above its share.
- A wallet's buy is its **shortfall** — target minus what it already holds — so re-running tops up
  rather than re-buying.
- The ladder is quoted **sequentially**: each wallet is priced against the reserves its predecessors
  left behind. Pricing is constant-product over the curve's **virtual** reserves, with the platform +
  creator fees riding on top; costs round **up** and token amounts round **down**.
- **The supply target wins over budget.** There is no global SOL cap: a wallet buys until it holds its
  target or its SOL runs out, and a wallet that cannot afford its shortfall buys the inverse-priced
  maximum it can and is reported **`Short`**. A per-wallet `reserveLamports` is withheld so every
  wallet can still pay its own transaction fee and ATA rent.
- **Every transaction carries `maxSolCost`** = quote × (1 + `slippageTolerance`), capped at what the
  wallet can actually pay. This is not a budget cap — it is what stops one buy being sandwiched — and
  the instruction cannot be built without it.

**Ordering and execution**
- **One buy per wallet, in ascending `alias` order** (ordinal). ⚠️ This is a deliberate change from
  the original card, which said largest-supply-first. Only the **gaps** jitter; the order is fixed.
- `alias` is **required and unique** — it is the ordering key, so a blank or duplicated one is a
  preflight error, never a tiebreak. Mixed padding (`W2` beside `W10`) is a **warning**: ordinal
  sorting puts `W10` first.

**Money gates**
- Dry run is the default. Money moves only with **`--execute`** *and* the operator typing the **exact
  quoted total SOL**; a comma decimal never matches (invariant formatting, the funder's lesson).
- Preflight runs **before buy #1** and reports **every** problem at once: aliases, 32-byte address
  decode, a `PrivateKey` that does not derive its `PublicKey`, non-positive percentages, targets
  exceeding what the curve can still sell, a graduated or non-SOL-quoted curve, a zero-supply mint. An
  empty pool is a no-op with **exit 0**.
- The `status` command is read-only and **structurally cannot submit** — it rejects `--execute`.

**Settlement and resume**
- A buy counts as `Filled` only on an **observed token-balance increase**, never on the signature
  alone. Submitted-but-not-observed is recorded `Unconfirmed`.
- ⚠️ **The on-chain balance is the resume authority; the receipts file is only an audit trail.** Solana
  has no server-side idempotency token, so a receipts-driven resume could re-buy a wallet whose
  transaction confirmed just before a crash. Re-running simply recomputes the shortfall.
- A retry after an RPC timeout **re-reads the token balance first** and only resubmits if the buy did
  not land. One wallet's failure never stops the batch.

**Scope**
- **Bonding curve only.** A graduated token is refused at preflight; graduation part-way through a run
  skips the remaining wallets and records where it stopped. There is no AMM path in either direction,
  and **no sell command** — both are deliberate exclusions, not omissions.
- **Exit codes:** `0` every targeted wallet reached target / dry run / empty pool · `1` anything short,
  failed or unresolved · `2` usage error.

**Secrets and artifacts**
- The pool is **one file shared with the funder** — the sniper binds `alias`, `PublicKey`,
  `PrivateKey`, `SupplyPercentage`; the funder's `Label`/`SOLAmount` ride along ignored, and vice
  versa. **The sniper is the only tool in the solution that ever binds a `PrivateKey`.**
- Reports land in `tools/reports/<timestamp>/` and carry **public addresses only** — no private key,
  no keyed RPC URL. ⚠️ `tools/.gitignore` needed **two new rules** for this: `reports/` and
  `snipe-receipts*.json` (the pre-existing `receipts*.json` glob is anchored at the filename start and
  does **not** match).

## ⚠️ Protocol facts (verified against mainnet 2026-08-11 — re-verify before a launch)

Read from the program's **own on-chain Anchor IDL** (`AYgC53tU5BbP2NAnv5nConJxAdpQZctvmZK88pu69xRs`)
plus decoded live buys, then pinned to a real transaction in `PumpFunProgramTests`. **If pump.fun
redeploys, that test is what tells you** — re-derive from a fresh transaction rather than patching from
documentation. Five things here contradicted what the spec assumed before implementation:

- The curve's quote side is **`virtual_quote_reserves` / `real_quote_reserves`**, not `*_sol_reserves`,
  and the account also carries `creator`, `is_mayhem_mode`, `is_cashback_coin` and **`quote_mint`**.
  A curve whose `quote_mint` is not the default pubkey is priced in **another token** and is refused.
- `buy` takes a **third argument** (`track_volume`). Live traffic carries all three encodings and all
  land: omitted (24-byte data, the commonest — our default), `0x00`, `0x01 0x01`.
- **Live buys carry 18 accounts against the IDL's 16.** The two extras are unnamed trailing accounts
  (one owned by the fee program, one not yet created). We send the declared 16 and expose
  `snipe.extraAccounts`; if a deployment ever requires them the failure is a **reverted transaction —
  no SOL moves**.
- **The token program varies per mint** — live pump.fun mints use both SPL Token and Token-2022, and
  both ATA derivations depend on which. Solnet's `DeriveAssociatedTokenAccount` hardcodes SPL Token, so
  the tool derives ATAs itself from `[owner, tokenProgram, mint]`.
- **Fee rates are `global`-account config** (95 bps platform + 5 bps creator on 2026-08-11), read at
  runtime. A hardcoded rate misquotes every wallet the day they change it.

## Key code touchpoints

All on `main` in **`monkify-meta`** — there is no backend or frontend surface.

- `tools/SpaceStroker.Tools.sln` — the solution; the sniper joins the funder here.
- `tools/SpaceStroker.TokenSniper/Program.cs` — entry point; maps `SniperException` → 1, `UsageException` → 2.
- `tools/SpaceStroker.TokenSniper/Domain/` — **IO-free**: `SnipePool` (parsing; ⚠️ a blank alias is
  *not* auto-named, unlike the funder's `WalletPool`), `BondingCurve` (`CurveState`, `CurveFees`,
  forward/inverse pricing, graduation), `SnipePlan` (targets, shortfall, affordability, `maxSolCost`),
  `BuySchedule` (alias order + jittered offsets), `SnipePreflight`, `RunLedger` (`WalletOutcome`,
  `RunReport`, exit code), `SniperException`.
- `tools/SpaceStroker.TokenSniper/Infrastructure/` — `SecretsFile`, `ReceiptsStore`,
  `Solana/PumpFunProgram` (PDAs, computed discriminator, the verified account list, hand-built
  compute-budget instructions), `Solana/AccountLayouts` (curve + global decoders),
  `Solana/SolanaGateway`, `Reports/JsonReportWriter`, `Reports/HtmlReportWriter`.
- `tools/SpaceStroker.TokenSniper/Application/` — `Sniper` (the run), `CliOptions`, `ConsoleReport`.
- `tools/SpaceStroker.TokenSniper.Tests/` — 118 tests mirroring the same folders.
- `tools/README.md` — operator preconditions, the dry-run-first workflow, the protocol notes.

## Gotchas worth knowing

- **`Solnet.Programs` 6.1.0 has no `ComputeBudgetProgram`.** The priority-fee instructions are
  hand-built: `[2, u32 units]` = SetComputeUnitLimit, `[3, u64 microLamports]` = SetComputeUnitPrice.
  Without a priority fee a buy can simply never land in a busy launch window.
- **Use a keyed RPC.** `api.mainnet-beta.solana.com` rate-limits — it returned `429` during this
  tool's own protocol research — and silently drops sends.
- **The funder's signature-collision workaround does not apply here.** `SolanaService` adds a memo
  because Orbit sends identical transfers from one signer; each ghost wallet signs and pays for its own
  distinct transaction, so no memo is needed.
- **Solnet's `new PublicKey(string)` is not validation** (the funder's finding, still true): preflight
  uses `PublicKey.IsValid` **plus** a 32-byte decode assertion.

## Verification status

The 118-test suite covers the **pure** logic end to end — curve math (forward, inverse, rounding
direction, graduation), targets/shortfall/affordability, the alias schedule, preflight, the ledger and
exit codes, config loading, receipts, both report writers — plus the instruction layout and every PDA
derivation asserted against a **real mainnet buy**, and the curve decoder against a **real account
dump**. The funder's 79 tests are untouched and green.

**Exercised live, read-only (2026-08-11):** `snipe` (dry run) and `status` against mainnet — mint
supply, **Token-2022** detection, fee-rate decode, curve decode, targets, the jittered alias-ordered
ladder, and empty-wallet skips. The graduation refusal was verified live by accident: the fixture token
graduated between capture and the run, and the tool refused it exactly as designed.

⚠️ **Never exercised: a real `--execute` buy.** The submit/confirm/balance-delta path, the ATA-creation
branch, the slippage revert and the mid-run graduation stop have never run against live money — and
that is also the only way to settle the **16-vs-18 account** question. A live run is an **operator**
step (`constraints.md` §5); the spec's Manual QA holds the sequence: `status` → dry run → a single
wallet at a tiny `SupplyPercentage` → kill-and-resume → the full pool.

The mandatory happy-path integration test in `SpaceStroker.Tests.Integration` was **explicitly waived**
for this card — the tool has zero backend surface, so there is no code path for such a test to drive
(same waiver as [`coinbase-wallet-funding.md`](coinbase-wallet-funding.md) and
[`../specs/main-production-operator.md`](../specs/main-production-operator.md)).

## Related

- [`coinbase-wallet-funding.md`](coinbase-wallet-funding.md) — the funding half: fills the same pool
  with SOL out of Coinbase before this tool spends it.
- [`../specs/ghost-wallet-snipe.md`](../specs/ghost-wallet-snipe.md) — authoritative design.
- [`../conventions/backend.md`](../conventions/backend.md) → "The folder tree must show the
  architecture" — the Domain/Infrastructure/Application rule this project follows.

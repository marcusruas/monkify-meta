# Domain — Token operations (the ghost-wallet pool)

> ⚠️ **Ops tooling, not part of the deployed app.** Nothing here runs in Cloud Run, in the local Docker
> stack, or in any request path. These are console apps an operator runs **by hand**, and **all three
> move real money on Solana mainnet**. They live in `monkify-meta/tools/` — see
> [`../conventions/backend.md`](../conventions/backend.md) → "Meta-repo console tools" for the solution
> layout, the shared `tools/secrets.json` and the `tools/Receipts/` rule.
>
> **One lifecycle, three tools:** `spacestroker-fund-wallets` **funds** the pool with SOL →
> `spacestroker-snipe-token` **buys** the supply → `spacestroker-nightfall` **sells** it.
>
> Deep design: [`../specs/coinbase-wallet-funding.md`](../specs/coinbase-wallet-funding.md) ·
> [`../specs/ghost-wallet-snipe.md`](../specs/ghost-wallet-snipe.md) ·
> [`../specs/nightfall-dashboard.md`](../specs/nightfall-dashboard.md).

## Rules all three share

- **Dry run is the default.** Money moves only with **`--execute`** *and* the operator typing the
  **exact** batch/quoted total. ⚠️ A comma decimal (`12,75`) never matches — everything is
  invariant-formatted, because a `pt-BR` machine would otherwise emit `"2,54"`.
- **Preflight runs before the first money move** and reports **every** problem at once, not the first.
- **One wallet's failure never stops the batch.** The rest proceed, the error is recorded, the process
  exits non-zero, and a re-run targets only what is unresolved.
- **Secrets never reach output.** No private key, API secret or minted JWT appears in a log, a receipt,
  an HTTP response or an error message. Key ids print masked; a keyed RPC URL is masked to host-only.
- **One shared pool file.** All three read `wallets[]` from `tools/secrets.json` and **ignore unknown
  fields**, which is exactly what lets one array serve a funder that never binds a `PrivateKey` and a
  sniper that must.
- ⚠️ **`tools/.gitignore` is load-bearing.** The `monkify-meta` root `.gitignore` ignores only
  `sales/pdf/`; secrets safety in `deploy/` comes from a nested `.gitignore`, so `tools/` needs its own
  or a live withdrawal credential becomes committable.

---

## 1. `spacestroker-fund-wallets` — funding

Funds the pool with **SOL out of a Coinbase account** (CDP Secret API Key → `POST /v2/accounts/{id}/transactions`),
replacing funding a dozen-plus wallets by hand in the Coinbase UI.

- **Preflight** aborts the whole run if any `PublicKey` fails a **32-byte** decode, any `SOLAmount` is
  below `minAmountSol`, an address repeats, or the Coinbase balance does not cover **batch total +
  `networkFeeHeadroomSol`**.
- ⚠️ **`SOLAmount` is what the destination receives.** Coinbase charges its network fee **on top**,
  against the account — that is what the headroom exists for.
- **Idempotency:** each wallet's `idem` UUID is **derived deterministically** from the plan hash +
  address + lamports, so a repeat run is deduplicated **server-side even when the receipts file is
  gone**. The plan hash covers **addresses and amounts only** — re-ordering the pool, renaming a
  `Label` or reformatting is not a plan change.
- ⚠️ **Editing an amount or an address between runs changes that wallet's derived idem**, so a resend
  becomes a genuinely new Coinbase transaction. The tool **refuses to resume a drifted plan** unless
  `--force-plan-change`. **This is the one path that can double-fund.**
- ⚠️ **Card #167 added a second guard on the same failure mode:** moving the default receipts path into
  `tools/Receipts/` means a legacy `tools/receipts.json` the tool cannot see would read as "no prior
  run". `Domain/LegacyReceipts.cs` makes that a **fatal** startup error.
- A wallet holding a Coinbase transaction id is **polled, never re-sent**. The receipts JSON is written
  **after every state change** — it *is* the resume state.
- **Settlement:** `Confirmed` requires Coinbase `completed` **and** an observed on-chain **balance
  delta** against a pre-send snapshot — so a wallet that already held SOL is handled and a concurrent
  deposit cannot mask a failure. `completed` with no observed credit is **`Unconfirmed`**, not funded.
- **Spread:** randomized wallet order at randomized offsets across `spreadMinutes` (`0` ⇒ back to
  back), **amounts exact** (the sniper's supply percentages assume exact SOL). The reason is
  anti-correlation, not secrecy: a Coinbase send leaves Coinbase's **shared hot wallets**, so the sender
  does not identify the account — **clustering** is what makes the pool legible as one operation.
- A `401`/`403` (revoked key, missing `wallet:transactions:send` scope, blocked by address allowlisting)
  **aborts the whole run immediately** with no retry loop — every remaining wallet would fail
  identically. `429`/`5xx`/transport retry with backoff; retrying the send POST is safe **precisely
  because** it carries its deterministic `idem`.
- ⚠️ **Travel Rule data is mandatory where the account's jurisdiction requires it.** `travel_rule_data`
  rides at the **top level** of the send body with the country **nested** as
  `beneficiary_address.country` — Coinbase names the missing param `BENEFICIARY_COUNTRY`, but a
  top-level field of that name does not exist. Blank optional fields are **omitted**, never sent empty.
  `beneficiaryWalletType` / `isSelf` are validated **at load**, so a typo fails before the run. Without
  it the API answers `400 invalid_request` — *"Incomplete travel rules information provided"* — and
  **nothing is sent, no `idem` is consumed**, so a fixed re-run resumes safely. Separately, **EU
  self-hosted sends above ~€1000 need a Satoshi Test** (a one-time per-address ownership proof in the
  Coinbase app) that no API field substitutes for.
- **Scope:** SOL on mainnet only — `currency`/`network` are hard-coded so a wrong config line cannot
  send the wrong asset to the right address.

## 2. `spacestroker-snipe-token` — buying

Drives the pool to a configured share of **our own pump.fun token's** supply: one buy per wallet at
randomized offsets across a window (default 60 min), then writes HTML + JSON achieved-vs-target reports.

- ⚠️ **`SupplyPercentage` is a percent number of TOTAL supply**: `0.4504` means **0.4504%**, never
  45.04%. It resolves against the mint's on-chain supply read at preflight, truncated so a wallet is
  never planned above its share.
- A wallet's buy is its **shortfall** — target minus current holding — so re-running tops up.
- The ladder is quoted **sequentially**: each wallet is priced against the reserves its predecessors
  left behind. Constant-product over the curve's **virtual** reserves, platform + creator fees on top;
  costs round **up**, token amounts round **down**.
- **The supply target wins over budget.** No global SOL cap: a wallet that cannot afford its shortfall
  buys the inverse-priced maximum it can and is reported **`Short`**. A per-wallet `reserveLamports` is
  withheld so every wallet can still pay its fee and ATA rent.
- ⚠️ **Every transaction carries `maxSolCost`** = quote × (1 + `slippageTolerance`), capped at what the
  wallet can pay. That is not a budget cap — it is what stops a buy being **sandwiched** — and the
  instruction cannot be built without it.
- **One buy per wallet in ascending `alias` order** (ordinal) — ⚠️ a deliberate change from the card's
  "largest-supply-first". Only the **gaps** jitter. `alias` is **required and unique** (it is the
  ordering key); mixed padding (`W2` beside `W10`) is a **warning** — ordinal sorting puts `W10` first.
- ⚠️ **On-chain balance is the resume truth; receipts are only an audit trail.** Solana has no
  server-side idempotency token, so a receipts-driven resume could double-buy after a crash between
  confirmation and write. Resume recomputes the shortfall from **live balances**, which makes every
  re-run idempotent by construction.
- **Bonding curve only** — preflight refuses a graduated CA; mid-run graduation stops the remainder and
  reports where.
- ⚠️ **The account list was re-derived from mainnet** (the program's own on-chain Anchor IDL plus
  decoded live buys) and is pinned in `PumpFunProgramTests` — if pump.fun redeploys, **that test is what
  tells you**. It corrected five spec assumptions: the curve's quote side is `*_quote_reserves` (not
  `*_sol_reserves`); `buy` takes a **third arg** `track_volume` (three encodings all land); **live buys
  carry 18 accounts against the IDL's 16** (we send the declared 16, with a `snipe.extraAccounts` escape
  hatch — the failure mode is a reverted tx, not lost SOL); **the token program varies per mint** (SPL
  Token *and* Token-2022 are both live, so ATAs are derived by hand — Solnet's helper hardcodes SPL
  Token); and **a curve need not be SOL-quoted** (`quote_mint` — non-SOL ones are refused rather than
  mispriced). Fee rates are `global`-account config (95 + 5 bps), read at runtime.
- **Disclosure:** the pool stays **undisclosed**, superseding card #63's own comment. **Detection-evasion
  features are explicitly out of scope.**

## 3. `spacestroker-nightfall` — selling

A **localhost-only ASP.NET host** serving a static `wwwroot/` dashboard (no JS build step, so `tools/`
stays a pure .NET solution): see each wallet's SOL and token position, and sell one or many
concurrently.

**Where the keys live**
- ⚠️ **The host holds the private keys in-process; the browser receives only public data.** The wallet
  DTO has **no key member at all**, so no serializer setting or later refactor can leak one. This is why
  the tool is a local host with a browser UI rather than a static frontend — a page that can sell would
  otherwise carry the keys into the bundle and devtools.
- **Binds `127.0.0.1` only.** There is no authentication and none is needed *because* of that bind;
  widening it turns the tool into a remote wallet-drainer.

**Arming — the guardrails are the feature**
- ⚠️ **Every launch starts in `SIMULATE`, never persisted** — a fresh process is never armed.
  `SIMULATE` quotes and builds the real transaction and broadcasts nothing. The check is **server-side**
  (`SellRunner.ExecuteAsync`), so a browser that posts a sell to a simulating host still sells nothing.
- ⚠️ **A click selects; it never sells.** Selling is always the explicit `SELL SELECTED` plus a yes/no
  confirmation showing per-wallet amounts and the batch's quoted SOL total. The card asked for
  click-to-sell; that was changed deliberately, because click would otherwise mean two different things
  depending on whether the mouse moved.
- One **`SELL %`** field applies to the selection. ⚠️ **Blank ⇒ 100%**, and the percentage is of **each
  wallet's own token balance, never of mint supply**. The amount is **floored** (rounding up would ask
  the chain to move more than the wallet holds) and **recomputed from a fresh balance read at
  execution** — the poll is up to `refreshIntervalSeconds` stale, and "100%" must mean 100% of what is
  there now.

**Gates, all before anything is signed**
- A wallet holding **none** of the token is excluded and never quoted (Jupiter has no route for zero).
- A wallet with **SOL below `minSolForFeeSol`** is flagged, excluded, and told to top up with the
  funder. The floor is **inclusive**.
- ⚠️ A quote whose **price impact exceeds `maxPriceImpactPercent`** aborts **that wallet** before
  broadcasting. Slippage alone would not cover this: a wallet can dump into near-zero liquidity at a
  terrible price that is still inside tolerance.
- `SwapInstructionsGuard` checks the assembled swap **before a signature exists**: every program id on
  an allowlist (Jupiter v6, SPL Token, Token-2022, ATA, ComputeBudget, System), every instruction
  involving this wallet, the input mint matching config, the output mint being SOL, and the quoted
  amount matching the plan.

**Executing**
- **Jupiter builds the swap; our own keyed RPC sends it.** Jupiter never sees a private key and never
  touches the chain. Every read, broadcast and confirmation goes through `solana.rpcUrl`.
- Concurrent, capped at `maxConcurrentSells` (default 4) — Jupiter and the RPC both rate-limit. A wallet
  **already in flight cannot be submitted again**.
- **Retryable** (429, transport, expired blockhash, confirmation timeout) retries with backoff + jitter.
  **Terminal** (slippage, no route, insufficient balance, guard failure, any other 4xx) surfaces with its
  reason and leaves the wallet re-selectable.
- ⚠️ **A confirmation timeout records `Unconfirmed`, not `Failed`**, and keeps the signature — the
  transaction may still land, and the operator needs to check rather than assume.
- **Realized SOL** is read from the confirmed transaction's own **balance deltas** (`post − pre + fee`,
  fee added back because the wallet is the fee payer), not from the quote.
- A 100% sell closes the emptied token account in a **separate follow-up transaction** — appending it to
  the swap could revert the sale.
- **No resume gate** — on-chain state *is* the resume state.

⚠️ **The primary transaction path did not survive implementation.** Solnet 6.1.0 has **no
versioned-transaction or address-lookup-table support**, and Jupiter routes routinely need ALTs — so the
swap is **not** assembled locally. Jupiter's `/swap` supplies the ready v0 transaction and
`TransactionSigner` signs it **without parsing**, with `/swap-instructions` fetched only to feed the guard.

## Verification status

⚠️ **Read this before trusting a green test run.** The suites cover the **pure** logic thoroughly, but
large parts of the live paths have never executed:

- **Funder** — exercised live 2026-08-11 (3-wallet, 0.6 SOL): JWT auth, SOL account resolution, balance
  preflight, plan table and typed-total confirmation all worked. The batch **stopped at the first send on
  the Travel Rule `400`** (which is what prompted travel-rule support). **No SOL moved, no `idem`
  consumed.** Still unexercised: on-chain confirmation and the unconfirmed case, partial failure, missing
  scope / allowlist — and the travel-rule body has been **built to spec but never accepted** by Coinbase.
- **Sniper** — shipped **without a live `--execute` buy ever running**: the submit/confirm path, the
  ATA-creation branch, the slippage revert and the mid-run graduation stop are unexercised, and a live
  run is the only way to settle the 16-vs-18 account question. It **is** live-verified read-only (dry run
  + `status` against mainnet: supply, Token-2022 detection, fee decode, curve decode, ladder), and the
  graduation refusal fired for real when the fixture token graduated mid-session.
- **Nightfall** — several ACs have never run against a wallet holding tokens.
- There is **no fake Coinbase sender** and no fake Jupiter, by decision — the live paths are verified by
  dry-running against real keys.
- The mandatory backend integration test was **explicitly waived** for all three: zero backend surface,
  so there is no code path for such a test to drive.

## Build gotchas

- **.NET 8 has no Ed25519 in the BCL.** The CDP bearer token is a hand-rolled JWS signed with
  BouncyCastle. ⚠️ The CDP secret decodes to **64 bytes** (32-byte seed ‖ 32-byte public key) and
  `Ed25519PrivateKeyParameters` wants the **first 32 only**. With `keyAlgorithm: ecdsa`, JWS needs raw
  **`r‖s`** (`IeeeP1363FixedFieldConcatenation`), not .NET's default DER. Each of these produces a silent
  `401`.
- **Use `Portable.BouncyCastle` 1.9.0, not `BouncyCastle.Cryptography` 2.4.0** — Solnet pulls 1.9.0 in
  transitively, and both on the graph makes every `Ed25519*` type ambiguous (`CS0433`).
- ⚠️ **Solnet's `new PublicKey(string)` is not validation.** Preflight uses `PublicKey.IsValid` **plus** a
  32-byte decode assertion, because a truncated address is the irreversible failure.
- **Solnet.Programs 6.1.0 has no `ComputeBudgetProgram`** — the priority-fee instructions are hand-built
  (`[2, u32]` limit, `[3, u64]` price).
- **The .NET 10 SDK writes `.slnx`** — `dotnet new sln` needs `--format sln` for the classic format.
- A **missing account is a zero balance, not an error**; `preflightCommitment` must match the blockhash
  commitment; never route money through a `double`.

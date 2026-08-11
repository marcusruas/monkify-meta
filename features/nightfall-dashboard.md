# Feature — Nightfall dashboard

> Status: **shipped.** Authoritative spec: [`../specs/nightfall-dashboard.md`](../specs/nightfall-dashboard.md)
> (merged via `feature/nightfall-dashboard` on 2026-08-11). Trello
> [#166](https://trello.com/c/mUDhAdBu).
>
> ⚠️ **Ops tooling, not part of the deployed app.** Nothing here runs in Cloud Run, in the local
> Docker stack, or in any request path. It is a local app an operator runs by hand, and **it sells
> real holdings — a sale cannot be undone.**

## What it does

`spacestroker-nightfall` is the **exit** side of the ghost-wallet pool: [#165](https://trello.com/c/9OoHAuEa)
funds it with SOL, [#63](https://trello.com/c/nokWHm7z) buys the supply, and this sells it. It is a
**localhost-only** ASP.NET host serving a static dashboard that shows every wallet's alias, SOL
balance, share of mint supply, token amount and estimated SOL value — and sells one wallet or many
**concurrently** behind a confirmation.

Before it existed, seeing what the pool held and exiting it meant opening a wallet app per wallet:
slow, error-prone, and impossible to do concurrently.

## Core rules

**Where the keys live**
- The **host holds the private keys in-process**; the browser receives **only public data**. The
  wallet DTO (`Application/NightfallState.WalletView`) has **no key member at all**, so no serializer
  setting or later refactor can leak one. This is why the tool is a local host with a browser UI
  rather than the static frontend the card first described — a page that can sell must otherwise
  carry the keys into the bundle and devtools.
- The host binds **`127.0.0.1` only**. There is no authentication and none is needed *because* of
  that bind; widening it turns the tool into a remote wallet-drainer.
- A private key never appears in a log, a receipt, an HTTP response, or an error message.

**Arming**
- Every launch starts in **`SIMULATE`**, and the mode is **never persisted** — a fresh process is
  never armed. `SIMULATE` quotes and builds the real transaction and broadcasts nothing.
- The check is **server-side** (`SellRunner.ExecuteAsync`): a browser that posts a sell to a
  simulating host still sells nothing.
- **A click selects; it never sells.** Selling is always the explicit `SELL SELECTED` action plus a
  yes/no confirmation showing per-wallet amounts and the batch's quoted SOL total. The card asked for
  click-to-sell; that was changed deliberately, because click would otherwise mean two different
  things depending on whether the mouse moved.
- Selection is click / ctrl-click (add) / shift-click (range) / drag-marquee / Select all / Clear.

**How much sells**
- One **`SELL %`** field applies to the whole selection. **Blank ⇒ 100%.**
- The percentage is of **each wallet's own token balance**, never of mint supply: a wallet holding 2%
  of supply at `SELL % = 40` sells 40% of its own balance — 0.8% of supply.
- The amount is **floored** (`SellPlan.AmountFor`); rounding up would ask the chain to move more than
  the wallet holds. A percentage that rounds to zero tokens is refused rather than selling nothing.
- The amount is recomputed from a **fresh balance read at execution**, never the polled figure — the
  poll is up to `refreshIntervalSeconds` stale, and "100%" must mean 100% of what is there now.

**Gates, all before anything is signed**
- A wallet holding **none** of the token is excluded and never quoted (Jupiter has no route for zero,
  so quoting it only burns rate limit).
- A wallet with **SOL below `minSolForFeeSol`** is flagged, excluded, and told to top up with
  `spacestroker-fund-wallets`. The floor is inclusive — a wallet funded to exactly that amount sells.
- A quote whose **price impact exceeds `maxPriceImpactPercent`** aborts **that wallet** before
  broadcasting; the rest of the batch proceeds. Slippage alone would not cover this: a wallet can dump
  into near-zero liquidity at a terrible price that is still inside tolerance.
- The assembled swap is checked by `SwapInstructionsGuard` **before a signature exists**: every
  program id on an allowlist (Jupiter v6, SPL Token, Token-2022, ATA, ComputeBudget, System), every
  instruction involving this wallet, the quote's input mint matching config, the output mint being
  SOL, and the quoted amount matching the plan.

**Executing**
- **Jupiter builds the swap; our own keyed RPC sends it.** Jupiter never sees a private key and never
  touches the chain — it supplies route data and the wire transaction. Every read (SOL, token
  balances, mint supply) and every broadcast and confirmation goes through `solana.rpcUrl`.
- Selected wallets sell **concurrently**, capped at `maxConcurrentSells` (default 4) — Jupiter and the
  RPC both rate-limit, and an unbounded burst becomes 429s. Each wallet is an independent transaction.
- A wallet **already in flight cannot be submitted again** (`NightfallState.TryBeginSell`).
- **Retryable** (429, transport, expired blockhash, confirmation timeout) retries with exponential
  backoff + jitter. **Terminal** (slippage, no route, insufficient balance, a guard failure, any other
  4xx) is surfaced with its reason and leaves the wallet re-selectable. **The batch never stops** for
  one wallet.
- A confirmation timeout records **`Unconfirmed`, not `Failed`**, and keeps the signature — the
  transaction may still land, and the operator needs to check rather than assume.
- **Realized SOL** is read from the confirmed transaction's own balance deltas
  (`post - pre + fee`, fee added back because the wallet is the fee payer), not from the quote. Both
  are kept, so the gap between them is the slippage a concurrent batch actually paid.
- On a **100% sell only**, once confirmed, a **separate follow-up transaction** closes the emptied
  token account and reclaims its rent. It is separate on purpose: appending `closeAccount` to the swap
  would let a failed close revert the sale itself. A failed follow-up costs only the rent and never
  marks the sale failed. A partial sell leaves the account alone.

**What the operator sees**
- Card face: alias · SOL · **% of total mint supply** · token amount · a compact **estimated** SOL
  value. The estimate comes from **one** Jupiter quote per refresh cycle against the pool's median
  holding — not one quote per wallet, which would exhaust the free tier within minutes. It therefore
  ignores per-wallet price impact and is labelled an estimate.
- **Hover** gives the real per-wallet quote for the current `SELL %` (SOL out, price impact, exact
  token amount) and, once sold, the **realized SOL** and the signature. Hover is debounced 300 ms and
  the quotes are cached (`quoteCacheSeconds`) with single-flight coalescing, keyed on the **amount**
  so two wallets holding the same position share one quote.
- Auto-poll every `refreshIntervalSeconds` in **two RPC calls for the whole pool** (one
  `getMultipleAccounts` for lamports, one over derived ATAs), plus a manual refresh and an immediate
  refresh of any wallet right after its sell confirms. A wallet **mid-sell is not overwritten** by a
  poll, so its row cannot flicker between "selling" and a stale balance.
- `tools/nightfall-receipts.json` is rewritten **after every state change** — token amount sold,
  quoted **and** realized SOL, signature, status, timestamp. It is the audit trail.

**Config and scope**
- `tools/secrets.nightfall.json` (git-ignored; `secrets.nightfall.example.json` is the tracked
  placeholder-only template). It binds `Alias` / `PublicKey` / `PrivateKey` and **ignores unknown
  fields**, so #63's ghost-wallet pool file pastes in verbatim.
- Every wallet is validated **at load**: the address decodes to a real 32-byte key, and the private
  key **derives** to the stated address. A mismatch is otherwise an opaque on-chain signature failure
  mid-batch, after a quote has been paid for.
- **Mainnet only**, one Token CA per secrets file. An empty pool is a clean no-op that reaches no
  external service and exits `0`.
- Exit codes: **`0`** clean shutdown / `--help` / empty pool · **`1`** fatal (unreadable secrets,
  invalid pool, unreadable mint) · **`2`** usage error.
- **No resume gate.** Unlike `spacestroker-fund-wallets` there is no plan hash and no
  `--force-plan-change`: a sell is never "resumed" — the wallet either still holds tokens (sell again)
  or does not. On-chain state *is* the resume state; the receipts file is the audit trail.

## Key code touchpoints

All on `main` in **`monkify-meta`** — there is no backend or frontend surface.

- `tools/SpaceStroker.Tools.sln` — `SpaceStroker.Nightfall` + `.Tests` joined the funder's solution.
- `tools/SpaceStroker.Nightfall/Program.cs` — entry point; maps `NightfallException` → 1,
  `UsageException` → 2; binds `127.0.0.1`; reads the mint once at startup (fatal on failure).
- `tools/SpaceStroker.Nightfall/Domain/` — **IO-free**: `WalletPool` (parsing, address + keypair
  validation), `TokenAmount` (exact-decimal conversion, invariant formatting), `SellPlan`,
  `SellPreflight`, `SwapInstructionsGuard`, `SellError`, `RealizedSol`, `SellReceipt`,
  `NightfallException`.
- `tools/SpaceStroker.Nightfall/Infrastructure/` — `SecretsFile`, `ReceiptsStore`,
  `Jupiter/` (`JupiterClient`, `JupiterQuote`, `QuoteCache`), `Solana/` (`SolanaReader`,
  `TransactionSigner`, `SolanaSender`, `TokenAccountCloser`, `RpcUrlMask`).
- `tools/SpaceStroker.Nightfall/Application/` — `CliOptions`, `NightfallState` (+ `WalletView`),
  `SellRunner`, `PollLoop`, `ApiEndpoints` (+ `EventStream`), `ConsoleReport`.
- `tools/SpaceStroker.Nightfall/wwwroot/` — `index.html` / `app.css` / `app.js`, the arcade-cabinet
  dashboard (mockup 1b), no build step.
- `tools/SpaceStroker.Nightfall.Tests/` — 112 tests mirroring the same folders.
- `tools/README.md` — the `spacestroker-nightfall` section: preconditions, SIMULATE-first workflow,
  config table, exit codes.

## Gotchas worth knowing

- ⚠️ **Solnet 6.1.0 cannot build a versioned transaction.** It exposes only legacy `Message` /
  `Transaction` / `TransactionBuilder` — no `VersionedTransaction`, no `MessageV0`, no
  address-lookup-table type. Jupiter routes routinely need lookup tables, so the swap is **not**
  assembled locally: Jupiter's `/swap` returns the ready v0 wire transaction and
  `Infrastructure/Solana/TransactionSigner` signs it **without parsing** — a signature covers the
  message bytes and the wire format is `[shortvec count][64-byte signature]…[message]`, so splicing
  into slot 0 is correct for legacy and v0 alike. `/swap-instructions` is still fetched, purely as the
  input to `SwapInstructionsGuard`: the guard vets the semantics, the signer vets the shape.
- ⚠️ **`new Account(privateKey, publicKey)` does not derive anything.** Solnet simply stores the
  address you hand it, so comparing `account.PublicKey` against the configured address always passes.
  `WalletPool.ValidateKeyPair` derives from the seed with `Ed25519PrivateKeyParameters` — and wants
  the **first 32** of the 64 secret bytes, the same 32-of-64 lesson
  [`coinbase-wallet-funding.md`](coinbase-wallet-funding.md) recorded for the CDP key.
- ⚠️ **Jupiter's `priceImpactPct` is a fraction, not a percentage** (`"0.0123"` = 1.23%). It is
  multiplied by 100 on the way in; reading it literally would make the price-impact abort ~100× too
  permissive, i.e. it would never fire.
- ⚠️ **A fire-and-forget batch must not run under the request's `CancellationToken`.** That token is
  `HttpContext.RequestAborted` and fires the moment the response completes, so the API returns a batch
  id and nothing ever happens. `SellRunner` takes the **application lifetime** token. For the same
  reason its concurrency `SemaphoreSlim` lives inside the background task, not in `RunAsync`.
- **`tools/.gitignore` needed two new rules.** `receipts*.json` anchors at the start of the name, so
  it missed `nightfall-receipts.json` (hence `*receipts*.json`); and `secrets*.json` swallowed
  `secrets.nightfall.example.json`, so the tracked template needed its own negation. The
  `monkify-meta` root `.gitignore` covers only `sales/pdf/` — `tools/.gitignore` stays load-bearing.
- **Jupiter's API base moved** off `quote-api.jup.ag/v6` to `lite-api.jup.ag/swap/v1` (free) and
  `api.jup.ag/swap/v1` (keyed). It is `jupiter.apiBaseUrl` in config precisely so another move needs
  no code change.
- **Token balances are read from raw account data**, not `getTokenAccountBalance`: the SPL layout puts
  the u64 amount at byte 64, so one `getMultipleAccounts` covers the whole pool instead of one round
  trip per wallet. A missing account reads as **zero, not an error** — the same distinction
  `SolanaService`'s retry predicates make in `monkify-backend`.

## Verification status

The 112-test suite covers the **pure** logic end to end: the sell-percentage maths, the exclusion
gates, the instruction guard, retryable-vs-terminal classification, the realized-SOL parse, the
receipt state machine, config loading (including the keypair derivation), the quote cache's TTL and
coalescing, and transaction signing verified against a real ed25519 signature. There is **no fake
Jupiter and no fake RPC** by decision, matching `coinbase-wallet-funding`.

**Exercised live (2026-08-11, read-only against mainnet):** the mint read (real USDC supply and
decimals), batched balance reads, static-file serving, the zero-balance gate rejecting a wallet, the
receipt written with no private key, SSE delivery, and the mode toggle. **No money moved.**

⚠️ **Never exercised against a wallet holding tokens:** AC07 (realized SOL on hover), AC14 (a live
signed sell), AC15 (the concurrency cap under load), AC19 (the fresh-balance recompute), AC23 (the
follow-up account close), AC24 (a partial sell) and AC26 (crash mid-batch). Every one requires a real
mainnet sale, which `constraints.md` §5 makes an operator step — the spec's §14 holds the sequence.

The mandatory happy-path integration test in `SpaceStroker.Tests.Integration` was **explicitly
waived** on the card: the tool has zero backend surface, so there is no code path for that suite to
drive (same waiver as [`coinbase-wallet-funding.md`](coinbase-wallet-funding.md) and
[`../specs/main-production-operator.md`](../specs/main-production-operator.md)).

## Related

- [`../specs/nightfall-dashboard.md`](../specs/nightfall-dashboard.md) — authoritative design.
- [`coinbase-wallet-funding.md`](coinbase-wallet-funding.md) — `spacestroker-fund-wallets` (#165)
  funds the same pool; Nightfall's low-SOL exclusion points operators back at it.
- Trello [#63 "Create ghost wallet snipe app"](https://trello.com/c/nokWHm7z) — buys the supply
  Nightfall sells; its pool file loads here unchanged.
- [`../conventions/backend.md`](../conventions/backend.md) → "The folder tree must show the
  architecture" — the Domain/Infrastructure/Application split this project follows.

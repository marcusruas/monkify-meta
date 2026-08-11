# Feature — Coinbase wallet funding

> Status: **shipped.** Authoritative spec: [`../specs/coinbase-wallet-funding.md`](../specs/coinbase-wallet-funding.md)
> (merged via `feature/coinbase-wallet-funding` on 2026-08-11). Trello
> [#165](https://trello.com/c/9OoHAuEa).
>
> ⚠️ **Ops tooling, not part of the deployed app.** Nothing here runs in Cloud Run, in the local
> Docker stack, or in any request path. It is a console app an operator runs by hand, and **it spends
> real SOL**.

## What it does

`spacestroker-fund-wallets` funds a pool of Solana wallets with **SOL** out of a Coinbase account, so
the ghost-wallet pool that card [#63](https://trello.com/c/nokWHm7z) will drive holds SOL before it has
to buy anything. It replaces funding a dozen-plus wallets by hand in the Coinbase UI — which is slow,
irreversible when an address is mistyped, and stamps the whole pool with one funding timestamp.

A run is **dry by default**, **double-send-proof**, **resumable**, **confirmed on-chain**, and
**spread over time**.

## Core rules

**Money gate**
- A run sends nothing unless it is invoked with **`--execute`** *and* the operator types the **exact
  batch total** at the prompt. Any mismatch (including a comma decimal, `12,75`) sends nothing and
  exits non-zero.
- Preflight runs **before the first send** and aborts the whole run on any failure: every `PublicKey`
  decodes to a valid **32-byte** Solana address, every `SOLAmount` is positive and at or above
  `minAmountSol`, no address appears twice, and the Coinbase SOL balance covers **batch total +
  `networkFeeHeadroomSol`**. All problems are reported at once, not the first one.
- `SOLAmount` is **what the destination receives**. Coinbase charges its network fee on top, against
  the account — that is what the headroom in the balance check exists for.
- An empty pool is a no-op with a clear message and **exit 0**; it never reaches Coinbase.

**Idempotency and resume**
- Each wallet's `idem` UUID is **derived deterministically** from the plan hash + address + lamports,
  so a repeated run reuses the same token and **Coinbase deduplicates server-side even when the local
  receipts file is gone**.
- The **plan hash** covers addresses and amounts only. Re-ordering the pool file, renaming a `Label`,
  or reformatting is **not** a plan change and does not invalidate a resume.
- ⚠️ **Editing an amount or an address between runs changes that wallet's derived idem**, so a resend
  would be a genuinely new Coinbase transaction. The receipts file stores the plan hash and the tool
  **refuses to resume a drifted plan** unless `--force-plan-change` is passed. This is the one path
  that can double-fund.
- A wallet that already holds a Coinbase transaction id is **polled, never re-sent** — on resume and in
  the read-only `status` command alike.
- The receipts JSON is written **after every state change**, not at the end, so a crash cannot lose a
  transaction id. It *is* the resume state.

**Sending and settlement**
- Sends are dispatched in **randomized wallet order at randomized offsets** across `spreadMinutes`
  (`0` ⇒ back to back). Amounts are sent exactly as specified — there is no amount jitter, because
  #63's supply percentages assume exact SOL.
  The reason is anti-correlation, not secrecy: a Coinbase send leaves Coinbase's **shared hot
  wallets**, so the sender does not identify the account — **clustering** (N deposits in one minute
  that then all buy the same token) is what makes the pool legible as one operation.
- A wallet counts as **`Confirmed` only when Coinbase reports `completed` *and* the destination's
  on-chain balance actually rose**. The check is a **delta against a pre-send snapshot**, so a wallet
  that already held SOL is handled and an unrelated concurrent deposit does not mask a failure.
  Coinbase `completed` with no observed credit is recorded **`Unconfirmed`** — not funded.
- One wallet's failure **never stops the batch**: the rest are funded, the error is recorded, the
  process exits non-zero, and a re-run targets only the unfunded.
- A `401`/`403` — revoked key, missing `wallet:transactions:send` scope, or a destination blocked by
  Coinbase address allowlisting — **aborts the entire run immediately** with an actionable message and
  no retry loop, because every remaining wallet would fail identically.
- Transport errors, `429` and `5xx` retry with exponential backoff + jitter. Retrying the **send POST
  is safe** precisely because it carries its deterministic `idem`; any other `4xx` is terminal.

**Scope and safety**
- **SOL on Solana mainnet only.** `currency`/`network` are hard-coded so a wrong config line cannot
  send the wrong asset to the right address.
- The wallet pool binds **only** `PublicKey`, `SOLAmount` and an optional `Label`; **unknown JSON
  fields are ignored**. #63's ghost-wallet pool file pastes in verbatim and this tool never binds,
  holds or logs a `PrivateKey`.
- The CDP API secret and the minted JWT are **never** printed, logged, or written to receipts. Key ids
  are printed masked.
- Exit codes: **`0`** all confirmed / dry run / empty pool · **`1`** anything unresolved (failed,
  unconfirmed or still-pending wallet, or a fatal error) · **`2`** usage error.

**Operator preconditions** (outside the tool, required before a live run)
- The CDP key carries the **`wallet:transactions:send`** scope.
- Destination wallets are **allowlisted** if the Coinbase account has address allowlisting enabled.
- The account already holds enough SOL — the tool never buys.

## Configuration

`tools/secrets.json` (**git-ignored**; `tools/secrets.example.json` is the tracked placeholder-only
template). Sections: `coinbase` (`apiKeyId`, `apiKeySecret`, `keyAlgorithm` = `ed25519`\|`ecdsa`,
optional `accountId`, `apiHost`, `apiVersion`), `solana.rpcUrl`, `funding`
(`spreadMinutes`, `networkFeeHeadroomSol`, `minAmountSol`, `sendPollTimeoutSeconds`,
`chainConfirmTimeoutSeconds`), and `wallets[]`.

⚠️ **`tools/.gitignore` is load-bearing.** The `monkify-meta` root `.gitignore` ignores only
`sales/pdf/`; secrets safety in `deploy/` comes from a nested `.gitignore`, and `tools/` needs its own
or a live withdrawal credential becomes committable.

## Key code touchpoints

All on `main` in **`monkify-meta`** — there is no backend or frontend surface.

- `tools/SpaceStroker.Tools.sln` — the solution; card #63 will join it.
- `tools/SpaceStroker.WalletFunder/Program.cs` — entry point; maps `FunderException` → 1,
  `UsageException` → 2.
- `tools/SpaceStroker.WalletFunder/Domain/` — **IO-free**: `WalletPool` (parsing, lamports, invariant
  formatting), `FundingPlan` (plan hash + `DeriveIdem`), `Preflight` (validation; takes the balance as
  a `decimal`), `SendSchedule` (randomized order + jittered offsets, injectable `Random`),
  `ReceiptEntry` (`ReceiptsDocument` + resume/never-re-send/plan-drift rules), `FunderException`.
- `tools/SpaceStroker.WalletFunder/Infrastructure/` — `SecretsFile`, `ReceiptsStore`,
  `Coinbase/CoinbaseJwt`, `Coinbase/CoinbaseClient`, `Solana/SolanaConfirmer`.
- `tools/SpaceStroker.WalletFunder/Application/` — `Funder` (the run), `CliOptions`, `ConsoleReport`.
- `tools/SpaceStroker.WalletFunder.Tests/` — 60 tests mirroring the same folders.
- `tools/README.md` — operator preconditions and the dry-run-first workflow.

## Gotchas worth knowing

- **.NET 8 has no Ed25519 in the BCL.** The CDP bearer token is a hand-rolled JWS signed with
  BouncyCastle. The CDP secret decodes to **64 bytes** (32-byte seed ‖ 32-byte public key) and
  `Ed25519PrivateKeyParameters` wants the **first 32 only**. With `keyAlgorithm: ecdsa`, JWS needs raw
  **`r‖s`** (`IeeeP1363FixedFieldConcatenation`), not .NET's default DER. Each of these produces a
  silent `401`.
- **Use `Portable.BouncyCastle` 1.9.0, not `BouncyCastle.Cryptography` 2.4.0.** Solnet pulls 1.9.0 in
  transitively; both on the graph makes every `Ed25519*` type ambiguous (`CS0433`).
- **Solnet's `new PublicKey(string)` is not validation** — it is only ever called on known-good values
  in `monkify-backend`. Preflight uses `PublicKey.IsValid` **plus** a 32-byte decode assertion, because
  a truncated address is the irreversible failure.
- **The `amount` string must be invariant-formatted.** A `pt-BR` machine would otherwise emit `"2,54"`.
- **The .NET 10 SDK writes `.slnx`** — `dotnet new sln` needs `--format sln` for the classic format.

## Verification status

The 60-test suite covers the **pure** logic end to end: plan/idem derivation, preflight, receipts and
resume, the spread schedule, config loading and JWT construction (including an Ed25519 signature
round-trip). There is **no fake Coinbase sender** by decision, so the live path is verified by
dry-running against a real key.

⚠️ **As of shipping, five acceptance criteria have never been exercised against a live CDP key** —
AC1 (dry-run plan table), AC10/AC11 (on-chain confirmation and the unconfirmed case), AC13 (partial
failure) and AC14 (missing scope / allowlist). The spec's Manual QA section holds the live sequence:
single-wallet dust test → idempotency re-run → lost-receipts test → drift test → full pool.

The mandatory happy-path integration test in `SpaceStroker.Tests.Integration` was **explicitly waived**
for this card — the tool has zero backend surface, so there is no backend code path for such a test to
drive (same waiver as [`../specs/main-production-operator.md`](../specs/main-production-operator.md)).

## Related

- [`../specs/coinbase-wallet-funding.md`](../specs/coinbase-wallet-funding.md) — authoritative design.
- [`../conventions/backend.md`](../conventions/backend.md) → "The folder tree must show the
  architecture" — the Domain/Infrastructure/Application rule this tool established and is the worked
  example for.
- Trello [#63 "Create ghost wallet snipe app"](https://trello.com/c/nokWHm7z) — consumes the funded
  pool; joins `SpaceStroker.Tools.sln`.

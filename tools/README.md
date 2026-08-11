# tools — SpaceStroker token/treasury operations

Standalone console tools for token-launch and treasury work. They are **not** part of the deployed
application: nothing here runs in Cloud Run or in the local Docker stack. Built and run by hand, on
purpose — they move real money.

| Project | Binary | What it does |
| --- | --- | --- |
| `SpaceStroker.WalletFunder` | `spacestroker-fund-wallets` | Funds a pool of Solana wallets with **SOL** out of a Coinbase account. |

> Sibling of `monkify-meta/deploy/` (the GCP deploy stack) and deliberately **not** part of it: a
> real-money withdrawal CLI does not belong in the deploy solution or in a repo built into container
> images. Card [#63 "Create ghost wallet snipe app"](https://trello.com/c/nokWHm7z) will add its
> project to this same solution.

---

## spacestroker-fund-wallets

Spec: [`specs/coinbase-wallet-funding.md`](../specs/coinbase-wallet-funding.md) ·
Trello [#165](https://trello.com/c/9OoHAuEa)

### ⚠️ Read this before your first run

- **This tool sends real SOL.** Every guardrail below exists because the failure mode is
  unrecoverable. Do not script around them.
- **Editing the wallet pool between runs can double-fund.** Each wallet's idempotency token is
  derived from the plan (addresses + amounts). Change an amount or an address and the token changes,
  so Coinbase treats a resend as a **new** transaction rather than deduplicating it. The tool detects
  this and refuses to resume; `--force-plan-change` overrides it and accepts that risk.
- **`SOLAmount` is what the destination receives.** Coinbase charges its network fee **on top**,
  against your account — hence `funding.networkFeeHeadroomSol` in the balance preflight.

### Operator preconditions (do these once, outside the tool)

1. Create a **CDP Secret API Key** with the **`wallet:transactions:send`** scope.
   Ed25519 is Coinbase's recommended (and this tool's default) algorithm; ECDSA keys work too via
   `coinbase.keyAlgorithm: "ecdsa"`.
2. If your Coinbase account has **address allowlisting** enabled, allowlist the destination wallets
   first — otherwise the first send fails and the run stops.
3. Make sure the account already holds enough SOL. This tool never buys.
4. Fill in the **`travelRule`** block (below). Coinbase applies the Travel Rule to outbound sends and
   rejects the transaction outright without it.

### Configure

```bash
cp secrets.example.json secrets.json    # secrets.json is git-ignored; the template is not
```

| Field | Meaning |
| --- | --- |
| `coinbase.apiKeyId` / `apiKeySecret` | The CDP key. **Never logged, never echoed, never written to receipts.** |
| `coinbase.keyAlgorithm` | `ed25519` (default) or `ecdsa`. |
| `coinbase.accountId` | Optional. Empty ⇒ the SOL account is resolved by currency. |
| `coinbase.apiVersion` | Sent as the `CB-VERSION` header, which the v2 API requires. |
| `travelRule.*` | Travel Rule data attached to every send — see below. Blank `beneficiaryWalletType` ⇒ nothing is attached. |
| `solana.rpcUrl` | Balance reads only. A keyed endpoint can be pasted in. |
| `funding.spreadMinutes` | Send window; `0` ⇒ back to back. |
| `funding.networkFeeHeadroomSol` | Added to the batch total in the balance check. |
| `funding.minAmountSol` | Floor per transfer; rejects fat-finger dust. |
| `wallets[]` | `PublicKey` + `SOLAmount`, optional `Label`. **Unknown fields are ignored**, so card #63's ghost-wallet pool file can be pasted in verbatim — this tool never binds a `PrivateKey`. |

### Travel Rule

Coinbase attaches Travel Rule data to outbound crypto sends. Send without it where the sending
account's jurisdiction requires it and the API answers **HTTP 400 `invalid_request`, "Incomplete
travel rules information provided"**, naming the missing params — no transaction is created, the
`idem` is not consumed, and a re-run resumes safely once the config is fixed.

| Field | Meaning |
| --- | --- |
| `beneficiaryWalletType` | `WALLET_TYPE_SELF_HOSTED` (a wallet you hold the keys to) or `WALLET_TYPE_EXCHANGE`. **Blank disables the whole block.** |
| `isSelf` | `IS_SELF_TRUE` when you own the destination, else `IS_SELF_FALSE`. |
| `beneficiaryName` | The recipient's name — for a self-send, the Coinbase account holder. |
| `beneficiaryCountry` | ISO 3166-1 alpha-2. Sent nested as `beneficiary_address.country`. |
| `beneficiaryAddress1` / `City` / `State` / `PostalCode` | Optional street address. Blank fields are **omitted**, never sent empty. |
| `beneficiaryFinancialInstitution` | `WALLET_TYPE_EXCHANGE` sends only. |
| `transferPurpose` | Optional free text. |

How much is required depends on where the **sending** account is domiciled: US/Canada self-hosted
sends need only `beneficiaryWalletType`, while GB/EU/Bermuda/Singapore/Hong Kong/Philippines also
require `isSelf`, `beneficiaryName` and `beneficiaryCountry`. Configuring more than your jurisdiction
needs is harmless. `beneficiaryWalletType` and `isSelf` are validated **at load**, so a typo fails
before the run rather than mid-batch.

> ⚠️ **EU self-hosted sends above ~€1000** additionally need a **Satoshi Test** proving you control
> the destination address. That is a one-time verification per address in the Coinbase app — the API
> cannot supply it, and no config here substitutes for it.

### Run

```bash
# 1. Dry run — plan table + full preflight, sends nothing. Always start here.
dotnet run --project SpaceStroker.WalletFunder -- fund

# 2. Live. Requires --execute AND typing the exact batch total when prompted.
dotnet run --project SpaceStroker.WalletFunder -- fund --execute

# 3. Read-only check on a run that was interrupted. Structurally cannot send.
dotnet run --project SpaceStroker.WalletFunder -- status
```

Useful options: `--pool <path>` (use a standalone wallet file, e.g. #63's pool),
`--spread-minutes <n>`, `--secrets <path>`, `--receipts <path>`, `--force-plan-change`.

### What a run does

1. **Plan** — builds an ordered transfer list, hashes it, derives each wallet's deterministic `idem`.
2. **Preflight** — valid 32-byte Solana addresses · no duplicates · amounts ≥ `minAmountSol` ·
   Coinbase balance ≥ batch total + fee headroom · snapshots each destination's on-chain balance.
   **Any failure aborts before the first send.**
3. **Confirm** — dry run stops here. `--execute` requires the typed total.
4. **Send** — randomized order, randomized gaps across the spread window. The process must stay
   alive for the window; being killed is safe (see resume).
5. **Settle** — polls Coinbase to `completed`, then requires an **on-chain balance increase** at the
   destination before marking the wallet `Confirmed`.
6. **Record** — `receipts.json` is written after every state change and **is** the resume state.

A wallet's failure never stops the batch; the run exits non-zero and a re-run targets only the
unfunded wallets. A wallet that already holds a Coinbase transaction id is **polled, never re-sent**.

### Exit codes

| Code | Meaning |
| --- | --- |
| `0` | All wallets confirmed · a dry run · an empty pool. |
| `1` | Anything unresolved: a failed, unconfirmed or still-pending wallet, or a fatal error. |
| `2` | Usage error (unknown option, missing value). |

### Tests

```bash
dotnet test SpaceStroker.Tools.sln
```

The suite covers the **pure** logic — plan/idem derivation, preflight, receipts/resume, the spread
schedule, config loading, JWT construction and the exact send-body JSON (including `travel_rule_data`). There is deliberately **no fake Coinbase sender**: the
live path is verified by dry-running against the real key (see the spec's Manual QA section).

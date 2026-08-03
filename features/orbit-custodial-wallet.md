# Feature — Orbit custodial wallet (1/3)

> Status: **Shipped — on `main` since 2026-08-03.** Authoritative spec:
> `../specs/orbit-custodial-wallet.md` (merged via `feature/orbit-custodial-wallet` into
> `release/orbit` on 2026-07-31, and onto `main` with the whole train on 2026-08-03).
>
> Orbit shipped as a three-card **release train**: `main` deliberately saw none of it until all three
> children were coherent together. #121 → #122 (2026-08-02) → #123 (2026-08-03), then
> `release/orbit` → `main`. Trello
> [#121](https://trello.com/c/R4Qe0jxX), umbrella [#118](https://trello.com/c/yxwdNTrb).

## What it does

Gives a player a house-held **Orbit balance** — the platform's first custody of real player money.
They fund it with fixed on-chain USDC deposits, the balance is tracked by an append-only ledger, and
they can withdraw it to their own wallet on demand. This card is the money foundation;
[#122](orbit-auto-bet-engine.md) is the engine that spends it and is now also on the train.

## Core rules

**Custody and the pool**

- Player money lives in a house-controlled **Orbit account**, a keypair configured at
  `GeneralSettings:Operator:Token:Orbit` and **deliberately separate from the bet/payout keypair**.
  Sharing them would make "player balances vs on-chain balance" uncheckable, and an Orbit-funded bet
  would become a same-account transfer, which confirms on-chain as a **no-op**.
- Every balance and ledger row is stamped with its **pool** — `(OrbitAccount, MintAddress)`. Rotating
  the configured account or mint starts a **new pool at zero** and leaves the old one intact and
  withdraw-only, so a balance is never silently re-pointed at a different pot of money.
- `OrbitBalance.Amount == SUM(OrbitTransaction.Amount)` for that pool and player, over **all** rows
  with **no status filter**. A failed cash-out stays at `−X` and is cancelled by an appended
  `CashOutReversal` at `+X`; rows are never edited or deleted to correct money.
- The **ledger is authoritative**. When the materialized column disagrees, the ledger wins, the column
  is corrected and an `Error` is logged — failing closed would freeze real funds over one bad row.

**Authorization**

- Reads need an **Orbit session**: a stateless HMAC token (wallet + expiry, signed with a config
  secret), ~7 days, carried as `X-Orbit-Session`. It is minted by signing a purpose-scoped
  `orbit-session` challenge from the existing `GET /players/nonce`.
- **Money never moves on a session alone.** A cash-out — and disabling Orbit, which forces one —
  additionally requires a **fresh** `orbit-cashout` ed25519 signature.
- **A deposit is the exception, and deliberately so.** It carries a wallet-signed on-chain transfer
  that is verified against mint, exact amount, receiver and **sender = the session wallet**, so a
  second `signMessage` would prove nothing while costing a wallet round-trip.
- The session **cannot be revoked** before it expires. Contained rather than dangerous: self-exclusion
  is re-checked server-side on every call, and every money movement re-signs.
- A bad signature, an unknown nonce, a replayed nonce and a missing session are **indistinguishable**
  to the caller (`constraints.md` §6). Limit refusals that are ordinary game state — below the minimum
  cash-out, already cashed out today — are **clear**, because a vague message there just makes the
  player retry.

**Deposits**

- Fixed ladder of **1 / 5 / 10**, at most 10 per deposit and 100 per UTC day, and a **per-pool** float
  cap. A cap refusal is vague to the player and an `Error` for the operator.
- **Idempotent by signature**, enforced by a unique index. A replay returns the current balance and
  writes nothing — the client auto-resubmits a stashed deposit, so a replay is a normal event.
- A confirmed transfer whose credit never landed is recovered two ways: a `localStorage` stash replayed
  on the next modal open, and a manual "recover a deposit" field. A **permanently** refused stash
  (any 4xx) is dropped rather than retried forever.
- The first deposit into a fresh Orbit account **creates its associated token account in the same
  transaction**, so that transaction reports no receiver pre-balance. Verification treats a missing
  receiver pre-balance as zero; a missing **sender** pre-balance is still a rejection, since that is
  what stops a player crediting themselves for someone else's transfer.

**Cash-out**

- Always the **whole balance**, always to the **owning wallet** — no caller-supplied destination
  exists in the contract at all. The amount is recomputed from the ledger, never read from the column.
- **Reserved at request**: a `Pending` row plus an atomic conditional debit in one DB transaction, so
  the balance reads `0` immediately and the money cannot be spent twice. **Debit before transfer,
  always.**
- Consumers executes it, claiming the row with `SET Executing WHERE Status = Pending` — a conditional
  update, so at-least-once bus delivery cannot pay twice. A failure appends a `CashOutReversal` and
  restores the balance.
- One manual cash-out per UTC day, and a configured minimum. **Threshold and forced cash-outs are
  exempt from both** — a limit must never trap a player's funds. (#123 added a fifth source,
  **`Floor`**, exempt for the same reason.)
- The hourly sweep re-drives a stuck `Pending` row but **never** an `Executing` one: that row may have
  a transfer in flight, so it is escalated to a human instead.
- ⚠️ **Extended by [#123](orbit-notifications.md):** every completed cash-out — all five sources —
  sends a **Telegram** message if the player has linked a chat. `TelegramChatId` was created on
  `OrbitEnrollment` here, for that card. Failed and reversed transfers send nothing.

**Eligibility, settings and self-exclusion**

- A game type is Orbit-eligible only when `GameParameters.AllowOrbit` is true. Seeded true for
  **Cosmic Sprint** and **Beast Mode**, false for **Space Jackpot** — on a 24h draw an auto-bet would
  lock funds for a day. Config-driven, with a name-matched startup self-heal, plus an admin checkbox at
  creation (never editable afterwards, because the portal is deactivate-or-create).
- **Self-exclusion disables Orbit and force-cashes-out the balance**, exempt from both the daily limit
  and the minimum. The enrolment's game type and threshold are **preserved**, so a lapsed timed
  exclusion resumes in one tap. Re-enabling is refused while it is active.
- ⚠️ **Superseded by [#122](orbit-auto-bet-engine.md):** the enable toggle no longer just persists a
  preference — the engine reads it, and three further rules now hang off it. A deposit is **refused
  while Orbit is off** and while the **disable cool-off** runs (both named refusals, both deferrals
  the client replays); turning Orbit off force-cashes-out and starts that cool-off; and the toggle now
  **prompts for a game type**, saving type and flag in one patch instead of sitting greyed out.

**Frontend**

- `ORBIT` sits in the header menu beside Settings · Stats · Fair · History, with a stacked
  `WALLET` / `ORBIT` balance pair. Signed out, the Orbit row shows `SIGN IN` rather than a number —
  the balance is genuinely unreadable without ownership proof, so showing `0` would be a lie.
- **Hidden entirely when embedded**, and when no Orbit account is configured.

## Known gaps (shipped knowingly)

- ⚠️ **Mobile deeplink wallets cannot sign in.** The `signMessage` deeplink helpers exist, but the
  pending-hop stash in `useMobileWallet.ts` is **per purpose**, and Orbit has none. Desktop extensions
  and wallet in-app browsers work. AC5 is **not met**.
- ⚠️ **The Orbit UI does not use the arcade design system** — no `press-start-2p` / `vt323` /
  `.arcade-panel`. Geometry follows #124's system tier, but it reads as a plain box beside
  `GameFairModal`.
- ⚠️ **Committed config carries a real throwaway DEVNET keypair**, not `MOCK_ORBIT_*` — a deliberate
  exception to `constraints.md` §1 so the local stack works out of the box. AC31 and the card's
  Config bullet still claim otherwise and are **stale**. Consequence: `IsConfigured` treats
  `MOCK_ORBIT_PUBLIC_KEY` as "not configured", so environments used to fail **closed**; with a real
  key committed, an environment whose secrets omit an Orbit override boots with Orbit **enabled**
  against the devnet account. Production must override it explicitly.
- ~~**Deploy plumbing is incomplete**: the two new secret ids are in `secrets.development.json` but not
  yet in `deploy/terraform/workload/main.tf` or `EnvNaming.DerivedIds()`.~~ **Closed by
  [#123](orbit-notifications.md)** — both ids plus the Telegram bot token now reach
  `deploy/terraform/workload/main.tf` and `deploy/secrets.example.json`. ⚠️ `EnvNaming.DerivedIds()`
  turned out to need **no** change: it enumerates the operator's own `secrets.<env>.json` keys, so it
  already validated whatever is configured. The genuine gap was the hardcoded Terraform list and the
  example file, which had no Orbit entries at all.

## Key code touchpoints

**`monkify-backend`**

- Entities — `SpaceStroker.Domain/Orbit/Entities/{OrbitEnrollment,OrbitBalance,OrbitTransaction}.cs`;
  value objects in `Domain/Orbit/ValueObjects/`. `GameParameters.AllowOrbit`.
- Config — `Domain/Configs/Entities/OrbitSettings.cs`. ⚠️ `DepositOptions` is **deliberately
  uninitialized**: `ConfigurationBinder` APPENDS to a pre-initialized array, so a C# initializer plus
  the config entry yields six deposit buttons. Read `AvailableDepositOptions`.
- Services — `Infrastructure/Services/Orbit/`: `OrbitSessionService` (HMAC, `FixedTimeEquals`),
  `OrbitAccountResolver` (⚠️ derives the **ATA** — `SolanaTokenDescriptor.SenderAccount` feeds
  `TokenProgram.Transfer`'s SOURCE and must be a token account, not a wallet), `OrbitLedgerService`,
  `OrbitCashOutReserver`, `OrbitProof`, `OrbitMessages`.
- Handlers — `Infrastructure/Handlers/Orbit/{CreateOrbitSession,GetOrbit,CreditOrbitDeposit,RequestOrbitCashOut,UpdateOrbitSettings}/`.
- Consumers/jobs — `Infrastructure/Consumers/OrbitCashOut/`,
  `Infrastructure/Jobs/{OrbitCashOutSweepJob,OrbitReconciliationJob}.cs`.
- API — `Api/Controllers/OrbitController.cs` (five **public** routes), `Api/Orbit/OrbitSessionAccessor.cs`.
- Solana — `ISolanaService.TransferTokensToWallet` / `ValidateIncomingTransfer` / `GetSolBalance`; the
  pure `SolanaService.DescribeIncomingTransferMismatch`.
- Self-exclusion wiring — `Infrastructure/Handlers/Players/SetSelfExclusion/SetSelfExclusionHandler.cs`.
- Migration `AddOrbitCustodialWallet`; rate-limit rules and the `Orbit` block in
  `Common/spacestrokersettings.json`.

**`monkify-front`**

- `src/hooks/useOrbit.ts`, `src/services/orbitService.ts`, `src/types/orbit.ts`.
- `src/components/OrbitBalance.vue`, `src/components/orbit/OrbitModal.vue`, `src/components/GameHeader.vue`.
- ⚠️ `OrbitModal` dismisses via an **explicit backdrop**, not `useClickOutside` — see
  `../conventions/frontend.md` → "Two overlay tiers".

**Tests** — `SpaceStroker.Tests/UnitTests/{Services,Handlers/Orbit,Api}/Orbit*`,
`IncomingTransferValidationTests`; `SpaceStroker.Tests.Integration/OrbitFlowTests.cs`.
⚠️ `OrbitLedgerService`'s write paths have **no unit coverage by construction** — `ExecuteUpdateAsync`
does not run on the EF in-memory provider — and are proven only by the integration suite.

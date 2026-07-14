# Monkify — "Non-custodial SolanaToken escrow" Feature — Implementation Spec

> **Status: Draft design (brainstorm output)** — NOT yet refined via a Trello card, NOT yet on a
> `feature/` branch, and NOT yet registered in `INDEX.md`. Produced from a design conversation on
> 2026-07-14. Before implementation this should go through the normal pipeline
> (`/monkify-refine-tasks` → `/monkify-create-spec`) so it gets a card, branches/worktrees per repo,
> and an INDEX entry.
>
> Repos that will be affected: **`monkify-backend`**, **`monkify-front`**, a **new Solana program**
> (Rust/Anchor — new artifact, likely a new repo `monkify-escrow-program` or a `program/` folder),
> and this doc in **`monkify-meta`**.
>
> **Purpose:** Make paid **`SolanaToken`** sessions settle through an **on-chain, program-owned escrow**
> so that **no private key Monkify holds can move the pot**. Today, running a `SolanaToken` session with
> an operator's own token requires the operator to hand Monkify the token treasury's **signing private
> key** (stored encrypted, but still custodial — see `payment-methods-refactor.md` §6.6 and
> `RegisterOperatorTokenHandler`). This feature removes that: the pot lives in a **PDA vault** governed
> by program rules, Monkify holds only a **non-spendable resolver authority** that can distribute a
> session's pot *among that session's real bettors and nothing else*, and winners **claim** their share.
> An always-available **permissionless timeout refund** guarantees funds can never be locked by an
> unresponsive authority.

---

## 1. Summary

### 1.1 The problem

`SolanaToken` settlement (shipped in `payment-methods-refactor.md`) is a **commingled treasury float,
custodial** model:

- Players transfer bet tokens **into** the operator's `SenderAccount` (treasury); `SolanaService`
  validates the signature (`RegisterBetHandler.cs`).
- Winner payouts and refunds are signed **out of** that same treasury by
  `SolanaService.TransferTokens`, which builds `new Account(token.TokenOwnerPrivateKey, …)`.
- Therefore the operator's treasury **private key must live in Monkify's DB** (encrypted at rest,
  AES-256-GCM via `ISecretProtector`), and `RegisterOperatorTokenHandler.cs:28` **requires** it.

The private key is needed **only to sign outbound transfers** (Credit/Refund). Accepting a bet needs
only public token data (`Debit` calls `BuildDescriptor(bet, includePrivateKey: false)`). So the entire
custodial surface exists to pay winners.

### 1.2 The change

Replace the treasury-float custodial model with a **non-custodial per-session escrow** for
`SolanaToken` sessions:

| Concern | Before (custodial float) | After (non-custodial escrow) |
| --- | --- | --- |
| Where the pot lives | operator treasury (commingled) | **per-session PDA vault**, owned by a Monkify Solana program; no keypair |
| Who signs payouts | Monkify, using the operator's treasury **private key** | nobody signs a "send" — **winners `claim`** from the vault via program rule |
| Key Monkify holds | operator treasury signing key (can drain) | **resolver authority** only — can set the *winning choice*, **cannot** drain/divert/over-pay |
| Registering a token | operator pastes **private key** (required) | operator registers **public data only** (mint, decimals, label) |
| Refund safety | Monkify-signed refund | **permissionless timeout refund** — any bettor reclaims their stake if unresolved past a deadline |
| Bet deposit | plain SPL transfer to treasury, validated by signature | `place_bet` **program instruction** into the vault |
| Trust in Monkify | full custody of funds | trusted only for the **outcome**, and that is guarded by **provably-fair** (commit-reveal) |

### 1.3 Why the accounting fits cleanly

Payout is already **pot-funded and zero-sum**: `BetDomainService.SetPotAmount` = `Σ bets` (no Monkify
commission), each winner gets `pot / winnerCount`, and every paid bet equals
`SessionParameters.RequiredAmount`. So a session's pot is exactly the sum of its own bets — **no
operator pre-funding is required** for a normal session. (The only exception: **lottery** sessions add a
seeded `LotteryParameters.LotteryPot` base, which the operator must deposit into the vault at session
init — see §5.4 and open question O5.)

Because the typer produces **one** winning sequence per session, there is exactly **one winning choice**;
all bets on that exact choice win and split the pot. This maps perfectly onto an on-chain
`resolve(winning_choice)` + permissionless `claim` where "winner" = "a bet whose recorded choice equals
the session's winning choice" — the program never trusts an enumerated winner list (§5.3).

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` and `AGENTS.md` (full lists there):

- **Worktree-per-repo:** one `feature/<slug>` worktree in **each** affected repo (`monkify-backend`,
  `monkify-front`, the escrow-program repo/folder, `monkify-meta`); hubs stay on `main`; run the
  collision pre-flight. Same branch name in every repo.
- **New feature ⇒ mandatory happy-path integration test** in `Monkify.Tests.Integration`
  (Testcontainers): a full `SolanaToken` **place_bet → resolve → claim** round on a **local
  `solana-test-validator`**, asserting the winner's token balance rises by exactly the pot share and the
  vault is drained. The **refund-timeout** path (complex flow) gets its own integration test. See §11.
- **Unit tests:** update/add in `Monkify.Tests`; **report changed tests** at end of implementation.
  On-chain program logic gets its own Rust/Anchor test suite (`anchor test`).
- **EF mapping** inline in `MonkifyDbContext.OnModelCreating`; **migrations** under
  `Monkify.Infrastructure/Migrations/`, applied **only** by `Monkify.Consumers` at startup; never
  hand-edit the model snapshot.
- **Secrets/config:** the **resolver authority** keypair is a real signing secret — encrypted/kept out of
  source, never logged, never returned by any API. Committed config/seed use **mock keys only**. There is
  **no** operator private key anymore.
- **Money movement stays in `Monkify.Consumers`.** The API stays stateless. `initialize_session` and
  `resolve` are outbound calls from Consumers (session lifecycle / reward flow). `place_bet` is
  client-side; `claim`/`refund` are client-side or permissionless cranks.
- **Real-money / audit:** the program runs on **mainnet** with real value — it **must be security
  audited** before mainnet cutover (Phase 3). Develop and integration-test on **devnet /
  test-validator** only.
- **Operator messaging** stays vague for operator/contract-status errors; player wallet errors
  (insufficient funds, no token account) stay specific.
- **`Standard` (off-chain USD) sessions are untouched.** This feature is `SolanaToken`-only.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **Model = "Approach A": program-owned escrow PDA + non-custodial resolver authority + claim-based payout.** (Fully-trustless on-chain outcome recomputation and multisig resolver were considered and deferred — see §13 / O1.) |
| 2 | **No key Monkify holds can move the pot.** Funds leave the vault only via `claim` (to a real winning bettor) or `refund` (to a real bettor). |
| 3 | **Resolver authority is non-spendable.** Its *only* power is to call `resolve(winning_choice, amount_per_winner, revealed_seed)` once per session. It cannot withdraw, cannot pay non-bettors, cannot exceed the vault. Worst-case abuse = misallocating **among real bettors**, which provably-fair + on-chain bet records make **publicly detectable**. |
| 4 | **Resolution is bound to provably-fair.** `initialize_session` stores the `ServerSeedHash` commitment; `resolve` requires the **revealed server seed** whose hash matches the commitment. The authority cannot resolve without revealing the seed, so the outcome is independently verifiable (ties into `provably-fair.md`). |
| 5 | **Winners `claim` (pull); Monkify never pushes payouts.** A `claim` verifies the bettor's recorded choice equals the session's `winning_choice`, transfers `amount_per_winner` once, and marks the bet claimed. Avoids the per-tx compute ceiling and removes Monkify from the payout path. |
| 6 | **Permissionless timeout refund is mandatory.** If a session is still `Open` after `resolve_deadline`, **any** bettor may `refund` their own stake with no authority involvement. The authority may also `cancel` a session early (→ `Refunding`) for invalid/aborted rounds, which opens refunds immediately. |
| 7 | **Token registration becomes public-only.** `OperatorToken.TokenOwnerPrivateKey` is **removed**; `RegisterOperatorTokenHandler` no longer accepts or requires a private key. Operators register `MintAddress`, `Decimals`, `Label` (+ whatever the escrow needs). The `SenderAccount`/`TokenOwnerPublicKey` treasury fields are no longer used for `SolanaToken` settlement (pot flows through the vault, not the treasury). |
| 8 | **Decimals leak fixed in the same feature.** `BetDomainService` must derive rounding/base-unit decimals from the **session's token** (`session.Parameters.OperatorToken.Decimals`) for `SolanaToken`, not from the global `GeneralSettings.Token`. (`Standard` keeps its currency precision.) See §9. |
| 9 | **Bet deposit is a `place_bet` program instruction**, not a raw SPL transfer. `RegisterBetHandler` validates the on-chain deposit (BetRecord PDA / instruction) instead of a transfer signature. |
| 10 | **`Standard` sessions unchanged.** Off-chain USD via `IOperatorWalletService`. |
| 11 | **Migration is dev/greenfield** — no production `SolanaToken` bet/ledger data preserved. |
| 12 | **The escrow program is a new, audited artifact** (Rust/Anchor). Deploy devnet → audit → mainnet. The `GeneralSettings.Token.ClusterUrl` stays global; add the deployed **program id** to config. |
| 13 | **Ledger continuity:** keep the existing `TransactionLog` + `CryptoTransactionLog` split. On-chain references now point at `place_bet` / `claim` / `refund` signatures and the vault, not treasury transfers. |
| 14 | **The winner split is computed on-chain, not authority-asserted** (promoted from O2 — required for fund safety). A per-`(session, choice)` **tally PDA** (seeds `["tally", session_id, hash(choice)]`) is created/incremented in `place_bet`; `resolve` reads the winning choice's tally and the program itself computes `amount_per_winner = total_deposited / winning_tally` on-chain. The resolver authority therefore asserts **only** the `winning_choice` (and reveals the seed) — it can no longer influence amounts. This closes the biggest stuck-funds risk (§12 L1). |

---

## 4. Architecture & fund flow

```
Session start (Consumers)
  └─ initialize_session(session_id, mint, required_amount, decimals,
                        server_seed_hash, resolve_deadline, resolver_authority)
       → creates Session PDA + Vault ATA (owned by Session PDA). Authority pays rent/fees (SOL only).

Bet (player wallet → program)         [frontend / wallet-bridge]
  └─ place_bet(choice, nonce, amount)
       → transfers `amount` tokens: player ATA → Vault
       → creates BetRecord PDA {bettor, choice, nonce, amount, claimed=false}
       → creates/increments Tally PDA for (session, choice)   [makes the split trustless]
  └─ backend RegisterBetHandler validates the on-chain deposit, persists the Bet, publishes BetPlacedEvent

Run + decide winner (Consumers, off-chain)   [unchanged typer + provably-fair]

Settle (Consumers)
  └─ resolve(winning_choice, revealed_server_seed)   [resolver authority signs]
       → require hash(revealed_server_seed) == Session.server_seed_hash
       → amount_per_winner = total_deposited / Tally[winning_choice]   [computed on-chain]
       → Session.state Open→Resolved; store winning_choice, amount_per_winner, revealed_seed

Payout (winners, permissionless-per-winner)   [frontend claim UI]
  └─ claim()   [winner signs]
       → require BetRecord.choice == Session.winning_choice, not claimed, state==Resolved
       → transfer amount_per_winner: Vault → winner ATA; mark claimed

Safety net (any time state==Open and now>deadline, OR state==Refunding)
  └─ refund()  [bettor signs, permissionless]
       → transfer BetRecord.amount: Vault → bettor ATA; mark refunded
```

**Key custody property:** the Vault's authority is the **Session PDA** (a program-derived address with
no private key). Tokens move only through `claim`/`refund`, each of which is gated on a real BetRecord.
The resolver authority never appears as a token-transfer signer.

---

## 5. The Solana program (Phase 0 — the foundation)

New Anchor program. Program id added to backend config once deployed.

### 5.1 Accounts

- **`Session` PDA** — seeds `["session", session_id]`. Fields: `resolver_authority: Pubkey`,
  `mint: Pubkey`, `required_amount: u64`, `decimals: u8`, `server_seed_hash: [u8;32]`,
  `state: enum { Open, Resolved, Refunding }`, `resolve_deadline: i64` (unix), `winning_choice: String`
  (empty until resolved; bounded length — see O3), `amount_per_winner: u64`, `revealed_seed: [u8;32]`
  (or bytes), `total_deposited: u64`, `bet_count: u32`, `created_at: i64`, `bump`.
- **`Vault`** — the SPL **associated token account** for `mint`, **authority = Session PDA**. Holds the
  pot.
- **`BetRecord` PDA** — seeds `["bet", session_id, bettor, nonce]` (nonce = the provably-fair per-bet
  `Bet.Nonce`, so a bettor may place multiple distinct bets). Fields: `bettor: Pubkey`,
  `choice: String`, `nonce: u64`, `amount: u64`, `settled: bool` (claimed or refunded), `bump`.
- **`Tally` PDA** — seeds `["tally", session_id, hash(choice)]`. Field: `count: u32`. Created on the
  first bet for a choice and incremented on each subsequent one (§5.2). Read at `resolve` to compute the
  split on-chain (decision 14), so `amount_per_winner` is never authority-asserted. (`hash(choice)` keeps
  the seed fixed-length for arbitrary free-type choices — collision-resistance is open question O2.)

### 5.2 Instructions — deposit & init

- **`initialize_session(...)`** — signer: resolver authority (fee payer). Creates `Session` + `Vault`.
  Rejects duplicate session id. Stores the provably-fair commitment. `resolve_deadline` = start +
  configured max session lifetime + buffer.
- **`place_bet(choice, nonce, amount)`** — signer: **bettor**. Requires `state == Open`,
  `amount == required_amount` (paid sessions), choice length/charset within bounds. CPI SPL transfer
  bettor ATA → Vault for `amount`. Creates `BetRecord`. Increments `bet_count`, `total_deposited`, and
  the **`Tally` PDA** for this `choice` (decision 14).
  (Free/amount-0 sessions never deposit — handled off-chain, no escrow; see O4.)

### 5.3 Instructions — resolve & claim (the non-custodial core)

- **`resolve(winning_choice, revealed_server_seed)`** — signer: **resolver authority**. Requires
  `state == Open`, `hash(revealed_server_seed) == server_seed_hash` (commit-reveal enforcement),
  `winning_choice` non-empty and a `Tally` PDA for it exists (i.e. someone bet it). The program then
  **computes `amount_per_winner = total_deposited / Tally[winning_choice].count` on-chain** (decision 14),
  sets `state = Resolved`, and stores `winning_choice`, `amount_per_winner`, `revealed_seed`.
  **Constraints that make the authority non-spendable:** it names a *choice*, not payees; it transfers
  nothing; it cannot set the amount (computed on-chain from the tally); and a claim can never exceed the
  vault. So the authority can neither divert funds nor cause payment beyond the pot.
- **`claim()`** — signer: **winner**. Requires `state == Resolved`, `BetRecord.bettor == signer`,
  `BetRecord.choice == winning_choice`, `!BetRecord.settled`. Transfers `amount_per_winner` Vault →
  winner ATA (Session PDA signs as vault authority via seeds). Marks `settled`.

> **Trust boundary (per decisions 3 & 14):** the resolver authority asserts **only** the
> `winning_choice` (and reveals the seed); `amount_per_winner` is computed **on-chain** from the
> per-choice `Tally` (decision 14), so the authority cannot influence amounts, pay **non-bettors**, or
> exceed the pot. Its sole residual power — choosing the winning *choice* — is guarded by provably-fair
> (the revealed seed must hash to the on-chain commitment) and is publicly verifiable. Closing this last
> gap fully (proof-verified outcome) is O1.

### 5.4 Instructions — refund / cancel (the safety net)

- **`refund()`** — signer: **bettor**, **permissionless**. Requires `!BetRecord.settled` and either
  (`state == Open` and `now > resolve_deadline`) or `state == Refunding`. Transfers `BetRecord.amount`
  Vault → bettor. Marks `settled`. This is invariant #3 from the design — no authority can lock funds.
- **`cancel()`** — signer: **resolver authority**. `state Open→Refunding`. For invalid/aborted rounds
  (maps to today's `RefundInvalidBet`). Opens `refund` immediately.
- **`close_session()`** (optional, post-settlement) — reclaims PDA/vault rent once all BetRecords are
  settled and the vault is empty (dust handling — O5).

### 5.5 Provably-fair tie-in

`initialize_session` persists `server_seed_hash` (the existing `ServerSeedHash` committed on first bet
in `provably-fair.md`). `resolve` requires the revealed seed to match. This puts the commit-reveal
**on-chain**, strengthening the audit story and constraining the authority further. Reconcile the
"committed on first bet" timing (`provably-fair.md`) with "committed at session init on-chain" — O6.

---

## 6. Backend integration (Phase 1)

- **`OperatorToken`** — remove `TokenOwnerPrivateKey` (+ `SenderAccount`/`TokenOwnerPublicKey` if
  unused post-escrow; keep `MintAddress`, `Decimals`, `Label`). Migration + drop from
  `RegisterOperatorTokenHandler` validation and the admin request body/DTO. Update
  `EnsureDevOperatorToken` (`Consumers/Program.cs`) and `IntegrationTestFixture` to seed public-only.
- **`SolanaTokenDescriptor`** — drop the private key; it now carries public token + program data
  (mint, decimals, program id, vault/session PDAs).
- **`ISolanaService`** — replace treasury transfer/validate with escrow interactions:
  `InitializeSession`, `ValidateBetDeposit` (replaces `ValidateBetPayment` — confirm the on-chain
  `place_bet` / BetRecord for this bet), `Resolve`, and read helpers. `claim`/`refund` are
  client/permissionless, but Consumers may **crank** overdue refunds (O7).
- **`RegisterBetHandler`** — validate the `place_bet` deposit instead of a raw transfer signature
  (`Parameters.OperatorToken` still resolves the mint/program; BetRecord confirms choice+amount+wallet).
- **`RewardSessionConsumer`** — instead of `Credit` transfers, call `Resolve(winning_choice,
  revealedSeed)` once (the split is computed on-chain, decision 14); mark bets `Rewarded` optimistically
  or on observed claims (O8 — reconcile bet status vs. pull-based claims). Fix the **decimals** source
  here (§9).
- **`RefundInvalidBetConsumer`** — call `cancel()` (→ Refunding) rather than signing refunds; bettors
  reclaim permissionlessly. Fix decimals (§9).
- **Ledger:** `TransactionLog`/`CryptoTransactionLog` references become `place_bet`/`resolve`/observed
  `claim`/`refund` signatures + vault address.

---

## 7. Frontend (Phase 2)

- **Bet flow** (`useSession.bet` / `sendToken` / wallet-bridge, incl. the iframe popup from
  `iframe-crypto-sessions.md`): call the program's `place_bet` instruction instead of a plain SPL
  transfer; return the signature to the backend as today.
- **Claim UI:** winners need a `claim` action (a "Claim winnings" affordance on `/game` and in the
  embedded/popup paths). Consider an auto-claim helper on session end for connected winners.
- **Refund UI:** an affordance to `refund` after the deadline / on cancellation.
- Token-native amounts throughout (unchanged from `iframe-crypto-sessions.md`).

---

## 8. Refunds, ops, mainnet (Phase 3)

- Monitoring/alerting for unresolved-past-deadline sessions; an optional **refund cranker** (a Consumers
  job that calls `refund` on behalf of bettors after the deadline, purely as a convenience — still
  permissionless, no custody).
- Resolver-authority key management (rotation, funding SOL for rent/fees).
- **Security audit** of the program; devnet → mainnet cutover; program-id config swap.

---

## 9. Decimals leak fix (folded in)

Independent of escrow but fixed here since we're in the money math. `BetDomainService`
(`CalculateRewardForBet` line ~80–81, `CalculateRemainingBetPayout` line ~103–109) currently rounds and
computes base units with `_settings.Decimals` (global `GeneralSettings.Token`). For `SolanaToken`
sessions this must use the **session token's** decimals (`session.Parameters.OperatorToken.Decimals`);
`Standard` keeps its USD/currency precision. Pass the correct decimals into `BetDomainService` from the
consumers (they already `.Include(...OperatorToken)`), rather than reading `settings.Token`. Add unit
tests covering a token whose decimals ≠ the global settings decimals.

---

## 10. Phasing / decomposition

Each phase is its own spec → plan → ship cycle (this doc is the umbrella design):

| Phase | Deliverable | Depends on |
| --- | --- | --- |
| **0** | The Anchor escrow program (§5) + Rust tests + devnet deploy + `solana-test-validator` harness | — |
| **1** | Backend integration (§6, §9): public-only `OperatorToken`, escrow `ISolanaService`, register/reward/refund rewiring, ledger, migrations, integration test | Phase 0 instruction/account shapes |
| **2** | Frontend deposit + claim + refund UI (§7) | Phases 0–1 |
| **3** | Refund cranker, monitoring, **audit**, mainnet cutover (§8) | Phases 0–2 |

**First build target: Phase 0** (foundation-first) — every other phase is shaped by the program's
instruction and account layout, and it carries the audit/real-money risk.

---

## 11. Testing

- **Program (Rust/Anchor):** unit + `anchor test` against a local validator — happy path
  (init→bet→resolve→claim), commit-reveal rejection (wrong seed), over-claim rejection, double-claim
  rejection, permissionless refund after deadline, cancel→refund, non-bettor claim rejection,
  wrong-choice claim rejection.
- **Backend integration (`Monkify.Tests.Integration`, mandatory):** a full `SolanaToken`
  **place_bet → resolve → claim** round on a `solana-test-validator` container — assert the winner's ATA
  balance increased by exactly `pot / winnerCount` and the vault is emptied. Plus a **refund-timeout**
  test (complex flow). Extend `IntegrationTestFixture` to run a local validator + deploy the program.
- **Backend unit (`Monkify.Tests`):** decimals fix (§9) with token decimals ≠ global; consumer rewiring;
  handler validation of the deposit.

---

## 12. Liveness & stuck-funds analysis

An escrow that holds real value must guarantee funds can always exit. This section enumerates every way
a fund/transaction could get **stuck** and the required handling.

**The catastrophic case is covered by design.** If Monkify never resolves (host offline, or `resolve`
rejected by the commit-reveal check), the **permissionless timeout refund** (§5.4) lets every bettor
reclaim their own stake with no authority, no Monkify, and no operator involved — *provided*
`resolve_deadline` is **bounded and validated** at init. So "the pot is held hostage forever" is off the
table. A resolution *bug* degrades gracefully into this same refund path.

Residual stuck-risks and their required handling:

| # | Scenario | What happens without mitigation | Required handling |
| --- | --- | --- | --- |
| L1 | `amount_per_winner` too high (bad winner count) | Early winners over-withdraw, vault empties, **late winners can neither claim (dry) nor refund (Resolved)** — funds stranded | **Compute the split on-chain** from the per-choice `Tally`, never authority-asserted — **decision 14**. This collapses the risk to zero. |
| L2 | Dust / imperfect division | Integer `total_deposited / count` leaves a remainder locked in the vault | Define a **sweep** (residual → operator) via `close_session`, callable once all winners have claimed (O5) |
| L3 | Winner never claims | Winnings + PDA **rent** stay locked; `close_session` is blocked | **Claim grace period**; after it, unclaimed winnings sweep (operator) and accounts close (O5/O8) |
| L4 | Winner has no ATA for the mint | `claim` transfer fails | `claim` **creates the ATA** (payer = claimant), or the UI prompts — recoverable, not permanent |
| L5 | Program logic bug (if immutable) | A withdraw path could be permanently broken | Ship with a **timelocked/multisig upgrade authority** (a documented trust trade-off), not a burned one, at least until audited + battle-tested |
| L6 | `resolve_deadline` misconfigured (too far / unbounded) | Refund blocked until a far-future time | **Validate/cap the deadline at init**: reject anything beyond `created_at + max_session_lifetime + buffer` |

Every row except L5 must be resolved in the program design (Phase 0); L5 is an operational/governance
decision recorded here. With **L1 fixed on-chain (decision 14)** and L2–L4/L6 handled, no scenario causes
fund *loss* — the only remaining "stuck" is unclaimed value that a grace-period sweep eventually frees.

---

## 13. Open questions / risks

- **O1 — trust model boundary.** Approach A accepts the authority is trusted for the *outcome* (guarded
  by provably-fair). Fully-trustless (program verifies the outcome) and multisig-resolver were deferred;
  revisit if operators demand zero outcome-trust.
- **O2 — trustless winner count (PROMOTED to required — decision 14, not optional).** The split is
  computed on-chain from a per-`(session, choice)` `Tally` PDA, so `amount_per_winner` is never
  authority-asserted (closes §12 L1). Remaining sub-questions: confirm `Tally`-PDA rent economics, and
  the collision-resistance / domain-separation of `hash(choice)` for arbitrary free-type choices.
- **O3 — storing `choice` on-chain.** Choices are short strings; bound length/charset to keep BetRecord
  size + compute predictable. Confirm max length vs. `ChoiceRequiredLength`.
- **O4 — free / amount-0 sessions.** No deposit, no escrow — settle entirely off-chain as today. Confirm
  no `SolanaToken` free session tries to touch the program.
- **O5 — dust & rent.** Integer `pot / count` can leave dust; PDAs/vault hold rent. Decide sweep target
  (operator? refundable?) and `close_session` timing.
- **O6 — provably-fair commit timing.** Today the server seed commits on the **first bet**; on-chain
  escrow wants the commitment at **session init**. Reconcile (commit at init on-chain, or init the
  escrow lazily on first bet).
- **O7 — who initializes the escrow, and SOL funding.** Resolver authority pays rent/fees (operational
  SOL, not custody). Budget + monitor this.
- **O8 — bet status vs. pull-based claims.** With claims being permissionless/pull, `Bet.Status`
  ("Rewarded") no longer maps 1:1 to a Monkify-signed payout. Decide whether "Rewarded" means "resolved
  and claimable" (observe claims separately) or track a distinct claimed state.
- **O9 — many bets / compute.** `place_bet` creates a PDA per bet; high-volume sessions = many accounts.
  Validate rent economics and any per-session account limits.

---

## 14. Out of scope

- `Standard` (off-chain USD) sessions — entirely unchanged.
- Cross-token / SOL↔token conversion, price oracles — amounts stay token-native.
- Fully-trustless on-chain outcome recomputation and multisig resolver (§13 O1) — deferred.
- A Monkify commission per bet (separate backlog item) — pot stays 100% to winners.

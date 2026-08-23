# Feature — SOL settlement (native, not an SPL token)

> Status: **shipped.** Authoritative spec: [`../specs/sol-settlement.md`](../specs/sol-settlement.md)
> (merged via `feature/sol-settlement` on 2026-08-20, Trello
> [#174](https://trello.com/c/K0Texy2Z/174-stop-using-usdc-in-the-games)).
>
> ⚠️ **This replaced the settlement transport, not just the ticker.** Everything that moves money —
> the SPA's transfer builder, payment validation, payouts, refunds, balance reads, Orbit custody —
> was SPL-shaped and now branches. Read [`money.md`](money.md) for the cut and the payout math and
> [`orbit.md`](orbit.md) for custody; this doc is about the asset itself.

## What it does

Every game settles in **native SOL** instead of the SPL token it used to (mainnet USDC). A ticket is
**0.01 SOL** on Cosmic Sprint (0.02 at the cutover; re-priced 2026-08-23), **0.1 SOL** on Space
Jackpot, and **0.02 SOL** on the gated Beast Mode.
Stakes leave the player's wallet as an ordinary lamport transfer and winnings arrive the same way —
no mint, no associated token account, nothing to unwrap.

## Core rules

### The asset

- **The settlement asset is native SOL**, at **9 decimals**, symbol `SOL`.
- ⚠️ **`So11111111111111111111111111111111111111112` — wrapped SOL's canonical mint — is carried as a
  SENTINEL IDENTIFIER, never as a token.** No wSOL account is created, read, wrapped or transferred.
  It exists so config, `GameParametersDto.TokenMintAddress`, the `/solana/balance` active-mint check
  and Orbit's `(OrbitAccount, MintAddress)` pool key keep the mint-shaped contracts they were built
  around. Treating it as a real SPL mint would pay winners into an account the house cannot unwrap
  for them — the design that was explicitly rejected.
- **One seam decides the transport:** `SolanaTokenDescriptor.IsNative`, computed from the mint via
  `NativeSol.IsSentinel`. It fails **closed** — a blank or unknown mint is not native — and trims,
  because the value arrives from JSON and env vars.
- ⚠️ **`SolanaTokenDescriptor.SenderAccount` INVERTS MEANING.** SPL: the treasury/Orbit/house
  **associated token account** (TokenProgram's source must be a token account). Native: the **wallet
  itself** (SystemProgram moves lamports between ordinary accounts). Both resolvers document the
  failure mode in each direction — it is a transaction the runtime rejects *after* the balance moved,
  not a caught exception.

### Paying and being paid

- A player's stake is a **`SystemProgram.transfer`** built by the SPA. No ATA-creation instructions,
  no mint in the instruction.
- **Payment validation reads lamport balances, not token balances.** `meta.preBalances` /
  `postBalances` are bare arrays **positionally aligned with `transaction.message.accountKeys`**, so
  an account is found by locating its public key and reading that index.
- ⚠️ **The transaction fee is netted out of the FEE PAYER's side.** The bettor signs their own
  transfer, so their balance drops by the stake **plus** the fee; comparing the raw delta against the
  stake refuses every genuine bet by exactly 5,000 lamports. The fee is subtracted only for
  `accountKeys[0]` — subtracting it from any other sender would credit lamports it never sent.
- ⚠️ **An account absent from `accountKeys` is a REFUSAL, never an index guess.** A versioned
  transaction with an address-lookup table carries some accounts in `meta.loadedAddresses`; guessing
  there would compare an unrelated account's balance against the stake.
- **Payouts and refunds skip the token-account lookup entirely** — the destination is the wallet.
- ⚠️ **A transfer below the rent-exempt minimum (890,880 lamports) is refused before signing.** Such
  a transfer fails on-chain against an unfunded destination, and retrying it would loop forever. Both
  ticket prices clear the floor comfortably; a **partial** refund can fall under it.
- **The memo still matters.** A Solana signature is deterministic over the message, and Orbit places
  concurrent identical custody→house transfers; without a distinguishing memo the second is rejected
  as a replayed signature and its player is debited with no bet. Native transport does not change this.
- **Balance reads** use `getBalance`; a never-funded account is a **zero** balance, not an error.
- **User-facing refusals are unchanged** — a bet that did not pay is refused with the same vague copy
  whichever asset it failed to pay in.

### The cutover (`SettlementCutoverUtc`)

- ⚠️ **Nothing in this system stamps a currency on money.** `Bet.Amount`, `OrbitBalance.Amount` and
  `Game.CarriedExtra` are bare `numeric(18,9)`. A bet taken when `1` meant 1 USDC and settled after
  the switch would pay **1 SOL** — roughly a 200x overpay, on-chain and unrecoverable.
- `GeneralSettings:Operator:Token:SettlementCutoverUtc` marks the boundary. **Null means "this
  deployment never switched"** and makes the guard completely inert — which is what every dev stack,
  fresh install and test fixture carries.
- **A bet created before the cutover never has money moved for it.** `RewardGameConsumer` and
  `RefundInvalidBetConsumer` mark it `NeedsManualAnalysis` and log at **Error** (production logging is
  Warning-and-above, so Information would make the one event needing a human invisible). Other
  winners in the same round still settle.
- ⚠️ **`PendingRefundSweepJob` PARKS such a bet rather than skipping it.** Skipping was the first
  implementation and was wrong: `NeedsRefunding` is exactly what the sweep selects on, so a skipped
  bet was re-swept every 12 hours forever. It now writes `NeedsManualAnalysis`, which the sweep never
  selects. *(An integration test caught this; the spec's §6.3 originally described the skip.)*
- **The boundary instant belongs to the NEW era** — a bet stamped exactly at the cutover was taken in
  SOL.
- ⚠️ **The guard is also what protects against a stale SPA.** An old bundle can still send an SPL
  transfer at any time after the switch; that bet is refused *after* its money moved, and must reach
  manual analysis rather than the auto-refund path, which would return the amount in SOL.

### Stats

- The cutover is a **floor under whatever window a stats read asked for** — global, per-type and
  per-player totals count only rounds completed after it. Otherwise a total would add USDC-era figures
  to SOL-era ones and report a number in no currency at all.
- ⚠️ **Only the SUMMING stops.** Games history, the recent-rounds feed and provably-fair verification
  still show every pre-cutover round; nothing is relabelled, hidden or deleted.
- ⚠️ The value feeds the **existing two-arm predicate**
  (`EndDate >= cutoff || (EndDate == null && CreatedDate >= cutoff)`). It is deliberately not a
  `COALESCE`, which is unsargable and would forfeit `IX_Games_ParametersId_Status_EndDate_CreatedDate`.

### Stake and gas are now the same balance

- ⚠️ **This is the subtlest consequence of native settlement.** While the stake was an SPL token, a
  house account's SOL was pure gas and `balance >= floor` genuinely meant "can pay fees". Now the
  stake **is** lamports out of that same balance.
- **Orbit's low-SOL guard is a SOLVENCY check:** `solBalance − Σ(the pool's owed player balances) ≥
  MinimumSolBalance`. The naive check passed forever, because player deposits sit in the balance it
  reads. A failed balance read is still "unknown", not "empty" — the engine keeps going.
- **The house seed guard subtracts the stake** it is about to place: `balance − stake ≥ floor`. A
  wallet holding exactly the floor would otherwise pass, spend the floor on the bet, and fail on-chain.
- **The SPL path keeps the old check** — there the two balances are genuinely separate.
- **The player-facing consequence:** the wallet balance in the header is also the gas. The SPA refuses
  a bet below `requiredAmount + 0.001 SOL` client-side, before anything is signed, and says so.

### Orbit deposits

- **Decimals are legal; the whole-number rule is gone.** It was policy inherited from the USDC ladder
  and in SOL meant a minimum deposit worth a few hundred dollars.
- What replaces it is a **precision** check: an amount finer than the asset's decimals is refused,
  because crediting one would write a ledger balance the chain can never pay back out.
- **Bounds:** ladder `0.05 / 0.1 / 0.5` SOL, max **5 SOL** per transaction, minimum cash-out **0.05**.
  `MinimumDeposit` is configured now (it was a hard-coded `1`).
- ⚠️ The published bound message stays the **sanctioned exception** to vague messaging
  (`constraints.md` §6) and therefore has to stay *accurate*: it reads
  *"Deposits are between 0.05 and 5 SOL."*
- ⚠️ **`OrbitSettings.AvailableDepositOptions`' fallback moved too.** It is the ladder an environment
  gets when it configures nothing; left at `[1, 5, 10]` it would have offered 1–10 SOL buttons.
- **The deposit chips are derived from the published ladder**, not hard-coded — the old literal
  `+10 / +25 / +50` survived the currency switch and pinned a 5-SOL field to its ceiling on every tap.

### What did NOT change

- **The commission is still 1%** of profit above each winner's own stake, house-funded lottery base
  excluded. The rate is a percent, so no code or config moved. ⚠️ It is read **at payout time**, never
  snapshotted — which is exactly why the cutover runbook drains every open paid round first.
- **Free bets** (amount 0) — no on-chain call, no commission, byte-identical behaviour.
- **Provably-fair** — the typer never sees a currency. Same seed and bets produce the same stream and
  the same winner.
- **No EF migration.** Every money column was already `numeric(18,9)`, which holds 9-decimal SOL
  exactly.

## Deploying a currency switch

⚠️ **Config alone does not re-price an existing database.** Game types are seeded **create-only by
name** and no `RequiredAmount` self-heal was added, so new prices in `DefaultGames` reach a *fresh*
install only. `deploy/sql/sol-settlement-cutover.sql` re-prices the three existing rows; it refuses to
run while a round is open (a mid-round price change makes a bet fail `InvalidAmount` after its money
moved) and refuses if Space Jackpot still carries an un-won pot from the previous asset.

⚠️ **A local or dev stack seeded before the switch keeps the OLD prices**, and re-running the seeder
will not fix it. That is the standing cost of the create-only decision.

The full runbook — drain, force Orbit cash-outs, fund the wallets, deploy Consumers **and** the API
together, run the SQL, verify by observable state — is [`../specs/sol-settlement.md`](../specs/sol-settlement.md) §13.
It moves real money and is executed by a human.

## Key code touchpoints

**Backend (`monkify-backend`)**

| File | Role |
| --- | --- |
| `SpaceStroker.Domain/Games/ValueObjects/NativeSol.cs` | Sentinel mint, decimals, symbol, lamports, rent-exempt floor, `IsSentinel` |
| `SpaceStroker.Domain/Games/ValueObjects/SolanaTokenDescriptor.cs` | The `IsNative` seam (computed, not a ctor param) |
| `SpaceStroker.Domain/Games/ValueObjects/NativeTransactionMetaData.cs` | Lamport meta + `IndexOf`; `NativeExchange` |
| `SpaceStroker.Domain/Bets/Services/SettlementCutover.cs` | `IsPreCutover` |
| `SpaceStroker.Domain/Configs/Entities/TokenSettings.cs` | `SettlementCutoverUtc` |
| `SpaceStroker.Domain/Games/ValueObjects/StatsWindow.cs` | `EffectiveCutoff` |
| `SpaceStroker.Infrastructure/Services/Solana/SolanaService.cs` | Native transport, `GetLamportBalances`, the two static `DescribeNative*Exchange` helpers, native balance read |
| `SpaceStroker.Infrastructure/Services/{Orbit,HouseSeed}/*AccountResolver.cs` | Native `SenderAccount` |
| `SpaceStroker.Infrastructure/Services/HouseSeed/HouseSeedBetService.cs` | Stake-aware solvency guard |
| `SpaceStroker.Infrastructure/Consumers/OrbitBetPlacement/PlaceOrbitBetConsumer.cs` | Pool-aware solvency guard |
| `SpaceStroker.Infrastructure/Consumers/{RewardGame,RefundInvalidBet}/*.cs` · `Jobs/PendingRefundSweepJob.cs` | The pre-cutover guard |
| `SpaceStroker.Infrastructure/Services/{Games,Players}/*StatsService.cs` | Cutover floor |
| `SpaceStroker.Infrastructure/Contracts/Games/GameParametersDto.cs` | `TokenIsNative` |

**Frontend (`monkify-front`)** — `src/hooks/useConnection.ts` (the `SystemProgram` builder),
`src/hooks/useGame.ts` (fee headroom), `src/hooks/useOrbit.ts` (precision + `depositChips`),
`src/components/orbit/OrbitAmountField.vue` (decimal validation, MAX/step maths),
`src/utils/money.ts` (4dp), `src/assets/sol.svg` + `src/constants/icons.ts` (the money mark),
`src/constants/keys.ts` (`SETTLEMENT_MINT`).

**Meta (`monkify-meta`)** — `deploy/sql/sol-settlement-cutover.sql`, `deploy/secrets.example.json`.

**Tests** — `NativeSolExchangeTests` (the fee-netting arithmetic, exact and RPC-free),
`SolanaTokenDescriptorTests`, `SettlementCutoverTests`, and integration `SolSettlementFlowTests`
(happy path, refund, the pre-cutover guard, the catalogue contract).

⚠️ **The integration suite fakes the chain**, so no test there observes a real SystemProgram
instruction — it proves the SOL configuration reaches the money path through two real hosts and that
the descriptor handed to a payout is the native one. The arithmetic is covered by the unit tests.

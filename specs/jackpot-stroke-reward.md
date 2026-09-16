# SpaceStroker — "Space Jackpot $STROKE reward" — Implementation Spec

> ✅ **Status: SHIPPED** — merged to `main` on **2026-09-16** from `feature/jackpot-stroke-reward`
> (`monkify-backend` + `monkify-meta`; **`monkify-front` untouched, zero files**).
>
> Verified on the branch before the merge: **1255 unit / 159 integration, all green.** 28 tests added
> (6 + 12 + 6 unit, 4 integration); **no existing test changed.**
>
> **Purpose:** both the landing page and the app's home card already promise *"Every winner of Space
> Jackpot will be rewarded with $STROKE from the Treasury Wallet."* That copy was deliberately
> **future tense** because nothing in code did it — see
> [`features/token-launch.md`](../features/token-launch.md) §4, open item 3. This closes it: a lottery
> round's winners now receive **100,000 $STROKE** each, on top of their SOL winnings.

---

## 1. Summary

`RewardGameConsumer` already settles a round's SOL winnings. After that settlement completes, a new
`JackpotRewardService` pays each **distinct winning player** of a **lottery** round a configured
amount of a configured **second token**, signed by the house settlement wallet out of its own
holding.

The bonus is **best effort and strictly downstream**: it runs last, inside a `try`/`catch`, and never
touches a `GameStatus` or a `BetStatus`.

---

## 2. What the chain forced (all read off mainnet, 2026-09-16)

These are not design preferences. Each one is a property of the real `$STROKE` mint that the existing
settlement path cannot handle.

| Fact | Consequence |
| --- | --- |
| Mint `Dts45D3WfhuJuwpK5nYkPM6Jyu5DqeEQzTw3rJkopump` is owned by **`TokenzQdB…` (Token-2022)**, not `Tokenkeg…` | Solnet 6.1.0 hardcodes the classic program in `TokenProgram` **and inside `AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount`**, where the program id is one of the **seeds**. The whole instruction set had to be built by hand — see §4. |
| Decimals **6** | 100,000 STROKE = `100_000_000_000` base units. The settlement asset is 9. |
| No `TransferFeeConfig`, no freeze authority, mint authority burned, supply 965,594,139 | A plain `TransferChecked` moves the exact amount; nothing is skimmed and no winner's account can be frozen. |
| House wallet `CjAULxZV5FSp2L5qQeLynv9rEbULT7ev5e81fYpFs5L2` holds **2,000,000 STROKE** in Token-2022 account `23PcY7ou41LVcapjS9feFrQLhU8e7N4BtjKKnNw5Ep3g` | The bonus is fundable **today**, with **20 rounds of runway**. See §8. |

⚠️ **The Token-2022 failure mode is silent in both directions.** A classic-program derivation yields a
well-formed address that does not exist; a non-existent token account **reads as a zero balance, not
as an error**. So the house's 2,000,000 would have read as empty and the pre-flight would have
disabled the feature permanently, with nothing in the logs saying why.

---

## 3. Decisions

1. **The signer is the house settlement wallet** (`Operator:Token:TokenOwner*`), not the disclosed
   treasury `GqxFusRS…` the copy names — that one's supply sits in a Streamflow vesting contract and
   cannot sign ad-hoc transfers. ⚠️ **The copy and the chain therefore disagree**; see §8.
2. **One bonus per distinct winning WALLET**, not per winning bet. ⚠️ Defensive rather than
   load-bearing today: `RegisterBetHandler.ValidateBetParameters` already makes a bet unique per
   `(game, player, choice)` and exactly one choice wins a round, so a player cannot currently hold two
   winning tickets. It is written this way because "per bet" is only correct while that rule holds.
3. **Lottery rounds only**, gated on `game.Parameters.LotteryParameters != null` rather than on the
   type's name — renaming Space Jackpot in config must not silently switch the bonus off, and adding a
   second lottery should be a deliberate decision made here.
4. **The house creates the winner's token account.** Almost nobody winning already holds `$STROKE`,
   and the existing SPL branch *refuses* a destination with no token account. The payout carries an
   **idempotent** `CreateAssociatedTokenAccount`. ⚠️ The **~0.002 SOL rent per new holder is spent by
   the house and is not recoverable** — the account belongs to the winner.
5. **All or nothing per round.** If the house cannot cover every winner, **nobody** is paid and the
   shortfall is logged at `Error`. A lottery round's winners are publicly comparable; a bonus that
   reached one and not the identically-placed player beside them reads as favouritism, not as an
   empty account.
6. **No ledger row, and no new table.** `TransactionLog` is denominated in the settlement asset and
   feeds pot maths, stats and the leaderboards — `$STROKE` rows would corrupt all three. Idempotency
   comes from §5 below instead.
7. **Config is committed, not a secret.** A mint address and a round number are public (the mint is
   printed on the landing page). This deliberately keeps the feature clear of the failure that
   silently broke Orbit (#123) and the house seed (#168): a key in `secrets.<env>.json` does nothing
   until it is hand-added to **both** `base_secret_ids` and `base_secret_env` in
   `deploy/terraform/workload/main.tf`. ⚠️ The price is that `JackpotRewardSettingsBindingTests` is
   the **only** guard on the production values.
8. **Free bets and Orbit-funded bets are excluded.** A free bet staked nothing; an Orbit win belongs
   to custody, and the Orbit ledger holds the settlement asset alone — it has nowhere to put a second
   token. (Space Jackpot has `AllowOrbit: false`, so the latter is a guard, not a live branch.)

---

## 4. Components

| Where | What |
| --- | --- |
| `Domain/Configs/Entities/JackpotRewardSettings.cs` | `MintAddress`, `Symbol`, `Decimals`, `AmountPerWinner`, `TokenProgramId`; `IsConfigured` (fails closed and trims); `RawAmountPerWinner` (exact decimal maths, never `Math.Pow`). Hangs off `TokenSettings.JackpotReward`. **Blank mint disables the feature** — the correct value off mainnet. |
| `Infrastructure/Services/Solana/Token2022Program.cs` | ATA derivation taking the token program **explicitly** (it is a seed), `CreateAssociatedTokenAccountIdempotent` (discriminator **1**, not 0), `TransferChecked` (discriminator 12, `[u8][u64 LE][u8]`). Mirrors the derivation `monkify-meta/tools` already carries in Nightfall's `AssociatedToken` and the sniper's `PumpFunProgram` — those are standalone console apps, so this is a third copy. **Keep the three in step.** |
| `ISolanaService.TransferJackpotReward` / `GetJackpotRewardBalance` | The transport. Builds `[CreateIdempotentATA, TransferChecked, Memo]`, fee payer and signer are the house keypair, source is the house's derived Token-2022 account. The balance read is deliberately **not** `GetTokenBalance`, which derives with the classic program. |
| `Infrastructure/Services/Games/JackpotRewardService.cs` | The policy in §3. Never throws, never changes a status. |
| `RewardGameConsumer` | Collects `bonusWinners` inside the existing `creditResult.IsSuccess` branch; calls the service after the status update, in a `try`/`catch`. |

⚠️ **`TransferChecked`, not `Transfer`.** The decimals byte is the point: the chain refuses the
instruction when it disagrees with the mint, so a config that says 9 against a 6-decimal token fails
**on-chain** for a fee instead of quietly moving a thousandth — or a thousand times — what was meant.

---

## 5. Idempotency, and why it needs no storage

`bonusWinners` is populated **only inside the successful-credit branch**, so it holds the winners
credited **in this pass**. On a MassTransit redelivery every winner is already `Rewarded`,
`CalculateRewardForBet` answers `BetHasAlreadyBeenRewarded`, the credit branch is never entered, and
the list arrives **empty**.

⚠️ **Populating it from `betService.Winners` instead would double-pay on the first retry.** This is
the entire reason decision 6 (no ledger row) is safe, and it is pinned by
`RedeliveredRewardEvent_DoesNotPayTheBonusTwice`.

A second, independent guard: the memo is **deterministic** (`sj:{gameId}:{wallet}`), so a retry of a
send that actually landed rebuilds the *same* signature — which the chain already treats as the same
transaction.

⚠️ **The memo is also mandatory for correctness, not just for retries.** Every bonus is the same
amount from the same sender, so two winners in one round would otherwise build byte-identical
messages, share a signature, and the second would be dropped as a replay. Same trap
`TransferTokensWithMemo` exists for.

---

## 6. Failure behaviour

| Failure | Result |
| --- | --- |
| Mint not configured | Silent no-op. Not an error — it is the normal state off mainnet. |
| Balance read fails | **Nobody** paid, `Log.Error`. Fails closed: a transport failure must not read as "plenty". |
| House short | **Nobody** paid, `Log.Error` naming the shortfall. |
| One transfer fails | The others still go. The failed wallet is named at `Log.Error` — with no ledger, **that line is the only record that a winner is owed a bonus by hand.** |
| Anything throws | Caught in the consumer, `Log.Error`, round unaffected. |

⚠️ **Every one of those is `Error`, never `Information` or `Warning`.** Production logging is
Warning-and-above (`features/infra.md`), so anything lower makes an unpaid bonus completely
invisible — the same reasoning the pre-cutover money guard already documents.

⚠️ **A failed bonus must never reach `GameStatus`.** `RewardForWinnersCompleted` is what the frontend
reads to decide whether it may print a PAID line at all, so letting the bonus flip it to
`ErrorWhenProcessingRewards` would **hide a SOL payout that genuinely happened**. Pinned by
`LotteryRound_BonusTransferFails_StillCompletesTheRound`.

---

## 7. Tests

**Unit (24 added).** `Token2022ProgramTests` (6) — ⚠️ the derivation is pinned against the **real
mainnet triple** (house wallet + mint ⇒ the live token account), not against the code's own output,
and a companion test asserts the classic program does **not** produce it. `JackpotRewardServiceTests`
(12) — dedupe, the lottery gate, free/Orbit exclusion, the all-or-nothing boundary at exactly the
required amount, fail-closed on a failed balance read, and one failure not stopping the rest.
`JackpotRewardSettingsBindingTests` (6) — pins the committed production config (see decision 7).

**Integration (4 added, `JackpotRewardFlowTests`).** Happy path: a lottery round with two winning
players pays each of them `100_000_000_000` base units of the right mint with **distinct memos**, and
never pays the loser. Complex flows: the all-or-nothing shortfall, the failed-bonus isolation, and
the redelivery.

⚠️ Assertions read `fixture.ConsumersSolanaService`, **never** the API's double — every transfer
happens in Consumers — and **filter by wallet** rather than counting the bag, because Hangfire's own
lottery scheduling can settle an unrelated round mid-test.

⚠️ **A test that could not be written.** "One player holding two winning tickets is paid once" was
drafted and **fails at bet placement** with *"The requested bet was already made"* — see decision 2.
The branch is covered at the unit level, where the input can be constructed directly.

---

## 8. Open before this is really live

1. ⚠️ **Runway is 20 rounds.** 2,000,000 ÷ 100,000, and Space Jackpot draws **daily** — so the house
   account is dry in roughly three weeks, after which every round logs the §6 shortfall and pays
   nobody. **Topping it up is an ops task with a deadline.**
2. ⚠️ **The copy says the bonus comes from "the Treasury Wallet"** (`GqxFusRS…`); it actually comes
   from `CjAULxZV5…` (decision 1). Anyone verifying on Solscan sees a different wallet than the one
   both surfaces name. Either the copy changes or the treasury funds the house wallet visibly.
3. **The ~0.002 SOL per new holder** (decision 4) comes out of the same wallet that pays SOL
   winnings. At ~1 new holder per round it is negligible, but it is a real draw on the payout wallet.
4. **Nothing is surfaced in the UI.** A winner sees their SOL, and the tokens arrive in their wallet
   with no in-app notice. Deliberate for this round — `monkify-front` is untouched.

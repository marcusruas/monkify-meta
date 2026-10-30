# Feature — Space Jackpot: free daily draw for $STROKE holders

> Status: **shipped.** Authoritative spec: [`../specs/space-jackpot-refactor.md`](../specs/space-jackpot-refactor.md)
> (merged via `feature/space-jackpot-refactor` on 2026-10-01, in `monkify-backend`, `monkify-front`,
> `monkify-lp` — branch `master` — and `monkify-meta`). Trello
> [#196](https://trello.com/c/mEB01ur9/196-space-jackpot-refactor).
>
> ⚠️ Where the spec body and the code disagree, the code wins — the spec's §16 revision-log entry dated
> 2026-10-01 lists every deviation. Supersedes [`../specs/jackpot-stroke-reward.md`](../specs/jackpot-stroke-reward.md)
> (the 100,000 $STROKE winner bonus is **removed**).

## What it does

Space Jackpot is no longer a paid 0.1 SOL lottery. It is a **free daily draw for $STROKE holders**: a
wallet that holds at least the round's minimum **N** $STROKE picks a 6-letter word and **signs a
message** — no transaction, no fee — to get one ticket. The balance is checked when the wallet enters
and again at the 22:00 UTC draw, so one bag of tokens backs at most one ticket. The pot is **funded by
the house**, sized from the treasury's free headroom, frozen when the round opens and reserved against
Hyper Roll and House duels until the round settles. The winner is paid the pot in SOL with no house cut.

These did **not** change: the 22:00 UTC daily draw (`LotteryParameters.DrawTimeUtc`), the 6-letter
free-text pick, the typer and its provably-fair derivation, minimum 2 players, `AllowOrbit: false`.

## Core rules

### The gate

- Every holder behaviour is gated on **`LotteryParameters.HolderOnly`** (the type) or
  **`Game.IsHolderRound`** (`HolderMinimum != null`, the round) — **never on the type's name**.
- A holder type takes **only** holder entries. `RequiredAmount` is 0, `AllowFreeBet` is false, the config
  `LotteryPot` is 0. The house seed bet skips it.

### Entry — a signed message, not a payment

- `POST /games/{roundId}/bets` with `amount: 0`, no `paymentSignature`, the `wallet`, the word, plus
  **`walletProofNonce`** / **`walletProofSignature`**. The nonce comes from `GET /players/nonce` with
  purpose **`jackpot-entry`**.
- The signed message is five LF-joined lines, byte-identical in `JackpotEntryMessages.Build` and the
  front's `buildJackpotEntryMessage`:
  `Space Stroker jackpot entry` / `Wallet: …` / `Round: <round id>` / `Word: <word>` / `Nonce: …`.
  `Round` and `Word` bind the signature to this ticket; the nonce is single-use.
- Order of checks (`RegisterBetHandler.RegisterHolderEntry`): **shape** → **round open** → **wallet
  proof** (before any RPC) → player upsert, **self-exclusion**, **Orbit auto-play** (same gates and
  messages as paid bets) → **balance read** → **balance ≥ N** → the clock again → the shared choice rules
  and **one ticket per wallet** → insert.
- **Entries are accepted only while `Status == WaitingBets` AND now < `StartDate`** (the draw time),
  checked again right before the insert. ⚠️ Not `Game.GameAcceptingBets`: nothing is accepted in the
  `GameStarting` countdown, because the draw recheck has already read every balance by then and a later
  entry would let a holder pass the bag to a second wallet.
- **One ticket per wallet per round**, whatever the word: `BetRoundRules.HolderTicketRefusal` →
  `OneBetPerPresetRound`, backed by the unique partial index `IX_GameBets_GameId_PlayerId_Holder`
  (`"EntryKind" = 3`) → `HolderTicketAlreadyExistsException` → the same message. Two concurrent entries
  from one wallet yield one row.
- ⚠️ **Free-text word exclusivity still applies** (one wallet per word, [`games.md`](games.md) §11), so
  two wallets can never hold the same word. The card's "same-word tickets split the pot" is therefore
  **not reachable** — the owner confirmed shipping as built. The reward maths still divides by the winner
  count.
- A holder entry writes **no ledger row**, publishes **no `BetPlacedEvent`**, and earns **no commission
  and no referral commission** (the ticket's `ReferralCode` is null; first-touch attribution on the
  `Player` is still recorded). The live bet notification still goes out.
- **A paid bet sent to a holder type** is refused with `InvalidAmount` and refunded — but only after its
  payment validates ([`money.md`](money.md) → "Never refund what has not been verified"). A refused holder
  entry has nothing to refund and never carries `RefundWarning`.

### What the caller is told ([`../constraints.md`](../constraints.md) §6)

| Situation | Message |
| --- | --- |
| Bad signature, unknown nonce, replayed nonce | **One vague sentence:** `We could not verify that request. Please try again.` (`JackpotEntryInvalid`) |
| Balance could not be read at entry | Vague: `Your entry could not be processed. Please try again.` (`HolderBalanceUnavailable`) — the entry is **refused** |
| Balance below N | **Specific — a sanctioned §6 exception:** `Hold at least 1,000,000 $STROKE to enter today's draw.` (`HolderBelowMinimum`, `N0` invariant culture) |
| Round paused, past the draw time, or coming soon | The generic `GameNotValidForBets` |
| Second ticket | `OneBetPerPresetRound` |

N is a **public bound** published on the round payload, and the front shows the same sentence **before
any signature is requested**.

### Eligibility — the Token-2022 balance

- Eligible = the wallet's **Token-2022 associated token account** for the holder mint holds **≥ N**
  (UI amount). Mint `Dts45D3WfhuJuwpK5nYkPM6Jyu5DqeEQzTw3rJkopump`, 6 decimals, config
  `Operator:Token:HolderToken` (`HolderTokenSettings`, public and committed).
- Read through `ISolanaService.GetToken2022Balance` (one wallet) / `GetToken2022Balances`
  (`getMultipleAccounts`, chunks of 100), wrapped by **`HolderBalanceReader`**. A missing account is a
  definite **0**; an RPC failure, a malformed wallet or an unconfigured mint is **null** ("unknown",
  never "zero").
- ⚠️ **Never the classic `GetTokenBalance`** — the program id is a seed of the ATA derivation, so a
  classic derivation reads a real holder as 0 instead of erroring.
- ⚠️ A holder whose $STROKE sits in a **non-associated** token account reads as below N (accepted, out
  of scope).
- `GeneralSettings:Games:HolderJackpot:DevAssumedBalance` replaces every read outside Production
  (devnet has no $STROKE). It is ignored when `IsProduction()`.

### N — the holder minimum, frozen per round

- Computed in **Consumers** when the round opens and stored on `Game.HolderMinimum`
  (`HolderMinimumRules`): Jupiter price (`https://lite-api.jup.ag/price/v3`, `JupiterPriceClient`) →
  `TargetUsd` (10) ÷ price → rounded **up** on the **nice-number ladder** **1, 1.5, 2, 2.5, 3, 4, 5, 6, 8**
  per decade (616,500 → 800,000; 1,010,000 → 1,500,000) → clamped to **×2 / ÷2 of the previous round's N** →
  clamped to `MinimumFloor` / `MinimumCeiling` (100,000 / 100,000,000). ⚠️ The ladder was 1-2-5 until
  2026-10-05 (owner: "the numbers cannot be that big" — $10 needing 616,500 asked for 1,000,000).
- When a ×2/÷2 clamp binds, N can land **off** the ladder (e.g. 4,000,000); the next unclamped round
  snaps back.
- **Price failure** (non-200, timeout, missing mint, absurd value) → N stays at the previous round's N
  (first round: `StartingMinimum` 1,000,000) and an **Error** log says `N kept at`.

### The pot — house-funded, frozen and reserved

- `Game.HouseFundedPot` = `PotBase` **0.03 SOL** + `PotStep` **0.005 SOL** for every **full**
  `PotStepBalance` (**10 SOL**) the treasury **holds** — its whole balance, not the headroom (17.17 SOL →
  0.035; under 10 SOL → 0.03), capped at `PotMaximum` 2 (`HolderJackpotPotRules`). The headroom (house SOL
  − open exposure − fee reserve − `ReserveFloor` 5 SOL) only **gates**: a pot it cannot cover opens the
  round PAUSED. ⚠️ Changed 2026-10-05 (owner, on `feature/old-game-modes-ux`); before that it was 0.5% of
  the headroom floored to 2 significant figures and clamped to 0.05–2 SOL.
- Sized by `HolderJackpotRoundPlanner` **under the shared treasury advisory lock**
  (`HouseEdgeCoverageService.AcquireLockAsync` + `EvaluateHeadroomAsync`, one retry of the lock and one
  re-read of a null balance); the round insert joins that transaction, so reserving and creating are one
  atomic write.
- **The reserved pot is house-edge exposure.** `HouseEdgeCoverageService.ReservedPotRounds` feeds
  `HouseEdgeCoverage.Evaluate`: every unsettled round with a pot adds the pot **plus one transfer fee
  reserve**. Hyper Roll's max stake and the VS HOUSE duel ceiling shrink by it all day. It is released
  with no code, the moment the round leaves the unsettled statuses.
- **PAUSED.** The round opens with `HolderEntriesPaused = true`, a null pot and an Error log
  (`opened PAUSED`) when: headroom cannot cover the pot floor, the treasury balance is unreadable, the
  mint is unconfigured, the `HolderJackpot` settings fail `Validate()`, the lock is unavailable, or there
  is **no planner** (the API host). A pot is never half-funded. Entries are refused with the generic
  not-receiving-bets message. The draw is still scheduled, finds no tickets, and the next round
  recomputes — a paused jackpot recovers by itself.
- **Nothing ever carries.** `CreateGame` forces `CarriedExtra = 0` on a holder round and
  `LotteryPotCarry.NextCarriedExtra` returns 0 for one. The pot ignores `LotteryPot` and `CarriedExtra`.
- ⚠️ **At most one live round per holder type.** `CreateGame` skips creation when the type already has a
  round in `WaitingBets` / `GameStarting` / `InProgress`, checked under the treasury lock. Without it an
  overdue draw job and startup `OpenNewGames` raced into two rounds and two reserved pots. A failed
  save/commit detaches the game so a later `SaveChanges` cannot insert it outside the lock.

### The draw

- Hangfire fires the scheduled `BetPlacedEvent`; for a holder round still accepting bets,
  `BetPlacedConsumer` runs **`HolderDrawRecheck`** before counting players.
- It batch-reads every `Made` holder ticket's balance — 1 + `DrawRecheckAttempts` (3) tries, only the
  still-unread wallets re-asked, total budget ~20 s.
  - **Below N → `Voided`** (`BetStatus.Voided = 6`, terminal). ⚠️ Only when the void is **persisted**
    (`GameService.TryUpdateBetStatus`); if the save fails the ticket is kept and an Error is logged.
  - **Unreadable → kept (fail-open)** with an Error naming the wallet and the game. An RPC outage never
    costs a real holder their ticket, and the draw stays on time.
- **Fewer than 2 valid tickets** → `NotEnoughPlayersToStart`, **no refunds** (nothing was paid), the
  reservation is released, and a new round opens with a fresh N and pot.
- **A voided ticket cannot win.** It is skipped by the typer's choices (`SpaceStrokerTyper.SetBets`), by
  winner marking in `RunGameConsumer` and by both `GameResult` winner branches. Its **seed stays in the
  provably-fair keystream prefix** (it was committed at entry), and the verification payload flags it
  `voided`, so the round still re-verifies offline.
- A redelivered draw never revives a void.

### The reward

- Pot = `HouseFundedPot`, split equally among the winners (in practice one — see exclusivity above),
  **commission 0** (the whole pot is house-funded).
- Since Profile V2 (#202) each winning wallet credited > 0 also gets a `jackpotWon` notification in the chat
  🔔 feed (one per wallet, amount = its credited total, `holder` flag) — see [`profile-v2.md`](profile-v2.md).
- Paid in **SOL to the player's own wallet** from the house wallet. `RewardGameConsumer` treats a holder
  round as **treasury-funded**, so the running balance guard applies. A redelivered `RewardGameEvent`
  never double-pays (`BetHasAlreadyBeenRewarded`).
- The **100,000 $STROKE winner bonus is gone**: `JackpotRewardService`, `IJackpotRewardService`,
  `ISolanaService.TransferJackpotReward` and `GetJackpotRewardBalance` are deleted, and
  `JackpotRewardSettings` became `HolderTokenSettings`.

### Stats, history and leaderboards

- Holder **credits** are excluded from global and per-type paid-out, winners-paid and biggest payout, and
  from a player's won / net / biggest win. Holder rounds never count for biggest pot.
- **Participation counts still include holder entries** (bets placed, rounds played, unique players),
  exactly like dev free bets.
- Leaderboards (race, PNL, hall of fame) are untouched — their `Amount > 0` filters already exclude a
  ticket.
- History rows show pot = `HouseFundedPot` and count holder winners; the round stays in the
  provably-fair view.

### Data model

- `Bet.EntryKind` — `BetEntryKind` `Paid = 1` / `Free = 2` / `Holder = 3`. `BetExtensions.IsFreeBet` =
  `Amount == 0` **and not** Holder; `IsHolderEntry` is the other half. ⚠️ "Nothing was paid" guards
  (`RefundInvalidBet`, the under-subscribed and deactivation refund loops) key on **`Amount == 0`**, not
  on `IsFreeBet`.
- `BetStatus.Voided` (= 6). `Game.HolderMinimum` / `HouseFundedPot` / `HolderEntriesPaused`.
  `LotteryParameters.HolderOnly`.
- One additive migration, **`20261001185217_HolderJackpot`**: the columns above on `Games`, `GameBets`
  and `LotteryParameters`, a **backfill** `UPDATE "GameBets" SET "EntryKind" = 2 WHERE "Amount" = 0`
  (existing dev free bets), and the unique partial index.

### API

- `GET /games/{gameTypeId}/holder-eligibility?wallet=` →
  `{ wallet, symbol, minimum, balance | null, eligible, entriesPaused }`, read off the type's live round.
  Cached 15 s per round + wallet, **never caching a null balance**; rate limit
  `get:/games/*/holder-eligibility` 30/min. An unknown, inactive or non-holder type, or no live round →
  the generic `ParameterNotFound`; a malformed wallet → the vague proof sentence.
- Round payload (`ActiveGameDto`): `holderOnly`, `holderMinimum`, `houseFundedPot`, `entriesPaused`.
  Bets: `voided`. Type: `lotteryParameters.holderOnly`.

### Configuration

- `GeneralSettings:Games:HolderJackpot` (`HolderJackpotSettings`): `TargetUsd`, `StartingMinimum`,
  `MinimumFloor`, `MinimumCeiling`, `PotBase` (> 0, ≤ `PotMaximum`), `PotStep` (≥ 0), `PotStepBalance`
  (> 0), `ReserveFloor`, `PotMaximum`,
  `PriceApiUrl`, `PriceTimeoutSeconds`, `DrawRecheckAttempts`, `DevAssumedBalance`. `Validate()` runs at
  startup in **both** hosts; a bad value logs at Error and holder rounds open PAUSED.
- No new secret (the Jupiter lite endpoint is keyless), no Terraform change, no front env var.

### Startup self-heals (Consumers) and the deploy

- **`HolderLotteryReconciler`** flips an existing default type onto the config: `HolderOnly`,
  `LotteryPot` 0, `RequiredAmount` 0, `AllowFreeBet` false, and the `Description`. The `AllowFreeBet`
  environment patch **skips holder defaults**, or the two would flip-flop on every non-prod boot.
- **`HolderJackpotTransition`** runs right after default-parameter seeding (before
  `CorrectScheduledLotteryDraws` / `ReplanOpenRounds`): for a live **legacy** round of a holder type
  (`HolderMinimum == null`) it marks every paid `Made` bet `NeedsRefunding` and publishes
  `RefundInvalidBetEvent` (reason `Space Jackpot is now free for token holders.`), closes the round
  `GameEndedAbruptely`, and `OpenNewGames` then opens the first holder round. Idempotent; settled rounds
  are never touched.
- ⚠️ **Deploy note.** Deploy **right after the 22:00 UTC draw has settled** — `CloseOngoingGames` runs
  first and would end an `InProgress` round abruptly. On the first Consumers boot watch the log for
  **`opened PAUSED`** and **`N kept at`**.

### Frontend and copy

- The holder branch of `useGame.runBet` sits **before wallet-source selection** and never reaches the
  transfer path: paused → a notice; balance known and below N → the bound plus a **GET $STROKE** link
  (only when `tokenLive`) and **no signature**; otherwise nonce → sign → submit with `amount: 0`. Server
  messages are shown as-is for holder entries. A null balance never blocks the lever — the server decides.
- `useHolderJackpot` polls eligibility every 30 s (stale-response guard), builds the message, and signs on
  desktop or through the **mobile deeplink hop** (the resume drops an entry whose round changed).
- Bets are refetched when a holder round leaves `WaitingBets`, so **TICKET VOIDED** shows live.
- Lever reads **ENTER FREE** and is disabled when paused or past the draw time. Terminal lines: ENTRY,
  YOUR $STROKE ✓/✗, POT, DRAW IN, YOUR TICKET, TICKET VOIDED, JACKPOT PAUSED. Lobby card: **FREE FOR
  HOLDERS · ≥ N**, the pot, the countdown, PAUSED.
- Copy everywhere: **"Token holders get a free daily lottery draw."** `HomeTokenCard.vue` is the verbatim
  twin of the LP's `TokenSection.vue` — they move together. ⚠️ One owner-sanctioned divergence (2026-10-27,
  #212): the app card's mechanic title reads **"Play Space Jackpot free"** and drops the *"Hold the day's
  minimum $STROKE at the draw."* sentence to save height; the LP and the guide keep it.
- **First-time notice (#212):** the first time a browser opens a holder-only type
  (`LotteryParameters.HolderOnly`, never the name), a one-time `ArcadeModal` (`JackpotHolderNotice.vue`,
  `useFirstJackpotNotice.ts`, `localStorage` `space-stroker.jackpot.first-visit-seen`) explains the free
  draw, today's minimum N, `You hold X` (connected only) and the entry + draw balance checks, with a
  **GET $STROKE** CTA to `/#swap`. Eligible wallets get a confirmation row instead. Gated on `tokenLive` and
  not embedded; a blocked open (another modal, boot recovery, idle disconnect, terms not accepted) is
  retried on the next trigger and not marked seen; storage failure counts as seen. See
  [`token-integration.md`](token-integration.md).
- The terminal's below-N **GET $STROKE →** link goes to the Home swap card (`/#swap`, same tab) since #212.
- ⚠️ **The rule "nothing may imply the token can be used to place bets" is deliberately reversed for this
  mode.** The token still never **stakes** a bet; holding it grants a **free** Space Jackpot entry. Do
  not "fix" the copy back — see [`token-launch.md`](token-launch.md) §4.
- LP: `GameModes` tag `FREE FOR HOLDERS · DAILY`; `/terms` — the house-cut clause names Cosmic Sprint
  only, and a Space Jackpot clause covers eligibility, the draw-time recheck, the house-funded pot and
  paused rounds.

## Key code touchpoints

**Backend (`monkify-backend/src`)**

| File | Role |
| --- | --- |
| `SpaceStroker.Domain/Bets/ValueObjects/BetEntryKind.cs`, `BetStatus.cs` | `Holder = 3`; `Voided` |
| `SpaceStroker.Domain/Bets/Services/BetExtensions.cs` | `IsFreeBet` excludes Holder; `IsHolderEntry` |
| `SpaceStroker.Domain/Bets/Services/BetDomainService.cs` | Pot and house-funded base from `HouseFundedPot` |
| `SpaceStroker.Domain/Games/Entities/Game.cs`, `LotteryParameters.cs` | `HolderMinimum`, `HouseFundedPot`, `HolderEntriesPaused`, `IsHolderRound`; `HolderOnly` |
| `SpaceStroker.Domain/Games/Services/HolderMinimumRules.cs` | The nice-number ladder and clamps |
| `SpaceStroker.Domain/Games/Services/HolderJackpotPotRules.cs` | Pot formula, 2-sig-fig floor, null = paused |
| `SpaceStroker.Domain/Games/Services/HouseEdgeCoverage.cs` | Reserved pots as exposure + fee reserve |
| `SpaceStroker.Domain/Games/Services/LotteryPotCarry.cs` | Holder round → carry 0 |
| `SpaceStroker.Domain/Games/Services/SpaceStrokerTyper.cs` | `SetBets` skips `Voided` |
| `SpaceStroker.Domain/Configs/Entities/HolderJackpotSettings.cs`, `HolderTokenSettings.cs` | Config + `Validate()` / `IsConfigured` |
| `SpaceStroker.Common/spacestrokersettings.json` | `Games:HolderJackpot`, `Operator:Token:HolderToken`, the Space Jackpot default, the rate-limit rule |
| `SpaceStroker.Common/Resources/ErrorMessages.resx` | `JackpotEntryInvalid`, `HolderBalanceUnavailable`, `HolderBelowMinimum` |
| `SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` | Inline mapping + the unique partial index |
| `SpaceStroker.Infrastructure/Migrations/20261001185217_HolderJackpot.cs` | Columns, backfill, index |
| `SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs` | `RegisterHolderEntry` |
| `SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/JackpotEntryMessages.cs` | The signed message |
| `SpaceStroker.Infrastructure/Handlers/Players/GetWalletNonce/GetWalletNonceRequest.cs` | `jackpot-entry` purpose |
| `SpaceStroker.Infrastructure/Handlers/Games/GetHolderEligibility/` | The eligibility read |
| `SpaceStroker.Infrastructure/Handlers/Games/GetGameVerification/GetGameVerificationHandler.cs` | `voided` on the payload |
| `SpaceStroker.Infrastructure/Services/Solana/HolderBalanceReader.cs`, `SolanaService.cs`, `Token2022Program.cs` | Token-2022 single + batch reads |
| `SpaceStroker.Infrastructure/Services/Pricing/JupiterPriceClient.cs` | Price (Consumers only) |
| `SpaceStroker.Infrastructure/Services/Games/HolderJackpotRoundPlanner.cs` | N + pot under the treasury lock |
| `SpaceStroker.Infrastructure/Services/Games/GameService.cs` | `CreateGame` (planner, one live round, detach), `TryUpdateBetStatus`, 23505 mapping |
| `SpaceStroker.Infrastructure/Services/Games/HouseEdgeCoverageService.cs` | `ReservedPotRounds`, `EvaluateHeadroomAsync` |
| `SpaceStroker.Infrastructure/Services/Games/BetRoundRules.cs` | `HolderTicketRefusal` |
| `SpaceStroker.Infrastructure/Services/Games/HolderDrawRecheck.cs` | The draw recheck |
| `SpaceStroker.Infrastructure/Consumers/BetPlaced/BetPlacedConsumer.cs`, `RunGame/RunGameConsumer.cs`, `RewardGame/RewardGameConsumer.cs` | Recheck call; voided never wins; treasury-funded payout |
| `SpaceStroker.Infrastructure/Services/Games/HolderLotteryReconciler.cs`, `HolderJackpotTransition.cs` | Startup self-heals |
| `SpaceStroker.Infrastructure/Services/Games/GameHistoryService.cs`, `GlobalStatsService.cs`, `GameStatsService.cs`, `Services/Players/PlayerStatsService.cs` | Holder pot / exclusions |
| `SpaceStroker.Infrastructure/Services/HouseSeed/HouseSeedBetService.cs` | Skips holder rounds |
| `SpaceStroker.Api/Controllers/GamesController.cs` | `GET {gameTypeId}/holder-eligibility` |
| `SpaceStroker.Consumers/Program.cs` | Planner + price client registration, reconciler, transition |

**Frontend (`monkify-front/src`)** — `hooks/useHolderJackpot.ts` (eligibility, message, sign),
`hooks/useGame.ts` (holder branch of `runBet`, resume, refetch), `hooks/useMobileWallet.ts` +
`types/mobileWallet.ts` (the sign hop), `hooks/useLiveGames.ts` (card snapshot),
`utils/gameArchetype.ts` (`isHolderLottery`, `holderRoundAcceptsEntries`), `utils/countdown.ts`,
`utils/pot.ts`, `services/mainService.ts` (`getHolderEligibility`), `types/index.ts`,
`components/game/GameConsole.vue`, `components/game/GameTerminal.vue`,
`components/game-selection/GameTypeCard.vue`, `components/home/HomeTokenCard.vue`,
`components/guide/GuideModeCards.vue`, `components/guide/sections/{GuideToken,GuideHowTheGameWorks,GuideRefunds,GuideWhatYouWin,GuideTheModes}.vue`.

**Landing page (`monkify-lp/src`)** — `components/TokenSection.vue`, `components/GameModes.vue`,
`pages/terms.astro`.

## Tests

- `SpaceStroker.Tests.Integration/HolderJackpotFlowTests.cs` — the mandatory happy path (two holders
  sign and enter → draw → the winner gets exactly one `Credit` equal to the frozen pot, no commission)
  plus the complex flows: pass-the-bag void (and the round re-verifies), under-subscribed after voids,
  fail-open, entry refusals and the concurrent double entry, pot reservation vs Hyper Roll, the deploy
  transition, and a paused round. The tests publish `BetPlacedEvent` so the recheck runs.
- Unit: `HolderMinimumRulesTests`, `HolderJackpotPotRulesTests`, `HolderJackpotRoundPlannerTests`,
  `HolderDrawRecheckTests`, `HolderBalanceReaderTests`, `JupiterPriceClientTests`,
  `HolderJackpotTransitionTests`, `GetHolderEligibilityHandlerTests`, `CreateGameTests`, the holder cases
  in `RegisterBetHandlerTests`, and the settings pins `HolderJackpotSettingsBindingTests` /
  `HolderTokenSettingsBindingTests` / `RateLimitSettingsTests`.
- ⚠️ `HolderLotteryReconciler` uses `ExecuteUpdate`, a silent no-op on the in-memory provider — it is
  proven by the integration suite (`DefaultGameSeedingTests`), not the unit suite.
- Verified before merge: 2216 unit / 264 integration green; live devnet runs with a real
  Token-2022 mint; a read-only mainnet probe of the production balance / price / pot code (mint is
  Token-2022 with 6 decimals; N = 1,000,000 at $0.0000160; pot ≈ 0.06 SOL at a 17.16 SOL treasury).

## Known gaps

- `WalletProof.TryConsume` is not atomic (pre-existing; also affects duel-cancel and Orbit).
- A concurrent **first** entry by a brand-new wallet can 500 on the `Player` upsert (pre-existing).
- Non-holder lotteries still have the boot-time duplicate-round race (no pot involved).
- The tokenomics "jackpot seed" allocation notes (app `constants/token.ts`, LP `constants.ts`,
  `FeedbackerSection.vue`) are untouched — the owner decides whether they still hold.
- Non-associated token accounts read as below N.

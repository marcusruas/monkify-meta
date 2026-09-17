# Space Stroker — "House edge rework" — Implementation Spec

> **Level:** `Feature` (money-affecting behaviour change across referral earnings, the house cut and
> house seeding). **Repos:** `monkify-backend`, `monkify-front`, `monkify-lp`, `monkify-meta`.
> **Branch / worktree:** `feature/house-edge-rework` in all four.
> **Trello:** none — raised directly by the owner on 2026-09-17 after a partner reported being paid
> "50% of what ppl wager, not 50% of house edge".

---

## 1. Summary

Three changes, shipped together because the first two share one number:

1. **Partners earn a commission on the house cut, not on the bets.** The house cut is
   `CommissionPercentage` (2%) of what referred players bet, won or lost; the partner's
   `SharePercentage` is their share **of that cut**. A referred player bets 100 SOL → 2 SOL house cut →
   a 50% partner earns 1 SOL (previously 50 SOL).
2. **House cut 5% → 2%** (`GeneralSettings:Operator:Token:CommissionPercentage`).
3. **The house seed bet yields to Orbit.** When **2 or more** Orbit players are betting in a round, the
   house seed wallet does not place its bet.

### 1.1 Before / after

| | Before | After |
|---|---|---|
| Partner earnings | `floor(Σ paid stakes × Share/100)` | `floor(Σ paid stakes × Commission/100 × Share/100)` |
| Referred player bets 100 SOL, 50% partner | 50 SOL | 1 SOL (50% of the 2 SOL house cut) |
| House cut | 5% of winnings above own stake | 2% of winnings above own stake |
| House seed | one bet in every production round | skipped when ≥ 2 Orbit players bet in the round |

---

## 2. Constraints (MANDATORY)

- `constraints.md` §2 worktree-first; §3 unit tests via `scripts/run_tests.bat`; §3 integration tests.
- The referral payable invariant (`specs/referral-cash-out.md`) is untouched: the cash-out ledger is
  still summed with **no status filter**, and a negative payable is still **carried, never clamped**.
- The house seed's production gate stays in `HouseSeedBetConsumer`; every decision stays in
  `HouseSeedBetService` so the Testcontainers suite can drive it.

---

## 3. Resolved product decisions (owner, 2026-09-17 — answered "A" to all three)

| # | Question | Decision | Why |
|---|---|---|---|
| 1 | What "house edge" means for a partner | **A — the commission their losses generated. ⚠️ SUPERSEDED in §3.3: a share of the 2% house cut on everything referred players bet.** `Share × Commission × Σ losing paid stakes`, only in rounds where at least one PAID winner was paid. | The only basis where "50% of house edge" is literally true and a share ≤ 100 can never pay more than the traffic earned. B (rate × turnover) still overpays ~2× heads-up. |
| 2 | Past earnings when the rate moves 5% → 2% | **A — re-price history at the current rate.** No per-round snapshot, no migration. | Production's only real partner is the 50% one whose history is the mistake being corrected. Consequence accepted: every future rate change re-prices partner history too, the same way a `SharePercentage` edit already does. |
| 3 | What "2 or more people registered on Orbit" means | **A — Orbit players actually betting in THIS round.** Union of (players whose `FundedByOrbit` bet has landed) and (`IOrbitBetPlacementService.ResolveCandidates(parameters, gameId)`), checked after the placement delay. | Skips only when the round will start without the house, using the planner's own eligibility rule so the two cannot disagree. B (any enabled enrolment) counts empty balances; C (global) would stop seeding types Orbit does not play. |

### 3.1 Why the Orbit count is a union (a code finding)

`ResolveCandidates(…, gameId)` **excludes** players who already bet in the round. The seed checks
after its randomised delay, by which point `OrbitPlacementPlannerConsumer` (same `GameOpenedEvent`)
has usually dispatched and the Orbit bets have landed — so a candidates-only count returns **zero**
for a round Orbit has fully filled, and the house would seed anyway.

### 3.2 Reversal of house-seed decision 11 (scoped)

`specs/house-seed-bet.md` decision 11 — "the seeded bet is placed whoever else turned up" — still
holds for **humans betting in person**. Orbit players are the single exception, pinned by
`StillSeeds_WhenTwoHumansWhoAreNotOnOrbitHaveBet`.

### 3.3 Decision 1 superseded: a share of the house cut on what referred players bet (owner, 2026-09-17)

Decision 1 went through two branch revisions (losses only, then losses plus won profit) before the
owner fixed the rule with a worked example:

> Player referred by partner X spent 100 SOL in bets. House cut of that is 2 SOL. Partner has 50%
> commission. Partner gets 1 SOL (50% of the house cut). The 2% is the house cut, not what the partner
> gets. The partner gets a percentage of the house cut — that is the commission.

- **Basis:** turnover — every paid bet by a referred player in a `RewardForWinnersCompleted` round, won
  or lost, with the pre-existing exclusions (free, refunded/parked, pre-cutover, the partner's own
  wallet). This is the #175 predicate, unchanged.
- **Earnings:** `floor(turnover × CommissionPercentage/100 × SharePercentage/100)`.
- ⚠️ **Accepted consequence:** the house does not actually keep 2% of every bet — it keeps 2% of a
  winner's profit, i.e. of the LOSING stakes (≈1% of turnover heads-up, ~1.67% at six bets with one
  winner). So a 50% partner is paid ≈100% of the cut the house really took on heads-up rounds, and a
  share above ~50% can pay more than the house kept. The rule is the owner's stated model; the gap is
  documented here, not enforced in code.

---

## 4. Data model

**No change. No migration.** Earnings remain a derived aggregate over `GameBets` / `Games` /
`Players` / `ReferralPartners` / `ReferralCashOuts`. `IX_GameBets_ReferralCode` still leads the query,
which is the #175 predicate unchanged.

---

## 5. Configuration

- `spacestrokersettings.json` → `GeneralSettings:Operator:Token:CommissionPercentage`: `5.0` → `2.0`.
  Overridden by no deploy surface, so the committed file **is** the production rate (pinned by
  `CommissionRateTests`).
- `ReferralPartners.SharePercentage` keeps its column and precision; its **meaning** changes from
  "% of turnover" to "% of the house cut on turnover". The production partner row set at `50` now means what the
  partner was told. **No data change is required for it.**

---

## 6. Backend behaviour

### 6.1 `ReferralEarningsService`

- `ResolveTurnover` keeps its name and its #175 predicate unchanged (free, refunded/parked,
  pre-cutover and self-referral bets excluded; wins and losses both count).
- `ApplyShare` multiplies by `CommissionPercentage/100` then `SharePercentage/100`, and floors
  (`MidpointRounding.ToZero`) **once**, on the final product.

### 6.2 `HouseSeedBetService`

- Injects `IOrbitBetPlacementService`.
- After the post-delay re-read, `CountOrbitPlayers(game) >= 2` ⇒ log at Information and return
  before any balance read, server-seed commit, choice derivation or transfer.
- Lottery types and an unconfigured Orbit account resolve zero candidates, so they always seed.

### 6.3 Behaviour that must NOT change

- `BetDomainService` commission arithmetic (only the configured rate moves).
- Cash-out reservation, solvency guard, cooldown, the no-status-filter ledger sum.
- House seed idempotency, min-players refusal, SOL floor, choice collision handling.

---

## 7. Frontend (`monkify-front`)

- `PartnerPage.vue`: `N% OF REFERRED TURNOVER` → `N% OF THE HOUSE CUT`.
- The hand-written 5% copies → 2%: `GuideToken.vue` (title + body + rule comment),
  `GuideWhatYouWin.vue` (body + rule comment), `HomeTokenCard.vue`.
- The STATS badges already read `commissionPercentage` live — no change.

## 7b. Landing page (`monkify-lp`)

- `TokenSection.vue`: "The profit from the 2% house cut…".
- `terms.astro`: "currently 2%", `LAST_UPDATED` → 17 September 2026. The REFERRALS clause names no
  percentage or basis, so it is unchanged.

## 7c. Meta

- `deploy/sql/referral-payout-report.sql` mirrors the new predicate and formula, reads partners from
  the `ReferralPartners` table (the config note was stale since #177), and takes the commission rate
  as a parameter.

---

## 8. Deploy sequencing

- The commission rate is read **at payout time**, so deploy with **no paid round open** and release
  **API and Consumers together** (same rule as #175 / #177).
- **Deploy the LP with the backend**, or `/terms` publishes a 5% cut while 2% is taken.
- Partner balances re-price the moment Consumers/API run the new code (earnings cache ≤ 60 s). A
  partner already paid more than their new all-time earnings will read **Available 0** and carry the
  deficit — expected, not a bug.

---

## 9. Out of scope

- Snapshotting the commission rate per round (decision 2 → B).
- Crediting no-winner rounds, where the house keeps the whole pot but no commission is levied.
- Clawing back anything already paid out under the turnover basis.
- A seeding config flag; the Orbit threshold is a code constant like the production gate.

---

## 10. File checklist

### 10.1 Backend
- `Services/Referrals/ReferralEarningsService.cs`, `IReferralEarningsService.cs`
- `Services/HouseSeed/HouseSeedBetService.cs`
- `SpaceStroker.Common/spacestrokersettings.json`
- Doc comments: `ReferralSettings.cs`, `ReferralPartner.cs`, `CacheHelper.cs`, `ReferralCashOutReserver.cs`
- Tests: `ReferralEarningsServiceTests.cs`, `CommissionRateTests.cs`, `HouseSeedBetServiceTests.cs`,
  `HouseSeedBetFlowTests.cs`, `ReferralCashOutFlowTests.cs`, `Harness/IntegrationTestFixture.cs`

### 10.2 Frontend
- `pages/PartnerPage.vue`, `components/guide/sections/GuideToken.vue`,
  `components/guide/sections/GuideWhatYouWin.vue`, `components/home/HomeTokenCard.vue`

### 10.3 Landing page
- `components/TokenSection.vue`, `pages/terms.astro`

### 10.4 Meta
- `specs/house-edge-rework.md` (this file), `deploy/sql/referral-payout-report.sql`

---

## 11. Tests

### 11.1 Unit
- `ReferralEarningsServiceTests`: the #175 turnover tests are kept with the suite partner at 50% of a
  2% cut; new `Turnover_CountsALosingBet_TheSameAsAWinningOne`, and
  `Earnings_AreThePartnersShareOfTheHouseCut` (the owner's 100 → 2 → 1 example), a 25% share of the
  same cut, current-rate re-pricing, and zero cut ⇒ zero earnings.
- `CommissionRateTests.CommissionPercentage_IsTwoPercent`.
- `HouseSeedBetServiceTests`: skip on 2 candidates, on 2 landed Orbit bets with no candidates, on 1
  landed + 1 candidate; seed on 1 Orbit player, on the same player landed and candidate, on 2 humans.

### 11.2 Integration (rule 8)
- `HouseSeedBetFlowTests.SeedRound_SkipsTheRound_WhenTwoOrbitPlayersWillBetInIt` — real Postgres,
  real planner eligibility query. The class now disables Orbit enrolments on dispose (churn rule).
- `ReferralCashOutFlowTests` happy path proves the new predicate translates on Postgres: the suite
  partner is now 50% (at 1% a heads-up round of 2 would earn 0.0008, under the 0.001 minimum), and
  `EarnAsync` returns `4 × Commission × Share` — two referred stakes of 2, won or lost alike.

---

## 12. Acceptance criteria

1. A partner's `EarnedAllTime` equals `floor(Σ referred paid stakes × CommissionPercentage/100 ×
   SharePercentage/100)`: 100 SOL bet at a 2% cut and a 50% share earns 1 SOL.
2. A referred player's bets count the same whether they won or lost.
3. The committed commission rate is 2%, and every hand-written rate in the app and LP says 2%.
4. The partner page labels the share as a share of the house cut.
5. In production, a round with ≥ 2 Orbit players betting receives no house seed bet; a round with
   0–1 Orbit players is seeded exactly as before, whatever humans have bet.

## 13. Manual QA

- `/partner` for the production partner after deploy: share reads `50% OF THE HOUSE CUT`; Available
  drops to the house-edge figure (or 0 with a carried deficit).
- Run section 3 of `referral-payout-report.sql` with `commission_percentage = 2.0` and compare with
  the page.
- Enrol two funded wallets on one Orbit type in production and confirm the next round has no bet
  from the house seed wallet; pause one and confirm the following round is seeded.

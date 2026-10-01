# Feature — Partner earnings page (referral cash-out)

> Status: **shipped.** Authoritative spec: [`../specs/referral-cash-out.md`](../specs/referral-cash-out.md)
> (merged via `feature/referral-cash-out` on 2026-08-24).
>
> Supersedes **decision 5 of [#175](referral-program.md)** (*"report only — the product pays
> nobody"*) and nothing else. Attribution, `Player.ReferralCode`, `Bet.ReferralCode` and the stamping
> all stand as that card shipped them; `deploy/sql/referral-payout-report.sql` survives as the
> **reconciliation** tool rather than the payment mechanism.

## What it does

A referral partner opens the **unlisted** `/partner` page, connects the wallet registered for their
code, signs a challenge, and reads a statement: earned all time, already paid out, and available.
Pressing **Cash out** pays their whole available balance in SOL to the wallet configured for that
code — the platform's first automated outbound payment to someone who is not a player.

## Core rules

### Who a partner is

- Partners are rows in **`ReferralPartners`**: `Code`, `NormalizedCode`, `DisplayName`,
  `SharePercentage`, `PayoutWallet`, `Active`. ⚠️ **Not config.** `GeneralSettings:Operator:DefaultPartners`
  is only a **create-only seed** run at Consumers startup, exactly like `DefaultGames` — an existing
  row is never rewritten, so a restart cannot resurrect a deactivated partner or move a share
  percentage out from under money already earned. A `MOCK_` wallet seeds as `NULL`.
- Two ways in. **Self-registration** on `/partner` (#195,
  [`partner-self-registration.md`](partner-self-registration.md)) inserts a row at the fixed
  `SharePercentage = 5`, `Active = true`, with `TermsAcceptedAt` set. Everything else — bespoke rates,
  deactivation, corrections — is **hand-run SQL** against the table. There is still no admin surface.
- `NormalizedCode` is UNIQUE; `PayoutWallet` is UNIQUE **filtered on NOT NULL**, so many partners may
  sit at "not set up to be paid yet" simultaneously. ⚠️ These two indexes replaced startup throws —
  the duplicates can no longer be written, and the in-memory directory resolves first-wins rather
  than taking every host down over a row the database already rejects.
- One wallet maps to exactly one code. A partner running two campaigns needs a second wallet.

### `Active` gates attribution only

- `Active = false` stops new bets carrying the code — **including bets from players already referred
  to that partner**, because the bet path re-resolves `Player.ReferralCode` through the directory
  instead of copying it. Their turnover stops growing from that moment.
- It touches **no history**. Earlier bets keep their code, the balance stays payable, and the partner
  can still sign in and cash out. ⚠️ Every money-path lookup (`ResolveByCode`, `ResolveByWallet`,
  `AllPartners` for the solvency guard) deliberately ignores `Active`: refusing to pay a balance
  someone already earned is a liability, not an off switch.
- The directory is cached **60s**, so a deactivation takes up to a minute to bite. Accepted: the
  alternative is a query on every bet. An operator who needs it instant restarts Consumers.

### What is earned

- `earnings = SharePercentage × SUM(paid stakes of that code's players)`, over rounds that reached
  `RewardForWinnersCompleted`, excluding free bets (`Amount = 0`), `BetStatus` 3/4/5, bets before
  `SettlementCutoverUtc`, and bets made from the partner's **own** `PayoutWallet` — the one hard
  self-referral guard.
- ⚠️ **Exact match on the code, never `lower()`** — that is what keeps the partial referral index
  (`WHERE "ReferralCode" IS NOT NULL`) usable. Normalization happens once, at write time.
  ⚠️ That index is now **`IX_GameBets_ReferralCode_CreatedDate_Id`** — widened (and the single-column
  one dropped) by the partner table, and widened again to `INCLUDE (Amount, PlayerId)` by the player
  roster, with `ReferralCode` still leading, so this aggregate is unaffected and measurably slightly
  faster. See [`partner-player-roster.md`](partner-player-roster.md).
- ⚠️ `SettlementCutoverUtc` must have its `DateTimeKind` normalized to UTC before it reaches the
  query: the configuration binder hands back `Local`, and Npgsql refuses a non-UTC value for a
  `timestamptz`, which 500s the whole statement.
- The share rounds **down** (`MidpointRounding.ToZero`).
- ⚠️ Payable is recomputed **all-time and unclamped**, so a deficit is carried and worked off by new
  turnover. The page shows `max(0, …)`; the button refuses `<= 0`. **Ops rule: do not re-rate a live
  partner — issue a new code**, because a `SharePercentage` edit re-prices their entire history.

### The ledger

- `ReferralCashOut` is **append-only with SIGNED amounts**: a withdrawal is negative, a reversal
  positive. Rows are never deleted and an amount is never rewritten.
- ⚠️ **The invariant, exactly Orbit's:** `payable = earnings + SUM(Amount)` over **ALL** rows with
  **NO status filter**. A `Failed` withdrawal still counts against the balance until its `Reversal`
  is appended. Excluding failed rows double-pays whenever a transfer times out but confirms on-chain.
- A `REVERSED` row stays **visible** on the page and nets to zero with its pair — hiding it would
  stop the headline figures reconciling against what the partner can see.

### Cashing out

- The session is a **read** credential. Every cash-out demands a **fresh** `referral-cashout`
  signature on its own purpose, so a leaked session can never drain a balance.
- There is no amount and no destination in the request: it is always the whole payable, to the
  `PayoutWallet` configured for the code. Never caller-supplied.
- Refused below the configured minimum (**0.1 SOL** by default), API-side, before a row is written.
- **Cooldown** (60 min by default) is one shared helper used by both the button's greying and the
  write, so they cannot drift. A failed withdrawal still consumes it.
- ⚠️ A **unique partial index** on `(ReferralCode) WHERE Type = 0 AND Status IN (0, 1)` is the
  concurrency gate — the cooldown is a *time* check, so two presses in the same millisecond both pass
  it. The loser gets `23505` having written nothing. The filter must cover `Executing` as well as
  `Pending`, or it stops being unique the instant the consumer claims the row.

### Settlement (`PayReferralCashOutConsumer`, Consumers only)

- Claims its row with a conditional `Pending → Executing` update, so an at-least-once redelivery
  affects zero rows and never pays twice.
- ⚠️ **Definite vs uncertain is decided by WHERE it failed, not by the return value.** Every pre-send
  guard — not configured, `MOCK_` value, account collision, solvency, below rent-exempt, below the
  minimum — is **definite**: nothing was built or signed, so the withdrawal fails and a `Reversal` is
  appended. A `null` back **from the transfer call** is **always uncertain**: the row is left
  `Executing`, an Error is logged, and **no reversal is written**. `OrbitCashOutConsumer` must not be
  copied here — it reverses on any `null`.
- **Solvency is pool-aware**: `balance − owed to ALL partners − amount ≥ MinimumSolBalance`. A flat
  floor passes forever, because with native SOL the money owed sits *in* the balance being read. Each
  partner is clamped at 0 so one in deficit cannot lend headroom to another. A failed balance read is
  "unknown", not "empty", and does not fail the payment.
- The payout account is **dedicated** — never the treasury, the Orbit account or the house seed, and
  it refuses to configure on collision with any of them. A referral-maths bug can drain at most the
  referral float.

### What a caller cannot learn

- An unknown wallet, an unknown code and a bad signature are **byte-identical** refusals
  (`constraints.md` §6) — the partner list is not enumerable.
- `GET /referrals/me` returns totals and cash-out history only: no referred-wallet identity, no
  wallet count, no bet count. A partner with one referred player would otherwise learn exactly what
  that player wagered. ⚠️ **The STATEMENT still holds that line; the partner page no longer does.**
  `GET /referrals/me/wagers` lists the referred PLAYERS themselves, one row each — an owner decision
  of 2026-09-18 with that exact trade-off on the table. Wallets leave the server **truncated**
  (`7Xf4…9kQa`) and the statement itself stays totals-only (it is cached). ⚠️ The partner's own
  `PayoutWallet` is **not** one of those rows, matching the self-referral guard the earnings predicate
  has always carried. See [`partner-player-roster.md`](partner-player-roster.md).
- `/partner` is unlisted: not in the header menu (a 3×3 icon pad that cannot take a tenth cell) — its
  only in-app link is the cold home hero's BECOME A PARTNER (#195) — and
  in `EMBED_REDIRECT_ROUTES`, because a commercial balance inside a casino's iframe is the same leak
  that put Orbit in that set. `robots.txt` already covers it via the app host's blanket `Disallow: /`.

## Key code touchpoints

**Backend (`monkify-backend`)**

- `SpaceStroker.Domain/Referrals/Entities/ReferralPartner.cs`, `ReferralCashOut.cs`, and the
  `Type`/`Status` value objects
- `SpaceStroker.Domain/Games/Services/ReferralDirectory.cs` — the immutable lookup snapshot;
  `ResolveActiveCode` (attribution) vs `ResolveByCode` / `ResolveByWallet` / `AllPartners` (money)
- `SpaceStroker.Infrastructure/Services/Referrals/` — `ReferralDirectoryProvider` (60s cache),
  `ReferralEarningsService` (the single predicate), `ReferralCashOutGate`, `ReferralCashOutReserver`,
  `ReferralPayoutAccountResolver`, `ReferralMessages`
- `SpaceStroker.Infrastructure/Handlers/Referrals/{CreateReferralSession,GetReferralEarnings,RequestReferralCashOut}/`
- `SpaceStroker.Infrastructure/Consumers/ReferralPayout/PayReferralCashOutConsumer.cs`
- `SpaceStroker.Infrastructure/Services/Wallets/` — the wallet-session component shared with Orbit
  (same secret, same `{wallet}|{expiresAt}` payload, **no audience field**)
- `SpaceStroker.Api/Controllers/ReferralsController.cs`, `Referrals/ReferralSessionAccessor.cs`
  (`X-Referral-Session`)
- `SpaceStroker.Consumers/Program.cs` → `CreateDefaultReferralPartners`
- Migrations: `AddReferralCashOuts`, `AddReferralPartners`

**Frontend (`monkify-front`)**

- `src/pages/PartnerPage.vue`, `src/hooks/usePartnerEarnings.ts` (message builders byte-identical to
  `ReferralMessages`; session persisted per wallet in `localStorage`; a settling cash-out is polled
  every 2s for at most 30s), `src/services/partnerService.ts`, `src/types/partner.ts`
- `src/routes.ts` (unlisted `/partner`), `src/main.ts` (`EMBED_REDIRECT_ROUTES`)

**Deploy (`monkify-meta`)**

- `deploy/terraform/workload/main.tf` — `GeneralSettings-Operator-Token-ReferralPayout-PrivateKey` in
  **both** `base_secret_ids` and `base_secret_env` (guarded by `SecretBindingCheck`)
- `deploy/terraform/workload/modules/monitoring/main.tf` — the `referral_payout_blocked` log metric
  and the module's first alert policy
- `deploy/sql/referral-payout-report.sql` — now subtracts `ReferralCashOuts`

## Tests

- `SpaceStroker.Tests.Integration/ReferralPlayersFlowTests.cs` — the player roster's happy path, its
  session-less refusal, its live rate limit, and the partner's own bets staying out of it
  ([`partner-player-roster.md`](partner-player-roster.md)).
- `SpaceStroker.Tests.Integration/ReferralCashOutFlowTests.cs` — the mandatory happy path
  (nonce → session → statement → signed cash-out → `Paid`) plus the reversal path.
- `SpaceStroker.Tests.Integration/ReferralPartnerTableFlowTests.cs` — seeding, the `Active` split,
  both unique indexes rejecting duplicates, and many NULL payout wallets coexisting.
- Unit coverage under `SpaceStroker.Tests/UnitTests/{Domain,Handlers/Referrals,Services}`.
  ⚠️ The consumer has **no** unit tests: it claims its row with `ExecuteUpdateAsync`, which the
  in-memory provider cannot translate — the same reason `OrbitCashOutConsumer` has none.

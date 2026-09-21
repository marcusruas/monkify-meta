# Feature — Partner wager table (your players' bets)

> Status: **shipped.** Merged via `feature/partner-player-bets` on 2026-09-21 in `monkify-backend`
> and `monkify-front`.
>
> ⚠️ **No spec and no Trello card.** This one was built straight from a Claude Design mockup round
> (five directions, the owner picked **1a** — "by the book": a filter bar over the cash-out table's
> own styling, numbered pages). It skipped `/monkify-create-spec`, so **this file is the only design
> record** — there is nothing in `specs/` to fall back on.
>
> Extends [`referral-cash-out.md`](referral-cash-out.md) (#177). Attribution is unchanged
> ([`referral-program.md`](referral-program.md)).

## What it does

A second section on the unlisted `/partner` page, under CASH-OUT HISTORY: **every bet placed by a
wallet that arrived through the partner's link**, filterable and paginated. One row per bet — when it
was placed, which player, which game, the stake, how it ended, and what the partner earned from it.

## The decision this reverses

⚠️ **#177 deliberately shipped totals only.** `GET /referrals/me` returns earnings, cash-out history
and nothing else: no referred-wallet list, no wallet count, no bet count, because **a partner with a
single referred player would otherwise learn exactly what that player wagered**. That reasoning is
still true — the owner reversed the trade-off anyway on **2026-09-18**, with it stated, because a
partner who cannot see the traffic they are paid for cannot check the number.

What survives of the old stance, and must not be widened without another decision:

- **Wallets are TRUNCATED server-side** (`7Xf4…9kQa`) before they leave the API. The endpoint never
  returns a full address, so a partner cannot walk away with a list of player addresses.
- The **statement** (`/referrals/me`) is still totals-only, and stays that way: it is cached, and a
  per-player field there would ride that cache.
- Still **no referral code on any player-facing contract** — the reversal is about what the partner
  reads about their own traffic, nothing else.

## The endpoint

`GET /referrals/me/wagers` — session-gated exactly like the statement (`X-Referral-Session`; a
missing, expired or forged session gets the one vague refusal, `constraints.md` §6). The partner is
resolved **from the session wallet**; no parameter names a partner or a code.

⚠️ **Routed `/me/wagers`, not `/me/bets`.** No referral path may contain the `/bets` substring —
`RateLimitSettingsTests` refuses any rate-limit rule matching it, so that the real bet path can never
be throttled (a rejected `POST /games/{id}/bets` means a transfer already left a player's wallet).

| Query parameter | Meaning |
| --- | --- |
| `pageNumber` / `pageSize` | 1-based; size clamped to **1–50**, never rejected. Past the end returns an empty page. |
| `player` | A wallet fragment, **or the truncated form pasted back in** (`7Xf4…9kQa` → prefix + suffix match). Case-sensitive: base58 is. Over 44 chars matches nothing. |
| `gameTypeId` | One game type. |
| `result` | `Open`, `Won`, `Lost` or `Refunded`. |
| `days` | Last N days (capped at 3650). |

The response carries the page plus **`gameTypes`** — the filter's options, built from the types this
partner's players actually played, so a retired type with history still appears.

### What a row says

- **Result is derived on every read, never stored:** `Won` = bet awaiting/received its reward;
  `Refunded` = refunding/refunded; `Lost` = an unrewarded bet in a round that **finished paying out**;
  everything else is `Open`. ⚠️ A bet is never called lost before its round has paid — that would be a
  lie the partner can see, and a round can still end in an error state.
- **`share`** is that bet's contribution to the partner's earnings, or **null** when it earns nothing
  (not settled, refunded, or placed from the partner's own payout wallet).
- Free bets (amount 0) and pre-`SettlementCutoverUtc` bets are **not listed at all** — the first earn
  nothing, the second are denominated in the previous settlement asset and cannot honestly be shown
  as SOL.

### The share column cannot drift from the statement

`IReferralEarningsService` grew two members so the table asks the **same predicate** the headline
figures use, instead of restating it:

- **`EarningBets(partner)`** — THE predicate as a query (paid, settled, not refunded, post-cutover,
  not the partner's own wallet). `ResolveTurnover` is now just its `SUM(Amount)`.
- **`ApplyShare(partner, amount)`** — house cut × share, rounded down once.

⚠️ Per-bet rounding means the column can sum to a hair **under** "earned all time" (never over):
each row floors at the asset's decimals, the headline floors once on the total.

## Performance — the index this feature widened

Every wager query leads on `Bet.ReferralCode`, and the single-column partial index #177 added could
not serve the ordering, so a large partner's page read **all** of their rows and sorted them.
**Measured** on a 2M-bet copy of the schema with one 200k-bet partner (2026-09-21):

| Query | `IX_GameBets_ReferralCode` | `IX_GameBets_ReferralCode_CreatedDate_Id` |
| --- | --- | --- |
| Ordered page (20 rows) | 107 ms | **0.6 ms** |
| Page count | 106 ms | **12 ms** (index-only) |
| Page 50 | 105 ms | **4 ms** |
| Earnings aggregate (statement, cash-out, solvency) | 220 ms | **172 ms** |

So `IX_GameBets_ReferralCode` was **replaced** (migration `WidenReferralCodeIndexForWagerTable`) by:

```sql
CREATE INDEX "IX_GameBets_ReferralCode_CreatedDate_Id"
  ON "GameBets" ("ReferralCode", "CreatedDate" DESC, "Id" DESC)
  INCLUDE ("Amount") WHERE "ReferralCode" IS NOT NULL;
```

- **Replaced, not added:** `ReferralCode` is still the leading column, so every earnings query keeps
  using it and a referred insert still pays **one** index write. Still partial, for the same reason as
  before — unreferred bets are the majority and pay nothing.
- ⚠️ **`INCLUDE (Amount)` is load-bearing.** The key alone made the count *slower* (125 ms, heap
  fetches for the `Amount > 0` filter); with `Amount` in the index the count is index-only.
- ⚠️ **The page's `ORDER BY CreatedDate DESC, Id DESC` must match the index order.** Change either and
  the page silently goes back to sorting the partner's whole history.
- The one query no index helps is the **game-type options** (it must visit every one of the partner's
  bets and join its round, ~150 ms for the large partner), so it is **cached per partner for 10
  minutes** (`CacheHelper.GenerateReferralWagerGameTypesCacheSettings`). The rows themselves are
  **never** cached: a round that just settled has to show immediately.

## Rate limiting

`get:/referrals/me/wagers` — **30/min per client IP** (`IpRateLimiting`, committed
`spacestrokersettings.json`). It is the only referral read that queries the bet table on every call.

⚠️ **GET only, not `*:`.** The browser sends an OPTIONS preflight before every read (the session rides
in a custom header); counting those would halve the budget for nothing, and OPTIONS touches no
database. Counters are **per API instance** (in-memory store), so N instances mean N × 30; Cloud
Armor's per-IP throttle (600/min, `deploy/terraform/workload/modules/edge`) is the blunt backstop.

⚠️ The other referral routes (`/referrals/session`, `/referrals/me`, `/referrals/cash-outs`) still
have **no** app-side rule — a deliberate gap, not an oversight to "fix" without a decision.

## Frontend

- `src/components/partner/PartnerWagersTable.vue` — the section: filter bar (wallet box, game, result,
  period, CLEAR), the table in the cash-out table's own styling, numbered pages with an ellipsis and a
  10/20/50 page-size picker. Result tone: orange lost, cyan won, grey refunded, yellow open.
- `src/hooks/usePartnerWagers.ts` — filters and paging. **Every filter change resets to page 1**
  (staying on page 7 of a result set that just shrank shows an empty table with no explanation), the
  wallet box is debounced 350 ms, and **only the latest response may write** so a slow page cannot
  flash stale rows.
- `usePartnerEarnings` now exposes its `sessionToken` **read-only** — the table borrows the session and
  never mints or clears one; signing in and out stays the statement's job.

## Key code touchpoints

**Backend (`monkify-backend`)**

- `SpaceStroker.Infrastructure/Handlers/Referrals/GetReferralWagers/` — handler + request/DTOs
- `SpaceStroker.Infrastructure/Services/Referrals/ReferralEarningsService.cs`, `IReferralEarningsService.cs`
  — `EarningBets`, `ApplyShare`, `ResolveSettlementCutover`
- `SpaceStroker.Api/Controllers/ReferralsController.cs` — `GET /referrals/me/wagers`
- `SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` — the widened index
- `SpaceStroker.Infrastructure/Migrations/…_WidenReferralCodeIndexForWagerTable.cs`
- `SpaceStroker.Common/spacestrokersettings.json` — the rate-limit rule

**Frontend (`monkify-front`)**

- `src/components/partner/PartnerWagersTable.vue`, `src/hooks/usePartnerWagers.ts`,
  `src/services/partnerService.ts`, `src/types/partner.ts`, `src/pages/PartnerPage.vue`

## Tests

- `SpaceStroker.Tests.Integration/ReferralWagersFlowTests.cs` — the mandatory happy path (referred bets
  through the real API → a settled round → the partner reads them back, filtered, paginated, truncated,
  with shares), the session-less refusal, and the **live rate limit** (exactly 30 allowed, then 429,
  while another client is still served).
- `SpaceStroker.Tests/UnitTests/Handlers/Referrals/GetReferralWagersHandlerTests.cs` — 18 cases,
  including one that pins the four result filters as an exact partition of every bet, and one that
  proves the game-type options come from the cache while the rows stay live.
- `SpaceStroker.Tests/UnitTests/Api/RateLimitSettingsTests.cs` — the rule is in the pinned exact set.
- `PassThroughCacheService` moved to `UnitTests/Shared/`.

## The commission basis is uniform, on purpose (owner, 2026-09-21)

⚠️ **Every game type pays a partner the same `turnover × 2% × SharePercentage`, including Hyper Roll** —
raised when this feature shipped, and **decided: keep it.** Do not "fix" it as a bug.

What the house actually keeps differs sharply by type, so the same formula lands differently:

| | House really keeps | A 50% partner is paid | Partner's share of the real take |
| --- | --- | --- | --- |
| Cosmic Sprint / Space Jackpot, heads-up | ~1% of turnover (2% of the winner's profit above their own stake, `BetDomainService.CommissionForWinner`) | 1% of turnover | ~100% |
| Same, six bets one winner | ~1.67% | 1% | ~60% |
| **Hyper Roll** | **~8.16%** (100 − `RtpPercent` 91.84; `CalculateHouseEdgeReward` takes **no** commission — the edge is in the multipliers) | 1% | **~12%** |

The pooled-game half was already known and accepted (`specs/house-edge-rework.md` §3.3); Hyper Roll was
simply not covered there, because that spec is a day older than Hyper Roll's merge.

⚠️ **Why it is not being changed:** earnings are recomputed **all-time from CURRENT config, never
snapshotted**, so moving the basis re-prices every past bet — a partner already paid under today's rule
can be pushed into a carried deficit. The standing ops rule applies (`referral-cash-out.md`): **do not
re-rate a live partner — issue a new code.** The accepted consequence is that Hyper Roll traffic is
worth less to a partner than it is to the house.

If this is ever revisited, the number that decides it is production Hyper Roll turnover carrying a
referral code — a hand-run query, not something an agent may run.

## Known gaps
- The player filter is a `LIKE '%fragment%'`, so it cannot use an index — bounded by the partner's own
  row count, and only reachable at 30 requests/min.

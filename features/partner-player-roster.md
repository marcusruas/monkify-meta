# Feature — Partner player roster (your players)

> Status: **shipped.** Merged via `feature/partner-players-table` on 2026-09-21 in `monkify-backend`,
> `monkify-front` and `monkify-meta`.
> Card: [#192](https://trello.com/c/5mx2SpXH) (written at ship time — the work was requested directly).
>
> ⚠️ **No spec.** Built from a Claude Design round (three directions) plus an owner reference
> screenshot, so **this file is the only design record** — there is nothing in `specs/` to fall back
> on. It replaces the per-bet **wager table** that shipped three days earlier (`feature/partner-player-bets`,
> 2026-09-21) and was renamed out of `features/partner-wager-table.md`.
>
> Extends [`referral-cash-out.md`](referral-cash-out.md) (#177). Attribution is unchanged
> ([`referral-program.md`](referral-program.md)).

## What it does

A panel on the unlisted `/partner` page — **above** CASH-OUT HISTORY since #199, it used to sit under
it: **one row per player who
arrived through the partner's link**, ranked with the **biggest wager at the top**. A row says who
the player is (their public display name when they chose one, over their truncated wallet), what they
**wagered**, what that **generated in commission** for the partner, and when they were **first** and
**last** seen.

## What it changed, and why

The surface it replaces listed **one row per bet** (placed / player / game / stake / result / share).
The owner rejected that on 2026-09-21 for two reasons:

1. **A partner could see their own bets in it.** A partner bets through their own link like anyone
   else, so their wallet sat among "their players".
2. **It said more than a partner needs.** What a partner checks is which players are worth the
   traffic they bought — not the anatomy of every ticket.

⚠️ **The money was already right.** `IReferralEarningsService.EarningBets` has excluded
`partner.PayoutWallet` — the same wallet that signs in on `/partner` — since #177. A partner has
**never** earned on their own bets; only the table was wrong. **This feature changed no payout path.**
The roster simply applies the same exclusion to what is listed, so the two can never disagree.

What survives of #177's stance, and must not be widened without another decision:

- **Wallets are TRUNCATED server-side** (`7Xf4…9kQa`) before they leave the API. The endpoint never
  returns a full address, so a partner cannot walk away with a list of player addresses.
- **Display names are the PUBLIC ones** — the same `Player.DisplayName` the leaderboard shows. A name
  here exposes nothing the public boards do not.
- The **statement** (`/referrals/me`) is still totals-only, and stays that way: it is cached, and a
  per-player field there would ride that cache.
- Still **no referral code on any player-facing contract**.

## The endpoint

`GET /referrals/me/wagers` — session-gated exactly like the statement (`X-Referral-Session`; a
missing, expired or forged session gets the one vague refusal, `constraints.md` §6). The partner is
resolved **from the session wallet**; no parameter names a partner or a code.

⚠️ **The route kept its name through the switch to per-player rows.** No referral path may contain the
`/bets` substring — `RateLimitSettingsTests` refuses any rate-limit rule matching it, so that the real
bet path can never be throttled — and the pinned rule `get:/referrals/me/wagers` matches this exact
path. Renaming it to `/me/players` would have moved the rule, the Cloud Armor expectations and the
pinned test for cosmetics.

| Query parameter | Meaning |
| --- | --- |
| `pageNumber` / `pageSize` | 1-based; size clamped to **1–50**, never rejected. Past the end returns an empty page. |
| `player` | A wallet fragment, **or the truncated form pasted back in** (`7Xf4…9kQa` → prefix + suffix match). Case-sensitive: base58 is. Over 44 chars matches nothing. |
| `days` | Last N days (capped at 3650). ⚠️ It moves **every figure in the row**, including first/last seen. |
| `sort` | `MostWagered` (default) or `RecentlyActive`. |

The game and result filters of the old table are **gone**: neither survives aggregation, because a
player's bets span many game types and every outcome at once.

### What a row says

- **`wagered`** — Σ of the player's paid stakes attributed to this code inside the window, settled or
  not. Free bets (amount 0) and pre-`SettlementCutoverUtc` bets are excluded, so a player whose only
  bets are those is **not a row at all**.
- **`revenue`** — what those bets earned the PARTNER. ⚠️ **Not `wagered × rate`**: only settled,
  non-refunded bets in rounds that finished paying out earn anything, so a player with an open round
  shows turnover that has not paid yet, and a refunded stake counts as wagered while earning nothing.
- **`firstSeen` / `lastSeen`** — their first and last bet through this link, inside the window.
- **`name`** — null unless the player set a public display name; **`wallet`** is always present, and
  is what the search box matches.

### The ranking is done in the database

Both orders are computed over the **whole filtered set**, then paged — never over the rows one page
happened to fetch, which would put a small player at the top of page 2. The tie-break chain ends on
the player id, so two players on the same figure never swap places between pages. The rank the UI
prints is the row's rank on the whole roster: page 2 starts at 21.

### The revenue column cannot drift from the statement

The rows and the revenue come from **two queries, one predicate**. The page is grouped from the
partner's referred bets; the revenue is then asked of `IReferralEarningsService.EarningBets(partner)`
— THE predicate the headline figures use — restricted to the page's players **and the same window**.

⚠️ A 30-day roster whose revenue column was all-time would not add up, which is why the window is
applied to both. Per-player rounding means the column can sum to a hair **under** "earned all time"
(never over): each row floors at the asset's decimals, the headline floors once on the total.

## Performance — the index

Every roster query leads on `Bet.ReferralCode` and then **groups by `PlayerId`**, summing `Amount`.
The index the wager table introduced carried `INCLUDE (Amount)` only, so the group-by had to heap-fetch
the player id for every one of the partner's rows. Migration **`IncludePlayerIdInReferralCodeIndex`**
widens the payload:

```sql
CREATE INDEX "IX_GameBets_ReferralCode_CreatedDate_Id"
  ON "GameBets" ("ReferralCode", "CreatedDate" DESC, "Id" DESC)
  INCLUDE ("Amount", "PlayerId") WHERE "ReferralCode" IS NOT NULL;
```

- **Replaced in place, not added beside:** `ReferralCode` is still the leading column, so every
  earnings query keeps using it and a referred insert still pays **one** index write.
- With both payload columns present the aggregate is **index-only**; the `Players` table is joined
  once, for the page's handful of rows, to resolve names and wallets.
- ⚠️ **It is a DROP + CREATE on `GameBets`.** Consumers applies it at startup and it takes a write
  lock on the table for the rebuild — bets block for those seconds. On the numbers the wager table
  measured (2M bets), that is a short but real pause on the hot path.
- The one query no index helps is the **wallet search**: `LIKE '%fragment%'` cannot use an index. It
  is bounded by the partner's own row count and reachable at 30 requests/min.

The wager table's own measurements (page 107ms → 0.6ms, count 106ms → 12ms, earnings aggregate
220ms → 172ms on a 200k-bet partner) still hold for the ordering and the earnings path; the roster's
group-by is the query this migration exists for.

## Rate limiting

`get:/referrals/me/wagers` — **30/min per client IP** (`IpRateLimiting`, committed
`spacestrokersettings.json`), unchanged by the rework.

⚠️ **GET only, not `*:`.** The browser sends an OPTIONS preflight before every read (the session rides
in a custom header); counting those would halve the budget for nothing. Counters are **per API
instance** (in-memory store), so N instances mean N × 30; Cloud Armor's per-IP throttle (600/min,
`deploy/terraform/workload/modules/edge`) is the blunt backstop.

⚠️ The other referral routes (`/referrals/session`, `/referrals/me`, `/referrals/cash-outs`) still
have **no** app-side rule — a deliberate gap, not an oversight to "fix" without a decision.

## Frontend

- `src/components/partner/PartnerPlayersTable.vue` — the section: filter bar (wallet box, period,
  sort, CLEAR), one **card per player** with labelled cells, numbered pages with an ellipsis and a
  10/20/50 page-size picker. The top row of page 1 wears the amber border; commission is amber (the
  colour the statement's figures wear), turnover is ink.
  - **One template for both layouts:** six columns above **1024px**, and the same six children flowing
    into two columns below it. There is no table header to keep aligned, because every figure carries
    its own label.
  - ⚠️ **The breakpoint is the VIEWPORT's, so it is only right for the page width it sits in.** The
    fixed columns and gaps cost 622px. It shipped at 900px inside a 768px frame, which left the player
    column ~98px: **names were cut to four letters ("moon…") and ran into WAGERED on every desktop**.
    #199 widened the page to 1180px and moved the breakpoint to 1024px, where the panel's content box
    is ~876px and the player gets ~250px. Re-do that sum if the page width ever changes.
  - It is its own `HomeSection` panel since #199, with the filtered player count beside the subtitle
    (the same number the footer's "of N" prints).
  - ⚠️ Figures go through `utils/money.formatSol`, **not** a local formatter. The old table carried
    its own 9-decimal one — exactly the drift that module exists to stop — and `formatSol`'s
    significant-digit escape is what keeps a 0.0003 SOL commission from rendering as `0.00`.
  - The line **"Your own bets are never counted or listed here."** sits under the list on purpose: a
    partner who bets through their own link *will* look for themselves.
- `src/hooks/usePartnerPlayers.ts` — filters, sort and paging. **Every filter change resets to page
  1**, the wallet box is debounced 350 ms, and **only the latest response may write** so a slow page
  cannot flash stale rows. ⚠️ The period defaults to **all time**, unlike the 30 days the per-bet
  table defaulted to: a row carries first/last seen, and a default window would silently cut both.
- `usePartnerEarnings` still exposes its `sessionToken` **read-only** — the roster borrows the session
  and never mints or clears one.

## Key code touchpoints

**Backend (`monkify-backend`)**

- `SpaceStroker.Infrastructure/Handlers/Referrals/GetReferralPlayers/` — handler + request/DTOs
  (replaces `GetReferralWagers/`, whose `ReferralWagerResult`, `Classify` and cached game-type options
  were deleted with the per-bet view)
- `SpaceStroker.Infrastructure/Services/Referrals/ReferralEarningsService.cs` — `EarningBets`,
  `ApplyShare`, `ResolveSettlementCutover` (unchanged; the roster is a second caller)
- `SpaceStroker.Api/Controllers/ReferralsController.cs` — `GET /referrals/me/wagers`
- `SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` — the index payload
- `SpaceStroker.Infrastructure/Migrations/…_IncludePlayerIdInReferralCodeIndex.cs`
- `SpaceStroker.Common/spacestrokersettings.json` — the rate-limit rule (untouched)

**Frontend (`monkify-front`)**

- `src/components/partner/PartnerPlayersTable.vue`, `src/hooks/usePartnerPlayers.ts`,
  `src/services/partnerService.ts`, `src/types/partner.ts`, `src/pages/PartnerPage.vue`

## Tests

- `SpaceStroker.Tests.Integration/ReferralPlayersFlowTests.cs` — the happy path (referred players bet
  through the real API across **two** settled rounds, then the partner reads back one row each with
  the right turnover and commission), the session-less refusal, and the **live rate limit** (exactly
  30 allowed, then 429, while another client is still served). ⚠️ Its third round-two bettor is the
  **partner's own wallet**, and the test asserts that row is neither listed nor counted — that is the
  regression this feature exists to prevent.
  ⚠️ The suite shares one database and other tests place referred bets under the same code, so the
  roster is never asserted as a closed set: known players are asserted through the search filter, and
  the ranking is asserted as an invariant over the page (every figure ≤ the one above it).
- `SpaceStroker.Tests/UnitTests/Handlers/Referrals/GetReferralPlayersHandlerTests.cs` — 14 cases,
  including the partner's own wallet, wagered-vs-revenue on an unsettled and a refunded bet, the
  window moving every figure, and the search by fragment and by pasted truncated form.
- `SpaceStroker.Tests/UnitTests/Api/RateLimitSettingsTests.cs` — the rule is in the pinned exact set.

## The commission basis is uniform, on purpose (owner, 2026-09-21)

⚠️ **Every game type pays a partner the same `turnover × 2% × SharePercentage`, including Hyper Roll** —
raised when the wager table shipped, and **decided: keep it.** Do not "fix" it as a bug.

What the house actually keeps differs sharply by type, so the same formula lands differently:

| | House really keeps | A 50% partner is paid | Partner's share of the real take |
| --- | --- | --- | --- |
| Cosmic Sprint / Space Jackpot, heads-up | ~1% of turnover (2% of the winner's profit above their own stake, `BetDomainService.CommissionForWinner`) | 1% of turnover | ~100% |
| Same, six bets one winner | ~1.67% | 1% | ~60% |
| **Hyper Roll** | **~8.16–8.86%** (100 − the board mode's RTP: 91.84 HYPER, 91.50 AVERAGE, 91.14 SUPER; `CalculateHouseEdgeReward` takes **no** commission — the edge is in the multipliers) | 1% | **~12%** |

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

- **No sort by revenue.** Revenue is computed for the page's players only, so ordering by it would
  rank a page against itself. `MostWagered` is the near-enough proxy the table was asked for.
- **The wallet search is a `LIKE '%fragment%'`** and cannot use an index (bounded by the partner's own
  row count, and only reachable at 30 requests/min).
- **No drill-down.** A row does not expand into the player's bets; the per-bet view was deliberately
  removed, not hidden.

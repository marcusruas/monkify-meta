-- =====================================================================================
-- REFERRAL PAYOUT REPORT — what each partner is owed for a period (Trello #175)
--
-- READ-ONLY. Run by hand through `spacestroker-db --db game --env production`.
-- See monkify-meta/specs/referral-program.md §8 for the design.
--
-- ⚠️ THE BASIS IS THE HOUSE EDGE, NOT TURNOVER (house-edge-rework). A partner earns
--    SharePercentage of the COMMISSION on their referred players' commission base:
--        commission_base = stake of every LOST bet + profit above own stake of every WON bet
--        owed            = commission_base × CommissionPercentage/100 × SharePercentage/100
--    counting only rounds where at least one PAID winner was paid (no winner ⇒ no commission was
--    charged). It used to be SharePercentage × everything wagered, which paid a 50% partner ~20x
--    what the house earned from their traffic. Mirrors ReferralEarningsService.ResolveCommissionBase
--    — keep them in lockstep.
--
-- ⚠️ BOTH SIDES OF A ROUND COUNT (owner, 2026-09-17). When two referred players meet, the one
--    commission is credited from the loss AND the win, so owed_sol can exceed share × the commission
--    the house actually took on those rounds.
--
-- ⚠️ THE RATE IS READ FROM CURRENT CONFIG, NOT SNAPSHOTTED. The app re-prices a partner's whole
--    history at today's CommissionPercentage, so params.commission_percentage must be TODAY's
--    deployed rate — not the rate in force during the reporting window — or this report and the
--    /partner page disagree.
--
-- ⚠️ SINCE #177 THIS IS A RECONCILIATION TOOL, NOT THE PAYMENT MECHANISM. Partners are paid
--    automatically from the /partner page, out of a dedicated ReferralPayout account, and every
--    payment is a row in "ReferralCashOuts". Section 3 below is the one that reconciles against
--    what the page shows; sections 1 and 2 still answer the WINDOWED question ("what did this
--    traffic wager in March"), which the page does not.
--
-- ⚠️ NEVER PAY FROM SECTION 1 ANY MORE. It totals earnings and knows nothing about what has already
--    been sent, so paying from it after the first cash-out pays a partner twice.
--
-- ⚠️ THIS FILE HAND-MIRRORS THE SCHEMA AND WILL ROT SILENTLY. Table names are EF defaults
--    from the DbSet property names ("GameBets", "Games", "Players") and the enums are stored
--    as ints. Re-check it whenever Bet, Player, GameStatus or BetStatus changes — the deploy
--    tools' seeder once broke for a week with every backend test green.
--
-- Partners are read from the "ReferralPartners" table (#177 moved them out of config). Inactive
-- partners are INCLUDED: Active gates attribution only, and a deactivated partner is still owed.
-- =====================================================================================

WITH params AS (
    -- EDIT ME: the reporting window, half-open [from, to).
    SELECT
        TIMESTAMP '2026-08-01 00:00:00' AS from_utc,
        TIMESTAMP '2026-09-01 00:00:00' AS to_utc,
        -- EDIT ME: GeneralSettings:Operator:Token:SettlementCutoverUtc from the deployed config.
        -- Bets before this are denominated in the PREVIOUS settlement asset; summing them would
        -- report a number in no currency at all. Use '-infinity' only on a deployment that never
        -- switched.
        TIMESTAMP '2026-08-20 00:00:00' AS settlement_cutover_utc,
        -- EDIT ME: GeneralSettings:Operator:Token:CommissionPercentage from the deployed config
        -- (a PERCENT: 2.0 means 2%). Today's rate — see the header.
        2.0::numeric AS commission_percentage
),

referrers (code, share_percentage, payout_wallet) AS (
    SELECT "Code", "SharePercentage", "PayoutWallet"
    FROM "ReferralPartners"
),

qualifying_bets AS (
    SELECT
        r.code,
        r.share_percentage,
        p."ExternalPlayerId" AS wallet,
        p."ReferredAt"       AS first_seen,
        -- LOST: the whole stake. WON: Σ round stakes / paid winners − own stake (the profit
        -- BetDomainService.CommissionForWinner taxes), floored at zero. House-funded lottery money is
        -- never commissioned, so it is deliberately absent from the pot here.
        CASE
            WHEN b."Status" = 0 THEN b."Amount"
            ELSE GREATEST(0,
                (SELECT SUM(a."Amount") FROM "GameBets" a WHERE a."GameId" = b."GameId")
                / (SELECT COUNT(*) FROM "GameBets" w
                   WHERE w."GameId" = b."GameId" AND w."Status" = 2 AND w."Amount" > 0)
                - b."Amount")
        END                  AS commission_base,
        params.commission_percentage
    FROM "GameBets" b
        JOIN "Games"   g ON g."Id" = b."GameId"
        JOIN "Players" p ON p."Id" = b."PlayerId"
        JOIN referrers r ON lower(r.code) = lower(b."ReferralCode")
        CROSS JOIN params
    WHERE b."ReferralCode" IS NOT NULL
      -- Free bets contribute nothing and receive nothing (features/money.md).
      AND b."Amount" > 0
      -- Only rounds that actually paid out. Refunded / under-subscribed (NotEnoughPlayersToStart),
      -- ErrorWhenProcessingRewards and GameEndedAbruptely are excluded — the same rule every stats
      -- read applies (features/stats-and-history.md).
      AND g."Status" = 7                      -- GameStatus.RewardForWinnersCompleted
      -- LOST (0 = Made, a losing bet is never re-statused) or WON (2 = Rewarded). This drops every
      -- refunded or parked stake (3/4/5). BetStatus is ZERO-based.
      AND b."Status" IN (0, 2)
      -- ...in a round where commission was actually CHARGED: at least one PAID winner was paid.
      AND EXISTS (
          SELECT 1 FROM "GameBets" w
          WHERE w."GameId" = b."GameId"
            AND w."Status" = 2                -- BetStatus.Rewarded
            AND w."Amount" > 0
      )
      AND b."CreatedDate" >= params.from_utc
      AND b."CreatedDate" <  params.to_utc
      AND b."CreatedDate" >= params.settlement_cutover_utc
      -- The one hard abuse guard: a partner never earns on bets from their own payout wallet.
      AND p."ExternalPlayerId" IS DISTINCT FROM r.payout_wallet
)

-- ── 1. TOTALS PER PARTNER ────────────────────────────────────────────────────────────
SELECT
    code                                         AS referrer,
    COUNT(DISTINCT wallet)                       AS referred_wallets,
    COUNT(*)                                     AS bets,
    ROUND(SUM(commission_base), 9)               AS commission_base_sol,
    ROUND(SUM(commission_base) * commission_percentage / 100, 9) AS house_edge_sol,
    share_percentage                             AS share_pct,
    -- Floored like the app (MidpointRounding.ToZero), never rounded to nearest.
    TRUNC(SUM(commission_base) * commission_percentage / 100 * share_percentage / 100, 9) AS owed_sol
FROM qualifying_bets
GROUP BY code, share_percentage, commission_percentage
ORDER BY owed_sol DESC;

-- ── 2. PER-WALLET BREAKDOWN ──────────────────────────────────────────────────────────
-- Run this second (same window). Concentration is what catches self-referral: a partner whose
-- volume is three wallets is not sending traffic. A cluster of wallets first seen on one day with
-- no stream behind it is the tell for code-stuffing. Neither is decided here — a human reads it.
--
-- ⚠️ Re-paste the CTE block above before this query; the REPL runs one statement at a time.
--
-- SELECT
--     code                          AS referrer,
--     wallet,
--     first_seen,
--     COUNT(*)                      AS bets,
--     ROUND(SUM(commission_base), 9) AS commission_base_sol,
--     ROUND(100 * SUM(commission_base) / NULLIF(SUM(SUM(commission_base)) OVER (PARTITION BY code), 0), 2) AS pct_of_referrer
-- FROM qualifying_bets
-- GROUP BY code, wallet, first_seen
-- ORDER BY code, commission_base_sol DESC;


-- ── 3. RECONCILIATION vs the /partner page (#177) ────────────────────────────────────
-- The number a partner actually sees, and the one the Cash out button pays. ALL-TIME on both
-- halves — the page has no reporting window — so it deliberately IGNORES params.from_utc/to_utc.
--
-- ⚠️ Re-paste the CTE block above before this query; the REPL runs one statement at a time. Then
--    edit qualifying_bets' date filter to drop the window, or this reads windowed earnings
--    against all-time payments and understates what is owed.
--
-- ⚠️ SUM(Amount) HAS NO STATUS FILTER, and must not grow one. A Failed withdrawal keeps its negative
--    amount and is cancelled only by an APPENDED positive Reversal row. Filtering Failed rows out
--    would double-count every transfer that timed out but confirmed on-chain — the same invariant
--    OrbitTransaction holds, and the reason the app never edits a row to undo a payment.
--
-- ⚠️ Amount is SIGNED: withdrawals are negative, reversals positive. So payable is earnings PLUS the
--    sum, never minus it.
--
-- A NEGATIVE payable is legitimate, not corruption: an admin refund can retroactively remove a
-- settled loss, and editing a partner's SharePercentage OR the CommissionPercentage re-prices their
-- whole history. The app carries that deficit rather than clamping it, so new earnings work it off.
--
-- SELECT
--     r.code                                                   AS referrer,
--     ROUND(COALESCE(e.earned, 0), 9)                          AS earned_all_time_sol,
--     ROUND(-COALESCE(c.settled, 0), 9)                        AS paid_out_sol,
--     ROUND(COALESCE(e.earned, 0) + COALESCE(c.settled, 0), 9)  AS payable_sol
-- FROM referrers r
--     LEFT JOIN (
--         SELECT code, TRUNC(SUM(commission_base) * commission_percentage / 100 * share_percentage / 100, 9) AS earned
--         FROM qualifying_bets
--         GROUP BY code, share_percentage, commission_percentage
--     ) e ON e.code = r.code
--     LEFT JOIN (
--         SELECT lower("ReferralCode") AS code, SUM("Amount") AS settled
--         FROM "ReferralCashOuts"
--         GROUP BY lower("ReferralCode")
--     ) c ON c.code = lower(r.code)
-- ORDER BY payable_sol DESC;


-- ── 4. CASH-OUTS NEEDING A HUMAN ─────────────────────────────────────────────────────
-- ⚠️ THE ONE THAT MATTERS OPERATIONALLY. Status 1 = Executing means the consumer signed something
--    and never learned the outcome — the send threw, or the confirmation timed out. It is
--    deliberately NOT auto-reversed, because a timed-out transfer can still confirm on chain, and
--    reversing it would credit the partner for money that also left the account.
--
--    A row sitting here needs a person to check the payout account on chain and then either mark it
--    Paid with its signature, or append a Reversal. Nothing else will move it.
--
-- SELECT "Id", "ReferralCode", "Amount", "RequestedAt", "Reason"
-- FROM "ReferralCashOuts"
-- WHERE "Type" = 0 AND "Status" = 1
-- ORDER BY "RequestedAt";

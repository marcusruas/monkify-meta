-- =====================================================================================
-- REFERRAL PAYOUT REPORT — what each partner is owed for a period (Trello #175)
--
-- READ-ONLY. Run by hand through `spacestroker-db --db game --env production`.
-- See monkify-meta/specs/referral-program.md §8 for the design.
--
-- ⚠️ TURNOVER IS NOT PROFIT. This totals what referred players WAGERED. The house does not
--    keep that; it keeps a commission on winnings, which at a 2% rate is 2% of the LOSING
--    stakes in a round — heads-up on a MinimumNumberOfPlayers = 2 type that is 1% OF
--    TURNOVER, rising to ~1.67% at six bets with one winner. A SharePercentage above ~1%
--    therefore pays a partner MORE than their traffic earned us. Nothing in code enforces
--    this; it is the reason the bound is written down here and in ReferralSettings.
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
-- ⚠️ THE PARTNER TABLE BELOW IS NOT IN THE DATABASE. Referrers live in config
--    (GeneralSettings:Operator:Referrals). Copy Code / SharePercentage / PayoutWallet out of
--    the deployed spacestrokersettings.json into the VALUES list, or the report will simply
--    return nothing.
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
        TIMESTAMP '2026-08-20 00:00:00' AS settlement_cutover_utc
),

-- EDIT ME: one row per configured partner, copied from the deployed config.
referrers (code, share_percentage, payout_wallet) AS (
    VALUES
        ('mock-partner', 1.0::numeric, 'MOCK_REFERRAL_PAYOUT_WALLET')
        -- , ('another-partner', 0.75::numeric, '<their wallet>')
),

qualifying_bets AS (
    SELECT
        r.code,
        r.share_percentage,
        p."ExternalPlayerId" AS wallet,
        p."ReferredAt"       AS first_seen,
        b."Amount"           AS amount
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
      -- A refunded stake came straight back to the player, so it earned nothing. 3/4/5 =
      -- NeedsRefunding / Refunded / NeedsManualAnalysis (BetStatus is zero-based: Made = 0).
      AND b."Status" NOT IN (3, 4, 5)
      AND b."CreatedDate" >= params.from_utc
      AND b."CreatedDate" <  params.to_utc
      AND b."CreatedDate" >= params.settlement_cutover_utc
      -- The one hard abuse guard: a partner never earns on bets from their own payout wallet.
      AND p."ExternalPlayerId" <> r.payout_wallet
)

-- ── 1. TOTALS PER PARTNER ────────────────────────────────────────────────────────────
SELECT
    code                                         AS referrer,
    COUNT(DISTINCT wallet)                       AS referred_wallets,
    COUNT(*)                                     AS bets,
    ROUND(SUM(amount), 9)                        AS turnover_sol,
    share_percentage                             AS share_pct,
    ROUND(SUM(amount) * share_percentage / 100, 9) AS owed_sol
FROM qualifying_bets
GROUP BY code, share_percentage
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
--     ROUND(SUM(amount), 9)         AS turnover_sol,
--     ROUND(100 * SUM(amount) / SUM(SUM(amount)) OVER (PARTITION BY code), 2) AS pct_of_referrer
-- FROM qualifying_bets
-- GROUP BY code, wallet, first_seen
-- ORDER BY code, turnover_sol DESC;


-- ── 3. RECONCILIATION vs the /partner page (#177) ────────────────────────────────────
-- The number a partner actually sees, and the one the Cash out button pays. ALL-TIME on both
-- halves — the page has no reporting window — so it deliberately IGNORES params.from_utc/to_utc.
--
-- ⚠️ Re-paste the CTE block above before this query; the REPL runs one statement at a time. Then
--    edit qualifying_bets' date filter to drop the window, or this reads a windowed turnover
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
-- settled bet from turnover, and editing a partner's SharePercentage re-prices their whole history.
-- The app carries that deficit rather than clamping it, so new turnover works it off.
--
-- SELECT
--     r.code                                                   AS referrer,
--     ROUND(COALESCE(e.earned, 0), 9)                          AS earned_all_time_sol,
--     ROUND(-COALESCE(c.settled, 0), 9)                        AS paid_out_sol,
--     ROUND(COALESCE(e.earned, 0) + COALESCE(c.settled, 0), 9)  AS payable_sol
-- FROM referrers r
--     LEFT JOIN (
--         SELECT code, SUM(amount) * share_percentage / 100 AS earned
--         FROM qualifying_bets
--         GROUP BY code, share_percentage
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

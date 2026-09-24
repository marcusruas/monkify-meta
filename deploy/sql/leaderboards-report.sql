-- =====================================================================================
-- LEADERBOARDS REPORT — the three public boards, straight from the database
--
-- READ-ONLY. Run by hand through `spacestroker-db --db game --env production` or Cloud SQL Studio.
-- Mirrors LeaderboardService (monkify-backend) — see specs/leaderboards.md and
-- features/leaderboards.md. Three independent queries, one per board; run the one you need.
--
-- ⚠️ THE HOUSE WALLETS ARE EXCLUDED BY ADDRESS, exactly as HouseWallets.Resolve does in the app:
--    treasury, Orbit custody, the HOUSE SEED BOT and the referral payout account. The house seed
--    places one paid bet in every round on every type, so without this filter it owns the top of the
--    gamble race. The values below are PRODUCTION's (deploy/secrets.production.json). ⚠️ If any of
--    those keys is rotated, update every params block here too — a stale address silently puts the
--    bot back on the board.
--
-- ⚠️ SettlementCutoverUtc floors EVERY board (Hall of Fame included) — summing across it adds
--    USDC-era figures to SOL-era ones. The value below is production's.
--
-- ⚠️ ANCHORED MODE ONLY. The weekly boards rank the grid slot { anchor + k*7d } containing now
--    (WeeklyResetAnchorUtc, Friday 12:00 BRT). The app's unanchored rolling-7-day fallback is NOT
--    reproduced — production is anchored.
--
-- ⚠️ THE APP CACHES ITS RESULT, so the site can briefly lag what this returns.
--
-- ⚠️ THIS FILE HAND-MIRRORS THE SCHEMA AND WILL ROT SILENTLY. Enums are ints:
--    "Games"."Status" = 7 is GameStatus.RewardForWinnersCompleted (1-based);
--    "TransactionLogs"."Type" = 0 is TransactionLogType.Credit.
-- =====================================================================================


-- -------------------------------------------------------------------------------------
-- 1. GAMBLE RACE — Σ paid stake this week
-- -------------------------------------------------------------------------------------
WITH params AS (
    SELECT
        TIMESTAMPTZ '2026-09-04 15:00:00+00' AS anchor,       -- WeeklyResetAnchorUtc
        TIMESTAMPTZ '2026-08-21 13:37:00+00' AS cutover,      -- SettlementCutoverUtc
        ARRAY[
            'CjAULxZV5FSp2L5qQeLynv9rEbULT7ev5e81fYpFs5L2',   -- treasury (TokenOwnerPublicKey)
            '3GBLYNVhVqmCZ6pUi3DrpcXLXEveVySbzKJUF2bW9dBg',   -- Orbit custody
            '5tPdymQM9vKX8xsYr7cfAffuBQjQftDp5wv4u5NcNJid',   -- house seed bot
            'HfDuURvvqX3xkcXogbsLSzJvwTJ5V6sD7BBS83BjPrh8'    -- referral payout
        ]::text[] AS house_wallets,
        NULL::uuid AS type_filter,                            -- one GameParameters.Id, or NULL = all active
        10 AS size                                            -- the API default; max 25
),
win AS (
    SELECT GREATEST(p.anchor + floor(extract(epoch FROM now() - p.anchor) / 604800) * INTERVAL '7 days',
                    p.cutover) AS from_ts,
           now() AS to_ts
    FROM params p
),
types AS (
    SELECT gp."Id" FROM "GameParameters" gp, params p
    WHERE gp."Active" AND (p.type_filter IS NULL OR gp."Id" = p.type_filter)
),
rounds AS (
    SELECT g."Id" FROM "Games" g, win w
    WHERE g."ParametersId" IN (SELECT "Id" FROM types)
      AND g."Status" = 7
      AND (g."EndDate" >= w.from_ts OR (g."EndDate" IS NULL AND g."CreatedDate" >= w.from_ts))
      AND (g."EndDate" <= w.to_ts   OR (g."EndDate" IS NULL AND g."CreatedDate" <= w.to_ts))
),
eligible AS (
    SELECT pl.* FROM "Players" pl, params p
    WHERE NOT (pl."ExternalPlayerId" = ANY (p.house_wallets))
      AND (pl."SelfExcludedAt" IS NULL
           OR (pl."SelfExcludedUntil" IS NOT NULL AND pl."SelfExcludedUntil" <= now()))
)
SELECT ROW_NUMBER() OVER (ORDER BY SUM(b."Amount") DESC, MIN(b."CreatedDate")) AS rank,
       pl."Id", pl."DisplayName", pl."ExternalPlayerId" AS wallet,
       SUM(b."Amount")      AS sol_gambled,
       MIN(b."CreatedDate") AS first_qualifying_at
FROM "GameBets" b
JOIN rounds   r  ON r."Id"  = b."GameId"
JOIN eligible pl ON pl."Id" = b."PlayerId"
WHERE b."Amount" > 0
GROUP BY pl."Id", pl."DisplayName", pl."ExternalPlayerId"
ORDER BY sol_gambled DESC, first_qualifying_at
LIMIT (SELECT size FROM params);


-- -------------------------------------------------------------------------------------
-- 2. WEEKLY PNL — Σ credits − Σ paid stakes this week (never clamped; a losing week ranks negative)
-- -------------------------------------------------------------------------------------
WITH params AS (
    SELECT
        TIMESTAMPTZ '2026-09-04 15:00:00+00' AS anchor,
        TIMESTAMPTZ '2026-08-21 13:37:00+00' AS cutover,
        ARRAY[
            'CjAULxZV5FSp2L5qQeLynv9rEbULT7ev5e81fYpFs5L2',   -- treasury
            '3GBLYNVhVqmCZ6pUi3DrpcXLXEveVySbzKJUF2bW9dBg',   -- Orbit custody
            '5tPdymQM9vKX8xsYr7cfAffuBQjQftDp5wv4u5NcNJid',   -- house seed bot
            'HfDuURvvqX3xkcXogbsLSzJvwTJ5V6sD7BBS83BjPrh8'    -- referral payout
        ]::text[] AS house_wallets,
        NULL::uuid AS type_filter,
        10 AS size
),
win AS (
    SELECT GREATEST(p.anchor + floor(extract(epoch FROM now() - p.anchor) / 604800) * INTERVAL '7 days',
                    p.cutover) AS from_ts,
           now() AS to_ts
    FROM params p
),
types AS (
    SELECT gp."Id" FROM "GameParameters" gp, params p
    WHERE gp."Active" AND (p.type_filter IS NULL OR gp."Id" = p.type_filter)
),
rounds AS (
    SELECT g."Id" FROM "Games" g, win w
    WHERE g."ParametersId" IN (SELECT "Id" FROM types)
      AND g."Status" = 7
      AND (g."EndDate" >= w.from_ts OR (g."EndDate" IS NULL AND g."CreatedDate" >= w.from_ts))
      AND (g."EndDate" <= w.to_ts   OR (g."EndDate" IS NULL AND g."CreatedDate" <= w.to_ts))
),
eligible AS (
    SELECT pl.* FROM "Players" pl, params p
    WHERE NOT (pl."ExternalPlayerId" = ANY (p.house_wallets))
      AND (pl."SelfExcludedAt" IS NULL
           OR (pl."SelfExcludedUntil" IS NOT NULL AND pl."SelfExcludedUntil" <= now()))
),
per_bet AS (
    SELECT b."PlayerId", b."Amount", b."CreatedDate",
           COALESCE((SELECT SUM(t."Amount") FROM "TransactionLogs" t
                     WHERE t."Type" = 0 AND t."BetId" = b."Id"), 0) AS won
    FROM "GameBets" b
    JOIN rounds r ON r."Id" = b."GameId"
    WHERE b."Amount" > 0 AND b."PlayerId" IS NOT NULL
)
SELECT ROW_NUMBER() OVER (ORDER BY SUM(x.won) - SUM(x."Amount") DESC, MIN(x."CreatedDate")) AS rank,
       pl."Id", pl."DisplayName", pl."ExternalPlayerId" AS wallet,
       SUM(x.won)                   AS sol_in,
       SUM(x."Amount")              AS sol_staked,
       SUM(x.won) - SUM(x."Amount") AS net_pnl,
       MIN(x."CreatedDate")         AS first_qualifying_at
FROM per_bet x
JOIN eligible pl ON pl."Id" = x."PlayerId"
GROUP BY pl."Id", pl."DisplayName", pl."ExternalPlayerId"
ORDER BY net_pnl DESC, first_qualifying_at
LIMIT (SELECT size FROM params);


-- -------------------------------------------------------------------------------------
-- 3. HALL OF FAME — best credit / own stake, ALL TIME (no weekly window, still cutover-floored).
--    Ranked on the MULTIPLIER, never the amount. Frontend-flagged off (VUE_APP_HALL_OF_FAME_ENABLED).
-- -------------------------------------------------------------------------------------
WITH params AS (
    SELECT
        TIMESTAMPTZ '2026-08-21 13:37:00+00' AS cutover,
        ARRAY[
            'CjAULxZV5FSp2L5qQeLynv9rEbULT7ev5e81fYpFs5L2',   -- treasury
            '3GBLYNVhVqmCZ6pUi3DrpcXLXEveVySbzKJUF2bW9dBg',   -- Orbit custody
            '5tPdymQM9vKX8xsYr7cfAffuBQjQftDp5wv4u5NcNJid',   -- house seed bot
            'HfDuURvvqX3xkcXogbsLSzJvwTJ5V6sD7BBS83BjPrh8'    -- referral payout
        ]::text[] AS house_wallets,
        NULL::uuid AS type_filter,
        10 AS size
),
types AS (
    SELECT gp."Id" FROM "GameParameters" gp, params p
    WHERE gp."Active" AND (p.type_filter IS NULL OR gp."Id" = p.type_filter)
),
eligible AS (
    SELECT pl.* FROM "Players" pl, params p
    WHERE NOT (pl."ExternalPlayerId" = ANY (p.house_wallets))
      AND (pl."SelfExcludedAt" IS NULL
           OR (pl."SelfExcludedUntil" IS NOT NULL AND pl."SelfExcludedUntil" <= now()))
),
wins AS (
    SELECT b."PlayerId", g."ParametersId",
           COALESCE(g."EndDate", g."CreatedDate") AS at,
           w.won,
           w.won / b."Amount" AS multiplier
    FROM "GameBets" b
    JOIN "Games" g ON g."Id" = b."GameId"
    JOIN eligible pl ON pl."Id" = b."PlayerId"
    CROSS JOIN params p
    CROSS JOIN LATERAL (SELECT COALESCE(SUM(t."Amount"), 0) AS won FROM "TransactionLogs" t
                        WHERE t."Type" = 0 AND t."BetId" = b."Id") w
    WHERE b."Amount" > 0
      AND g."ParametersId" IN (SELECT "Id" FROM types)
      AND g."Status" = 7
      AND (p.cutover IS NULL
           OR g."EndDate" >= p.cutover OR (g."EndDate" IS NULL AND g."CreatedDate" >= p.cutover))
      AND w.won > 0
),
best AS (
    SELECT DISTINCT ON (x."PlayerId") x.*
    FROM wins x
    ORDER BY x."PlayerId", x.multiplier DESC, x.at
),
first_at AS (SELECT "PlayerId", MIN(at) AS first_qualifying_at FROM wins GROUP BY "PlayerId")
SELECT ROW_NUMBER() OVER (ORDER BY b.multiplier DESC, f.first_qualifying_at) AS rank,
       pl."Id", pl."DisplayName", pl."ExternalPlayerId" AS wallet,
       b.multiplier, b.won AS best_win_sol, b.at AS achieved_at, gp."Name" AS game_type,
       f.first_qualifying_at
FROM best b
JOIN first_at f          ON f."PlayerId" = b."PlayerId"
JOIN "Players" pl        ON pl."Id" = b."PlayerId"
JOIN "GameParameters" gp ON gp."Id" = b."ParametersId"
ORDER BY b.multiplier DESC, f.first_qualifying_at
LIMIT (SELECT size FROM params);

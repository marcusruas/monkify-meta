-- =====================================================================================
-- BEAST MODE DEACTIVATION — #183 Hyper Roll replaces it
--
-- Run ONCE, by hand, in Cloud SQL Studio against the `spacestroker` database.
-- `spacestroker-prod-pg` is PRIVATE-IP ONLY: cloud-sql-proxy fails with `instance does not
-- have IP of type "PUBLIC"`, so the console's SQL Studio is the route in.
--
-- ⚠️ WHY THIS IS SQL AND NOT CONFIG. Game types are seeded CREATE-ONLY BY NAME and no startup
--    self-heal ever writes `Active` (that is what stops a restart resurrecting a deactivated type).
--    Removing Beast Mode from GeneralSettings:Operator:DefaultGames only stops a FRESH database
--    seeding it; an existing database needs this script.
--
-- ⚠️ ORDER: run this BEFORE the Consumers deploy that ships Hyper Roll. That boot drops the 12h
--    `ActiveParameters` Redis blob unconditionally; run it after instead and Beast Mode stays in the
--    lobby until Consumers restarts again.
--
-- ⚠️ STATUSES ARE 1-BASED: WaitingBets = 1, GameStarting = 3, InProgress = 4. Beast Mode has been
--    ComingSoon since 2026-08-13, so no round should be open — the guard refuses (0 rows) if one is.
--
-- History, stats and provably-fair verification of Beast Mode's settled rounds are untouched.
-- =====================================================================================

-- ---------------------------------------------------------------------------------
-- PRE-CHECK (read-only, run on its own first): the row and any open round.
-- ---------------------------------------------------------------------------------
SELECT p."Id", p."Name", p."Active", p."ComingSoon", g."Id" AS game_id, g."Status"
FROM "GameParameters" p
LEFT JOIN "Games" g ON g."ParametersId" = p."Id" AND g."Status" IN (1, 3, 4)
WHERE p."Name" = 'Beast Mode';

-- ---------------------------------------------------------------------------------
-- DEACTIVATE. Expect "UPDATE 1". "UPDATE 0" means it is already inactive or a round is open.
-- ---------------------------------------------------------------------------------
UPDATE "GameParameters" p
SET "Active" = false, "UpdatedDate" = now()
WHERE p."Name" = 'Beast Mode'
  AND p."Active" = true
  AND NOT EXISTS (
      SELECT 1 FROM "Games" g
      WHERE g."ParametersId" = p."Id" AND g."Status" IN (1, 3, 4)
  );

-- ---------------------------------------------------------------------------------
-- VERIFY (after the Consumers deploy): GET https://api.spacestroker.com/games/active-types
-- lists "Hyper Roll" with "isHouseEdge": true and does NOT list "Beast Mode".
-- ---------------------------------------------------------------------------------

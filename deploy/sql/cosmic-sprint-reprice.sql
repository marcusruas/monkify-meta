-- =====================================================================================
-- COSMIC SPRINT RE-PRICE — 0.02 SOL -> 0.01 SOL
--
-- Run ONCE, by hand, in Cloud SQL Studio against the `spacestroker` database.
-- `spacestroker-prod-pg` is PRIVATE-IP ONLY: cloud-sql-proxy fails with `instance does not
-- have IP of type "PUBLIC"`, so the console's SQL Studio is the route in.
--
-- ⚠️ WHY THIS IS SQL AND NOT CONFIG. Game types are seeded CREATE-ONLY BY NAME and there is
--    deliberately NO self-heal for RequiredAmount (specs/sol-settlement.md §3 decision 6).
--    Editing GeneralSettings:Operator:DefaultGames reaches a FRESH database only; an existing
--    one needs this script. The config edit still ships, so the two agree.
--
-- ⚠️ THE CACHE IS HALF THE JOB — SEE THE FOOTER. Committing this alone is what produced the
--    "stale price" failure: the browser keeps quoting the OLD price from the 12h Redis
--    `ActiveParameters` blob while this row already holds the new one, and every UI bet is then
--    refused as InvalidAmount AFTER its SOL moved on-chain. Bounce Consumers immediately after.
--
-- ⚠️ THERE IS NO OPEN-ROUND GUARD HERE, AND THAT IS DELIBERATE. sol-settlement-cutover.sql
--    refuses to run while a round is open; that guard can NEVER pass on a live product, because
--    OpenNewGames opens a fresh round the moment one ends (it skipped twice during the 2026-08-21
--    cutover for exactly this reason). It is also unnecessary: the amount check runs ONLY at bet
--    registration (BetDomainService.ChoiceIsValidForGame, called from RegisterBetHandler), so bets
--    ALREADY in the open round are never re-validated and settle normally at what they paid.
--    Run the pre-check below instead, and decide with your eyes open.
-- =====================================================================================

-- ---------------------------------------------------------------------------------
-- PRE-CHECK (read-only, run on its own first). Shows the current price and what is
-- riding on the open round. A bet here from a wallet that is NOT the house-seed wallet
-- is a real player who may re-bet during the swap and get refused + auto-refunded.
-- ---------------------------------------------------------------------------------
SELECT p."Name", p."RequiredAmount", g."Id" AS game_id, g."Status",
       b."Id" AS bet_id, b."Amount", pl."ExternalPlayerId" AS wallet
FROM "GameParameters" p
LEFT JOIN "Games" g ON g."ParametersId" = p."Id" AND g."Status" IN (1, 3, 4)
LEFT JOIN "GameBets" b ON b."GameId" = g."Id"
LEFT JOIN "Players" pl ON pl."Id" = b."PlayerId"
WHERE p."Name" = 'Cosmic Sprint';

-- ---------------------------------------------------------------------------------
-- THE CHANGE. Everything below runs as one transaction and rolls back whole on any
-- assertion failure, so a bad match commits nothing.
-- ---------------------------------------------------------------------------------
BEGIN;

-- 1. Refuse unless EXACTLY ONE row carries this name. A rename, a duplicate, or the wrong
--    database would otherwise make the UPDATE a silent no-op or hit more than intended.
DO $$
DECLARE
    matched int;
BEGIN
    SELECT COUNT(*) INTO matched FROM "GameParameters" WHERE "Name" = 'Cosmic Sprint';

    IF matched <> 1 THEN
        RAISE EXCEPTION
            'Expected exactly 1 "Cosmic Sprint" row, found %. Nothing committed — check the name against GeneralSettings:Operator:DefaultGames.',
            matched;
    END IF;
END $$;

-- 2. Re-price. Cosmic Sprint ONLY — Beast Mode (0.02, still ComingSoon) and Space Jackpot (0.1)
--    are deliberately untouched. Mirrors GeneralSettings:Operator:DefaultGames.
UPDATE "GameParameters" SET "RequiredAmount" = 0.01 WHERE "Name" = 'Cosmic Sprint';

-- 3. Assert the row now reads what we meant. A silent no-op here would leave production quietly
--    taking the old price, which is the whole failure this guard exists to prevent.
DO $$
DECLARE
    now_priced numeric;
BEGIN
    SELECT "RequiredAmount" INTO now_priced FROM "GameParameters" WHERE "Name" = 'Cosmic Sprint';

    IF now_priced <> 0.01 THEN
        RAISE EXCEPTION 'Cosmic Sprint reads % after the update, expected 0.01. Nothing committed.', now_priced;
    END IF;
END $$;

COMMIT;

-- ---------------------------------------------------------------------------------
-- IMMEDIATELY AFTER THE COMMIT — this is not optional.
--
-- 1. Bounce Consumers so it drops the `ActiveParameters` Redis key on boot
--    (Consumers/Program.cs — unconditional on every startup, commit 2164e56). That key has a
--    12h TTL and production has NO other way to clear it: both invalidating handlers are
--    admin-only and no admin token can be minted there.
--
--    Re-deploy the revision's CURRENT digest, so the new revision boots with byte-identical code
--    and config and nothing unreleased on `main` ships (the tip carries the referral program, the
--    1%->2% commission and a migration that has not been applied):
--
--        gcloud run services describe spacestroker-prod-consumers --region=us-central1 --format='value(template.containers[0].image)'
--
--        gcloud run services update spacestroker-prod-consumers --region=us-central1 --image=<the digest that printed>
--
--    WARNING: do NOT restart it by injecting a throwaway env var (CACHE_BUST and friends). Env on
--       this service is Terraform-owned, so the next `apply` would strip it and roll the service a
--       second time.
--
--    ⚠️ Confirm the new revision actually took traffic. `update-traffic --to-revisions=X=100`
--       PINS a service, and a pinned service gives new revisions 0% and never starts them —
--       the deploy then reports success while nothing changed (2026-08-05 rollback).
--
-- 2. Verify by OBSERVABLE STATE, never by a log line — production logs at Warning and above, so
--    Log.Information never reaches Cloud Logging:
--
--        curl https://api.spacestroker.com/games/active-types
--
--    Cosmic Sprint must report requiredAmount 0.01, Beast Mode 0.02, Space Jackpot 0.1, all with
--    tokenSymbol "SOL", tokenDecimals 9 and tokenIsNative true.
--
-- ⚠️ If the catalogue still shows 0.02, the cache is stale — the update did NOT fail. Re-check
--    that the Consumers revision actually booted before touching this row again.
--
-- UNDO: UPDATE "GameParameters" SET "RequiredAmount" = 0.02 WHERE "Name" = 'Cosmic Sprint';
--       then bounce Consumers again. Prod SQL has PITR OFF, so a backup is the only recovery
--       from a mis-scoped WHERE — take one before running this.
-- ---------------------------------------------------------------------------------

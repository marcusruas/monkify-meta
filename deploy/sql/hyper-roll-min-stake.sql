-- =====================================================================================
-- HYPER ROLL MINIMUM STAKE — 0.01 SOL -> 0.002 SOL   (MaximumStake 0.1 is untouched)
--
-- Run ONCE, by hand, in Cloud SQL Studio against the `spacestroker` database.
-- `spacestroker-prod-pg` is PRIVATE-IP ONLY, so the console's SQL Studio is the route in.
--
-- ⚠️ WHY THIS IS SQL AND NOT CONFIG. Game types are seeded CREATE-ONLY BY NAME and
--    HouseEdgeReconciler deliberately never touches RequiredAmount or MaximumStake. Editing
--    spacestrokersettings.json reaches a FRESH database only; the config edit still ships so the
--    two agree.
--
-- ⚠️ THE CACHE IS HALF THE JOB — SEE THE FOOTER. Until the `ActiveParameters` Redis key is dropped
--    the browser keeps quoting the OLD minimum (0.01). LOWERING the floor makes that harmless: bet
--    validation reads THIS row directly, and every stake the stale UI allows (>= 0.01) is still
--    >= 0.002, so no bet is refused in the gap. Players just cannot pick 0.002 until the bounce.
--
-- The 0.002 floor is above the rent-exempt minimum HouseEdgeRules enforces (0.00089088 SOL), so
-- the type keeps opening rounds. The smallest possible payout becomes 0.002 × 4 = 0.008 SOL.
-- =====================================================================================

-- ---------------------------------------------------------------------------------
-- PRE-CHECK (read-only, run on its own first). Current bounds for Hyper Roll.
-- ---------------------------------------------------------------------------------
SELECT "Name", "RequiredAmount", "MaximumStake", "IsHouseEdge"
FROM "GameParameters"
WHERE "Name" = 'Hyper Roll';

-- ---------------------------------------------------------------------------------
-- THE CHANGE. One transaction; any failed assertion rolls the whole thing back.
-- ---------------------------------------------------------------------------------
BEGIN;

-- 1. Refuse unless EXACTLY ONE house-edge row carries this name and it is still at the old
--    floor. A rename, a duplicate, the wrong database or a second run all stop here.
DO $$
DECLARE
    matched int;
    current_min numeric;
BEGIN
    SELECT COUNT(*) INTO matched FROM "GameParameters" WHERE "Name" = 'Hyper Roll' AND "IsHouseEdge";

    IF matched <> 1 THEN
        RAISE EXCEPTION
            'Expected exactly 1 house-edge "Hyper Roll" row, found %. Nothing committed.', matched;
    END IF;

    SELECT "RequiredAmount" INTO current_min FROM "GameParameters" WHERE "Name" = 'Hyper Roll' AND "IsHouseEdge";

    IF current_min <> 0.01 THEN
        RAISE EXCEPTION
            'Hyper Roll minimum stake is %, expected 0.01. Already changed? Nothing committed.', current_min;
    END IF;
END $$;

-- 2. Lower the floor. Hyper Roll ONLY — Cosmic Sprint and Space Jackpot are untouched.
UPDATE "GameParameters" SET "RequiredAmount" = 0.002 WHERE "Name" = 'Hyper Roll' AND "IsHouseEdge";

-- 3. Assert the row now reads what we meant, and that the ceiling is still above the floor.
DO $$
DECLARE
    now_min numeric;
    now_max numeric;
BEGIN
    SELECT "RequiredAmount", "MaximumStake" INTO now_min, now_max
    FROM "GameParameters" WHERE "Name" = 'Hyper Roll' AND "IsHouseEdge";

    IF now_min <> 0.002 THEN
        RAISE EXCEPTION 'Hyper Roll reads % after the update, expected 0.002. Nothing committed.', now_min;
    END IF;

    IF now_max IS NULL OR now_max < now_min THEN
        RAISE EXCEPTION 'Hyper Roll MaximumStake % is below the new minimum %. Nothing committed.', now_max, now_min;
    END IF;
END $$;

COMMIT;

-- ---------------------------------------------------------------------------------
-- IMMEDIATELY AFTER THE COMMIT.
--
-- 1. Bounce Consumers so it drops the `ActiveParameters` Redis key on boot. Re-deploy the running
--    revision's DIGEST (never `:latest`, never a throwaway env var — env is Terraform-owned):
--
--        gcloud run revisions describe <serving rev> --region=us-central1 --format='value(status.imageDigest)'
--        gcloud run services update spacestroker-prod-consumers --region=us-central1 --image=<repo>@<digest>
--
--    ⚠️ Only while NO round of ANY type is GameStarting/InProgress: CloseOngoingGames runs at boot
--       and parks those rounds' bets as NeedsManualAnalysis. WaitingBets rounds are safe.
--
-- 2. Verify by OBSERVABLE STATE: `curl https://api.spacestroker.com/games/active-types` must show
--    Hyper Roll requiredAmount 0.002 and maximumStake 0.1.
--
-- UNDO: UPDATE "GameParameters" SET "RequiredAmount" = 0.01 WHERE "Name" = 'Hyper Roll' AND "IsHouseEdge";
--       then bounce Consumers again. ⚠️ RAISING the floor is NOT gap-safe the way lowering is: a
--       stale UI would quote 0.002 against a 0.01 row, and those bets are refused after the SOL
--       moved — bounce Consumers the moment the UNDO commits.
-- ---------------------------------------------------------------------------------

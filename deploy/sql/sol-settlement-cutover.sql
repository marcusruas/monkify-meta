-- =====================================================================================
-- SOL SETTLEMENT CUTOVER — re-price the seeded game types (Trello #174)
--
-- Run ONCE, by hand, immediately after deploying the SOL configuration.
-- See monkify-meta/specs/sol-settlement.md §13 for the full runbook.
--
-- ⚠️ SUPERSEDED FOR COSMIC SPRINT (2026-08-23). This script is kept as the RECORD OF WHAT WAS RUN
--    at the SOL cutover, so its values are deliberately left at 0.02 / 0.02 / 0.1. Cosmic Sprint
--    now prices at 0.01 — re-running this file as-is would silently put it BACK to 0.02. Use
--    deploy/sql/cosmic-sprint-reprice.sql instead, and treat this one as history.
--
-- ⚠️ WHY THIS IS SQL AND NOT CODE. Game types are seeded CREATE-ONLY BY NAME: the startup
--    seeder inserts a type when no row with that name exists and never rewrites an existing
--    one, which is what stops a restart resurrecting an admin-deactivated type or mutating a
--    type under a live round. Only three named self-heals reconcile a field (AllowFreeBet,
--    AllowOrbit, ComingSoon), and a fourth was deliberately NOT added for RequiredAmount
--    (spec §3 decision 6). So new prices in spacestrokersettings.json reach a FRESH database
--    only; an existing one needs this.
--
-- ⚠️ THE STANDING COST OF THAT DECISION: config and any existing database now disagree until
--    this is re-run. A local or dev stack seeded before the switch keeps the OLD prices, and
--    re-running the seeder will not fix it. If a stack looks like it is still asking for 1 SOL
--    a ticket, this script is what it is missing.
--
-- ⚠️ RUN IT WHEN NO PAID ROUND IS OPEN. RequiredAmount is read per bet, so changing it under a
--    round that is taking bets makes the round's earlier and later bets disagree about the
--    stake — and a bet whose Amount != RequiredAmount is refused as InvalidAmount after the
--    money has already moved on-chain. The runbook's drain step already guarantees this; the
--    guard below re-checks it rather than trusting the operator to have done it.
-- =====================================================================================

BEGIN;

-- ---------------------------------------------------------------------------------
-- 1. Refuse to run while any round is open on a type we are about to re-price.
--    Statuses 1/3/4 are WaitingBets / GameStarting / InProgress (Game.ActiveGamesStatus).
--    ⚠ GameStatus IS 1-BASED (WaitingBets = 1). This guard read IN (0, 2, 3) until 2026-08-21,
--    which is nothing + NotEnoughPlayersToStart + GameStarting: it MISSED WaitingBets and
--    InProgress, the two states where a round is actually taking bets or running. It reported a
--    clean drain during the production cutover while two rounds were open and holding paid bets.
--    BetStatus, by contrast, IS zero-based (Made = 0) - do not "fix" the two to match.
-- ---------------------------------------------------------------------------------
DO $$
DECLARE
    open_rounds int;
BEGIN
    SELECT COUNT(*) INTO open_rounds
    FROM "Games" g
    JOIN "GameParameters" p ON p."Id" = g."ParametersId"
    WHERE p."Name" IN ('Cosmic Sprint', 'Beast Mode', 'Space Jackpot')
      AND g."Status" IN (1, 3, 4);

    IF open_rounds > 0 THEN
        RAISE EXCEPTION
            'Refusing to re-price: % round(s) are still open. Let them settle first — a bet placed at the old price would be refused as InvalidAmount after its money moved on-chain.',
            open_rounds;
    END IF;
END $$;

-- ---------------------------------------------------------------------------------
-- 2. Re-price. Values mirror GeneralSettings:Operator:DefaultGames so a fresh install and a
--    migrated one agree.
--
--    ⚠️ Beast Mode is repriced even though it is ComingSoon and cannot be bet today. That is
--    the point: leaving a live 1-SOL-per-ticket row in the database means whoever un-gates it
--    later silently ships a 50x ticket. Defuse it while nobody can reach it.
-- ---------------------------------------------------------------------------------
UPDATE "GameParameters" SET "RequiredAmount" = 0.02 WHERE "Name" = 'Cosmic Sprint';
UPDATE "GameParameters" SET "RequiredAmount" = 0.02 WHERE "Name" = 'Beast Mode';
UPDATE "GameParameters" SET "RequiredAmount" = 0.1  WHERE "Name" = 'Space Jackpot';

-- ---------------------------------------------------------------------------------
-- 3. Assert we touched exactly the three rows we meant to. A silent no-op here — a renamed
--    type, a different database — would leave production quietly taking the old price, which
--    is the failure this whole script exists to prevent.
-- ---------------------------------------------------------------------------------
DO $$
DECLARE
    repriced int;
BEGIN
    SELECT COUNT(*) INTO repriced
    FROM "GameParameters"
    WHERE ("Name" = 'Cosmic Sprint' AND "RequiredAmount" = 0.02)
       OR ("Name" = 'Beast Mode'    AND "RequiredAmount" = 0.02)
       OR ("Name" = 'Space Jackpot' AND "RequiredAmount" = 0.1);

    IF repriced <> 3 THEN
        RAISE EXCEPTION
            'Expected 3 re-priced game types, found %. Nothing has been committed — check the type names against GeneralSettings:Operator:DefaultGames before retrying.',
            repriced;
    END IF;
END $$;

-- ---------------------------------------------------------------------------------
-- 4. Report the un-won jackpot carry. NOT changed here — the runbook settles the open Space
--    Jackpot round first and expects this to already be zero. A non-zero value means the drain
--    was incomplete and a USDC-denominated pot is about to be paid out in SOL.
-- ---------------------------------------------------------------------------------
DO $$
DECLARE
    carried numeric;
BEGIN
    SELECT COALESCE(MAX(g."CarriedExtra"), 0) INTO carried
    FROM "Games" g
    JOIN "GameParameters" p ON p."Id" = g."ParametersId"
    WHERE p."Name" = 'Space Jackpot';

    IF carried > 0 THEN
        RAISE EXCEPTION
            'Space Jackpot still carries % of un-won pot from the previous settlement asset. Settle or zero it before switching, or it pays out in SOL.',
            carried;
    END IF;
END $$;

COMMIT;

-- ---------------------------------------------------------------------------------
-- Verification — run AFTER the commit, and verify by OBSERVABLE STATE rather than by this
-- script's exit code:
--
--     curl https://api.spacestroker.com/games/active-types
--
-- Cosmic Sprint must report requiredAmount 0.02, Space Jackpot 0.1, Beast Mode 0.02, all with
-- tokenSymbol "SOL", tokenDecimals 9 and tokenIsNative true.
--
-- ⚠️ The API caches the active-parameters blob for 12h. If the catalogue still shows the old
--    prices, the cache is stale rather than the update having failed.
-- ---------------------------------------------------------------------------------

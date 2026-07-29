# Feature — Choice Exclusivity

> Status: **shipped.** Authoritative spec: `../specs/choice-exclusivity.md`
> (merged via `feature/choice-exclusivity` on 2026-07-24).

## What it does

In **free-text games** (game types with no `PresetChoices` — e.g. *Cosmic Sprint*), each typed
sequence is **exclusive** to the first player who bets it in a round: a *different* player who tries
the same sequence is rejected, and **auto-refunded** if they already paid on-chain. **Preset games**
(e.g. *Beast Mode*) are unchanged — many players may still share a preset choice. Because a free-text
winning sequence now maps to exactly one bet, free-text rounds are effectively **winner-takes-all**.

## Core rules

- **Scope — free-text only.** Exclusivity applies when `GameParameters.PresetChoices` is empty
  (race **or** lottery). Preset game types keep shared choices (that's the point of a preset list).
- **First persisted bet reserves the choice**, per game round; choices free up on the next round
  (new `Game`). Reservation is keyed on the choice (`Bet.Choice` is **trimmed**, case-preserved, so
  `" abcdef "` == `abcdef`). Casing is **not** folded: uppercase is rejected as `InvalidCharacters`
  before reservation (the game alphabets are lowercase-only), so a reserved choice is always
  lowercase. See `specs/bet-choice-casing.md`.
- **A different player taking a taken free-text choice is rejected** with a specific message,
  `ErrorMessages.ChoiceAlreadyTaken` ("This sequence has already been taken by another player. Pick a
  different one."). For a paid **`SolanaToken`** bet — where the on-chain debit happens *before*
  validation — the bet is **auto-refunded** (`RefundInvalidBetEvent`) and the message carries the
  refund warning. **Standard** paid bets are rejected before the wallet debit (no refund needed);
  **free** bets are rejected log-only (no refund), per the free-bet rule.
  > ⚠️ **Correction (2026-07-29, `legal-docs-self-exclusion` #125).** From this feature's ship until
  > that card, the auto-refund above **did not actually happen**. `RefundInvalidBet` publishes for a
  > bet that `InsertBet` only creates later, so `RefundInvalidBetConsumer` could not load it and
  > returned after a warning — the player saw the refund warning and the stake stayed with the house.
  > Refunds are now issued **bet-less from the event** (which carries `GameId` + the payment
  > signature), so this paragraph describes real behavior again. See
  > `legal-docs-self-exclusion.md` → "The refund fix".
- **Same-player behavior is unchanged.** A player re-betting their *own* choice still hits the
  existing `DuplicateBet` / `DuplicateBetRefunded` path (the exclusivity check runs *after* the
  per-player duplicate check); a player may still place multiple bets on *different* free-text choices.
- **Concurrency is race-safe.** A DB **unique index** `(GameId, ExclusiveChoice)` guarantees exactly
  one persisted bet per free-text choice even when two bets pass the application check simultaneously;
  the loser is rejected + refunded (paid) / logged (free). `Bet.ExclusiveChoice` is the normalized
  choice for free-text bets and **`NULL`** for preset bets (Postgres treats NULLs as distinct, so
  preset games never collide).
- **Frontend blocks a taken sequence before payment.** In a free-text game, the client checks the live
  bet list and refuses to submit (no on-chain payment) a sequence already present, showing "That
  sequence is already taken — pick another." This is best-effort (the live list can lag); the backend
  reject+refund is authoritative.
- **No reward-math change.** `pot / payableWinnerCount` already handles the single-winner case;
  multi-winner splitting still applies to preset games.
- **Edge cases:** a choice reserved by a bet later admin-refunded **stays reserved** for that round
  (the row still occupies the unique slot); payment-invalid / RG-denied bets never persist, so they
  reserve nothing.

## Key code touchpoints

- **Backend** (`monkify-backend`):
  - `SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs` — cross-player
    exclusivity check in `ValidateBetParameters` (free-text only) via `RefundInvalidBet`; sets
    `_bet.ExclusiveChoice` before persistence; catches `ChoiceAlreadyTakenException` around
    `RegisterBet`; per-player duplicate query compares the normalized `_bet.Choice`.
  - `SpaceStroker.Domain/Bets/Entities/Bet.cs` — `ExclusiveChoice` column.
  - `SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` — unique index
    `(GameId, ExclusiveChoice)`; migration `Migrations/*_AddBetExclusiveChoice.cs`.
  - `SpaceStroker.Infrastructure/Services/Games/GameService.cs` — `InsertBet` maps the 23505
    unique-violation to `ChoiceAlreadyTakenException` (`Services/Games/ChoiceAlreadyTakenException.cs`).
  - `SpaceStroker.Common/Resources/ErrorMessages.resx` — `ChoiceAlreadyTaken`.
  - Tests: `SpaceStroker.Tests/UnitTests/Handlers/RegisterBetHandlerTests.cs`,
    `SpaceStroker.Tests.Integration/ChoiceExclusivityFlowTests.cs`.
- **Frontend** (`monkify-front`):
  - `src/hooks/useGame.ts` — pre-pay exclusivity guard in `bet()`.
  - `src/components/game/GameConsole.vue` — `placeBet()` lever-error buzz for a taken choice.

# SpaceStroker — "Typer Refactoring" Feature — Implementation Spec

> **Status: Ready for implementation** — refined via Trello card
> [Space Stroker typer refactoring](https://trello.com/c/Xim01Mvw/181-space-stroker-typer-refactoring) (2026-09-01).
> Repos affected: `monkify-backend` and this doc in `monkify-meta` — **`monkify-front` is NOT affected**
> (no SignalR contract change, no verifier change; see §7).
>
> **Purpose:** the deployed Consumers instance takes ~21 s to finish a round. Bring a round down to
> **~10 s average / 15 s max**, make free-text races rely **less on the deterministic fallback draw**
> to decide the winner, and cut the CPU/Redis cost of a round — without moving lottery win odds and
> without breaking the provably-fair verification contract for any round, past or future.

---

## 1. Summary

Three coordinated changes to the run path, plus one persistence change that keeps verification honest:

| # | Change | Where | Effect |
| --- | --- | --- | --- |
| 1 | **Pacing retune** — race mean 8–12 s (avg 10), p95 15 s; lottery draw run retuned to the same landing zone; character ceiling 5 000 000 → **500 000** | `GameSettings` defaults + `spacestrokersettings.json` | Fallback-bound rounds are *scheduled* to land at ~10 s and are CPU-cheap enough to actually meet the schedule |
| 2 | **Time-based terminal flush** — the 200/500-char count batch becomes a flush every **50/40/30 ms keyed to the `DisplaySpeed` ramp** (1→2→3), variable batch sizes | `GameService.RunGame` | ~25 000 SignalR messages per fallback round → a few hundred; Redis backplane traffic drops proportionally |
| 3 | **Bet-derived character pool for free-text races** — pool = union of the placed choices' characters, topped up to the existing ⌊alphabet/5⌋ diversity floor | `SpaceStrokerTyper.SetCharactersOnTyper` | Few bets ⇒ small pool ⇒ natural sliding-window wins become reachable inside the cap; the fallback share drops. **Lottery pool derivation is untouched** (money behavior) |
| 4 | **Persist the run's verification inputs** — `Game.CharacterPool` + `Game.MaxCharacters` written when the run ends; verification publishes persisted values, `NULL` ⇒ legacy derivation | `Game` entity + migration + `GetGameVerificationHandler` | Historical rounds keep verifying byte-for-byte after the derivation changes — and future config retunes can never shift a settled round's published inputs again |

**Why a round takes ~21 s today (code exploration).** For a free-text race the natural-win search
space is `36^6 ≈ 2.18B` windows, unreachable, so the run is fallback-bound: the typer generates the
full `MaxTyperCharacters = 5 000 000` HMAC-SHA256 characters and `RunGame` sends one SignalR group
message per `TerminalBatchLimit` chars. The pacing schedule targets 13 s mean, but
`ApplyPacingDelay` only ever *sleeps when ahead* — it can never speed up a loop that is CPU/IO-bound
below the scheduled rate. 5M HMAC blocks + tens of thousands of Redis-backplane publishes on a
shared Cloud Run vCPU run *behind* schedule, so the wall clock stretches to ~21 s. Shrinking the
worst case to 500K chars and a few hundred messages makes the schedule the binding constraint again.

**Expected fallback-share math (for tuning, not asserted in tests).** With pool size `p`, choice
length `L=6` and `B` distinct choices, the expected natural-win point is `p^6 / B` characters:

| Bets (distinct 6-char choices) | Typical pool `p` (union + floor 7) | Expected chars | vs 500K cap |
| --- | --- | --- | --- |
| 2 | 7–10 | 26K–500K | mostly natural |
| 3 | 8–12 | 87K–1.0M | often natural |
| 6+ | 14+ | ≥1.25M | fallback-bound, as today |

The card asks to *reduce* fallback reliance, not eliminate it — the win condition is the low-bet
regime (which, with the house seed bet on every round, is the common early-volume case).

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo:** `feature/typer-refactoring` in `monkify-backend` + `monkify-meta`, work in
  `.worktrees/typer-refactoring/<repo>`; hubs stay on `main`.
- **Unit tests:** update/add in `SpaceStroker.Tests`; run via `scripts/run_tests.bat`; report changed
  tests at the end. Game-flow change ⇒ integration tests (`SpaceStroker.Tests.Integration`) must be
  updated too, run via `scripts/run_integration_tests.bat`.
- **Level = `Feature`** ⇒ at least one **happy-path integration test** (§11.3) is mandatory.
- **EF mapping:** inline in `SpaceStrokerDbContext.OnModelCreating`; no `IEntityTypeConfiguration`.
- **Migrations:** under `SpaceStroker.Infrastructure/Migrations/`; applied only by `SpaceStroker.Consumers`.
- **Secrets/config:** mock-only values in committed files.
- **Card constraints (verbatim):** code must be concise and easy to understand — this is a crucial
  part of the app; **no new classes**; the solution must make rounds rely less on the fallback.
- **New config keys ship with code defaults** — nothing here requires a Secret Manager /
  `base_secret_ids` binding (see `features/infra.md`; unbound keys fail silently).
- **Outcome determinism is inviolable:** character selection, the fallback trigger and the fallback
  draw remain pure functions of `ServerSeed` + ordered bet seeds + committed config-derived inputs.
  Pacing and batching may only ever change *timing*, never the stream or the winner.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | Pacing targets (avg ~10 s / max 15 s) apply to **races AND the lottery draw run**; the **pool shrink is races-only** — shrinking a lottery pool would raise how often *anyone* wins, a money/pot-stacking change, not pacing. |
| 2 | "Max 15 s" is achieved by **config retune + a lower character ceiling**, keeping the count-triggered deterministic fallback. No wall-clock abort — elapsed time is not reproducible by a verifier. |
| 3 | Terminal batching becomes a **time-based flush at 50/40/30 ms keyed to the current `DisplaySpeed`** (config, code defaults). Every character is still delivered in order — the frontend `N AWAY!` row replays the full stream client-side; no dropping/truncating. |
| 4 | Free-text race pool = **union of placed choices' characters + the existing ⌊alphabet/5⌋ minimum-diversity floor, sorted**. No bet-count threshold. Invariant: the pool always contains every character of every placed bet's choice. |
| 5 | **Persist `CharacterPool` + `MaxCharacters` on `Game`** in the same `SaveChanges` as `WinningChoice`; verification publishes persisted values; `NULL` ⇒ legacy round ⇒ derive with the **old** rules (full alphabet / old cap math, pinned constants). |
| 6 | Tests assert the **deterministic math** (cap sizing, pool derivation, fallback-share reduction over seeded simulated rounds); wall-clock is proven **operationally** (local prod-mode measurement + post-deploy timestamp spot-check). No stopwatch asserts in CI. |
| 7 | Level = **`Feature`** ⇒ mandatory happy-path integration test + a dedicated `features/typer-refactoring.md` at ship time. |

---

## 4. Data model

### 4.1 Entity changes — `SpaceStroker.Domain/Games/Entities/Game.cs`

```csharp
// Character pool the run actually used, persisted when the game ends (GameService.UpdateGameStatus,
// same SaveChanges as WinningChoice/CharactersTyped). The verification endpoint publishes this
// verbatim instead of re-deriving it, so a later change to pool derivation or settings can never
// shift a settled round's published inputs. Null = round never ran, or pre-migration ("legacy") —
// the verification handler then derives with the pinned legacy rules.
public string? CharacterPool { get; set; }

// The fallback character cap the run actually used, persisted alongside CharacterPool. Same
// null-means-legacy semantics. WinnerDecidedByFallback is derived as CharactersTyped >= this.
public int? MaxCharacters { get; set; }
```

### 4.2 EF configuration (inline, `SpaceStrokerDbContext.OnModelCreating`, `Entity<Game>` block)

```csharp
builder.Property(x => x.CharacterPool).HasMaxLength(64).IsRequired(false); // largest alphabet is 36 chars
builder.Property(x => x.MaxCharacters).IsRequired(false);
```

### 4.3 Migration

Name: **`TyperRunSnapshot`** — adds nullable `CharacterPool` (varchar 64) and `MaxCharacters` (int)
to `public."Games"`. No backfill (NULL *is* the legacy marker). Generate from
`SpaceStroker.Infrastructure`:

```bash
dotnet ef migrations add TyperRunSnapshot -p src/SpaceStroker.Infrastructure -s src/SpaceStroker.Consumers
```

Applied only by `SpaceStroker.Consumers` at startup, as always. Never hand-edit the snapshot.

---

## 5. Configuration

All in `GameSettings` (`SpaceStroker.Domain/Configs/Entities/GameSettings.cs`) with **code defaults**,
mirrored in the committed `SpaceStroker.Common/spacestrokersettings.json` (mock/dev values only — the
deployed environments bind their own copies; an absent key falls back to the code default, so no
secret-binding work is needed).

| Key | Today (code default / committed json) | New | Why |
| --- | --- | --- | --- |
| `RaceTargetMeanSecondsMin` | 8 / 8 | **8** | unchanged floor |
| `RaceTargetMeanSecondsMax` | 18 / 18 | **12** | mean (min+max)/2 = 10 s |
| `RaceTargetP95SecondsMax` | 30 / 30 | **15** | the "max": schedule ceiling 15 × 0.9 = 13.5 s |
| `LotteryTargetMeanSecondsMin` | 10 / 10 | **8** | lottery draw run lands in the same zone (decision 1) |
| `LotteryTargetMeanSecondsMax` | 30 / 30 | **12** | 〃 |
| `LotteryTargetP95SecondsMax` | 45 / 45 | **15** | 〃 |
| `MaxTyperCharacters` | 5 000 000 / 5 000 000 | **500 000** | worst-case CPU/messages bounded; top of the agreed 250–500K range to maximise the natural-win share (§1 math) |
| `TerminalBatchLimit` | 500 (json; card said 200 — the deployed value differs from the committed one) | **removed** | superseded by the flush intervals below; delete the property and the json key |
| `TerminalFlushSpeed1Ms` | — | **50** (new) | flush interval while `DisplaySpeed == 1` |
| `TerminalFlushSpeed2Ms` | — | **40** (new) | 〃 speed 2 |
| `TerminalFlushSpeed3Ms` | — | **30** (new) | 〃 speed 3 |

Three flat `int` properties (not an array/child object) — deliberate: trivially bindable, no new
class (card constraint). The card's numbers are starting points; validate during implementation and
tune the committed values if the measured cadence/burst size argues for it (note it in the spec's
revision log if changed).

> ⚠️ `MinimumWaitPeriodForBets`, `TimeUntilGameStarts` and the front's 9 s post-`Ended` hold
> (`specs/game-mode-ux.md`) are **not** part of this card — "round time" here is the typer run
> (`InProgress` → `Ended`).

---

## 6. Backend behaviour

### 6.1 `SpaceStrokerTyper.SetCharactersOnTyper` — bet-derived pool for free-text races

Today: preset games build the pool from preset-choice characters; free-text games take the **full**
`AllowedCharacters` alphabet (`GameCharacterType` descriptions: numbers 10 / letters 26 /
numbers+letters 36 chars). Both then top up to `minimumAvailableCharacters = allowedChars.Length / 5`
using alphabet order, and sort.

New rule — **only the free-text branch changes, and only for races**:

```csharp
if (!game.Parameters.PresetChoices.IsNullOrEmpty())
    // presets: unchanged — pool from preset-choice characters
else if (IsLottery)
    // lottery free-text: unchanged — full alphabet (money behavior; decision 1)
else
    // free-text RACE: pool from the characters of the placed bets' choices
    foreach (var character in game.Bets.SelectMany(b => b.Choice)) availableCharacters.Add(character);
```

The existing floor top-up and `Order()` sort stay exactly as they are and run after every branch —
they provide the ⌊alphabet/5⌋ diversity minimum and the canonical ordering the keystream indexes
into. Notes:

- `IsLottery` is already computed in the constructor **before** `SetCharactersOnTyper` runs — keep
  that ordering.
- Bet choices are validated at placement (trimmed, in-alphabet, length-exact — `features/games.md`
  §5), so the union is always a subset of the alphabet; free bets and the house-seed/Orbit bets are
  ordinary `game.Bets` entries and join the union like any other.
- The pool remains a **pure function of the committed inputs** (the bets), so the verification
  endpoint's read-time typer reproduces it — and after §6.4 it is persisted anyway.
- **Invariant (unit-tested):** every character of every `game.Bets[i].Choice` is in
  `CharactersOnTyper`. A choice with an out-of-pool character could otherwise never win naturally.

### 6.2 Pacing retune — config only, formulas untouched

`CalculatePacing` keeps its exact shape: `_targetMeanMs = (meanMin+meanMax)/2`, horizon
`min(expectedCharsToWin, MaxTyperCharacters)`, `cap = min(ceiling, hardMaxMs / msPerChar)` with
`P95_SAFETY_FACTOR = 0.9`, `MaxCharacters = max(QueueLength, ceil(cap))`. With the §5 values a
fallback-bound race schedules 500K chars across 10 000 ms (`msPerChar = 0.02`), and
`capByTime = 13 500 / 0.02 = 675K > 500K`, so the ceiling binds — the worst case is *scheduled* to
land at the 10 s mean, well under the 15 s p95. `DEFAULT_MAX_CHARACTERS` (the 0-guard fallback
constant in `SpaceStrokerTyper`) drops from 5 000 000 to **500 000** to match.

The fallback trigger (`CharactersTypedCount >= MaxCharacters`) and the labelled fallback draw are
untouched.

### 6.3 `GameService.RunGame` — time-based flush

Replace the count check with a wall-clock check inside the existing loop (no new classes; reuse the
`Stopwatch`/`Environment.TickCount64` style the typer already uses):

```
lastFlush = now                                  // before the loop
loop:
  emit DisplaySpeed on change (unchanged)
  batch.Append(await monkey.GenerateNextCharacter(ct))
  interval = speed switch { 3 => TerminalFlushSpeed3Ms, 2 => ..., _ => TerminalFlushSpeed1Ms }
  if (now - lastFlush >= interval && batch.Length > 0):
      SendAsync(terminalEndpoint, batch.ToString()); batch.Clear(); lastFlush = now
```

Keep, unchanged: the final drain flush after the loop, and the **fallback reveal** batch (the
winning choice is emitted as a last terminal message when `WinnerDecidedByFallback`, so the terminal
visibly ends with the winning word — `GameService.cs:243-249`).

Sizing sanity (documented, asserted only as an order-of-magnitude in tests): a fallback-bound round
is ≤ 500K chars over ~10 s ⇒ ~50K chars/s ⇒ ≤ ~2.5 KB per 50 ms flush — far under SignalR's default
32 KB message cap — and ~200–330 messages per round vs ~25 000 today.

### 6.4 `GameService.UpdateGameStatus` — persist the run snapshot

In the existing `monkey != null` arm (the one that already writes `WinningChoice` +
`CharactersTyped`), also write, in the **same** `SaveChanges`:

```csharp
game.CharacterPool = new string(monkey.CharactersOnTyper);
_context.Entry(game).Property(x => x.CharacterPool).IsModified = true;
game.MaxCharacters = monkey.MaxCharacters;
_context.Entry(game).Property(x => x.MaxCharacters).IsModified = true;
```

(The `IsModified` flags matter — the context is NoTracking by default; the surrounding code already
uses this pattern.) The reward-path arm (`monkey == null`) does not touch these fields.

### 6.5 `GetGameVerificationHandler` — publish persisted values; `NULL` ⇒ legacy derivation

Today the handler builds a read-time `SpaceStrokerTyper` and publishes its `CharactersOnTyper` /
`QueueLength` / `MaxCharacters` — which is exactly what would break historical rounds once the
derivation changes. New sourcing:

- `dto.CharacterPool = game.CharacterPool ?? <legacy-derived pool>`
- `dto.MaxCharacters = game.MaxCharacters ?? <legacy-derived cap>`
- `dto.WinnerDecidedByFallback = CharactersTyped >= (game.MaxCharacters ?? legacyCap)` (null-count
  semantics unchanged: null ⇒ unknown, never false).
- `QueueLength`, ordered seeds/choices: unchanged (derivable from bets alone, derivation not changing).

**Legacy derivation** (for rounds that ran before this ships — `CharacterPool IS NULL`): a private
static helper **method** on the handler (no new class) that reproduces the **old** rules with
**pinned constants**, immune to future config drift:

- pool: preset-choice chars if presets exist, else the **full alphabet**; floor top-up + sort as
  today (the floor rule is unchanged, so calling the current top-up logic is fine);
- cap: the current `CalculatePacing` formula evaluated with the **legacy values** — race 8/18/30,
  lottery 10/30/45, ceiling 5 000 000, safety 0.9.

A **running** (pre-`Ended`) round's payload during the deploy window: rounds run entirely on one
Consumers revision, and pre-final payloads are never cached, so publishing the *new* derivation for
in-flight rounds and the persisted values once ended is consistent. The 12h settled-round cache
key/eviction semantics are unchanged.

- Not run yet / no bets: unchanged behavior (no pool exposed for a betless round; a not-yet-ended
  round with bets publishes the new-code derivation, which is what its run will use).

### 6.6 Cost-efficiency pointers (deliverable, NO implementation — card's optional ask)

To be written into the implementation report (and carried to the KB at ship time). Candidate
pointers the exploration already surfaced, to be sanity-checked then listed:

1. The two changes here (ceiling 10× down, messages ~100× down) are themselves the dominant CPU and
   Redis wins; measure before optimising further.
2. `ProvablyFair.Keystream` yields 32 bytes per HMAC block; rejection sampling consumes ~1 byte per
   char — confirm block bytes are fully consumed before the next HMAC (if not, that's a free ~30×
   HMAC reduction).
3. SignalR Redis backplane fan-out is per-group-message; batches already coalesce it — a further
   lever is skipping terminal publishes entirely when the group has zero connections (Redis
   `PUBLISH` to nobody still costs a round trip).
4. `CheckForWinners` allocates a string per character (`string.Concat` of the queue); a rolling
   hash / KMP automaton over the window would cut per-char allocations (measure first; 500K× less
   work may make this irrelevant).
5. Redis: the game-loop's cache usage (`ActiveParameters` etc.) is per-round, not per-char — no
   action expected; verify with the local stack's Redis `MONITOR` during one round.

---

## 7. Frontend behaviour

**None.** Verified during exploration:

- `…/Terminal` stays "batched string per message"; `GameTyper.vue` appends whatever arrives (and
  already crops its own display to ~2000 chars). The `N AWAY!` row consumes every character — which
  is why decision 3 forbids dropping any.
- `…/DisplaySpeed` events are unchanged.
- `src/utils/provablyFair.ts:155` and `public/verify.html:182` consume `payload.characterPool`
  **verbatim** — the KEEP-IN-SYNC triad does not encode pool derivation and needs no edit.
- `GameVerificationDto` shape is unchanged (only the *sourcing* of two existing fields changes).

---

## 8. Flow diagram — the run loop after this card

```mermaid
sequenceDiagram
  participant C as RunGameConsumer
  participant T as SpaceStrokerTyper
  participant H as ActiveGamesHub (SignalR/Redis)
  C->>T: new SpaceStrokerTyper(game, settings)
  Note over T: pool = bets' chars ∪ floor (free-text race)<br/>cap = min(500K, 13.5s/msPerChar)
  loop until HasWinners
    C->>T: GenerateNextCharacter()
    T-->>C: char (HMAC keystream; fallback at cap)
    Note over C: append to batch
    alt elapsed ≥ 50/40/30ms (by DisplaySpeed)
      C->>H: Terminal(batch) — flush + clear
    end
  end
  C->>H: Terminal(final drain [+ fallback reveal])
  C->>C: UpdateGameStatus(Ended):<br/>WinningChoice + CharactersTyped + CharacterPool + MaxCharacters (one SaveChanges)
```

---

## 9. Out of scope

- CPU/Redis cost-efficiency **implementation** — §6.6 pointers only.
- Any lottery character-pool change (pacing targets only).
- Wall-clock-triggered fallback.
- Frontend changes of any kind (incl. the 9 s post-`Ended` hold and lobby timings).
- The stale `serverSeedHash` read in `useGame.onStatus` and the unused `DelayBetweenGames` (separate cards).

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/SpaceStroker.Domain/Games/Services/SpaceStrokerTyper.cs` | Free-text-race branch in `SetCharactersOnTyper` (§6.1); `DEFAULT_MAX_CHARACTERS` → 500 000 |
| 2 | `src/SpaceStroker.Domain/Configs/Entities/GameSettings.cs` | Retuned defaults; drop `TerminalBatchLimit`; add `TerminalFlushSpeed1/2/3Ms` (§5) |
| 3 | `src/SpaceStroker.Domain/Games/Entities/Game.cs` | `CharacterPool` + `MaxCharacters` (§4.1) |
| 4 | `src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` | Inline mapping (§4.2) |
| 5 | `src/SpaceStroker.Infrastructure/Migrations/*_TyperRunSnapshot.cs` (+snapshot) | Generated migration (§4.3) |
| 6 | `src/SpaceStroker.Infrastructure/Services/Games/GameService.cs` | Time-based flush in `RunGame` (§6.3); snapshot persist in `UpdateGameStatus` (§6.4) |
| 7 | `src/SpaceStroker.Infrastructure/Handlers/Games/GetGameVerification/GetGameVerificationHandler.cs` | Persisted-first sourcing + legacy helper (§6.5) |
| 8 | `src/SpaceStroker.Common/spacestrokersettings.json` | §5 values (mock/dev file) |
| 9 | `src/SpaceStroker.Tests/UnitTests/Domain/Games/SpaceStrokerTyperTests.cs` | §11.1 |
| 10 | `src/SpaceStroker.Tests/UnitTests/Services/GameServiceTests.cs` | §11.2 |
| 11 | `src/SpaceStroker.Tests/UnitTests/Handlers/GetGameVerificationHandlerTests.cs` | §11.2 |
| 12 | `src/SpaceStroker.Tests.Integration/…` (fixture + `ProvablyFairFlowTests` + new happy-path test) | §11.3 |

### 10.2 Frontend (`monkify-front`)

None (§7).

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/typer-refactoring.md` | This spec |
| 2 | `INDEX.md` | Map row + feature-in-flight entry |

---

## 11. Unit tests

Project: `monkify-backend/src/SpaceStroker.Tests` (xUnit + Shouldly + Moq + Bogus).

### 11.1 `SpaceStrokerTyperTests`

| Test | Expected |
| --- | --- |
| ⚠️ **Rewrite** `Constructor_LettersWithoutPresetChoices_ShouldAlwaysUseFullAlphabet` | This test pins the exact behavior being changed. Becomes: free-text race pool = union of bet-choice chars + floor, sorted |
| New: pool invariant | For arbitrary (Bogus) bet sets: every char of every bet's choice ∈ `CharactersOnTyper`; pool ⊆ alphabet; pool sorted; `|pool| ≥ ⌊alphabet/5⌋` |
| New: lottery pool unchanged | `IsLottery` game with free-text bets ⇒ pool = full alphabet (byte-identical to old rule) |
| New: preset pool unchanged | Preset race ⇒ pool from preset chars (existing tests keep passing) |
| New: cap sizing | With §5 defaults: fallback-bound config ⇒ `MaxCharacters == 500 000` and scheduled duration `MaxCharacters × _targetMsPerChar ≤ 15 000 × 0.9` (assert via the public `MaxCharacters` + config arithmetic, no stopwatch) |
| New: fallback-share reduction (seeded simulation) | N seeded 2-bet free-text rounds (deterministic server seeds, real generation loop with pacing targets zeroed): fallback share with the new pool rule `<` share with full alphabet, and below a pinned threshold |
| Existing determinism/fallback tests | Still green — same-seed streams identical; count-triggered fallback unchanged |

### 11.2 `GameServiceTests` + `GetGameVerificationHandlerTests`

| Test | Expected |
| --- | --- |
| `RunGame` flush cadence | With mocked hub: all generated characters delivered in order across messages (concatenation equals the stream); message count within an order-of-magnitude bound (≪ character count); final drain + fallback-reveal messages still sent |
| `UpdateGameStatus` persists snapshot | Monkey arm writes `CharacterPool`/`MaxCharacters` with `WinningChoice`/`CharactersTyped` in one `SaveChanges`; reward arm (`monkey == null`) leaves them untouched |
| Verification: persisted round | DTO carries the **persisted** pool/cap (set them ≠ what read-time derivation would produce to prove sourcing) |
| Verification: legacy round | `CharacterPool IS NULL` ⇒ DTO carries full-alphabet pool + legacy-constant cap; `WinnerDecidedByFallback` derived against the legacy cap; null `CharactersTyped` still ⇒ null flag |
| Cache semantics | Unchanged (existing tests keep passing) |

### 11.3 Integration (`SpaceStroker.Tests.Integration`, Testcontainers)

| Test | Expected |
| --- | --- |
| **New happy path (mandatory, Feature):** low-bet free-text round | 2 paid bets with overlapping-alphabet choices ⇒ round runs to `RewardForWinnersCompleted`; `WinnerDecidedByFallback == false` (natural win); winner rewarded; `GET /games/{id}/verification` returns the persisted pool (⊇ both choices' chars) and cap, consistent with `charactersTyped` |
| `ProvablyFairFlowTests` (existing) | Update expectations where they assume the full-alphabet pool or the 5M cap; the fixture's `SetMaxTyperCharacters` / pinned `RaceTarget*` env values (`IntegrationTestFixture.cs:214-216, 865-890`) must be revisited — ⚠️ this is the suite's known flakiness area: with a bet-derived pool the expected-chars horizon shrinks, so caps chosen to *force* fallback must sit **below** the new, smaller natural-win expectation |

---

## 12. Acceptance criteria

(1:1 with the card's Acceptance Criteria checklist.)

1. Given a free-text race with bets placed, when the typer is constructed, then the character pool equals the union of the placed choices' characters topped up to at least ⌊alphabet/5⌋ distinct characters, sorted — and always contains every character of every placed bet's choice.
2. Given a lottery round, when the typer is constructed, then its character-pool derivation is byte-identical to today's behavior — only the pacing targets change.
3. Given the retuned config defaults, when `CalculatePacing` sizes the character cap, then the scheduled worst-case duration is ≤ 15 s by construction and `MaxCharacters` never exceeds the new lowered ceiling.
4. Given a batch of seeded simulated 2-bet free-text rounds (unit-level, deterministic), when run with the new pool rule, then the share of rounds decided by fallback is materially lower than with the full-alphabet pool (threshold pinned in the test).
5. Given a running round, when characters are generated, then terminal batches flush on the 50/40/30 ms cadence keyed to the current `DisplaySpeed` (configurable, code defaults), every generated character is delivered exactly once in order, and a full round emits on the order of hundreds — not tens of thousands — of SignalR messages.
6. Given a round whose run finishes, when the final status is written, then `Game.CharacterPool` and `Game.MaxCharacters` are persisted in the same `SaveChanges` as `WinningChoice`, and `GET /games/{id}/verification` publishes the persisted values.
7. Given a legacy round with NULL persisted pool/cap, when verification is requested, then the endpoint derives pool and cap with the pre-change rules so the offline recompute still reproduces the recorded winner.
8. Given the same `ServerSeed` and ordered bet seeds, when a round is replayed on any host, then the character stream, the fallback trigger (fixed character count) and the winner are identical — no wall-clock dependence anywhere in outcome decisions.
9. Given the happy-path integration test, when a low-bet free-text round runs end to end, then it resolves naturally with the shrunk pool (`WinnerDecidedByFallback = false`), the winner is rewarded, and the verification payload carries the persisted pool and cap consistently.
10. Given the local prod-mode Docker stack, when a batch of ≥20 rounds is measured (operational check, not a CI assert), then average round duration lands near 10 s and no round's typer run exceeds 15 s.
11. Given the card's optional cost-efficiency ask, when implementation completes, then a written CPU/Redis cost-reduction pointers section is delivered with zero code changes attached to it.

---

## 13. Manual QA

1. Run the local prod-mode stack (`docker-compose.prod.yml` overlay — see the deploy notes) and let
   ≥20 Cosmic Sprint rounds run with 2–3 bets each (the fill-crypto-clash flow or free bets in dev
   mode).
2. Measure per-round duration from `GameStatusLogs` (`InProgress` → `Ended`) or
   `Game.StartDate`/`EndDate`: mean ≈ 10 s, max ≤ 15 s.
3. Watch one round's terminal: characters stream smoothly, the `N AWAY!` row climbs, the fallback
   reveal still types the winner when it fires.
4. Open the FAIR panel after a round ends: commitment + recompute both pass; `COPY VERIFICATION
   JSON` shows the small pool.
5. Verify one **pre-change** round from Games History (if any exist locally): recompute still
   reproduces the winner.

---

## 14. Trello / pipeline

- **Source card:** [#181 Space Stroker typer refactoring](https://trello.com/c/Xim01Mvw/181-space-stroker-typer-refactoring)
  — list **Specs created**; the card's **Implementation** section lists branch, worktree and this spec.
- **Next:** `/monkify-implement-feature Space Stroker typer refactoring`, then
  `/monkify-ship-feature Space Stroker typer refactoring`.

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-09-01 | Initial spec from refined card #181 + same-session refinement decisions |

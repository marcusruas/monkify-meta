# Space Stroker — "Provably Fair Final" Feature — Implementation Spec

> **Status: Shipped 2026-07-23** — merged via `feature/provably-fair-final` (monkify-backend,
> monkify-front, monkify-meta). Distilled in `../features/provably-fair-final.md`.
>
> **Two decisions below did NOT ship as written:**
> 1. **`public/verify.html` was kept, not deleted.** The in-app panel runs code we ship; the standalone
>    file is the "don't trust us" path a player saves and runs offline. It gained a character-count
>    cross-check and a last-1,000-character download, and the FAIR tab links it with instructions. The
>    3-way algorithm duplication therefore **remains** — keep `ProvablyFair.cs`,
>    `src/utils/provablyFair.ts` and `public/verify.html` byte-identical.
> 2. **The worker is instantiated as a CLASSIC worker**, not `{ type: 'module' }` (§ frontend): the
>    module form loaded the chunk and died silently before `onmessage`. Worker payloads must also be
>    plain objects — Vue reactive Proxies fail structured clone.
>
> Refined via Trello card [Provably fair final](https://trello.com/c/RcJrKixz) (2026-07-22).
> Repos affected: `monkify-backend`, `monkify-front`, and this doc in `monkify-meta`.
>
> **Purpose:** Close the loop on the shipped provably-fair feature. Today a player *can* verify a
> round only by copy-pasting JSON from `GET /games/{id}/verification` into a separate
> `public/verify.html` page — nobody will. This card puts verification **in the app** as a `FAIR`
> side-panel tab that verifies the round the player just watched end, and makes the endpoint cheap
> enough to call freely: the server stops replaying the character stream (it persists the run's
> character count instead) and caches the immutable revealed payload.

---

## 1. Summary

Two halves, one card.

**Frontend — an in-app `FAIR` panel.** A third lazy tab in the existing `GameSidePanel` (next to
`CHAT` · `STATS`; on mobile inside the `CHAT·STATS` segment). It is **locked until the round ends**.
On unlock it runs two **instant** checks in the browser — the commitment hash and the player's own
bet seed — and offers an **opt-in** full outcome recompute that runs in a **Web Worker** with
progress and cancel. It retains the last ended round across the rollover so a slow recompute isn't
killed by the next round starting. `public/verify.html` is deleted.

**Backend — a cheap endpoint.** `GetGameVerificationHandler` currently replays the *entire*
character stream on every call (up to `MaxTyperCharacters = 5,000,000` draws) purely to compute
`winnerDecidedByFallback`. That replay is deleted: the run persists `Game.CharactersTyped` and the
flag is derived from it. The revealed payload is immutable, so it is cached via `ICacheService`.

| | Today | After this card |
| --- | --- | --- |
| How a player verifies | Copy JSON → open `verify.html` → paste → run | Open the `FAIR` tab; checks are already done |
| Outcome recompute | Only in `verify.html` (blocking, main thread) | Opt-in, in a Web Worker, cancellable, with progress |
| Server work per call | 1 game query + 1 bets query + **full stream replay** | 1 game + 1 bets query on first call; **0 DB, 0 HMAC** on repeats |
| `winnerDecidedByFallback` | Recomputed by replaying the stream | Derived from persisted `CharactersTyped` |
| Verification reach | Current round (via terminal) + manual JSON | Current round, unlocked at `Ended`, retained across rollover |

**Scope guard:** this is *not* a round-history feature. There is no list of past rounds, no game-id
lookup box, and no tokenless/public verification — see §9.

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo:** `feature/provably-fair-final` in `monkify-backend`, `monkify-front`, and
  `monkify-meta`; work lives in `.worktrees/provably-fair-final/<repo>`. The shared checkouts stay on
  `main` — never switch a hub's branch. Push after branch work.
- **Unit tests:** update/add in `SpaceStroker.Tests`; run via `scripts/run_tests.bat` (never the full
  `dotnet test` suite — `ProductionSessions` is a slow Monte Carlo suite). Report changed tests at the
  end of implementation.
- **Integration test is mandatory** — the card's **Level is `Feature`**, so
  `SpaceStroker.Tests.Integration` gets a happy-path test (§11.2).
- **EF mapping:** inline in `SpaceStrokerDbContext.OnModelCreating`; no `IEntityTypeConfiguration`.
- **Migrations:** under `SpaceStroker.Infrastructure/Migrations/`; applied **only** by
  `SpaceStroker.Consumers` at startup.
- **Secrets/config:** mock-only values in committed files.
- **Operator messaging:** vague errors only — an unknown game and another operator's game must remain
  indistinguishable; never reveal existence, ownership, or contract status.
- **Feature-specific:** the algorithm in `ProvablyFair.cs` and `src/utils/provablyFair.ts` must stay
  byte-for-byte identical. Nothing in this card changes the RNG, the commit-reveal scheme, the bet
  seed/nonce derivation, or the game mechanic.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | Scope = **one-click verify UX + a cheap endpoint**. Round history, tokenless/public verification, and "verify a round you didn't watch" are out of scope. |
| 2 | Reach = **current round only**, unlocked at `Ended`, plus rollover retention (#8). "Verify every round" means every round the player plays through. |
| 3 | Verification moves **into the SPA** as a **third lazy tab `FAIR`** in `GameSidePanel`, next to `CHAT` · `STATS`; on mobile it joins the existing `CHAT·STATS` segment's sub-tabs. |
| 4 | The `FAIR` tab is **hidden when embedded** (`useEmbed()` true) — regulated casino iframes get no in-frame fairness panel. |
| 5 | **Locked until `Ended`**: pre-reveal the tab shows only the commitment (`serverSeedHash`) and an "unlocks when this round ends" state. No checks run pre-reveal. |
| 6 | On unlock, **two instant checks** render with no replay: (a) `SHA256(UTF8(serverSeed)) === serverSeedHash`; (b) the player's own bet seed = `HMAC-SHA256(clientSeed, String(nonce))`. |
| 7 | The full outcome recompute is **opt-in** (`RECOMPUTE OUTCOME`), runs in a **Web Worker**, is **cancellable**, and reports progress sized by the new `charactersTyped`. A capped 5M-character round is never replayed unless the player asks. |
| 8 | **Rollover retention:** the tab keeps the last ended round's payload client-side across the rollover, labelled with its round id; an in-flight recompute survives. It is replaced only when the *next* round ends. |
| 9 | A **COPY VERIFICATION JSON** button exposes the raw payload for third-party verifiers. |
| 10 | **`public/verify.html` is deleted.** The panel replaces it and the 3-way byte-identical duplication (backend / `provablyFair.ts` / `verify.html`) collapses to 2. |
| 11 | Backend **stops replaying the stream**: persist `Game.CharactersTyped` (`int?`) at run end; derive `winnerDecidedByFallback` from it. |
| 12 | `charactersTyped` is added to `GameVerificationDto` so the client can size and estimate its opt-in replay. |
| 13 | The revealed payload is **cached** via `ICacheService` under `GameVerification:{gameId}`, ~12h TTL. See #14 for exactly when it is written. |
| 14 | **Spec-time refinement of #13 (see §6.3a):** the cache is written only once the game reaches a **final** status (`RewardForWinnersCompleted` / `ErrorWhenProcessingRewards`), not merely `Ended`. The DTO carries `Status`, and `Ended` → `RewardForWinnersInProgress` → `RewardForWinnersCompleted` happens seconds later; caching at `Ended` would serve a stale `status` forever. The transient window stays uncached — it is already cheap once the replay is gone. |
| 15 | Non-bettors and players who rotated their client seed get an **explicit informational** own-seed row — never a rendered failure. |
| 16 | Reveal gating, operator scoping, and the vague not-found message are **unchanged**. |
| 17 | **Level: `Feature`** → mandatory happy-path integration test + a dedicated `features/provably-fair-final.md` doc at ship time; `features/provably-fair.md` must also be corrected (this card reverses two of its shipped decisions — see §3a). |

### 3a. Decisions this card explicitly reverses

Both were deliberate in `specs/provably-fair.md` and are now superseded:

- *"the in-terminal 'Verify this round' recompute was intentionally dropped; the terminal only
  **displays** the commitment and the revealed seed"* → an in-app recompute now exists, in the `FAIR`
  panel, opt-in and worker-based. **The terminal itself is unchanged** — it keeps its
  `> SERVER SEED HASH:` / `> SERVER SEED:` lines.
- *"standalone offline `public/verify.html`"* → removed.

---

## 4. Data model

### 4.1 Entity change

`monkify-backend/src/SpaceStroker.Domain/Games/Entities/Game.cs` — one nullable column:

```csharp
// Number of characters the typer actually produced in this run, persisted when the game ends
// (GameService.UpdateGameStatus, alongside WinningChoice). Lets the verification endpoint report
// whether the fallback decided the winner WITHOUT replaying the whole stream, and lets the client
// size its opt-in recompute. Null for rounds that never ran (and for pre-migration rows).
public int? CharactersTyped { get; set; }
```

### 4.2 EF configuration (inline)

`monkify-backend/src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs`, inside the
existing `Game` block in `OnModelCreating` — a plain optional `integer`, no index (it is only ever
read by primary key on the game row):

```csharp
game.Property(x => x.CharactersTyped).IsRequired(false);
```

### 4.3 Migration

```bash
# run from monkify-backend/src (worktree), targeting the Consumers startup project
dotnet ef migrations add GameCharactersTyped \
  --project SpaceStroker.Infrastructure \
  --startup-project SpaceStroker.Consumers
```

Expected `Up`: `ADD COLUMN "CharactersTyped" integer NULL` on `Games`. `Down` drops it. **No
back-fill** — historical rows stay `null` (see §6.4 for how that is reported). Migrations are applied
only by `SpaceStroker.Consumers` at startup.

---

## 5. Configuration

**None.** No new `GeneralSettings` keys, env vars, or seed data. The cache TTL is a constant in
`CacheHelper` (consistent with `GenerateGameStatsCacheSettings`), and `MaxTyperCharacters` already
exists.

---

## 6. Backend behaviour

### 6.1 Persisting `CharactersTyped` (the write path)

`RunGameConsumer.ProcessMessage` already ends a run with:

```csharp
var monkey = await gameService.RunGame(game, CancellationToken.None);
await gameService.UpdateGameStatus(game, Ended, monkey);
```

`SpaceStrokerTyper.CharactersTypedCount` is public and final at that point, so **no consumer change is
needed**. Persist it in `GameService.UpdateGameStatus`, in the existing `if (monkey != null)` block
that already writes `WinningChoice` — same entity, same `SaveChangesAsync`, so a game can never be
observed as ended-with-no-count:

```csharp
if (monkey != null)
{
    game.WinningChoice = monkey.FirstChoiceTyped;
    _context.Entry(game).Property(x => x.WinningChoice).IsModified = true;

    // Provably-fair: record how many characters the run actually produced. The verification
    // endpoint derives winnerDecidedByFallback from this instead of replaying the stream, and the
    // client uses it to size its opt-in recompute. Written in the SAME SaveChanges as the winner.
    game.CharactersTyped = monkey.CharactersTypedCount;
    _context.Entry(game).Property(x => x.CharactersTyped).IsModified = true;
    ...
}
```

> **Note:** `UpdateGameStatus` wraps its body in `try/catch` and only logs on failure. That is
> existing behaviour and is safe here: if `SaveChangesAsync` throws, neither the status nor the count
> is written, so there is no partial state. Do not change the error handling in this card.

### 6.2 Deriving the fallback flag (delete the replay)

In `GetGameVerificationHandler`, **delete the entire private `WinnerWasDecidedByFallback` method**
(the `while (true)` keystream replay) and its call site. Replace with a derivation from the persisted
count and the same read-time `SpaceStrokerTyper` the handler already builds:

```csharp
if (isRevealed)
{
    dto.WinningChoice = game.WinningChoice;
    dto.CharactersTyped = game.CharactersTyped;

    // The run stopped either because a choice matched the rolling window (natural win) or because
    // it hit the character cap (fallback draw). No replay needed — compare the recorded count with
    // the cap this configuration produces. Null (pre-migration / never-run) => unknown, not false.
    dto.WinnerDecidedByFallback = game.CharactersTyped.HasValue
        ? game.CharactersTyped.Value >= typer.MaxCharacters
        : (bool?)null;
}
```

`GameVerificationDto.WinnerDecidedByFallback` is **already** `bool?`, so reporting "unknown" for
legacy rows needs no contract change.

> **Consistency gap found during exploration (documented, not a blocker).** `typer.MaxCharacters` is
> recomputed at read time from `GeneralSettings.Games` and `Bets.Count`. If those settings change
> between the run and a later verification, the recomputed cap — and therefore the derived flag —
> could disagree with the run. **This drift already exists today**: `maxCharacters` is published in
> the payload from the same read-time typer, and the deleted replay used that same recomputed cap.
> Deriving from it keeps the flag consistent with the `maxCharacters` the client replays against, so
> the panel and the flag can never contradict each other. Do **not** "fix" this by persisting the cap
> in this card — that would make the published `maxCharacters` and the flag disagree instead.

### 6.3 Caching the revealed payload

Add to `SpaceStroker.Infrastructure/Services/Cache/CacheHelper.cs`, following the existing style:

```csharp
// Per-game provably-fair verification payload. Only ever cached once the round is FINAL (rewards
// settled) — at that point every field, status included, is immutable, so no invalidation exists
// or is needed. Long TTL: the payload is the round's permanent record.
public static CacheSettings GenerateGameVerificationCacheSettings(Guid gameId)
    => new($"GameVerification:{gameId}", TimeSpan.FromHours(12));
```

`ICacheService` resolves to `RedisCacheService` when `ConnectionStrings:Redis` is configured and to
`MemoryCacheService` otherwise (`DependencyInjection.cs`) — no new registration.

`GetGameVerificationHandler` gains an injected `ICacheService` and wraps its work:

1. **Read** — `TryGet<GameVerificationDto>(settings, out var cached)`; on a hit return it immediately
   (no `GetGameById`, no `GetBetsByGameId`, no typer construction).
   **Operator scoping still applies:** the cache is keyed by game id only, so the handler must still
   confirm the caller's operator owns the game. Resolve this by doing the **operator check on the
   cached path too** — see §6.3a.
2. **Compute** — the existing path (game + bets + typer + DTO).
3. **Write** — `Set(settings, dto)` only when the payload is final and complete (§6.3a).

### 6.3a Exact cache-write predicate, and the operator-scoping order

Write the cache **only** when all of these hold:

```csharp
var isFinal = game.Status is GameStatus.RewardForWinnersCompleted
                          or GameStatus.ErrorWhenProcessingRewards;

if (isFinal && !string.IsNullOrEmpty(game.ServerSeed) && game.CharactersTyped.HasValue && bets.Count > 0)
    _cache.Set(cacheSettings, dto);
```

- **Final status, not merely `Ended`** (decision #14): the DTO carries `Status`, and a round moves
  `Ended` → `RewardForWinnersInProgress` → `RewardForWinnersCompleted` within seconds. Caching at
  `Ended` would pin a stale `status` for 12h. The uncached window is tiny and, with the replay gone,
  costs only two indexed queries.
- **Seed + count populated** — never cache a payload that is missing the reveal.
- **Bets present** — a round with no bets returns the early "nothing to verify" DTO; not worth a key.

**Operator scoping must not be bypassed by the cache.** The cache key is per game (correct — the
payload is operator-independent), so the handler must still prove ownership before returning a hit.
Implement it in this order:

1. `var game = await _gameService.GetGameById(request.GameId);`
2. Validate `game != null && game.Parameters?.OperatorId == request.OperatorId`, else the existing
   vague `ErrorMessages.GameNotValidForBets` failure — **unchanged**.
3. *Then* check the cache; on a hit, return the cached DTO (this skips only
   `GetBetsByGameId` + typer construction + DTO assembly).

That keeps the "0 HMAC, no bets query, no DTO rebuild" win while making it **impossible** for a
cached entry to leak a round across operators. The card's "0 DB queries on repeat" target is
therefore met for the expensive half (the bets query and the replay); the single indexed
`GetGameById` lookup remains, and it is the security check. **This is a deliberate, security-driven
deviation from the card's wording — call it out at implementation review.**

### 6.4 What the endpoint returns (unchanged except two fields)

| Field | Behaviour |
| --- | --- |
| `serverSeedHash` | Unchanged — present from the first bet. |
| `serverSeed` | Unchanged — only when `Status ∈ GameEndedStatus`. |
| `winningChoice` | Unchanged — present when revealed. |
| `winnerDecidedByFallback` | **Derived** from `CharactersTyped` (§6.2); `null` when unknown. |
| `charactersTyped` | **New** — `int?`, present when revealed and recorded. |
| `characterPool`, `queueLength`, `maxCharacters`, `orderedBetSeeds`, `orderedChoices`, `bets` | Unchanged. |

DTO addition in `SpaceStroker.Infrastructure/Contracts/Games/GameVerificationDto.cs`:

```csharp
/// <summary>How many characters the run actually produced. Present once revealed and recorded;
/// null for pre-migration rounds. The client uses it to size/estimate its opt-in recompute.</summary>
public int? CharactersTyped { get; set; }
```

---

## 7. Frontend behaviour

### 7.1 `GameFairPanel.vue` — the FAIR tab

New `monkify-front/src/components/game/GameFairPanel.vue`, modelled on `GameStatsPanel.vue` (an
`active: boolean` prop, lazy fetch on first activation, same CRT panel styling).

**States**

| State | When | Renders |
| --- | --- | --- |
| Locked | round not in an ended status | `> SERVER SEED HASH: …` (from `gameInstance.serverSeedHash`) + `VERIFICATION UNLOCKS WHEN THIS ROUND ENDS`. No fetch, no checks. |
| No bets | revealed payload has no bets / no seed | `NO BETS THIS ROUND — NOTHING TO VERIFY.` No recompute button. |
| Unlocked | payload fetched with `serverSeed` present | The two instant checks + the recompute affordance + COPY JSON (§7.2–7.4). |
| Error | fetch failed | A retry affordance; never a "verification failed" claim (a network error is not a fairness failure). |

**Fetch:** on first activation *after* the round reaches an ended status, call
`mainService.getGameVerification(gameId)` once and store the payload. The panel owns this fetch;
`GameTerminal`'s existing `revealServerSeed()` is left alone (decision #3a — the terminal is
unchanged; the duplicate call is one cached request and not worth coupling the two components).

### 7.2 Instant checks (no replay)

Both run on the main thread immediately when the payload lands — they are a handful of WebCrypto
calls.

1. **Commitment** — `sha256Hex(payload.serverSeed) === payload.serverSeedHash`
   → `COMMITMENT ✓ SHA256 MATCHES` / a loud failure row.
2. **Your bet seed** — `useClientSeed()` **already exposes `clientSeed` as a computed**, so *no
   change to the hook is required* (the card's "expose the stored seed" line is already satisfied).
   For each `bets[i].nonce`, recompute `hmacSha256Hex(clientSeed, String(nonce))` and compare with
   `orderedBetSeeds[i]` — the two arrays share the canonical order, so index `i` lines up.
   - ≥1 match → `YOUR SEED ✓ HMAC(clientSeed, <nonce>)` (list the matched choices).
   - 0 matches → the **informational** row, never a failure (decision #15):
     `NO VERIFIABLE BET FROM THIS BROWSER` — with the sub-line
     `(spectator, or the client seed was rotated since this bet)`.

> Rotating the client seed resets the nonce to 0, so nonce values repeat across rotations. A match is
> proof the player's entropy was used unmodified; a non-match is **not** an accusation and the copy
> must not imply one.

### 7.3 Opt-in recompute (Web Worker)

`RECOMPUTE OUTCOME` starts the full deterministic replay off the main thread.

- **New worker** `monkify-front/src/workers/verifyRound.worker.ts`. It receives the payload, runs the
  replay, posts `{type:'progress', typed}` periodically and `{type:'done', result}` at the end.
- **`verifyRound` gains an optional progress callback** in `src/utils/provablyFair.ts`:
  `verifyRound(payload, onProgress?: (typed: number) => void)`, invoked every ~50k characters. The
  algorithm is otherwise untouched (it must stay byte-identical to `ProvablyFair.cs`).
- **Progress is by characters, not bytes** — rejection sampling means bytes consumed ≠ characters
  produced. Denominator = `charactersTyped` when present, else `maxCharacters`.
- **Cancel = `worker.terminate()`**, so no cancellation token has to thread through the algorithm.
  Terminate on: the cancel button, panel unmount, and leaving the game screen.
- **Up-front warning** when `charactersTyped` is large (threshold: ≥ 250k characters): show the count
  and a rough estimate before starting, e.g.
  `THIS ROUND TYPED 4,213,880 CHARACTERS — RECOMPUTE MAY TAKE ~30s.` Derive the estimate from a
  measured constant (~160k HMAC calls per 5M characters); a rough order-of-magnitude figure is fine.
- **Result rows:** `OUTCOME ✓ "cosmic" MATCHES` / a loud `OUTCOME ✗ MISMATCH` (never swallowed, never
  auto-retried), plus `DECIDED BY: NATURAL MATCH | FALLBACK DRAW`.

> **Implementation risk (flagged, with a fallback).** This is the **first Web Worker in
> `monkify-front`**. The app builds on Vue CLI 5 / webpack 5, which supports
> `new Worker(new URL('@/workers/verifyRound.worker.ts', import.meta.url), { type: 'module' })`
> natively. If that bundling fights the existing `vue.config.js` (it sets `crypto: false` and other
> Node polyfill fallbacks — note `crypto.subtle` is a *browser* global and is unaffected), fall back
> to a **chunked main-thread replay** that yields with `await new Promise(r => setTimeout(r))` every
> ~50k characters. That keeps the UI responsive and satisfies the spirit of the AC; if you take the
> fallback, say so explicitly in the implementation report so the AC can be amended rather than
> silently reinterpreted.

### 7.4 Copy JSON

`COPY VERIFICATION JSON` writes `JSON.stringify(payload, null, 2)` to the clipboard via
`navigator.clipboard.writeText`, with a brief `COPIED` confirmation. The payload is copied
**unchanged** — it is what a third-party verifier consumes.

### 7.5 Rollover retention

The panel holds `verifiedRound = { gameId, payload }` independently of the live `gameInstance`.

- When the live round reaches an ended status and its id differs from `verifiedRound.gameId`, fetch
  and **replace** — this is the only thing that clears a retained round.
- A rollover to a new *running* round does **not** clear it and does **not** terminate a running
  worker.
- The header labels which round is shown whenever it is not the live one, e.g.
  `FAIR — ROUND 3f2a…c91 (ENDED 14:02)` plus a muted `A NEW ROUND IS NOW RUNNING.`

### 7.6 Tab wiring

`GameSidePanel.vue`:

- `activeTab` type widens: `ref<'chat' | 'stats' | 'fair'>('chat')`; `openTab()` likewise.
- Add a `FAIR` button to **both** tab bars (mobile sub-tabs and the desktop header) and a third
  vertical label to the **collapsed dock rail**, matching the existing markup exactly.
- Render `<game-fair-panel v-show="activeTab === 'fair'" :active="… && activeTab === 'fair'" />`
  alongside the other two — `v-show`, never `v-if`, so a running recompute is not unmounted by a tab
  switch.
- **Embed gate:** compute `const { isEmbedded } = useEmbed()` and wrap every FAIR affordance in
  `v-if="!isEmbedded"` (both tab bars, the dock rail label, and the panel itself), so the tab does not
  exist when embedded. If `activeTab` is somehow `'fair'` while embedded, fall back to `'chat'`.

`GameMobileTabs.vue` is **unchanged** — FAIR lives inside the existing `CHAT·STATS` segment's
sub-tabs, so the top-level segmented control keeps its two options.

### 7.7 Types and deletions

- `src/types/index.ts` — add `charactersTyped?: number;` to `GameVerification`.
- `src/utils/provablyFair.ts` — add `charactersTyped?: number` to `VerificationPayload`, add the
  progress callback (§7.3), and **update the header comment**: it currently says
  `public/verify.html duplicates this logic verbatim … keep the two in sync` — that sentence must go,
  replaced by a note that the worker is the only other consumer.
- **Delete `monkify-front/public/verify.html`.**
- `GameTerminal.vue` — its two comments reference "the standalone /verify.html"; update the wording to
  point at the FAIR panel. **No behavioural change to the terminal.**

---

## 8. Flow diagram

```mermaid
sequenceDiagram
    participant P as Player (FAIR tab)
    participant W as Verify Worker
    participant API as GamesController
    participant C as ICacheService
    participant DB as Postgres

    Note over P: round reaches Ended → tab unlocks
    P->>API: GET /games/{id}/verification
    API->>DB: GetGameById (operator check — always)
    alt cached (round is final)
        API->>C: TryGet GameVerification:{id}
        C-->>API: payload (0 bets query, 0 HMAC)
    else not cached
        API->>DB: GetBetsByGameId
        API->>API: build typer + DTO; fallback = CharactersTyped >= MaxCharacters
        opt status is final
            API->>C: Set GameVerification:{id} (12h)
        end
    end
    API-->>P: payload (+ serverSeed, charactersTyped)

    P->>P: instant: SHA256(serverSeed) == hash
    P->>P: instant: HMAC(clientSeed, nonce) vs orderedBetSeeds
    Note over P: outcome NOT recomputed yet

    opt player presses RECOMPUTE OUTCOME
        P->>W: postMessage(payload)
        loop every ~50k characters
            W-->>P: {progress, typed}
        end
        W-->>P: {done, recomputedWinner, ok}
    end
    opt cancel / unmount / leave screen
        P->>W: terminate()
    end
```

---

## 9. Out of scope

- **Round history** — no listing of past rounds, no per-player round list, no game-id paste box, no
  "verify yesterday's round".
- **Public / tokenless verification** — the endpoint stays launch-token + operator scoped.
- Any change to the **RNG, commit-reveal scheme, bet seed/nonce derivation, or the game mechanic**.
- The FAIR panel in **embedded** contexts (deliberately hidden).
- A prefilled or deep-linked `verify.html` — the file is deleted instead.
- **Back-filling `CharactersTyped`** for historical rounds (not derivable without a replay).
- Changing `GameTerminal`'s commitment/reveal lines, or `UpdateGameStatus`'s error handling.

> **Accepted risk carried from refinement:** with current-round-only reach *and* `verify.html` gone, a
> player can only verify the round they watched end, in that browser tab. Re-checking an older round
> would need a follow-up card (round history + a durable verification surface).

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/SpaceStroker.Domain/Games/Entities/Game.cs` | Add `public int? CharactersTyped { get; set; }` with the explanatory comment. |
| 2 | `src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` | Inline mapping: optional `integer`, no index. |
| 3 | `src/SpaceStroker.Infrastructure/Migrations/*_GameCharactersTyped.cs` | New migration adding the nullable column. |
| 4 | `src/SpaceStroker.Infrastructure/Services/Games/GameService.cs` | In `UpdateGameStatus`, persist `monkey.CharactersTypedCount` in the same `SaveChanges` as `WinningChoice`. |
| 5 | `src/SpaceStroker.Infrastructure/Contracts/Games/GameVerificationDto.cs` | Add `CharactersTyped` (`int?`). |
| 6 | `src/SpaceStroker.Infrastructure/Handlers/Games/GetGameVerification/GetGameVerificationHandler.cs` | **Delete** `WinnerWasDecidedByFallback` + its call; derive the flag; inject `ICacheService`; read-after-operator-check, write-when-final. |
| 7 | `src/SpaceStroker.Infrastructure/Services/Cache/CacheHelper.cs` | Add `GenerateGameVerificationCacheSettings(Guid gameId)` (12h). |
| 8 | `src/SpaceStroker.Tests/UnitTests/Handlers/GetGameVerificationHandlerTests.cs` | Extend — derived flag, unknown-when-null, cache read/write, operator scoping (§11.1). |
| 9 | `src/SpaceStroker.Tests.Integration/ProvablyFairFlowTests.cs` | Add the mandatory happy-path verification test (§11.2). |

> `RunGameConsumer.cs` needs **no change** — it already passes the typer to `UpdateGameStatus`.

### 10.2 Frontend (`monkify-front`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/components/game/GameFairPanel.vue` | **New.** The FAIR tab: locked/no-bets/unlocked/error states, instant checks, recompute, copy JSON, rollover retention. |
| 2 | `src/workers/verifyRound.worker.ts` | **New.** Runs `verifyRound` off the main thread; posts progress + result. |
| 3 | `src/utils/provablyFair.ts` | Add the optional progress callback + `charactersTyped` to `VerificationPayload`; rewrite the `verify.html` sync-warning comment. |
| 4 | `src/components/game/GameSidePanel.vue` | Third `FAIR` tab in both tab bars + the dock rail; `activeTab` widened; `useEmbed()` gate; `v-show` mount. |
| 5 | `src/types/index.ts` | `GameVerification.charactersTyped?: number`. |
| 6 | `src/components/game/GameTerminal.vue` | Comment-only: stop pointing at `/verify.html`. |
| 7 | `public/verify.html` | **Delete.** |

> `src/hooks/useClientSeed.ts` needs **no change** (`clientSeed` is already exposed).
> `src/components/game/GameMobileTabs.vue` needs **no change**.
> `src/services/mainService.ts` needs **no change** (`getGameVerification` already exists).

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/provably-fair-final.md` | This spec. |
| 2 | `INDEX.md` | Spec row (status **Ready**) + "Feature currently in flight". |
| 3 | `features/provably-fair-final.md` | **At ship time** (Level = Feature). |
| 4 | `features/provably-fair.md` | **At ship time** — correct the two reversed decisions (§3a) and the `verify.html` references. |

---

## 11. Tests

### 11.1 Unit tests — `monkify-backend/src/SpaceStroker.Tests`

Run with `scripts/run_tests.bat`.

| Test file | Test case | Expected |
| --- | --- | --- |
| `UnitTests/Handlers/GetGameVerificationHandlerTests.cs` | Revealed round with `CharactersTyped` **below** the cap | `WinnerDecidedByFallback == false`, `CharactersTyped` echoed |
| " | Revealed round with `CharactersTyped` **at/above** the cap | `WinnerDecidedByFallback == true` |
| " | Revealed round with `CharactersTyped == null` (legacy) | `WinnerDecidedByFallback == null` — **not** `false` |
| " | Not-yet-ended round | `serverSeed` null, `charactersTyped` null, **nothing written to the cache** |
| " | Second call for a **final** round | Served from cache; `GetBetsByGameId` **not** called again |
| " | Second call for a round still in transient `Ended` | **Not** cached; recomputed (guards decision #14) |
| " | Cached round requested by a **different operator** | Vague `GameNotValidForBets` failure — cache never bypasses the operator check |
| " | Round with **zero bets** | Early DTO, no pool/seeds, nothing cached |
| `UnitTests/Services/GameServiceTests.cs` (or existing equivalent) | `UpdateGameStatus(game, Ended, monkey)` | `game.CharactersTyped == monkey.CharactersTypedCount`, persisted with `WinningChoice` |
| " | `UpdateGameStatus` to a non-ended status (`monkey == null`) | `CharactersTyped` untouched |
| `UnitTests/Domain/Games/ProvablyFairTests.cs` | Existing keystream/fallback tests | **Unchanged** — the algorithm must not move |

### 11.2 Integration test (MANDATORY — Level = `Feature`)

`monkify-backend/src/SpaceStroker.Tests.Integration/ProvablyFairFlowTests.cs`, extending the existing
Testcontainers harness (RG operator, launch tokens, SignalR):

**Happy path, end to end:**
1. Launch a game type, place bet(s) with a derived `Seed` + `Nonce`, let the round run to completion.
2. `GET /games/{id}/verification` →
   - `serverSeed` revealed and `SHA256(serverSeed) == serverSeedHash`;
   - `charactersTyped` present and `> 0`;
   - `winnerDecidedByFallback` non-null and consistent with `charactersTyped >= maxCharacters`.
3. **Independently replay** the published inputs (`ProvablyFair` primitives, exactly as a player's
   verifier would) and assert the reproduced winner equals the round's `winningChoice`.
4. Call the endpoint a **second time** and assert the payload is identical (cache hit path) — assert
   on the observable result, not on internal call counts.

---

## 12. Acceptance criteria

Mapped 1:1 from the Trello **Acceptance Criteria** checklist (19 items).

1. Given a round that has finished running, when the run completes, then `Game.CharactersTyped` is persisted with the typer's final character count in the same unit of work that sets the ended status/winner.
2. Given an Ended round, when `GET /games/{id}/verification` is called, then the response includes `charactersTyped` and `winnerDecidedByFallback` derived as `(CharactersTyped >= MaxCharacters)`, and the handler performs no keystream replay.
3. Given an Ended round whose verification payload was already requested once, when the endpoint is called again, then the response is served from the cache key `GameVerification:{gameId}` with no bets query or DTO rebuild, and is identical to the first response. *(Spec refinement §6.3a: "Ended" means the round has reached a **final** status, and one indexed `GetGameById` lookup remains as the operator-scoping check.)*
4. Given a round that has not yet Ended (or whose `ServerSeed`/`CharactersTyped` are not populated), when the verification endpoint is called, then the response is **not** written to the cache and the raw `serverSeed` is omitted.
5. Given a game id that does not exist or belongs to another operator, when the verification endpoint is called, then the same vague not-found/unavailable failure is returned as today and nothing is cached.
6. Given a player on the game screen in a standalone (non-embedded) context, when they open the right-side panel, then a third `FAIR` tab is available alongside `CHAT` and `STATS`, and on mobile it appears within the `CHAT·STATS` segment.
7. Given the game is running inside an iframe (`useEmbed()` true), when the game screen renders, then the `FAIR` tab is not shown at all.
8. Given a round that has not yet Ended, when the player opens the `FAIR` tab, then it shows only the server seed hash commitment plus an "unlocks when this round ends" state, and runs no checks.
9. Given the round has Ended, when the player opens the `FAIR` tab, then `SHA256(UTF8(serverSeed)) == serverSeedHash` is computed in the browser and rendered as pass/fail immediately, without any stream replay.
10. Given a player who bet in the ended round from this browser with their current client seed, when the `FAIR` tab unlocks, then a "your bet seed" row shows a pass, proving `Bet.Seed == HMAC-SHA256(clientSeed, nonce)`.
11. Given a spectator who did not bet, or a player who rotated their client seed after betting, when the `FAIR` tab unlocks, then the own-seed row shows an explicit informational message and is never rendered as a failed check, while the commitment check and the opt-in recompute remain available.
12. Given the `FAIR` tab is unlocked, when the player presses `RECOMPUTE OUTCOME`, then the full deterministic replay runs in a Web Worker (never on the main thread), shows a progress indicator sized by `charactersTyped`, and reports MATCH or MISMATCH against the published `winningChoice` plus whether the fallback decided it.
13. Given a recompute is running, when the player cancels it, closes the panel, or leaves the game screen, then the worker is terminated and no further CPU work happens.
14. Given a recompute is in progress and the game rolls over to a new round, when the new round starts, then the `FAIR` tab keeps showing the retained ended round (labelled with its round id) and the recompute continues uninterrupted; the retained round is only replaced when the next round Ends.
15. Given a round whose `charactersTyped` is very large (up to the 5,000,000 cap), when the player is about to recompute, then the panel warns up front with an estimated duration before starting, and the recompute remains cancellable throughout.
16. Given the `FAIR` tab is unlocked, when the player presses `COPY VERIFICATION JSON`, then the raw verification payload is copied to the clipboard unchanged.
17. Given an ended round in which no bets were placed, when the player opens the `FAIR` tab, then it states there is nothing to verify and offers no recompute.
18. Given the shipped frontend build, when the app is searched, then `public/verify.html` no longer exists and nothing links to it, while `src/utils/provablyFair.ts` remains the single frontend implementation of the algorithm.
19. Given the integration suite, when a round is played end to end and then verified, then the payload reveals the server seed, reports `charactersTyped` and the correct fallback flag, and independently replaying the published inputs reproduces the round's `winningChoice`.

---

## 13. Manual QA

Local Docker stack (`scripts/setup_local_environment.bat`), seeded dev operator:

1. Open the 1-minute preset lottery (`6L Minute Menagerie`) — fast rollover, ideal for AC 14.
2. Open `FAIR` **before** the round ends → locked state + commitment only (AC 8).
3. Place a free bet, let the round end → tab unlocks; both instant checks render immediately, own-seed row passes (AC 9, 10).
4. Press `RECOMPUTE OUTCOME` → progress advances, result matches; UI stays responsive throughout (AC 12).
5. Start a recompute and wait for the rollover → the tab stays on the retained round and the recompute finishes (AC 14).
6. Rotate the client seed in the gear modal, reload, re-open `FAIR` on a fresh ended round → informational own-seed row, no failure styling (AC 11).
7. Open in a second browser without betting → spectator path (AC 11).
8. `COPY VERIFICATION JSON` → paste into an editor; valid JSON matching the API (AC 16).
9. Load the embed example page → no `FAIR` tab anywhere (AC 7).
10. Hit `GET /games/{id}/verification` twice for a settled round and compare payloads + latency (AC 3).
11. Confirm `/verify.html` 404s on the built frontend (AC 18).

---

## 14. Trello / pipeline

- **Source card:** [Provably fair final](https://trello.com/c/RcJrKixz) (#82) — list **Specs created**;
  the **Implementation** section on the card carries branch, worktree, spec path, and **Level: Feature**.
- **Branch:** `feature/provably-fair-final` in `monkify-backend`, `monkify-front`, `monkify-meta`.
- **Worktree:** `.worktrees/provably-fair-final/<repo>`.
- **Next:** `/monkify-implement-feature Provably fair final` → then
  `/monkify-ship-feature Provably fair final`.

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-07-22 | Initial spec from the refined card (#82) + code exploration. Documents two exploration findings that refine the card: the cache must be written only at a **final** status (the DTO carries `Status`), and the operator check must run **before** the cache read (the key is per-game). |

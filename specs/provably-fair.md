# Monkify — "Provably Fair" Feature — Implementation Spec

> **Status: Shipped** — merged to `main` via branch `feature/provably-fair` on 2026-07-10 (Trello
> [#42 XWAeIgIQ](https://trello.com/c/XWAeIgIQ)). Distilled summary: `../features/provably-fair.md`.
> Repos affected: `monkify-backend`, `monkify-front`, this doc in `monkify-meta`, and the
> out-of-repo legal PDF under `Paperwork/Legal documents/` (see §9 caveat).
>
> **Post-ship notes (deviations from the spec body below):**
> - The **in-terminal "Verify this round" recompute (§7.3) was dropped** at the user's request:
>   verification lives only in the backend endpoint (§6.4) and the standalone `public/verify.html`
>   (§7.4). `GameTerminal` still shows the `> SERVER SEED HASH:` commitment during betting and the
>   `> SERVER SEED:` reveal after `Ended` (auto-fetched from the verification endpoint). Spec AC14 is
>   intentionally not shipped; AC13/AC15 hold.
> - Bundled an unrelated **`GameTypeCard` price fix** — the sessions-list price now mirrors the in-game
>   `> BET:` label (`formatAmount`: token symbol for `SolanaToken`, display-currency USD for `Standard`)
>   instead of the hardcoded `$<n> MONKI`.
> - The **legal PDF** (`Paperwork/Legal documents/04 Fairness & game rules.pdf`) was regenerated to the
>   new scheme (out-of-repo; not part of the merge).
>
> **Purpose:** Make Monkify's advertised "provably fair" claim literally true. Today the outcome is
> only *replayable* from a truncated `Int32` seed generated **after** bets lock, using
> `System.Random` — which is grindable, non-portable, and unverifiable by a player. This feature adds
> a **commit-reveal server seed**, a **portable HMAC-SHA256 RNG** any language can reproduce,
> **player-controlled (Stake-style) client seeds + nonce**, a reveal-gated **verification endpoint**,
> and an **in-app + standalone player verifier**.

---

## 1. Summary

We replace the seed/RNG core of the game and expose everything a player needs to verify a round.

| Aspect | Today | After this feature |
| --- | --- | --- |
| **Server entropy** | 32 bytes generated at typer construction (**after** bets lock), in `MonkifyTyper.GenerateSessionSeed` | Raw 32-byte `ServerSeed` generated and its `ServerSeedHash = SHA256` **committed on the first bet**, immutably |
| **Commitment** | None | `ServerSeedHash` published during betting; raw `ServerSeed` revealed only after `Ended` |
| **Persisted seed** | Derived `Int32` in `Session.Seed` (`int?`) | `Session.ServerSeed` (raw hex) + `Session.ServerSeedHash` (SHA-256 hex); `Session.Seed` **removed** |
| **RNG** | `new System.Random(Int32)` — not portable | **HMAC-SHA256 counter keystream** over `serverSeed ‖ sessionId ‖ orderedBetSeeds ‖ counter`, indices via **rejection sampling** |
| **Client seed** | `crypto.randomUUID()` inline at bet time, never shown/recorded | **Persistent, settable/rotatable** client seed (localStorage) + per-bet **nonce**; `Bet.Seed = HMAC-SHA256(clientSeed, nonce)` |
| **Inputs exposed** | None (only the derived int) | New reveal-gated `GET /sessions/{id}/verification`; `ServerSeedHash` on the live session DTO |
| **Verifier** | None | In-app "Verify this round" panel + standalone offline `public/verify.html` |

**The game mechanic is unchanged** — the monkey still streams characters and the first bet choice to
appear in the rolling window still wins. Only the *source of randomness* changes (from `System.Random`
to the HMAC keystream) and the *fallback winner* is drawn from the same primitive. Pacing, SignalR
streaming, pot/reward math, payment flows, and operator scoping are untouched.

**Two verification layers:**

1. **Outcome (anyone):** from `ServerSeed` + all `Bet.Seed`s (canonically ordered) + the character
   pool + cap, reproduce the exact character stream → the reproduced winning choice must equal
   `Session.WinningChoice`, and `SHA256(ServerSeed)` must equal the pre-published `ServerSeedHash`.
2. **Entropy (the bettor):** privately confirm `Bet.Seed == HMAC-SHA256(myClientSeed, nonce)` — proof
   the player's own entropy was mixed in unmodified. The raw `clientSeed` is **never** sent to or
   returned by Monkify (privacy: it does not link a player's bets).

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo:** work lives in `.worktrees/provably-fair/<repo>`; the shared checkouts stay on
  `main`. Branch `feature/provably-fair` in `monkify-backend`, `monkify-front`, `monkify-meta` (already
  created + pushed).
- **Unit tests:** update/add in `Monkify.Tests`; run via `scripts/run_tests.bat` (never the full
  `dotnet test` — `ProductionSessions` is a slow Monte-Carlo suite). **Report changed/added tests** at
  the end of implementation.
- **Integration tests (mandatory — this is a new feature that changes session flow + outcome
  generation):** add at least one happy-path test to `Monkify.Tests.Integration` (Testcontainers),
  run via `scripts/run_integration_tests.bat`, plus a dedicated test for the **fallback-winner** flow
  (a particularly complex flow) — AGENTS.md rule 8.
- **EF mapping:** inline in `MonkifyDbContext.OnModelCreating`; no `IEntityTypeConfiguration`.
- **Migrations:** add under `Monkify.Infrastructure/Migrations/`; applied **only** by
  `Monkify.Consumers` at startup. Never hand-edit `MonkifyDbContextModelSnapshot.cs`.
- **Secrets/config:** mock/placeholder values only; `ServerSeed` is per-session runtime data (not a
  configured secret) — never a config key.
- **API stays stateless / does not migrate.** The commit-on-first-bet and the verification read
  endpoint both run in `Monkify.Api`; the typer/outcome still runs in `Monkify.Consumers`.
- **Operator messaging:** unchanged — vague errors for inactive operators; never reveal contract
  status. The verification endpoint is operator-scoped like every other `/sessions/*` call.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **Commit point = first bet.** On the first bet of a session, generate a raw 32-byte `ServerSeed` and persist `ServerSeedHash = SHA256(ServerSeed)` **atomically, first-writer-wins**, never regenerated (mirrors `TryStartSession`'s conditional `UPDATE`). Rationale: no seeds minted for rolled-over sessions that never receive a bet. |
| 2 | **First-bettor nuance (accepted).** The first bettor sees no pre-existing commitment (none exists until their bet creates it). Their guarantee rests on (a) their own recorded client seed and (b) the post-round reveal. Later bettors + observers get the published hash during betting. Grinding is still prevented: post-commit the server can't change `ServerSeed`, and it cannot know future bettors' seeds. |
| 3 | **Reveal timing.** `ServerSeedHash` is exposed as soon as it exists; the raw `ServerSeed` is returned **only** once `Session.Status ∈ SessionEndedStatus`. |
| 4 | **Portable RNG = HMAC-SHA256 counter keystream** (see §6.3 for the exact, language-agnostic algorithm). Indices into the character pool via **rejection sampling** (no modulo bias). Canonical bet ordering: **`CreatedDate` asc, tie-break `Id` ordinal**. Everything operates on UTF-8 bytes of canonical **strings** (hex seeds, decimal counter) to remove byte-layout ambiguity across languages. |
| 5 | **Fallback winner** is drawn from the same primitive via a dedicated `"fallback"`-labelled HMAC (reproducible regardless of how many characters were streamed). |
| 6 | **Client seed = Stake-style.** Persistent client seed in `localStorage` (`monkify.clientSeed`), global-until-rotated, viewable/settable/rotatable in the header gear modal. Per-bet **nonce** (`monkify.clientSeed.nonce`) increments each bet. `Bet.Seed = HMAC-SHA256(key=clientSeed, msg=nonce)` (lowercase hex, 64 chars), computed client-side; the raw `clientSeed` never leaves the browser. |
| 7 | **Verification exposure = new endpoint** `GET /sessions/{id}/verification` (reveal-gated). `ServerSeedHash` is additionally added to the live session DTO (`ActiveSessionDto`) so the terminal can show the commitment during betting. |
| 8 | **Verifier = both**: an in-app "Verify this round" panel (recomputes outcome fully client-side) **and** a self-contained static `public/verify.html` (offline, paste inputs, reproduce). |
| 9 | **Migration = greenfield reshape** (no prod data): drop `Session.Seed` (`int?`); add `Session.ServerSeed` + `Session.ServerSeedHash`; add `Bet.Nonce`; widen `Bet.Seed` `HasMaxLength(40 → 64)`. Historical rows aren't back-fillable — acceptable. |
| 10 | **Legal doc in scope**: regenerate `Paperwork/Legal documents/04 Fairness & game rules.pdf` via `generate_04_fairness.py` (§9 caveat: out-of-repo). |

---

## 4. Data model

### 4.1 Entity / column changes

**`Monkify.Domain/Sessions/Entities/Session.cs`**

- **Remove:** `public int? Seed { get; set; }`
- **Add:**
  ```csharp
  // Provably-fair commitment. Committed on the first bet (see SessionService.EnsureServerSeedCommitted).
  // ServerSeed = lowercase hex of 32 random bytes (64 chars); null until the first bet.
  public string? ServerSeed { get; set; }
  // ServerSeedHash = SHA256(UTF8(ServerSeed-hex)) lowercase hex (64 chars). Published during betting;
  // ServerSeed itself is only revealed by the API after the session Ends.
  public string? ServerSeedHash { get; set; }
  ```

**`Monkify.Domain/Bets/Entities/Bet.cs`**

- **Add:**
  ```csharp
  // Stake-style per-bet nonce. Bet.Seed = HMAC-SHA256(clientSeed, Nonce) (computed client-side).
  // Lets the bettor prove their own entropy without exposing the raw client seed.
  public long Nonce { get; set; }
  ```
- `Seed` stays `string` but is now a 64-char HMAC hex (was a 36-char UUID). Extend the constructor at
  `Bet.cs:26` to accept `nonce` (and set it), or set `Nonce` in `RegisterBetHandler` right after
  construction — pick one and keep the free-bet path setting it too.

### 4.2 EF configuration (inline — `MonkifyDbContext.OnModelCreating`)

**`Session` block (`MonkifyDbContext.cs:134`):** the removed `Seed` was mapped by convention (no
explicit line) — deleting the property drops the column. Add:

```csharp
builder.Property(x => x.ServerSeed).HasMaxLength(64);
builder.Property(x => x.ServerSeedHash).HasMaxLength(64);
```

**`Bet` block (`MonkifyDbContext.cs:167`):**

```csharp
builder.Property(x => x.Seed).IsRequired().HasMaxLength(64);   // was HasMaxLength(40)
builder.Property(x => x.Nonce).IsRequired();                   // default 0
```

### 4.3 DTO length guard (hidden blocker)

**`RegisterBetRequestBody.cs`** — `Seed` is `[MaxLength(40)]`; an HMAC hex seed is **64 chars** and
would be rejected. Change to `[MaxLength(64)]` and add the nonce field:

```csharp
[Required(ErrorMessage = "Seed is required.")]
[MaxLength(64, ErrorMessage = "Seed's max length is 64 characters")]
public string? Seed { get; set; }

// Stake-style per-bet nonce (client-managed). Required; defaults handled client-side.
public long Nonce { get; set; }
```

### 4.4 Migration

```bash
# from the backend worktree, in src/
dotnet ef migrations add ProvablyFair \
  --project Monkify.Infrastructure --startup-project Monkify.Consumers
```

Expected operations: drop `Sessions.Seed`; add `Sessions.ServerSeed`, `Sessions.ServerSeedHash`; add
`Bets.Nonce`; alter `Bets.Seed` length 40 → 64. Applied by `Monkify.Consumers` at startup. Do not
hand-edit the snapshot.

### 4.5 ER note

No new tables or relationships — three column changes on existing `Session`/`Bet` rows.

---

## 5. Configuration

**None new.** The keystream needs no configuration; the fallback cap continues to derive from the
existing `SessionSettings` pacing bounds (`MaxTyperCharacters`, race/lottery target seconds). No env
vars, no `GeneralSettings` additions, no seed-data changes.

---

## 6. Backend behaviour

### 6.1 Commit the server seed on the first bet (`Monkify.Api`)

Add a race-safe method to `ISessionService` / `SessionService`, modelled on `TryStartSession`
(`SessionService.cs:295`):

```csharp
// Generates a 32-byte ServerSeed + SHA256 hash and commits them ONLY if none exists yet
// (first-writer-wins). Returns silently if the session already has a ServerSeed. Never overwrites.
public async Task EnsureServerSeedCommitted(Guid sessionId)
{
    var serverSeed = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    var serverSeedHash = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(serverSeed))).ToLowerInvariant();

    await _context.Database.ExecuteSqlRawAsync(
        @"UPDATE public.""Sessions""
             SET ""ServerSeed"" = @serverSeed,
                 ""ServerSeedHash"" = @serverSeedHash,
                 ""UpdatedDate"" = @updatedDate
           WHERE ""Id"" = @id AND ""ServerSeed"" IS NULL",
        new NpgsqlParameter("@serverSeed", serverSeed),
        new NpgsqlParameter("@serverSeedHash", serverSeedHash),
        new NpgsqlParameter("@updatedDate", DateTime.UtcNow),
        new NpgsqlParameter("@id", sessionId));
}
```

Call it from `RegisterBetHandler.RegisterBet()` (`RegisterBetHandler.cs:253`) **immediately after** a
successful `InsertBet`, before publishing `BetPlacedEvent`:

```csharp
private async Task RegisterBet()
{
    var betSaved = await _sessionService.InsertBet(_bet);
    if (!betSaved) { /* unchanged */ }

    await _sessionService.EnsureServerSeedCommitted(_bet.SessionId);   // NEW — commit on (first) bet
}
```

Because the `UPDATE` is conditional on `ServerSeed IS NULL`, concurrent first bets and every
subsequent bet are safe: exactly one writer sets it, and it is never regenerated. (Lottery bets reach
`RegisterBet` the same way, so lottery sessions commit on their first bet too.)

> **Defensive safety net:** every bet calls `EnsureServerSeedCommitted` (idempotent), so a transient
> failure on the very first bet is re-attempted by the next. As a belt-and-suspenders guarantee that a
> running session always has a seed, `RunSessionConsumer` (before `RunSession` constructs the typer)
> should also call `EnsureServerSeedCommitted(sessionId)` and reload the seed. In the normal path this
> is a no-op (seed already committed at the first bet); it only fires in the pathological all-bets-failed
> case, and it still commits **before** the outcome is generated.

### 6.2 Stop deriving/persisting the old seed

- **`MonkifyTyper` constructor / `GenerateSessionSeed` (`MonkifyTyper.cs:170`):** remove the
  `RandomNumberGenerator.Fill` + `BitConverter.ToInt32` + `new System.Random(...)` logic. Read
  `session.ServerSeed` (throw `ArgumentException` if null — a session cannot run without a committed
  seed, which is guaranteed because it must have ≥1 bet). Build the canonical ordered bet-seed list and
  initialize the keystream (§6.3).
- Remove the `public int SessionSeed` property; the seed concept is now the string `ServerSeed`. Add
  what the verification endpoint needs as public read-only surface: `CharactersOnTyper` (already
  public), `QueueLength` (already public), and expose the character cap via a **public** `int MaxCharacters`
  (rename/promote the private `_maxCharacters`).
- **`SessionService.UpdateSessionStatus` (`SessionService.cs:255-260`):** delete the
  `session.Seed = monkey.SessionSeed` block. Keep persisting `WinningChoice`. `ServerSeed` is already
  stored (from §6.1); nothing seed-related is written on `Ended`.
- Grep for any remaining `session.Seed` / `.SessionSeed` references (DTOs, tests, LoadTester) and
  update. Known: `ActiveSessionDto` does **not** expose it; there is no `GET /sessions` history endpoint
  anymore ("Remove GET /sessions endpoint" is the backend `main` HEAD).

### 6.3 Portable RNG — exact algorithm (the crux; backend + JS + docs MUST match)

All hashing/HMAC operates on **UTF-8 bytes of canonical strings** so any language reproduces it
identically.

**Canonical inputs**

- `serverSeed` — lowercase hex string of the 32 committed bytes (`Session.ServerSeed`).
- `sessionId` — the session GUID as lowercase `"D"` format (e.g. `3f9a…-…`).
- `orderedBetSeeds` — every placed bet's `Seed`, bets sorted by `(CreatedDate asc, Id ordinal asc)`.
- `prefix = sessionId + "|" + string.Join("|", orderedBetSeeds)`.

**Keystream** (`key = UTF8(serverSeed)`):

```
block(counter) = HMAC_SHA256(key = UTF8(serverSeed), msg = UTF8(prefix + "|" + counter))   // counter = 0,1,2,…
keystreamBytes = block(0) ‖ block(1) ‖ block(2) ‖ …                                          // 32 bytes each
```

**Character selection (rejection sampling, pool size `P ≤ 36`):**

```
limit = 256 - (256 % P)
loop: take next keystream byte b
      if b < limit  -> index = b % P ; emit CharactersOnTyper[index]
      else          -> discard b, continue
```

Each emitted character enters the rolling window exactly as today; `CheckForWinners` is unchanged.

**Fallback winner** (character cap reached with no natural match) — dedicated labelled draw so it does
not depend on how many characters were streamed:

```
orderedChoices = Bets.Keys sorted Ordinal            // unchanged from today (includes 0-count presets)
fb = HMAC_SHA256(key = UTF8(serverSeed), msg = UTF8(prefix + "|fallback"))
rejection-sample fb's bytes into [0, orderedChoices.Length) with the same 'limit' rule
winningChoice = orderedChoices[index]
```

`GenerateNextCharacter` replaces `_random.Next(CharactersOnTyper.Length)` with the next keystream
index; `DecideFallbackWinner` replaces `new Random(SessionSeed)` with the `"fallback"` derivation.

> **Implementation note:** keep a small keystream reader (buffer of `block(counter)` bytes + cursor)
> inside `MonkifyTyper`. `_expectedCharsToWin`, `CalculatePacing`, `ApplyPacingDelay`, and
> `UpdateTypingSpeed` are untouched — pacing still only *delays* after a character is chosen, so timing
> cannot influence the (now keystream-derived) stream.

### 6.4 Verification endpoint (`Monkify.Api`)

New handler `GetSessionVerification` (pattern: `Handlers/Sessions/GetSessionVerification/` with a
`Request` + `Handler`), exposed as `GET /sessions/{id}/verification` on `SessionsController`
(operator-scoped via `_launchContext.OperatorId`, like the other actions). Returns
`SessionVerificationDto`:

| Field | Always? | Notes |
| --- | --- | --- |
| `sessionId` | yes | |
| `status` | yes | so the client knows if reveal is available |
| `serverSeedHash` | yes | the pre-published commitment |
| `serverSeed` | **only if `Status ∈ SessionEndedStatus`** | the reveal — the one gated field |
| `winningChoice` | when Ended | |
| `winnerDecidedByFallback` | when Ended | drives which repro path the verifier runs |
| `characterPool` | yes | ordered `CharactersOnTyper` as a string — verifier uses verbatim (no need to re-derive the diversity/order rule) |
| `queueLength` | yes | rolling-window size |
| `maxCharacters` | yes | the fallback cap actually used |
| `orderedBetSeeds` | yes | canonical order (server sorts; JS doesn't have to) |
| `orderedChoices` | yes | `Bets.Keys` ordered Ordinal (for fallback repro) |
| `bets` | yes | `[{ choice, nonce, date }]` for display + the per-bettor entropy check |

Handler builds a read-only `MonkifyTyper(session, settings)` (pure; needs bets loaded via
`GetBetsBySessionId`) to read `CharactersOnTyper`, `QueueLength`, `MaxCharacters`, and the ordered
choice set — guaranteeing the payload matches exactly what a run would use. Gate `serverSeed` on
`Session.SessionEndedStatus.Contains(status)`.

> The raw `clientSeed` is **never** in this payload — only `Bet.Seed` (via `orderedBetSeeds`) and the
> per-bet `nonce`. A bettor verifies their own entropy locally with their `clientSeed`.

### 6.5 Live session DTO — commitment during betting

**`ActiveSessionDto` (`ActiveSessionDto.cs`):** add `public string? ServerSeedHash { get; set; }` set
from `session.ServerSeedHash`. `GetActiveSessionForParameterHandler` already loads the `Session` row
(scalar columns included), so no query change is needed. Do **not** add raw `ServerSeed` here.

---

## 7. Frontend behaviour

### 7.1 Client seed + nonce (`useClientSeed` composable)

New `src/hooks/useClientSeed.ts` (singleton shape modelled on `useDisplayCurrency.ts`):

- Module-scope refs `clientSeed` + `nonce`, hydrated on first use from `localStorage`
  (`monkify.clientSeed`, `monkify.clientSeed.nonce`); mirror the `monkify.audio` persistence pattern in
  `GameHeader.vue`. Auto-generate a random 64-hex `clientSeed` if none stored.
- `setClientSeed(value)` — validate non-empty, sane length (e.g. ≤ 128 chars); persist; **reset nonce
  to 0** on change/rotate.
- `rotateClientSeed()` — generate a new random hex seed via `crypto.getRandomValues`.
- `nextBetSeed(): { seed: string, nonce: number }` — `seed = HMAC-SHA256(clientSeed, String(nonce))`
  lowercase hex via `crypto.subtle.importKey('raw', UTF8(clientSeed), {name:'HMAC',hash:'SHA-256'}) →
  sign(UTF8(String(nonce)))`; then increment + persist `nonce`. Must match §6.3's derivation byte-for-byte.

### 7.2 Bet flow wiring

- **`useSession.ts` `bet()` (lines 236–294) / `submit()` (245–261):** change the seed source. Either
  change `bet(seed)` to `bet({ seed, nonce })` or have `bet()` call `useClientSeed().nextBetSeed()`
  internally. Thread `nonce` into all three `submit({...})` calls (free, SolanaToken, Standard).
- **`GameConsole.vue:85`** and **`GameTerminal.vue:269`:** replace `crypto.randomUUID()` with the
  derived seed from `nextBetSeed()` (or drop the arg if `bet()` derives internally).
- **`types/index.ts` `BodySessionBet` (82–88):** tighten `seed: any → string`; add `nonce: number`.
- **`mainService.ts` `applyBet` (54–60):** unchanged shape — body now carries `nonce`.

### 7.3 Commitment + reveal + in-app verifier (`GameTerminal.vue`)

- **`types/index.ts` `SessionInstance` (73–80):** add `serverSeedHash?: string`. Add a new
  `SessionVerification` interface mirroring §6.4.
- **`mainService.ts`:** add `getSessionVerification(sessionId)` → `GET /sessions/${id}/verification`
  (shape like `getSessionInstance`), register in the default export.
- **Pre-round block (`GameTerminal.vue` ~409–485):** add a `> SERVER SEED HASH:` line near
  `> POT:` / `> FREQUENCY:`, shown while betting (from `sessionInstance.serverSeedHash`).
- **Ended block (~351–407):** after the winner reveal, show the revealed `> SERVER SEED:` and a
  **"Verify this round"** action that calls `getSessionVerification`, runs the §6.3 algorithm fully
  client-side (shared JS module, see §7.4), and shows pass/fail + the recomputed winning choice.
- Capture any `serverSeedHash`/status transition in the `/Status` websocket handler
  (`useSession.ts` 156–187) so the hash appears live as soon as the first bet commits it.

### 7.4 Shared verifier core + standalone `public/verify.html`

- Put the portable algorithm (§6.3) in a small, dependency-free TS/JS module usable by both the in-app
  panel and the standalone file (e.g. `src/utils/provablyFair.ts`), using WebCrypto (`crypto.subtle`).
- **`public/verify.html`** — a self-contained HTML+JS page (inline, no bundler) served verbatim at
  `/verify.html` under Vue CLI. Paste an Ended round's verification JSON (or fields) → recompute →
  show whether `WinningChoice` and `SHA256(ServerSeed) == ServerSeedHash` both check out, offline, with
  no call back to Monkify. (Keep its JS in sync with `provablyFair.ts` — it must be standalone, so the
  logic is duplicated verbatim; call this out in the file header.)

### 7.5 Client-seed UI (header gear modal)

- **`PlayerCustomizationModal.vue`** — add a "Fairness / client seed" section (template ~92–113 +
  `<script setup>` state) that reads/writes via `useClientSeed`: show the current client seed
  (copyable), a **Set** field (validated), and a **Rotate** button. This is **localStorage-only** — it
  does **not** go through `mainService.updatePlayerPreferences` (unlike display name/currency).

---

## 8. Flow diagram — commit → play → reveal → verify

```mermaid
sequenceDiagram
    actor P as Player
    participant FE as Frontend (useClientSeed / useSession)
    participant API as Monkify.Api (RegisterBet / Verification)
    participant DB as PostgreSQL
    participant C as Monkify.Consumers (RunSession / MonkifyTyper)

    P->>FE: place bet
    FE->>FE: seed = HMAC(clientSeed, nonce); nonce++
    FE->>API: POST /sessions/{id}/bets { seed, nonce, choice, … }
    API->>DB: InsertBet
    API->>DB: EnsureServerSeedCommitted (UPDATE … WHERE ServerSeed IS NULL)  %% first-writer-wins
    Note over API,DB: ServerSeedHash now published; ServerSeed hidden
    FE->>API: GET /sessions/{id} → ActiveSessionDto.serverSeedHash (shown during betting)
    C->>C: RunSession → MonkifyTyper reads ServerSeed, streams via HMAC keystream
    C->>DB: status Ended, WinningChoice persisted
    P->>FE: "Verify this round"
    FE->>API: GET /sessions/{id}/verification (status Ended → serverSeed revealed)
    FE->>FE: recompute stream from serverSeed+orderedBetSeeds → equals WinningChoice?  ✅/❌
```

---

## 9. Out of scope / caveats

- Independent lab RNG certification (GLI/BMM) — separate, deferred track.
- Server-side / cross-device client-seed history — `localStorage` only for now.
- Operator-facing fairness dashboards.
- **Legal PDF (in scope but out-of-repo):** regenerate `Paperwork/Legal documents/04 Fairness &
  game rules.pdf` via its `generate_04_fairness.py` so §3 ("Published RNG seeding method") and §7
  ("Provably-fair verification") describe commit-reveal + the HMAC keystream + client seed/nonce and no
  longer reference `System.Random` or the `Int32` seed. ⚠️ `Paperwork/` is **not** one of the three git
  repos and has no `feature/provably-fair` branch — it is edited **in place** as a local deliverable
  (no PR). The implementer should regenerate it after the code lands and note it in the ship report; it
  is not part of the `monkify-meta` merge.

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

| # | File | Change |
| --- | --- | --- |
| 1 | `Monkify.Domain/Sessions/Entities/Session.cs` | Remove `int? Seed`; add `string? ServerSeed`, `string? ServerSeedHash` |
| 2 | `Monkify.Domain/Bets/Entities/Bet.cs` | Add `long Nonce`; set it in the bet constructor/handler |
| 3 | `Monkify.Domain/Sessions/Services/MonkifyTyper.cs` | Read `ServerSeed`; HMAC keystream + rejection sampling; labelled fallback; drop `System.Random`/`Int32`; promote `MaxCharacters` public; remove `SessionSeed` |
| 4 | `Monkify.Infrastructure/Data/Context/MonkifyDbContext.cs` | Session: map `ServerSeed`/`ServerSeedHash` (64). Bet: `Seed` 40→64, map `Nonce` |
| 5 | `Monkify.Infrastructure/Services/Sessions/SessionService.cs` | Add `EnsureServerSeedCommitted`; remove `Seed` write in `UpdateSessionStatus` |
| 6 | `Monkify.Infrastructure/Services/Sessions/ISessionService.cs` | Declare `EnsureServerSeedCommitted` |
| 7 | `Monkify.Infrastructure/Handlers/Sessions/RegisterBet/RegisterBetHandler.cs` | Call `EnsureServerSeedCommitted` after `InsertBet`; set `Nonce` on the bet |
| 7b | `Monkify.Infrastructure/Consumers/RunSession/RunSessionConsumer.cs` | Defensive `EnsureServerSeedCommitted` + reload before `RunSession` (safety net, §6.1) |
| 8 | `Monkify.Infrastructure/Handlers/Sessions/RegisterBet/RegisterBetRequestBody.cs` | `Seed` `[MaxLength(64)]`; add `long Nonce` |
| 9 | `Monkify.Infrastructure/Handlers/Sessions/GetSessionVerification/*` | New `Request` + `Handler` (reveal-gated payload) |
| 10 | `Monkify.Infrastructure/Contracts/Sessions/SessionVerificationDto.cs` | New DTO (§6.4) |
| 11 | `Monkify.Infrastructure/Contracts/Sessions/ActiveSessionDto.cs` | Add `ServerSeedHash` |
| 12 | `Monkify.Api/Controllers/SessionsController.cs` | Add `GET {id}/verification` action |
| 13 | `Monkify.Infrastructure/Migrations/*` | `ProvablyFair` migration (generated) |

### 10.2 Frontend (`monkify-front`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/hooks/useClientSeed.ts` (new) | Persistent seed + nonce; `nextBetSeed()`, `setClientSeed`, `rotateClientSeed` |
| 2 | `src/utils/provablyFair.ts` (new) | Shared portable verifier core (§6.3) via WebCrypto |
| 3 | `src/hooks/useSession.ts` | Seed/nonce from `useClientSeed`; send `nonce`; capture `serverSeedHash` |
| 4 | `src/components/game/GameConsole.vue` | Replace `crypto.randomUUID()` (line 85) |
| 5 | `src/components/game/GameTerminal.vue` | Replace UUID (269); `> SERVER SEED HASH:` line; reveal + "Verify this round" panel |
| 6 | `src/components/header/PlayerCustomizationModal.vue` | Fairness / client-seed section (view/set/rotate) |
| 7 | `src/types/index.ts` | `BodySessionBet.nonce` + `seed: string`; `SessionInstance.serverSeedHash?`; new `SessionVerification` |
| 8 | `src/services/mainService.ts` | Add `getSessionVerification(sessionId)` |
| 9 | `public/verify.html` (new) | Standalone offline verifier (self-contained) |

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/provably-fair.md` | This spec |
| 2 | `INDEX.md` | KB-map row + "Feature currently in flight" |
| 3 | `features/sessions.md` (§7 Typer) + `architecture.md` (§9) + `glossary.md` | Updated at **ship** time to describe the new scheme (per INDEX "keep KB current") |

### 10.4 Out-of-repo

| # | Path | Change |
| --- | --- | --- |
| 1 | `Paperwork/Legal documents/04 Fairness & game rules.pdf` (+ `scripts/generate_04_fairness.py`) | Regenerate to match the new scheme (§9 caveat — in place, no branch) |

---

## 11. Unit tests

Project: `monkify-backend/src/Monkify.Tests` — run via **`scripts/run_tests.bat`** (filters to
`UnitTests` and **excludes** `ProductionSessions`; never `dotnet test` the whole solution).

> ⚠️ **Existing tests that WILL break and must be rewritten onto the keystream.** In
> `Monkify.Tests/UnitTests/Domain/Sessions/MonkifyTyperTests.cs`, these assert against an oracle
> `new Random(typer.SessionSeed)` and reference the removed `int SessionSeed`:
> - `GenerateNextCharacter_CharacterStreamIsDeterministicFunctionOfSeed_PacingDoesNotAlterIt`
> - `GenerateNextCharacter_TwoTypersWithSameSeed_ProduceIdenticalStream`
> - `GenerateNextCharacter_WhenCharacterCapReached_DecidesWinnerDeterministicallyFromSeed` (recomputes
>   `orderedChoices[new Random(SessionSeed).Next(...)]`)
>
> Rewrite their oracle to the §6.3 HMAC keystream (and the `"fallback"` draw). Tests build a `Session`
> **inline** (`new Session(); session.Parameters = new SessionParameters{ … }`; bets via
> `session.Bets.Add(new Bet(BetStatus.Made, 10){ Choice = Faker.Random.String2(len,"alphabet") })`) and
> pass pacing through the private `InstantPacing(int maxCharacters = 50_000_000)` helper — now these
> sessions must also set `ServerSeed` (the constructor throws if it's null). Add a small test helper that
> stamps a fixed `session.ServerSeed` so streams are deterministic.

| # | Test file | Cases | Expected |
| --- | --- | --- | --- |
| 1 | `MonkifyTyperTests.cs` (rewrite the 3 above) | Same `ServerSeed` + ordered bet seeds ⇒ identical stream across two typers; matches an independent keystream oracle | Byte-for-byte; no `System.Random`/wall-clock (AC7) |
| 2 | `MonkifyTyperTests.cs` (new) | A hard-coded reference vector `(serverSeed, sessionId, betSeeds, pool)` ⇒ expected stream prefix + winner (shared verbatim with the JS verifier) | Cross-language parity (AC6) |
| 3 | `MonkifyTyperTests.cs` (new) | Rejection sampling over a pool size not dividing 256 ⇒ uniform index distribution; out-of-range bytes discarded | No modulo bias (AC8) |
| 4 | `MonkifyTyperTests.cs` (adapt fallback test) | `InstantPacing(maxCharacters: 200)` + high-entropy config ⇒ `WinnerDecidedByFallback`, winner = `"fallback"` draw, reproducible; and a tractable config ⇒ not fallback | Deterministic fallback (AC9) |
| 5 | new `SessionServiceTests` case (or extend existing) | `EnsureServerSeedCommitted`: 1st call sets `ServerSeed`+hash; 2nd call no-ops (value unchanged) | First-writer-wins, immutable (AC1, AC2) |
| 6 | `UnitTests/Handlers/RegisterBetHandlerTests.cs` | Body factories (`SolanaBody`/`StandardBody`/`FreeBody`) now carry a 64-char seed + `Nonce`; assert `_sessionServiceMock.Verify(x => x.EnsureServerSeedCommitted(session.Id))` after a successful insert; `Nonce` persisted | Commit invoked once; nonce saved (AC1, AC11) |
| 7 | new `GetSessionVerificationHandlerTests` | Status < Ended ⇒ payload has `serverSeedHash`, `serverSeed == null`; Ended ⇒ `serverSeed` present; `clientSeed` never in payload | Reveal gating (AC3, AC4, AC12) |

Test bases: `MonkifyTyperTests` is a plain xUnit class with its own `Faker`; handler tests extend
`UnitTestsClass` (in-memory EF, `Messaging`, `ShouldReturnValidationFailure` helpers) and construct the
handler via `CreateHandler(context)` with Moq'd `ISessionService`/`IPaymentMethodResolver`/etc.

### Integration tests (`Monkify.Tests.Integration`, Testcontainers — `scripts/run_integration_tests.bat`, Docker required)

Harness: `Harness/IntegrationTestFixture.cs` (`IAsyncLifetime`, boots Postgres 16 / RabbitMQ / Redis
containers + both API & Consumers hosts via `TestWebAppFactory`, swaps in `FakeSolanaService` /
`FakeOperatorWalletResolver` / `ControllablePreBetCheckService`, seeds 4 operators + a crypto
`OperatorToken` + default session types). Tests opt in with `[Collection(IntegrationCollection.Name)]`
and extend the patterns in `SessionFlowTests.cs`. Bets go over real HTTP: `MintToken(operatorId,
externalPlayerId)` → `PlaceBetAsync(sessionId, token, RegisterBetRequestBody)`; sessions seeded via
`SeedWaitingBetsSessionAsync(parametersId)`; wait with
`WaitForStatusAsync(sessionId, SessionStatus.RewardForWinnersCompleted, Timeout)`; read the row via
`GetSessionAsync`. Bet bodies currently set `Seed = Guid.NewGuid().ToString("N")` — keep any valid
seed + set `Nonce` (the outcome check re-derives from the **stored** `Bet.Seed`s, so the frontend's
HMAC client-seed derivation is out of scope for the backend integration test).

1. **Happy path (mandatory)** — new test in a `ProvablyFairFlowTests.cs` (or extend `SessionFlowTests`):
   mirror `RunPaidCryptoRaceAsync` / `PresetRace_PaidRegulated…` (bet every preset choice from distinct
   players so a winner is guaranteed); after `RewardForWinnersCompleted`, `GET
   /sessions/{id}/verification`, recompute the stream from the revealed `serverSeed` + `orderedBetSeeds`
   + `characterPool` and assert the reproduced winner == `session.WinningChoice` **and**
   `SHA256(serverSeed) == serverSeedHash` (AC5, AC6, AC16). Also assert pre-`Ended` the endpoint hides
   `serverSeed` (AC3). `AssertNoErrorLogs()`.
2. **Fallback flow (complex flow — required)** — seed a high-entropy session type (large alphabet +
   long `ChoiceRequiredLength`, or a small `MaxTyperCharacters`) so the cap triggers; the config must
   still finish within the fixture's pacing (`RaceTargetP95SecondsMax=2`, 120s test `Timeout`). Assert
   `winnerDecidedByFallback` and that the verifier reproduces the same winner from the payload (AC9,
   AC17), mirroring the unit fallback test's structure.

---

## 12. Acceptance criteria

Mapped 1:1 from the Trello **Acceptance Criteria** checklist (AC1–AC18):

1. First bet generates a 32-byte `ServerSeed` and persists `ServerSeedHash = SHA256(ServerSeed)` exactly once.
2. Concurrent first bets commit only one seed (first-writer-wins); never regenerated.
3. Pre-`Ended` `GET /sessions/{id}/verification` returns `ServerSeedHash` + inputs but **not** raw `ServerSeed`.
4. `Ended` verification returns raw `ServerSeed`, hash, every `Bet.Seed`+`Choice`+`Nonce`, params, `WinningChoice`.
5. `SHA256(ServerSeed)` equals the previously published `ServerSeedHash`.
6. Recomputing the stream from `ServerSeed` + ordered `Bet.Seed`s reproduces `Session.WinningChoice`.
7. Identical inputs replay byte-for-byte on any host/language (no `System.Random`/wall-clock/hardware dependence).
8. Character selection uses rejection sampling — every pool character equally likely (no modulo bias).
9. Fallback winner drawn from the same HMAC primitive and reproducible by the verifier.
10. Header gear modal shows the client seed; player can set a custom value or rotate; persists in localStorage.
11. `Bet.Seed = HMAC-SHA256(clientSeed, nonce)` sent; `nonce` persisted; nonce increments per bet.
12. Bettor can confirm `HMAC(clientSeed, nonce) == Bet.Seed` from the payload; raw `clientSeed` never returned by the API.
13. `ServerSeedHash` shown during betting; raw `ServerSeed` + "Verify this round" panel shown after `Ended`.
14. "Verify this round" reproduces the outcome fully client-side (pass/fail + recomputed winner).
15. Standalone `public/verify.html` reproduces the winner offline from pasted inputs.
16. Greenfield migration reshapes the columns; happy-path bet→run→Ended→verify passes as a mandatory integration test.
17. Fallback-winner flow reproduced end-to-end in a dedicated integration test.
18. KB + `04 Fairness & game rules.pdf` regenerated to the new scheme; no `System.Random`/`Int32` references remain.

---

## 13. Manual QA

1. Local stack up (`docker-compose`); open a `Standard` game; place a bet.
2. Confirm `> SERVER SEED HASH:` appears during betting; note it.
3. Let the round finish; confirm the raw server seed is revealed and "Verify this round" shows ✅ with
   the recomputed winner matching.
4. Copy the revealed inputs into `/verify.html` (offline) → same ✅.
5. In the gear modal, rotate the client seed; place another bet; confirm the new `Bet.Seed` matches
   `HMAC(newClientSeed, nonce)` (nonce reset to 0 on rotate).

---

## 14. Trello / pipeline

- **Source card:** [XWAeIgIQ](https://trello.com/c/XWAeIgIQ) — moved to **Specs created**;
  **Implementation** section on the card lists worktree + branch + spec.
- **After merge:** `/monkify-ship-feature Make the game actually provably fair` (reads branch from the card).

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-07-10 | Initial spec from refined card #42. Grounded in the live backend/frontend code and test suites (exact file paths + harness patterns confirmed in §11). |

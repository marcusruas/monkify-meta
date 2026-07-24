# Feature — Provably Fair Final (in-app FAIR panel + a cheap verification endpoint)

> Status: **shipped.** Authoritative spec: `../specs/provably-fair-final.md`
> (merged via `feature/provably-fair-final` on 2026-07-23).
> Builds on `provably-fair.md` — the commit-reveal scheme, the keystream RNG and the client
> seed/nonce derivation are **unchanged**; this feature only changes where and how cheaply a round
> gets verified.

## What it does

A player verifies the round they just watched **inside the app**: a `FAIR` tab in the game side panel
that unlocks when the round ends, proves the seed commitment and the player's own bet seed instantly,
and offers an opt-in full outcome replay that runs off the main thread. The verification endpoint that
feeds it no longer replays the character stream on every call and caches settled rounds, so the panel
is cheap enough to open freely.

## Core rules

**The FAIR panel (frontend)**

- **Housing (superseded by `fair-stats-menu`).** The panel is no longer a side-panel tab. `GameSidePanel`
  is now **chat-only**; `GameFairPanel` is rendered inside the header-menu **Fair modal**
  (`GameFairModal`) — an always-on *how-to-verify* explainer above the same reused panel — and off
  `/game` it shows a "watch a round to verify it" empty state (no fetch). Still **hidden when embedded**
  (`useEmbed()`): regulated casino iframes get no in-frame fairness surface. All the panel behaviour
  below is unchanged. See `features/fair-stats-menu.md`.
- It is **locked until the round reaches a terminal status**. While the round runs it shows only the
  committed `serverSeedHash` and "verification unlocks when this round ends". The commitment is
  fetched from the verification endpoint (not from `gameInstance`), because the backend commits the
  seed on the first bet via a raw `UPDATE` that broadcasts nothing — a bet placed while the tab is
  open would otherwise never surface a hash.
- On unlock **two instant checks** run — a fixed handful of WebCrypto calls, never a replay:
  1. **Commitment** — `SHA256(UTF8(serverSeed)) == serverSeedHash`.
  2. **Your bet seed** — `HMAC-SHA256(clientSeed, String(nonce))` recomputed per bet and matched
     against the payload's ordered bet seeds. A **non-match is never a failed check**: it renders as
     "no verifiable bet from this browser (spectator, or the client seed was rotated since this bet)".
- The **full outcome recompute is opt-in** behind `RECOMPUTE OUTCOME`, runs in a **cancellable Web
  Worker** with a progress bar sized by `charactersTyped`, and warns with an estimated duration above
  250,000 characters. A mismatch is reported loudly and never auto-retried; a worker that dies
  surfaces its error in place of silently reverting to the button.
- **Rollover retention:** the panel keeps the last ended round (labelled with its short round id)
  when the next round starts, and a running recompute is **not** cancelled by the rollover. The
  retained round is replaced only when the *next* round ends. The worker **is** terminated on unmount,
  so a closed panel never burns CPU.
- A round **nobody bet on** never committed a seed: the panel says "no bets this round — nothing to
  verify" and offers no recompute.
- **`COPY VERIFICATION JSON`** hands over the raw payload for any third-party verifier.
- **Objects crossing to the worker must be plain.** `postMessage` uses structured clone, which rejects
  Vue reactive Proxies with `DataCloneError: [object Array] could not be cloned` — the retained round
  is held in a `shallowRef` and the message is built from `toRaw` with copied arrays.

**The standalone offline verifier**

- **`public/verify.html` is kept** (this reverses the card's and `provably-fair.md`'s "delete it"
  decision). The in-app panel runs code Space Stroker ships and serves; the standalone file is one
  self-contained page a player **saves and runs offline**, with nothing of ours executing live — the
  "don't trust us" path. The FAIR tab links it with a `download` and step-by-step instructions.
- It independently reproduces the winner, the SHA256 commitment **and the character count**, cross-
  checking the count against the payload's `charactersTyped` (the fallback flag is derived from that
  number, so a wrong count would mean a wrong flag), and can download the **last 1,000 reproduced
  characters** — the run-up to the winning sequence — via a fixed-size ring buffer, so a capped
  5,000,000-character round costs the same as a short one.
- It therefore keeps its **own inline copy** of the algorithm: `ProvablyFair.cs`,
  `src/utils/provablyFair.ts` and `public/verify.html` must stay byte-identical. The KEEP-IN-SYNC
  comments in all three are load-bearing.

**The verification endpoint (backend)**

- **The server never replays the stream.** `Game.CharactersTyped` (`int?`) is persisted when the run
  finishes, in the **same `SaveChanges` as `WinningChoice`**, so a game is never observable as
  ended-with-no-count. `winnerDecidedByFallback` is **derived** (`CharactersTyped >= typer.MaxCharacters`).
- **`null` means unknown, never `false`.** A legacy/never-run row reports the flag as `null`; claiming
  `false` would be a lie. The client's own recompute can still determine it locally.
- **`charactersTyped` is published** in `GameVerificationDto` so the client can size and estimate its
  opt-in replay.
- **Only a settled round is cached** (`GameVerification:{gameId}`, 12h, via `ICacheService`): the DTO
  carries `Status`, and `Ended → RewardForWinnersInProgress → RewardForWinnersCompleted` happens
  seconds later, so caching at `Ended` would pin a stale status for the whole TTL. Caching requires a
  final status **and** a revealed seed **and** a populated count. Every field is immutable by then, so
  **no invalidation path exists or is needed**.
- **The operator check runs before the cache read.** The key is per-game (the payload is
  operator-independent), so serving a hit first would leak a round across operators. One indexed
  `GetGameById` therefore stays on the cached path — it *is* the security check; the cache eliminates
  the bets query, the typer construction and the DTO rebuild. Unknown or other-operator games keep the
  existing vague `GameNotValidForBets` message on both paths.
- Reveal gating is unchanged: `serverSeedHash` is published as soon as it exists, the raw `serverSeed`
  only once the round has ended. Pre-`Ended` responses are never cached.
- **`MaxCharacters` drift is accepted, not fixed:** the cap is taken from the same read-time typer that
  publishes `maxCharacters`, so a settings change between run and verification shifts both together —
  the flag can never contradict what the client replays against.

**Reach (deliberate limits)**

- **In-app FAIR tab covers the current round only** — no game-id lookup and no tokenless/public
  verification; the endpoint stays launch-token + operator scoped. **Re-checking older rounds now
  ships** via the **Games History** page (`features/games-history.md`): its per-row Verify button
  fetches this same `GET /games/{id}/verification` payload for any completed round and hands it to the
  offline `public/verify.html` (link-out only — no in-app replay on the history page).

## Key code touchpoints

**Backend (`monkify-backend`)**
- `SpaceStroker.Domain/Games/Entities/Game.cs` — `CharactersTyped` (`int?`); inline mapping in
  `SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs`; migration
  `Migrations/20260722211737_GameCharactersTyped.cs` (applied only by `SpaceStroker.Consumers`).
- `SpaceStroker.Infrastructure/Services/Games/GameService.cs` — `UpdateGameStatus` writes
  `monkey.CharactersTypedCount` alongside `WinningChoice`.
- `SpaceStroker.Infrastructure/Handlers/Games/GetGameVerification/GetGameVerificationHandler.cs` —
  replay deleted, flag derived, cache read after the ownership check, cache write only when final.
- `SpaceStroker.Infrastructure/Services/Cache/CacheHelper.cs` —
  `GenerateGameVerificationCacheSettings(gameId)` (12h).
- `SpaceStroker.Infrastructure/Contracts/Games/GameVerificationDto.cs` — `CharactersTyped`.
- Tests: `UnitTests/Handlers/GetGameVerificationHandlerTests.cs` (derived flag, cache-only-when-final,
  operator scoping on the cached path), `UnitTests/Services/GameServiceTests.cs` (count write),
  `SpaceStroker.Tests.Integration/ProvablyFairFlowTests.cs` (`HappyPath_EndedGame_VerificationReproducesWinnerAndHashHolds`,
  `SettledGame_SecondVerificationRequest_ReturnsIdenticalPayload`,
  `FallbackFlow_CapReached_VerificationReproducesFallbackWinner`).

**Frontend (`monkify-front`)**
- `src/components/game/GameFairPanel.vue` — the FAIR tab (locked state, instant checks, opt-in
  recompute, retention, copy JSON, offline-verifier instructions).
- `src/workers/verifyRound.worker.ts` — the replay off the main thread; imports the algorithm from
  `src/utils/provablyFair.ts` (one copy in the bundle). Cancellation is `terminate()`, deliberately
  not a message, so it can't be missed between progress ticks.
- `src/components/game/GameSidePanel.vue` — third tab in both tab bars + the collapsed dock rail,
  `useEmbed()` gate, panel widened so the tab row isn't clipped.
- `src/utils/provablyFair.ts` — optional progress callback; `public/verify.html` — the standalone
  offline verifier (count cross-check + last-1,000-character download).
- `src/types/index.ts` — `GameVerification.charactersTyped`.

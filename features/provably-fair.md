# Feature — Provably Fair (commit-reveal + portable RNG + player verifier)

> Status: **shipped.** Authoritative spec: `../specs/provably-fair.md`
> (merged via `feature/provably-fair` on 2026-07-10).

## What it does

Every round is **provably fair**: the server commits to a secret seed **before betting closes**,
reveals it after the round, and drives the monkey from a **portable HMAC-SHA256 keystream** that any
language can reproduce. Players can verify — from published inputs — that the outcome was not
manipulated. Replaces the old post-lock `Int32` + `System.Random` scheme, which was grindable and not
independently reproducible.

## Core rules

- **Commit on the first bet.** The first bet of a session generates a 32-byte `ServerSeed` (stored as
  a 64-char lowercase-hex string) and commits `ServerSeedHash = SHA256(UTF8(ServerSeed))` in one
  **atomic, first-writer-wins** `UPDATE … WHERE "ServerSeed" IS NULL` (mirrors `TryStartSession`). It
  is **never** regenerated. `RunSessionConsumer` re-invokes the commit defensively before a run as a
  safety net.
- **Reveal gating.** `ServerSeedHash` is published as soon as it exists; the raw `ServerSeed` is
  disclosed **only** once `Session.Status ∈ SessionEndedStatus`.
- **Portable RNG.** Character selection is a pure function of the committed inputs via an HMAC-SHA256
  counter keystream: `block(counter) = HMAC-SHA256(key = UTF8(serverSeed), msg = UTF8(prefix + "|" +
  counter))`, where `prefix = sessionId("D") + "|" + join("|", orderedBetSeeds)` and `orderedBetSeeds`
  are the bets' `Seed`s ordered by `(CreatedDate asc, Id ordinal asc)`. Keystream bytes map to
  character-pool indices by **rejection sampling** (`limit = 256 − 256 % poolSize`) so there is no
  modulo bias. The game mechanic (rolling-window match) is unchanged; only the RNG source changed
  (no more `System.Random`, no `RandomNumberGenerator.Fill` at run time).
- **Fallback winner** (character cap reached) is drawn from a dedicated `HMAC-SHA256(serverSeed,
  prefix + "|fallback")` label — reproducible regardless of how many characters streamed. The final
  terminal reveal of the fallback winner (see `characters-away.md`) is preserved.
- **Client seed + nonce (Stake-style).** Each player holds a persistent client seed (localStorage,
  settable/rotatable in the header gear modal); each bet carries an incrementing `nonce` and sends
  `Bet.Seed = HMAC-SHA256(key = clientSeed, msg = String(nonce))`. The raw `clientSeed` **never**
  leaves the browser — only the derived `Bet.Seed` reaches the server. Rotating/setting the seed
  resets the nonce to 0. Free bets still carry a seed + nonce.
- **Two verification layers.** (a) Anyone reproduces the outcome from `ServerSeed` + canonically
  ordered `Bet.Seed`s and confirms `SHA256(ServerSeed) == ServerSeedHash`. (b) A bettor privately
  confirms their `Bet.Seed == HMAC(theirClientSeed, nonce)` — proof their entropy was used unmodified.
- **Verification surface.** `GET /sessions/{id}/verification` (operator-scoped, reveal-gated) returns
  `serverSeedHash`, the ordered bet seeds/choices/nonces, `characterPool`, `queueLength`,
  `maxCharacters`, and — once Ended — `serverSeed` + `winningChoice` + `winnerDecidedByFallback`. The
  raw `clientSeed` is never returned. `ActiveSessionDto` also carries `serverSeedHash` for the in-game
  commitment line. Verification runs in the **backend endpoint** and a **standalone offline
  `public/verify.html`** — the in-terminal "Verify this round" recompute was intentionally dropped; the
  game terminal only **displays** the commitment (during betting) and the revealed seed (after Ended).
- **Storage / migration (greenfield).** `Session.Seed` (`int?`) is removed; `Session.ServerSeed` +
  `Session.ServerSeedHash` (64) and `Bet.Nonce` are added; `Bet.Seed` widened `40 → 64`. Historical
  rows are not back-fillable (old int seeds cannot reproduce the HMAC stream) — acceptable (no prod
  data). `RegisterBetRequestBody.Seed` is `[MaxLength(64)]` and carries `Nonce`.

## Key code touchpoints

**Backend (`monkify-backend`)**
- `Monkify.Domain/Sessions/Services/ProvablyFair.cs` — keystream primitives (`GenerateServerSeed`,
  `HashServerSeed`, `BuildPrefix`, `Keystream`, `NextIndex`, `FallbackIndex`).
- `Monkify.Domain/Sessions/Services/MonkifyTyper.cs` — reads `Session.ServerSeed`, drives characters +
  fallback from the keystream; `MaxCharacters` public.
- `Monkify.Infrastructure/Services/Sessions/SessionService.cs` — `EnsureServerSeedCommitted` (+ removed
  the old `Seed` write in `UpdateSessionStatus`); `ISessionService`.
- `Monkify.Infrastructure/Handlers/Sessions/RegisterBet/*` — commit on first bet, persist `Nonce`,
  64-char seed.
- `Monkify.Infrastructure/Consumers/RunSession/RunSessionConsumer.cs` — defensive commit + reload.
- `Monkify.Infrastructure/Handlers/Sessions/GetSessionVerification/*` +
  `Contracts/Sessions/SessionVerificationDto.cs` + `Api/Controllers/SessionsController.cs`
  (`GET {id}/verification`); `Contracts/Sessions/ActiveSessionDto.cs` (`ServerSeedHash`).
- `Monkify.Domain/Sessions/Entities/Session.cs`, `Monkify.Domain/Bets/Entities/Bet.cs`,
  `Monkify.Infrastructure/Data/Context/MonkifyDbContext.cs`, `Migrations/*_ProvablyFair.cs`.
- Tests: `UnitTests/Domain/Sessions/ProvablyFairTests.cs`, `UnitTests/Handlers/GetSessionVerificationHandlerTests.cs`,
  and `Monkify.Tests.Integration/ProvablyFairFlowTests.cs` (happy path + fallback).

**Frontend (`monkify-front`)**
- `src/utils/provablyFair.ts` — portable verifier core (WebCrypto), mirrors `ProvablyFair.cs`.
- `src/hooks/useClientSeed.ts` — persistent client seed + nonce; `nextBetSeed()`.
- `src/hooks/useSession.ts` — `bet()` derives seed+nonce from `useClientSeed`; sends `nonce`.
- `src/components/game/GameTerminal.vue` — `> SERVER SEED HASH:` (betting) + `> SERVER SEED:` reveal
  (auto-fetched on Ended); `src/components/header/PlayerCustomizationModal.vue` — client-seed controls.
- `src/services/mainService.ts` (`getSessionVerification`), `src/types/index.ts`
  (`BodySessionBet.nonce`, `SessionInstance.serverSeedHash`, `SessionVerification`).
- `public/verify.html` — standalone offline verifier.

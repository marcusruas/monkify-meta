# Feature — Provably Fair (commit-reveal + portable RNG + player verifier)

> Status: **shipped.** Authoritative spec: `../specs/provably-fair.md`
> (merged via `feature/provably-fair` on 2026-07-10).
>
> **Superseded in part by `provably-fair-final.md` (2026-07-23)** — verification now runs **in the app**
> (the `FAIR` side-panel tab), the endpoint no longer replays the stream, and the "in-terminal recompute
> intentionally dropped" decision below is reversed. The commit-reveal scheme, keystream RNG and client
> seed/nonce derivation are untouched.

## What it does

Every round is **provably fair**: the server commits to a secret seed **before betting closes**,
reveals it after the round, and drives the monkey from a **portable HMAC-SHA256 keystream** that any
language can reproduce. Players can verify — from published inputs — that the outcome was not
manipulated. Replaces the old post-lock `Int32` + `System.Random` scheme, which was grindable and not
independently reproducible.

## Core rules

- **Commit on the first bet.** The first bet of a game generates a 32-byte `ServerSeed` (stored as
  a 64-char lowercase-hex string) and commits `ServerSeedHash = SHA256(UTF8(ServerSeed))` in one
  **atomic, first-writer-wins** `UPDATE … WHERE "ServerSeed" IS NULL` (mirrors `TryStartGame`). It
  is **never** regenerated. `RunGameConsumer` re-invokes the commit defensively before a run as a
  safety net.
- **Reveal gating.** `ServerSeedHash` is published as soon as it exists; the raw `ServerSeed` is
  disclosed **only** once `Game.Status ∈ GameEndedStatus`.
- **Portable RNG.** Character selection is a pure function of the committed inputs via an HMAC-SHA256
  counter keystream: `block(counter) = HMAC-SHA256(key = UTF8(serverSeed), msg = UTF8(prefix + "|" +
  counter))`, where `prefix = gameId("D") + "|" + join("|", orderedBetSeeds)` and `orderedBetSeeds`
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
- **Verification surface.** `GET /games/{id}/verification` (operator-scoped, reveal-gated) returns
  `serverSeedHash`, the ordered bet seeds/choices/nonces, `characterPool`, `queueLength`,
  `maxCharacters`, and — once Ended — `serverSeed` + `winningChoice` + `winnerDecidedByFallback`
  (plus `charactersTyped`, added by `provably-fair-final.md`). The raw `clientSeed` is never returned.
  `ActiveGameDto` also carries `serverSeedHash` for the in-game commitment line. Players verify in the
  **`FAIR` side-panel tab** (instant commitment + own-seed checks, opt-in Web Worker recompute) or in
  the **standalone offline `public/verify.html`**; the game **terminal** still only *displays* the
  commitment (during betting) and the revealed seed (after Ended) — that part was never re-added. See
  `provably-fair-final.md`.
- **Storage / migration (greenfield).** `Game.Seed` (`int?`) is removed; `Game.ServerSeed` +
  `Game.ServerSeedHash` (64) and `Bet.Nonce` are added; `Bet.Seed` widened `40 → 64`. Historical
  rows are not back-fillable (old int seeds cannot reproduce the HMAC stream) — acceptable (no prod
  data). `RegisterBetRequestBody.Seed` is `[MaxLength(64)]` and carries `Nonce`.

## Key code touchpoints

**Backend (`monkify-backend`)**
- `SpaceStroker.Domain/Games/Services/ProvablyFair.cs` — keystream primitives (`GenerateServerSeed`,
  `HashServerSeed`, `BuildPrefix`, `Keystream`, `NextIndex`, `FallbackIndex`).
- `SpaceStroker.Domain/Games/Services/SpaceStrokerTyper.cs` — reads `Game.ServerSeed`, drives characters +
  fallback from the keystream; `MaxCharacters` public.
- `SpaceStroker.Infrastructure/Services/Games/GameService.cs` — `EnsureServerSeedCommitted` (+ removed
  the old `Seed` write in `UpdateGameStatus`); `IGameService`.
- `SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/*` — commit on first bet, persist `Nonce`,
  64-char seed.
- `SpaceStroker.Infrastructure/Consumers/RunGame/RunGameConsumer.cs` — defensive commit + reload.
- `SpaceStroker.Infrastructure/Handlers/Games/GetGameVerification/*` +
  `Contracts/Games/GameVerificationDto.cs` + `Api/Controllers/GamesController.cs`
  (`GET {id}/verification`); `Contracts/Games/ActiveGameDto.cs` (`ServerSeedHash`).
- `SpaceStroker.Domain/Games/Entities/Game.cs`, `SpaceStroker.Domain/Bets/Entities/Bet.cs`,
  `SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs`, `Migrations/*_ProvablyFair.cs`.
- Tests: `UnitTests/Domain/Games/ProvablyFairTests.cs`, `UnitTests/Handlers/GetGameVerificationHandlerTests.cs`,
  and `SpaceStroker.Tests.Integration/ProvablyFairFlowTests.cs` (happy path + fallback).

**Frontend (`monkify-front`)**
- `src/utils/provablyFair.ts` — portable verifier core (WebCrypto), mirrors `ProvablyFair.cs`.
- `src/hooks/useClientSeed.ts` — persistent client seed + nonce; `nextBetSeed()`.
- `src/hooks/useGame.ts` — `bet()` derives seed+nonce from `useClientSeed`; sends `nonce`.
- `src/components/game/GameTerminal.vue` — `> SERVER SEED:` reveal only (auto-fetched on Ended). The
  `> SERVER SEED HASH:` commitment line was **removed from the terminal on 2026-07-25** (it was a
  64-char hash that wrapped over two lines and crowded the pre-bet view); the commitment is still
  published on `ActiveGameDto.serverSeedHash` and is shown in the **FAIR** tab
  (`GameFairPanel.vue` — "Server seed hash (committed)") and in `public/verify.html`.
  `src/components/header/PlayerCustomizationModal.vue` — client-seed controls.
- `src/services/mainService.ts` (`getGameVerification`), `src/types/index.ts`
  (`BodyGameBet.nonce`, `GameInstance.serverSeedHash`, `GameVerification`).
- `public/verify.html` — standalone offline verifier (kept; linked from the FAIR tab).
- `src/components/game/GameFairPanel.vue` + `src/workers/verifyRound.worker.ts` — the in-app verifier
  (added by `provably-fair-final.md`).

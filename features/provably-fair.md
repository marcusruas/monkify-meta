# Domain — Provably fair

> **What this covers.** How a round proves it was not manipulated: the commit-reveal server seed, the
> portable HMAC-SHA256 keystream that drives the monkey, the player's own client seed + nonce, and the
> two places a player can verify — the in-app **FAIR** panel and the standalone offline
> `public/verify.html`.
>
> Deep design: [`../specs/provably-fair-final.md`](../specs/provably-fair-final.md) is the current
> record (the in-app panel + the cheap endpoint). The original scheme's spec was retired in card #167 —
> its content is here.

---

## 1. The scheme

Every round is provably fair: the server commits to a secret seed **before betting closes**, reveals it
after the round, and drives the monkey from a **portable HMAC-SHA256 keystream** any language can
reproduce. This replaced an `Int32` + `System.Random` scheme that was grindable and not independently
reproducible.

- **Commit on the first bet.** The first bet generates a 32-byte `ServerSeed` (64-char lowercase hex)
  and commits `ServerSeedHash = SHA256(UTF8(ServerSeed))` in one **atomic, first-writer-wins**
  `UPDATE … WHERE "ServerSeed" IS NULL` (mirrors `TryStartGame`). It is **never** regenerated.
  `RunGameConsumer` re-invokes the commit defensively before a run as a safety net.
- **Reveal gating.** `ServerSeedHash` is published as soon as it exists; the raw `ServerSeed` only once
  `Game.Status ∈ GameEndedStatus`.
- **Portable RNG.** Character selection is a pure function of the committed inputs:
  `block(counter) = HMAC-SHA256(key = UTF8(serverSeed), msg = UTF8(prefix + "|" + counter))`, where
  `prefix = gameId("D") + "|" + join("|", orderedBetSeeds)` and `orderedBetSeeds` are the bets' `Seed`s
  ordered by `(CreatedDate asc, Id ordinal asc)`. Keystream bytes map to character-pool indices by
  **rejection sampling** (`limit = 256 − 256 % poolSize`), so there is **no modulo bias**. The game
  mechanic (rolling-window match) is unchanged — only the RNG source.
- **Fallback winner** (character cap reached) is drawn from a dedicated
  `HMAC-SHA256(serverSeed, prefix + "|fallback")` label — reproducible regardless of how many characters
  streamed.
- **Voided holder tickets stay in the prefix (Space Jackpot, #196).** A ticket voided by the draw
  recheck keeps its `Seed` in `orderedBetSeeds` — it was committed at entry, and dropping it would make
  the stream depend on a post-entry RPC read. Only its **choice** leaves the race: the typer skips
  `Voided` bets, so the server's `orderedChoices` already exclude it. The verification payload lists the
  bet with **`voided: true`**; the verifier builds the prefix from **all** bets and takes the racing
  choices from `orderedChoices`. See [`space-jackpot-refactor.md`](space-jackpot-refactor.md).
- **Weighted fallback (house-edge types — Hyper Roll).** Natural matching is off, so the winner is
  always the fallback, drawn by `WeightedFallbackIndex`: a uniform draw over the board's **published
  integer weights** (32-bit rejection sampling on the same `|fallback` block). The weights ship in the
  verification payload and are always read, never recomputed. See [`hyper-roll.md`](hyper-roll.md).
  **Word Lines (#209, 2026-10-22):** the same draw runs over the round's **13 outcome rows**
  (`GameBoardChoices` = the published table, sorted ordinally by key) and picks the result row; the 5×3 grid
  is then built with ordinary randomness and is presentation only. `GET /games/{id}/verification` adds
  `BoardMode = LINES`, the grid and the hits. The algorithm is unchanged; a **V4 known-answer vector** over the
  13 weights is pinned in `ProvablyFairTests` and quoted in `provablyFair.ts` / `verify.html`. See
  [`hyper-roll-word-lines.md`](hyper-roll-word-lines.md).
- **Client seed + nonce (Stake-style).** Each player holds a persistent client seed (localStorage,
  rotatable in the header gear modal); each bet carries an incrementing `nonce` and sends
  `Bet.Seed = HMAC-SHA256(key = clientSeed, msg = String(nonce))`. ⚠️ **The raw `clientSeed` never leaves
  the browser** — only the derived `Bet.Seed` reaches the server. Rotating the seed resets the nonce to
  0. Free bets still carry a seed + nonce.
- **Two verification layers.** (a) Anyone reproduces the outcome from `ServerSeed` + canonically ordered
  `Bet.Seed`s and confirms `SHA256(ServerSeed) == ServerSeedHash`. (b) A bettor privately confirms their
  `Bet.Seed == HMAC(theirClientSeed, nonce)` — proof their entropy was used unmodified.
- **Storage (greenfield).** `Game.Seed` (`int?`) removed; `Game.ServerSeed` + `Game.ServerSeedHash` (64)
  and `Bet.Nonce` added; `Bet.Seed` widened 40 → 64. Historical rows are not back-fillable — old int
  seeds cannot reproduce the HMAC stream.

## 2. The verification endpoint

`GET /games/{id}/verification` (public, reveal-gated) returns `serverSeedHash`, the ordered bet
seeds/choices/nonces, `characterPool`, `queueLength`, `maxCharacters`, `charactersTyped`, and — once
Ended — `serverSeed`, `winningChoice`, `winnerDecidedByFallback`. The raw `clientSeed` is never
returned.

- ⚠️ **The server never replays the stream.** `Game.CharactersTyped` (`int?`) is persisted when the run
  finishes, in the **same `SaveChanges` as `WinningChoice`**, so a game is never observable as
  ended-with-no-count. `winnerDecidedByFallback` is **derived** (`CharactersTyped >= typer.MaxCharacters`).
- ⚠️ **`null` means unknown, never `false`.** A legacy/never-run row reports the flag as `null`; claiming
  `false` would be a lie. The client's own recompute can still determine it locally.
- **Only a settled round is cached** (`GameVerification:{gameId}`, 12h). The DTO carries `Status`, and
  `Ended → RewardForWinnersInProgress → RewardForWinnersCompleted` happens seconds later — caching at
  `Ended` would pin a stale status for the whole TTL. Caching requires a final status **and** a revealed
  seed **and** a populated count. Every field is immutable by then, so **no invalidation path exists or
  is needed**. Pre-`Ended` responses are never cached.
- *(The operator check that once ran before the cache read is moot since
  [`remove-operators.md`](remove-operators.md) — the endpoint is public and there are no tenants to leak
  a round across, so the cache is read first.)* One indexed `GetGameById` stays on the cached path; the
  cache eliminates the bets query, the typer construction and the DTO rebuild. Unknown games keep the
  vague `GameNotValidForBets` message on both paths.
- **Pool + cap are PERSISTED, not re-derived** (since `typer-refactoring`, 2026-09-02): a finished
  run writes `Game.CharacterPool` + `Game.MaxCharacters` in the same `SaveChanges` as
  `WinningChoice`, and the endpoint publishes those verbatim — a later derivation or settings change
  can never shift a settled round's published inputs. A finished round with **NULL** columns
  predates the `TyperRunSnapshot` migration and reproduces the **pinned legacy derivation** (full
  alphabet / old cap math: race 8/18/30, lottery 10/30/45, ceiling 5M, safety 0.9 —
  `GetGameVerificationHandler.LegacyCharacterPool`/`LegacyMaxCharacters`). A not-yet-run round
  publishes the live derivation, which is what its run will use. The fallback flag derives against
  the same published cap, so it can never contradict the client's replay. (This replaced the old
  "MaxCharacters drift is accepted" caveat — drift is now impossible for settled rounds.)

- **Monkey Duel (#184) adds the house word draw** — [`monkey-duel.md`](monkey-duel.md). The payload
  gains `isDuel`, `duelKind`, `duelPoolSnapshot` (the ordinal-sorted pool snapshotted when the round
  opened — a later pool edit never changes it) and `houseWord`. The house word is
  `DuelHouseWordGenerator.DeriveWord(serverSeed, gameId, attempt, pool)` (prefix
  `duel|{gameId}|{attempt}`, `Keystream` + `NextIndex`), taking the first attempt whose word differs
  from the creator's. `houseWord` is resolved from the round's `ClaimedByPlayerId`, so a HouseSeed
  wallet rotation never blanks it. ⚠️ The verifier **fails closed** on a House duel with missing inputs
  (`houseWordOk = false`, reported "not published", distinct from "mismatch"). KEEP-IN-SYNC with
  `provablyFair.ts` / `verify.html`.

## 3. The in-app FAIR panel

- **Housing:** `GameSidePanel` is **chat-only**; `GameFairPanel` renders inside the header-menu **Fair
  modal** (`GameFairModal`) — an always-on how-to-verify explainer above the reused panel. Off `/game` it
  shows a "watch a round to verify it" empty state (no fetch). ⚠️ **Hidden when embedded**
  (`useEmbed()`). See [`embedding.md`](embedding.md) and [`stats-and-history.md`](stats-and-history.md).
- **Locked until the round reaches a terminal status.** While running it shows only the committed
  `serverSeedHash` and "verification unlocks when this round ends". ⚠️ The commitment is fetched from the
  **endpoint**, not from `gameInstance` — the backend commits the seed on the first bet via a raw
  `UPDATE` that broadcasts nothing, so a bet placed while the tab is open would otherwise never surface
  a hash.
- On unlock, **two instant checks** run — a fixed handful of WebCrypto calls, never a replay:
  1. **Commitment** — `SHA256(UTF8(serverSeed)) == serverSeedHash`.
  2. **Your bet seed** — `HMAC-SHA256(clientSeed, String(nonce))` recomputed per bet and matched against
     the payload's ordered bet seeds. ⚠️ **A non-match is never a failed check** — it renders as "no
     verifiable bet from this browser (spectator, or the client seed was rotated since this bet)".
- **The full recompute is opt-in** behind `RECOMPUTE OUTCOME`, runs in a **cancellable Web Worker** with
  a progress bar sized by `charactersTyped`, and warns with an estimated duration above 250,000
  characters. A mismatch is reported loudly and never auto-retried; a worker that dies surfaces its error
  rather than silently reverting to the button. Cancellation is `terminate()`, deliberately not a
  message, so it cannot be missed between progress ticks.
- **Rollover retention:** the panel keeps the last ended round (labelled with its short round id) when
  the next round starts, and a running recompute is **not** cancelled by the rollover. The retained round
  is replaced only when the *next* round ends. The worker **is** terminated on unmount, so a closed panel
  never burns CPU.
- A round **nobody bet on** never committed a seed: "no bets this round — nothing to verify", no
  recompute offered.
- **`COPY VERIFICATION JSON`** hands over the raw payload for any third-party verifier.
- ⚠️ **Objects crossing to the worker must be plain.** `postMessage` uses structured clone, which rejects
  Vue reactive Proxies with `DataCloneError: [object Array] could not be cloned` — the retained round is
  held in a `shallowRef` and the message is built from `toRaw` with copied arrays.

## 4. The offline verifier

**`public/verify.html` is kept** — this reverses the original card's "delete it" decision. The in-app
panel runs code Space Stroker ships and serves; the standalone file is one self-contained page a player
**saves and runs offline**, with nothing of ours executing live — the "don't trust us" path. The FAIR
panel links it with a `download` and step-by-step instructions.

- It independently reproduces the winner, the SHA256 commitment **and the character count**,
  cross-checking the count against `charactersTyped` (the fallback flag is derived from that number, so a
  wrong count would mean a wrong flag).
- It can download the **last 1,000 reproduced characters** — the run-up to the winning sequence — via a
  fixed-size ring buffer, so a capped 5,000,000-character round costs the same as a short one.
- ⚠️ **It keeps its own inline copy of the algorithm.** `ProvablyFair.cs`, `src/utils/provablyFair.ts`
  and `public/verify.html` must stay byte-identical. **The KEEP-IN-SYNC comments in all three are
  load-bearing.**

## 5. Reach

The FAIR panel covers the **current round only** — no game-id lookup. Re-checking older rounds ships via
the **Games History** page ([`stats-and-history.md`](stats-and-history.md)): its per-row Verify button
fetches the same payload for any completed round and hands it to the offline verifier — **link-out only,
no in-app replay on the history page**.

## Key code touchpoints

**Backend (`monkify-backend`)**

| File | Role |
| --- | --- |
| `SpaceStroker.Domain/Games/Services/ProvablyFair.cs` | Keystream primitives (`GenerateServerSeed`, `HashServerSeed`, `BuildPrefix`, `Keystream`, `NextIndex`, `FallbackIndex`, `WeightedFallbackIndex` for house-edge boards) |
| `SpaceStroker.Domain/Games/Services/SpaceStrokerTyper.cs` | Drives characters + fallback from the keystream |
| `SpaceStroker.Infrastructure/Services/Games/GameService.cs` | `EnsureServerSeedCommitted`; `UpdateGameStatus` writes `CharactersTypedCount` with `WinningChoice` |
| `SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/*` | Commit on first bet, persist `Nonce` |
| `SpaceStroker.Infrastructure/Handlers/Games/GetGameVerification/*` | Derived flag, cache read/write |
| `SpaceStroker.Infrastructure/Contracts/Games/GameVerificationDto.cs` · `ActiveGameDto.cs` | Payload + `ServerSeedHash` |
| `Migrations/*_ProvablyFair.cs` · `*_GameCharactersTyped.cs` · `*_TyperRunSnapshot.cs` | Applied only by `SpaceStroker.Consumers` |

**Frontend (`monkify-front`)** — `src/utils/provablyFair.ts` (portable core, mirrors the C#),
`src/hooks/useClientSeed.ts`, `src/components/game/GameFairPanel.vue`,
`src/workers/verifyRound.worker.ts`, `public/verify.html`, `src/services/mainService.ts`
(`getGameVerification`), `src/types/index.ts`.

⚠️ The `> SERVER SEED HASH:` commitment line was **removed from the game terminal on 2026-07-25** (a
64-char hash that wrapped over two lines and crowded the pre-bet view). The terminal shows only the
`> SERVER SEED:` reveal after Ended; the commitment lives in the FAIR panel and `verify.html`.

**Tests** — `ProvablyFairTests`, `GetGameVerificationHandlerTests` (derived flag, cache-only-when-final),
`GameServiceTests` (count write), and integration `ProvablyFairFlowTests` (happy path, identical repeat
payload, fallback).

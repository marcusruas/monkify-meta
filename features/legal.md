# Feature — Legal documentation & per-wallet self-exclusion

> Status: **shipped.** Authoritative spec: `../specs/legal-docs-self-exclusion.md`
> (merged via `feature/legal-docs-self-exclusion` on 2026-07-29). Trello #125
> ([3kPjRxsJ](https://trello.com/c/3kPjRxsJ)).
> Repos: `monkify-backend` + `monkify-front` + `monkify-lp` + `monkify-meta`.

## What it does

Space Stroker takes real money from anonymous crypto players, so it now says so out loud and gives
players a way to stop. Four pieces: an **18+/Terms entry gate** on both public properties, a
**Terms & Conditions** page, a **responsible-gambling** page, and a **per-wallet self-exclusion**
that genuinely blocks bets on the server.

Shipping the fourth piece exposed — and required fixing — a pre-existing money bug: the inline
"reject and refund" paths in `RegisterBetHandler` **never actually refunded anything**. See
"The refund fix" below; it changes behavior for four already-shipped rejection paths, not just this
feature.

## Core rules

**The entry gate**

- A blocking overlay on first visit to **either** `www.spacestroker.com` or `app.spacestroker.com`,
  with **ACCEPT and DECLINE given equal weight** — a consent with no way to refuse is not evidence of
  consent. DECLINE is terminal: a short "you can't use Space Stroker" panel with a link to the
  responsible-gambling page and no route into a game.
- The answer is an **`ss_terms`** cookie (180 days, `SameSite=Lax`, `Secure` on https) scoped to
  **`.spacestroker.com`**, so one acceptance covers the LP→app hop. The `domain` attribute is
  **omitted** on any host that is not `*.spacestroker.com` — a domain the browser rejects writes no
  cookie at all and would re-prompt forever. Exactly the `ss_consent` contract from
  `analytics.md`, and the two repos keep their own copy of it.
- The value is a **version string** (`v1`), not a boolean: bumping it re-prompts everyone after a
  material change to the Terms, with no new mechanism and no migration.
- **The cookie-consent banner renders only after the gate is accepted**, so a first-time visitor never
  faces two overlays. Merging the two prompts was explicitly **rejected**: consent to analytics must be
  freely given and separable from accepting the Terms, and bundling them would invalidate the cookie
  consent that `analytics.md` depends on.
- **Not shown** when embedded (`useEmbed()`), on `meta.chromeless` routes
  (`/wallet-bridge`, `/background`), on `/error`, in the admin portal, or on
  the LP's own legal pages — someone in trouble must reach the responsible-gambling page and its help
  links **without** accepting anything first.

**The pages** (hosted on the LP: the app is `Disallow: /` + `X-Robots-Tag: noindex`, see
`robots-txt` — legal copy has to be indexable)

- `/terms` and `/responsible-gambling`, siblings of `/cookies`, linked from the LP footer and picked
  up by the sitemap automatically.
- The Terms are written against **what the code actually does**: non-custodial (we never hold keys),
  SOL on Solana, no accounts, provably-fair verification, the house cut (**"currently 1%"** since
  2026-08-04; 0.5% before) taken only from profit above the player's own stake, and **exactly when a bet
  is and is not auto-refunded**. They state plainly that they are **not legal advice and have not been
  reviewed by a lawyer**.
- ⚠️ **The house-cut rate is the one number the Terms hardcode, and it goes stale silently.** The rate
  lives in backend config, every in-app badge renders it live, and this page does not — it was found
  still claiming 0.5% after the rate rose. The wording now says *"currently 1%"* and points at the
  in-app STATS panel as the live figure, but **any rate change must still update
  `monkify-lp/src/pages/terms.astro`** (see `features/crypto-commission.md`).
- ⚠️ **No restricted-jurisdictions clause.** We do not geo-block, and an unenforced clause is worse
  than silence. Geoblocking is a separate, unclaimed card.
- ⚠️ **No legal entity and no governing law are named** — a deliberate known gap, not an oversight.
  Contact is `space.stroker@gmail.com` + Telegram `@SpaceStroker`.
- The responsible-gambling page states the tool's **honest limitation**: self-exclusion applies to the
  wallet you sign with, and connecting a different wallet bypasses it. It points at BeGambleAware,
  GamCare, Gamblers Anonymous and Gambling Therapy for anything stronger.

**Self-exclusion**

- Flags the **`Player`** row, which is keyed by the wallet — so this is literally "per wallet".
  (It used to be an `OperatorPlayer` scoped to one tenant; there is one deployment now.)
- Two columns: **`SelfExcludedAt`** is the flag (non-null ⇒ excluded) and **`SelfExcludedUntil`** the
  expiry, where **null with the flag set means PERMANENT**. One nullable expiry could not tell "never
  excluded" apart from "excluded forever", which is why there are two.
- Durations **24h / 7d / 30d / 90d / permanent**, and **it cannot be lifted early** — by the player or
  by us. There is no support desk and no account recovery, so expiry is the only reversal that can
  honestly be offered. Permanent therefore requires a second confirmation naming the wallet.
- **Extend-only:** a shorter duration never shortens an active block, and nothing downgrades a
  permanent one. A lapsed exclusion can be set again from scratch.
- **Blocks paid *and* free bets.** A "take a break" that still lets you play is not a break.
- **Expiry needs no scheduled job** — `IsSelfExcluded(now)` is evaluated per bet and per preferences
  read, so a lapsed block lifts itself.
- An excluded player **can still watch** the round; only betting is closed.
- Bets **already placed** when the exclusion is set settle and pay out normally. Refusing an owed
  payout would be taking the player's money.

**Proving the wallet is yours**

- Setting an exclusion requires an **ed25519 signature** over a canonical, human-readable message
  binding the wallet, the duration and a **single-use nonce** (Redis, 5-minute TTL). Verified with
  `Solnet.Wallet`'s `PublicKey.Verify` — **no new NuGet**.
- This is not ceremony. Wallet addresses are public — they even ride live bet notifications
  (`wallet-display-name.md`) — and an exclusion cannot be lifted, so an unproven request would let
  anyone permanently lock anyone else out.
- The nonce is consumed **only on a successful verify**, so a rejected or mistyped signature does not
  force a fresh round-trip. Every rejection returns one vague message: it must not reveal which half
  failed.

**Enforcement — three layers**

1. **Pre-pay guard (the normal case).** The SPA knows the player is excluded and never opens the
   wallet: `allowBets` folds it in, so the lever, the mobile keypad and PLACE BET all go inert at once
   rather than bouncing off a guard. No money moves.
2. **Backend authority.** The check runs in `RegisterBetHandler` **before `ValidateSolanaPayment`** —
   i.e. before the handler validates (and thereby accepts) the on-chain payment — and for **every**
   game always. *(It was deliberately not routed through the opt-in per-operator
   `IPreBetCheckResolver`, which never had an adapter configured — that hook has since been deleted
   outright, leaving self-exclusion as the only RG control that runs.)*
3. **Refund backstop.** A stale tab or a direct API call can still transfer before we reject; that
   stake is refunded (see below).

## The refund fix (behavior change beyond this feature)

Every inline `RefundInvalidBet` call in `RegisterBetHandler` runs **before** `InsertBet`, so the event
named a `Bet` row that had never been persisted, `RefundInvalidBetConsumer.LoadBet` returned `null`,
and the consumer logged a warning and returned. **No money moved** — while the player was shown
`ErrorMessages.RefundWarning` promising a refund. This affected four shipped paths:
choice-already-taken, duplicate bet, game-not-valid-for-bets, and invalid choice.

Fixing it by persisting the rejected bet as `NeedsRefunding` was **rejected**: nothing filters bets by
status (`GameService.GetBetsByGameId` has no status predicate and feeds **both the typer and the
provably-fair replay**; `RunGameConsumer` promotes winners with no status check; `Bet.PublicBetStatus`
is declared and never referenced), so a persisted rejected bet could enter the typer's target set,
**win a round**, and change a verified outcome.

Refunds are therefore issued **bet-less, from the event**:

- `RefundInvalidBetEvent` carries `GameId` + the payment signature. Publishers that *do* have a
  persisted bet (the 12h sweep, the admin refund) leave both null and take the unchanged bet-backed path.
- The consumer resolves the game's registered token and refunds a **detached, never-saved** `Bet`
  carrying the wallet and amount, with `Id` left empty so the ledger row records a null `BetId`.
- **Idempotency** keys on the indexed **`TransactionLog.RelatedSignature`** — the payment a
  refund reverses. It could not key on `Signature`, which holds the **refund's own** transaction; that
  mistake shipped in the spec and was caught only by the integration test.
- The same treatment now applies to the RG adapter's `Denied` and `Unavailable` arms, which also
  rejected paid crypto bets without refunding.

⚠️ **Known limitation:** a bet-less refund that exhausts its retries has **no automated recovery** —
the 12h `PendingRefundSweepJob` only picks up `NeedsRefunding` **bets**, and `POST /admin/bets/refund`
finds bets by `Bet.Signature`. It logs at `Error`; recovery is a manual on-chain
transfer.

## Key code touchpoints

**Backend (`monkify-backend`)**

- `SpaceStroker.Domain/Games/Entities/Player.cs` — `SelfExcludedAt`, `SelfExcludedUntil`,
  `IsSelfExcluded(now)` (the single source of truth for "blocked").
- `SpaceStroker.Domain/Games/ValueObjects/SelfExclusionDuration.cs` — the enum + `ToTimeSpan()` and
  `ToDisplayText()`. **The display text goes into the signed message**, so it is mirrored in
  `monkify-front` and pinned by a test.
- `SpaceStroker.Domain/Bets/Entities/TransactionLog.cs` — `BetId` nullable, `RelatedSignature`
  (the columns survived the ledger merge; `CryptoTransactionLog` was folded in and dropped).
- `.../Handlers/Players/GetSelfExclusionNonce/*`, `.../SetSelfExclusion/*` (`BuildMessage` is the
  canonical message; signature verify; extend-only upsert).
- `SpaceStroker.Api/Controllers/PlayersController.cs` — `GET /players/self-exclusion/nonce`,
  `POST /players/self-exclusion`. `/players` is already in
  the nonce is now **purpose-scoped** (`self-exclusion` vs `preferences`) so it cannot be replayed
  across flows. Both endpoints are public — the ed25519 signature *is* the authentication.
- `.../GetPlayerPreferences/*` — `selfExcluded` + `selfExcludedUntil` on the existing boot read.
- `.../RegisterBet/RegisterBetHandler.cs` — the exclusion check, the enriched refund event, the RG
  deny/unavailable refunds.
- `.../Consumers/RefundInvalidBet/*` — the bet-less branch; `.../Services/Solana/SolanaService.cs`
  — null-`BetId` ledger write + `RelatedSignature`.
- Migration `AddSelfExclusionAndCryptoLogRefundLink`.
- Tests: `PlayerSelfExclusionTests`, `SetSelfExclusionHandlerTests` (real keypairs),
  `RefundInvalidBetWithoutBetTests`, self-exclusion cases in `RegisterBetHandlerTests`, exclusion cases
  in `GetPlayerPreferencesHandlerTests`; integration `SelfExclusionFlowTests`.

**Frontend (`monkify-front`)**

- `src/utils/legalGate.ts` — the `ss_terms` contract and the LP page URLs; `src/components/LegalGate.vue`
  (since #124: the shared `ui/ArcadeModal.vue` shell, grey panel, `layer="legal"` = `z-legal` 80,
  `dismissible: false`, dither backdrop — never `backdrop-filter`); `App.vue` mounts it and gates
  `ConsentBanner`. ⚠️ **`ResponsibleGamingModal.vue` no longer exists** — Trello #130 (2026-08-02)
  converted it into the **`/responsible-gaming` page**, which keeps the same **grey** material
  (`.arcade-frame .arcade-frame--grey`), so the conversion was material-preserving.
- `src/hooks/useSelfExclusion.ts` — `buildSelfExclusionMessage` (byte-identical to the backend),
  state, `exclude()` (returns `'submitted' | 'hopped'`), `resumeFromDeeplink()`.
- `src/pages/ResponsibleGamingPage.vue` (route `ResponsibleGaming`, `/responsible-gaming`) + the
  `GameHeader.vue` ☰ `RouterLink` and the `HomeFooter.vue` link, both hidden when embedded — and the
  route itself **redirects to `GameLobby` when embedded**, via `main.ts`'s `EMBED_REDIRECT_ROUTES`.
  It is **not** an `isPlayerRoute()`, so it loads preferences itself on mount and on a wallet change.
  ⚠️ **The mobile deeplink return targets the route now.** `rememberReturnTo()` stores
  `pathname + search` before the hop, so a hop started on `/responsible-gaming` comes back to it.
- `src/hooks/useGame.ts` — the pre-pay guard and the deeplink-return branch;
  `src/components/game/GameConsole.vue` — `allowBets` folds in the exclusion.
- `src/utils/mobileWalletProtocol.ts` — `buildSignMessageDeeplink` / `parseSignMessageReturn`;
  `src/hooks/useMobileWallet.ts` — the pending-self-exclusion stash and return decode.
  ⚠️ A wallet's return params are **identical** for `signAllTransactions` and `signMessage`, so what was
  stashed before leaving is the only thing that tells the two hops apart.
- `src/hooks/usePlayerPreferences.ts` — `loadPreferences` folds the exclusion state in, so every caller
  keeps the guard current with no extra request.
  ⚠️ **This was FALSE from `remove-operators` until Trello #130 (2026-08-02).** That rewrite dropped
  the call and left `applySelfExclusionPreferences` a **dead export** — defined in `useSelfExclusion.ts`,
  invoked from nowhere. **Consequence for that whole window:** after any page load `selfExcluded` was
  `false`, the RG surface always rendered the *not-excluded* branch, and the client pre-bet guards
  (`useGame.ts`, `GameConsole.vue`'s `allowBets`) were dead code. The backend kept enforcing exclusion
  in `RegisterBetHandler` throughout, so it was a UX/trust regression, **not** a money hole. #130 wired
  the call back in, which means those client guards are **live again on the money screen**.

**Landing page (`monkify-lp`)**

- `src/utils/legalGate.ts`, `src/components/LegalGate.vue` (`client:load` — a legal gate must not render
  late), `src/layouts/Layout.astro` (`legalGate` prop, default true; the legal pages set it false),
  `src/pages/terms.astro`, `src/pages/responsible-gambling.astro`, `src/components/Footer.vue`.

## Known gaps

- **The mobile deeplink `signMessage` hop is device-unverified.** It mirrors the shipped transfer hop
  and compiles, but no phone was in the loop at ship time.
- **No legal entity or governing law** is published (deliberate — see above).
- ~~**The crypto RG-deny path has no integration test.**~~ Moot: the RG pre-bet hook was deleted with
  operators. *(Original note: the RG fixture operator seeded only `Standard`
  game types, and giving it a crypto catalogue collides with the existing RG seeds. Covered by two unit
  tests, and the refund mechanism itself is proven end-to-end by `SelfExcludedPlayer_PaidBet_IsRefunded`.)*

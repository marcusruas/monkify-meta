# Feature — Referral program

> Status: **shipped.** Authoritative spec: [`../specs/referral-program.md`](../specs/referral-program.md)
> (merged via `feature/referral-program` on 2026-08-21). Trello
> [#175](https://trello.com/c/GPgZJksn/175-referral-program).
>
> ⚠️ **This card also raised the house commission 1% → 2%.** That half is documented in
> [`money.md`](money.md); everything here is the attribution half.

## What it does

A partner — a streamer, a community — hands out `https://spacestroker.com?ref=<code>`. Every **paid**
bet placed by a player who arrived through that link is stamped with the partner's code, including
the player's **Orbit auto-bets**, so a report can total what that traffic wagered and the partner is
paid a share of it.

⚠️ **The product pays nobody.** There is no accrual table, no job, no outbound transfer, no admin
screen and no player-facing UI. Its entire job is to record *who sent whom*; the money is moved by a
person reading `deploy/sql/referral-payout-report.sql`. This is the [Feedbacker
Program](feedbacker.md) shape — an arrangement run by hand, with code doing only the part code is
good at.

## Core rules

### Attribution is first-touch, wallet-bound and permanent

- `Player.ReferralCode` is written **only when it is NULL**. A later link never overrides it, on any
  path. The programme pays for **acquisition**, so the partner who brought a wallet keeps it — and a
  mutable column would let one partner take another's established player with a single click, and
  would retroactively change history a partner had already been paid on.
- `Player.ReferredAt` records when that write happened. ⚠️ Deliberately **not** `CreatedDate`: a
  `Player` row is created by five different paths (a paid bet, an Orbit session, the display-name
  prompt on wallet connect, self-exclusion, the house seed) and can predate attribution by days.
- **An existing player cannot be claimed.** Someone who has already bet has a row, and a code arriving
  later finds it non-NULL.

### The code is only accepted where wallet control is already proven

- Two write sites: `POST /games/{id}/bets` (which carries a confirmed on-chain transfer from that
  wallet) and `POST /orbit/session` (nonce + ed25519 signature). Both already prove the caller owns
  the wallet, so the referral rides along on a proof that was being demanded anyway.
- ⚠️ There is deliberately **no** `POST /players/referral`. Player endpoints are public and a wallet
  address is not a secret, so an unsigned write keyed on one would let anyone attribute any wallet on
  the chain to their own code — the same reasoning that makes `PATCH /players/preferences`
  signature-gated.
- On the Orbit path the write happens **after** `OrbitProof.TryConsume` succeeds, never before.

### One stamp site covers Orbit

- `Bet.ReferralCode` is copied from the **`Player` row** in `RegisterBetHandler`, not from the request
  body.
- ⚠️ **That is what makes an Orbit auto-bet carry the referral.** `PlaceOrbitBetConsumer` sends an
  in-process `RegisterBetRequest { FundedByOrbit = true }` with a body it builds itself, which has no
  referral on it — there is no browser. Reading the body instead of the player row would leave every
  Orbit bet NULL. `PlaceOrbitBetConsumer` and `OrbitBetPlacementService` needed **no change at all**.
- The `POST /orbit/session` capture is still load-bearing for the other direction: a player can enrol
  in Orbit **before ever betting**, and their first bet is then placed server-side.
- The stamp is denormalized on purpose — it **freezes** each bet's attribution at placement, so a
  future change to the attribution policy cannot move history a partner was already paid on.

### Partners are a TABLE with an `Active` column, and an unknown code is silent

- **`ReferralPartners`** holds `{ Code, NormalizedCode, DisplayName, SharePercentage, PayoutWallet,
  Active }` per partner. `NormalizedCode` is UNIQUE; `PayoutWallet` is unique **filtered on NOT
  NULL**, so many partners may sit at "not set up to be paid yet" at once. Those two indexes are
  the guards that used to be startup throws in `ReferralDirectory`.
- ⚠️ **This replaced config (#177).** #175 put the whole programme in
  `GeneralSettings:Operator:Referrals` because no admin token can be minted in production
  ([`admin-and-ops.md`](admin-and-ops.md)). That never actually reached production: the key was
  never added to `base_secret_ids`/`base_secret_env` in `deploy/terraform/workload/main.tf`, so a
  deployed environment only ever saw the committed MOCK partner. Hand-run SQL against the table
  reaches production; a config edit did not.
- `GeneralSettings:Operator:DefaultPartners` survives only as a **create-only seed**, run at
  Consumers startup exactly like `DefaultGames`. ⚠️ An existing row is never rewritten from
  config, so a restart cannot resurrect a deactivated partner or move a `SharePercentage` /
  `PayoutWallet` out from under money already earned. A `MOCK_` wallet is seeded as NULL.
- ⚠️ **`Active` gates ATTRIBUTION ONLY.** Turning a partner off stops new bets carrying their
  code — including bets from players **already** referred to them, because the bet path
  re-resolves the player's stored code through the directory rather than copying it — so their
  turnover stops growing. It touches **no history**: earlier bets keep their code, the balance
  stays payable, and the partner can still sign in and cash out. Refusing to pay an earned
  balance would be a liability, not an off switch.
- The directory is served by `IReferralDirectoryProvider`, which caches the whole table for
  **60s** (`ReferralPartners` key). ⚠️ That cache is what keeps attribution off a
  per-bet query, and it bounds how long a just-deactivated partner keeps earning: up to one
  minute. Seeding and any write call `Invalidate()`.
- `ReferralDirectory` is the only place a raw code is resolved. It **fails closed** — null, blank,
  malformed, over-length and not-configured all resolve to null, which downstream means "not
  referred", never an error. ⚠️ A caller must not be able to probe which codes exist
  (`constraints.md` §6): an unknown code produces a response byte-identical to a bet carrying none.
- Matching is case-insensitive and trimmed, but the **configured** spelling is what gets stored — one
  canonical value per partner, or their earnings split across two buckets in the report with no way to
  merge them.
- ⚠️ **Duplicate codes in config throw at startup.** Two partners sharing a code means one is paid for
  the other's traffic. A *malformed* configured code is skipped instead, not thrown on: it can never
  be matched by a URL, so failing the whole deployment over a typo in one partner row would be worse.

### Capture, and the host hop

- The browser accepts `?ref=<code>` on **both** hosts. The apex and `www.` serve the LP; the game is a
  different origin at `app.` and every "Play now" is a hard-coded `GAME_URL` with no parameters, so
  the LP carries the code across by adding `'ref'` to the **existing campaign-forwarder allowlist**
  (`CAMPAIGN_KEYS` in `monkify-lp/src/layouts/Layout.astro`) — the same stash-and-rewrite path
  `utm_*` already rides. ⚠️ That block sits **outside** the `gaId` guard, so forwarding works in
  environments where GA4 is off.
- ⚠️ The LP's `sessionStorage` stash is **not** the attribution window — it only has to survive the
  visit. The app owns the real window: `{ code, capturedAt }` in `localStorage` for **30 days**,
  after which the entry is **cleared**, not merely ignored, so a stale code cannot sit in a browser
  waiting to mis-attribute someone who came back on their own.
- The parameter is **stripped from the URL** on capture, keeping it out of GA4's `page_location`, out
  of screenshots, and out of a link a player might paste to a friend. ⚠️ The strip removes **only**
  `ref` — unlike `useAdmin`'s, which drops the whole fragment — preserving the path, other query
  parameters and the hash.
- ⚠️ **The SPA validates shape only** (`[a-z0-9_-]`, 1–32 chars); membership is the backend's call.
  Publishing the allowlist to the browser would expose every partner's code and name.
- ⚠️ **There is a SECOND hop, and the strip above is what makes it dangerous.** Opening the game in a
  wallet's in-app browser (`openInWalletBrowser`, the Tier-2 path and the Tier-1 "open in app
  instead" fallback) is a jump into a **different browsing context with its own `localStorage`** — the
  stash does not travel. Because capture already removed `ref` from the address bar, handing the
  wallet a bare `window.location.href` arrived with **nothing**, so every player who connected that
  way was unattributed. `walletDeepLink.withReferral()` re-appends `?ref=<code>` to the URL given to
  the wallet; the webview boots the SPA, `captureFromUrl()` stashes it in **that** context and strips
  it again. On Android this is the default route for the Tier-1 wallets, so it was most of mobile,
  not an edge case.
- ⚠️ The re-append is for the **browse** links only. The Phantom/Solflare deeplink **protocol** hop
  (`useMobileWallet`'s `redirectLink()`) returns to the *same* browser, where the stash never left —
  adding a code there would only put back in the address bar what the strip just cleaned. Note the
  browse links carry their own `?ref=<dApp origin>` on the **outer** wallet URL; ours rides inside the
  percent-encoded inner URL, so the two never collide.
- The code is attached **centrally** in `mainService.applyBet` and `orbitService.createOrbitSession`,
  never at the call sites: ⚠️ `useGame.ts` builds a bet body in **four** places, two of them
  mobile-deeplink resume paths that rebuild it after a round trip through the wallet app.

### What counts as earnings

- Basis is **turnover**: `SharePercentage × Σ(paid stakes of that partner's players)`.
- Counted only for **paid** bets in rounds that reached `RewardForWinnersCompleted`, excluding
  refunded and pre-`SettlementCutoverUtc` bets — the same rule every other money read applies
  ([`stats-and-history.md`](stats-and-history.md)). Free bets are amount 0 and contribute nothing
  automatically.
- Bets from the partner's own `PayoutWallet` are excluded: the one hard self-referral guard.
- ⚠️ **Turnover is not profit.** At 2% the house retains 2% of *losing* stakes, which on a
  `MinimumNumberOfPlayers = 2` type (Cosmic Sprint, heads-up) is **1% of turnover**, rising to ~1.67%
  at six bets with one winner. **A `SharePercentage` above ~1% pays a partner more than their traffic
  earned the house.** Documented in `ReferralSettings` and in the SQL header; deliberately **not**
  enforced in code.

### Nothing leaks

- ⚠️ **One deliberate exception, added 2026-09-21:** the partner's own player roster
  ([`partner-player-roster.md`](partner-player-roster.md)) lists the players carrying their code, one
  row each — to the partner, behind their session, with wallets truncated and only the public display
  name. Nothing about a code reaches a PLAYER-facing contract, which is what the rest of this section
  is about.
- `ReferralCode` appears on **no** DTO, no SignalR event and no public endpoint — not `BetDto`, not
  `BetNotificationEvent`, not games history, stats or the verification payload. A partner's code is a
  commercial arrangement, not public information, and adding it to a shipped contract is not
  reversible.
- `/terms` carries a **numberless** REFERRALS clause: we may compensate third parties for referrals, a
  referral link confers no player benefit or obligation, and attribution is at our discretion. ⚠️ No
  percentage, cap or partner name — that page is hand-maintained and has gone stale before. Ungated,
  unlike the Feedbacker clause; the programme is not a token surface.

### Two decisions that look like omissions and are not

- ⚠️ **There is no index on `Bet.ReferralCode`.** `SpaceStrokerDbContext` already carries a MEASURED
  verdict against a speculative `Bet` index ("NO INDEX ON Status — tried, MEASURED, and removed"), and
  the payout report runs by hand, perhaps monthly: an index would buy ~40ms a month and cost an index
  write on **every** bet insert, forever. If it ever matters, add a **partial** index
  (`WHERE "ReferralCode" IS NOT NULL`) with measurements behind it — the way every other index on that
  table was decided. `Player.ReferralCode` has none either; every read of it is by the unique
  `ExternalPlayerId`.
- **No backfill.** Historical `Player` and `Bet` rows stay NULL and earn nothing. ⚠️ The consequence
  is that attribution only exists from the merge forward — traffic before it can never be credited.

## Key code touchpoints

**Backend (`monkify-backend`)**

| File | Role |
| --- | --- |
| `SpaceStroker.Domain/Games/Services/ReferralDirectory.cs` | `Normalize` + `Resolve`; fails closed; duplicate-code guard |
| `SpaceStroker.Domain/Configs/Entities/ReferralSettings.cs` | One partner; carries the `SharePercentage` bound as a comment |
| `SpaceStroker.Domain/Configs/Entities/OperatorSettings.cs` | `List<ReferralSettings> Referrals` |
| `SpaceStroker.Domain/Games/Entities/Player.cs` | `ReferralCode`, `ReferredAt` (write-once) |
| `SpaceStroker.Domain/Bets/Entities/Bet.cs` | `ReferralCode` (stamped at placement) |
| `SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` | Inline mapping, `HasMaxLength(32)`, **no index** (with the reasoning) |
| `Migrations/*_AddReferralAttribution.cs` | Three nullable columns; no backfill, no index |
| `.../Handlers/Games/RegisterBet/RegisterBetHandler.cs` | Resolve → NULL-only player write → `_bet.ReferralCode = player?.ReferralCode` |
| `.../Handlers/Games/RegisterBet/RegisterBetRequestBody.cs` | `ReferralCode` (on the body, unlike `FundedByOrbit`) |
| `.../Handlers/Orbit/CreateOrbitSession/CreateOrbitSessionHandler.cs` | Same NULL-only write, after the proof |
| `SpaceStroker.Infrastructure/DependencyInjection.cs` | `ReferralDirectory` singleton — a duplicate code fails the host at startup |

**Frontend (`monkify-front`)** — `src/utils/referral.ts` (stash, 30-day window, shape check),
`src/hooks/useReferral.ts` (capture + narrow strip), `src/App.vue` (first call in `onMounted`),
`src/services/mainService.ts` + `src/services/orbitService.ts` (central attach + clear on success),
`src/types/index.ts`.

**Landing page (`monkify-lp`, on `master`)** — `src/layouts/Layout.astro` (`'ref'` in `CAMPAIGN_KEYS`),
`src/pages/terms.astro` (the REFERRALS clause).

**Meta (`monkify-meta`)** — `deploy/sql/referral-payout-report.sql`. ⚠️ It hand-mirrors the schema and
**will rot silently**; re-check it whenever `Bet`, `Player`, `GameStatus` or `BetStatus` changes.

**Tests** — `ReferralDirectoryTests` (15 cases), referral cases in `RegisterBetHandlerTests` and
`CreateOrbitSessionTests` (including ⚠️ *writes nothing when the proof fails*), and integration
`ReferralAttributionFlowTests` — happy path, the dedicated **Orbit-stamping** test, never-override,
silent discard of an unknown code, and a contract check that no public response carries the code.

⚠️ Two test-harness facts worth knowing before extending these: seeding a `Player` through the
**handler's own** in-memory context throws an EF identity conflict (seed through a separate context,
as `OrbitHandlerTestsBase` does), and `IntegrationTestFixture` configures its partner via
`GeneralSettings__Operator__Referrals__0__*` env vars **before the hosts build** — `ReferralDirectory`
is constructed at DI time, so a test cannot add a partner afterwards.

## Follow-up

[#177 Partner earnings page](https://trello.com/c/FRzhCu07/177-partner-earnings-page-referral-cash-out)
supersedes the "report only" decision with a partner-facing page that pays automatically from a
dedicated referral account. Until it ships, the SQL report **is** the payment mechanism.

# Feature — Player profile, player tags, levels and color profiles (Profile V1)

> Status: **shipped.** Authoritative spec: [`../specs/player-profile.md`](../specs/player-profile.md)
> (merged via `feature/player-profile` on 2026-10-16). Trello
> [#186](https://trello.com/c/cF0gPauc) with [#206 Levelling system](https://trello.com/c/b4U9FiVd) folded in.
> Level **`Feature`**.
> ⚠️ **The header shipped differently from the spec body** (owner's call, 2026-10-16): there is **no ☰
> PROFILE cell**. A header **profile card** replaced the ☰ glyph and the menu is headed by who you are; the
> bar shows **one** balance. §"Header" below is what runs; the spec banner lists the drift. **The code wins.**

## What it does

Every wallet has a **public profile at `/player/:wallet`** that anyone can open, connected or not: name,
short wallet, joined date, a cosmetic **level** with a progress bar, the full personal record, a shelf of
**10 trophies** derived from rounds already played, and the last 10 rounds across all modes with Verify.
Every player name in the app's live surfaces is a tappable **player tag** that opens a **mini profile card**
over the current screen without interrupting the round. Each player picks one of **8 color profiles** that
paints their identity surfaces, and anyone can **DONATE SOL** straight to another player from their profile
or card. The home page keeps only a compact strip of the player's own record.

## Core rules

### The profile read — `GET /players/profile?wallet=` (API, public)

- Order: (1) trim; not a 32-byte base58 public key (`WalletAddress.IsValid`) → unavailable; (2) a house
  wallet (`HouseWallets.Resolve` — treasury, Orbit, HouseSeed, ReferralPayout; blank / `MOCK_` skipped) →
  unavailable; (3) load the `Player` row by `ExternalPlayerId` (**never creates one**); (4) an **active
  self-exclusion** → unavailable, checked **live on every request, before the cache**, so a fresh exclusion
  hides the profile at once; (5) no row → available and empty (name null, `joinedAt` null, 10 locked
  trophies, no rounds, level 0) and **not cached**; (6) otherwise the body is served from
  `PlayerProfile:{wallet}` (**60 s**) or computed, and `DisplayName` + `ProfileTheme` are stamped from the
  **live** row onto a copy (`with`), so a rename or theme change shows at once.
- ⚠️ **Unavailable is HTTP 200 `{ available: false }`, byte-identical for malformed, house and
  self-excluded** — never a 400/404, never a reason (`constraints.md` §6). Nothing on the wire marks a
  streamer wallet: an active streamer's profile is simply available with every trophy locked.
- Body: `wallet`, `displayName`, `joinedAt` (= `Player.CreatedDate`, which really means "first seen" — five
  flows create the row; accepted), `profileTheme`, `achievements` (always all 10, fixed order), `recentRounds`
  (≤ 10), `level { level, gambled, levelFloor, nextLevelAt }` (`nextLevelAt` null at the cap),
  `tokenSymbol`, `tokenDecimals`.
- The record figures come from the existing `GET /players/stats` (fetched in parallel by the front);
  **money is public** (owner: on-chain data). The front **discards** the stats when the profile says
  unavailable, because `/players/stats` itself still answers any wallet.

### Trophies — 10, derived on read, no table

- Qualifying bets `Q`: the player's bets in `RewardForWinnersCompleted` rounds, bet status not
  `NeedsRefunding` / `Refunded` / `Voided`, `EntryKind != Streamer`. **Every real round counts** — paid,
  free, holder ticket, Orbit manual and auto. Practice never reaches the server.
- **An active streamer wallet unlocks nothing** (all 10 locked, no queries); a streamer **play bet** never
  counts even after the wallet is deactivated.
- ⚠️ **Not floored at `SettlementCutoverUtc`** — trophies are money-free, so USDC-era rounds count (a
  USDC-era streak can unlock *Win streak 5* while the floored Best streak stat shows less; accepted).
- Won = case-insensitive `Choice == WinningChoice`. Unlock date = the round date (`EndDate ?? CreatedDate`)
  of the **earliest** qualifying round.
- Keys (wire identifiers; titles and hints live on the front): `firstSprintWin`, `firstHyperRollWin`,
  `firstDuelWin`, `jackpotWinner`, `hyperHit` (won a 10×+ word on a `HYPER` board), `rivalSlayer` (won an
  `OpenRoom` duel — VS HOUSE does not count), `crowdBeater` (won a Sprint round with **≥ 5** distinct
  players, house wallets excluded, anonymous free bets counted individually), `winStreak3`, `winStreak5`
  (running streak over rounds in date order; a round with any winning bet is a win), `explorer` (played all
  four modes; date = the latest of the four first-play dates).
- Mode comes from one shared classifier over `GameParameters` flags (`GameModeClassifier`, first match
  wins): `IsDuel` → `duel`, `IsHouseEdge` → `hyperRoll`, `LotteryParameters != null` → `jackpot`, else
  `sprint`. There is still no game-mode enum.

### Recent rounds

- The 10 newest completed rounds the player bet in, **across all modes** (streamer play rounds included,
  unmarked), one row per round: the winning bet, else the earliest. Row: mode, type name, board mode,
  `duelVsHouse`, choice, stake (0 for free / ticket), `entry` (`paid` | `free` | `ticket`; streamer →
  `paid`), won, payout (Σ `Credit` rows; a streamer bet reads `IStreamerLedger.WonFor`; holder credits are
  shown — the money exclusion applies to aggregates, not to one round's row), `viaOrbit` (= `FundedByOrbit`).
- Each row has **Verify** (the History page's verification download, shared via `utils/verifyRound.ts`). No
  pagination; the owner gets **All history →** `/game/history`.

### Levels (#206) — cosmetic, no perks

- **Basis = Σ `Bet.Amount`** over bets with `Amount > 0`, a player, `EntryKind != Streamer`, in
  `RewardForWinnersCompleted` rounds of **every** type, **floored at `SettlementCutoverUtc`** (two-arm
  sargable form, never `COALESCE`). Orbit-funded bets count; free bets and holder tickets add nothing. Same
  predicates as the gamble race, all-time.
- Curve (`PlayerLevels`, the **only** source of thresholds — the front renders `levelFloor` / `nextLevelAt`
  from the API and never re-derives it): L1 = 0.2 SOL, L2 = 0.5, L3 = 1, then ×`10^(6/117)` ≈ 1.1253 per
  level (rounded to 2 dp), so L120 ≈ 1,000,000 SOL; **capped at L200**; below 0.2 SOL = level 0. Lookup is a
  binary search, so a boundary value lands on the higher level. Retuning is a code change, no migration —
  levels are never persisted.
- ⚠️ **Levels are public for every valid non-house wallet, with no streamer or self-exclusion filter** —
  hiding a badge would make its absence the signal. An excluded wallet's profile and card still say
  "Profile unavailable".
- **`GET /players/levels?wallets=a,b,…`** (public): split, trim, distinct, **first 50** inputs, drop
  malformed and house wallets — **never an error**. Per-wallet cache `PlayerLevel:{wallet}` (**60 s**,
  value `{ Level, Theme }`, level 0 cached too); all misses go to **one** grouped aggregate plus one indexed
  read of explicit themes. Response `{ levels: { wallet: n }, themes: { wallet: key } }`: `levels` only for
  level ≥ 1, `themes` only for wallets with an explicit pick (level 0 included). Absent = no badge / default
  theme. No gambled figure on this read (hot path).
- Freshness: a level may lag a settled round by up to ~60 s. No level-up notification, no SignalR push.

### Color profile — 8 preset themes

- `Player.ProfileTheme` (`string?`, max 20, migration `20261013185904_AddPlayerProfileTheme`, no backfill).
  Keys: `amber` · `comet` · `nebula` · `toxic` · `ocean` · `aurora` · `sunset` · `solar` (exact,
  case-sensitive; `ProfileThemes`). No red, no violet. Colors live only on the front
  (`constants/profileThemes.ts`).
- `null` = never picked → a **wallet-derived default** computed on the front (FNV-1a of the wallet mod 8;
  `amber` for no wallet). ⚠️ Not the `avatarColor` hash — it clustered real wallets on one theme.
- Set in **SETTINGS** (COLOR PROFILE grid of 8 + live preview; opens on the theme in effect, sends a key only
  when it changed) through the existing signed **`PATCH /players/preferences`**: body `profileTheme` — null =
  unchanged, `"default"` = clear, a key = set, anything else = the generic validation failure. A
  theme-only save is allowed (`displayName` optional when a theme is sent).
- ⚠️ **The signed message covers the theme.** A name-only save signs today's `BuildMessage` byte-for-byte; any
  save with a theme signs `BuildProfileMessage` = `"Space Stroker profile\nWallet: …\nDisplay name: …\nTheme:
  …\nNonce: …"` (empty name on a theme-only save). A name signature can never set a theme. Front twin:
  `buildProfileMessage` in `useDisplayNameSigning.ts` — keep byte-identical.
- A theme save drops that wallet's `PlayerLevel:` cache entry, and the front seeds its own tag store, so the
  player's own surfaces repaint at once; other viewers see it within ~60 s.
- **Theme = identity color only**: the profile hero / avatar / level bar / section rules / trophy frames,
  the mini card's band / avatar / level bar, the race-card avatar tile (the own card keeps its YOU ring), the
  `LV n` badge, the header profile card and menu head. Buttons, links, the wallet pill, red losses and Orbit
  violet never change.

### `/player/:wallet` page

- Route name `Player`; in `EMBED_REDIRECT_ROUTES` (an embedded load redirects to the lobby). Not an
  `isPlayerRoute()`: the page loads the owner's preferences itself.
- `isOwner` = connected wallet (`effectivePublicKey`) equals the route wallet; disconnecting turns the page
  into the visitor view live.
- Header: name (fallback short wallet), short wallet + copy + Solana explorer link, "Joined <date>", level
  + progress bar (`gambled / nextLevelAt SOL gambled`, `MAX LEVEL` at the cap; fine print *"Total gambled,
  not deposited — Orbit re-bets what it wins."*). Owner: **EDIT NAME** and **EDIT THEME** (both open
  SETTINGS). Visitor on an available profile: **DONATE SOL**.
- Record: headline **GAMBLED · WON · BETS** (GAMBLED = the level basis), then NET · WIN RATE · BIGGEST WIN ·
  ROUNDS · BEST STREAK · MOST PLAYED. NET is never the lead and never red-framed. Owner: a **Responsible
  gaming** link by the record. Copy says "gamble", never "wager".
- Trophy shelf (locked = greyed + hint; unlocked = title + date), then recent rounds. The Monkey Duel row
  thumbnail is a CSS red/blue stepped split with "VS" (the lobby card's art), not a poster.
- States: loading; failed → inline *"> profile offline — retry"* (never `/error`); unavailable → *"Profile
  unavailable"*, **except** the owner with an active exclusion, who sees *"Self-excluded until <date>"* (or
  permanent) + RG resources; no rounds → *"No rounds yet"*, all trophies locked. Reloads on
  `visibilitychange` and on a `:wallet` change; no polling.

### Player tags and the mini card

- `PlayerTag` renders every player name on: Sprint / Jackpot race cards, the in-game bet list, Duel open
  rooms and the in-match opponent (`DuelCard` / `DuelVsSplash` via `rivalWallet`), leaderboard rows and
  podium, the History YOU column and the home strip. Text = `YOU` (or the surface's YOU label), the house
  label, else display name → 4…4 wallet → `ANON`.
- **Tappable only for a real player wallet outside an iframe.** House tags (VS HOUSE `THE HOUSE`, the house
  seed bet via the bet payload's **`isHouse`**) and every embedded tag are plain text. Race lanes, the
  winners tab, "PAID TO", chat and practice stay plain (no wallet).
- ⚠️ **Self-exclusion is enforced at the card, not per bet**: no exclusion flag is broadcast on bet payloads
  (it would publish exclusion status to every viewer). An excluded player's tag opens a card that says
  "Profile unavailable".
- `LV n` badge beside the name when the wallet's level ≥ 1 — YOU tags and embedded tags included; never on a
  house tag or at level 0. Levels arrive through `usePlayerLevels` (module store, 60 s TTL, flushed once per
  ~50 ms tick in chunks of 50, one in-flight request per chunk). **A failed read just hides badges** — no
  toast, no retry loop.
- `PlayerCard` is mounted once in `App.vue`: from `lg` up a teleported popover anchored to the tag; below `lg`
  an `ArcadeModal` bottom sheet. Body: name, level + bar, short wallet + copy, joined date, rounds · win rate
  · net, the 3 most recent trophies (else "No trophies yet"), **VIEW FULL PROFILE** and **DONATE** (not on
  your own card). ⚠️ It never navigates or pauses anything on its own — the round keeps running underneath;
  it closes on route change. Profile reads are single-flighted and memoised 30 s per wallet.

### Header (as shipped, 2026-10-16)

- **The profile card replaced the ☰ glyph** (`ProfileCard.vue`): the player's avatar tile (initial) in their
  theme + `LV` badge + a chevron; **no name at any width**. Signed out: a grey guest tile. It opens the same
  menu as before.
- The menu opens with **`ProfileMenuHead`**: an identity block (avatar, name, `LV`, a 10-segment level bar,
  `x / y SOL gambled → LV n`) that links to your own profile (plain block when embedded), then a **wallet
  row** (balance, tap to copy the address) and an **Orbit row** (balance, opens Orbit; hidden when embedded
  or Orbit is off). Signed out: **Connect wallet** (`useWalletConnectRequest`). Its profile read happens
  **only while the menu is open**.
- Below it: the phone audio strip, the **4-column** grid of eight cells (Home · Practice · Stats · Fair · How
  to play · History · Responsible · Settings; History + Settings when embedded), the token row, and a
  **DISCONNECT** button for whichever wallet stack is connected.
- **One balance in the bar, at every width:** when Orbit holds money (session + balance > 0) the Orbit pill
  shows its balance and the wallet pill is hidden (`v-show`, so the desktop connect trigger stays
  registered); otherwise the wallet pill shows its balance and the Orbit pill folds to its icon square
  (`door-compact`). The other balance is in the menu head and in Orbit's surfaces (**WALLET HOLDS** card —
  see [`orbit.md`](orbit.md)).
- The wallet pill's own menu also has **My profile** on top (desktop: My profile · Copy address · Change
  wallet · Disconnect, re-implemented in the library's `dropdown-list` slot; phone session: My profile ·
  Disconnect).

### Home

- **YOUR RECORD is a compact strip** (`HomePlayerStats.vue`): own `PlayerTag`, rounds · win rate · best
  streak and **Full profile →**, themed in the player's color. Net / gambled / won / biggest win / most
  played live only on the profile. Section order and `HomeHero` are unchanged.

### DONATE SOL (front only)

- A **direct wallet-to-wallet SOL transfer the donor's wallet signs** — never the treasury, never Orbit, no
  backend record, endpoint or table; the explorer link is the only receipt. Every available player can
  receive (no opt-in); streamer wallets look ordinary and can receive too.
- Presets **0.01 / 0.05 / 0.1 / 0.5 SOL** or custom **0.001–1 SOL** (≤ 9 decimals), then a confirm step that
  says it is final and non-refundable, then SENT + explorer link. Exact amounts, never truncated.
- Refused **before anything is signed**: no wallet (→ connect, then the sheet), donating to yourself, an active
  self-exclusion of the donor, out of bounds, more than the wallet balance. A confirmation timeout is never
  reported as a failure. Success debits the header balance (`MoneyChangeReason` `'donation'`).
- **Every wallet path can donate**: an in-page wallet signs through `useConnection.sendToken`; a phone browser
  on a wallet-app deeplink session stashes `swMobileWalletPendingDonation`, hops via `signAllTransactions`, and
  `useGame.handleMobileDeeplinkReturn` resumes the donation (before the paid-bet arm) and broadcasts it. The bet
  and donation stashes are mutually exclusive. A cancel return says "Cancelled in your wallet. No SOL was sent."
- The button is a plain Solana-purple key, never the amber CTA or the player's theme. The Terms have no
  donation clause yet.

## Key code touchpoints

**Backend (`monkify-backend`)**

| File | Role |
| --- | --- |
| `SpaceStroker.Api/Controllers/PlayersController.cs` | `GET /players/profile`, `GET /players/levels` (+ OPTIONS twins) |
| `SpaceStroker.Infrastructure/Handlers/Players/GetPlayerProfile/` | Availability order, live exclusion check, cache, live name/theme stamp |
| `SpaceStroker.Infrastructure/Handlers/Players/GetPlayerLevels/` | Batched levels + themes, 50 cap, per-wallet cache |
| `SpaceStroker.Infrastructure/Services/Players/PlayerProfileService.cs` · `AchievementKeys.cs` · `GameModeClassifier.cs` | Trophies, recent rounds, mode classifier |
| `SpaceStroker.Infrastructure/Services/Players/PlayerLevelService.cs` | The grouped gambled aggregate |
| `SpaceStroker.Infrastructure/Services/Players/WalletAddress.cs` | Shared "is a 32-byte public key" check |
| `SpaceStroker.Domain/Players/PlayerLevels.cs` · `ProfileThemes.cs` | Threshold table; the 8 theme keys |
| `SpaceStroker.Infrastructure/Contracts/Players/PlayerProfileDto.cs` · `PlayerLevelsDto.cs` | Wire shapes |
| `SpaceStroker.Infrastructure/Handlers/Players/UpdatePlayerPreferences/` · `GetPlayerPreferences/` | `profileTheme`, `BuildProfileMessage`, theme-only save |
| `SpaceStroker.Domain/Games/Entities/Player.cs` · `Data/Context/SpaceStrokerDbContext.cs` · `Migrations/20261013185904_AddPlayerProfileTheme.cs` | The column |
| `SpaceStroker.Infrastructure/Contracts/Games/BetDto.cs` · `ActiveGameDto.cs` · `Consumers/BetNotification/BetNotificationEvent.cs` · `Services/Games/GameService.cs` · `Handlers/Games/GetActiveGameForParameter/` | Additive `isHouse` |
| `SpaceStroker.Infrastructure/Services/Cache/CacheHelper.cs` | `PlayerProfile:{wallet}`, `PlayerLevel:{wallet}` (60 s) |

**Frontend (`monkify-front`)** — `src/pages/PlayerProfilePage.vue`; `src/components/player/`
(`PlayerTag.vue`, `PlayerCard.vue`, `PlayerCardBody.vue`, `DonateButton.vue`, `DonateModal.vue`);
`src/components/header/ProfileCard.vue`, `ProfileMenuHead.vue`, `WalletButtons.vue`,
`PlayerCustomizationModal.vue`; `src/components/GameHeader.vue`; `src/components/home/HomePlayerStats.vue`;
`src/hooks/usePlayerProfile.ts`, `usePlayerCard.ts`, `usePlayerLevels.ts`, `useWalletConnectRequest.ts`,
`useDonate.ts`, `useDisplayNameSigning.ts`, `useMobileWallet.ts` (`signMobileDonation`); `src/constants/achievements.ts`,
`profileThemes.ts`; `src/utils/explorer.ts`, `clipboard.ts`, `verifyRound.ts`, `playerCard.ts`;
`src/routes.ts`, `src/main.ts` (`EMBED_REDIRECT_ROUTES`), `src/App.vue` (mounts `PlayerCard` + `DonateModal`).
Tag surfaces: `RaceScreen.vue`, `RaceMainTab.vue`, `GameTerminal.vue`, `DuelBoard.vue`, `DuelCard.vue`,
`DuelVsSplash.vue`, `LeaderboardRow.vue`, `LeaderboardPlinth.vue`, `GameHistoryPage.vue`.

**Tests** — integration `PlayerProfileFlowTests` (real keypairs: profile happy path vs `/players/stats`,
self-exclusion hide/return, house + malformed unavailable, levels + themes on Postgres, signed theme save).
Unit: `PlayerProfileAchievementsTests`, `PlayerProfileRecentRoundsTests`, `GetPlayerProfileHandlerTests`,
`PlayerLevelsTests`, `PlayerLevelServiceTests`, `GetPlayerLevelsHandlerTests`,
`UpdatePlayerPreferencesThemeTests`, `GameModeClassifierTests`, `WalletAddressTests`, `BetDtoIsHouseTests`,
`GameServiceBetNotificationTests`, `GetActiveGameForParameterHandlerTests`.

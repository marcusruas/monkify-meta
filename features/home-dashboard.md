# Feature — Starting page (home dashboard) + prefix-free URLs

> Status: **shipped.** Authoritative spec: `../specs/home-dashboard.md`
> (merged via `feature/home-dashboard` on 2026-08-01).
> Design project: https://claude.ai/design/p/d469c44f-87b1-43ed-ab7c-e7d0a742535a

## What it does

The app has a front door. `/` renders a landing page — hero, the real game picker, the player's own
numbers, and panelled sections that lead to every other surface — instead of redirecting into the
bare lobby. Before this, `/space-stroker/game` *was* the app: a 16-line wrapper around three cards,
with Stats, History, Provably Fair, Settings and the tutorial reachable only from the ☰ menu, which a
first-time player has no reason to open. Folded in because it rewrites the same routes: the
`/space-stroker` URL prefix is gone (the app is hosted at `app.spacestroker.com`).

## Core rules

### Routing

- **`/` is the starting page.** `/game` still renders the plain lobby unchanged — it is the URL
  `EMBEDDING.md` points integrators at and the `?type=` deep-link target.
- Paths carry **no `/space-stroker` prefix**: `/` · `/game` · `/game/play` · `/game/history` ·
  `/game/stats` · `/admin` · `/wallet-bridge` · `/background` · `/soon` · `/error`.
- A **strip-prefix shim** redirects `/space-stroker/<path>` to the stripped path, **preserving query
  and hash**. It exists for two concrete failures: a surviving old URL would render a *blank page*
  (there was no catch-all, and the load balancer's `/*` fallback serves `index.html` happily), and a
  **mobile wallet deeplink hop straddling the deploy** returns the player to the old path mid-bet.
- A **generic catch-all** sends every unmatched path to `/error`, closing that blank-page gap. The two
  catch-alls coexist by vue-router **specificity ranking**, not declaration order.
- **Embedded `/` redirects to `/game`.** There is no embedded dashboard variant — `HomePage` never
  mounts in a frame.
- The header logo/wordmark resolves to `Home`, **except when embedded**, where it stays the lobby.
- **`/soon?feature=`** is a shared placeholder for controls that are not built yet (`PRACTICE` → #129,
  `READ THE WIKI` → #130). An unknown or missing `feature` renders a safe fallback rather than an
  empty heading. It is deliberately **not** an `isPlayerRoute()` — it has no identity to render.

### The page

- **Two designed states.** **Cold** (no wallet): welcome-led, connect invitation — `GET /players/stats`
  is **not called at all**. **Connected**: the hero states what is *happening* (a live round line off
  the same snapshot the cards use), shows the player's `displayName` with the short address as
  fallback, and renders a streak **only when it is at least 1**.
- Sections below the hero are framed with the shared **`.arcade-frame`** primitive (a thin border on
  near-black, flat — the modal system's material), **not** `.arcade-panel` (whose amber bevel is the
  cabinet, not a screen). The hero is deliberately unframed. `QuickTourModal` consumes the same
  primitive instead of redefining it inline.
- The picker reuses **`GameTypes.vue` verbatim**, so the lobby and the starting page can never drift.
- ⚰️ ~~The Quick Tour **`overview@1` auto-open fires on the first `/` visit**, not the first lobby
  visit, with every existing guard intact (catalogue loaded, not embedded, no other tour open, key
  unseen, no boot-recovery/idle/relaunch overlay, terms accepted, display-name prompt closed).
  Accepted gap: a player deep-linking straight to `/game` gets no overview.~~
  **GONE — Trello #130 (2026-08-02) deleted the Quick Tour outright.** Nothing auto-opens on `/`, or
  on any route, for any player. `HomeHowItWorks` ("HOW A ROUND GOES") is now the dashboard's **only**
  onboarding, and its button reads **`How to play`**, pushing to the `HowToPlay` route — it used to
  say `Read the wiki` and push to `/soon?feature=wiki`, whose `FEATURES` entry is deleted (a
  bookmarked `?feature=wiki` degrades to the generic fallback rather than breaking). See
  `features/how-to-play.md`.
- Fair and Settings modal state is a shared **`useHeaderModals`** singleton — it used to be local to
  `GameHeader.vue`, which the dashboard cannot reach and which a second modal instance cannot fix.

### The picker card

- **Nothing on the card is ever rendered as `0`.** The status pill is absent rather than idle; the
  live chip is absent rather than zero. A picker with nothing to say says nothing.
- Reading order is the design: art → **name** → derived **tag** (how you play) → description →
  the button. Art and accent are chosen by **archetype** (`lottery → preset → race`, exhaustive),
  never by name or `gameTypeId` — operators create their own types and the id is a fresh GUID on every
  reseed.
- Accents follow the LP's mapping so the marketing page and the app agree: **race → primary,
  preset → cyan, lottery → magenta**. One accent per card.
- The tag is derived from the catalogue: `{n} CHARS · {charset}` / `PICK 1 OF {n}` / `DAILY DRAW`. An
  **anchored** lottery appends its slot — `DAILY DRAW · 22:00 UTC` — so the picker is not the one
  surface that knows the draw is daily but not when. The tag is the only field allowed to truncate.
- Two chips float over the **artwork's bottom-right**, so they cost the card no height: `TICKET`
  always, plus `PLAYERS` on a race/preset or `POT` on a lottery. Both chip values use the same neutral
  colour on every card — a price that is amber on one card and cyan on the next reads as a different
  *kind* of fact. The `PLAYERS` dot uses the semantic `--color-live` green and is lit **only** when
  the round actually has players.
- Fonts follow the repo's 2-font system: the name in **Press Start 2P**, the description in **VT323**.
- ⚠️ **`loading="lazy"` never fires inside these frames.** `.pixel-border-outer` / `-inner` clip the
  card with a `clip-path` polygon, and Chrome folds ancestor clips into the intersection check that
  drives lazy loading — every card renders an empty black frame indefinitely, *including* an image
  webpack inlined as a `data:` URI. Any future `lazy` in a clipped frame is the same bug.
- Hover fetches the GIF **once, on first hover**, swaps only after it decodes, and lifts the card
  rather than repainting its frame. A phone requests **zero** GIFs (no hover, always `light-graphics`).

### Live figures

- The live snapshot is **three `GET /games/{gameTypeId}` calls and no backend** — fired in parallel
  **once on mount** and again **when the tab regains focus** behind a 15 s guard. No interval polling.
- `/` opens **no SignalR connection and joins no hub group**: `useGameHub` keeps its
  one-connection/one-group invariant. The price is that a figure can be one focus-cycle stale, which
  is why the status pill is coarse (`FILLING`, not a per-second counter).
- Pot and player-count arithmetic is **extracted**, not re-derived (`utils/pot.ts`, `utils/players.ts`)
  — the lottery base and the nullable wallet are the two things a fresh copy gets wrong.
- Entering a game is **one handler** (`useGamePicker`): select the game, fire `game_type_selected`,
  route, `updateNewGame(true)`. Two entry points exist (a card and the hero CTA) and a player entering
  from the hero must not land in a game the app never selected.

### `GET /players/stats?wallet=` (new, public)

- **Public and wallet-keyed**, exactly like `GET /players/preferences?wallet=`. It aggregates data
  already publicly readable by wallet, over bets that are public on-chain USDC transfers.
- Returns 11 metrics — rounds played · bets placed · most-played mode · total wagered · total won ·
  net · win rate · biggest single win · biggest win multiplier · current streak · best streak — plus
  the currency descriptor (`tokenSymbol`, `tokenDecimals`) from `GeneralSettings.Operator.Token`.
- **An unknown, never-seen, or omitted wallet returns zeroed stats with 200** — never a 404, never
  anything that reveals whether that player exists (`constraints.md` §6).
- Money rules mirror `game-type-stats` / `fair-stats-menu` exactly: only `RewardForWinnersCompleted`
  rounds count; **free bets (`Amount == 0`) are excluded from money but counted in participation**;
  payouts come from `Credit` `TransactionLog` rows; amounts are token-native.
- A round counts as a **win** if **any** of that player's bets in it matched the winning choice
  (case-insensitive). Streaks order by `Game.EndDate`, ties by `CreatedDate`; `CurrentStreak` is the
  trailing win-run (0 if the newest round lost), `BestStreak` the longest anywhere.
- Cached at **`PlayerStats:{wallet}` for 30 s** — a quarter of the 120 s global/per-type TTL, because
  a player's own just-settled win going invisible is exactly the complaint `wallet-balance-refresh`
  was filed for.
- ⚠️ A **free bet may carry a wallet**: `RegisterBetHandler` only rejects `Amount == 0` when a payment
  signature is also present, and resolves a `PlayerId` from any non-empty wallet. That is the only
  reason a free bet can land in a per-wallet aggregate.

### `GET /games/history?take=` (new, public)

- The newest settled rounds **across every active type in one call**, replacing a per-type fan-out on
  the landing path. `take` is clamped server-side (default 6, max 10), which bounds cache-key growth.
- The read is **wholly public**: every `My*` column is null regardless of who asks.
- Shares `BuildRow` / `LoadBetsAsync` with the per-type history, so both surfaces apply one money rule.

### Not added

- **No entity, no migration, no Consumers change, no SignalR change.**
- **No rate-limit rule.** `RateLimitSettingsTests` pins an exact set (`*:/rpc`, `*:/solana/*`,
  `*:/solana/balance/live`); `/players/stats` and `/games/history` join `/players/preferences` as
  unlimited app-side reads behind Cloud Armor's per-IP rule.
- No live pots via SignalR, no leaderboards, no cross-player ranking, no how-it-works page (#130).

## Key code touchpoints

**monkify-backend**

- `SpaceStroker.Api/Controllers/PlayersController.cs` — `GET /players/stats`
- `SpaceStroker.Api/Controllers/GamesController.cs` — `GET /games/history`
- `SpaceStroker.Infrastructure/Handlers/Players/GetPlayerStats/` — request + handler
- `SpaceStroker.Infrastructure/Handlers/Games/GetRecentRounds/` — request + handler
- `SpaceStroker.Infrastructure/Services/Players/PlayerStatsService.cs` (+ `IPlayerStatsService`,
  `PlayerStats.cs`)
- `SpaceStroker.Infrastructure/Services/Games/GameHistoryService.cs` — `GetRecentRounds`, shared
  `BuildRow` / `LoadBetsAsync`
- `SpaceStroker.Infrastructure/Contracts/Players/PlayerStatsDto.cs`
- `SpaceStroker.Infrastructure/Services/Cache/CacheHelper.cs` — `PlayerStats:{wallet}` (30 s),
  `RecentRounds:{take}`

**monkify-front**

- `src/routes.ts` — `Home` at `/`, the prefix-free map, the strip-prefix shim, the catch-all
- `src/pages/HomePage.vue`, `src/pages/ComingSoonPage.vue`
- `src/components/home/` — `HomeHero`, `HomePlayerStats`, `HomeFloor`, `HomeHowItWorks`,
  `HomeFeatureCards`, `HomeFooter`, `HomeSection`
- `src/components/game-selection/GameTypeCard.vue` · `GameTypes.vue` — the rebuilt picker
- `src/hooks/useLiveGames.ts` · `usePlayerStats.ts` · `useGamePicker.ts` · `useHeaderModals.ts`
- `src/utils/gameArt.ts` · `pot.ts` · `players.ts`
- `src/assets/styles/arcade.css` — `.arcade-frame` / `--cyan` / `__rule`;
  `src/assets/styles/colors.css` — `--color-live`
- `src/components/GameHeader.vue` — wordmark → `Home` (lobby when embedded)
- `EMBEDDING.md` · `public/embed-example.html` — prefix-free paths (the `#launch_token` flow they
  documented no longer exists and was **not** restored)

## Tests

- Unit — `PlayerStatsServiceTests`, `GetPlayerStatsHandlerTests`, `GetRecentRoundsHandlerTests`,
  extended `GameHistoryServiceTests`.
- Integration — `PlayerStatsFlowTests`, `RecentRoundsFlowTests`.
- ⚠️ `GeneratePlayerStatsCacheSettings(wallet)` caches the **zeroed** result under the same key for
  30 s, so a pre-round read of the wallet under test pins zeros across the round.
  `InvalidateGlobalCaches()` does not clear per-wallet keys. The unknown-wallet arm is therefore its
  own test, not a pre-read.
- `monkify-front` has no test runner; verification is `yarn lint` + `yarn build` + manual QA.

## Known gaps

- **AC24 / AC38b are not verified against real bets.** The `POT NOW` / `FILLING` / `LIVE NOW` variants
  and the green `PLAYERS` dot were exercised with a temporary local stub (reverted) and by arithmetic
  that is the shipped `computePot` the terminal itself calls — but no funded round has been driven
  through this build, so the card-vs-terminal comparison is still owed.
- **AC32** (a non-USDC game must not show the USDC mark) is not exercisable — every game settles in
  USDC today. The guard is `symbol.toUpperCase() === 'USDC'` on one `v-if`.
- The embedded branch of the wordmark rule (AC42) needs a real frame and was not exercised locally.

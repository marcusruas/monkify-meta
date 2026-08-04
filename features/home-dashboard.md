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

- **Three designed states**, since Trello #138 split the connected one. **Cold** (no wallet):
  welcome-led, connect invitation — `GET /players/stats` is **not called at all**, and the cold path
  must never start fetching. **Connected** splits on whether the wallet has *ever played*; both shapes
  state what is *happening* (a live round line off the same snapshot the cards use) and show the
  player's `displayName` with the short address as fallback. See "The hero band" below.
  ⚰️ ~~renders a streak **only when it is at least 1**~~ — **the streak block is GONE (#138).**
  `HomePlayerStats` ("YOUR RECORD") shows **best** streak, so `currentStreak` no longer renders
  anywhere in the app. `GET /players/stats` still returns it.
- Sections below the hero are framed with the shared **`.arcade-frame`** primitive (a thin border on
  near-black, flat — the modal system's material), **not** `.arcade-panel` (whose amber bevel is the
  cabinet, not a screen). The hero is deliberately unframed. (This primitive was extracted so
  `QuickTourModal` could stop redefining it inline; that modal was deleted by #130, but the primitive
  is the shared material for every framed surface here.)
- **Vertical rhythm scales with the viewport** (#138): the section stack is
  `gap-6 md:gap-10 xl:gap-12 2xl:gap-16`, not a flat 24 px. With the star-strip dividers gone the gap
  is the *only* thing separating two framed panels, and the panels are capped at `max-w-[1180px]` and
  never grow — so on a wide monitor a fixed gap made everything below the hero read as one continuous
  stack. The hero's own `pb-*` is the fourth step of that scale, because it is frameless and its
  bottom padding **is** the gap to the picker.
- The picker reuses **`GameTypes.vue` verbatim**, so the lobby and the starting page can never drift.
- **`HomeFeatureCards` is one panel, not two** (#143): `PROVABLY FAIR`, **full width**, with no grid
  wrapper — one panel inside a one-column `auto-fit` grid still reads as half of something. Its twin
  `TRY IT FREE` was deleted because the trimmed hero (#138) carries a `Try it free` CTA a few hundred
  pixels above it, so the page made the same offer twice in one screen. Two consequences that must not
  be undone silently: the **`home_link_click` / `practice` event moved to `HomePage.startPractice`**
  (the hero's CTA routed without tracking, so leaving it would have killed the GA series on ship day),
  and because the fair panel is `v-if="!isEmbedded"` and is now the component's only child, an embedded
  render produces **nothing** rather than an empty full-width box.
- ⚰️ ~~The Quick Tour **`overview@1` auto-open fires on the first `/` visit**, not the first lobby
  visit, with every existing guard intact (catalogue loaded, not embedded, no other tour open, key
  unseen, no boot-recovery/idle/relaunch overlay, terms accepted, display-name prompt closed).
  Accepted gap: a player deep-linking straight to `/game` gets no overview.~~
  **GONE — Trello #130 (2026-08-02) deleted the Quick Tour outright.** Nothing auto-opens on `/`, or
  on any route, for any player. `HomeHowItWorks` — titled **`WANNA LEARN HOW TO PLAY?`** since #143,
  with no eyebrow above it (a label over a question reads as noise) — is the dashboard's **only**
  onboarding. Its three beats are named after what happens, not after the mechanic:
  **`PLACE A BET` · `EDSON STARTS KEY-STROKING` · `FIRST BET TYPED WINS`** (#143; the sub-copy under
  each is unchanged from #131). Its button reads **`Learn more`** and pushes to the `HowToPlay` route
  — it said `How to play` between #130 and #143, and `Read the wiki` before that, pushing to
  `/soon?feature=wiki`, whose `FEATURES` entry is deleted (a bookmarked `?feature=wiki` degrades to the
  generic fallback rather than breaking). See `features/how-to-play.md`.
- Fair and Settings modal state is a shared **`useHeaderModals`** singleton — it used to be local to
  `GameHeader.vue`, which the dashboard cannot reach and which a second modal instance cannot fix.

### The hero band

Trimmed by **Trello #138** (`specs/home-hero-trim.md`). It **deliberately reverses** two decisions
this page originally shipped with — the raised padding (`pt-10 pb-14 md:pt-20 md:pb-24`, decision 29)
and the large saucer that was meant to *"dominate the fold, not sit politely beside the text"*
(decisions 23/28). The reason is **reach, not taste**: `+ PICK YOUR GAME +` — the only thing on this
page a visitor can act on — sat at or below the fold at 1366×768, so the page's best pixels went to an
element nobody can click. Measured: **531 px → 249–250 px cold / 223 px connected**; the picker
heading moved from 617 px to 336 px and the first card from 672 px to 391 px, clearing even a real
maximized 1366×768 window (~640 px of viewport) with ~250 px to spare.

- **Still frameless** — a short wide band, never `.arcade-frame` / `.arcade-panel`. The page's first
  object must not be a box.
- **The art can never drive the band's height, by construction.** The art column is `shrink-0` and
  sized by **height** (`h-[110px] md:h-[140px] w-auto`) — never a width cap, never a flex basis — so
  its height is a constant (140 px) against a 151–178 px copy column. Sizing it by width is what let
  the old saucer reach 355 px, because width was a function of the viewport.
- **The art is `guide/game-modes.gif`** (45 KB, the same sprite `/how-to-play` renders for PICK A
  MODE) and is **explicitly temporary** — a real asset is [Trello #145](https://trello.com/c/6T053xnq),
  which also owns deleting the now-unimported `edson-ship.gif`/`.png` (559 KB, kept only so the swap
  back stays a two-line revert; webpack emits neither).
- **The art is not rendered below `sm` (640 px).** Beside the copy at 390 px it leaves the headline a
  ~217 px column, which breaks `FIRST WORD OUT` to three lines; stacking it instead adds back the
  height this change exists to remove.
- ⚠️ **The GIF loops on every device, `html.light-graphics` included** — no still-frame swap, no
  `usePerfMode` branch. This is a **scoped, deliberate exception** to `specs/light-graphics-mode.md`
  §7.10's ban on infinite animation, justified because `/how-to-play` already ships this exact sprite
  raw on every phone and 45 KB is an eighth of the asset that rule was written about. **It is not a
  repeal, and it does not extend to a heavier asset or a new animation.** Everything else in the band
  obeys §7.10: the `.hero-art` glow is two **flat** `drop-shadow`s, and the band contains no
  `backdrop-filter`, `blur()`, `mix-blend-mode` or infinite CSS animation.
- **Press Start 2P advances ~1 em per character**, so a 14-character headline needs ~14× the font
  size of column width. This is why the flex bases were dropped: the copy column is now "everything
  the art does not take", which makes the check one arithmetic statement instead of a per-breakpoint
  table. Re-derive it if the art is ever re-widened.
- **Below `sm` the band is centred** (`text-center sm:text-left`, both button rows
  `justify-center sm:justify-start`) — the same breakpoint where the art disappears, so below it the
  copy is the only thing in the row and full-width, and from `sm` up it is a real left column that must
  align with the picker header and cards beneath it.

#### The connected state's two shapes

Split on `isNewPlayer` — a **settled** `stats.roundsPlayed === 0`. Costs no request: `usePlayerStats`
is a module singleton `HomePlayerStats` already loads.

| | Round live on the floor | Floor quiet |
| --- | --- | --- |
| **New (0 rounds)** | `NEW HERE` · `[JOIN {NAME}] [TRY IT FREE]` | `NEW HERE` · `[TUTORIAL MODE] [HOW IT WORKS]` |
| **Returning** | `WELCOME BACK` · `[JOIN {NAME}] [GAME HISTORY]` | `WELCOME BACK` · `[PLAY {most-played}] [GAME HISTORY]` |

- **Primary is decided by the floor, secondary by the player.** An open round is the most valuable
  thing either player can do, so JOIN/WATCH wins for both. The practice screen is the newcomer's
  constant (primary when there is nothing to join, secondary when there is, so `HOW IT WORKS` only
  appears when it is not needed above); `GAME HISTORY` is the regular's constant.
- ⚠️ **One destination, two labels — and that asymmetry is deliberate.** `/game/practice` is
  **`TUTORIAL MODE`** in the newcomer's **primary** slot (quiet floor) but **`TRY IT FREE`** in the
  newcomer's **secondary** slot (a round is live), and the **cold** hero's own button keeps
  `TRY IT FREE` too. Owner's call at the `release/ui-ux-changes` ship (2026-08-04), on top of #143:
  #142 turned that screen into a guided, always-win tutorial, so the slot a newcomer lands on when
  there is nothing else to do names the **mode**, while the two fallback slots keep naming the
  **benefit**. This is the one place `HomeHero`'s own "one button, one name, everywhere" rule is
  knowingly broken — do not unify the three labels without asking.
- ⚠️ **Nothing in this band may be a scroll-to-what-you-can-already-see.** `Play again` — whose only
  job was scrolling to mode cards ~48 px below it — is deleted, and with it the hero's `play` emit,
  `HomePage`'s `scrollToModes` and its `modesSection` ref. Every path out of the band is a real
  destination.
- **An empty primary label renders no button at all** — the only honest state for a regular whose
  floor is quiet *and* whose most-played game was deactivated.
- ⚠️ **An unknown `stats` resolves to the RETURNING shape, never the new one.** `stats` is null while
  the read is in flight *and* if it fails; greeting a regular with `NEW HERE` for a frame is the worse
  lie, and the returning shape is what shipped before. Only a settled zero flips it.
- ⚠️ **`PLAY {most-played}` resolves the game against `gameTypes` (the `/games/active-types`
  catalogue), NOT against `liveByType`.** They look interchangeable and are not: `useLiveGames`
  deliberately drops any type whose live-instance call **failed** ("a live figure is an enrichment,
  never a gate"), so keying off it makes a transient backend hiccup indistinguishable from a
  deactivated game and silently eats the CTA. `/players/stats` returns a **name**, not an id, so the
  match is by name — which is also what makes a genuinely deactivated favourite disappear correctly.
- The hero emits `practice` / `history` / `how-to-play`; **`HomePage` owns all three destinations** and
  their `trackHomeLinkClick` links, as it already did for `practice`.
- The **cold** state's second CTA is labelled **`TRY IT FREE`**, not "Practice". It was named after the
  page's own section for that action, and since #143 deleted that section (below) the hero is the
  **only** place on `/` offering a free round — the label stays, because it names the benefit rather
  than the mode.

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
- Chips float over the **artwork's bottom-right**, so they cost the card no height: `TICKET` always,
  plus `POT` **on a lottery only**. Both chip values use the same neutral colour on every card — a
  price that is amber on one card and cyan on the next reads as a different *kind* of fact.
  ⚰️ ~~plus `PLAYERS` on a race/preset, whose dot uses the semantic `--color-live` green and is lit
  only when the round actually has players~~ — **the `PLAYERS` chip is GONE (Trello #143).** A player
  count was a scoreboard on a card whose job is to say what the game *is*, and on a quiet floor its
  headline was a zero. The **status pill still reports `FILLING`** off the same `live.players`, so the
  count is still fetched and still drives the one signal a player can act on — it is simply no longer
  printed. This applies to **`/` and the `/game` lobby alike**, because the picker is one shared
  component. `.live-dot` / `.live-dot--on` and `--color-live` survive in the tree **unreferenced**,
  keeping the "never light a green dot over an empty round" rule available to a future chip.
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
  Trello #143 proposed deleting this fan-out and it was **considered and rejected by the owner**: it is
  the single source of the `POT` chip, the `FILLING`/`STARTING`/`LIVE NOW` pill, the picker's
  "N rounds filling now" eyebrow **and** the hero's live line + `Join`/`Watch` CTA. Anything that
  removes it takes all five with it.
- **The catalogue is fetched once per page load** (#143). `App.vue`'s boot probe keeps its result in
  the module-level `gameTypes` ref instead of discarding it, and `GameTypes.vue` fetches only when
  that store is empty — which still happens on a cold mount from a non-player route
  (`/wallet-bridge`, `/admin`, `/error`, where the probe never runs) or after a failed probe.
  ⚠️ `mainService.getGamesTypes()` resolves to the **envelope** `DefaultResponse<ActiveGame[]>`, not
  to the array — the service's own destructure unwraps the *axios response*. Both call sites must take
  `.data`. Assigning the envelope whole puts an object in an array-typed ref, and `v-for` then renders
  one nameless `TICKET undefined` card per envelope field with the eyebrow reading `0 games`. **No
  gate in this repo catches it:** `yarn lint`, `yarn build` and `tsc --noEmit` all pass, because plain
  `tsc` does not type-check `.vue` script blocks and `vue-tsc` is not a dependency.
- **`THE FLOOR` loads when it is reached, not on mount** (#143). Its `GET /games/stats` +
  `GET /games/history?take=6` fire from a **one-shot `IntersectionObserver`** (`rootMargin: 200px`,
  disconnected *before* the load so scrolling away and back never refires); with no observer available
  the reads run immediately. `loading` therefore starts **`true`** and means *"we do not have the floor
  yet"*, covering both the deferred and the in-flight window — starting it `false` would fall through
  to the empty branch and claim **"No rounds have settled yet"** about a floor nobody has read.
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

# Feature — Mobile layout (lobby + game screen)

> Status: **shipped.** Authoritative spec: `../specs/mobile-layout.md`
> (merged via `feature/mobile-layout` on 2026-07-15). **Frontend-only** (`monkify-front`); no backend,
> DB, SignalR, or game-flow change.

## What it does

Makes SpaceStroker's two main player pages — the games lobby (`/space-stroker/game/games`) and the game
screen (`/space-stroker/game/play`) — usable on phones, in portrait and landscape, standalone **and** inside
a regulated casino's mobile `<iframe>`. The desktop-first arcade layout (a single non-wrapping flex row
of terminal + monkey + docked chat/stats cabinet with JS/`vh`-driven widths) reflows for narrow
viewports while keeping the pixel / arcade / space identity. It also adds a mobile crypto
wallet-connect path so `SolanaToken` bets can be placed from a phone.

## Core rules

- **One mobile boundary — below `lg` (1024px).** A shared `useIsMobile()` composable
  (`matchMedia('(max-width: 1023px)')`) drives the structural mobile branches. This reuses the existing
  `GameMonkey hidden lg:block` boundary and covers landscape phones (~740–932px wide), so portrait and
  landscape share **one** layout. At `≥ lg` the desktop layout is unchanged.
- **Header stays inline on phones.** Logo + mute/volume + wallet cluster (SolanaToken only) + settings
  gear + dev-admin stay on the single 64px row (smaller sizes, `flex-nowrap`); no hamburger/overflow.
- **Lobby cards condense.** The grid is already 1-column below `md`; on phones the card's long
  description clamps to one line (`max-lg:`) so the art, game name, price, lottery badge, and
  `PRESS TO START` stay the focus.
- **The game screen is a `GAME | CHAT·STATS` segmented view.** Below `lg`, `GamePage` replaces the
  desktop three-sibling row with a segmented control (`GameMobileTabs`). The two views are **absolutely
  stacked**; only the active one is visible + interactive (the hidden one is
  `invisible pointer-events-none z-0`) and the tab bar sits above both (`z-20`), so a hidden view can
  never intercept taps meant for the other view or the tabs. Both views stay mounted, so chat keeps its
  live SignalR subscription and stats keeps its lazy fetch.
- **GAME view = full-width fluid terminal + compact bet panel.** On mobile the terminal skips its
  desktop JS height-driven width and its fixed-aspect cabinet background in favour of a full-width
  metal-bezel CRT panel (`.mobile-terminal`); it still shows game/bet/frequency, `> POT:`, bets,
  countdown, the free-bet toggle, the `N AWAY!` near-miss row, and the provably-fair reveal. The three
  arcade console modules collapse into **one** compact bet panel: a choice input (device native
  keyboard — `inputmode="numeric"` for number games — or a themed preset keypad) and a single
  `PLACE BET` action (the lever). **There is no amount field — the ticket price is fixed at
  `requiredAmount`.**
- **CHAT·STATS view = full-width chat + stats.** The desktop absolute cabinet (dock rail / docked /
  maximized) is not used on mobile; `GameSidePanel` renders the chat and stats bodies full-width with
  simple `CHAT | STATS` sub-tabs. An **unread dot** shows on the CHAT·STATS tab when a chat message
  arrives while the player is on GAME (`useChatUnread` — `GameChatPanel` bumps it when it isn't visible;
  it clears when the segment is opened).
- **Monkey hidden on phones** (already `hidden lg:block`) — the terminal carries all game feedback.
- **Customization modal is mobile-friendly** — it scrolls (`max-h-[90dvh] overflow-y-auto`) and the
  provably-fair client-seed row wraps on narrow widths. The standalone `public/verify.html` was already
  responsive (fluid `max-width`), so it needed no change. The header has **no** provably-fair "shield" —
  the third header icon is the dev-admin button.
- **Mobile crypto wallet-connect.** Mobile browsers have no injected wallet extension, so for a
  `SolanaToken` **paid** bet with no connected/injected wallet the bet panel shows an **"OPEN IN WALLET
  APP"** button that deep-links the current page into the wallet app's in-app browser (Phantom /
  Solflare universal links; `utils/walletDeepLink.ts`), where the provider injects and the existing
  `useConnection.sendToken` flow works unchanged. Free (amount 0) SolanaToken and all Standard games
  need no wallet. The bet POST is unchanged, so there is **no backend work**. (Android-native Mobile
  Wallet Adapter was considered and **deferred** — the in-app-browser deep link covers iOS + Android.)
- **Embedded on mobile.** The layout is width-driven, so it renders the same inside a mobile iframe; the
  existing embedded 401 → vague "relaunching" path is unchanged (never `/error`, never contract status).

## Key code touchpoints

- `monkify-front/src/hooks/useIsMobile.ts` — **new.** `matchMedia('(max-width: 1023px)')` singleton `isMobile`.
- `monkify-front/src/hooks/useChatUnread.ts` — **new.** Shared `chatUnread` + `bumpChatUnread()` / `clearChatUnread()`.
- `monkify-front/src/components/game/GameMobileTabs.vue` — **new.** `GAME | CHAT·STATS` segmented control + unread dot.
- `monkify-front/src/utils/walletDeepLink.ts` — **new.** `isMobileBrowser()` + Phantom/Solflare in-app-browse deep links.
- `monkify-front/src/pages/GamePage.vue` — mobile branch: segmented tabs + absolutely-stacked GAME/CHAT·STATS views.
- `monkify-front/src/components/game/GameTerminal.vue` — `mobile` prop; guards the JS width; full-width `.mobile-terminal` CRT panel.
- `monkify-front/src/components/game/GameConsole.vue` — `mobile` prop; compact bet panel (native keyboard / preset keypad, PLACE BET, no amount field); "OPEN IN WALLET APP" affordance (`canDeepLinkWallet`).
- `monkify-front/src/components/game/GameSidePanel.vue` — `mobile`/`visible` props; full-width chat/stats segment (desktop cabinet unchanged).
- `monkify-front/src/components/game/GameChatPanel.vue` — `visible` prop; bumps `useChatUnread` when hidden.
- `monkify-front/src/components/GameHeader.vue` — inline single-row header at phone sizes.
- `monkify-front/src/components/game-selection/GameTypeCard.vue` — description clamps to one line `< lg`.
- `monkify-front/src/components/header/PlayerCustomizationModal.vue` — scrolls (`max-h-[90dvh]`) + seed row wraps on narrow widths.

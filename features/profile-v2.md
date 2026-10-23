# Feature — Profile V2 (Edson profile icons + notifications feed)

> Status: **shipped.** Authoritative spec: [`../specs/profile-v2.md`](../specs/profile-v2.md)
> (merged via `feature/profile-v2` on 2026-10-23, Trello [#202](https://trello.com/c/FWV5xDzH)).
> Level **`Feature`**. Builds on Profile V1 ([`player-profile.md`](player-profile.md)) and the chat revamp
> ([`chat.md`](chat.md)).

## What it does

Every player has a **profile icon**: one of 21 pixel-art Edson portraits (10 free, 10 each unlocked by one
Profile V1 trophy, **Golden** for all 10 trophies) plus 3 **staff icons** gated by config wallet lists. The
icon replaces V1's coloured initial tile everywhere a player is drawn, still sitting on a tile in the
player's colour profile. The owner changes it from their own `/player/:wallet` hero through a signed save.
The chat's 🔔 tab became one **NOTIFICATIONS** feed (donations, trophy unlocked, refund issued, jackpot
won) with a live push, a toast and an unread count, so a player learns the moment they unlock something.

## Core rules

### Icon catalog (`PlayerIcons`, keys = wire ids; names live on the front only)

| Key | Name | Unlock |
| --- | --- | --- |
| `edson` | Edson | free — the **default** (`AvatarId == null` means Edson) |
| `suit` · `blue-hoodie` · `brasil` · `stoned` · `blue-jersey` · `broke` · `pepe` · `copium` · `noir` | Suit · Blue Hoodie · Brazil · Weed · Blue Jersey · Fries in the Bag · Pepe · Copium · Noir | free |
| `racer` | Racer | trophy `firstSprintWin` |
| `boombox` | Boombox | trophy `firstHyperRollWin` |
| `sol` | Solana | trophy `hyperHit` |
| `spartan` | Spartan | trophy `firstDuelWin` |
| `ghoul` | Ghoul | trophy `rivalSlayer` |
| `pimp` | Pimp | trophy `jackpotWinner` |
| `gibby` | Gibby | trophy `crowdBeater` |
| `cold` | Subzero | trophy `winStreak3` |
| `old-master` | Old Master | trophy `winStreak5` |
| `greek` | Greek | trophy `explorer` |
| `golden` | Golden | all 10 trophies |
| `elder` · `phantom` · `beanie` | Elder · Phantom · Beanie | staff list `GeneralSettings:PlayerIcons:<Elder\|Phantom\|Beanie>:Wallets` |

- Keys are exact and case-sensitive. A unit test pins that every trophy rule names a real
  `AchievementKeys.Ordered` key and each trophy maps to exactly one icon. **No level-gated icons** (volume
  rewards are an anti-goal).
- **Unlocks are derived on read** from the trophy derivation (`PlayerIconRules.Unlocked`: free + earned
  trophy icons + Golden + allowed staff icons) — there is no inventory table.
- **Staff lists** are independent of `Admins` / `Moderators`, cleaned like the chat staff lists (trim, drop
  blanks, ordinal distinct) and **read live** on every call; committed **empty** (real wallets go in the
  deploy env, e.g. `GeneralSettings__PlayerIcons__Elder__Wallets__0`). Staff may also pick any player icon
  they unlocked; nothing is auto-assigned.

### Saving an icon — signed `PATCH /players/preferences`

- Body gains `avatarId` (`[MaxLength(20)]`): `null` = unchanged, `"default"` = clear (stored as `null`),
  a key = set. "At least one of name / theme / icon" must be present.
- **Message selection** (byte-identical with the front's `useDisplayNameSigning.ts`):
  `avatarId` present → `BuildProfileIconMessage` = `Space Stroker profile` / `Wallet:` / `Display name:` /
  `Theme:` / `Icon:` / `Nonce:` lines (empty string for an absent name or theme; the icon is the raw body
  value, `"default"` included); else a theme → `BuildProfileMessage` (unchanged); else `BuildMessage`
  (unchanged). **Name-only and theme-only saves sign exactly what they signed before** — a name or theme
  signature can never set an icon.
- Validation: an unknown key is refused **before** the signature step; the **unlock check runs after the
  signature is valid and the nonce consumed**, so an unsigned caller cannot probe which icons a wallet has.
  A first save (no `Player` row) has no trophies → free icons (and allowed staff icons) only.
- ⚠️ **Every refusal (unknown, locked, staff not allowed) is the same generic validation failure** as any
  other refused preferences save (`constraints.md` §6), and **nothing is written** — a name/theme in the
  same request is not saved either (one signed request, all or nothing). The front says only *"That icon
  could not be saved."*
- A save with an icon or theme drops `PlayerLevel:{wallet}`; a save with an icon also drops
  `PlayerProfile:{wallet}`. The response (`PlayerPreferencesDto`) and `GET /players/preferences` carry
  `avatarId`.

### Reads

- **`GET /players/levels`** gains `avatars: { wallet: key }` — only wallets whose stored pick is still
  effective; absent = Edson. The per-wallet cache `PlayerLevel:{wallet}` now holds `{ Level, Theme, AvatarId }`
  (the **stored** value) and `PlayerIconRules.Effective` runs **after** the cache, so a wallet removed from a
  staff list loses its staff icon at once. Same 50 cap, 60 s TTL, never an error.
- **`GET /players/profile`** adds `avatarId` (stamped live, never cached) and `unlockedAvatars` (catalog
  order, staff part live). An available wallet with no `Player` row gets the free keys + any allowed staff
  keys. ⚠️ The **unavailable** body (malformed / house / self-excluded) is still byte-identical for everyone:
  it lists the **free keys only, never staff**, so it leaks no staff membership.
- **A lost trophy icon is cleared, not hidden** (`PlayerAvatarGuard`): when the equipped icon is a trophy or
  Golden icon whose trophy is no longer unlocked, `AvatarId` is set to `null` and the `PlayerLevel:` +
  `PlayerProfile:` cache entries are dropped. It runs on every profile read (from the achievements in hand,
  so a loss may take up to the 60 s profile TTL to show there) and after every successful bet refund (at
  once). The write re-checks the value it read, so a pick the player just made is never wiped. The player is
  not notified and re-picks if the trophy comes back. Free icons never lapse; staff icons are checked live.
- A self-excluded wallet's icon still shows on the batched levels read (like levels and themes, accepted).

### Where the icon shows (`PlayerAvatar`)

- One shared component draws the player picture: the Edson (`avatarNow(wallet)`, unknown key → Edson) on a
  tile in the player's colour profile (`themeNow`), 128 px source above 48 px, else the 48 px one, rendered
  pixelated. A failed levels read just shows Edson (no toast).
- Surfaces: header profile card (signed out = grey guest tile, still Edson), profile menu head, profile hero
  (owner: a button, aria *"Change icon"*, that opens the picker; visitor / embedded: plain picture), mini
  card, Sprint/Jackpot race cards, **open duel room cards** (34 px tile spanning the card's three lines),
  global chat (20 px before the name; the `LV` square stays), DM rows (32 px) and thread head (24 px),
  leaderboard rows + podium (via `PlayerTag`'s `avatar` prop, 24 px), the donate sheet and the SETTINGS
  colour-profile preview (shows the equipped icon on the theme being previewed).
- **House tags/cards never get an icon** (`house` → the plain initial tile: VS HOUSE, the house seed bet).
  Anonymous free-bet race cards show Edson on the card's own colour. `PlayerTag` shows no icon when embedded.

### Icon picker

- `PlayerIconPicker` is mounted **once in `App.vue`** (`usePlayerIconPicker`), so a trophy toast or feed row
  opens it over any page without navigating mid-round; it only ever opens for the connected wallet.
- `ArcadeModal` titled `PROFILE ICON`: a grid (4 columns on a phone, 6 on desktop) of the 21 player icons in
  catalog order, then the staff icons present in `unlockedAvatars`. Locked = greyed + *"Unlock: <trophy
  title>"*; Golden shows *"n/10 trophies"*.
- **NEW chip** is client-side: `localStorage` `space-stroker.icons-seen.{wallet}`; the first open seeds it
  with the free icons (never NEW); each open writes the current unlocked set. Storage failure → no NEW tags.
- SAVE sends `"default"` for Edson only when the stored value is not already null. Success → toast
  *"Icon saved"*, the owner's surfaces repaint at once (`rememberAvatar`); other viewers within ~60 s.

### Notifications feed (the 🔔 tab)

- New table `Notifications` (`Wallet`, `Type`, `DedupKey`, `Payload` camelCase JSON, `ReadAt`), **unique
  `(Wallet, Type, DedupKey)`**: a retried settlement / refund / donation report inserts nothing and pushes
  nothing (`NotificationService.CreateAsync` returns null on the duplicate). Notifications are **never
  purged**. No backfill — nothing that happened before the deploy is notified.
- Each new row is pushed live as `NotificationReceived` (`NotificationDto { id, type, createdAt, unread,
  payload }`) to the recipient's DM group `dm:{wallet}`, from either host (Redis backplane). A push failure
  is logged, never thrown; the row is the truth.

| Type | Written by | DedupKey | Payload | Copy (row + toast) |
| --- | --- | --- | --- | --- |
| `donation` | `ReportDonationHandler`, after the `Donation` row; **not pushed** when the recipient blocked the donor (row still stored) | transfer signature | `{ donationId, signature, donorWallet, donorDisplayName, amount }` | `<donor tag> donated <amount> SOL` + THANK (→ DM) |
| `trophyUnlocked` | `RoundSettledConsumer` | trophy key | `{ trophy, icon, golden }` | `TROPHY: <title> · new Edson unlocked: <name>` (+ ` · GOLDEN unlocked`); tap → picker |
| `refundIssued` | `RefundInvalidBetConsumer`, after a successful bet refund, only `EntryKind == Paid` with `Amount > 0` | bet id | `{ betId, gameId, amount, destination: "wallet" \| "orbit" }` (amount = what this refund actually returned) | `Refund: <amount> SOL sent to your wallet` / `… returned to your Orbit balance` |
| `jackpotWon` | `RoundSettledConsumer`, rounds `GameModeClassifier` puts in `jackpot` (holder draw or paid lottery) | game id | `{ gameId, amount, holder }` | `SPACE JACKPOT! You won <amount> SOL` |

- **Settlement hook:** `RewardGameConsumer` publishes `RoundSettledEvent(GameId)` **last**, only after the
  round reached `RewardForWinnersCompleted` (both the no-winners and the winners exit; never on
  `ErrorWhenProcessingRewards`). A publish failure is logged, never thrown — payouts are already written.
  `RoundSettledConsumer` runs on **its own receive endpoint** in Consumers, so it never queues with payouts.
- **Trophy rule:** for each distinct player with a counting bet (not streamer, not
  `NeedsRefunding`/`Refunded`/`Voided`, not a house wallet), `ComputeAchievementUnlocksAsync` returns
  `(Key, At, GameId)` per unlocked trophy; a trophy is notified **only when its unlock round is the settling
  round**. That is what makes "no flood on deploy" and "retry adds nothing" hold. Unlock round: first-win /
  `hyperHit` / `rivalSlayer` / `crowdBeater` → the earliest qualifying round; `winStreak3/5` → the round that
  completes the streak; `explorer` → the round of the latest first-play. `golden: true` rides on the last
  fresh trophy (in `AchievementKeys` order) of the run that completes all 10. The profile cache is dropped
  before the push, so tapping the toast shows the new icon.
- Short-cuts: `TrophiesComplete:{wallet}` (1 h) skips players who already hold all 10; a **loss gate**
  skips the derivation when every counting bet of the player in this round lost and they already have
  another completed round of this game type. "Lost" is the derivation's rule: choice ≠ winning choice, or on a
  Word Lines (`LINES`) round, credited at most the stake (every LINES choice is `spin`). An active streamer wallet unlocks
  nothing → never notified. One bad player is logged and skipped (tracker cleared), never re-thrown.
- **Jackpot rule:** one `jackpotWon` per **wallet** (not per bet): amount = Σ `Credit` rows across its
  `Rewarded` bets, `holder` = any of them is a holder ticket. Streamer wins, zero-credit wins and house
  wallets get none.
- **No** payout, level-up, per-round or icon-lost notifications. `RefundWithoutBet` (deposit with no bet
  row), free, holder and streamer refunds create none.
- **Routes** (`ChatController`, header `X-Chat-Session`, 401 without a session):
  `GET /chat/notifications?before=<iso>` → `{ items, hasMore, unreadCount }`, newest first, 30 per page,
  unread counted over all of the wallet's rows; `POST /chat/notifications/read` → 204, stamps `ReadAt` on
  every unread notification **and** every unread `Donation` of the wallet (one read state), pushing
  `NotificationsRead {}` to `dm:{wallet}` once if anything changed. `GET /chat/donations` +
  `POST /chat/donations/read` and `DonationReceived` stay (TOP DONORS data, old clients) — but the **unread
  count and the toast come only from notifications**.

### Client

- Drawer tabs **GLOBAL | DMs | 🔔** — the bell (aria *Notifications*) opens `NotificationsView`: the same
  CONNECT / SIGN IN TO CHAT states as DMs, header `NOTIFICATIONS · private`, segment **ALL | TOP DONORS**
  (TOP DONORS = #205's donor ranking from `useDonations`), rows newest first with an unread dot, relative
  time and LOAD MORE. Opening the tab (with the page visible) marks everything read. Amounts are exact
  (up to 9 decimals, `utils/notificationCopy.ts`, shared by row and toast).
- Chat button badge = unread DMs + `unreadCount` (99+); its click opens DMs first, else notifications.
  The old tab name `'donations'` is accepted as an alias (stored mobile hops, old callers).
- `NotificationToast` (in `App.vue`, hidden when embedded) shows each arrival for 6 s under the header;
  tapping a trophy opens the picker, anything else opens the drawer on notifications; a donation keeps THANK.
  A `trophyUnlocked` arrival also forgets the own profile cache so the picker is fresh.
- `useNotifications` reloads on chat sign-in / wallet change and on hub rejoin, de-dupes pushes by id, and
  marks read instead of bumping the count while the tab is actually rendered and visible.

## Key code touchpoints

**monkify-backend**
- Domain: `SpaceStroker.Domain/Players/PlayerIcons.cs`; `Games/Entities/Player.cs` (`AvatarId`);
  `Chat/Entities/Notification.cs`, `Chat/NotificationTypes.cs`; `Configs/Entities/GeneralSettings.cs`
  (`PlayerIconSettings`, `StaffIconList`); `SpaceStroker.Common/spacestrokersettings.json` (empty lists).
- EF: `Data/Context/SpaceStrokerDbContext.cs` (`AvatarId` max 20, `Notifications` mapping, two indexes);
  migration `Migrations/20261023005356_ProfileV2AvatarsAndNotifications.cs` (additive, no backfill).
- Services: `Services/Players/PlayerIconRules.cs`, `PlayerAvatarGuard.cs`, `PlayerProfileService.cs`
  (`ComputeAchievementUnlocksAsync`, `AchievementUnlock.cs`); `Services/Notifications/NotificationService.cs`;
  `Services/Chat/ChatEvents.cs` (`NotificationReceived`, `NotificationsRead`); `Services/Cache/CacheHelper.cs`
  (`TrophiesComplete:{wallet}`); registered in `Infrastructure/DependencyInjection.cs`.
- Handlers: `Handlers/Players/UpdatePlayerPreferences/` (`BuildProfileIconMessage`, unlock check),
  `GetPlayerLevels/`, `GetPlayerProfile/`, `GetPlayerPreferences/`; `Handlers/Chat/GetNotifications/`,
  `MarkNotificationsRead/`, `ReportDonation/`. Contracts `Contracts/Chat/NotificationDto.cs`
  (+ `NotificationsPageDto`), `Contracts/Players/PlayerLevelsDto.cs`, `PlayerProfileDto.cs`.
- Consumers: `Consumers/RoundSettled/{RoundSettledEvent,RoundSettledConsumer}.cs` (registered in
  `SpaceStroker.Consumers/Program.cs`), `Consumers/RewardGame/RewardGameConsumer.cs` (publish),
  `Consumers/RefundInvalidBet/RefundInvalidBetConsumer.cs` (`AfterRefund`).
- API: `SpaceStroker.Api/Controllers/ChatController.cs` (two routes + OPTIONS).
- Tests: integration `ProfileV2FlowTests` (free icon saves + shows on profile/levels, locked refused;
  staff icon only for the listed wallet; first Sprint win → `trophyUnlocked` on the feed and the hub, then
  `racer` saves). Unit: `PlayerIconsTests`, `PlayerIconRulesTests`, `PlayerAvatarGuardTests`,
  `UpdatePlayerPreferencesIconTests`, `RoundSettledConsumerTests`, `NotificationServiceTests`,
  `NotificationsHandlerTests`, `PlayerIconSettingsBindingTests`, plus additions to `RewardGameConsumerTests`,
  `RefundInvalidBetConsumerTests`, `DonationHandlerTests`, `GetPlayerLevelsHandlerTests`,
  `GetPlayerProfileHandlerTests`, `PlayerProfileAchievementsTests`, `PlayerProfileRecentRoundsTests`.

**monkify-front**
- `src/assets/player-icons/<key>.png` + `<key>-48.png` (24 icons, nearest-neighbour 128 / 48 px);
  `src/constants/playerIcons.ts` (`PLAYER_ICONS`, `iconFor`, names).
- `src/components/player/PlayerAvatar.vue`, `PlayerIconPicker.vue`, `PlayerTag.vue` (`avatar` /
  `avatarSize`); `src/hooks/usePlayerIconPicker.ts`, `usePlayerLevels.ts` (`avatarNow`, `rememberAvatar`),
  `usePlayerPreferences.ts`, `useDisplayNameSigning.ts` (`buildProfileIconMessage`).
- Surfaces: `header/ProfileCard.vue`, `header/ProfileMenuHead.vue`, `header/PlayerCustomizationModal.vue`,
  `pages/PlayerProfilePage.vue`, `player/PlayerCardBody.vue`, `player/DonateModal.vue`,
  `game/RaceMainTab.vue`, `game/DuelBoard.vue`, `chat/GlobalChatView.vue`, `chat/DirectMessagesView.vue`,
  `leaderboard/LeaderboardRow.vue`, `leaderboard/LeaderboardPlinth.vue`.
- Feed: `src/hooks/useNotifications.ts`, `useDonations.ts` (TOP DONORS only), `useChatDrawer.ts`,
  `useGameHub.ts`, `useDirectMessages.ts`, `useMobileWallet.ts` (tab alias);
  `src/components/chat/NotificationsView.vue`, `NotificationToast.vue`, `ChatDrawer.vue`, `ChatButton.vue`;
  `src/utils/notificationCopy.ts`; `src/services/chatService.ts`; `src/types/chat.ts`, `src/types/index.ts`;
  `src/App.vue` (mounts the picker + toast).

## Known gaps (as shipped)

- The practice race (`PracticeTerminal.vue`) still draws bots and GUEST with the V1 initial tile (the spec
  listed it as a surface).

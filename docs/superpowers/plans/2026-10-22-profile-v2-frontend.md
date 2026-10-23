# Profile V2 — Frontend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show each player's Edson icon everywhere a player is drawn, let the owner pick it (signed save), and turn the chat 🔔 donations tab into a NOTIFICATIONS feed with a generic toast.

**Architecture:** A shared `PlayerAvatar` reads the icon from the batched `usePlayerLevels` store (new `avatars` map) and paints it on the existing theme tile. A singleton `usePlayerIconPicker` + modal mounted in `App.vue` handles selection and the signed `PATCH /players/preferences`. A singleton `useNotifications` (modelled on `useDonations`) owns feed, unread count and toast, fed by the SignalR `NotificationReceived`/`NotificationsRead` events on the DM group.

**Tech Stack:** Vue 3 + TS (Composition API, no store), Vue CLI/webpack, Tailwind 3, `@microsoft/signalr`, axios. Yarn.

**Spec:** `monkify-meta/specs/profile-v2.md` §7 (worktree `.worktrees/profile-v2/monkify-meta`). Backend contract: `2026-10-22-profile-v2-backend.md` (Tasks 4–7 Interfaces).

**Worktree:** `C:\Users\kurok\Desktop\monkify\.worktrees\profile-v2\monkify-front` (branch `feature/profile-v2`). Before starting: copy hub `yarn.lock` and `.env.local` into the worktree, `yarn install --frozen-lockfile`.

## Global Constraints

- No test runner in this repo: each task is verified by `yarn lint` (0 errors) and `npx vue-tsc --noEmit` if available, else `yarn build`; UI tasks additionally by browser QA on the local stack.
- No new `package.json` dependency. Image downscale script lives in the scratchpad; commit only the PNG outputs.
- Signing twin must be byte-identical to the backend: `['Space Stroker profile', 'Wallet: …', 'Display name: …', 'Theme: …', 'Icon: …', 'Nonce: …'].join('\n')`. Name-only and theme-only messages unchanged.
- Refused save copy: *"That icon could not be saved."* (vague). Success toast: `Icon saved`.
- Copy: "gamble", never "wager". House tags never get an icon. Embedded (`?embed=1`) tags stay plain text.
- `image-rendering: pixelated` on every Edson `<img>`; `draggable="false"`.
- Do **not** commit during execution — leave changes in the worktree for the user's review.

## Review Focus

1. **Levels read fails / backend old** → every surface still renders Edson on the theme tile, no toast, no console error (`avatarNow` defaults to `'edson'`; unknown key → Edson) — Task 2.
2. **Picker opened while signed out / for another wallet** → no-op (`open()` guards on connected wallet) — Task 5.
3. **Same notification delivered twice (reconnect replay + load)** → one row, unread counted once (de-dupe by id) — Task 6.
4. **Old resume state with tab `'donations'`** (mobile wallet return, stored drawer state) → opens NOTIFICATIONS, no blank drawer — Task 7.
5. **`localStorage` throws (private mode)** → picker works, just no NEW chips — Task 5.

---

### Task 1: Assets + catalog

**Files:**
- Create: `src/assets/player-icons/<key>.png` (128 px) and `<key>-48.png` (48 px) for the 24 keys of spec §5.1, from `C:\Users\kurok\Downloads\Edson Profiles-20261022T210709Z-1-001\Edson Profiles\` (mapping file → key in §5.1; `2.Edson.png` = `suit`, `15.CopeEdson.png` = `copium`, `15.GoldEdson.png` = `golden`, `Admin1/2/3` = `elder/phantom/beanie`).
- Create: `src/constants/playerIcons.ts`

**Interfaces:**
- Produces: `type PlayerIconUnlock = { kind: 'free' } | { kind: 'trophy'; trophy: string } | { kind: 'all' } | { kind: 'staff' }`; `interface PlayerIconDef { key: string; name: string; unlock: PlayerIconUnlock; src: string; src48: string }`; `PLAYER_ICONS: PlayerIconDef[]` (§5.1 order); `PLAYER_ICON_KEYS_FREE: string[]`; `iconFor(key?: string | null): PlayerIconDef` (unknown/null → Edson); `DEFAULT_ICON = 'edson'`.

- [ ] **Step 1:** Write a scratchpad Node script (no deps: use PowerShell `System.Drawing` with `InterpolationMode.NearestNeighbor` + `PixelOffsetMode.Half` if Node has no image lib) to emit both sizes; verify one output visually (Read the PNG).
- [ ] **Step 2:** Write `playerIcons.ts` with `require`/`import` of each PNG (match how other `src/assets` images are imported in the repo).
- [ ] **Step 3:** `yarn lint` — 0 errors.

### Task 2: Types, levels store, preferences, signing

**Files:**
- Modify: `src/types/index.ts` (`PlayerLevels.avatars?`, `PlayerProfile.avatarId?`, `unlockedAvatars?`, `PlayerPreferencesBody.avatarId?`, `PlayerPreferences.avatarId?`), `src/hooks/usePlayerLevels.ts`, `src/hooks/usePlayerPreferences.ts`, `src/hooks/useDisplayNameSigning.ts`, `src/services/mainService.ts` (only if the body type isn't passed through)

**Interfaces:**
- Produces (`usePlayerLevels`): `Known.avatar: string | null`; `avatarOf(wallet): string | null`; `avatarNow(wallet?: string | null): string` (→ `'edson'` fallback); `rememberAvatar(wallet: string, key: string | null): void`.
- Produces (`usePlayerPreferences`): `avatarId: Ref<string | null>`, `setAvatarId(key: string | null)`.
- Produces (`useDisplayNameSigning`): `buildProfileIconMessage(wallet, displayName, theme, icon, nonce): string`; `savePreferences(wallet, displayName: string | null, theme?: string | null, avatarId?: string | null)` — `avatarId` present → icon message with `''` for absent name/theme, body includes `avatarId`.

- [ ] **Step 1:** Implement. **Step 2:** `yarn lint` + type-check — 0 errors. **Step 3:** confirm by reading both builders that `buildProfileIconMessage('W','','','spartan','N') === 'Space Stroker profile\nWallet: W\nDisplay name: \nTheme: \nIcon: spartan\nNonce: N'` (same literal the backend test pins).

### Task 3: `PlayerAvatar` + `PlayerTag` slot

**Files:**
- Create: `src/components/player/PlayerAvatar.vue`
- Modify: `src/components/player/PlayerTag.vue` (`avatar?: boolean` default false, `avatarSize?: number`; render `PlayerAvatar` in the reserved "V2: profile icon slot" for non-house tags)

**Interfaces:**
- Produces: `PlayerAvatar` props `wallet?: string | null`, `size: number`, `house?: boolean`, `fallbackColor?: string`, `avatarId?: string | null`, `theme?: ProfileTheme`, `initial?: string` (house tile letter). Tile colour = `themeNow(wallet)` (existing helper; find it) → `fallbackColor` → grey. `size > 48` → 128 px source.

- [ ] **Step 1:** Implement, matching the existing initial-tile CSS (radius, border, shadow) from `ProfileCard.vue`/`PlayerCardBody.vue` so sizes don't shift. **Step 2:** lint/type-check.

### Task 4: Surfaces

**Files (spec §7.4 table):** `ProfileCard.vue`, `ProfileMenuHead.vue`, `PlayerProfilePage.vue` (hero: owner → `<button aria-label="Change icon">` calling `usePlayerIconPicker().open()`; visitor → plain), `PlayerCardBody.vue`, `RaceMainTab.vue` + `RaceScreen.vue`, `PracticeTerminal.vue`, `GlobalChatView.vue` (20 px before name), `DirectMessagesView.vue` (rows + 24 px thread head), `LeaderboardRow.vue`, `LeaderboardPlinth.vue` (via `PlayerTag avatar`), `DonateModal.vue`, `PlayerCustomizationModal.vue`.

**Interfaces:** Consumes `PlayerAvatar` (Task 3), `usePlayerIconPicker().open` (Task 5 — create the hook stub first if doing Task 4 before Task 5).

Rules: house (`isHouse`) → initial tile unchanged; no wallet (anon free bet, bots, GUEST) → Edson on the existing colour. Keep every existing size/layout.

- [ ] **Step 1:** Replace each hand-built tile. **Step 2:** lint/type-check. **Step 3:** browser QA at 360 px and desktop: each surface shows Edson, no layout shift vs `main` screenshots.

### Task 5: Icon picker

**Files:**
- Create: `src/hooks/usePlayerIconPicker.ts`, `src/components/player/PlayerIconPicker.vue`
- Modify: `src/App.vue` (mount once)

**Interfaces:**
- Produces: `usePlayerIconPicker(): { isOpen: Ref<boolean>; open(): void; close(): void }` (module singleton; `open` no-op without connected wallet).
- Consumes: `usePlayerProfile` / `forgetPlayerProfile` (existing), `savePreferences` + `rememberAvatar` + `setAvatarId` (Task 2), `achievementDef(trophy).title` (`constants/achievements.ts`), `ArcadeModal` (`size="standard"`, `variant="grey"`).

Behaviour: spec §7.5 + decision 22 (`localStorage` key `space-stroker.icons-seen.{wallet}`; first open seeds with free keys; opening writes current unlocked set; all storage access in try/catch). Golden hint `"<n>/10 trophies"` from the profile's achievements. Edson when stored is already null = unchanged (SAVE disabled); Edson when stored non-null → sends `"default"`.

- [ ] **Step 1:** Implement. **Step 2:** lint/type-check. **Step 3:** browser QA: own profile → open, locked greyed with hint, Golden `0/10`, pick Suit → sign → header/menu/hero repaint at once; visitor profile icon not clickable.

### Task 6: Notifications data layer

**Files:**
- Create: `src/hooks/useNotifications.ts`
- Modify: `src/types/chat.ts` (`NotificationDto`, `NotificationsPage`, payload types per spec §6.6), `src/services/chatService.ts` (`getNotifications(before?: string)`, `markNotificationsRead()` — same session header as `getDonations`), `src/hooks/useGameHub.ts` (`CHAT_EVENTS.notification = 'NotificationReceived'`, `notificationsRead = 'NotificationsRead'`; `DirectMessageHandlers.onNotification` / `onNotificationsRead`; bind in `bindDmHandlers`), `src/hooks/useDirectMessages.ts` (`setNotificationListener`), `src/hooks/useDonations.ts` (drop unread count + toast ownership; keep RECENT/TOP data)

**Interfaces:**
- Produces: `useNotifications(): { items, hasMore, unreadCount, toast, load(), loadMore(), markRead(), dismissToast() }`, `NOTIFICATION_TOAST_MS = 6000`. Behaviour per spec §7.7 bullet 1 (reload on auth change + `onChatRejoined`; de-dupe by id; visible tab → mark read instead of bump; `trophyUnlocked` → `forgetPlayerProfile(own)`).

- [ ] **Step 1:** Implement; grep every consumer of the removed `useDonations` unread/toast members and repoint them. **Step 2:** lint/type-check — 0 errors.

### Task 7: Notifications UI

**Files:**
- Create: `src/components/chat/NotificationsView.vue`, `src/components/chat/NotificationToast.vue`
- Delete: `src/components/chat/DonationsView.vue`, `src/components/chat/DonationToast.vue` (move TOP DONORS section markup into `NotificationsView`)
- Modify: `ChatDrawer.vue`, `ChatButton.vue`, `hooks/useChatDrawer.ts` (`'donations'` → `'notifications'`, accept `'donations'` alias in `open()`), `hooks/useMobileWallet.ts` (resume alias), `App.vue` (swap toast)

Rows/toasts copy per spec §7.7 table exactly; trophy row/toast tap → `usePlayerIconPicker().open()`; others → drawer on `notifications`; donation keeps THANK → DM. Badge = unread DMs + `unreadCount` (99+).

- [ ] **Step 1:** Implement. **Step 2:** `yarn lint` + `yarn build` — green. **Step 3:** browser QA on the local stack with a real settled Sprint win (memory: local-dev-free-bet-testing): toast "TROPHY: First Sprint win · new Edson unlocked: Racer", tap → picker with Racer NEW; badge counts it; opening 🔔 clears it; TOP DONORS still renders.

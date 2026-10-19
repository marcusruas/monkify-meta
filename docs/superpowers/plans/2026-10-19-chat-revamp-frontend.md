# Chat revamp — Frontend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One global chat on every page (header button → drawer / mobile sheet, GLOBAL | DMs), wallet-signed chat session, DMs with unread + block, reply-to, online count, admin moderation — in `monkify-front`.

**Architecture:** Chat state moves out of `useGame` into module-singleton composables (`useChatSession`, `useGlobalChat`, `useDirectMessages`, `useChatDrawer`). `useGameHub` keeps the one-connection-per-tab model and gains global/DM channel methods with rebind on reconnect **and** on a rebuilt connection. `GameHeader` (mounted app-wide) starts global chat; a teleported `ChatDrawer` is mounted once in `App.vue`.

**Tech Stack:** Vue 3 `<script setup>` + TypeScript, `@microsoft/signalr`, axios services, solana-wallets-vue, Tailwind + `arcade.css`.

**Authoritative design:** `monkify-meta/specs/chat-revamp.md` §6.3–6.4 (wire contract), §6.11–6.12 (routes), §7 (frontend). Read it first.

## Global Constraints

- Worktree: `E:\Monkify\.worktrees\chat-revamp\monkify-front` on `feature/chat-revamp`. Never touch the hub.
- **Wire contract (must match backend exactly):**
  - Hub methods: `JoinGlobalChat()`, `LeaveGlobalChat()`, `AuthenticateChat(token): boolean`, `SendGlobalMessage(text, replyToId|null)`, `SendDirectMessage(recipientWallet, text, replyToId|null): { status: 'sent'|'unavailable'|'dropped', message: DirectMessage|null }`.
  - Events (payloads are **objects**, camelCase — no `JSON.parse`): `ChatMessage` (ChatMessage), `ChatMessageDeleted` `{ id }`, `ChatOnlineCount` `{ count }`, `DirectMessage` (DirectMessage), `DirectMessagesRead` `{ counterpartWallet, lastReadAt }`.
  - Types: `ChatReply { id; displayName; snippet: string|null; deleted: boolean }`, `ChatMessage { id; displayName; text; sentAt; wallet: string|null; replyTo: ChatReply|null }`, `DirectMessage { id; senderWallet; recipientWallet; text; sentAt; replyTo: ChatReply|null }`, `Conversation { counterpartWallet; counterpartDisplayName; lastMessage: DirectMessage; unreadCount; blocked }`, `ConversationsResponse { conversations; totalUnread }`, `ThreadResponse { messages; hasMore; counterpartDisplayName; blocked; available }`, `ChatSession { sessionToken; expiresAt }`.
  - REST (responses wrapped in the existing `DefaultResponse<T>` `.content`, like other `mainAPI` calls): `GET /chat`, `POST /chat/session {wallet,nonce,signature}`, `GET /chat/dms`, `GET /chat/dms/{wallet}?before=`, `POST /chat/dms/{wallet}/read`, `POST|DELETE /chat/blocks/{wallet}` (header `X-Chat-Session`); admin `DELETE /admin/chat/messages/{id}`, `GET|POST /admin/chat/mutes` (`{wallet, mutedUntil}`), `DELETE /admin/chat/mutes/{wallet}`.
  - Session message (byte-identical): `['Space Stroker chat session', \`Wallet: ${wallet}\`, \`Nonce: ${nonce}\`].join('\n')`; nonce purpose `'chat-session'`.
- Copy: "This player is unavailable." · "Staff will never DM you asking for funds, seed phrases or keys." · "Direct messages are unavailable while self-excluded." · "No messages yet. Tap a player's name to start one." · "CONNECT WALLET TO CHAT" · "message deleted" · "GLOBAL CHAT" · "N online".
- Limits: 200 chars, 1 msg/s client guard, reply snippet shown as given by the server.
- Hidden when `useEmbed().isEmbedded` or `route.name === 'Practice'`; practice files must never import chat composables.
- Drawer must `<Teleport to="#app">` (header `backdrop-filter` traps fixed children); z-index `z-modal`.
- Checks: `yarn lint` and `yarn build` must pass. Working tree is CRLF; eslint `endOfLine: 'auto'`.
- Commit after each task with `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`.

---

### Task 1: Types + services
- Create `src/types/chat.ts` (types above); move `ChatMessage` out of `src/types/index.ts` (re-export if many imports).
- Create `src/services/chatService.ts`: `CHAT_SESSION_HEADER = 'X-Chat-Session'`, `getGlobalChat()` (single-flighted via `mainService`'s `singleFlight` — export it if needed), `createChatSession(wallet, nonce, signature)`, `getConversations(token)`, `getThread(token, wallet, before?)`, `markRead(token, wallet)`, `block(token, wallet)`, `unblock(token, wallet)`. Use `mainAPI` (export it if not exported).
- `mainService.getWalletNonce` purpose union += `'chat-session'`; delete `getGameChat`.
- `adminService`: `deleteChatMessage(id)`, `getChatMutes()`, `muteChatWallet(wallet, mutedUntil)`, `unmuteChatWallet(wallet)`.
- Verify: `yarn lint`. Commit.

### Task 2: Hub channel methods (`src/hooks/useGameHub.ts`)
- Remove `attachToChat`, `detachFromChat`, `onChatRejoined`, `currentChatTypeId`, `boundChatEvent`, `chatRejoinedHandler`.
- Add: `joinGlobalChat(handlers: { onMessage, onDeleted, onOnline })`, `leaveGlobalChat()`, `authenticateChat(token, handlers: { onDirectMessage, onRead }): Promise<boolean>`, `clearChatAuth()`, `onChatRejoined(cb)`, `invokeWithResult<T>(method, ...args): Promise<T|null>`.
- State: `globalChatJoined`, `chatHandlers`, `chatToken`, `dmHandlers`. Bind handlers with `live.on(...)` once per connection instance (track the instance they were bound to).
- `onreconnected`: after Orbit rejoin, re-invoke `JoinGlobalChat` if joined and `AuthenticateChat(chatToken)` if set, then `chatRejoined?.()`.
- **Rebuilt connection:** in `ensureConnected`, when a new `HubConnection` replaces the old one, rebind chat/DM handlers and re-invoke the joins (blocker B4 from exploration).
- `stopForIdle` clears join flags but keeps handlers/token so the drawer can rejoin via `joinGlobalChat`/`authenticateChat`.
- Verify lint. Commit.

### Task 3: `useChatSession` + mobile hop
- `src/hooks/useChatSession.ts`: storage key `space-stroker.chat-session.${wallet}`; `readStored`, `store`, `forgetChatSession`, `ensureChatSession(wallet, { allowHop })` mirroring `useOrbit.ensureSession` / `signOrbitMessage` (desktop `useWallet().signMessage`, deeplink → `signMobileChat` + throw `handled`+`hopped` error), `getStoredChatToken(wallet)`.
- `useMobileWallet.ts` + `types/mobileWallet.ts`: `MobilePendingChat { walletApp, wallet, nonce, createdAt }`, `signMobileChat`, `getPendingChat`, clear in `disconnectMobileWallet` (copy the Orbit trio).
- `useGame.handleMobileDeeplinkReturn`: new arm before the bet arm → read signature, `createChatSession`, store, then `useChatDrawer().open()` and `useDirectMessages().authenticate()`.
- Commit.

### Task 4: `useGlobalChat` + `useChatDrawer`; strip chat from `useGame`
- `src/hooks/useChatDrawer.ts`: `isOpen`, `tab`, `open(tab?)`, `close()`.
- `src/hooks/useGlobalChat.ts` (module singleton): `messages`, `onlineCount`, `lastLiveMessage`, `replyingTo`, `started`; `start()` (idempotent: `hub.joinGlobalChat` then `getGlobalChat()` merge by id, sort by `Date.parse(sentAt)`; `hub.onChatRejoined` → refetch), `send(text)` (validate 200 / 1 s; wallet via `useMobileWallet().effectivePublicKey`; no wallet → false; `ensureChatSession(wallet,{allowHop:true})` → `hub.authenticateChat` if not yet → `hub.invoke('SendGlobalMessage', text, replyingTo?.id ?? null)`; on success clear reply), `setReply(msg)`, `clearReply()`, deleted handler (remove + patch replies `deleted=true, snippet=null`), `canSend`, `validationMessage`, `maxLength`, `ownWallet`.
- Remove from `useGame.ts`: `chatMessages`, `lastLiveChatMessage`, `mergeChatMessages`, `loadChatHistory`, `onChatMessage`, `hub.onChatRejoined` registration, the chat join in `initalizeWebsocketConnections`, `sendChatMessage`, and their exports. Delete `useGameChat.ts`; move `shortenId` to `utils` if used elsewhere.
- `GamePage.vue` / `useChatUnread`: dot driven by `useGlobalChat().lastLiveMessage` (live, not mine, panel hidden).
- Commit.

### Task 5: `useDirectMessages`
- Module singleton: `conversations`, `totalUnread`, `thread` (`wallet`, `messages`, `hasMore`, `available`, `blocked`, `counterpartDisplayName`, `loading`), `authenticated`.
- `authenticate({ allowHop })`: wallet → token (`allowHop:false` = stored only) → `hub.authenticateChat(token, { onDirectMessage, onRead })` → `loadConversations()`; on 401 anywhere → `forgetChatSession` + `authenticated=false`.
- `openThread(wallet)`, `loadOlder()`, `send(text)` (→ `invokeWithResult('SendDirectMessage', …)`; `unavailable` → toast "This player is unavailable." + `thread.available=false`; `sent` → append if not present), `block()`, `unblock()`, `closeThread()`, `requestMessage({ wallet, name })` (mirror `useDonate.requestDonate`: no wallet → `afterConnect` + `requestConnect`; then `useChatDrawer().open('dms')` + `openThread`).
- Live: `DirectMessage` for the open thread → append + `markRead`; else bump that conversation (or reload list) + `totalUnread`. `DirectMessagesRead` → zero that conversation, recompute total.
- Wallet change → reset state + re-authenticate silently.
- Commit.

### Task 6: Components
- `constants/icons.ts` + asset: `chat` pixel icon (follow how existing icons are defined).
- `components/chat/ChatComposer.vue` (input, SEND, reply bar with ✕, disabled/placeholder props, maxLength).
- `components/chat/GlobalChatView.vue` (extract list + composer from `GameChatPanel.vue`, keep `LV` square/theme/profile link via `usePlayerLevels`; title "GLOBAL CHAT" + "{n} online"; reply action per message; quote line `↪ name: snippet` or "message deleted").
- `components/chat/DirectMessagesView.vue` (list ↔ thread, notice, LOAD OLDER, BLOCK/UNBLOCK, connect CTA, self-excluded copy via `useSelfExclusion().selfExcluded`, composer disabled when `available === false`).
- `components/chat/ChatDrawer.vue` (Teleport, desktop right panel ~400px / mobile full-screen via `useIsMobile`, tabs GLOBAL | DMs with DMs count, `useOverlayStack` + `useFocusTrap` + `useBodyScrollLock`, Esc / overlay close, opening calls `useGlobalChat().start()` and `useDirectMessages().authenticate({allowHop:false})`).
- `components/chat/ChatButton.vue` (icon + `totalUnread` badge, `99+` cap, `useChatDrawer().open()`).
- `GameChatPanel.vue` → wrapper rendering `GlobalChatView` (props passthrough).
- `GameHeader.vue`: add `ChatButton` before the menu root; app-wide start next to `useOrbitNotifications().start()` guarded by `!isEmbedded && route.name !== 'Practice'` (watch route), plus silent `useDirectMessages().authenticate({allowHop:false})` on wallet connect.
- `App.vue`: mount `<chat-drawer />` beside `<player-card />` with the same guards.
- `PlayerProfilePage.vue` (~L344) + `PlayerCardBody.vue` (~L197): MESSAGE button (same visibility rule as DONATE, not own wallet) → `requestMessage`.
- Commit.

### Task 7: Admin moderation UI
- `components/admin/ChatModerationManager.vue`: last 100 global messages (`getGlobalChat`) with DELETE (confirm) → `deleteChatMessage` → remove locally; mute form (wallet + preset 1h/24h/7d/30d → `mutedUntil` ISO) → `muteChatWallet`; active mutes list with UNMUTE. Toasts via `apiErrorMessage`.
- `GameTypeManager.vue`: add `'chat'` section to the `Section` type + nav, render the manager.
- Commit.

### Task 8: Verify
- `yarn lint` and `yarn build` green; grep `src` for `getGameChat|attachToChat|sendChatMessage|useGameChat|JoinChat|SendPlayerChatMessage` → none.
- Commit any fixes.

## Self-review

Spec §7.1→T3–T5, §7.2→T2, §7.3/7.4→T6, admin→T7, §7.5→T1/T4. Wire contract lives in Global Constraints so tasks share names.

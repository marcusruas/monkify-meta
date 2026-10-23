# Feature — Chat (global room, DMs, notifications)

> Status: **shipped.** Authoritative spec: [`../specs/chat-revamp.md`](../specs/chat-revamp.md)
> (merged via `feature/chat-revamp` on 2026-10-22, Trello [#205](https://trello.com/c/il70nAG8)).
> Supersedes [`game-chat.md`](game-chat.md) (one room per game type) and the client-trusted identity of
> [`../specs/game-chat.md`](../specs/game-chat.md). The daily purge, the Redis-list history shape and the
> 200-char limit carry over.
> **Profile V2** (#202, 2026-10-23, [`profile-v2.md`](profile-v2.md)) turned the donations bell into the
> **NOTIFICATIONS** feed (donations, trophies, refunds, jackpot wins) and put each player's Edson icon in the
> global chat and DMs.

## What it does

One app-wide chat room reachable from **every page**, plus private **direct messages** between players
and a 🔔 **notifications** feed. On desktop the chat is a docked 340px column beside the page (it pushes the page,
it never covers it) that can collapse to a 48px rail; on mobile it is a full-screen sheet. Tabs:
**GLOBAL | DMs | 🔔 (notifications)**. Anyone can read the global room; **sending anything** needs a
**7-day chat session** created by one wallet signature, so the **server** — not the browser — decides who
the sender is.

## Core rules

**Identity — the chat session**
- `GET /players/nonce?purpose=chat-session` → sign `"Space Stroker chat session\nWallet: {wallet}\nNonce: {nonce}"`
  (byte-identical in `ChatMessages.BuildSessionMessage` and the front's `buildChatSessionMessage`) →
  `POST /chat/session` → `{ sessionToken, expiresAt }` (TTL = `Orbit.SessionTtlDays`, 7). One signature,
  asked only on the player's first send (or MESSAGE press); later sends need none until expiry.
- Minting **creates the `Player` row** if missing. House wallets and invalid wallets are refused. Every
  failure (bad signature, replayed or unknown nonce, house, invalid) is the one vague
  `ChatSessionInvalid` — *"Your chat sign-in could not be completed."* (400). Self-exclusion does **not**
  block minting.
- The token reuses the session HMAC key but its payload is prefixed `chat:` — a chat token never validates
  as an Orbit/referral token (`ValidateAndGetWallet` rejects any wallet part containing `:`) and vice versa.
  No new secret.
- Sender **wallet and display name are resolved server-side** (`Player.DisplayName`, else
  `first4…last4`). Level square and color theme stay client-resolved (`/players/levels`) from that verified
  wallet. HTTP routes read the token from **`X-Chat-Session`**; a missing/invalid one → 401 with the same
  vague body.

**Global room**
- SignalR group `chat:global` on `ActiveGamesHub`. `JoinGlobalChat()` is anonymous (and registers presence);
  `AuthenticateChat(token)` → `bool`, joins the caller's DM group `dm:{wallet}` (name derived server-side) and
  is re-called after every reconnect; `SendGlobalMessage(text, replyToId)`.
- Each message shows the sender's Edson icon (20 px `PlayerAvatar`) before the name; the `LV` square stays.
- Events (camelCase objects): `ChatMessage` (`{ id, displayName, text, sentAt, wallet, replyTo }`),
  `ChatMessageDeleted` `{ id }`, `ChatOnlineCount` `{ count }`.
- Send order: text 1–200 chars → active mute → rate limit → reply lookup (not found ⇒ sent **without** a
  reply, never refused) → broadcast → append to Redis history → publish `ChatMessagePersistedEvent`.
  **Every refusal drops silently** (no session, expired token, bad text, muted, rate-limited).
- `GET /chat` (anonymous) → the newest ≤ 100 within `Chat.RetentionDays` (committed **2**), oldest first,
  from Redis list `ChatHistory:global` (RPUSH + LTRIM + EXPIRE), DB fallback (`GameChatMessage` rows with
  `GameTypeId == null`) + refill. The room started clean at ship; old per-type rows/keys age out.
- **Reply:** `replyTo { id, displayName, snippet ≤ 80 (… counts), deleted }`; a deleted parent reads
  "message deleted".
- **Online count:** approximate. Redis sorted set `ChatOnline` (member = connection id, score = last seen);
  `ChatPresenceBroadcaster` (API host only) re-scores its local ids, drops entries older than 3 × 30s and
  broadcasts the count every `Chat.OnlineBroadcastSeconds` (30). A joiner gets the count immediately.
- Persisted asynchronously by `ChatMessagePersistedConsumer` (idempotent per id; skips ids tombstoned by an
  admin delete via `ChatDeleted:{id}`).

**Direct messages**
- Any signed-in player can DM any wallet that has a `Player` row, except **self** and **house wallets**.
  `SendDirectMessage(recipient, text, replyToId)` → `{ status: "sent" | "unavailable" | "dropped", message }`.
- `unavailable` — recipient invalid / self / house / no `Player` row / **self-excluded** / **has blocked the
  sender**, or the **sender is self-excluded**. The client shows only *"This player is unavailable."* — a
  block is indistinguishable from an exclusion. Nothing is stored or delivered.
- `dropped` — no session, bad text, muted, rate-limited; the client keeps the text in the input.
- A DM is **persisted before delivery** (`DirectMessages`), then pushed as `DirectMessage` to both
  `dm:{recipient}` and `dm:{sender}` (sender's other tabs). Replies must point to a DM in the same
  conversation, else the DM is sent without one.
- Routes (`X-Chat-Session`): `GET /chat/dms` (conversations within `DirectMessageRetentionDays`, newest
  first, ≤ 50, unread counts + `totalUnread`); `GET /chat/dms/{wallet}?before=` (50 per page, oldest first,
  `hasMore`, `blocked`, `available` — the composer is disabled when false); `POST /chat/dms/{wallet}/read`
  (moves `LastReadAt` forward only, pushes `DirectMessagesRead` to the owner's group); `POST|DELETE
  /chat/blocks/{wallet}` (idempotent; blocks DMs only, history kept).
- Unread = messages **from** the counterpart newer than `DirectMessageReadState.LastReadAt`. Header badge =
  total unread DMs + unread **notifications** (`99+` cap); a click opens DMs first when they have unread,
  else the 🔔 tab. Conversation rows (32 px) and the thread head (24 px) show the counterpart's Edson icon.
- A self-excluded player keeps global chat and can read old threads, but cannot send or receive DMs
  (owner-only copy: *"Direct messages are unavailable while self-excluded."*).
- Every thread shows the fixed notice *"Staff will never DM you asking for funds, seed phrases or keys."*
- Admins cannot read or delete DMs.

**Donations (feed into the 🔔 NOTIFICATIONS tab)**
- After a DONATE transfer confirms, the donor's client calls `POST /chat/donations` (anonymous — the chain is
  the proof). The API verifies it on chain as native SOL donor → recipient (`ValidateIncomingTransfer`),
  minimum **0.001 SOL**, and records it once per signature (`Donations`, unique `Signature`); any refusal is
  the vague `DonationNotVerified`. The backend never moves this money.
- Live `DonationReceived` to the recipient's DM group (not sent when the recipient blocked the donor — the
  row is still recorded). `GET /chat/donations` (session: RECENT + TOP DONORS, totals) and
  `POST /chat/donations/read` (pushes `DonationsRead`) stay for TOP DONORS and old clients. Donations are
  **not** purged.
- Since Profile V2 each recorded donation also writes a `donation` **notification** (same block rule for the
  push). The 🔔 tab is the general feed — `donation`, `trophyUnlocked`, `refundIssued`, `jackpotWon` — with
  segments **ALL | TOP DONORS**; the unread count and the 6 s toast come **only** from notifications
  (`NotificationReceived`), and `POST /chat/notifications/read` also stamps the wallet's donations read.
  Full rules: [`profile-v2.md`](profile-v2.md).

**Abuse and moderation**
- Per-wallet server limit **1 msg/s and 20/min** (global + DM share the budget), fixed windows
  (`ChatRate:s:{wallet}:{second}`, `ChatRate:m:{wallet}:{minute}`) — a sender straddling a minute boundary
  can get up to ~40 through in 60 s. A Redis error **allows** (fail-open). The client keeps its own 1/s guard.
- Admin (`X-Admin-Token`, `AdminPage` → Chat section): `DELETE /admin/chat/messages/{id}` (removed live, from
  Redis history, DB row deleted, replies patched `deleted`, tombstone so a late consumer skips it; unknown id
  → 404); `GET|POST /admin/chat/mutes`, `DELETE /admin/chat/mutes/{wallet}` (mute until a time; blocks global
  + DM sends silently — the muted player gets no feedback).
- **Staff badges:** `GeneralSettings.Admins` / `Moderators` (public wallet lists, config only) →
  `GET /chat/staff` → **ADM** (yellow) / **MOD** (red) beside names in chat, DMs and the profile hero.
  Display only — no powers.

**Retention** — daily `game-chat-purge` (`ChatMessagePurgeJob`): global rows older than `RetentionDays` (2),
DMs and read states older than `DirectMessageRetentionDays` (30), expired mutes.

**Client**
- Started app-wide from `GameHeader` (not embedded, not on `/game/practice` — there is no chat there at all).
  A wallet with a stored chat session authenticates silently on load (no prompt) so the badge is right.
- The game screen has **no chat of its own** (`GameSidePanel`/`GameChatPanel` removed); the mobile strip is
  a GAME label.
- Header order: sound → **chat** → Orbit → wallet → profile card.
- **Desktop dock state is remembered** in `localStorage` `space-stroker.chat-dock` (`open` | `collapsed` |
  `closed`), first visit open. Mobile always starts closed and never writes it; narrowing below `lg` closes
  without saving, widening back restores the preference.
- Reconnect (or a rebuilt hub connection) re-joins the global room and the DM group and refetches history,
  conversations and unread. A 401 forgets the stored session (`space-stroker.chat-session.{wallet}`).
- Phone wallets on the deeplink protocol sign the chat session through a wallet-app hop
  (`MobilePendingChat`), resumed in `useGame.handleMobileDeeplinkReturn` before the bet arm.
- MESSAGE buttons on the profile hero and the player card open the DM thread (connect first if needed).

## Key code touchpoints

**monkify-backend**
- `SpaceStroker.Infrastructure/Hubs/ActiveGamesHub.cs` — `GlobalChatGroup`, `DirectMessageGroupFor`,
  `JoinGlobalChat`, `LeaveGlobalChat`, `AuthenticateChat`, `SendGlobalMessage`, `SendDirectMessage`.
- `SpaceStroker.Infrastructure/Services/Chat/` — `ChatService` (send logic), `ChatEvents`, `ChatRooms`,
  `ChatNames`, `ChatMessages`, `ChatSendStatus`, history stores (`Redis`/`Memory`ChatHistoryStore),
  rate limiters, presence + `ChatPresenceBroadcaster`.
- `SpaceStroker.Infrastructure/Services/Wallets/WalletSessionService.cs` — `MintChat`, `ValidateChatAndGetWallet`.
- `SpaceStroker.Infrastructure/Handlers/Chat/*` (session, global history, staff, conversations, thread,
  read, blocks, donations, notifications) and `Handlers/Admin/{DeleteChatMessage,GetChatMutes,SetChatMute,RemoveChatMute}`.
- `SpaceStroker.Api/Controllers/ChatController.cs`, `Api/Chat/ChatSessionAccessor.cs`, `AdminController.cs`.
- `SpaceStroker.Domain/Chat/` — `ChatReply`, entities `DirectMessage`, `DirectMessageReadState`, `ChatBlock`,
  `ChatMute`, `Donation`; `Games/Entities/GameChatMessage.cs` (nullable `GameTypeId`/`GameId`, reply
  columns). Migrations `ChatRevamp`, `AddDonations`. Settings `ChatSettings` (`RetentionDays`,
  `DirectMessageRetentionDays`, `MessagesPerSecond`, `MessagesPerMinute`, `OnlineBroadcastSeconds`).
- Consumer `Consumers/ChatMessagePersisted/*`; job `Jobs/ChatMessagePurgeJob.cs`.
- Tests: `UnitTests/Chat/*`, `UnitTests/Handlers/{CreateChatSession,GetGlobalChat,GetConversations,GetThread,MarkThreadRead,GetChatStaff,Donation}HandlerTests.cs`,
  `Admin/ChatModerationHandlerTests.cs`, `Hubs/ActiveGamesHubTests.cs`, `Wallets/WalletSessionServiceTests.cs`;
  integration `ChatFlowTests.cs` (happy path, block, moderation, token separation) and `DonationFlowTests.cs`.

**monkify-front**
- Hooks: `useChatSession.ts`, `useGlobalChat.ts`, `useDirectMessages.ts`, `useDonations.ts` (TOP DONORS),
  `useNotifications.ts` (feed, unread, toast),
  `useChatDrawer.ts` (dock state + persistence), `useStaffRoles.ts`, chat parts of `useGameHub.ts`
  (`joinGlobalChat`, `authenticateChat`, rebind on a rebuilt connection), deeplink arm in `useMobileWallet.ts`.
- Components: `components/chat/{ChatButton,ChatDrawer,GlobalChatView,ChatComposer,DirectMessagesView,NotificationsView,NotificationToast,StaffBadge}.vue`
  (drawer mounted once in `App.vue`), `components/admin/ChatModerationManager.vue`, MESSAGE buttons in
  `pages/PlayerProfilePage.vue` and `components/player/PlayerCardBody.vue`, header slot in `GameHeader.vue`.
- `services/chatService.ts` (`X-Chat-Session`), `services/adminService.ts` (moderation), `types/chat.ts`.

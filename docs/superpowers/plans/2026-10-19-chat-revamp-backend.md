# Chat revamp — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace per-game-type chat with one global room, add wallet-signed chat sessions, DMs, reply-to, presence, a per-wallet rate limit and admin moderation in `monkify-backend`.

**Architecture:** The hub (`ActiveGamesHub`) stays thin and delegates to a scoped `IChatService`; Redis-backed singletons (history store, rate limiter, presence) each have a memory twin registered in the existing Redis-vs-memory branch. Chat tokens reuse `WalletSessionService` with a `chat:`-prefixed payload. DMs are written synchronously by the API; global messages stay async-persisted by the existing consumer.

**Tech Stack:** .NET 8, ASP.NET Core SignalR (Redis backplane), EF Core (Npgsql, NoTracking default), MediatR `BaseRequestHandler`, MassTransit Postgres transport, StackExchange.Redis, xUnit + Shouldly + Moq, Testcontainers.

**Authoritative design:** `monkify-meta/specs/chat-revamp.md` (§4–§6, §11). Read it first; this plan sequences it.

## Global Constraints

- Worktree: `E:\Monkify\.worktrees\chat-revamp\monkify-backend` on `feature/chat-revamp`. Never touch the hub `E:\Monkify\monkify-backend`.
- EF mapping inline in `SpaceStrokerDbContext.OnModelCreating`; context is **NoTracking** by default — use `AsTracking()`, `Add`, `ExecuteUpdateAsync`/`ExecuteDeleteAsync` for writes.
- Migration via `dotnet ef migrations add ChatRevamp --project SpaceStroker.Infrastructure --startup-project SpaceStroker.Consumers` from `src/`; never hand-edit the snapshot.
- No new secret; chat tokens use the existing `Orbit.SessionSecret` + `Orbit.SessionTtlDays`.
- Vague copy: `ErrorMessages.ChatSessionInvalid` = "Your chat sign-in could not be completed."; DM refusal status `unavailable` (front renders "This player is unavailable.").
- Limits: text ≤ 200 chars; reply snippet ≤ 80 (incl. `…`); history 100 within `Chat.RetentionDays` (committed 2); DMs 30 days; 1 msg/s + 20/min per wallet; online broadcast every 30 s; thread page 50; conversations max 50.
- Group names: `chat:global`, `dm:{wallet}`. Event names: `ChatMessage`, `ChatMessageDeleted`, `ChatOnlineCount`, `DirectMessage`, `DirectMessagesRead`.
- Unit tests: `scripts\run_tests.bat` (never full `dotnet test`). Integration: `scripts\run_integration_tests.bat` (Docker).
- Files are CRLF in the working tree; do not bulk `sed -i` (flips line endings repo-wide).
- Commit after each task with `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`.

---

### Task 1: Chat tokens separate from session tokens

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Services/Wallets/IWalletSessionService.cs`, `WalletSessionService.cs`
- Test: existing wallet-session test file (find with `grep -rl "WalletSessionService" src/SpaceStroker.Tests`) — extend

**Interfaces — Produces:** `string MintChat(string wallet)`, `string? ValidateChatAndGetWallet(string? token)`.

- [ ] Write failing tests: (a) `MintChat(w)` → `ValidateChatAndGetWallet` returns `w`; (b) `MintChat(w)` → `ValidateAndGetWallet` returns **null**; (c) `Mint(w)` → `ValidateChatAndGetWallet` returns null; (d) tampered / expired chat token → null; (e) existing `Mint`/`ValidateAndGetWallet` round-trip unchanged.
- [ ] Run → fail (methods missing).
- [ ] Implement: extract the current parse body into `private string? TryReadWalletPart(string? token)` (returns the text before the last `|` after signature + expiry checks). `ValidateAndGetWallet` = part, null if it contains `':'`. `ValidateChatAndGetWallet` = part must start with `"chat:"`, strip, then `WalletAddress.IsValid` else null. `MintChat` = same as `Mint` with payload `$"chat:{wallet}|{exp}"`. Add both to the interface (fix any test fakes implementing it).
- [ ] Run unit tests → pass. Commit `Chat tokens: chat:-prefixed payload that never validates as a session token`.

### Task 2: Data model + migration + settings

**Files:**
- Modify: `src/SpaceStroker.Domain/Games/Entities/GameChatMessage.cs`, `src/SpaceStroker.Domain/Configs/Entities/ChatSettings.cs`, `GameSettings.cs` (remove `GameChatEndpoint`), `src/SpaceStroker.Common/spacestrokersettings.json`, `src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs`
- Create: `src/SpaceStroker.Domain/Chat/ChatReply.cs`, `src/SpaceStroker.Domain/Chat/Entities/{DirectMessage,DirectMessageReadState,ChatBlock,ChatMute}.cs`, migration `*_ChatRevamp.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Chat/ChatReplyTests.cs`

**Produces:** entities exactly as spec §4.1–4.2; `ChatReply(Guid Id, string DisplayName, string Snippet)` with `static string SnippetOf(string text)`; `ChatSettings.{DirectMessageRetentionDays=30, MessagesPerSecond=1, MessagesPerMinute=20, OnlineBroadcastSeconds=30}`; DbSets `DirectMessages`, `DirectMessageReadStates`, `ChatBlocks`, `ChatMutes`.

- [ ] Failing tests for `SnippetOf`: ≤80 unchanged (trimmed); 81+ → 79 chars + `…` (length 80).
- [ ] Implement entities, mapping (§4.3), settings + json (`"Chat": { "RetentionDays": 2, "DirectMessageRetentionDays": 30, "MessagesPerSecond": 1, "MessagesPerMinute": 20, "OnlineBroadcastSeconds": 30 }`), remove `GameChatEndpoint` from class + json. `GameChatMessage.GameTypeId`/`GameId` → `Guid?`; fix compile errors only where they block the build (the hub/consumer get rewritten in later tasks — stub minimal compile fixes there if needed).
- [ ] `dotnet ef migrations add ChatRevamp ...`; inspect: only AlterColumn-nullable, AddColumn×4, CreateTable×4 + indexes.
- [ ] Build + unit tests green (existing chat tests may need the nullable change). Commit.

### Task 3: History store re-keyed by room + RemoveAsync; DTOs

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Services/Chat/{IChatHistoryStore,RedisChatHistoryStore,MemoryChatHistoryStore,ChatHistory}.cs`, `Services/Cache/CacheHelper.cs`
- Create: `src/SpaceStroker.Infrastructure/Contracts/Chat/{ChatMessageDto,ChatReplyDto,DirectMessageDto,DirectMessageSendResult,ChatSessionDto,ConversationDto,ConversationsDto,ThreadDto}.cs`, `Services/Chat/{ChatEvents,ChatRooms,ChatNames,ChatSendStatus,ChatMessages}.cs`
- Delete: `Contracts/Games/GameChatMessageDto.cs` (update usages)
- Test: `UnitTests/Chat/MemoryChatHistoryStoreTests.cs` (update + add)

**Produces:**
```csharp
public record ChatReplyDto(Guid Id, string DisplayName, string? Snippet, bool Deleted);
public record ChatMessageDto(Guid Id, string DisplayName, string Text, DateTime SentAt, string? Wallet, ChatReplyDto? ReplyTo);
public record DirectMessageDto(Guid Id, string SenderWallet, string RecipientWallet, string Text, DateTime SentAt, ChatReplyDto? ReplyTo);
public record DirectMessageSendResult(string Status, DirectMessageDto? Message);
public record ChatSessionDto(string SessionToken, DateTime ExpiresAt);
public record ConversationDto(string CounterpartWallet, string CounterpartDisplayName, DirectMessageDto LastMessage, int UnreadCount, bool Blocked);
public record ConversationsDto(IReadOnlyList<ConversationDto> Conversations, int TotalUnread);
public record ThreadDto(IReadOnlyList<DirectMessageDto> Messages, bool HasMore, string CounterpartDisplayName, bool Blocked, bool Available);
public static class ChatSendStatus { public const string Sent="sent", Unavailable="unavailable", Dropped="dropped"; }
public static class ChatRooms { public const string Global = "global"; }
public static class ChatNames { public static string Short(string wallet) /* first4 + "…" + last4; ≤8 chars → as is */; public static string Display(string? displayName, string wallet); }
public static class ChatMessages { public static string BuildSessionMessage(string wallet, string nonce) => string.Join("\n", "Space Stroker chat session", $"Wallet: {wallet}", $"Nonce: {nonce}"); }
// IChatHistoryStore: AppendAsync(string room, ChatMessageDto), ReadAsync(string room) -> IReadOnlyList<ChatMessageDto>?, RefillAsync(string room, IReadOnlyList<ChatMessageDto>), RemoveAsync(string room, Guid id)
// CacheHelper.GenerateChatHistoryKey(string room) => $"ChatHistory:{room}"; CacheHelper.GenerateChatDeletedKey(Guid id) => $"ChatDeleted:{id}"
```
- [ ] Failing tests (memory store): room keys isolate; `RemoveAsync` removes the id and sets `ReplyTo.Deleted=true, Snippet=null` on replies to it; unknown id no-op.
- [ ] Implement both stores (Redis `RemoveAsync`: read list, rebuild, `MULTI` DEL + RPUSH all + EXPIRE remaining TTL or `RetentionDays`; never throws). Add `ChatNames` tests (`Short`, `Display` fallback).
- [ ] Green. Commit.

### Task 4: Rate limiter + presence

**Files:**
- Create: `Services/Chat/{IChatRateLimiter,RedisChatRateLimiter,MemoryChatRateLimiter,IChatPresence,RedisChatPresence,MemoryChatPresence,ChatPresenceBroadcaster}.cs`
- Modify: `src/SpaceStroker.Infrastructure/DependencyInjection.cs` (register in the Redis / memory branches), `src/SpaceStroker.Api/Program.cs` (`AddHostedService<ChatPresenceBroadcaster>()`)
- Test: `UnitTests/Chat/{MemoryChatRateLimiterTests,MemoryChatPresenceTests}.cs`

**Produces:**
```csharp
public interface IChatRateLimiter { Task<bool> TryAcquireAsync(string wallet); }
public interface IChatPresence { Task AddAsync(string connectionId); Task RemoveAsync(string connectionId); Task<long> CountAsync(); Task RefreshAsync(); }
```
- Memory limiter takes a `Func<DateTime>` clock (testable) — 1/s and 20/min windows from `ChatSettings`. Redis limiter per spec §6.7 (fail-open on `RedisException`). Presence per §6.8; `RefreshAsync` re-scores local ids + trims stale; broadcaster loop: `RefreshAsync` → `CountAsync` → `IHubContext<ActiveGamesHub>.Clients.Group("chat:global").SendAsync("ChatOnlineCount", new { count })`, delay `OnlineBroadcastSeconds`, swallow + log errors.
- [ ] Failing tests: 2nd message within same second refused; 21st within a minute refused; next window allowed; presence add/remove/count.
- [ ] Implement, register, green, commit.

### Task 5: ChatService (global + direct sends)

**Files:**
- Create: `Services/Chat/{IChatService,ChatService}.cs`
- Test: `UnitTests/Chat/{ChatServiceGlobalTests,ChatServiceDirectTests}.cs`

**Consumes:** Tasks 2–4. **Produces:**
```csharp
public interface IChatService
{
    Task SendGlobalAsync(string wallet, string text, string? replyToId);
    Task<DirectMessageSendResult> SendDirectAsync(string senderWallet, string recipientWallet, string text, string? replyToId);
}
```
Behaviour exactly spec §6.5 (order of checks matters: text → mute → rate limit → exclusion/recipient checks). Push via `IHubContext<ActiveGamesHub>` (objects, not `.AsJson()`). Publish `ChatMessagePersistedEvent` (new shape from Task 7 — define the record here if Task 7 not done yet; Task 7 owns the consumer). Self-exclusion predicate inline (`SelfExcludedAt != null && (SelfExcludedUntil == null || SelfExcludedUntil > now)`). House wallets: `HouseWallets.Resolve(settings.Operator.Token)`.
- [ ] Failing tests per spec §11.1 rows "ChatServiceGlobalTests" and "ChatServiceDirectTests" (use in-memory `UnitTestsClass.ContextOptions`, mocked `IHubContext` capturing `SendCoreAsync`, memory store/limiter, mocked `IMessageBus`).
- [ ] Implement, green, commit.

### Task 6: Hub rewrite

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Hubs/ActiveGamesHub.cs`
- Test: `UnitTests/Hubs/ActiveGamesHubTests.cs`, `UnitTests/Hubs/HubDuelHarness.cs`

Per spec §6.3. Remove old chat members; constructor `(GeneralSettings, IMessageBus, IWalletSessionService, SpaceStrokerDbContext, IGameService, IChatService, IChatPresence)`. `JoinGlobalChat` also `Clients.Caller.SendAsync("ChatOnlineCount", new { count })`. `OnDisconnectedAsync` → presence remove.
- [ ] Replace old chat tests with: `AuthenticateChat` with chat token joins `dm:{wallet}` and returns true; with Orbit token → false + no group; `SendGlobalMessage` unauthenticated → `IChatService` not called; authenticated → called with the session wallet (not any client value); expired stored token → dropped; `SendDirectMessage` returns the service result / `dropped` when unauthenticated; `JoinGlobalChat` adds group + presence.
- [ ] Update `HubDuelHarness` ctor. Green, commit.

### Task 7: Global history route, consumer, purge

**Files:**
- Create: `Handlers/Chat/GetGlobalChat/{GetGlobalChatRequest,GetGlobalChatHandler}.cs`
- Delete: `Handlers/Games/GetGameChat/*`, `UnitTests/Handlers/GetGameChatHandlerTests.cs` (→ `GetGlobalChatHandlerTests.cs`)
- Modify: `Consumers/ChatMessagePersisted/{ChatMessagePersistedEvent,ChatMessagePersistedConsumer}.cs`, `Jobs/ChatMessagePurgeJob.cs`, `Api/Controllers/GamesController.cs` (remove route)
- Test: `UnitTests/Handlers/GetGlobalChatHandlerTests.cs`, `UnitTests/Chat/{ChatMessagePersistedConsumerTests,ChatMessagePurgeJobTests}.cs`

Event: `ChatMessagePersistedEvent(Guid Id, string DisplayName, string Text, DateTime SentAt, string Wallet, Guid? ReplyToId, string? ReplyToDisplayName, string? ReplyToSnippet)`. Consumer skips when `ChatDeleted:{id}` exists (`ICacheService.TryGet`). Purge adds DMs / read states (older than `DirectMessageRetentionDays`) and expired mutes. Spec §6.9, §6.10, §6.13.
- [ ] Tests first, implement, green, commit.

### Task 8: Chat session + DM HTTP routes

**Files:**
- Create: `Handlers/Chat/CreateChatSession/*`, `Handlers/Chat/GetConversations/*`, `Handlers/Chat/GetThread/*`, `Handlers/Chat/MarkThreadRead/*`, `Handlers/Chat/SetChatBlock/*`, `src/SpaceStroker.Api/Chat/ChatSessionAccessor.cs`, `src/SpaceStroker.Api/Controllers/ChatController.cs`
- Modify: `Handlers/Players/GetWalletNonce/GetWalletNonceRequest.cs` (`ChatSession = "chat-session"` + `IsKnown`), error messages source (find `OrbitSessionInvalid` / `ReferralRequestInvalid` to locate it) — add `ChatSessionInvalid`, `src/SpaceStroker.Api/Program.cs` (register accessor)
- Test: `UnitTests/Handlers/{CreateChatSessionHandlerTests,GetConversationsHandlerTests,GetThreadHandlerTests,MarkThreadReadHandlerTests}.cs`

Routes + behaviour: spec §6.1, §6.11. Session-gated requests carry the wallet resolved by the controller via `ChatSessionAccessor` (null → 401 generic body like `OrbitController.RejectWithoutSession`). `MarkThreadRead` pushes `DirectMessagesRead { counterpartWallet, lastReadAt }` to `dm:{owner}`.
- [ ] Tests first (spec §11.1 rows), implement, green, commit.

### Task 9: Admin moderation

**Files:**
- Create: `Handlers/Admin/{DeleteChatMessage,GetChatMutes,SetChatMute,RemoveChatMute}/*`
- Modify: `src/SpaceStroker.Api/Controllers/AdminController.cs` (routes + `[HttpOptions]` list)
- Test: `UnitTests/Handlers/Admin/ChatModerationHandlerTests.cs`

Spec §6.12. Delete: store `RemoveAsync`, cache `ChatDeleted:{id}` (TTL `RetentionDays`), `ExecuteDelete` row, `ExecuteUpdate` replies, broadcast `ChatMessageDeleted { id }`; unknown id (not in store nor DB) → null result (404).
- [ ] Tests first, implement, green, commit.

### Task 10: Integration tests

**Files:**
- Delete: `src/SpaceStroker.Tests.Integration/GameChatTests.cs`
- Create: `src/SpaceStroker.Tests.Integration/ChatFlowTests.cs`
- Modify: `Harness/IntegrationTestFixture.cs` (replace by-type chat helpers with global ones; add `MintChatSessionAsync(Account)` doing real nonce + sign + `POST /chat/session`; a hub-connection helper that can `AuthenticateChat`)

Four tests per spec §11.2 (happy path, block refusal, moderation, token separation). Use real Solnet `Account`s (see `OrbitFlowTests.cs:60-95`). Admin token: reuse whatever existing admin integration tests use (`grep -rn "X-Admin-Token" src/SpaceStroker.Tests.Integration`).
- [ ] Write, run `scripts\run_integration_tests.bat`, fix until green. Also run the full unit suite. Commit.

---

## Self-review

Spec coverage: §6.1→T8, §6.2→T1, §6.3→T6, §6.4→T3, §6.5→T5, §6.6→T3, §6.7/6.8→T4, §6.9/6.10/6.13→T7, §6.11→T8, §6.12→T9, §4→T2, §5→T2, §11.2→T10. No gaps.

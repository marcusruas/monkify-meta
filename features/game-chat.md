# Feature — Game chat (per game type)

> ⚠️ **SUPERSEDED on 2026-10-22 by [`chat.md`](chat.md)** (Chat revamp, #205). Per-type rooms,
> `JoinChat` / `SendChatMessage` / `SendPlayerChatMessage`, `GET /games/{parametersId}/chat`,
> `GameSidePanel` / `GameChatPanel` and the client-trusted display name no longer exist. Kept as history
> only — do not use it to describe current behavior.

> Status: **shipped.** Authoritative specs: [`../specs/chat-per-game-type.md`](../specs/chat-per-game-type.md)
> (the per-type room + history, merged via `feature/chat-per-game-type` on 2026-09-16, Trello #185) on top
> of [`../specs/game-chat.md`](../specs/game-chat.md) (v2 — identity, limits, persistence, purge; 2026-07-03).
> Where the two disagree, `chat-per-game-type` wins: the room is the **game type**, not the round.

## What it does

Players on the game screen talk in a chat room that belongs to the **game type** they are playing
(Cosmic Sprint, Space Jackpot, …). The conversation carries on across rounds, and opening or refreshing
the page shows the **last 100 messages** of that type (within the retention window) before new ones
arrive live. It lives in the right-docked cabinet (`GameSidePanel`, chat-only) on desktop and behind the
`GAME | CHAT` segment on mobile.

## Core rules

**Room**
- One room per game type (`GameParameters.Id`). SignalR group `chat:{gameTypeId}`; event
  `{gameTypeId}/ChatMessage` (`GeneralSettings.Games.GameChatEndpoint` = `"{0}/ChatMessage"`, formatted with
  the type id). It is separate from the round's `gameId` group.
- A round rollover and the `Ended` state **do not touch chat**: the list is not cleared and the input is
  never read-only. The client joins the type's room once and only leaves it on a game-type switch
  (leave → clear list → join the new room → load its history) or an idle disconnect.
- `JoinChat` / `SendChatMessage` accept only a type that is in the **cached active catalogue**
  (`IGameService.GetAllActiveGameParameters`, 12h cache, invalidated by admin create/deactivate). Unknown,
  non-GUID or deactivated ids are dropped silently — no broadcast, no history, no persistence, no error.
  `LeaveChat` is not validated.

**Sending** — `SendChatMessage(gameTypeId, gameId, displayName, text)`
- Guards: text non-empty and ≤ **200** chars, display name non-empty, `gameId` parses as a GUID, type is
  active. Any failure drops the message silently. No server rate limiting.
- `gameId` is the round on the sender's screen — stored for **audit only**, trusted like the display name.
- The hub generates the message `id` (GUID) and `sentAt` (server UTC), then in order: **broadcasts**
  `{ id, displayName, text, sentAt }` to the room → **appends** it to the type's history → **publishes**
  `ChatMessagePersistedEvent(Id, GameTypeId, GameId, DisplayName, Text, SentAt)`. The realtime path never
  touches the DB.

**History** — `GET /games/{parametersId}/chat`
- Returns the newest ≤ **100** messages of the type, **oldest first**, filtered to
  `sentAt ≥ now − Chat.RetentionDays`. Anonymous.
- Source: a per-type Redis LIST `ChatHistory:{gameTypeId}` — every append is one transaction of RPUSH +
  LTRIM to the newest 100 + EXPIRE `RetentionDays`. On a **miss** (key absent, or any Redis error) the handler
  reads the newest 100 DB rows of the type within retention and **refills** the list, merged by `id` with
  anything appended meanwhile. Redis-less runs use an in-process store with the same semantics.
- Unknown or inactive type → the vague `ParameterNotFound` validation failure (400), like the stats route.
- ⚠️ Retention: committed `GeneralSettings.Chat.RetentionDays` is **2** (class default 7), so history reaches
  back 2 days.

**Persistence and purge**
- `ChatMessagePersistedConsumer` (`SpaceStroker.Consumers`) inserts `GameChatMessage` with the broadcast's
  `Id`, `GameTypeId`, `GameId`, and `CreatedDate = SentAt`. It is **idempotent**: a redelivered event (same
  `Id`) inserts nothing. An event with `GameTypeId == Guid.Empty` (queued by a pre-#185 hub) is dropped.
- The rows are the audit trail **and** the history fallback. The daily Hangfire job `game-chat-purge`
  (`ChatMessagePurgeJob`) deletes rows older than `RetentionDays`.

**Client**
- Order on joining a new room: **join first, then fetch history**, merge history and live messages by `id`,
  sort by `sentAt` (parsed dates — live and history timestamps can differ in fraction digits). History is
  fetched without blocking the round load; a failed fetch is logged, never toasted.
- After a SignalR reconnect the client re-joins the room and re-fetches history to backfill the gap.
- Identity: the player's display name if set, else the truncated wallet (`Ax4f…9kQz`). A player with no
  connected wallet can **read** but not send ("CONNECT WALLET TO CHAT"). Client limits: 200 chars, 1 message
  per second, inline validation. Plain text only.
- Mobile unread dot (`useChatUnread`): set only by a **live** message from someone else while the chat
  segment is hidden — a history load never sets it.
- Practice mode has no chat.

## Key code touchpoints

**monkify-backend**
- `SpaceStroker.Infrastructure/Hubs/ActiveGamesHub.cs` — `JoinChat`, `LeaveChat`, `SendChatMessage`,
  `ChatGroupFor`, `MaxChatMessageLength`.
- `SpaceStroker.Infrastructure/Services/Chat/` — `IChatHistoryStore`, `RedisChatHistoryStore`,
  `MemoryChatHistoryStore`, `ChatHistory` (`Limit = 100`, retention helpers); key factory
  `CacheHelper.GenerateChatHistoryKey`; registered in `DependencyInjection.cs` (Redis vs memory branch).
- `SpaceStroker.Infrastructure/Handlers/Games/GetGameChat/` + `SpaceStroker.Api/Controllers/GamesController.cs`
  (`GET games/{parametersId}/chat`); DTO `Contracts/Games/GameChatMessageDto.cs`.
- `SpaceStroker.Infrastructure/Consumers/ChatMessagePersisted/` — event + idempotent consumer.
- `SpaceStroker.Domain/Games/Entities/GameChatMessage.cs` (`GameTypeId`, `GameId`, `DisplayName`, `Text`);
  inline mapping in `SpaceStrokerDbContext` (index `(GameTypeId, CreatedDate)`); migration
  `20260916213233_ChatPerGameType` (backfills `GameTypeId` from `Games.ParametersId`, deletes orphans).
- `SpaceStroker.Infrastructure/Jobs/ChatMessagePurgeJob.cs`; settings `Domain/Configs/Entities/ChatSettings.cs`.
- Tests: `UnitTests/Hubs/ActiveGamesHubTests.cs`, `UnitTests/Chat/*`, `UnitTests/Handlers/GetGameChatHandlerTests.cs`,
  `SpaceStroker.Tests.Integration/GameChatTests.cs` (⚠️ after seeding a type outside `SeedIsolatedTypeAsync`,
  call `InvalidateGlobalCaches` or every send is dropped by the cached active-type check).

**monkify-front**
- `src/hooks/useGameHub.ts` — `attachToChat` / `detachFromChat` / `onChatRejoined` / `currentChatTypeId`
  (a chat channel separate from the round's `attachToGame`; re-joined in `onreconnected`, cleared by
  `stopForIdle`).
- `src/hooks/useGame.ts` — `chatMessages`, `lastLiveChatMessage`, `mergeChatMessages`, `loadChatHistory`,
  room join at the end of `initalizeWebsocketConnections`, `sendChatMessage` (type id + round id).
- `src/hooks/useGameChat.ts` (identity, limits, `canSend`), `src/services/mainService.ts` (`getGameChat`),
  `src/types/index.ts` (`ChatMessage` with `id`).
- `src/components/game/GameChatPanel.vue` (list keyed by `id`, unread bump on `lastLiveChatMessage`),
  `GameSidePanel.vue`, `GameMobileTabs.vue`, `src/hooks/useChatUnread.ts`.

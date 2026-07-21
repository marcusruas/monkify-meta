# Feature — Game chat (Add chat)

> Status: **shipped (v2).** Authoritative spec: `../specs/game-chat.md`
> (merged via `feature/game-chat` on 2026-07-03).

## What it does

Gives players on `/game` a lightweight **per-game chat** while watching the monkey type — during
the betting/waiting phase and the live run. Messages appear in a right-sidebar panel, attributed to
each player's **display name** (same identity as bets/winners). Chat reuses the existing
`ActiveGamesHub` connection and game SignalR group — no third hub or connection.

## Core rules

- **Scope:** one chat room per **active game instance** = the existing SignalR group keyed by
  `gameId`. Transport: hub method `SendChatMessage(gameId, displayName, text)`; clients listen
  on `{gameId}/ChatMessage` for `{ displayName, text, sentAt }`.
- **Identity (client-resolved):** nickname = `displayName` from `useDisplayCurrency` if set, else a
  **truncated identifier** (last-4 style of wallet or token `sub`), else `ANON` for regulated players
  with no resolved id. No auto-generated `Player-XXXX` nicknames; no `sessionStorage` guest names.
  **No per-message server/DB lookup** on the realtime path.
- **Send eligibility:** regulated players always have token identity → can send. **Crypto players must
  connect a wallet to send** (input disabled + connect prompt until then); they can still **read**.
- **Persistence (audit-only):** after broadcast, the hub publishes `ChatMessagePersistedEvent` →
  `ChatMessagePersistedConsumer` writes `GameChatMessage` (`GameId`, `DisplayName`, `Text`;
  `CreatedDate` = sent time). **Never replayed** to players — late joiners see no prior messages; no
  history-load endpoint.
- **Retention:** daily Hangfire job `ChatMessagePurgeJob` deletes rows older than
  `GeneralSettings.Chat.RetentionDays` (**default 7**). Migration applied by `SpaceStroker.Consumers`.
- **Hub guards:** reject empty/whitespace text, text **> 200** chars, empty game id, or empty
  display name — no broadcast, no persist. **No server rate limiting.**
- **Client limits:** plain text only; **200 chars max**, **1 message / 1 second**, inline validation
  in `useGameChat`. No linkify, emoji picker, or markdown.
- **Lifecycle on `/game`:** panel **open by default**; hide/show toggle (not persisted). After game
  `Ended`: messages stay visible, input **read-only**. On rollover to a new instance: message list
  clears, input re-enables, same identity kept, chat re-registers on the new game group.
- **No REST API** for chat. No operator-specific messaging added.

> **Update (in flight, `feature/side-panel-cabinet`).** Presentation-only restyle of the chat panel as
> part of the side-panel cabinet re-housing (see `game-type-stats.md` and INDEX "Feature currently
> in flight"). The chat log now renders as CRT text (VT323, sender names in green, your **own** messages
> in orange — matched by your resolved display name, like the terminal's "YOU"); the Send button is
> orange (Silkscreen); a small **connection meta line** under the input shows "CONNECTED AS
> &lt;name&gt;" / "CONNECT WALLET TO CHAT" / "CHAT CLOSED" alongside the char counter; and the empty
> state uses an in-world line ("&gt; comms open — say something.") instead of "No messages yet." Each
> line's **`HH:MM` timestamp is hidden in the docked view and restored (with a larger log font) only
> when the panel is maximized** (`maximized` prop from `GameSidePanel`), where there's width for it.
> Identity, send eligibility, limits, persistence, and lifecycle are unchanged.

## Key code touchpoints

**Backend (`monkify-backend`)**

- `SpaceStroker.Infrastructure/Hubs/ActiveGamesHub.cs` — `SendChatMessage`, `MaxChatMessageLength = 200`
- `SpaceStroker.Infrastructure/Consumers/ChatMessagePersisted/` — event + consumer
- `SpaceStroker.Infrastructure/Jobs/ChatMessagePurgeJob.cs` — daily purge (registered in `SpaceStroker.Consumers/Program.cs`)
- `SpaceStroker.Domain/Games/Entities/GameChatMessage.cs` — EF entity
- `SpaceStroker.Infrastructure/Migrations/20260703145848_AddGameChatMessages.cs`
- `SpaceStroker.Domain/Configs/Entities/ChatSettings.cs` — `RetentionDays`
- `SpaceStroker.Common/spacestrokersettings.json` — `Chat.RetentionDays`, `Games.GameChatEndpoint`
- Tests: `ActiveGamesHubTests`, `ChatMessagePersistedConsumerTests`, `ChatMessagePurgeJobTests`

**Frontend (`monkify-front`)**

- `src/hooks/useGameChat.ts` — identity, limits, send eligibility
- `src/hooks/useGame.ts` — `{gameId}/ChatMessage` handler, `sendChatMessage`, rollover clear
- `src/components/game/GameChatPanel.vue` — sidebar UI
- `src/pages/GamePage.vue` — mounts chat panel
- `src/types/index.ts` — `ChatMessage`
- `src/utils/launchToken.ts` — `getPlayerIdFromToken` (regulated chat identity)

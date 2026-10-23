# Profile V2 — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Edson profile icons (catalog, unlock rules, signed save, read paths) and a per-wallet notifications feed (donation, trophyUnlocked, refundIssued, jackpotWon) in `monkify-backend`.

**Architecture:** Icons are a code catalog (`PlayerIcons`) plus a stored `Player.AvatarId`; unlocks are derived on read from the existing trophy derivation, extended to carry the round that unlocked each trophy. Notifications are one new table written by a scoped `NotificationService` (insert-or-skip on a unique `(Wallet, Type, DedupKey)`, then push on `dm:{wallet}`); writers are the donation handler, the refund consumer and a new `RoundSettledConsumer` fed by `RewardGameConsumer`.

**Tech Stack:** .NET 8, EF Core/Postgres, MediatR, MassTransit/RabbitMQ, SignalR (Redis backplane), xUnit + Testcontainers.

**Spec:** `monkify-meta/specs/profile-v2.md` (worktree `.worktrees/profile-v2/monkify-meta`). Section refs (§) below point there; read the spec section before each task.

**Worktree:** `C:\Users\kurok\Desktop\monkify\.worktrees\profile-v2\monkify-backend` (branch `feature/profile-v2`). Never work in the hub.

## Global Constraints

- EF mapping **inline** in `SpaceStrokerDbContext.OnModelCreating`; migration via `dotnet ef migrations add ProfileV2AvatarsAndNotifications -p SpaceStroker.Infrastructure -s SpaceStroker.Consumers`; never hand-edit the snapshot.
- Unit tests via `scripts/run_tests.bat` only (never bare `dotnet test` — it pulls the ProductionGames Monte Carlo suite). Integration via `scripts/run_integration_tests.bat` (Docker up).
- Every refused icon save → `Messaging.ReturnValidationFailureMessage(ErrorMessages.SelfExclusionRequestInvalid)` — the same generic failure; nothing written.
- Committed staff lists are **empty** (`[]`). Config path `GeneralSettings:PlayerIcons:<Elder|Phantom|Beanie>:Wallets`.
- A notification write must never fail a payout, refund or donation: wrap in try/catch + `Log.Warning`.
- Name-only save signs `BuildMessage` byte-for-byte; theme-without-icon signs `BuildProfileMessage` byte-for-byte.
- Copy: "gamble", never "wager". No level-gated icons.
- Commits happen only after the user approves (skill Step 10); during execution, do **not** commit — leave changes in the worktree.

## Review Focus

1. **Settlement retried / MassTransit redelivery** → no duplicate notification, no duplicate push (unique index + `CreateAsync` returns null). Pinned in Task 6 (`Duplicate_returns_null_and_does_not_push`) and Task 8 (`Rerun_creates_nothing`).
2. **Deploy with existing trophies** → first settled round after deploy notifies only for trophies whose unlock `GameId` is that round (Task 8 `Trophy_unlocked_in_older_round_creates_nothing`).
3. **Icon save with a valid signature but a name signature replayed** → a `BuildMessage` signature must not set an icon (Task 4 `Name_signature_cannot_set_icon`).
4. **Notification push throws (hub/Redis down)** → row still stored, payout/refund/donation still succeed (Task 6 `Push_failure_still_returns_dto`).
5. **Unknown/blank `avatarId` alongside a valid name** → whole request refused, name not saved (Task 4 `Locked_icon_with_name_saves_nothing`).

---

### Task 1: Catalog, settings and data model

**Files:**
- Create: `src/SpaceStroker.Domain/Players/PlayerIcons.cs` (model on `ProfileThemes.cs` in the same folder)
- Create: `src/SpaceStroker.Domain/Chat/Entities/Notification.cs`, `src/SpaceStroker.Domain/Chat/NotificationTypes.cs`
- Modify: `src/SpaceStroker.Domain/Games/Entities/Player.cs` (`AvatarId`), `src/SpaceStroker.Domain/Configs/Entities/GeneralSettings.cs` (§5.2), `src/SpaceStroker.Common/spacestrokersettings.json` (§5.2), `src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` (§4.3)
- Create (generated): `src/SpaceStroker.Infrastructure/Migrations/*_ProfileV2AvatarsAndNotifications.cs` + snapshot
- Test: `src/SpaceStroker.Tests/UnitTests/Domain/PlayerIconsTests.cs`, `src/SpaceStroker.Tests/UnitTests/Api/PlayerIconSettingsBindingTests.cs` (copy the shape of `HolderJackpotSettingsBindingTests`)

**Interfaces:**
- Produces: `PlayerIcons` exactly as §5.1 (`Default="default"`, `Edson="edson"`, `MaxLength=20`, `enum UnlockKind { Free, Trophy, AllTrophies, Staff }`, `record IconRule(string Key, UnlockKind Kind, string? Trophy = null, string? StaffList = null)`, `All` in §5.1 table order, `IconRule? Find(string key)` ordinal), staff list names `"Elder"`, `"Phantom"`, `"Beanie"`; `NotificationTypes.Donation="donation"`, `TrophyUnlocked="trophyUnlocked"`, `RefundIssued="refundIssued"`, `JackpotWon="jackpotWon"`; `Notification` entity per §4.2; `DbSet<Notification> Notifications`; `GeneralSettings.PlayerIcons` (`PlayerIconSettings` with `Elder/Phantom/Beanie : StaffIconList { List<string> Wallets }`).

- [ ] **Step 1: Write failing tests** — `PlayerIconsTests`: `All.Count == 24`; keys unique and `Length <= 20`; first key `"edson"`; 10 `Free`, 10 `Trophy`, 1 `AllTrophies` (`"golden"`), 3 `Staff` (`elder`/`phantom`/`beanie` → `Elder`/`Phantom`/`Beanie`); trophy rules' `Trophy` values == `AchievementKeys.Ordered` as a set (each trophy exactly once); `Find("spartan").Trophy == "firstDuelWin"`; `Find("Spartan") == null`; `Find("default") == null`. `PlayerIconSettingsBindingTests`: binding the committed `spacestrokersettings.json` `GeneralSettings` yields three non-null, empty `Wallets` lists.
- [ ] **Step 2: Run** `scripts/run_tests.bat` — expect compile failure.
- [ ] **Step 3: Implement** the domain types, settings, JSON, `Player.AvatarId`, the EF mapping (§4.3, next to `ProfileTheme` and following the `Donation` mapping), then generate the migration from `src/` with the command in Global Constraints. Inspect the migration: one nullable `AvatarId varchar(20)` column, one `Notifications` table, unique index `(Wallet, Type, DedupKey)`, index `(Wallet, CreatedDate)`, nothing else.
- [ ] **Step 4: Run** `scripts/run_tests.bat` — PASS.

### Task 2: `PlayerIconRules`

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Services/Players/PlayerIconRules.cs` (+ `IPlayerIconRules`)
- Modify: `src/SpaceStroker.Infrastructure/DependencyInjection.cs` (register scoped/singleton next to `IPlayerProfileService`)
- Test: `src/SpaceStroker.Tests/UnitTests/Services/Players/PlayerIconRulesTests.cs`

**Interfaces:**
- Consumes: `PlayerIcons`, `GeneralSettings.PlayerIcons`, `AchievementDto(string Key, bool Unlocked, DateTime? UnlockedAt)` (existing; check its exact shape in `Contracts/Players`).
- Produces: `IPlayerIconRules { IReadOnlySet<string> Unlocked(string wallet, IEnumerable<AchievementDto> achievements); bool IsStaffAllowed(string wallet, string staffList); string? Effective(string wallet, string? storedAvatarId); IReadOnlyList<string> UnlockedOrdered(string wallet, IEnumerable<AchievementDto> achievements); }` — `UnlockedOrdered` = `Unlocked` in catalog order (for `UnlockedAvatars`). Ctor `(GeneralSettings settings)`. Wallet lists cleaned per §5.2 (trim, drop blanks, ordinal distinct).

- [ ] **Step 1: Failing tests:** free keys always present, with no achievements; `racer` only when `firstSprintWin` unlocked; `golden` only when all 10 unlocked (9 → absent); `elder` only when the wallet is in `Elder.Wallets` (wallet in `Admins`/`Moderators` only → absent; in `Phantom` list → `phantom` not `elder`); whitespace-padded config wallet matches; `Effective(w, "elder")` → null when not in list, `"elder"` when in; `Effective(w, "racer")` → `"racer"` (trophies not checked, decision 17); `Effective(w, null)` → null; `Effective(w, "bogus")` → null.
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement.** **Step 4: Run** — PASS.

### Task 3: Trophy unlock rounds

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Services/Players/PlayerProfileService.cs`, `IPlayerProfileService.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Services/Players/PlayerProfileAchievementsTests.cs` (add cases; existing cases must pass **untouched**)

**Interfaces:**
- Produces: `public sealed record AchievementUnlock(string Key, DateTime At, Guid GameId);` (in `Services/Players`) and `Task<IReadOnlyList<AchievementUnlock>> ComputeAchievementUnlocksAsync(Guid playerId, string wallet, CancellationToken ct = default)` on the interface — only unlocked trophies, `AchievementKeys.Ordered` order. `ComputeAchievementsAsync` becomes `Build(unlocks)`.

Approach (§6.4): replace each `MinAsync` date with an ordered first: `rows.OrderBy(x => x.EndDate ?? x.CreatedDate).ThenBy(x => x.GameId).Select(x => new { At = x.EndDate ?? x.CreatedDate, x.GameId }).FirstOrDefaultAsync()`. For the per-mode first wins / first plays, the current grouped aggregate cannot carry the GameId — project per-mode first-win and first-play rows with the same ordered-first query per mode type-id set (4 modes × 2 = small queries) or one narrow projection of `(ParametersId, GameId, At, Won)` grouped in C#; pick whichever keeps the existing tests green. Streaks: record the `GameId` of the round where `run` hits 3 / 5 (add `GameId = g.Key` to the projection). Explorer: the `GameId` of the latest of the four first-play rows. Ties on `At` broken by `GameId` consistently everywhere.

- [ ] **Step 1: Failing tests** (reuse the file's seeding helpers): first Sprint win in round B after a loss in A → unlock `GameId == B`; two wins → the earlier round; win streak of 3 over rounds R1..R3 → `winStreak3.GameId == R3`; explorer → `GameId` of the last mode's first play; active streamer → empty list; `ComputeAchievementsAsync` result equals `Build` over the unlocks (dates match).
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement.** **Step 4: Run** — PASS including all pre-existing `PlayerProfileAchievementsTests`.

### Task 4: Signed icon save (`PATCH /players/preferences`)

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Players/UpdatePlayerPreferences/UpdatePlayerPreferencesHandler.cs`, `UpdatePlayerPreferencesRequestBody.cs` (`[MaxLength(20)] string? AvatarId`), `PlayerPreferencesDto` (find it; add `string? AvatarId`), `GetPlayerPreferences` handler if it builds the DTO (return `AvatarId` too)
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/UpdatePlayerPreferencesIconTests.cs` (model on the existing preferences handler tests — real ed25519 keypairs)

**Interfaces:**
- Consumes: `IPlayerIconRules` (Task 2), `IPlayerProfileService.ComputeAchievementsAsync` (existing), `PlayerIcons.Find`.
- Produces: `public static string BuildProfileIconMessage(string wallet, string displayName, string theme, string icon, string nonce)` — lines exactly `"Space Stroker profile"`, `Wallet: {wallet}`, `Display name: {displayName}`, `Theme: {theme}`, `Icon: {icon}`, `Nonce: {nonce}` joined with `"\n"`. Handler ctor gains `IPlayerIconRules`, `IPlayerProfileService` (update every construction site in tests).

Rules (§6.1): message selection `avatarId != null` → icon message (`""` for absent name/theme; icon = raw body value) · `theme != null` → `BuildProfileMessage` · else `BuildMessage`. Pre-signature validation: `avatarId` must be `"default"` or `Find(key) != null`; "at least one of name/theme/icon". Post-signature (nonce already removed): key ≠ `"default"` → load existing player (may be null → achievements = none) → `rules.Unlocked(wallet, achievements).Contains(key)` else generic failure, nothing saved. Store `"default"` as null. Drop `PlayerLevel:{wallet}` when theme **or** avatar provided; also drop `PlayerProfile:{wallet}` when avatar provided (use `CacheHelper`'s existing profile cache settings method).

- [ ] **Step 1: Failing tests:** `BuildProfileIconMessage("W","","","spartan","N") == "Space Stroker profile\nWallet: W\nDisplay name: \nTheme: \nIcon: spartan\nNonce: N"`; name-only save verifies against `BuildMessage` (unchanged); theme-only against `BuildProfileMessage`; `suit` on a fresh wallet → saved, `AvatarId == "suit"`, `PlayerLevel:` cache entry removed; `spartan` without the trophy → failure, row unchanged; `Locked_icon_with_name_saves_nothing` (name + `spartan` → display name not changed); `bogus` → failure before signature check; `elder` not in list → failure; `elder` in list → saved; `"default"` → `AvatarId == null`; `Name_signature_cannot_set_icon` (body has `avatarId:"suit"` but signature over `BuildMessage`) → failure.
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement.** **Step 4: Run** — PASS.

### Task 5: Read paths + avatar guard

**Files:**
- Modify: `Handlers/Players/GetPlayerLevels/GetPlayerLevelsHandler.cs`, `Contracts/Players/PlayerLevelsDto.cs`, `Handlers/Players/GetPlayerProfile/GetPlayerProfileHandler.cs`, `Contracts/Players/PlayerProfileDto.cs`, `DependencyInjection.cs`
- Create: `Services/Players/PlayerAvatarGuard.cs` (+ `IPlayerAvatarGuard`)
- Test: `UnitTests/Handlers/GetPlayerLevelsHandlerTests.cs`, `UnitTests/Handlers/GetPlayerProfileHandlerTests.cs` (extend), `UnitTests/Services/Players/PlayerAvatarGuardTests.cs` (new)

**Interfaces:**
- Consumes: `IPlayerIconRules`, `IPlayerProfileService`, `ICacheService`, `CacheHelper` level/profile cache settings.
- Produces: `PlayerLevelsDto.Avatars : IReadOnlyDictionary<string,string>`; `PlayerProfileDto.AvatarId : string?`, `UnlockedAvatars : IReadOnlyList<string>`; `IPlayerAvatarGuard { Task RevalidateAsync(Guid? playerId, CancellationToken ct = default); Task<string?> RevalidateAsync(Guid playerId, string wallet, string? storedAvatarId, IReadOnlyList<AchievementDto> achievements, CancellationToken ct = default); }` — second overload used by the profile handler with already-computed achievements; returns the effective value after self-heal.

Rules: §6.2 (cache record gains `AvatarId`; `Effective` applied **after** the cache; only non-null go in `Avatars`), §6.3 (unavailable/no-row bodies: `AvatarId = null`, `UnlockedAvatars` = free + allowed staff; stamp live values after cache/compute; self-heal), §6.8 (guard: only `Trophy`/`AllTrophies` icons are checked; on loss `ExecuteUpdateAsync` `AvatarId = null`, drop `PlayerLevel:` and `PlayerProfile:`).

- [ ] **Step 1: Failing tests:** levels — wallet with `AvatarId="suit"` → `Avatars[w]=="suit"`; wallet with null → absent; cached `elder` for a wallet removed from config → absent without clearing cache; profile — `avatarId` and `unlockedAvatars` (free keys in catalog order, plus `racer` when `firstSprintWin`); stored `racer` with no trophy → response `avatarId == null` and DB row cleared; self-excluded body unchanged (`available:false` only); guard — refund-lost trophy clears and drops both caches; free icon untouched; staff icon untouched (checked live elsewhere).
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement.** **Step 4: Run** — PASS.

### Task 6: `NotificationService` + wire shapes

**Files:**
- Create: `Services/Notifications/NotificationService.cs` (+ `INotificationService`), `Contracts/Chat/NotificationDto.cs`, `Contracts/Chat/NotificationsPageDto.cs`
- Modify: `Services/Chat/ChatEvents.cs` (`NotificationReceived = "NotificationReceived"`, `NotificationsRead = "NotificationsRead"`), `DependencyInjection.cs`
- Test: `UnitTests/Services/Notifications/NotificationServiceTests.cs`

**Interfaces:**
- Consumes: `IHubContext<ActiveGamesHub>`, `ActiveGamesHub.DirectMessageGroupFor(wallet)` (existing; see how `ReportDonationHandler` pushes).
- Produces: `INotificationService.CreateAsync(string wallet, string type, string dedupKey, object payload, bool push = true, CancellationToken ct = default) : Task<NotificationDto?>`; `record NotificationDto(Guid Id, string Type, DateTime CreatedAt, bool Unread, JsonElement Payload)` with static `From(Notification)`; `record NotificationsPageDto(IReadOnlyList<NotificationDto> Items, bool HasMore, int UnreadCount)`. Payload serialized camelCase (§6.6 table). Unique violation detection: catch `DbUpdateException` whose inner `PostgresException.SqlState == "23505"`; detach the entity so the scoped context stays usable. In-memory/SQLite test providers: also pre-check `AnyAsync` on the triple before insert (cheap, makes unit tests deterministic); the catch covers the race.

- [ ] **Step 1: Failing tests:** insert → row stored, `Unread == true`, one `SendAsync("NotificationReceived", …)` to `dm:{wallet}`; `Duplicate_returns_null_and_does_not_push`; `push:false` → stored, no send; `Push_failure_still_returns_dto` (hub throws).
- [ ] **Step 2–4:** run FAIL → implement → run PASS.

### Task 7: Feed routes + donation writer

**Files:**
- Create: `Handlers/Chat/GetNotifications/*` (request + handler), `Handlers/Chat/MarkNotificationsRead/*`
- Modify: `SpaceStroker.Api/Controllers/ChatController.cs` (two routes, add to `[HttpOptions(...)]`, same session guard as `GET /chat/donations`), `Handlers/Chat/ReportDonation/ReportDonationHandler.cs`
- Test: `UnitTests/Handlers/NotificationsHandlerTests.cs` (new), `UnitTests/Handlers/DonationHandlerTests.cs` (extend)

**Interfaces:**
- Consumes: `INotificationService`, `NotificationsPageDto`, chat-session wallet resolution exactly as `GetDonations`/`MarkDonationsRead` do (copy their request shape).
- Produces: `GET /chat/notifications?before=<iso>` → `NotificationsPageDto` (30 per page, newest first, `hasMore`, unread over all rows); `POST /chat/notifications/read` → 204, stamps `Notification.ReadAt` and `Donation.ReadAt` for the wallet, pushes `NotificationsRead {}` once if anything changed.

- [ ] **Step 1: Failing tests:** 35 rows → first page 30 newest-first, `hasMore`, second page via `before` → 5; unread count counts all unread; mark read stamps both tables and pushes once, second call pushes nothing; other wallets' rows untouched; donation report → one `donation` notification (`DedupKey = signature`, payload §6.6); blocked donor → stored, not pushed; re-reporting the same signature → still one row.
- [ ] **Step 2–4:** FAIL → implement → PASS.

### Task 8: Settled-round consumer (trophies + jackpot)

**Files:**
- Create: `Consumers/RoundSettled/RoundSettledEvent.cs` (`record RoundSettledEvent(Guid GameId)`), `Consumers/RoundSettled/RoundSettledConsumer.cs`
- Modify: `Consumers/RewardGame/RewardGameConsumer.cs` (publish at both `RewardForWinnersCompleted` exits, §6.5; publish failure logged, not thrown), `SpaceStroker.Consumers/Program.cs` (`AddConsumer` + `ReceiveEndpoint` like siblings), `Services/Cache/CacheHelper.cs` (`TrophiesComplete:{wallet}`, 1 h)
- Test: `UnitTests/Consumers/RoundSettledConsumerTests.cs` (new), `UnitTests/Consumers/RewardGameConsumerTests.cs` (extend)

**Interfaces:**
- Consumes: `IPlayerProfileService.ComputeAchievementUnlocksAsync` (Task 3), `INotificationService` (Task 6), `GameModeClassifier.Classify`, `PlayerIcons` (trophy → icon key), `HouseWallets.Resolve`.
- Produces: `trophyUnlocked` payload `{ trophy, icon, golden }`, `jackpotWon` payload `{ gameId, amount, holder }` (`holder = bet.EntryKind == Holder`), `DedupKey` = trophy key / game id string.

Rules in §6.5 steps 1–3. Golden: when all 10 are unlocked and `max(unlock.GameId by At)` is this game, the notification for that last trophy carries `golden:true` (if two trophies complete in the same round, set it on the one later in `AchievementKeys.Ordered`). Drop `PlayerProfile:{wallet}` when anything fresh. Per-player try/catch.

- [ ] **Step 1: Failing tests:** first Duel win in this game → exactly one `trophyUnlocked` `{trophy:"firstDuelWin", icon:"spartan", golden:false}`; `Rerun_creates_nothing`; `Trophy_unlocked_in_older_round_creates_nothing`; 10th trophy this game → `golden:true`; active streamer → none; house wallet bet → none; game not `RewardForWinnersCompleted` → none; jackpot-mode game, rewarded bet with Σ Credit 2.5 → `jackpotWon {amount:2.5}`; zero-credit win → none; non-jackpot mode → no `jackpotWon`. RewardGame: event published on no-winners exit and winners exit; not published on `ErrorWhenProcessingRewards`.
- [ ] **Step 2–4:** FAIL → implement → PASS.

### Task 9: Refund notification

**Files:**
- Modify: `Consumers/RefundInvalidBet/RefundInvalidBetConsumer.cs`
- Test: `UnitTests/Background/RefundInvalidBetConsumerTests.cs` (extend)

**Interfaces:**
- Consumes: `INotificationService`, `IPlayerAvatarGuard.RevalidateAsync(Guid?)`.
- Produces: `refundIssued` payload `{ betId, gameId, amount, destination }`, `destination = FundedByOrbit ? "orbit" : "wallet"`, `DedupKey = betId`.

Rules §6.7/decision 20: only after `MarkRefunded` succeeds on the normal bet path, `EntryKind == Paid && Amount > 0`, recipient = the bet's player wallet. Then `RevalidateAsync(bet.PlayerId)` for every refunded bet (any kind — a refund can remove a trophy round). Both wrapped so a failure never fails the refund.

- [ ] **Step 1: Failing tests:** paid wallet refund → `destination:"wallet"`; Orbit-funded → `"orbit"`; free / holder / streamer / `RefundWithoutBet` → none; refund that removes the only `firstSprintWin` round of a player wearing `racer` → `AvatarId` cleared.
- [ ] **Step 2–4:** FAIL → implement → PASS. Then run the whole `scripts/run_tests.bat` — all green.

### Task 10: Integration test (mandatory)

**Files:**
- Create: `src/SpaceStroker.Tests.Integration/ProfileV2FlowTests.cs` (`[Collection(IntegrationCollection.Name)]`); extend the harness config overrides only if it can't already inject `GeneralSettings:PlayerIcons:Elder:Wallets:0`.

**Interfaces:**
- Consumes: harness helpers used by `PlayerPreferencesTests`/`PlayerProfileFlowTests` (keypairs, nonce + signed PATCH), `GameFlowTests`/`DonationFlowTests` (play a Sprint round to a win, `WaitForStatusAsync`, `MintChatSessionAsync`, hub connection).

- [ ] **Step 1:** Write the four scenarios of spec §11.2 exactly (fresh wallet saves `suit` → profile + levels show it; `spartan` → 400, still `suit`; Elder via config 200 / non-listed 400; Sprint win → `trophyUnlocked {trophy:"firstSprintWin", icon:"racer"}` via `GET /chat/notifications` and via the hub's `NotificationReceived`; then `racer` saves 200).
- [ ] **Step 2: Run** `scripts/run_integration_tests.bat` (filter to `ProfileV2FlowTests` first, then the full suite) — PASS.

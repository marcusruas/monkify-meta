# Orbit (3/3) — Notifications, Telegram and the First-Win Upsell — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tell the player what Orbit did — a session-gated per-player SignalR channel that announces
every Orbit win, Telegram messages on every completed cash-out, a "paused — insufficient funds"
state, and a one-time first-win upsell into Orbit.

**Architecture:** Backend adds one hub method pair on the existing `ActiveGamesHub` (authorised by the
shipped Orbit session token, group derived server-side from the `PlayerId`), one event + consumer that
pushes wins from `SpaceStroker.Consumers` over the existing Redis backplane, an `ITelegramNotifier`
that is inert without a bot token, and three public `/orbit/telegram*` routes. Frontend adds a
notifications hook over the shipped one-socket-per-tab `useGameHub`, a Telegram section and paused
state in `OrbitModal`, and a tutorial-tier first-win modal fired from the existing round-ended
one-shot in `useGame`. **No migration** — `OrbitEnrollment.TelegramChatId` already exists.

**Tech Stack:** .NET 8 (MediatR, MassTransit, SignalR + Redis backplane, EF Core, xUnit + Shouldly +
Moq, Testcontainers), Vue 3 + TypeScript (composables in `src/hooks`, `vue3-toastify`, Tailwind +
`arcade.css`).

## Global Constraints

- **Base branch is `origin/release/orbit`, NOT `main`** — `main` carries zero Orbit code. Branch
  `feature/orbit-notifications`; worktrees `.worktrees/orbit-notifications/<repo>`; hubs stay on `main`.
- **No migration.** `OrbitEnrollment.TelegramChatId` exists (created by #121). Everything else is Redis
  or `localStorage`.
- **Secrets:** `TelegramBotToken` committed as `MOCK_TELEGRAM_BOT_TOKEN`; never logged; a `MOCK_`
  prefix counts as **not configured** (fail closed into inert).
- **Vagueness (`constraints.md` §6):** an invalid session, an unknown link code and a replayed link
  code are indistinguishable (`ErrorMessages.OrbitRequestInvalid`). "No `/start` seen yet" and
  "not linked" are ordinary state and get **clear** copy.
- **Host split (§8):** the API owns link/verify/unlink + reads; **Consumers** owns every send.
- **Telegram never fails a money path.** Every send/read is wrapped; failures log an `Error` and return.
- **Tests:** unit tests in `SpaceStroker.Tests` (`scripts/run_tests.bat`), mandatory happy-path
  integration tests in `SpaceStroker.Tests.Integration` (`scripts/run_integration_tests.bat`) — the card
  is **Level: Feature**.
- **Payload rule:** the win push carries amount + game type + token label and **never a balance**.
- Frontend env vars are `VUE_APP_*`. No new env var is needed by this feature.

### Decision taken at plan time (spec §15 open item 1)

The notification **event name is the constant `"OrbitNotification"`**, not `{playerId}/OrbitNotification`.
The per-player **group** already scopes delivery, and the codebase already has a constant, non-scoped
event name for exactly this shape (`Games.ActiveGamesEndpoint = "GameReceived"`). Consequences: no
`notificationEvent` field on `OrbitDto`, and `Games.OrbitNotificationEndpoint` is a plain name with no
`{0}` placeholder. Update spec §5.2, §6.3, §7.1, §7.3, §10 and §15 to match.

---

## File structure

**`monkify-backend`**

| File | Responsibility |
| --- | --- |
| `Domain/Configs/Entities/OrbitSettings.cs` | Telegram config + `IsTelegramConfigured` |
| `Domain/Configs/Entities/GameSettings.cs` | `OrbitNotificationEndpoint` |
| `Common/spacestrokersettings.json` | mock Telegram values, endpoint name, 3 rate-limit rules |
| `Common/Resources/ErrorMessages.resx` (+ Designer) | `OrbitTelegramNotFound` |
| `Infrastructure/Services/Orbit/OrbitNotificationChannel.cs` | the one group-name definition |
| `Infrastructure/Services/Telegram/TelegramOptions.cs` | named `HttpClient` constant + base address |
| `Infrastructure/Services/Telegram/ITelegramNotifier.cs` | the contract |
| `Infrastructure/Services/Telegram/TelegramNotifier.cs` | send + one `getUpdates`, inert without a token |
| `Infrastructure/Services/Cache/CacheHelper.cs` | link-code + update-offset keys |
| `Infrastructure/Hubs/ActiveGamesHub.cs` | `JoinOrbitNotifications` / `LeaveOrbitNotifications` |
| `Infrastructure/Consumers/OrbitNotification/OrbitWinNotificationEvent.cs` | ids + primitives only |
| `Infrastructure/Consumers/OrbitNotification/OrbitNotificationConsumer.cs` | group push |
| `Infrastructure/Services/Orbit/OrbitBetSettlementService.cs` | publish after the credit commits |
| `Infrastructure/Consumers/OrbitCashOut/OrbitCashOutConsumer.cs` | Telegram on `Completed` |
| `Infrastructure/Handlers/Orbit/StartTelegramLink/*` | issue a code + deep link |
| `Infrastructure/Handlers/Orbit/VerifyTelegramLink/*` | one `getUpdates`, store the chat id |
| `Infrastructure/Handlers/Orbit/UnlinkTelegram/*` | clear the chat id |
| `Infrastructure/Handlers/Orbit/GetOrbit/*` | `TelegramEnabled` + `TelegramLinked` |
| `Infrastructure/DependencyInjection.cs` | notifier + named client |
| `Api/Controllers/OrbitController.cs` | 3 routes + OPTIONS |
| `Consumers/Program.cs` | consumer + receive endpoint |

**`monkify-front`**

| File | Responsibility |
| --- | --- |
| `src/types/orbit.ts` | Telegram + notification types |
| `src/services/orbitService.ts` | 3 HTTP calls |
| `src/hooks/useGameHub.ts` | Orbit group join/leave/rejoin |
| `src/hooks/useOrbitNotifications.ts` | channel lifecycle + win toast |
| `src/hooks/useOrbit.ts` | Telegram actions + paused-for-funds |
| `src/hooks/useFirstWinUpsell.ts` | once-ever guarded modal state |
| `src/hooks/useGame.ts` | `ownBetWasPaid` + fire the upsell |
| `src/components/orbit/OrbitModal.vue` | Telegram section + paused row |
| `src/components/orbit/OrbitFirstWinModal.vue` | tutorial-tier upsell |
| `src/components/GameHeader.vue` | mount both |

**`monkify-meta`** — `deploy/terraform/workload/main.tf`, `deploy/tools/SpaceStroker.Deploy/EnvNaming.cs`,
plus spec/plan updates.

---

## Task 1: Telegram configuration

**Files:**
- Modify: `src/SpaceStroker.Domain/Configs/Entities/OrbitSettings.cs`
- Modify: `src/SpaceStroker.Domain/Configs/Entities/GameSettings.cs`
- Modify: `src/SpaceStroker.Common/spacestrokersettings.json`
- Test: `src/SpaceStroker.Tests/UnitTests/Services/Telegram/TelegramSettingsTests.cs`

**Interfaces:**
- Produces: `OrbitSettings.TelegramBotToken`, `.TelegramBotUsername`, `.TelegramLinkCodeTtlMinutes`,
  `.IsTelegramConfigured` (bool); `GameSettings.OrbitNotificationEndpoint` (string).

- [ ] **Step 1: Write the failing test** — `IsTelegramConfigured` is false for blank token, blank
  username, and a `MOCK_`-prefixed token; true only when both are real.
- [ ] **Step 2:** `scripts/run_tests.bat` → FAIL (property does not exist).
- [ ] **Step 3:** add the three properties + `IsTelegramConfigured` to `OrbitSettings`, the endpoint to
  `GameSettings`, and the mock values + `"OrbitNotificationEndpoint": "OrbitNotification"` to
  `spacestrokersettings.json`.
- [ ] **Step 4:** tests pass.

## Task 2: Group name + cache keys

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitNotificationChannel.cs`
- Modify: `src/SpaceStroker.Infrastructure/Services/Cache/CacheHelper.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Services/Orbit/OrbitNotificationChannelTests.cs`

**Interfaces:**
- Produces: `OrbitNotificationChannel.GroupFor(Guid playerId) => $"orbit/{playerId}"`;
  `CacheHelper.GenerateTelegramLinkCodeCacheSettings(string code, int ttlMinutes)`;
  `CacheHelper.GenerateTelegramUpdateOffsetCacheSettings()` (24h).

- [ ] **Step 1:** test that the group name is stable and player-scoped, and the cache keys are
  namespaced (`Telegram:LinkCode:{code}`, `Telegram:UpdateOffset`).
- [ ] **Step 2:** run → FAIL. **Step 3:** implement. **Step 4:** pass.

## Task 3: `ITelegramNotifier`

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Services/Telegram/{TelegramOptions,ITelegramNotifier,TelegramNotifier}.cs`
- Modify: `src/SpaceStroker.Infrastructure/DependencyInjection.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Services/Telegram/TelegramNotifierTests.cs`

**Interfaces:**
- Produces: `bool IsConfigured`, `string? BuildStartLink(string code)`,
  `Task<bool> SendMessage(string chatId, string text, CancellationToken)`,
  `Task<string?> FindChatIdForCode(string code, CancellationToken)`.
- Consumes: `OrbitSettings` (Task 1), `ICacheService` + `CacheHelper` (Task 2).

- [ ] **Step 1: failing tests** — unconfigured ⇒ no HTTP attempted, `SendMessage` false,
  `FindChatIdForCode` null, `BuildStartLink` null; configured ⇒ `sendMessage` POSTed; non-200/timeout ⇒
  false + no throw; `getUpdates` with a matching `"/start CODE"` ⇒ chat id returned **and** the offset
  written as `update_id + 1`; no match ⇒ null **and the offset untouched**; malformed body ⇒ null.
  Use a stub `HttpMessageHandler` through `IHttpClientFactory` (mirror `RecordingRpcHandler`).
- [ ] **Step 2:** run → FAIL. **Step 3:** implement + register (`AddScoped<ITelegramNotifier, TelegramNotifier>()`
  and `AddHttpClient(TelegramOptions.HttpClientName, c => c.BaseAddress = new Uri("https://api.telegram.org/"))`).
  **Step 4:** pass. Never log the token or the request URI.

## Task 4: The hub channel

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Hubs/ActiveGamesHub.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Hubs/ActiveGamesHubTests.cs`

**Interfaces:**
- Produces: `Task JoinOrbitNotifications(string sessionToken)`, `Task LeaveOrbitNotifications()`,
  `const string OrbitGroupItemKey`.
- Consumes: `IOrbitSessionService.ValidateAndGetWallet` (shipped), `OrbitNotificationChannel` (Task 2).

- [ ] **Step 1: failing tests** — valid token ⇒ `Groups.AddToGroupAsync(connectionId, "orbit/{playerId}")`
  called once and `Context.Items` records it; null/expired/forged token ⇒ **no** group add, no throw;
  a wallet with no `Player` row ⇒ no group add; `Leave` after a join ⇒ one remove; `Leave` without a
  join ⇒ no-op. Follow the existing `ActiveGamesHubTests` mocking style (`Mock<IGroupManager>`,
  `Mock<HubCallerContext>`).
- [ ] **Step 2:** run → FAIL. **Step 3:** implement (constructor gains `IOrbitSessionService` +
  `SpaceStrokerDbContext`). **Step 4:** pass.

## Task 5: Publish the win

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Consumers/OrbitNotification/OrbitWinNotificationEvent.cs`
- Modify: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitBetSettlementService.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Services/Orbit/OrbitBetSettlementServiceTests.cs`

**Interfaces:**
- Produces: `record OrbitWinNotificationEvent(Guid PlayerId, decimal Amount, string GameTypeName, Guid GameId)`.
- Consumes: `IMessageBus.PublishEventAsync<T>` (shipped).

- [ ] **Step 1: failing tests** — a successful `CreditWin` publishes exactly one event carrying the
  player, the net amount and the game type name; a failed settle publishes **nothing**; a publish that
  throws does **not** fail `CreditWin` (still `IsSuccess`); `RefundToOrbit` publishes nothing.
- [ ] **Step 2:** run → FAIL. **Step 3:** implement (ctor gains `IMessageBus`; publish after
  `EvaluateThreshold`, wrapped in try/catch + `Log.Error`). **Step 4:** pass.

## Task 6: Deliver the win

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Consumers/OrbitNotification/OrbitNotificationConsumer.cs`
- Modify: `src/SpaceStroker.Consumers/Program.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Consumers/OrbitNotificationConsumerTests.cs`

**Interfaces:**
- Consumes: `OrbitWinNotificationEvent` (Task 5), `OrbitNotificationChannel` (Task 2),
  `GameSettings.OrbitNotificationEndpoint` (Task 1).

- [ ] **Step 1: failing test** — the consumer sends on group `orbit/{playerId}` with event name
  `OrbitNotification`, and the JSON payload contains `amount`, `gameTypeName`, `tokenLabel`, `gameId`
  and **no** `balance`. Mock `IHubContext<ActiveGamesHub>` the way `BetNotificationConsumer`'s tests do.
- [ ] **Step 2:** run → FAIL. **Step 3:** implement + register the consumer and its receive endpoint.
  **Step 4:** pass.

## Task 7: Telegram on cash-out completion

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Consumers/OrbitCashOut/OrbitCashOutConsumer.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Consumers/OrbitCashOutConsumerTests.cs`

**Interfaces:** consumes `ITelegramNotifier` (Task 3).

- [ ] **Step 1: failing tests** — a completed cash-out for a **linked** player sends exactly one message
  containing the amount, for **each** of `Manual`, `Threshold`, `ForcedOnDisable`,
  `ForcedOnSelfExclusion`; an **unlinked** player ⇒ no send; unconfigured notifier ⇒ no send; a send that
  throws ⇒ the row still ends `Completed`; `FailAndRestore` ⇒ **no** send.
- [ ] **Step 2:** run → FAIL. **Step 3:** implement `NotifyCashOut` after the `Completed` update, whole
  body wrapped. **Step 4:** pass.

## Task 8: The three endpoints

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Handlers/Orbit/StartTelegramLink/{StartTelegramLinkRequest,StartTelegramLinkHandler}.cs`
- Create: `src/SpaceStroker.Infrastructure/Handlers/Orbit/VerifyTelegramLink/{VerifyTelegramLinkRequest,VerifyTelegramLinkHandler}.cs`
- Create: `src/SpaceStroker.Infrastructure/Handlers/Orbit/UnlinkTelegram/{UnlinkTelegramRequest,UnlinkTelegramHandler}.cs`
- Modify: `src/SpaceStroker.Api/Controllers/OrbitController.cs`,
  `src/SpaceStroker.Common/Resources/ErrorMessages.resx` (+ `.Designer.cs`),
  `src/SpaceStroker.Common/spacestrokersettings.json`,
  `src/SpaceStroker.Tests/UnitTests/Api/RateLimitSettingsTests.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/Orbit/{StartTelegramLink,VerifyTelegramLink,UnlinkTelegram}HandlerTests.cs`

**Interfaces:**
- Produces: `POST /orbit/telegram/link` → `TelegramLinkDto { Code, DeepLink, ExpiresAt }`;
  `POST /orbit/telegram/verify` (body `{ code }`) → `TelegramLinkStateDto { Linked }`;
  `DELETE /orbit/telegram` → `TelegramLinkStateDto { Linked = false }`.

- [ ] **Step 1: failing tests** —
  - *Start:* unconfigured ⇒ `OrbitUnavailable`, nothing cached; configured ⇒ an 8-char code from the
    unambiguous alphabet is cached against the player and the deep link is returned; a player with **no**
    enrolment ⇒ a disabled enrolment is created.
  - *Verify:* unknown code, another player's code, and a consumed code ⇒ **`OrbitRequestInvalid`** (all
    identical) and nothing stored; a code whose `/start` has not arrived ⇒ **`OrbitTelegramNotFound`**
    (clear) and nothing stored; a match ⇒ `TelegramChatId` stored and the code removed.
  - *Unlink:* clears the column; is idempotent when nothing is linked.
- [ ] **Step 2:** run → FAIL. **Step 3:** implement handlers + controller routes (each behind
  `RejectWithoutSession`, all added to `[HttpOptions]`), add `OrbitTelegramNotFound` to the resx +
  Designer, add the 3 rate-limit rules, and update `RateLimitSettingsTests`' expected set +
  `[InlineData]` rows **without** touching the bet-path guard. **Step 4:** pass.

## Task 9: `OrbitDto` Telegram flags

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Orbit/GetOrbit/GetOrbitRequest.cs`,
  `.../GetOrbitHandler.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/Orbit/GetOrbitHandlerTests.cs`

- [ ] **Step 1: failing tests** — `TelegramEnabled` mirrors `ITelegramNotifier.IsConfigured`;
  `TelegramLinked` is true only when the enrolment holds a chat id; **the chat id itself never appears
  on the DTO**.
- [ ] **Step 2:** run → FAIL. **Step 3:** implement. **Step 4:** pass.

## Task 10: Integration tests (MANDATORY — Level: Feature)

**Files:**
- Create: `src/SpaceStroker.Tests.Integration/OrbitNotificationsFlowTests.cs`
- Modify: `src/SpaceStroker.Tests.Integration/Harness/TestDoubles.cs` (add `FakeTelegramNotifier`),
  `Harness/IntegrationTestFixture.cs` (register it on both hosts, expose the Consumers instance)

- [ ] **Step 1:** write the three tests — (a) an Orbit-funded win delivers one message on the winner's
  channel carrying amount + game type and no balance; (b) a second connection joined as a **different**
  player receives nothing; (c) a completed cash-out records exactly one Telegram send with the amount,
  and with the fake reporting `IsConfigured == false` records **zero** while the cash-out still completes.
  Drive SignalR the way `HubBetNotificationTests` does.
- [ ] **Step 2:** `scripts/run_integration_tests.bat` → FAIL.
- [ ] **Step 3:** make them pass (harness wiring only — the production code is Tasks 1-9).
- [ ] **Step 4:** whole integration suite green.

## Task 11: Frontend types + service

**Files:** `src/types/orbit.ts`, `src/services/orbitService.ts`

- [ ] Add `telegramEnabled` / `telegramLinked` to `OrbitState`, plus `OrbitTelegramLink`,
  `OrbitTelegramState`, `OrbitWinNotification`. Add `startTelegramLink`, `verifyTelegramLink(code)`,
  `unlinkTelegram` in the existing `withSession(...)` style. Verify with `yarn lint`.

## Task 12: The Orbit channel in `useGameHub`

**Files:** `src/hooks/useGameHub.ts`

- [ ] Add module-level `orbitSessionToken` + `orbitHandler`; `joinOrbitChannel(token, handler)` →
  `ensureConnected()` → `invoke('JoinOrbitNotifications', token)` → bind `OrbitNotification`;
  `leaveOrbitChannel()` → unbind + `invoke('LeaveOrbitNotifications')` + clear; re-join inside
  `onreconnected` beside the existing `JoinGame`; clear both in `stopForIdle`. Export both.

## Task 13: `useOrbitNotifications`

**Files:** Create `src/hooks/useOrbitNotifications.ts`; modify `src/components/GameHeader.vue`

- [ ] Module singleton. Watches `hasSession && orbit.enabled && !isEmbedded` → join/leave. On a message:
  `toast.info` with amount + token label + game type, then `refreshQuietly(wallet)`. Leaves on wallet
  change. Mounted once in `GameHeader`.

## Task 14: `useOrbit` — Telegram actions + paused state

**Files:** `src/hooks/useOrbit.ts`

- [ ] Add `startTelegramLink`, `verifyTelegramLink`, `unlinkTelegram` (session-only, `isBusy`/`lastError`
  like the others); `isPausedForFunds` computed (`enabled && gameParametersId && balance <` that type's
  `requiredAmount`); `notePauseEpisode()` / pause-flag helpers over
  `space-stroker.orbit.paused-notified.{wallet}`, cleared when the pause ends.

## Task 15: `OrbitModal` — Telegram section + paused row

**Files:** `src/components/orbit/OrbitModal.vue`

- [ ] Telegram block rendered only when `telegramEnabled`: unlinked ⇒ LINK TELEGRAM → code + open-in-
  Telegram anchor + VERIFY (inline retry copy on `OrbitTelegramNotFound`); linked ⇒ confirmation +
  UNLINK. Paused row above the deposit ladder when `isPausedForFunds`, naming the reason. Existing
  system-tier chrome; no new overlay.

## Task 16: The first-win upsell

**Files:** Create `src/hooks/useFirstWinUpsell.ts`, `src/components/orbit/OrbitFirstWinModal.vue`;
modify `src/hooks/useGame.ts`, `src/components/GameHeader.vue`

- [ ] `useFirstWinUpsell`: module singleton, `localStorage` key `space-stroker.orbit.first-win-seen`
  (whole-browser, not per wallet), `maybeOpen()` refusing silently on: already seen, `isEmbedded`,
  `orbit.enabled`, `openTour !== null`, `recovering`/`isIdleDisconnected`/`relaunching`/`!termsAccepted`.
  Every dismissal marks it seen.
- [ ] `useGame.ts`: module-level `ownBetWasPaid`, set true in the paid bet path (false in the free path),
  reset with `ownBetChoices`; inside the existing `GameStatus.Ended` one-shot call
  `maybeOpen()` when `didBet && won && ownBetWasPaid`.
- [ ] `OrbitFirstWinModal.vue`: tutorial tier (near-black + 2px CRT-cyan frame), `edson.png` on top,
  `<Teleport to="#app">`, dithered backdrop, no `backdrop-filter`/blur/infinite animation, X/Esc/backdrop/
  primary all dismiss; primary closes and opens `OrbitModal`. Mounted in `GameHeader`.

## Task 17: Deploy plumbing (`monkify-meta`)

**Files:** `deploy/terraform/workload/main.tf`, `deploy/tools/SpaceStroker.Deploy/EnvNaming.cs`
(+ `SpaceStroker.Deploy.Tests/EnvNamingTests.cs` if it pins the id list)

- [ ] Add the three Orbit secret ids to `base_secret_ids` and their env-var mappings to
  `base_secret_env`; add the same ids to `EnvNaming.DerivedIds()`. Keep `TelegramBotUsername` out — it
  is plain config, not a secret.

## Task 18: Spec sync + verification

- [ ] Update `specs/orbit-notifications.md` for the constant event name (§5.2, §6.3, §7.1, §7.3, §10,
  §15) and record any other deviation in §16.
- [ ] `scripts/run_tests.bat` and `scripts/run_integration_tests.bat` both green; `yarn lint` +
  `yarn build` green in the front worktree.
- [ ] Commit + push all three repos, build the GitHub compare links, then deploy to Docker.

# Feature — Orbit notifications, Telegram and the first-win upsell (3/3)

> Status: **Shipped — on `main` since 2026-08-03.** Authoritative spec:
> `../specs/orbit-notifications.md` (merged via `feature/orbit-notifications` into `release/orbit`
> on 2026-08-03, and onto `main` with the whole train the same day).
>
> The last of Orbit's three cards: it completed `release/orbit`, which then landed on `main` as one
> merge. Trello [#123](https://trello.com/c/NIH7XFHi), umbrella
> [#118](https://trello.com/c/yxwdNTrb). Builds on
> [`orbit-custodial-wallet.md`](orbit-custodial-wallet.md) (#121) and
> [`orbit-auto-bet-engine.md`](orbit-auto-bet-engine.md) (#122).
>
> ⚠️ **Dark in production** since #146 (2026-08-04): with `VUE_APP_ORBIT_ENABLED` off,
> `useOrbitNotifications.start()` refuses, so the hub channel is never joined, neither the win toast
> nor the "paused — insufficient funds" toast can fire, and the first-win upsell is refused inside
> `maybeOpen()`. The **server** still publishes; nobody is listening. ⚠️ Because
> `joinOrbitChannel` is the only thing that sets `useGameHub`'s `orbitSessionToken`, the
> `onreconnected` re-join stays clean too — `useGameHub.ts` needed **no** flag of its own. See
> [`orbit-custodial-wallet.md`](orbit-custodial-wallet.md) → "The production gate".

## What it does

Closes the loop the engine left open. Orbit bets and settles with nobody watching, and until this
card nothing told the player — a win only showed up if they opened the modal, and money leaving
custody, including the forced cash-outs they never asked for, was silent.

Four deliverables:

1. **A private per-player realtime channel** on the existing `ActiveGamesHub` that announces every
   Orbit win, wherever the player is in the app.
2. **Telegram messages when money leaves custody** — every cash-out, linked with no poller and no
   webhook.
3. **An honest "paused" state** when Orbit stops betting for lack of funds.
4. **A one-time invitation into Orbit** for a first-time winner.

It also added a **cash-out floor** (§ below) and fixed two engine defects from #122 that only a
running stack could surface.

## Core rules

**The win channel**

- The group is joined with the **Orbit session token** (`ActiveGamesHub.JoinOrbitNotifications`), and
  its name is derived **server-side** from the token's `PlayerId` — never accepted from the caller.
  Joining by wallet was rejected outright: a wallet address is public on every bet list, so a
  wallet-keyed Orbit group would let anyone subscribe to a stranger's win amounts and, worse, learn
  that they use Orbit at all. Orbit's invisibility to other players is the umbrella's rule, and a
  channel is the easiest place to break it.
- A bad, expired or forged token joins **nothing** and says nothing (`constraints.md` §6). There is
  no error to report: the client cannot act on it, and a distinguishable refusal is a probe.
- **One indexed `Players.ExternalPlayerId` read per join, never per message.** The publisher only
  ever holds a `PlayerId`, so grouping on the wallet would move that lookup onto the hot path.
- `OrbitNotificationChannel.GroupFor(playerId)` is the **single** definition of the group name, used
  by both the hub and the consumer. Two hand-written strings that drift produce a notification that
  is never delivered and never errors — the hardest failure here to notice, because everything looks
  healthy.
- The payload carries **amount, game type name, token label and game id — and deliberately no
  balance.** A threshold cash-out fires the instant the credit lands, so a balance captured at
  publish time can already be zero when the toast renders. The client toasts, then re-reads the
  authoritative balance with `refreshQuietly`.
- It fires **even on the winning round's own page**: the terminal reveal says the player won, it does
  not say the money went into Orbit rather than their wallet.
- The event name is the **constant** `OrbitNotification` (`GameSettings.OrbitNotificationEndpoint`),
  not a `{id}/…` format. The per-player group already scopes delivery, so there is nothing to
  disambiguate — the same reasoning as the shipped `ActiveGamesEndpoint`. Consequence: `OrbitDto`
  carries no event-name field and the client binds the constant.
- Publishing happens **after** `Settle` commits and **after** `EvaluateThreshold`, so the win toast
  and the Telegram cash-out message arrive in the order the money actually moved.
- A notification that fails to publish is logged and swallowed — it must never fail a settlement that
  already moved money.

**Who gets a channel, and when they lose it**

- Only a player whose Orbit session is **already stored** joins. Minting one costs a wallet
  signature, and nobody is asked to sign a message merely because they loaded a page — so an enabled
  player on a fresh browser gets notifications after they press `SIGN IN`, which they must do anyway
  to read the balance. The alternative — asking the server "is this wallet enrolled?" unauthenticated
  — re-opens the membership leak the session gate just closed.
- **The idle disconnect wins.** `stopForIdle()` still kills the tab's socket after 30 min hidden /
  60 min inactive and the channel dies with it. Exempting Orbit players would pin a Cloud Run request
  per idle tab — exactly the cost `unified-websocket-connection` exists to remove. Missed wins are
  still in the modal and Games History on return.
- `onreconnected` **re-joins** the Orbit group beside the existing `JoinGame` re-join. Without it the
  channel dies silently on the first network blip.
- **Nothing at all happens when embedded** — no join, no toast, no Telegram UI, no upsell.

**Telegram — cash-outs, and (opt-in) zero-outs and wins**

> ⚠️ **Amended 2026-08-06** ([#150](orbit-feedback-adjustments.md)) — Telegram is no longer cash-outs
> only. Three independent per-player toggles now exist: **Cashout (on)**, **Zero out (on)** and
> **Any bet's wins (off)**. The win toggle **deliberately reverses this card's decision** not to send
> Telegram for wins; being **opt-in and off by default** is what makes the reversal safe, and an
> always-on player would otherwise get dozens of messages a day. "Zero out" means the balance can no
> longer cover the type's stake — it needed a **server-side** `OrbitEnrollment.ZeroOutNotifiedAt`,
> because the out-of-funds state below is derived client-side and de-duplicated in `localStorage`,
> which a sender in Consumers cannot read. **The toggles govern Telegram only** — the in-app toasts
> below are unchanged.

- **All four cash-out sources send**: `Manual`, `Threshold`, `ForcedOnDisable` and
  `ForcedOnSelfExclusion`. The two `Forced*` values did not exist when the umbrella said "manual and
  threshold", and a forced cash-out is precisely the movement the player did not initiate and most
  needs to see land.
  > ⚠️ **`ForcedOnDisable` has no writer since 2026-08-07**
  > ([orbit-single-control](orbit-single-control.md)) — disabling no longer cashes out, so that
  > source can no longer fire. The rule stands for the sources that remain (incl. `Floor`).
- Sent from `OrbitCashOutConsumer` **once the row reaches `Completed`** with an on-chain signature.
  **A failed or reversed transfer sends nothing** — an ops event the player cannot act on, with the
  balance already restored.
- **Telegram is never allowed to fail a money path.** Any transport failure, timeout, non-200 or
  malformed body is logged and returns false; the cash-out is already done.
- **Linking needs no poller and no webhook**: a `t.me/<bot>?start=<code>` deep link, then a VERIFY
  press that makes **one** `getUpdates` call. The link code and the read offset both live in Redis
  via the shipped `ICacheService`; the code is short-lived (10 min), single-use, and scoped to the
  player.
- ⚠️ **The offset advances only on a match.** Advancing on every read would consume another player's
  pending `/start` before they pressed VERIFY. The accepted cost: unmatched updates stay unconfirmed
  and are re-served next call, bounded by `limit=100` and Telegram's 24h retention. A Redis flush
  costs one more VERIFY press.
- The link code is 8 chars from an unambiguous alphabet (`ABCDEFGHJKLMNPQRSTUVWXYZ23456789`) — it is
  typed by a human into a chat, so no `0/O`, no `1/I`.
- **Telegram is inert without a bot token.** `IsTelegramConfigured` requires a token *and* a bot
  username, and treats a `MOCK_`-prefixed token as **not configured** — mirroring how
  `OrbitAccountResolver` handles `MOCK_ORBIT_PUBLIC_KEY`. Committed config must fail **closed** into
  "inert", never into a live bot. `GET /orbit` exposes `telegramEnabled` so the client **hides** the
  whole section rather than rendering a button that always fails.
- **The `chat_id` is never returned to the client** — only `telegramLinked`. A chat id in a JSON body
  is one screenshot from being public. The bot token is never logged, and the URL that contains it is
  treated as a secret (the `MaskRpcUrl` precedent).
- **Unlinking is session-only** (it moves no money, so it follows the game-type/threshold rule, not
  the cash-out rule) and is idempotent. Without it a linked chat is permanent — a changed phone or
  Telegram account would keep receiving that player's cash-out amounts.
- **Vague vs clear, deliberately split.** A forged session, an unknown code and another player's code
  are **indistinguishable** (`constraints.md` §6). The two *ordinary-state* refusals — "we haven't
  seen your `/start` yet" and "Telegram is not linked" — are **clear**, following #121's rule that a
  vague message about ordinary state just makes the player retry blindly.

**The paused state**

- Derived **client-side** from `enabled && balance < the selected type's requiredAmount`, both
  already in `GET /orbit`. Pushing it from the planner would need a new per-player "already notified"
  flag on the enrolment, or it would fire every single round.
- ⚠️ It additionally requires **`orbitBetsPlaced > 0`** — a freshly enabled Orbit has not started, so
  calling it "paused" on the first enable is both wrong and alarming. That player gets an invitation
  to fund it (`needsFirstDeposit`) instead. Deliberately **not** "balance > 0": a real pause very
  often lands on exactly 0, which that test would miss.
- One toast per **pause episode**, tracked per wallet in `localStorage` and cleared the moment the
  balance covers the stake again, so a later pause notifies again.

**The first-win upsell**

- Fires on a **paid own-bet win only**. A free-bet win cannot fund a custodial USDC balance, so "put
  your winnings into Orbit" would be false on a free round. `ownBetWasPaid` is set where the bet is
  placed and reset with `ownBetChoices` on rollover.
- The trigger lives inside the existing `GameStatus.Ended` one-shot in `useGame.ts` that already
  computes `didBet` and `won` — no second watcher, no new round detection.
- Guards, cheapest first: already seen in this browser → embedded → Orbit already enabled (nothing to
  sell) → another overlay is open (never two modals — the header-owned **Settings** or **Provably
  Fair** modal; this guarded the Quick Tour until that was traded for the `/how-to-play` page, which
  is a route and cannot be open over anything) → `recovering` / `isIdleDisconnected` /
  `relaunching` / terms not accepted. **Every refusal is silent.**
- `localStorage`-only, **one key per browser, not per wallet** — it is a once-ever onboarding modal,
  and keying it per wallet would re-show it to the same human on their second address. The accepted
  cost is that it reappears on a new browser or after a cache clear. Persisting it server-side would
  mean either asking someone to sign a Solana message to dismiss a modal, or an unsigned write keyed
  on a public wallet address that would let anybody suppress a stranger's modal.
- **Tutorial tier** chrome (`conventions/frontend.md` → "Two overlay tiers"), variant B (centred, art
  on top). **Every** exit — X, Esc, backdrop, primary CTA — marks it seen, so nobody is nagged for
  closing it "the wrong way". The primary CTA opens the existing `OrbitModal` enable flow.

**The cash-out floor (added during implementation, from live testing)**

- **The problem, reported from the running stack:** a player set "cash out at" to 3, watched Orbit
  lose four rounds (balance 5 → 1) and was never cashed out. Nothing was broken — `EvaluateThreshold`
  runs only after a **win** credit and fires at `balance >= threshold`, so a balance *falling
  through* the number cannot trigger it. That is a take-profit; the player was asking for a floor.
- Two settings now answer **opposite questions**, rather than changing the meaning of the first:

  | Setting | Fires when | Evaluated |
  | --- | --- | --- |
  | `CashOutAtThreshold` | a **win** lifts the balance to ≥ it | after each `WinCredit` |
  | `CashOutFloor` | the balance falls to ≤ it | once the round **settles** |

- ⚠️ **Both act on a RESOLVED bet, never on a placed one.** The floor first shipped evaluating at
  placement and on save, which was wrong in a way only the running stack shows: a stake leaves the
  balance the moment it is placed, so the floor could send a player's money home **while the bet they
  had just made was still riding**. A live bet is not a loss yet. `RewardGameConsumer` now evaluates
  it once the round is settled, for every distinct `FundedByOrbit` bettor — and neither the placement
  path nor the settings save touches it.
- It gets its own `OrbitCashOutSource.Floor` rather than reusing `Threshold`: a ledger that cannot
  tell "I won enough" from "I have lost enough" cannot explain to a player why their money came back.
  It is exempt from the daily cap for free, since only `Manual` counts toward it.
- Guards: the floor must be ≥ `MinimumCashOut` (below that the reserver could only ever refuse), and
  a balance of 0 reserves nothing — a zero cash-out row would claim money moved when none did.

**Two #122 engine defects fixed here** (the feature was untestable without them)

- **A round could never pick Orbit up after the player enrolled mid-round.** `BetPlacedConsumer`
  re-planned only on a `null` gate, but a round that opens with nobody eligible is stamped `0`, and
  that stamp then locked Orbit out for the rest of the round. So the most ordinary sequence there is
  — open a round, enable Orbit, deposit, watch humans fill it — left Orbit sitting out a round it was
  funded and eligible for. The guard is now **`null` or `0`**; a **positive** count still blocks,
  which is what it was really written for (placements in flight behind an armed gate).
- **A cold boot lost its first `GameOpenedEvent`s.** `OpenNewGames` ran *before* `app.Run()`, while
  the transport database and its `transport` schema are provisioned by a hosted service that only
  starts **at** `app.Run()` — so the first rounds opened with no Orbit planning at all. The
  `SpaceStroker.Consumers` startup work now runs from **`ApplicationStarted`**, after every hosted
  service has started. Silent on every warm boot, which is how it survived: it only bites a fresh
  volume and a brand-new environment's first start.

**Other behaviour corrected here**

- **Orbit error messages reach the player.** `error.message` on an AxiosError is the status line, so
  purpose-written copy ("already cashed out today", "we haven't seen your message yet") was arriving
  as "Request failed with status code 400". `useOrbit` now surfaces the server's own message.
- **Framework logs are pinned to Warning** (`Microsoft`, `System`, `MassTransit`, `Hangfire`), by
  request. The API's whole log on a cold boot went from ~1000 lines per 30 min to 3. Real framework
  problems still surface — which is exactly how the cold-boot defect above became visible at all.
- **Native `<select>` options are readable.** A native select's popup list is painted by the browser,
  so `bg-transparent` left every `<option>` white-on-white; they carry their own colours now, plus
  `color-scheme: dark`.

**Deploy plumbing (absorbed from #121)**

- All **three** Orbit secret ids now reach `deploy/terraform/workload/main.tf` (`base_secret_ids` +
  `base_secret_env`) **and** `deploy/secrets.example.json`: the Orbit `PrivateKey`, the
  `SessionSecret` (both #121's, previously unplumbed) and this card's `TelegramBotToken`. Without
  them the train cannot deploy to a real environment.
- ⚠️ **Corrected during implementation:** `EnvNaming.DerivedIds()` needed **no** change — it
  enumerates the operator's own `secrets.<env>.json` keys, so it already validates whatever is
  configured. The card and the spec both said otherwise. What was genuinely missing was the
  hardcoded Terraform list and the example secrets file, which had **no Orbit entries at all** — an
  operator copying that template would boot Orbit against the committed **devnet** keypair instead of
  failing closed.
- `TelegramBotUsername` is **not** a secret; it ships in config, and configuring it avoids a `getMe`
  round-trip on every link request.

## Known gaps (shipped knowingly)

- **Telegram is exercised against a fake** in the integration suite — nothing in CI may call the real
  API (`constraints.md` §5). The live flow was verified by hand against a real bot: link → `/start` →
  VERIFY → chat id stored, code consumed, offset advanced past the matched update only.
- The three new public endpoints are rate-limited but **not** checked against Cloud Armor. Their
  bodies carry no base58, so the risk is low, but it is unverified
  (`cloud-armor-waf-blocks-crypto-payloads`).
- ⚠️ The **cold-boot startup reordering touches `SpaceStroker.Consumers` startup** more broadly than
  this card's scope, and was flagged at ship for a second pair of eyes.
- The **`GameOpenedEvent` publish is still best-effort**: a bus failure at any other moment loses that
  round's Orbit planning silently. The `null or 0` re-plan guard covers it on the next human bet, but
  nothing retries the publish itself. Candidate follow-up card.
- **No delivery when the tab is closed** or after the idle disconnect — `unified-websocket-connection`'s
  socket policy is left exactly as shipped.
- `useOrbitRedirect` **keeps its 2s poll.** Its own comment rejected a per-wallet channel as too
  expensive for a once-a-round hint, and that reasoning is now stale — but it is freshly merged #122
  code with the same session precondition a push would have, so replacing it buys latency, not
  correctness. Up to ~22 cheap reads per round end, only for an enabled player watching a *different*
  game type. Candidate follow-up card.
- ⚠️ Committed config still carries a real **devnet** keypair (inherited from #121/#122, deliberate
  and user-authorised so the local stack works out of the box). **Production must override it.**
- No browser/OS-level **Web Push**, no Telegram for **wins** (an always-on Orbit player would get
  dozens of messages a day), no email/SMS, no broadcast channel.

## Key code touchpoints

**`monkify-backend`**

- Hub — `Infrastructure/Hubs/ActiveGamesHub.cs` (`JoinOrbitNotifications` / `LeaveOrbitNotifications`;
  gains `IOrbitSessionService` + `SpaceStrokerDbContext`).
- Channel name — `Infrastructure/Services/Orbit/OrbitNotificationChannel.cs` (the one definition).
- Event + consumer — `Infrastructure/Consumers/OrbitNotification/{OrbitWinNotificationEvent,
  OrbitNotificationConsumer}.cs`. ⚠️ **Ids and primitives only** on the record — #122 shipped
  `GameOpenedEvent` because an event carrying a `GameParameters` entity throws in MassTransit's
  serializer on the navigation cycle.
- Telegram — `Infrastructure/Services/Telegram/{ITelegramNotifier,TelegramNotifier,TelegramOptions}.cs`,
  a **named `HttpClient`** registered in both hosts. ⚠️ **The request path must be rooted (`/bot…`)**:
  a bot token contains a colon, so the unrooted string parses as an absolute URI whose *scheme* is the
  token, and `HttpClientFactory` throws before anything leaves the process. The unit tests passed
  through all of it — a bare `HttpClient` over a stub handler never validates the scheme, only the
  factory's pipeline does — so the stub now refuses non-`http(s)` and a test pins both verbs to an
  absolute URI.
- Handlers — `Infrastructure/Handlers/Orbit/Telegram/{StartTelegramLink,VerifyTelegramLink,
  UnlinkTelegram}Handler.cs`; `GetOrbit/GetOrbitHandler.cs` (`telegramEnabled`, `telegramLinked`,
  `cashOutFloor`); `UpdateOrbitSettings/` (the floor, with an explicit clear flag).
- Settlement — `Infrastructure/Services/Orbit/OrbitBetSettlementService.cs` (`PublishWinNotification`,
  `EvaluateFloor`); `Consumers/RewardGame/RewardGameConsumer.cs` (settle-time floor evaluation);
  `Consumers/OrbitCashOut/OrbitCashOutConsumer.cs` (the Telegram send); `Consumers/BetPlaced/
  BetPlacedConsumer.cs` (the `null or 0` re-plan guard).
- API — `Api/Controllers/OrbitController.cs`: `POST /orbit/telegram/link`,
  `POST /orbit/telegram/verify`, `DELETE /orbit/telegram` — all public, all session-gated, all in the
  `[HttpOptions]` CORS list, each with its own `AspNetCoreRateLimit` rule.
- Cache — `Infrastructure/Services/Cache/CacheHelper.cs`: `Telegram:LinkCode:{code}` (10 min,
  single-use) and `Telegram:UpdateOffset` (24h).
- Config — `OrbitSettings.{TelegramBotToken,TelegramBotUsername,TelegramLinkCodeTtlMinutes,
  IsTelegramConfigured}`; `GameSettings.OrbitNotificationEndpoint`.
- Migration — `AddOrbitCashOutFloor` (`OrbitEnrollment.CashOutFloor`, `decimal?` 18,9; additive and
  nullable, no backfill). ⚠️ The card and the spec both said "no migration"; live testing changed
  that.

**`monkify-front`**

- `src/hooks/useOrbitNotifications.ts` (new) — channel lifecycle, the win toast, the once-per-episode
  pause toast.
- `src/hooks/useFirstWinUpsell.ts` (new) + `src/components/orbit/OrbitFirstWinModal.vue` (new).
- `src/hooks/useGameHub.ts` — `joinOrbitChannel` / `leaveOrbitChannel`, the module-level token so a
  reconnect can re-join, cleared by `stopForIdle`.
- `src/hooks/useOrbit.ts` — Telegram link/verify/unlink, `isPausedForFunds`, `needsFirstDeposit`, the
  server's own error message.
- `src/components/orbit/OrbitModal.vue` — the Telegram section, the paused row, the cash-out floor
  field beside the renamed "cash out on a win at", readable native `<option>`s.
- `src/components/GameHeader.vue` — mounts the notifications hook and the upsell modal beside
  `OrbitModal` / `QuickTourModal`. `src/hooks/useGame.ts` — `ownBetWasPaid` + the trigger.
- `src/types/orbit.ts`, `src/services/orbitService.ts`.

**`monkify-meta`** — `deploy/terraform/workload/main.tf`, `deploy/secrets.example.json`.

**Tests** — `SpaceStroker.Tests/UnitTests/{Services/Telegram,Handlers/Orbit,Consumers,Hubs,Api}/`;
`SpaceStroker.Tests.Integration/OrbitNotificationsFlowTests.cs` (the mandatory happy path, channel
isolation between two players, a forged session joining nothing, and the Telegram send against a
fake). `RateLimitSettingsTests`' exact-set assertion was updated deliberately for the three new
endpoints — **its guard that the bet path never gains a rule is untouched**.

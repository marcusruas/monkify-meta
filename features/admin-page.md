# Feature — Admin portal

> Status: **shipped.** Authoritative spec: `../specs/operator-admin-page.md`
> (merged via `feature/operator-admin-page` on 2026-07-06).
>
> ⚠️ **Reworked by `features/remove-operators.md` (2026-07-30).** There is no operator any more, so the
> portal is no longer *self-service for tenants* — it is **the deployment's own admin surface**. The
> admin token is now validated against **`GeneralSettings:Operator:AdminToken:PublicKeyPem`** with **no
> `iss` and no contract check**, `IAdminContext` carries no `OperatorId`, no handler scopes by operator,
> and the Solana-token registration screens are gone (there is one token, and it comes from config).
> **`POST /dev/admin-token` is back** (non-production only), restoring the local workflow that
> `main-only-player-entry` had removed. *(Renamed from `operator-admin-page.md`.)*
>
> ⚠️ **Earlier access note, now historical:** the portal was briefly reachable only by presenting an
> offline-minted token at `/game#admin_token=<jwt>`. That fragment hand-off still works;
> the dev mint endpoint is simply available again outside production.

## What it does

A password-less admin portal at `/admin`, entered by presenting an **admin-scoped RS256
JWT**, where game types (game configurations) are **created**, **listed** and **soft-deactivated**
across all three modes (free-type race, preset-choice, lottery), and a single bet can be flagged for
refund. There is **no in-place edit** — to change a type, deactivate the old one and create a new row.

## Core rules

- **Auth:** an RS256 JWT with `scope = "admin"` and **no `sub`**, signed with the configured admin
  private key and validated by `AdminTokenValidationMiddleware` against the configured public key.
  Transport: `/admin#admin_token=<jwt>`; the SPA stores it per-tab in `sessionStorage`
  and sends **`X-Admin-Token`** on admin REST calls.
- **It is the only authenticated surface left.** Player endpoints (`/games/*`, `/players/*`) are
  public, so there is no launch/admin token separation rule any more — there is only one token.
  An auth failure on `/admin` → vague **HTTP 401** (reason logged server-side only).
- **No contract gate, no tenant scoping.** A valid signature grants access to every game type.
- **Operations:** **Create**, **List** (active types only), **Deactivate** (`Active = false`). **No
  edit** and **no hard delete** (historical `Game`/`Bet` rows FK to the type row).
- **Refund a bet:** `POST /admin/bets/refund { paymentSignature }` marks a single bet (located by its
  indexed `Bet.Signature`) as `NeedsRefunding`; the 12h pending-refund sweep does the actual refund.
  Blocks already-`Rewarded`/`Refunded` bets and free bets; an unknown signature returns a vague
  "No bet found for that signature." Frontend: a **"Refund a Bet"** tab. See
  `features/refund-safeguards.md`.
- **`AllowFreeBet` is never exposed** — admin-created types always have `AllowFreeBet = false`.
- **Flat `GameParameters`:** one row per type, created once and never mutated (config-wise). The only
  mutable field is `Active`. Roll-over reloads the same row and creates a successor only when
  `Active == true`.
- **Create → first game:** `CreateGameType` publishes `GameParametersCreatedEvent`; a Consumers
  consumer opens the first game so the type is enterable shortly after creation.
- **Deactivate while live:** the current game finishes normally; roll-over sees `Active == false` and
  creates **no** successor. The type drops from `GET /games/active-types` and the admin list
  immediately.
- **Deactivate while waiting:** `GameTypeDeactivatedEvent` cancels every `WaitingBets` game of that
  type — paid bets are refunded (`RefundInvalidBetEvent`), free bets skipped, game set to
  `NotEnoughPlayersToStart`. `GameStarting`/`InProgress` games are left to finish.
- **Deactivate a lottery holding an un-won pot:** if the lottery's current game has a stacked jackpot
  (`CarriedExtra > 0`), `POST /admin/game-types/{id}/deactivate` requires a `{ targetLotteryId }` body
  and **transfers** the pot into that target (any active lottery — the same-operator/same-method/
  same-token conditions are gone with those concepts). Omitting the target, or when no eligible target
  exists, **blocks** the deactivation (the pot is never destroyed). With `CarriedExtra == 0`
  deactivation is unchanged. `GET /admin/game-types` exposes each lottery's current `CarriedExtra` so
  the portal can prompt + filter eligible targets. See `features/lottery-pot-stacking.md`.
- **Validation:** `GameParametersValidation` enforces name/description, charset, choice length (1–20),
  min players (≥ 2), amount (> 0), preset-choice rules, and lottery bounds. Field-level validation
  errors are acceptable on authenticated admin endpoints (not the vague auth path).
- **Cache:** every create/deactivate invalidates the single global `ActiveParameters` key.
- **Dev entry:** `POST /dev/admin-token` (404 in production) mints a token for local use.

## Key code touchpoints

**Backend (`monkify-backend`)**

- Auth: `SpaceStroker.Api/Admin/AdminTokenService.cs` (issue + validate against the configured
  keypair); `AdminTokenValidationMiddleware` guards `/admin`.
- Identity: `IAdminContext` — present for the request pipeline's shape, carries no tenant.
- Controller: `AdminController` — `GET /admin/game-types`, `POST /admin/game-types`,
  `POST /admin/game-types/{id}/deactivate`, `POST /admin/bets/refund`.
- Handlers: `Handlers/Admin/{GetGameTypes,CreateGameType,DeactivateGameType,RefundBet}/`.
- Validation: `SpaceStroker.Domain/Games/Services/GameParametersValidation.cs`.
- DTOs: `Contracts/Admin/{GameTypeAdminDto,GameTypeRequestBody}.cs`.
- Consumers: `GameParametersCreatedConsumer`, `GameTypeDeactivatedConsumer`; `RunGameConsumer` uses
  `GetActiveParametersForRollover`; `BetPlacedConsumer` guards against post-cancel lottery resurrection.
- Dev: `DevController.MintAdminToken` — `POST /dev/admin-token`.
- Tests: `AdminGameTypeHandlerTests`, `AdminTokenServiceTests`, `GameParametersCreatedConsumerTests`,
  `GameTypeDeactivatedConsumerTests`, `GameParametersValidationTests`, `DevControllerTests`;
  integration `AdminPortalTests`.

**Frontend (`monkify-front`)**

- Route: `/admin` (`AdminPage.vue`) — the token is the access control.
- Token: `utils/adminToken.ts`, `hooks/useAdmin.ts` (fragment capture/strip/store).
- Service: `services/adminService.ts` — dedicated axios instance with `X-Admin-Token`; 401 → `/error`.
- UI: `components/admin/{GameTypeManager,GameTypeList,GameTypeForm}.vue` (create + list + deactivate
  only; no edit, no payment-method or token selectors).
- Types: `GameTypeAdmin`, `GameTypeBody` in `types/index.ts`.

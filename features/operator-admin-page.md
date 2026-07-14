# Feature — Operator admin portal

> Status: **shipped.** Authoritative spec: `../specs/operator-admin-page.md`
> (merged via `feature/operator-admin-page` on 2026-07-06).
>
> ⚠️ **Extended by `features/payment-methods.md` (2026-07-08).** The portal now also manages **Solana
> token registration** (`GET/POST /admin/operator-tokens`, `POST …/{id}/deactivate` — register/list/
> deactivate, no edit; private key encrypted at rest, never returned) and reports **method availability**
> (`GET /admin/payment-methods`). The session-type create form gained a **PaymentMethod** selector
> (`Standard`/`SolanaToken`, offered only when available) and, for SolanaToken, a **token** selector;
> `SessionTypeRequestBody`/`CreateSessionTypeHandler`/`SessionTypeAdminDto`/`SessionTypeList` carry
> `PaymentMethod` + `OperatorTokenId`. The dev admin button dropped its `?paymentModel` param.

## What it does

Gives operators (casinos) a **self-service admin portal** at `/monkify/admin` where they authenticate
with an operator-signed **admin-scoped JWT** and manage **their own** session types (game
configurations). Operators can **create**, **list**, and **soft-deactivate** types across all three
modes (free-type race, preset-choice, lottery). There is **no in-place edit** — to change a type,
deactivate the old one and create a new row. A dev-only header shortcut mints an admin token locally.

## Core rules

- **Auth:** operator signs an RS256 JWT with `scope = "admin"`, `iss = operatorId`, **no `sub`**,
  validated against the operator's existing `LaunchPublicKey`. Transport: redirect to
  `/monkify/admin#admin_token=<jwt>`; SPA stores per-tab in `sessionStorage` and sends
  **`X-Admin-Token`** on admin REST calls.
- **Token separation:** admin tokens are **rejected** on player endpoints (`/sessions`, `/operators`,
  `/players`, `/fx`); launch/player tokens are **rejected** on `/admin`. Any auth failure → vague
  **HTTP 401** (reason logged server-side only); SPA routes to `/error`.
- **Not contract-gated:** admin access works for operators whose contract is inactive (future start or
  expired) as long as the signature is valid and the operator exists. Player sessions remain
  contract-gated.
- **Scoping:** every admin handler uses `IAdminContext.OperatorId` from the token `iss`. An operator
  never sees or mutates another operator's types.
- **Operations:** **Create**, **List** (active types only), **Deactivate** (`Active = false`). **No
  edit** and **no hard delete** (historical `Session`/`Bet` rows FK to the type row).
- **`AllowFreeBet` is never exposed** — operator-created types always have `AllowFreeBet = false`.
  **`OperatorId` is forced from the token**, never client-settable.
- **Flat `SessionParameters`:** one row per type, created once and never mutated (config-wise). The
  only mutable field is `Active`. Roll-over reloads the same row and creates a successor only when
  `Active == true`.
- **Create → first session:** `CreateSessionType` publishes `SessionParametersCreatedEvent`; a
  Consumers consumer opens the first session so the type is enterable shortly after creation.
- **Deactivate while live:** the current session finishes normally; roll-over sees `Active == false`
  and creates **no** successor. The type drops from `GET /sessions/active-types` and the admin list
  immediately.
- **Deactivate while waiting:** `SessionTypeDeactivatedEvent` cancels every `WaitingBets` session of
  that type — paid bets are refunded (`RefundInvalidBetEvent`), free bets skipped, session set to
  `NotEnoughPlayersToStart`. `SessionStarting`/`InProgress` sessions are left to finish.
- **Deactivate a lottery holding an un-won pot:** if the lottery's current session has a stacked
  jackpot (`CarriedExtra > 0`), `POST /admin/session-types/{id}/deactivate` requires a
  `{ targetLotteryId }` body and **transfers** the pot into that target (same operator, active, a
  lottery, same `PaymentMethod`, and same `OperatorTokenId` for SolanaToken). Omitting the target, or
  when no eligible target exists, **blocks** the deactivation (the pot is never destroyed). With
  `CarriedExtra == 0` deactivation is unchanged. `GET /admin/session-types` exposes each lottery's
  current `CarriedExtra` so the portal can prompt + filter eligible targets. See
  `features/lottery-pot-stacking.md`.
- **Validation:** `SessionParametersValidation` enforces name/description, charset, choice length
  (1–20), min players (≥ 2), amount (> 0), preset-choice rules, and lottery bounds. Field-level
  validation errors are acceptable on authenticated admin endpoints (not the vague auth path).
- **Cache:** every create/deactivate invalidates `ActiveParameters:{operatorId}`.
- **Dev entry:** `DevAdminButton` in `GameHeader` (gated by `VUE_APP_DEV_LAUNCH`) offers
  Crypto/Regulated → `POST /dev/admin-token?paymentModel=…` (404 in prod) → `/monkify/admin`.

## Key code touchpoints

**Backend (`monkify-backend`)**

- Auth: `LaunchTokenService.IssueAdmin` + `Scope` on `LaunchTokenValidationResult`;
  `AdminTokenValidationMiddleware` (guards `/admin`); launch middleware rejects `scope == "admin"`.
- Identity: `IAdminContext` (`Monkify.Infrastructure/Services/Admin/`); `GetOperatorById` (no
  contract filter) on `IOperatorService`.
- Controller: `AdminController` — `GET /admin/session-types`, `POST /admin/session-types`,
  `POST /admin/session-types/{id}/deactivate`.
- Handlers: `Handlers/Admin/{GetOperatorSessionTypes,CreateSessionType,DeactivateSessionType}/`.
- Validation: `Monkify.Domain/Sessions/Services/SessionParametersValidation.cs`.
- DTOs: `Contracts/Admin/{SessionTypeAdminDto,SessionTypeRequestBody}.cs`.
- Consumers: `SessionParametersCreatedConsumer`, `SessionTypeDeactivatedConsumer`;
  `RunSessionConsumer` uses `GetActiveParametersForRollover`; `BetPlacedConsumer` guards against
  post-cancel lottery resurrection.
- Dev: `DevController.MintAdminToken` — `POST /dev/admin-token`.
- Tests: `AdminSessionTypeHandlerTests`, `SessionParametersCreatedConsumerTests`,
  `SessionTypeDeactivatedConsumerTests`, `SessionParametersValidationTests`, extended
  `LaunchTokenServiceTests` / `DevControllerTests` / `BetPlacedHandlerTests`.

**Frontend (`monkify-front`)**

- Route: `/monkify/admin` (`AdminPage.vue`) — not dev-gated; token is the access control.
- Token: `utils/adminToken.ts`, `hooks/useAdmin.ts` (fragment capture/strip/store).
- Service: `services/adminService.ts` — dedicated axios instance with `X-Admin-Token`; 401 → `/error`.
- UI: `components/admin/{SessionTypeManager,SessionTypeList,SessionTypeForm}.vue` (create + list +
  deactivate only; no edit).
- Dev: `components/header/DevAdminButton.vue` in `GameHeader.vue`.
- Types: `SessionTypeAdmin`, `SessionTypeBody` in `types/index.ts`.

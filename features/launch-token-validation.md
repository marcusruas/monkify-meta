# Feature — Launch token validation (signed casino launch flow)

> Status: **shipped.** Authoritative spec: `../specs/launch-token-validation.md`
> (merged via `feature/launch-token-validation` on 2026-06-30).

## What it does

Replaces the trusted **`x-operator-id`** header with a **cryptographically signed launch token**
(JWT, RS256) that the operator's casino signs and Monkify validates before the game loads. The token
proves which operator launched the game and which player it acts for. Every operator-scoped REST call
must now carry a valid `X-Launch-Token`; operator and player identity come from the token, not from
headers or request-body fields. Supersedes the `x-operator-id` model in `features/operators.md`.

## Core rules

- **Token required for every operator.** All operator-scoped REST endpoints (`/sessions/*`,
  `/operators/*`, `/players/*`) require an `X-Launch-Token` header. The `x-operator-id` header is gone;
  `operatorId` is the token's `iss`, the player id is its `sub`.
- **Asymmetric RS256.** The casino signs with its private key; Monkify validates against the operator's
  stored **`Operator.LaunchPublicKey`** (PEM). Optional `Operator.LaunchKeyId` supports rotation.
- **Claims:** `iss = operatorId`, `sub = externalPlayerId` (empty for crypto operators), `exp`, `iat`,
  `jti` (audit/log only). `displayName`/`currency` are **not** claims.
- **Validation** (stateless middleware in `Monkify.Api`): signature, `exp`/`iat` with **±2 min** clock
  skew, and — when the path carries an operatorId — `iss` must equal it. Replay protection is
  **expiry-only**. The operator must exist and be contract-active and have a `LaunchPublicKey`.
- **Vague failures.** Every failure (missing/expired/bad-signature/unknown-operator/iss-mismatch)
  returns a single generic **HTTP 401**; the real reason + a correlationId are logged **server-side
  only**. The SPA routes any 401 to `/error`. A token expiring mid-play takes the same path (re-launch).
- **SignalR unchanged.** Hubs are session-id scoped — no token on websockets. `/dev/*`, Swagger, and
  `OPTIONS` preflight are not token-guarded.
- **`Operator.PaymentModel` (`Regulated` | `Crypto`)** replaces `OperatorPlayer.IsCrypto` (the column
  is dropped). Crypto-ness is a property of the operator. It drives **wallet-skip**: crypto operators
  connect a wallet and pay on-chain (`paymentSignature` required); regulated operators settle off-chain
  (no wallet, no signature) and identify the player by the token `sub`. Existing operators were
  backfilled to **Crypto**; new operators must be provisioned with `PaymentModel` + `LaunchPublicKey`.
- **Register-bet identity from the token.** For crypto operators the wallet still comes from the body
  (post wallet-connect); for regulated operators the player id is the token `sub` and **body identity
  fields are ignored**. The operator/`PaymentModel` is read via the cached `IOperatorService`; an
  inactive/unknown operator → vague `SessionNotValidForBets` (the middleware already blocks this path).
- **Player preferences.** `PATCH /players/preferences` (token-gated) upserts the `OperatorPlayer`'s
  **display-only** `DisplayName` and preferred `Currency` (default `USD`). Identity: regulated via token
  `sub`; crypto via the wallet in the body. Currency only affects rendering — actual FX conversion is
  out of scope (tracked separately).
- **Dev mint endpoint.** `POST /dev/launch-token?paymentModel=Crypto|Regulated` (default `Crypto`) is
  **disabled in production**; in dev it resolves the seeded dev operator and returns `{ token,
  operatorId }`. Two dev operators are seeded: **DEV Cassino** (Crypto) and **DEV Regulated Cassino**
  (Regulated), both with a mock launch keypair; the private signing key is a mock placeholder in
  `monkifysettings.json`. Seeding is idempotent and self-heals operators that predate the feature.
- **Cached operator lookup.** `IOperatorService.GetActiveOperatorById` caches the active operator under
  the per-operator cache key, shared by the middleware (every request), the branding endpoint, and
  register-bet. Seeding invalidates that cache entry after create/patch.
- **Free bets** remain dev-gated (`AllowFreeBets = false` in prod); each free bet counts as an
  individual player even when they share an `OperatorPlayer` (see `features/free-bet-mode.md`).

## Frontend behaviour

- **Launch:** the casino redirects to `…/monkify/game/<operatorId>#launch_token=<jwt>`. `useLaunch`
  reads the token from the URL fragment, **strips it from the URL**, and persists it in
  **`sessionStorage`** (per-tab; survives reloads/hard navigation). A fresh fragment token overrides a
  stored one. In dev, if no token is present it mints one from `/dev/launch-token`.
- The axios interceptor (`mainService.ts`) reads the token lazily from the store and sends
  `X-Launch-Token` on every request; any `401` routes to `/error`.
- **Wallet UX is gated on `paymentModel`** (from `GET /operators/{id}`): wallet-connect + on-chain
  `sendToken` run only for `Crypto`; regulated operators post `{ amount, choice, seed }` with no wallet
  or signature.

## Key code touchpoints

**Backend (`monkify-backend`)**

- Entity/enum: `Operator.PaymentModel`, `Operator.LaunchPublicKey`/`LaunchKeyId`
  (`Sessions/ValueObjects/OperatorPaymentModel.cs`); `OperatorPlayer.IsCrypto` removed.
- Migration: `Migrations/20260630133341_AddOperatorLaunchAndPaymentModel` (adds columns, drops
  `IsCrypto`, backfills `PaymentModel = Crypto`).
- Middleware + token: `Monkify.Api/Middleware/LaunchTokenValidationMiddleware.cs`,
  `Monkify.Api/Launch/LaunchTokenService.cs` (RS256 sign/validate; key from `RSAParameters`, provider
  caching off); request identity in `Infrastructure/Services/Launch/ILaunchContext.cs`.
- Cached operator lookup: `Infrastructure/Services/Operators/{I,}OperatorService.cs` (used by
  middleware, `GetOperatorByIdHandler`, `RegisterBetHandler`).
- Endpoints: `Monkify.Api/Controllers/{Players,Dev}Controller.cs`;
  `Handlers/Players/UpdatePlayerPreferences/*`; `SessionsController` (no `x-operator-id`).
- Bet path: `Handlers/Sessions/RegisterBet/RegisterBetHandler.cs` (+ `RegisterBetRequest.ExternalPlayerId`).
- Config/seed: `GeneralSettings.LaunchTokens` (`Configs/Entities/LaunchTokenSettings.cs`,
  `Monkify.Common/monkifysettings.json` — mock keys); `Monkify.Consumers/Program.cs` (seed + self-heal).

**Frontend (`monkify-front`)**

- `src/hooks/useLaunch.ts`, `src/utils/launchToken.ts` (capture/store/strip; dev mint).
- `src/services/mainService.ts` (`X-Launch-Token` interceptor, 401→`/error`, `updatePlayerPreferences`).
- `src/hooks/useOperator.ts` (`paymentModel`), `src/hooks/useSession.ts` (payment-model-gated bet).
- `src/types/index.ts` (`OperatorPaymentModel`, `OperatorBranding.paymentModel`, prefs types).

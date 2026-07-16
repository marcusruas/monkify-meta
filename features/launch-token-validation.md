# Feature — Launch token validation (signed casino launch flow)

> Status: **shipped.** Authoritative spec: `../specs/launch-token-validation.md`
> (merged via `feature/launch-token-validation` on 2026-06-30).
>
> ⚠️ **Partly superseded by `features/payment-methods.md` (2026-07-08).** The `Operator.PaymentModel`
> (`Regulated | Crypto`) this feature introduced is **removed**. Payment is now decided **per game
> type** via `GameParameters.PaymentMethod` (`Standard | SolanaToken`). Wherever this doc says the
> launch flow / wallet-skip / identity keys on the operator's payment model, read it as the **game's
> `PaymentMethod`**: Standard → player = token `sub`, no wallet; SolanaToken → connected wallet +
> signature. The launch token itself (RS256, `iss`/`sub`, vague-401, param-free entry) is unchanged.

## What it does

Replaces the trusted **`x-operator-id`** header with a **cryptographically signed launch token**
(JWT, RS256) that the operator's casino signs and ChimpOut validates before the game loads. The token
proves which operator launched the game and which player it acts for. Every operator-scoped REST call
must now carry a valid `X-Launch-Token`; operator and player identity come from the token, not from
headers or request-body fields. Supersedes the `x-operator-id` model in `features/operators.md`.

## Core rules

- **Token required for every operator.** All operator-scoped REST endpoints (`/games/*`,
  `/operators/*`, `/players/*`) require an `X-Launch-Token` header. The `x-operator-id` header is gone;
  `operatorId` is the token's `iss`, the player id is its `sub`.
- **Asymmetric RS256.** The casino signs with its private key; ChimpOut validates against the operator's
  stored **`Operator.LaunchPublicKey`** (PEM). Optional `Operator.LaunchKeyId` supports rotation.
- **Claims:** `iss = operatorId`, `sub = externalPlayerId` (empty for crypto operators), `exp`, `iat`,
  `jti` (audit/log only). `displayName`/`currency` are **not** claims.
- **Validation** (stateless middleware in `ChimpOut.Api`): signature, `exp`/`iat` with **±2 min** clock
  skew, and — when the path carries an operatorId — `iss` must equal it. Replay protection is
  **expiry-only**. The operator must exist and be contract-active and have a `LaunchPublicKey`.
- **Vague failures.** Every failure (missing/expired/bad-signature/unknown-operator/iss-mismatch)
  returns a single generic **HTTP 401**; the real reason + a correlationId are logged **server-side
  only**. The SPA routes any 401 to `/error`. A token expiring mid-play takes the same path (re-launch).
- **SignalR unchanged.** Hubs are game-id scoped — no token on websockets. `/dev/*`, Swagger, and
  `OPTIONS` preflight are not token-guarded.
- **`Operator.PaymentModel` (`Regulated` | `Crypto`)** replaces `OperatorPlayer.IsCrypto` (the column
  is dropped). Crypto-ness is a property of the operator. It drives **wallet-skip**: crypto operators
  connect a wallet and pay on-chain (`paymentSignature` required); regulated operators settle off-chain
  (no wallet, no signature) and identify the player by the token `sub`. Existing operators were
  backfilled to **Crypto**; new operators must be provisioned with `PaymentModel` + `LaunchPublicKey`.
- **Register-bet identity from the token.** For crypto operators the wallet still comes from the body
  (post wallet-connect); for regulated operators the player id is the token `sub` and **body identity
  fields are ignored**. The operator/`PaymentModel` is read via the cached `IOperatorService`; an
  inactive/unknown operator → vague `GameNotValidForBets` (the middleware already blocks this path).
- **Player preferences.** `GET /players/preferences` (token-gated) reads `{ externalPlayerId,
  displayName, currency }` (defaults when no row; no upsert on read). `PATCH /players/preferences`
  upserts the `OperatorPlayer`'s **display-only** `DisplayName` and preferred `Currency` (default
  `USD`). Identity: regulated via token `sub`; crypto via optional `?wallet=` on GET or wallet in PATCH
  body. Currency allow-list is payment-model-scoped (regulated → 33 fiat codes; crypto → USD only).
  Display name: trimmed, 1–20 chars. Currency only affects **client-side rendering** — backend REST
  and SignalR always carry USD; settlement and operator API amounts stay USD. See
  `features/player-customization-modal.md`.
- **Dev mint endpoint.** `POST /dev/launch-token?paymentModel=Crypto|Regulated` (default `Crypto`) is
  **disabled in production**; in dev it resolves the seeded dev operator and returns **`{ token }`
  only** — the operator is recoverable from the token's `iss` claim; the minted dev token is
  long-lived (effectively non-expiring) for local convenience. Two dev operators are seeded:
  **DEV Cassino** (Crypto) and **DEV Regulated Cassino**
  (Regulated), both with a mock launch keypair; the private signing key is a mock placeholder in
  `chimpoutsettings.json`. Seeding is idempotent and self-heals operators that predate the feature.
- **Cached operator lookup.** `IOperatorService.GetActiveOperatorById` caches the active operator under
  the per-operator cache key, shared by the middleware (every request), the branding endpoint, and
  register-bet. Seeding invalidates that cache entry after create/patch.
- **Free bets** remain dev-gated (`AllowFreeBets = false` in prod); each free bet counts as an
  individual player even when they share an `OperatorPlayer` (see `features/free-bet-mode.md`).

## Frontend behaviour

- **Launch (operator from the token, not the URL):** the casino redirects to
  `…/chimpout/game/games#launch_token=<jwt>` — there is **no `/<operatorId>` path segment**.
  `useLaunch.ensureLaunchToken()` reads the token from the URL fragment, **strips it from the URL**,
  and persists it in **`sessionStorage`** (per-tab; survives reloads/hard navigation); a fresh
  fragment token overrides a stored one. The operator id is decoded from the token's `iss` claim via
  `getOperatorIdFromToken` (`utils/launchToken.ts`) and used by `useOperator.loadOperator` to fetch
  branding — operator-scoped routes are param-free (`/chimpout/game/games`, `/chimpout/game/play`).
  Missing/undecodable token → `/error`.
- **Dev entry (no silent auto-mint):** `/chimpout/game` is a dev-only landing (`DevLandingPage.vue`)
  with **Crypto** and **Regulated** buttons that mint a token from `/dev/launch-token` and drop into
  the game-select page. It is gated by **`VUE_APP_DEV_LAUNCH`** (`featureFlags.ts` — default on;
  `.env.production` sets it `false`, so `/chimpout/game` redirects to `/error` and the buttons never
  ship). `scripts/setup_local_environment.bat` forces it on for the local docker build. See
  `features/dev-qol-stuff.md`.
- The axios interceptor (`mainService.ts`) reads the token lazily from the store and sends
  `X-Launch-Token` on every request; any `401` routes to `/error`.
- **Wallet UX is gated on `paymentModel`** (from `GET /operators/{id}`): wallet-connect + on-chain
  `sendToken` run only for `Crypto`; regulated operators post `{ amount, choice, seed }` with no wallet
  or signature.

## Key code touchpoints

**Backend (`monkify-backend`)**

- Entity/enum: `Operator.PaymentModel`, `Operator.LaunchPublicKey`/`LaunchKeyId`
  (`Games/ValueObjects/OperatorPaymentModel.cs`); `OperatorPlayer.IsCrypto` removed.
- Migration: `Migrations/20260630133341_AddOperatorLaunchAndPaymentModel` (adds columns, drops
  `IsCrypto`, backfills `PaymentModel = Crypto`).
- Middleware + token: `ChimpOut.Api/Middleware/LaunchTokenValidationMiddleware.cs`,
  `ChimpOut.Api/Launch/LaunchTokenService.cs` (RS256 sign/validate; key from `RSAParameters`, provider
  caching off); request identity in `Infrastructure/Services/Launch/ILaunchContext.cs`.
- Cached operator lookup: `Infrastructure/Services/Operators/{I,}OperatorService.cs` (used by
  middleware, `GetOperatorByIdHandler`, `RegisterBetHandler`).
- Endpoints: `ChimpOut.Api/Controllers/{Players,Dev}Controller.cs`;
  `Handlers/Players/UpdatePlayerPreferences/*`; `GamesController` (no `x-operator-id`).
- Bet path: `Handlers/Games/RegisterBet/RegisterBetHandler.cs` (+ `RegisterBetRequest.ExternalPlayerId`).
- Config/seed: `GeneralSettings.LaunchTokens` (`Configs/Entities/LaunchTokenSettings.cs`,
  `ChimpOut.Common/chimpoutsettings.json` — mock keys); `ChimpOut.Consumers/Program.cs` (seed + self-heal).

**Frontend (`monkify-front`)**

- `src/hooks/useLaunch.ts` (capture/store/strip token), `src/utils/launchToken.ts`
  (`getOperatorIdFromToken` — decode `iss`), `src/pages/DevLandingPage.vue` + `src/routes.ts`
  (param-free routes + dev landing), `src/constants/featureFlags.ts` (`devLaunchEnabled`).
- `src/services/mainService.ts` (`X-Launch-Token` interceptor, 401→`/error`, `updatePlayerPreferences`).
- `src/hooks/useOperator.ts` (`paymentModel`), `src/hooks/useGame.ts` (payment-model-gated bet).
- `src/types/index.ts` (`OperatorPaymentModel`, `OperatorBranding.paymentModel`, prefs types).

# Monkify — "Launch token validation (signed casino launch flow)" Feature — Implementation Spec

> **Status: Ready for implementation** — refined via Trello card
> [Launch token validation (signed casino launch flow)](https://trello.com/c/LPNm2l1C) (2026-06-30).
> Repos affected: `monkify-backend`, `monkify-front`, and this doc in `monkify-meta`.
>
> **Purpose:** Replace the trust-on-URL-params model — where any caller can claim any operator via
> the `x-operator-id` header — with a **cryptographically signed launch token (JWT, RS256)** that
> the casino signs and Monkify validates before the game loads. The token proves *which operator*
> launched the game and *which player* it acts for, for both regulated and crypto operators.

---

## 1. Summary

Today every operator-scoped REST call carries an untrusted **`x-operator-id`** header
(`SessionsController.RequireOperatorId`, `mainService.ts` interceptor). The operator id is taken on
faith; the player identity for regulated play would arrive as plain body fields. This feature makes
the **launch token** the single source of truth for `operatorId` and `externalPlayerId`.

| Aspect | Before | After |
| --- | --- | --- |
| Operator identity | `x-operator-id` header (untrusted) | `iss` claim of a signed JWT (`X-Launch-Token`) |
| Player identity (regulated) | body `externalPlayerId` | `sub` claim of the token |
| Player identity (crypto) | body `wallet` | body `wallet` (post wallet-connect); `sub` empty |
| Trust | none (header is whatever the caller sends) | RS256 signature against the operator's public key |
| Wallet connect required? | inferred from `wallet` presence in body | driven by `Operator.PaymentModel` |
| `OperatorPlayer.IsCrypto` | per-player bool | **removed** — derived from `Operator.PaymentModel` |
| Display name / currency | `displayName` on register-bet; currency defaults USD | **token-gated prefs endpoint** (display-only) |

**Key shape.** Stateless middleware in `Monkify.Api` validates `X-Launch-Token` on every
operator-scoped REST call (`/sessions/*`, `/operators/*`, `/players/*`), stashes
`{ operatorId, externalPlayerId }` in the request context, and returns a **generic 401** on any
failure (real reason logged server-side only). SignalR hubs are unchanged (session-id only, no
token). Each operator signs its own token on its own site and **redirects** to Monkify with the
token in the **URL fragment**; the SPA reads it, strips it from the URL, and attaches it to every
REST call. **DEV Cassino** uses a **dev-only mint endpoint** (disabled in production) so local dev
can obtain a valid token.

Parent epic: [B2B integration package](https://trello.com/c/w6ymVPgD). Builds directly on
`specs/operator-feature.md` (operator scoping) and `specs/operator-player-identity.md`
(`OperatorPlayer`, `Bet.OperatorPlayerId`). Currency **conversion / FX rendering** is split to a
separate card → [#25](https://trello.com/c/ZyJViMKI).

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Branch-per-repo:** `feature/launch-token-validation` in `monkify-backend`, `monkify-front`,
  `monkify-meta`.
- **Unit tests:** update/add in `Monkify.Tests`; report changed tests at end of implementation.
- **EF mapping:** inline in `MonkifyDbContext.OnModelCreating`; no `IEntityTypeConfiguration`.
- **Migrations:** under `Monkify.Infrastructure/Migrations/`; applied **only** by `Monkify.Consumers`
  at startup (`Database.Migrate()`). The API does not migrate.
- **Secrets/config:** **mock-only** values in committed files. The launch dev **private** key is a
  placeholder PEM; **never** write a real key, and **never** touch the existing real
  `GeneralSettings.Token.TokenOwnerPrivateKey` in `monkifysettings.json`.
- **Operator/token messaging:** vague errors only — a single generic **401** for *all* token
  failures; never reveal whether the cause was bad signature, expiry, unknown operator, or
  `iss`/path mismatch.
- **API stays stateless:** validation middleware lives in `Monkify.Api`; no migration work there.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **Asymmetric JWT, RS256.** Casino signs with its **private** key; Monkify validates with the operator's stored **public** key. No shared/house key. |
| 2 | **Minimal claims:** `iss = operatorId` (Guid), `sub = externalPlayerId` (empty/absent for crypto), `exp`, `iat`, `jti`. `displayName` and `currency` are **not** claims. |
| 3 | **Public key on the operator row:** `Operator.LaunchPublicKey` (PEM) + optional `Operator.LaunchKeyId`. |
| 4 | **Token required for *every* operator.** `x-operator-id` header is **removed**; `operatorId` comes from `iss`. |
| 5 | **Stateless middleware in `Monkify.Api`** validates `X-Launch-Token` on every operator-scoped REST call (signature, `exp`/`iat`, clock skew ±2 min). Replay protection = **expiry only**; `jti` is audit/log only. |
| 6 | Middleware stashes `{ operatorId, externalPlayerId }` in request context. `register-bet` reads identity **from context** and **ignores body identity** for regulated operators. |
| 7 | **`operatorId` consistency:** if the URL path carries an operatorId, middleware requires it to equal `iss`; mismatch → generic 401. |
| 8 | **Per-operator issuer model:** every operator signs its own token on its own site and **redirects** to Monkify. Crypto partners must have a redirectable site too. |
| 9 | **DEV Cassino** has a **dev-only `POST` mint endpoint** (disabled in production) that signs and returns a JWT. Dev keypair seeded; private key is a mock placeholder in settings. |
| 10 | **`Operator.PaymentModel` enum (`Regulated` \| `Crypto`)** — exclusively one. Drives wallet-skip: FE shows wallet-connect only for `Crypto`; BE requires `paymentSignature` only for `Crypto`. |
| 11 | **`OperatorPlayer.IsCrypto` removed** (migration drops the column). Crypto-ness derived from the player's `Operator.PaymentModel`. |
| 12 | **New token-gated prefs endpoint** updates `OperatorPlayer.DisplayName` + preferred `Currency` (default `USD`). **Display-only.** Identity: regulated via `sub`; crypto via wallet in body. |
| 13 | **Currency is a display preference only.** Game-native currency stays (regulated = USD, crypto = the token). Conversion/FX rendering is **out of scope** → card #25. |
| 14 | **Transport: URL fragment.** `…/monkify/game/<operatorId>#launch_token=<jwt>`. FE reads on load, holds in memory, **strips from URL**, sends `X-Launch-Token` on every REST call. |
| 15 | **Trust the casino `exp`** (`now < exp`, no Monkify cap). Clock skew ±2 min on `exp`/`iat`. |
| 16 | **All token failures → generic 401** + vague message; real reason + correlationId → server logs only. FE routes to `/error`. Token expiring mid-play → same path (re-launch). |
| 17 | **SignalR unchanged** — no token on websockets (session-id scoping only). |
| 18 | **Migration backfills `PaymentModel = Crypto`** for all existing operators; DEV Cassino = Crypto. New operators must be provisioned explicitly with `PaymentModel` + `LaunchPublicKey`. |
| 19 | **Dev convenience (assumption E):** seed a second dev operator `DEV Regulated Cassino` (`PaymentModel = Regulated`) so the regulated path is testable locally. The dev mint endpoint takes a `paymentModel` query param (`Crypto` default \| `Regulated`) to pick which dev operator to mint for, and returns `{ token, operatorId }`. |

---

## 4. Data model

### 4.1 Entity / column changes

**`Operator`** — `monkify-backend/src/Monkify.Domain/Sessions/Entities/Operator.cs` — add:

```csharp
public OperatorPaymentModel PaymentModel { get; set; }  // Regulated | Crypto
public string? LaunchPublicKey { get; set; }            // PEM (SubjectPublicKeyInfo) — required to launch
public string? LaunchKeyId { get; set; }                // optional key id (kid) for rotation
```

**New enum** — `monkify-backend/src/Monkify.Domain/Sessions/ValueObjects/OperatorPaymentModel.cs`
(new):

```csharp
namespace Monkify.Domain.Sessions.ValueObjects
{
    public enum OperatorPaymentModel
    {
        Regulated = 0,
        Crypto = 1,
    }
}
```

> Stored as `int`. The migration backfills existing rows to `Crypto` (value `1`) — do **not** rely
> on the CLR default (`0 = Regulated`) for existing data.

**`OperatorPlayer`** — `monkify-backend/src/Monkify.Domain/Sessions/Entities/OperatorPlayer.cs` —
**remove** `public bool IsCrypto { get; set; }`. Keep `DisplayName`, `Currency`, `LastSeenAt`.

### 4.2 EF configuration (inline)

File: `monkify-backend/src/Monkify.Infrastructure/Data/Context/MonkifyDbContext.cs`

**`Operator` block (≈ lines 59–79)** — add inside the existing `modelBuilder.Entity<Operator>`:

```csharp
builder.Property(x => x.PaymentModel).IsRequired().HasDefaultValue(OperatorPaymentModel.Crypto);
builder.Property(x => x.LaunchPublicKey).HasMaxLength(2000);
builder.Property(x => x.LaunchKeyId).HasMaxLength(100);
```

**`OperatorPlayer` block (≈ lines 81–92)** — **remove** the line:

```csharp
builder.Property(x => x.IsCrypto).IsRequired();   // DELETE
```

### 4.3 Migration

Name: **`AddOperatorLaunchAndPaymentModel`**

`Up()` must:

1. Add `Operators.PaymentModel` (`integer`, not null, default `1`).
2. **Backfill** `UPDATE "Operators" SET "PaymentModel" = 1;` (Crypto) — all existing operators.
3. Add `Operators.LaunchPublicKey` (`text`, nullable) and `Operators.LaunchKeyId` (`varchar(100)`,
   nullable).
4. Drop `OperatorPlayers.IsCrypto`.

```bash
# from monkify-backend/src/Monkify.Infrastructure
dotnet ef migrations add AddOperatorLaunchAndPaymentModel \
  --startup-project ../Monkify.Consumers/Monkify.Consumers.csproj \
  --context MonkifyDbContext
```

Review the generated migration: ensure the `PaymentModel` backfill runs and `IsCrypto` is dropped.
Do not hand-edit the snapshot. Applies automatically via `ApplyMigrations(app)` in
`Monkify.Consumers/Program.cs`.

### 4.4 ER diagram

```mermaid
erDiagram
    Operator ||--o{ OperatorPlayer : has
    Operator ||--o{ SessionParameters : owns
    OperatorPlayer ||--o{ Bet : identifies

    Operator {
        guid Id PK
        int PaymentModel "Regulated|Crypto"
        string LaunchPublicKey "PEM, nullable"
        string LaunchKeyId "nullable"
        datetime ContractStartDate
        datetime ContractEndDate "nullable"
    }
    OperatorPlayer {
        guid Id PK
        guid OperatorId FK
        string ExternalPlayerId
        string DisplayName "nullable"
        string Currency "default USD"
        datetime LastSeenAt
    }
```

---

## 5. Configuration

New settings section for launch-token signing/validation. **Mock values only.**

**`GeneralSettings`** — `monkify-backend/src/Monkify.Domain/Configs/Entities/GeneralSettings.cs` —
add `public LaunchTokenSettings LaunchTokens { get; set; }`.

**New** `monkify-backend/src/Monkify.Domain/Configs/Entities/LaunchTokenSettings.cs`:

```csharp
[ExcludeFromCodeCoverage]
public class LaunchTokenSettings
{
    public int ClockSkewSeconds { get; set; }            // 120 (±2 min)
    public string DevSigningPrivateKeyPem { get; set; }  // MOCK — dev mint endpoint only
    public string DevOperatorPublicKeyPem { get; set; }  // MOCK — seeded onto DEV operators' LaunchPublicKey
}
```

**`monkify-backend/src/Monkify.Common/monkifysettings.json`** — add a new section under
`GeneralSettings` (sibling of `Token`, **not** inside it). Use obvious mock PEMs:

```jsonc
"LaunchTokens": {
    "ClockSkewSeconds": 120,
    "DevSigningPrivateKeyPem": "-----BEGIN PRIVATE KEY-----\nMOCK_DEV_LAUNCH_PRIVATE_KEY_DO_NOT_USE\n-----END PRIVATE KEY-----",
    "DevOperatorPublicKeyPem": "-----BEGIN PUBLIC KEY-----\nMOCK_DEV_LAUNCH_PUBLIC_KEY\n-----END PUBLIC KEY-----"
}
```

> Leave the existing `GeneralSettings.Token.*` Solana values untouched. The launch keys are a
> **separate** concern from the Solana token owner key. Real operator public keys are provisioned
> per-operator in the DB (`Operator.LaunchPublicKey`), not in settings.

---

## 6. Backend behaviour

### 6.1 Launch context accessor (new)

Add a scoped accessor so handlers read the validated identity instead of a header.

`monkify-backend/src/Monkify.Infrastructure/Services/Launch/ILaunchContext.cs` (new):

```csharp
public interface ILaunchContext
{
    bool IsSet { get; }
    Guid OperatorId { get; }
    string? ExternalPlayerId { get; }   // token sub; null/empty for crypto
    void Set(Guid operatorId, string? externalPlayerId);
}
```

Register the implementation **scoped** in `Monkify.Infrastructure/DependencyInjection`. The
middleware calls `Set(...)`; handlers read it. (Alternatively store on `HttpContext.Items`, but a
typed scoped service keeps handlers host-agnostic and testable.)

### 6.2 Token validation middleware (new, `Monkify.Api`)

`monkify-backend/src/Monkify.Api/Middleware/LaunchTokenValidationMiddleware.cs` (new). Registered in
`Monkify.Api/Program.cs` **after** `UseCors` and **before** `MapControllers`.

Guards **operator-scoped** paths: `/sessions`, `/operators`, `/players`. **Skips:** `OPTIONS`
preflight, the dev mint endpoint (`/dev/launch-token`), Swagger, and SignalR hubs
(`/Hubs/*` are mapped on the API host — exclude them; they keep session-id-only scoping per
`specs/operator-feature.md` §13.14).

Algorithm:

1. Read header `X-Launch-Token`. Missing/empty → **401 generic**.
2. Parse JWT without trusting it; read `iss` (operator id). Unparseable → 401.
3. Load operator (`Operators.FirstOrDefaultAsync(Id == iss)`); if not found, no `LaunchPublicKey`,
   or `!ContractIsActive(UtcNow)` → 401. (Reuse the active-contract semantics; do **not** reveal
   which check failed.)
4. Validate signature (RS256) against `LaunchPublicKey`, and `exp`/`iat` with
   `ClockSkew = LaunchTokens.ClockSkewSeconds`. Any failure → 401.
5. If the request path contains an operatorId segment that does **not** equal `iss` → 401.
6. `ILaunchContext.Set(iss, sub)`; continue.

On every 401: write a single generic body (reuse the existing
`new ApiResult<object>(null, new[] { new Message(MessageType.ValidationFailure, "The request could not be processed.") })`
shape, status 401) and `Log.Warning` the **real** reason + a correlation id. Never echo the reason
to the client.

> Validation uses `System.IdentityModel.Tokens.Jwt` / `Microsoft.IdentityModel.Tokens`
> (`JwtSecurityTokenHandler` + `RsaSecurityKey` built from the PEM via
> `RSA.ImportFromPem(...)`). Add the package to `Monkify.Api` (or `Monkify.Infrastructure` if the
> handler logic is factored there for unit testing).

### 6.3 Controllers — drop `x-operator-id`

File: `monkify-backend/src/Monkify.Api/Controllers/SessionsController.cs`

- **Remove** `RequireOperatorId` and every `[FromHeader(Name = "x-operator-id")] Guid? operatorId`
  parameter. Operator id now comes from `ILaunchContext.OperatorId`.
- `Filter`, `GetActiveParameters`, `GetActiveSessionForParameter`, `RegisterBet` build their MediatR
  request with `launchContext.OperatorId` (inject `ILaunchContext` into the controller or read via a
  `BaseController` helper).
- `RegisterBet`: pass `launchContext.ExternalPlayerId` into the request so the handler can use it for
  the regulated path (see §6.4).

File: `monkify-backend/src/Monkify.Api/Controllers/OperatorsController.cs` — `GET /operators/{id}`
is now token-guarded by the middleware (the path operatorId must equal `iss`). No code change beyond
the DTO addition in §6.6.

### 6.4 `RegisterBetHandler` — identity from token, crypto from PaymentModel

File: `monkify-backend/src/Monkify.Infrastructure/Handlers/Sessions/RegisterBet/RegisterBetHandler.cs`

Current handler infers `isCrypto` from whether the body `Wallet` is present (lines 74–83). Replace
with **operator-driven** logic:

1. Load the operator (or read `PaymentModel` via a lightweight lookup / from the session's
   `Parameters.Operator`, already loaded in `ValidateBetParameters`). Determine
   `isCrypto = operator.PaymentModel == OperatorPaymentModel.Crypto`.
2. **Crypto operator:** identity = body `Wallet` (post wallet-connect), `externalId = wallet`. Paid
   bets require `paymentSignature` (unchanged). Free bets keep anonymous `OperatorPlayerId = null`.
3. **Regulated operator:** identity = `request.ExternalPlayerId` (from token `sub`); **ignore** body
   `Wallet`/`ExternalPlayerId`. No `paymentSignature` required for the regulated path (no Solana
   transfer; regulated settlement is out of scope here — bets register against the casino player id).
4. `UpsertOperatorPlayerAsync` — **drop the `isCrypto` parameter and the
   `IsCrypto = isCrypto` assignment** (the column is gone). Signature becomes
   `(operatorId, externalPlayerId, displayName, ct)`.

> `RegisterBetRequest` gains `public string? ExternalPlayerId { get; set; }` populated by the
> controller from `ILaunchContext.ExternalPlayerId`. The body `ExternalPlayerId`/`DisplayName`
> fields on `RegisterBetRequestBody` are no longer the regulated identity source — keep the body
> shape backward-compatible but document that regulated identity now comes from the token.

### 6.5 Wallet-skip / Solana

- `SolanaService` reads the wallet from `OperatorPlayer.ExternalPlayerId`
  (`Bet.GetWalletAddress()` in `BetExtensions.cs`) — **no change**; it never read `IsCrypto`.
- The "wallet vs no wallet" decision is now `Operator.PaymentModel`. For regulated operators the
  handler never calls Solana validation (no `paymentSignature`). Free-bet gating
  (`_settings.Sessions.AllowFreeBets`, `Parameters.AllowFreeBet`) is unchanged.

### 6.6 `OperatorDto` — expose payment model

File: `monkify-backend/src/Monkify.Infrastructure/Handlers/Operators/GetOperatorById/OperatorDto.cs`
— add `public OperatorPaymentModel PaymentModel { get; set; }` (set from `op.PaymentModel`). The FE
needs it to decide whether to render wallet-connect. (Do **not** expose `LaunchPublicKey` /
`LaunchKeyId` / contacts.)

### 6.7 Player preferences endpoint (new)

New controller `monkify-backend/src/Monkify.Api/Controllers/PlayersController.cs`
(`[Route("players")]`), token-guarded by the middleware:

```csharp
[HttpPatch("preferences")]
public Task<IActionResult> UpdatePreferences([FromBody] UpdatePlayerPreferencesRequestBody body)
    => ProcessRequest(new UpdatePlayerPreferencesRequest(launchContext.OperatorId,
                                                         launchContext.ExternalPlayerId,
                                                         body));
```

New handler `Handlers/Players/UpdatePlayerPreferences/` (request + `RequestBody` + handler):

- Body: `{ string? DisplayName; string? Currency; string? Wallet; }`.
- **Identity resolution:** if `launchContext.ExternalPlayerId` is non-empty (regulated) → that is the
  player id; else (crypto) → `body.Wallet` (required for crypto; reject vague if missing).
- **Upsert** `OperatorPlayer(operatorId, externalPlayerId)`; set `DisplayName` (≤100 chars) and
  `Currency` (≤10 chars, default `USD` when not yet set). Display-only — **no** validation of the
  currency against a supported set here (deferred to card #25), but trim/upper-case and bound length.
- Returns the updated prefs (or `204`). Reuse the upsert logic from `RegisterBetHandler`
  (extract to a shared `IOperatorPlayerService.UpsertAsync` if convenient).

### 6.8 Dev mint endpoint (new, non-production only)

New controller `monkify-backend/src/Monkify.Api/Controllers/DevController.cs`
(`[Route("dev")]`) — **the middleware skips `/dev/*`**, and the action itself returns `404` when
`IHostEnvironment.IsProduction()`:

```csharp
[HttpPost("launch-token")]
public async Task<IActionResult> MintLaunchToken([FromQuery] OperatorPaymentModel paymentModel = OperatorPaymentModel.Crypto)
{
    if (env.IsProduction()) return NotFound();
    // Pick the seeded dev operator by payment model (default Crypto).
    var name = paymentModel == OperatorPaymentModel.Regulated ? "DEV Regulated Cassino" : "DEV Cassino";
    var op = await context.Operators.AsNoTracking().FirstOrDefaultAsync(x => x.Name == name);
    if (op == null) return NotFound();
    // Sign an RS256 JWT with LaunchTokens.DevSigningPrivateKeyPem:
    //   iss = op.Id, sub = (regulated ? "dev-player" : ""), iat = now, exp = now + 3600, jti = Guid
    // Return { token, operatorId = op.Id }.
}
```

The endpoint takes a single optional **`paymentModel`** query parameter (`Crypto` default, or
`Regulated`) to choose which seeded dev operator to mint for, and returns **both the token and that
operator's id** (`{ token, operatorId }`). Regulated tokens carry a default `sub` (`dev-player`) so
the regulated register-bet/prefs paths are exercised; crypto tokens have an empty `sub` (the wallet
identifies the player). Both dev operators are seeded with
`LaunchPublicKey = LaunchTokens.DevOperatorPublicKeyPem`, so the middleware validates dev-minted
tokens against the seeded public key. Inject `IHostEnvironment`, `GeneralSettings`, and
`MonkifyDbContext`.

### 6.9 Seeding — `Monkify.Consumers/Program.cs`

File: `monkify-backend/src/Monkify.Consumers/Program.cs` → `CreateDefaultOperatorAsync` (≈ line 170):

- Set `PaymentModel = OperatorPaymentModel.Crypto` and
  `LaunchPublicKey = settings.LaunchTokens.DevOperatorPublicKeyPem` on the seeded **DEV Cassino**.
- **Also seed** a second operator **DEV Regulated Cassino** with
  `PaymentModel = OperatorPaymentModel.Regulated` and the same mock `LaunchPublicKey`, so the
  regulated launch/prefs paths are testable locally (assumption E). Its
  `CreateDefaultSessionParametersAsync` coverage is optional — at minimum it can share default
  params, or skip game types for the regulated dev operator if not needed for the test.

### 6.10 Hidden blockers (documented)

| Blocker | Fix |
| --- | --- |
| `BetTestFactory.cs` sets `IsCrypto = true` (2 places) | Remove those assignments — column is gone |
| `RegisterBetHandler.UpsertOperatorPlayerAsync` takes `isCrypto` and sets `IsCrypto` | Drop the param + assignment |
| `MonkifyDbContext` `OperatorPlayer` block maps `IsCrypto` | Remove the mapping line |
| Existing `RegisterBetHandlerTests` rely on `x-operator-id` / body identity | Re-point to `ILaunchContext` + token sub; crypto via body wallet |
| `GET /operators/{id}` previously had no token; FE now needs it before game render | Token is in the URL fragment at load; interceptor attaches it (§7) |
| Real `Token.TokenOwnerPrivateKey` exists in `monkifysettings.json` | Do **not** touch it; launch keys are a separate mock section |
| Middleware must not block SignalR hubs / dev mint | Path allow-list excludes `/Hubs/*`, `/dev/*`, `OPTIONS`, Swagger |

---

## 7. Frontend behaviour

### 7.1 Launch token capture — new `useLaunch` composable

New `monkify-front/src/hooks/useLaunch.ts` + module-level `ref` store (singleton, like other hooks):

- On game-route entry, read `window.location.hash` for `launch_token=<jwt>` (the fragment never hits
  the server). Store it in a module-level `ref<string | null>` held **in memory only** (not
  localStorage).
- **Strip** the token from the URL: `history.replaceState(null, '', window.location.pathname + window.location.search)`.
- Expose `launchToken` (getter) and `setLaunchToken`.
- **Dev fallback:** when `process.env.NODE_ENV !== 'production'` and no token is present, call the
  dev mint endpoint (`POST /dev/launch-token`, **no body** — it returns `{ token, operatorId }` for
  the seeded dev operator) so local dev works without a real casino redirect.

### 7.2 `mainService.ts` interceptor — send `X-Launch-Token`

File: `monkify-front/src/services/mainService.ts`

- **Replace** the `x-operator-id` block (lines 16–20) with:

```ts
const token = getLaunchToken();           // from useLaunch module store
if (token) {
  config.headers = config.headers ?? {};
  (config.headers as Record<string, string>)['X-Launch-Token'] = token;
}
```

- On any `401`, route to `/error` (add a response interceptor, or handle in callers /
  `useOperator`). The `operatorRoute.ts` GUID helper stays for the **path** operatorId used for
  branding fetch and the `iss`/path consistency the backend enforces.

### 7.3 `useOperator` — token-aware branding + wallet model

File: `monkify-front/src/hooks/useOperator.ts`

- `loadOperator()` still calls `GET /operators/{id}` (interceptor now attaches the token). On `401`
  or missing branding → `/error` (existing behavior).
- Surface `branding.paymentModel` so the game can decide wallet UX.

### 7.4 Wallet UX gated on `paymentModel`

Files: `monkify-front/src/hooks/useSession.ts`, `useConnection.ts`, and the bet UI components.

- Show wallet-connect and call `sendToken` **only** when `paymentModel === 'Crypto'`. For
  `Regulated`, skip wallet entirely: the bet POST carries no `wallet`/`paymentSignature`; the player
  is identified by the token `sub` server-side.
- `bet()` in `useSession.ts` (lines 174–240): branch on payment model — crypto keeps the current
  `sendToken` → `paymentSignature` flow; regulated posts `{ amount, choice, seed }` only.

### 7.5 Types & prefs service

File: `monkify-front/src/types/index.ts`

- `OperatorBranding` — add `paymentModel: 'Regulated' | 'Crypto'` (mirror backend enum; confirm
  serialized form — string vs int — and match it exactly).
- Add a prefs request type `{ displayName?: string; currency?: string; wallet?: string }`.

File: `monkify-front/src/services/mainService.ts` — add
`updatePlayerPreferences(body)` → `PATCH /players/preferences` and export it.

### 7.6 User-facing copy

- All token/`401` failures show the existing generic `/error` page — **no** token-specific copy and
  no hint about why (vague messaging constraint).

---

## 8. Flow diagrams

### 8.1 Launch + validation

```mermaid
sequenceDiagram
    participant Casino as Operator site
    participant FE as Monkify SPA
    participant MW as Launch middleware (Monkify.Api)
    participant H as Handlers

    Casino->>Casino: sign JWT (RS256, private key)
    Casino->>FE: redirect /monkify/game/<opId>#launch_token=<jwt>
    FE->>FE: read fragment, store in memory, strip URL
    FE->>MW: GET /operators/<opId> (X-Launch-Token)
    MW->>MW: load Operator(iss).LaunchPublicKey; verify RS256, exp/iat; iss == path?
    alt valid
        MW->>H: ILaunchContext.Set(operatorId, sub)
        H-->>FE: branding (incl. paymentModel)
    else any failure
        MW-->>FE: 401 generic (real reason logged only)
        FE->>FE: route to /error
    end
```

### 8.2 Register bet by payment model

```mermaid
flowchart TD
    A[POST /sessions/:id/bets + X-Launch-Token] --> B{Operator.PaymentModel}
    B -->|Crypto| C[identity = body wallet; paid needs paymentSignature; Solana validate]
    B -->|Regulated| D[identity = token sub; ignore body wallet; no Solana]
    C --> E[Upsert OperatorPlayer; insert Bet]
    D --> E
```

---

## 9. Out of scope

- **Currency conversion / FX rendering** of the preferred currency → card #25 (`ZyJViMKI`).
- **postMessage / iframe** launch transport (only URL-fragment redirect here).
- **Token revocation lists / `jti` replay store** (expiry-only).
- Operator onboarding UI for uploading public keys (keys provisioned operationally / via seeding).
- Regulated **settlement / wallet debit-credit** with the casino ledger (register-bet only records
  the casino player id; money movement for regulated is a later story).
- `monkify-meta/features/` KB update (done at ship via `/monkify-ship-feature`).

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

| # | File | Change |
| --- | --- | --- |
| 1 | `Monkify.Domain/Sessions/ValueObjects/OperatorPaymentModel.cs` | **New** enum (Regulated, Crypto) |
| 2 | `Monkify.Domain/Sessions/Entities/Operator.cs` | Add `PaymentModel`, `LaunchPublicKey`, `LaunchKeyId` |
| 3 | `Monkify.Domain/Sessions/Entities/OperatorPlayer.cs` | Remove `IsCrypto` |
| 4 | `Monkify.Domain/Configs/Entities/GeneralSettings.cs` | Add `LaunchTokens` |
| 5 | `Monkify.Domain/Configs/Entities/LaunchTokenSettings.cs` | **New** settings class |
| 6 | `Monkify.Common/monkifysettings.json` | Add `GeneralSettings.LaunchTokens` (mock PEMs) |
| 7 | `Monkify.Infrastructure/Data/Context/MonkifyDbContext.cs` | Operator mapping (PaymentModel/keys); remove `IsCrypto` map |
| 8 | `Monkify.Infrastructure/Migrations/*AddOperatorLaunchAndPaymentModel*` | Add columns + backfill Crypto; drop `IsCrypto` |
| 9 | `Monkify.Infrastructure/Services/Launch/ILaunchContext.cs` (+ impl) | **New** scoped accessor |
| 10 | `Monkify.Infrastructure/DependencyInjection.cs` | Register `ILaunchContext` (scoped) |
| 11 | `Monkify.Api/Middleware/LaunchTokenValidationMiddleware.cs` | **New** RS256 validation, 401 generic |
| 12 | `Monkify.Api/Program.cs` | Register middleware before `MapControllers` |
| 13 | `Monkify.Api/Controllers/SessionsController.cs` | Drop `x-operator-id`/`RequireOperatorId`; read `ILaunchContext` |
| 14 | `Monkify.Api/Controllers/OperatorsController.cs` | (token-guarded by MW; no body change) |
| 15 | `Monkify.Api/Controllers/PlayersController.cs` | **New** `PATCH /players/preferences` |
| 16 | `Monkify.Infrastructure/Handlers/Players/UpdatePlayerPreferences/*` | **New** request/body/handler |
| 17 | `Monkify.Api/Controllers/DevController.cs` | **New** dev-only `POST /dev/launch-token` |
| 18 | `Monkify.Infrastructure/Handlers/Sessions/RegisterBet/RegisterBetHandler.cs` | Identity from token; crypto from `PaymentModel`; drop `isCrypto` |
| 19 | `Monkify.Infrastructure/Handlers/Sessions/RegisterBet/RegisterBetRequest.cs` | Add `ExternalPlayerId` (from token sub) |
| 20 | `Monkify.Infrastructure/Handlers/Operators/GetOperatorById/OperatorDto.cs` | Expose `PaymentModel` |
| 21 | `Monkify.Consumers/Program.cs` | Seed DEV Cassino (Crypto) + DEV Regulated Cassino + LaunchPublicKey |
| 22 | `Monkify.Tests/**` | Update `BetTestFactory`, register-bet, add middleware/prefs/dev tests |

### 10.2 Frontend (`monkify-front`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/hooks/useLaunch.ts` | **New** — capture/store/strip fragment token; dev mint fallback |
| 2 | `src/services/mainService.ts` | Send `X-Launch-Token`; add `updatePlayerPreferences`; 401 → `/error` |
| 3 | `src/hooks/useOperator.ts` | Surface `paymentModel`; 401 handling |
| 4 | `src/hooks/useSession.ts` | Gate wallet/`sendToken` on `paymentModel`; regulated bet body |
| 5 | `src/types/index.ts` | `OperatorBranding.paymentModel`; prefs type |
| 6 | `src/utils/operatorRoute.ts` | Keep GUID helper for path operatorId (no x-operator-id) |

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/launch-token-validation.md` | This spec |
| 2 | `INDEX.md` | KB map row + feature-in-flight |

---

## 11. Unit tests

Project: `monkify-backend/src/Monkify.Tests`.

| Test file | Test cases | Expected |
| --- | --- | --- |
| `LaunchTokenValidationTests.cs` (new) | No `X-Launch-Token` | 401 generic; no handler reached |
| `LaunchTokenValidationTests.cs` | Bad signature | 401; real reason logged only |
| `LaunchTokenValidationTests.cs` | Expired (beyond skew) | 401 |
| `LaunchTokenValidationTests.cs` | Valid token | `ILaunchContext` set to `iss`/`sub`; pipeline continues |
| `LaunchTokenValidationTests.cs` | `iss` ≠ path operatorId | 401 |
| `LaunchTokenValidationTests.cs` | Operator unknown / inactive / no public key | 401 (indistinguishable) |
| `RegisterBetHandlerTests.cs` | Regulated operator | identity from token `sub`; body wallet ignored; no Solana call |
| `RegisterBetHandlerTests.cs` | Crypto operator, paid | wallet from body; `paymentSignature` required; Solana validated |
| `RegisterBetHandlerTests.cs` | Crypto operator, free (dev) | anonymous `OperatorPlayerId == null` |
| `RegisterBetHandlerTests.cs` | Upsert no longer sets `IsCrypto` | compiles; player created without the column |
| `UpdatePlayerPreferencesTests.cs` (new) | Regulated via `sub` | `DisplayName`/`Currency` updated on upserted player |
| `UpdatePlayerPreferencesTests.cs` | Crypto via body wallet | upserts and updates; missing wallet → vague reject |
| `UpdatePlayerPreferencesTests.cs` | No currency set | defaults `USD` |
| `DevMintEndpointTests.cs` (new) | Non-prod | returns a token validatable by the middleware |
| `DevMintEndpointTests.cs` | Production | 404 |
| `OperatorSeedTests` (extend) | Migration/seed | existing operators `PaymentModel == Crypto`; `IsCrypto` column gone |

> If middleware is hard to unit-test in isolation, factor the validation into a
> `Monkify.Infrastructure` service (`ILaunchTokenValidator`) and test that directly; the middleware
> becomes a thin adapter.

---

## 12. Acceptance criteria

Mirrors the Trello **Acceptance Criteria** checklist — implement until all pass:

1. Given a request to an operator-scoped REST endpoint with no `X-Launch-Token`, when received, then generic 401 and no operator data.
2. Given a token whose signature fails against the operator's `LaunchPublicKey`, when an operator-scoped call is made, then generic 401 and the real reason + correlationId are logged server-side only.
3. Given a token whose `exp` is past (beyond ±2 min skew), when an operator-scoped call is made, then generic 401.
4. Given a valid token, when an operator-scoped call is made, then `operatorId` = `iss`, `externalPlayerId` = `sub`, and the request proceeds without any `x-operator-id` header.
5. Given a token whose `iss` ≠ the path operatorId, when an operator-scoped call is made, then generic 401.
6. Given a regulated operator and a valid token, when the player registers a bet, then `OperatorPlayer` is upserted from token identity and body identity is ignored.
7. Given `PaymentModel = Regulated`, when the player opens the game and bets, then no wallet-connect is shown and no `paymentSignature` is required.
8. Given `PaymentModel = Crypto`, when the player bets, then wallet-connect is shown and `paymentSignature` is required; crypto-ness derives from `Operator.PaymentModel` (not `OperatorPlayer.IsCrypto`).
9. Given the migration runs against an existing DB, when complete, then `OperatorPlayer.IsCrypto` is dropped and every existing operator has `PaymentModel = Crypto`.
10. Given a regulated player with a valid token, when they call the prefs endpoint with display name + currency, then their `OperatorPlayer.DisplayName`/`Currency` update (identity from `sub`).
11. Given a crypto player with a valid token, when they call the prefs endpoint with their wallet in the body, then the matching `OperatorPlayer` is upserted and `DisplayName`/`Currency` update.
12. Given a new `OperatorPlayer` with no preference set, when amounts are rendered, then `Currency` defaults to `USD` and only display rendering is affected (no conversion here).
13. Given a casino redirect to `…/monkify/game/<operatorId>#launch_token=<jwt>`, when the SPA loads, then it reads the token from the fragment, holds it in memory, strips it from the URL, and attaches `X-Launch-Token` on every REST call.
14. Given a token that expires mid-play, when the next REST call is made, then 401 and the SPA routes to `/error` (re-launch required).
15. Given the dev mint endpoint, when called in non-production then it returns a valid signed JWT for the dev operator; when called in production then it is disabled/unavailable.
16. Given a SignalR hub connection, when the player streams typer/bets/status, then no launch token is required (session-id scoping unchanged).

---

## 13. Manual QA

1. Start `Monkify.Consumers` (applies migration, seeds DEV Cassino + DEV Regulated Cassino) and
   `Monkify.Api`.
2. Open `/monkify/game/<dev-crypto-guid>` — dev fallback mints a token; confirm `X-Launch-Token`
   is sent, branding loads, wallet-connect shown, paid bet works.
3. Open `/monkify/game/<dev-regulated-guid>` — confirm **no** wallet-connect; place a bet; confirm
   `OperatorPlayer` keyed on the token `sub`.
4. Tamper the token (edit a char) → next call 401 → redirected to `/error`; server log shows real
   reason + correlationId.
5. `PATCH /players/preferences` with a display name + currency → row updated; default `USD` when
   unset.
6. Confirm a SignalR session still streams with no token.
7. Run full `Monkify.Tests` (`scripts/run_tests.bat`); report changed tests.

---

## 14. Trello / pipeline

- **Source card:** https://trello.com/c/LPNm2l1C — list **Specs created**; **Implementation**
  section on the card lists branch + spec.
- **Spun-off:** currency conversion → https://trello.com/c/ZyJViMKI (#25).
- **After merge:** `/monkify-ship-feature Launch token validation` (reads branch from card).

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-06-30 | Initial spec from refined card + same-session refinement decisions (1–19). |

# ChimpOut — "Main production casino (default operator + auto-mint)" Feature — Implementation Spec

> **Status: Shipped** — merged via `feature/main-production-operator` on 2026-07-15. Refined via Trello
> card [Main Prd cassino (#60)](https://trello.com/c/bdkTZJOL/60-main-prd-cassino).
> Repos affected: `monkify-backend`, `monkify-front`, and this doc in `monkify-meta`.
>
> **Purpose:** Give production a **default operator called "Main"** so the public game "just works"
> with no casino redirect. Today production seeds **no operator at all** (`CreateDefaultOperatorAsync`
> early-returns in prod), so a token-less visitor dead-ends on `/error`. This feature seeds Main
> (its own registered Solana token + three crypto game types) and, when a visitor arrives with **no
> launch token**, has the app **mint an anonymous Main launch token and use it**. All Main play is
> crypto (SolanaToken / wallet-paid), so the auto-minted token needs no player identity.

---

## 1. Summary

Two independent-but-paired changes:

| Area | Change |
| --- | --- |
| **Backend — seeding** | Seed an operator named **`Main`** in **all** environments (prod included), pre-provisioned with one registered `OperatorToken` (from `GeneralSettings.Token`) and **three SolanaToken** default game types (free-type race, preset "horse race", 24h lottery — all 6-char alphanumeric, free bets **off**). Keep the existing **DEV Cassino** seeding **non-prod-gated** and unchanged. |
| **Backend — mint** | New **production-enabled** endpoint `POST /launch-token/default` that mints an **anonymous** (`sub` empty) RS256 launch token for **Main only**, signed with Main's private key from config. Not under `/dev`; not 404 in prod; hard-wired to Main. |
| **Frontend — auto-mint** | When `ensureLaunchToken()` finds no fragment/stored token (and we're **not** embedded), call `POST /launch-token/default`, store the token (flagged as auto-minted), and proceed to Main's game list. A real fragment/stored token always wins. On a **401 of an auto-minted token**, silently **re-mint + retry** instead of routing to `/error`. Real-operator 401s still route to `/error` (unchanged). The prod token-less entry (`/chimpout/game`) redirects to the game page instead of `/error`. |

Net effect: **prod → only Main exists**; **non-prod → both Main and DEV Cassino** (DEV Cassino + its dev landing buttons unchanged, so local testing is preserved and Main's auto-mint is exercisable locally by hitting the game route with no token).

This is **data seeding + config + a thin mint endpoint + a frontend fallback** — no new game-lifecycle behaviour and **no schema/migration**.

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo / hub stays on `main`:** `feature/main-production-operator` in each affected
  repo; all work in `.worktrees/main-production-operator/<repo>`. Never `git checkout -b` in a hub.
- **Unit tests:** update/add in `ChimpOut.Tests`; report changed tests at end of implementation.
- **Integration test — WAIVED for this card** (per card author). Rationale: seeding/config + a thin
  mint endpoint + a frontend fallback introduce **no new game-lifecycle behaviour**; Main's crypto
  `bet → run → reward` path is already covered by the existing SolanaToken integration tests
  (`GameFlowTests` `…PaidCrypto…`). The ship gate should **not** expect a new integration test for
  this card. (This is an explicit, recorded exception to `constraints.md §3` / `AGENTS.md` rule 8.)
- **EF mapping:** inline in `ChimpOutDbContext.OnModelCreating`; no `IEntityTypeConfiguration`. **No
  schema change expected** (Main is seeded data, not new columns) → **no migration**.
- **Secrets/config:** **mock-only** values in committed files. Main's launch **private** signing key,
  Main's launch **public** key, and the reused `GeneralSettings.Token.TokenOwnerPrivateKey` in
  `chimpoutsettings.json` are **placeholders**; the real values are injected via the prod environment.
  Never print/commit real credentials.
- **Operator messaging:** vague errors only; never reveal contract status. Real-operator 401s stay
  generic (`/error`); the Main auto-mint path never leaks operator internals.
- **Host split:** the mint endpoint lives in `ChimpOut.Api` (sign only, no private-token-key decrypt);
  seeding runs in `ChimpOut.Consumers` at startup (as today).

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | A default **production** operator named **`Main`** is seeded, with a **fixed RFC-4122 GUID** (candidate `22222222-2222-4222-8222-222222222222`; version nibble `4` + variant `8` are required so the frontend `isValidOperatorGuid` accepts the `iss`). Contract **active**, `ContractEndDate = null` (never expires). Has its own `LaunchPublicKey`. Seeding is **idempotent + self-healing** (mirrors `EnsureDevOperator`). |
| 2 | **Per-environment seeding.** Main is seeded in **all** environments. **Production seeds only Main.** DEV Cassino stays **non-prod-gated** (never in prod, as today). **Non-prod seeds both** Main and DEV Cassino; DEV Cassino keeps all its existing default game types + dev landing buttons unchanged. The no-token auto-mint **targets Main in every environment**. |
| 3 | **Main's Solana token** is one registered `OperatorToken` seeded from **`GeneralSettings.Token`** (the same source the dev token uses), private key **encrypted at rest** via `ISecretProtector`. Mock values in repo; real values via prod env. → SolanaToken becomes available for Main. |
| 4 | **Three default game types for Main**, all **SolanaToken**, **6-char alphanumeric** (`NumbersAndLetters`), **`AllowFreeBet = false`**, `RequiredAmount = 1`, `MinimumNumberOfPlayers = 2`, duplicates allowed: (a) **free-type race**; (b) **preset "horse race"** — back 1 of **8 six-char alphanumeric runners** (placeholder codes; renamable); (c) **lottery** — **24h** draw, base `LotteryPot = 4`. |
| 5 | **Auto-mint endpoint** `POST /launch-token/default` — **production-enabled** (outside `/dev`, not 404 in prod). Mints an **anonymous Main token** (`sub` empty), signed with Main's private signing key from config, TTL = `LaunchTokens.MainTokenTtlSeconds` (mock default 3600). **Hard-wired to only ever issue for the Main operator** — no operator parameter; it resolves Main by name and mints for nobody else. The minted token is a normal RS256 launch token validated against Main's stored `LaunchPublicKey` (no middleware change). |
| 6 | **Frontend.** `useLaunch.ensureLaunchToken()`: no fragment/stored token **and not embedded** → call the mint endpoint, store as **auto-minted** (marker in `sessionStorage`), proceed. A real fragment/stored token always wins (and clears the marker). On a **401 of an auto-minted token** (standalone, non-embedded) → silently **re-mint + retry once**, not `/error`. Real-operator 401 → `/error` (unchanged). Embedded + no token → unchanged (`token-expired`, never auto-mint Main into someone else's frame). Prod token-less entry `/chimpout/game` redirects to the game page (which auto-mints Main) instead of `/error`. |
| 7 | **Integration test waived**; unit tests still required (see §11). |

---

## 4. Data model

**No entity or column changes. No migration.** Main is seeded rows in existing tables:

- `Operator` (existing) — one new seeded row (`Name = "Main"`). Fields used: `Name`, `MainPageUrl`,
  `IconUrl`, `Color` (null), `LaunchPublicKey`, `ContractStartDate`, `ContractEndDate = null`.
- `OperatorToken` (existing) — one new seeded row owned by Main, built from `GeneralSettings.Token`,
  `TokenOwnerPrivateKey` `Protect`ed via `ISecretProtector`, `Active = true`.
- `GameParameters` (existing) — three new seeded rows for Main (`PaymentMethod = SolanaToken`,
  `OperatorTokenId` = Main's token, one carrying `PresetChoices`, one carrying `LotteryParameters`).

EF mapping already covers all three entities (no change). `ChimpOutDbContext.OnModelCreating` is **not**
touched.

---

## 5. Configuration

All committed values are **mock/placeholder** (`constraints.md §1`).

### 5.1 `ChimpOut.Domain/Configs/Entities/LaunchTokenSettings.cs` — add three members

```csharp
// MOCK-only PEM used by the /launch-token/default endpoint to sign Main's launch tokens.
// Real key injected via the production environment. Distinct from the Dev* keys.
public string MainSigningPrivateKeyPem { get; set; }

// MOCK-only PEM seeded onto the Main operator's LaunchPublicKey so Main-minted tokens validate.
public string MainOperatorPublicKeyPem { get; set; }

// TTL (seconds) for an auto-minted Main token. Modest is fine — the frontend silently re-mints on
// expiry. Mock default 3600 (1h).
public int MainTokenTtlSeconds { get; set; }
```

> **Why separate Main keys (not reuse `Dev*`):** the `Dev*` keys are documented **mock-only, never
> real**, and DEV Cassino is never seeded in prod. Main needs a **real** signing key in production, so
> it must have its own config keys.

### 5.2 `ChimpOut.Common/chimpoutsettings.json` — under `GeneralSettings.LaunchTokens`

Add mock PEMs + TTL alongside the existing `ClockSkewSeconds` / `DevSigningPrivateKeyPem` /
`DevOperatorPublicKeyPem`:

```jsonc
"LaunchTokens": {
  "ClockSkewSeconds": 120,
  "DevSigningPrivateKeyPem": "-----BEGIN PRIVATE KEY-----\nMOCK...\n-----END PRIVATE KEY-----",
  "DevOperatorPublicKeyPem": "-----BEGIN PUBLIC KEY-----\nMOCK...\n-----END PUBLIC KEY-----",
  "MainSigningPrivateKeyPem": "-----BEGIN PRIVATE KEY-----\nMOCK-MAIN...\n-----END PRIVATE KEY-----",
  "MainOperatorPublicKeyPem": "-----BEGIN PUBLIC KEY-----\nMOCK-MAIN...\n-----END PUBLIC KEY-----",
  "MainTokenTtlSeconds": 3600
}
```

The `MainSigningPrivateKeyPem` and `MainOperatorPublicKeyPem` **must be an RSA key pair** (public is
the SubjectPublicKeyInfo matching the private) so that tokens signed by the endpoint validate against
Main's seeded `LaunchPublicKey`. Generate a **throwaway mock pair** for the committed file.

Main's **token** reuses the existing `GeneralSettings.Token` section (already mock). No new token
section.

### 5.3 Seeded operator branding (mock)

`MainPageUrl` / `IconUrl` are branding placeholders (e.g. `https://app.monkify.net` /
`https://app.monkify.net/icon.png`); `Color = null`. Real values can be set later — not secrets, but
keep them generic in the committed seed.

---

## 6. Backend behaviour

### 6.1 Seeding — `ChimpOut.Consumers/Program.cs`

Refactor `CreateDefaultOperatorAsync` so **Main seeds in every environment** while **DEV stays
non-prod-gated**:

```csharp
void CreateDefaultOperatorAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var context   = scope.ServiceProvider.GetRequiredService<ChimpOutDbContext>();
    var settings  = scope.ServiceProvider.GetRequiredService<GeneralSettings>();
    var cache     = scope.ServiceProvider.GetRequiredService<ICacheService>();
    var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();

    // Main is the default PRODUCTION operator — seed it in ALL environments.
    EnsureMainOperator(context, cache, MainOperatorId, MainOperatorName,
                       settings.LaunchTokens?.MainOperatorPublicKeyPem);
    EnsureMainOperatorToken(context, settings, protector);   // reuses GeneralSettings.Token

    // DEV operator + token stay dev-only (never in production).
    if (app.Environment.IsProduction())
        return;

    var devPublicKey = settings.LaunchTokens?.DevOperatorPublicKeyPem;
    EnsureDevOperator(context, cache, new Guid("11111111-1111-4111-8111-111111111111"), "DEV Cassino", devPublicKey);
    EnsureDevOperatorToken(context, settings, protector);
}
```

- Add constants near the top of the seeding region (or a small shared constants holder):
  `const string MainOperatorName = "Main";` and
  `static readonly Guid MainOperatorId = new("22222222-2222-4222-8222-222222222222");`
  (finalize the exact GUID; it **must** be RFC-4122 valid).
- **`EnsureMainOperator`** — same shape as `EnsureDevOperator` (idempotent create by `Name`, self-heal
  a missing `LaunchPublicKey`, invalidate the operator cache on patch). Seed `MainPageUrl`/`IconUrl`
  placeholders, `Color = null`, `LaunchPublicKey = MainOperatorPublicKeyPem`,
  `ContractStartDate = DateTime.UtcNow.Date`, `ContractEndDate = null`. `Id = MainOperatorId`.
- **`EnsureMainOperatorToken`** — same shape as `EnsureDevOperatorToken` but resolves the **Main**
  operator and skips if Main already has any `OperatorToken`. Build the token from
  `settings.Token` (`Protect` the private key), `Active = true`, `Label` = a production symbol
  placeholder (e.g. `"USDC"`; renamable to the real token symbol — this is the user-visible
  `tokenSymbol`).

> **Idempotency:** re-running Consumers must not duplicate Main, its token, or its game types — all
> guards key off `Name == "Main"` / existing token / existing active params, exactly like the dev path.

### 6.2 Default game types — `ChimpOut.Consumers/DefaultGameParameters.cs`

Add a Main-specific generator (all **SolanaToken**, `NumbersAndLetters`, length 6, **free bets off**):

```csharp
// Main (production default) crypto game types: a free-type race, a preset "horse race", and a
// 24h lottery — all 6-char alphanumeric, settled in Main's registered Solana token, free bets OFF.
public static List<GameParameters> GenerateMainDefaultParameters(Guid operatorTokenId) =>
[
    MainRace  ("6X Main Sprint",  "6-character alphanumeric race settled in Main's token. Connect a wallet to bet. Min 2.", operatorTokenId),
    MainPreset("6X Main Derby",   "Back 1 of 8 runners in a 6-character alphanumeric preset race, settled in Main's token. Min 2.", operatorTokenId,
               "ZEPHYR", "BOREAS", "COMET1", "BLAZE2", "SHADOW", "TEMPO3", "VOLTZ4", "NOVA05"),
    MainLottery("6X Main Millions", "6-character alphanumeric lottery drawn once a day; the pot grows all day. Min 2.", operatorTokenId, TimeSpan.FromHours(24)),
];

private static GameParameters MainBase(string name, string description, Guid operatorTokenId)
{
    var p = Base(name, description, GameCharacterType.NumbersAndLetters, 6);
    p.PaymentMethod   = PaymentMethod.SolanaToken;
    p.OperatorTokenId = operatorTokenId;
    p.AllowFreeBet    = false;                 // production
    return p;
}

private static GameParameters MainRace(string name, string description, Guid operatorTokenId)
    => MainBase(name, description, operatorTokenId);

private static GameParameters MainPreset(string name, string description, Guid operatorTokenId, params string[] choices)
{
    var p = MainBase(name, description, operatorTokenId);
    p.PresetChoices = choices.Select(c => new PresetChoice(c)).ToList();
    return p;
}

private static GameParameters MainLottery(string name, string description, Guid operatorTokenId, TimeSpan frequency)
{
    var p = MainBase(name, description, operatorTokenId);
    p.LotteryParameters = new LotteryParameters { LotteryPot = 4, Frequency = frequency };
    return p;
}
```

- The 8 preset runner codes are 6-char **alphanumeric** placeholders (valid for the `NumbersAndLetters`
  charset at length 6) — rename freely.
- `AllowFreeBet = false` overrides the `Base(...)` default of `true`.

### 6.3 Seed Main's game types — `ChimpOut.Consumers/Program.cs` (`CreateDefaultGameParametersAsync`)

Currently seeds only for `DEV Cassino`. Extend to **also** seed Main's three crypto types when Main has
no active params. Add a Main branch that mirrors the crypto-seeding block already used for dev:

```csharp
// --- Main (all environments) ---
var main = context.Operators.AsNoTracking().FirstOrDefault(x => x.Name == MainOperatorName);
if (main != null)
{
    var mainActive = gameService.GetAllActiveGameParameters(main.Id).GetAwaiter().GetResult();
    if (mainActive.Keys.Count == 0)
    {
        var mainToken = context.OperatorTokens.AsNoTracking().FirstOrDefault(x => x.OperatorId == main.Id && x.Active);
        if (mainToken != null)
        {
            var mainParams = DefaultGameParameters.GenerateMainDefaultParameters(mainToken.Id);
            foreach (var p in mainParams) p.OperatorId = main.Id;
            context.AddRange(mainParams);
            context.SaveChanges();
            cacheService.Set(CacheHelper.GenerateActiveParametersCacheSettings(main.Id),
                             mainParams.ToDictionary(x => x.Id, x => x));
        }
    }
}
```

Keep the existing DEV Cassino block (non-prod) untouched. `OpenNewGames` already opens games for
**all** active-parameter operators whose contract is active, so Main's games start automatically —
**no change** to `OpenNewGames`.

### 6.4 Mint endpoint — new `ChimpOut.Api/Controllers/LaunchController.cs`

Production-enabled controller (route does **not** start with a middleware-protected prefix, so it is
**not** token-guarded):

```csharp
[Route("launch-token")]
[Produces("application/json")]
public class LaunchController(
    GeneralSettings settings,
    ILaunchTokenService tokenService,
    ChimpOutDbContext context) : ControllerBase
{
    private const string MainOperatorName = "Main";

    /// <summary>
    /// Mint an anonymous launch token for the default production operator (Main). Available in ALL
    /// environments. Only ever mints for Main — never any other operator. Returns { token } only.
    /// </summary>
    [HttpPost("default")]
    public async Task<IActionResult> MintDefault()
    {
        var main = await context.Operators.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Name == MainOperatorName);

        if (main == null)
            return NotFound();   // vague — no reason surfaced

        var token = tokenService.Issue(
            settings.LaunchTokens.MainSigningPrivateKeyPem,
            main.Id,
            externalPlayerId: string.Empty,                     // anonymous; wallet identifies the player
            ttlSeconds: settings.LaunchTokens.MainTokenTtlSeconds);

        return Ok(new { token });
    }
}
```

- **No operator parameter** — the endpoint resolves Main by name and mints for nobody else, so it can
  never become a token oracle for real operators.
- `Issue(...)` already exists (`ILaunchTokenService`), currently used by `DevController`. Update its
  XML-doc from "dev mint only" to "dev mint and the default-operator mint" (behaviour unchanged).
- The minted token has `iss = main.Id`, empty `sub`, `exp = now + MainTokenTtlSeconds`. The middleware
  validates it against Main's seeded `LaunchPublicKey` on the next operator-scoped call — **no
  middleware change** (verified: `RequiresToken` only guards `/games`, `/operators`, `/players`,
  `/fx`).

### 6.5 What blocks today / failure modes

- **Blocks today:** prod seeds no operator; `POST /launch-token/default` does not exist; `/dev/*` mints
  are 404 in prod. This feature removes those blocks for Main only.
- **Main not resolvable** (seeding not yet run) → endpoint returns 404 (vague). Seeding is idempotent at
  Consumers startup, so this self-heals; the API and Consumers both run at boot.
- **Free bets off** → a Main bet requires wallet connect + a valid on-chain signature (SolanaToken
  path, unchanged). Amounts are token-native (token `Decimals`), never USD.

---

## 7. Frontend behaviour

### 7.1 `src/utils/launchToken.ts` — auto-mint marker

Add a per-tab marker so the 401 handler can tell an auto-minted Main token from a real operator token:

```ts
const AUTO_LAUNCH_KEY = 'chimpout.autoLaunch';

export function setAutoMintedLaunchToken(token: string): void {
  setLaunchToken(token);
  try { sessionStorage.setItem(AUTO_LAUNCH_KEY, '1'); } catch { /* in-memory only */ }
}

export function isAutoMintedLaunch(): boolean {
  try { return sessionStorage.getItem(AUTO_LAUNCH_KEY) === '1'; } catch { return false; }
}
```

And **clear** the marker in `setLaunchToken(...)` (a real fragment/stored token is not auto-minted):

```ts
export function setLaunchToken(token: string | null): void {
  launchToken = token;
  try {
    if (token) sessionStorage.setItem(STORAGE_KEY, token);
    else sessionStorage.removeItem(STORAGE_KEY);
    sessionStorage.removeItem(AUTO_LAUNCH_KEY);   // real token → not auto-minted
  } catch { /* in-memory only */ }
}
```

> Order matters: `setAutoMintedLaunchToken` calls `setLaunchToken` first (which clears the marker) and
> **then** sets the marker — so the marker ends up set only for the auto-mint path.

### 7.2 `src/services/mainService.ts`

Add a mint call and make the 401 interceptor re-mint for auto-minted tokens:

```ts
import { getLaunchToken, LAUNCH_TOKEN_HEADER, isAutoMintedLaunch, setAutoMintedLaunchToken } from '@/utils/launchToken';

const getDefaultLaunchToken = async () => {
  const { data } = await mainAPI.post<{ token: string }>('/launch-token/default', null);
  return data?.token ?? null;
};
```

Response interceptor (replace the current 401 branch):

```ts
mainAPI.interceptors.response.use(
  (response) => response,
  async (error) => {
    const status = error?.response?.status;
    const original = error?.config;
    if (status === 401) {
      const { isEmbedded, setRelaunching } = useEmbed();
      if (isEmbedded) {
        postToParent('token-expired');
        setRelaunching(true);
      } else if (isAutoMintedLaunch() && original && !original._retriedDefaultMint) {
        // Auto-minted Main token expired → silently re-mint + retry ONCE (never /error).
        const fresh = await getDefaultLaunchToken();
        if (fresh) {
          setAutoMintedLaunchToken(fresh);
          original._retriedDefaultMint = true;
          (original.headers ??= {})[LAUNCH_TOKEN_HEADER] = fresh;
          return mainAPI(original);
        }
        if (window.location.pathname !== '/error') window.location.assign('/error');
      } else if (window.location.pathname !== '/error') {
        window.location.assign('/error');   // real-operator 401 (unchanged)
      }
    }
    return Promise.reject(error);
  }
);
```

Export `getDefaultLaunchToken` on the service default export so `useLaunch` can call it.

- The `_retriedDefaultMint` flag prevents an infinite re-mint loop.
- The mint call itself returns 200 (the endpoint is public), so it never re-enters the 401 branch.

### 7.3 `src/hooks/useLaunch.ts` — auto-mint fallback

```ts
import { getLaunchToken, setLaunchToken, setAutoMintedLaunchToken, LAUNCH_TOKEN_FRAGMENT_KEY } from '@/utils/launchToken';
import { useEmbed } from '@/hooks/useEmbed';
import { mainService } from '@/services';

const ensureLaunchToken = async (): Promise<string | null> => {
  const fromFragment = readTokenFromFragment();
  if (fromFragment) {
    setLaunchToken(fromFragment);   // real operator token; clears the auto-mint marker
    stripFragment();
    return fromFragment;
  }

  const stored = getLaunchToken();
  if (stored) return stored;

  // No token at all. Standalone → mint & use the default (Main) operator token. Embedded → do NOT
  // auto-mint Main into another casino's frame; let the caller fail auth (token-expired).
  const { isEmbedded } = useEmbed();
  if (isEmbedded) return null;

  try {
    const token = await mainService.getDefaultLaunchToken();
    if (!token) return null;
    setAutoMintedLaunchToken(token);
    return token;
  } catch {
    return null;   // caller (useOperator.loadOperator) → /error
  }
};
```

`useOperator.loadOperator()` is unchanged — it already calls `ensureLaunchToken()`, decodes the
operator from `iss`, and fetches branding. For an auto-minted Main token, `iss = Main` (a valid
RFC-4122 GUID), branding loads, and the Main game list renders.

### 7.4 `src/routes.ts` — prod token-less entry no longer dead-ends

Change the production branch of `entryRoute` from redirecting to `/error` to redirecting to the game
page, so App.vue's boot runs `loadOperator()` → `ensureLaunchToken()` → auto-mints Main:

```ts
const entryRoute = devLaunchEnabled
  ? { name: 'DevLanding', path: '/chimpout/game', component: require('@/pages/DevLandingPage.vue').default }
  : { path: '/chimpout/game', redirect: '/chimpout/game/games' };   // was: redirect '/error'
```

- **Dev unchanged:** `/chimpout/game` still shows the DEV Cassino launch buttons. To exercise Main
  locally, navigate to `/chimpout/game/games` with no token → auto-mint Main.
- **Prod:** `/` → `/chimpout/game` → `/chimpout/game/games` → boot auto-mints Main → Main games.

### 7.5 Optional (dev convenience)

`src/pages/DevLandingPage.vue` **may** gain a third "Enter as Main (default)" button that calls
`mainService.getDefaultLaunchToken()` + `setAutoMintedLaunchToken()` + `loadOperator()`, purely to make
the Main flow one click in dev. **Optional, low-risk** — implement only if convenient.

### 7.6 Wallet UX

Unchanged. Main's three types are `SolanaToken`, so the existing per-game wallet gating
(`GameHeader.vue` wallet cluster, `GameConsole.vue` connected gate, `GameTerminal.vue` token-native
`formatAmount`) applies as-is.

---

## 8. Flow diagrams

### 8.1 Token-less standalone entry (production)

```mermaid
sequenceDiagram
  participant U as Visitor
  participant SPA as Frontend (App.vue / useOperator)
  participant L as useLaunch.ensureLaunchToken
  participant API as ChimpOut.Api

  U->>SPA: open / (no launch token)
  SPA->>SPA: / → /chimpout/game → /chimpout/game/games
  SPA->>L: ensureLaunchToken()
  L->>L: no fragment, no stored, not embedded
  L->>API: POST /launch-token/default
  API->>API: resolve operator "Main", Issue(mainKey, iss=Main, sub="", ttl)
  API-->>L: { token }
  L->>L: setAutoMintedLaunchToken(token)
  L-->>SPA: token (iss = Main)
  SPA->>API: GET /operators/{Main} (X-Launch-Token)
  API-->>SPA: Main branding
  SPA-->>U: Main game list
```

### 8.2 Auto-minted token expiry mid-play

```mermaid
sequenceDiagram
  participant SPA as Frontend (mainAPI interceptor)
  participant API as ChimpOut.Api
  SPA->>API: GET /games/... (expired auto-minted token)
  API-->>SPA: 401
  SPA->>SPA: isAutoMintedLaunch() && !_retriedDefaultMint
  SPA->>API: POST /launch-token/default
  API-->>SPA: { token } (fresh)
  SPA->>SPA: setAutoMintedLaunchToken(fresh), mark _retriedDefaultMint
  SPA->>API: retry original request with fresh token
  API-->>SPA: 200 (no /error)
```

---

## 9. Out of scope

- Real production token mint/sender/owner-keypair and Main's real launch private key (injected via
  env; repo holds mock placeholders only).
- Admin-portal changes for Main (Main can use the existing `/chimpout/admin` flows later).
- **Standard** (off-chain) game types for Main — all Main types are crypto for now.
- Branding/naming polish beyond `Main` (final `MainPageUrl`/`IconUrl`/token symbol).
- Any **new integration test** (explicitly waived — see §2).
- Rate-limiting the mint endpoint (anonymous, Main-only, cheap; can be added later if abused).

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/ChimpOut.Domain/Configs/Entities/LaunchTokenSettings.cs` | Add `MainSigningPrivateKeyPem`, `MainOperatorPublicKeyPem`, `MainTokenTtlSeconds`; update doc comment. |
| 2 | `src/ChimpOut.Common/chimpoutsettings.json` | Add the two mock Main PEMs (a valid RSA pair) + `MainTokenTtlSeconds` under `GeneralSettings.LaunchTokens`. Mock only. |
| 3 | `src/ChimpOut.Consumers/Program.cs` | Refactor `CreateDefaultOperatorAsync` (seed Main in all envs; DEV stays non-prod). Add `EnsureMainOperator` + `EnsureMainOperatorToken` + `MainOperatorName`/`MainOperatorId` constants. Extend `CreateDefaultGameParametersAsync` with the Main crypto-seeding branch. |
| 4 | `src/ChimpOut.Consumers/DefaultGameParameters.cs` | Add `GenerateMainDefaultParameters(Guid operatorTokenId)` + `MainBase`/`MainRace`/`MainPreset`/`MainLottery` helpers (SolanaToken, `NumbersAndLetters` len 6, `AllowFreeBet=false`). |
| 5 | `src/ChimpOut.Api/Controllers/LaunchController.cs` | **New.** `POST /launch-token/default` → mint anonymous Main token (Main-only, empty sub, prod-enabled). |
| 6 | `src/ChimpOut.Api/Launch/LaunchTokenService.cs` | Doc-comment only: `Issue`/interface note now "dev mint **and** default-operator mint". No behaviour change. |
| 7 | `src/ChimpOut.Tests/...` | New/updated unit tests (see §11). |

### 10.2 Frontend (`monkify-front`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/utils/launchToken.ts` | Add `AUTO_LAUNCH_KEY`, `setAutoMintedLaunchToken`, `isAutoMintedLaunch`; clear the marker inside `setLaunchToken`. |
| 2 | `src/services/mainService.ts` | Add `getDefaultLaunchToken()`; 401 interceptor re-mints + retries once for auto-minted tokens (else `/error` as today); export the new fn. |
| 3 | `src/hooks/useLaunch.ts` | `ensureLaunchToken()` auto-mints Main when no fragment/stored token **and not embedded**; store via `setAutoMintedLaunchToken`. |
| 4 | `src/routes.ts` | Prod `entryRoute`: redirect `/chimpout/game` → `/chimpout/game/games` (was `/error`). Dev unchanged. |
| 5 | `src/pages/DevLandingPage.vue` | **Optional** dev "Enter as Main (default)" button. |

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/main-production-operator.md` | This spec. |
| 2 | `INDEX.md` | Add the spec row (status **Ready**); update **Feature currently in flight**. |

---

## 11. Unit tests

Project: `monkify-backend/src/ChimpOut.Tests`. (Integration test **waived** — §2.)

| Test file | Test cases | Expected |
| --- | --- | --- |
| `UnitTests/Controllers/LaunchControllerTests.cs` (new) | `MintDefault` when Main is seeded | 200 `{ token }`; token `iss == MainOperatorId`; `sub` empty; `exp ≈ now + MainTokenTtlSeconds`. |
| ″ | `MintDefault` when Main is **not** seeded | 404 (vague), no token. |
| ″ | `MintDefault` **in production** environment | Still 200 (endpoint is prod-enabled) — assert it does **not** 404 for env reasons. |
| ″ | Token is signed with `MainSigningPrivateKeyPem` and validates against `MainOperatorPublicKeyPem` | `LaunchTokenService.Validate(token, MainId, MainOperatorPublicKeyPem, skew).Success == true`. |
| `UnitTests/Consumers/DefaultGameParametersTests.cs` (new or extend) | `GenerateMainDefaultParameters(tokenId)` | Returns exactly 3; all `PaymentMethod == SolanaToken`, `OperatorTokenId == tokenId`, `AllowedCharacters == NumbersAndLetters`, `ChoiceRequiredLength == 6`, `AllowFreeBet == false`; one has `PresetChoices.Count == 8`, one has `LotteryParameters` with 24h `Frequency` + `LotteryPot == 4`, one is a plain race. |
| `UnitTests/...` seeding (extend existing seeding/operator tests if present, else a focused test) | `EnsureMainOperator` idempotency | Called twice → single `Main` row; contract active, `ContractEndDate == null`, `LaunchPublicKey` set; self-heals a null key. |
| ″ | `EnsureMainOperatorToken` idempotency | One active `OperatorToken` for Main; private key stored **encrypted** (not equal to plaintext); not duplicated on re-run. |

> Follow the existing `DevControllerTests` / seeding test patterns (xUnit + Shouldly + Moq + Bogus,
> in-memory EF). Report the exact list of changed/added tests at the end of implementation.

Frontend has no unit-test harness change mandated here; verify the auto-mint + precedence + re-mint
behaviour via Manual QA (§13).

---

## 12. Acceptance criteria

Mapped 1:1 from the Trello **Acceptance Criteria** checklist:

1. Given a production environment, when `ChimpOut.Consumers` starts, then an operator named "Main"
   exists (active, no contract end date, with a `LaunchPublicKey`) and DEV Cassino is **not** seeded.
2. Given a non-production environment, when `ChimpOut.Consumers` starts, then **both** "Main" and
   "DEV Cassino" exist, and DEV Cassino retains all its existing default game types plus the dev
   landing buttons unchanged.
3. Given Main is seeded, when its default parameters are created, then Main has **exactly three**
   active SolanaToken game types — a free-type 6-char alphanumeric race, a preset "horse race" over
   6-char alphanumeric runners, and a 24h lottery — each with free bets disabled and backed by Main's
   registered token seeded from `GeneralSettings.Token`.
4. Given a user opens the app with no launch token (neither URL fragment nor sessionStorage), when the
   app loads, then it calls `POST /launch-token/default`, receives an anonymous Main token (empty
   `sub`), stores it, and lands on Main's game list — never on `/error`.
5. Given a valid operator launch token is present in the URL fragment or sessionStorage, when the app
   loads, then it uses that token and does **not** auto-mint a Main token.
6. Given an auto-minted Main token expires or returns 401 mid-play, when the next request fails, then
   the frontend silently re-mints a fresh Main token and retries instead of routing to `/error`.
7. Given a real operator's launch token returns 401 (expired/invalid/inactive), when it fails, then the
   app still routes to `/error` with a vague message and never reveals contract status (unchanged).
8. Given `POST /launch-token/default` is called in any environment including production, when it runs,
   then it mints a token only for the Main operator (never any other operator), with an empty `sub` and
   a normal TTL matching real launch tokens.
9. Given a Main SolanaToken game, when a player places a bet, then wallet connect + a valid on-chain
   signature are required (no free bets) and all amounts render token-native (token decimals), never
   converted to USD.

---

## 13. Manual QA (local)

1. `scripts/setup_local_environment.bat` → Consumers seeds **Main** + **DEV Cassino** (non-prod).
   Confirm one `Main` operator, one active `OperatorToken` for Main, and three active Main
   `GameParameters` (race / preset / 24h lottery), all SolanaToken.
2. In a fresh tab, open `/chimpout/game/games` **with no token** → the app calls
   `POST /launch-token/default`, lands on **Main**'s game list (not `/error`). DevTools →
   `sessionStorage` shows `chimpout.launchToken` + `chimpout.autoLaunch=1`.
3. Open `/chimpout/game` (dev) → DEV Cassino launch buttons still work (regression).
4. Launch with a real `#launch_token=<jwt>` fragment → uses that token; `chimpout.autoLaunch` is
   **absent** (real operator). (AC #5)
5. Connect a wallet, place a bet on a Main type → requires signature, amounts render token-native. (AC #9)
6. Force-expire the auto-minted token (short `MainTokenTtlSeconds`, or clear/replace it) and trigger a
   request → the app silently re-mints and continues; no `/error`. (AC #6)

---

## 14. Trello / pipeline

- **Source card:** [Main Prd cassino (#60)](https://trello.com/c/bdkTZJOL/60-main-prd-cassino) —
  list **Specs created** after this skill; **Implementation** section on the card carries branch +
  worktree + spec path.
- **Next:** `/monkify-implement-feature Main Prd cassino` to build it (backend seeding + endpoint +
  unit tests, frontend auto-mint) in the worktrees; then `/monkify-ship-feature Main Prd cassino` to
  sync the KB and merge `feature/main-production-operator` → `main` in each repo.
- **Ship note:** integration test is **waived** for this card (recorded in §2 + on the card) — the ship
  gate should not block on a new integration test.

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-07-15 | Initial spec from refined card #60 (decisions from in-thread refinement + code exploration of seeding, launch-token service, middleware, and the frontend launch/operator flow). |

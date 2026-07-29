# SpaceStroker — "Remove Operators" — Implementation Spec

> **Status: Ready for implementation** — refined via Trello card
> [Remove the entire concept of operators from the system.](https://trello.com/c/TWf2oXxi) (2026-07-29).
> Repos affected: `monkify-backend`, `monkify-front`, and this doc + `deploy/` in `monkify-meta`.
>
> **Purpose:** SpaceStroker is built as a multi-tenant B2B platform, but only one operator ("Main") has
> ever existed. This spec deletes the operator concept outright — the `Operator`/`OperatorToken`
> tables, the signed launch-token auth on every player call, the unused `Standard` settlement path,
> the per-operator RG hook, and the split money ledger. **A new `GeneralSettings:Operator` config
> section becomes the operator:** onboarding another one means deploying the app again with a
> different `spacestrokersettings.json`, not inserting a row.

---

## 1. Summary

197 of 367 backend `.cs` files and 33 frontend source files mention "operator". The concept reaches
into request auth, player identity, settlement, caching, seeding and the admin portal. This card
removes it everywhere and collapses what is left.

| Area | Today | After |
| --- | --- | --- |
| Tenancy | `Operator` row owns every `GameParameters`; contract window gates play | No operator; one deployment = one operator, configured |
| Player auth | `X-Launch-Token` (RS256, `iss`=operator, `sub`=player) on `/games`, `/players`, `/operators`, `/fx` | **No auth.** Player identity = the connected wallet, passed per call |
| Admin auth | `X-Admin-Token` validated against `Operator.LaunchPublicKey` (DB) | `X-Admin-Token` validated against a **config** public key; no `iss` check |
| Player entity | `OperatorPlayer` keyed `(OperatorId, ExternalPlayerId)` | `Player` keyed on `ExternalPlayerId` (the wallet) |
| Settlement | `PaymentMethod` ∈ {`Standard`, `SolanaToken`} behind a strategy + resolver | Solana only, called directly. Enum, strategy and resolver deleted |
| Money ledger | `TransactionLog` **+** `CryptoTransactionLog` (two tables, a FK, a join) | **One** `TransactionLog` with a nullable `BetId` |
| Solana token | `OperatorToken` row, private key AES-encrypted at rest | `GeneralSettings:Operator:Token`, key from Secret Manager |
| Default game types | Hardcoded in `DefaultGameParameters.cs` | `GeneralSettings:Operator:DefaultGames`, seeded create-only |
| Prefs write | Launch-token gated, wallet in body unverified | **ed25519 wallet signature** over a purpose-scoped nonce |

The database is **greenfield** — nothing in production needs preserving — so the migration may drop
and recreate freely, with no backfill, dedupe or rehearsal.

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo:** `feature/remove-operators` in `monkify-backend`, `monkify-front`,
  `monkify-meta`, each in `.worktrees/remove-operators/<repo>`. Hubs stay clean on `main`.
- **Unit tests:** update/add in `SpaceStroker.Tests`; **report changed tests** at the end.
- **Integration tests:** this rewrites game flow ⇒ `SpaceStroker.Tests.Integration` must be updated.
  Level is **`Feature`** ⇒ a **mandatory happy-path integration test** is required.
- **EF mapping:** inline in `SpaceStrokerDbContext.OnModelCreating`; no `IEntityTypeConfiguration`.
- **Migrations:** under `SpaceStroker.Infrastructure/Migrations/`; applied only by `SpaceStroker.Consumers`.
- **Secrets/config:** **mock-only** values in committed files. The admin private key and the token
  owner private key in `spacestrokersettings.json` stay obvious placeholders. Real values live in
  `monkify-meta/deploy/secrets.<env>.json` (unversioned) → Secret Manager. **Never** print, copy or
  commit a real key.
- **§8 host split:** the API stays stateless and does not migrate; money/typer work stays in
  `SpaceStroker.Consumers`.
- **⚠️ `constraints.md` §6 (vague operator messaging) becomes obsolete** — there are no operator
  contract states left to conceal. Remove or rewrite that rule at ship time. Generic 401/404 handling
  elsewhere is unaffected.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **Full removal**, not a single hardcoded operator. Drop `Operator` + `OperatorToken`; every `OperatorId` column, scope filter, cache key and contract check goes. Also remove config for all non-crypto stuff. |
| 2 | **"Removing the game parameters class"** = `DefaultGameParameters.cs` only. The `GameParameters` **entity stays** — admin CRUD, `Game`/`Bet` FKs, stats and history are untouched. |
| 3 | **`PaymentMethod.Standard` removed** — no operators means nobody to debit. Deletes the operator wallet API and its config. |
| 4 | **`PaymentMethod` enum removed entirely**, along with `IPaymentMethodService`/`IPaymentMethodResolver`. Solana is called directly. |
| 5 | **One ledger.** `CryptoTransactionLog` merges into `TransactionLog`, which gains a **nullable** `BetId`. Never 1:N today, so the merge is lossless. |
| 6 | **Token snapshot columns kept** (`MintAddress`, `SenderAccount`, `Decimals`) — identical on every row today, but a redeploy against a different mint must not rewrite what historical rows settled in. |
| 7 | **Placement rows typed `Debit`** (not a new `Placement` value). `BetDomainService` already never subtracts `Debit`. Must be pinned by a test. |
| 8 | **`ExternalTransactionReference` → `Signature`** everywhere, as a naming convention. |
| 9 | **Player endpoints go public.** Launch token, middleware, `ILaunchContext`, `LaunchController` and the SPA mint/boot path deleted. |
| 10 | **Admin token stays**, keyed from `GeneralSettings:Operator`, no `iss`/operator check. **`POST /dev/admin-token` re-added** (non-production-gated). |
| 11 | **`OperatorPlayer` → `Player`**, wallet-keyed, `Currency` dropped. |
| 12 | **RG pre-bet hook removed entirely.** First-party self-exclusion stays. |
| 13 | **Iframe embedding kept** — it is operator-free in code. Only the embedded `401 → token-expired` branch is removed. |
| 14 | **Branding removed** — no `GET /operators/{id}`, no runtime theme override. Explicitly reconsidered against the deploy-per-operator plan and still removed. |
| 15 | **Migration is greenfield.** Drop/recreate; no backfill. |
| 16 | **`GeneralSettings:Operator`** holds `Token` (the whole current `Token` block), `DefaultGames`, and the admin RS256 keypair. Top-level `Token`, `LaunchTokens`, `OperatorWallets`, `OperatorRgChecks`, `Fx` deleted. |
| 17 | **Section name stays `Operator`** — deliberately. Future operators are onboarded by deploying with a different settings file, so the section *is* the operator. |
| 18 | **Seeding is create-only by name.** A restart never resurrects an admin-deactivated type or mutates a type under a live round. The environment free-bet self-heal survives. |
| 19 | **`ISecretProtector` deleted.** Encrypting a config value with a key in the same config bought nothing once no user can type a private key in. |
| 20 | **`PATCH /players/preferences` requires an ed25519 wallet signature**, reusing a **generalised purpose-keyed nonce**. `GET` stays open. |

---

## 4. Data model

### 4.1 Entities deleted

| File | Action |
| --- | --- |
| `SpaceStroker.Domain/Games/Entities/Operator.cs` | **Delete** |
| `SpaceStroker.Domain/Games/Entities/OperatorToken.cs` | **Delete** |
| `SpaceStroker.Domain/Bets/Entities/CryptoTransactionLog.cs` | **Delete** (merged into `TransactionLog`) |
| `SpaceStroker.Domain/Games/ValueObjects/PaymentMethod.cs` | **Delete** |
| `SpaceStroker.Domain/Games/ValueObjects/SolanaTokenDescriptor.cs` | **Keep**, but built from config (see §5.3) |

### 4.2 `OperatorPlayer` → `Player`

Rename the file to `SpaceStroker.Domain/Games/Entities/Player.cs` and the type to `Player`.

```csharp
public class Player : TableEntity
{
    public string ExternalPlayerId { get; set; }   // the wallet address
    public string? DisplayName { get; set; }
    public DateTime LastSeenAt { get; set; }

    // Self-exclusion — unchanged semantics. SelfExcludedAt is the FLAG; SelfExcludedUntil NULL WITH
    // SelfExcludedAt SET MEANS PERMANENT.
    public DateTime? SelfExcludedAt { get; set; }
    public DateTime? SelfExcludedUntil { get; set; }

    public ICollection<Bet> Bets { get; set; }

    public bool IsSelfExcluded(DateTime now)
        => SelfExcludedAt != null && (SelfExcludedUntil == null || SelfExcludedUntil > now);
}
```

Dropped: `OperatorId`, `Operator`, **`Currency`** (display currency was Standard-only).

### 4.3 `GameParameters`

Drop `OperatorId`, `Operator`, `PaymentMethod`, `OperatorTokenId`, `OperatorToken`. Everything else —
including `PresetChoices`, `LotteryParameters`, `AllowFreeBet`, `Active` — is unchanged.

### 4.4 `Bet`

- Drop `PaymentMethod`.
- Rename `ExternalTransactionReference` → **`Signature`** (keep the index — the admin
  refund-by-signature lookup uses it).
- Rename `OperatorPlayerId` → **`PlayerId`**, `OperatorPlayer` → `Player`.

### 4.5 The unified `TransactionLog`

```csharp
public class TransactionLog : TableEntity
{
    public TransactionLog() { }

    public TransactionLog(Guid? betId, TransactionLogType type, decimal amount, string signature,
        string wallet, string mintAddress, string senderAccount, long rawTokenAmount, int decimals,
        string? reason, string? relatedSignature = null)
    { /* assign */ }

    // NULLABLE: a refund for a bet rejected BEFORE it was ever persisted still gets a ledger row.
    // This is what today's split forces into a special case — CryptoTransactionLog allows a null
    // BetId, TransactionLog does not, so WriteLedger skips the generic ledger entirely for that path.
    public Guid? BetId { get; set; }
    public Bet? Bet { get; set; }

    public TransactionLogType Type { get; set; }   // Debit (placement) | Credit | Refund
    public decimal Amount { get; set; }            // token-native decimal

    public string Signature { get; set; }          // on-chain signature OF THIS movement
    // The signature of the movement this one reverses. Null for placements and credits. Indexed:
    // it is the idempotency key for a refund whose bet was never persisted.
    public string? RelatedSignature { get; set; }

    public string Wallet { get; set; }
    public string MintAddress { get; set; }        // snapshot — see decision #6
    public string SenderAccount { get; set; }
    public long RawTokenAmount { get; set; }       // Amount * 10^Decimals
    public int Decimals { get; set; }
    public string? Reason { get; set; }
}
```

`TransactionLogType` (`Credit | Debit | Refund`) is **unchanged**. Placement rows are `Debit`.

**Row shapes after the merge:**

| Movement | `BetId` | `Type` | `Signature` | `RelatedSignature` |
| --- | --- | --- | --- | --- |
| Bet placement | set | `Debit` | the player's payment | null |
| Reward credit | set | `Credit` | the payout tx | null |
| Refund (bet persisted) | set | `Refund` | the refund tx | the original payment |
| Refund (bet never persisted) | **null** | `Refund` | the refund tx | the original payment |

### 4.6 EF configuration (inline, `OnModelCreating`)

Delete the `Operator`, `OperatorToken` and `CryptoTransactionLog` blocks and their DbSets. Rename
`DbSet<OperatorPlayer> OperatorPlayers` → `DbSet<Player> Players`. Then:

```csharp
modelBuilder.Entity<Player>(builder =>
{
    builder.HasKey(x => x.Id);
    builder.Property(x => x.ExternalPlayerId).IsRequired().HasMaxLength(50);
    builder.Property(x => x.DisplayName).HasMaxLength(100);
    builder.Property(x => x.LastSeenAt).IsRequired();
    builder.Property(x => x.SelfExcludedAt);
    builder.Property(x => x.SelfExcludedUntil);
    // Was (OperatorId, ExternalPlayerId) unique — the wallet alone is now the identity.
    builder.HasIndex(x => x.ExternalPlayerId).IsUnique();
});

modelBuilder.Entity<TransactionLog>(builder =>
{
    builder.HasKey(x => x.Id);
    builder.Property(x => x.Type).IsRequired();
    builder.Property(x => x.Amount).HasPrecision(18, 9).IsRequired();
    builder.Property(x => x.Signature).IsRequired().HasMaxLength(100);
    builder.Property(x => x.RelatedSignature).HasMaxLength(100);
    builder.Property(x => x.Wallet).IsRequired().HasMaxLength(100);
    builder.Property(x => x.MintAddress).IsRequired().HasMaxLength(100);
    builder.Property(x => x.SenderAccount).IsRequired().HasMaxLength(100);
    builder.Property(x => x.RawTokenAmount).IsRequired();
    builder.Property(x => x.Decimals).IsRequired();
    builder.Property(x => x.Reason).HasMaxLength(300);

    builder.HasIndex(x => x.Signature);
    builder.HasIndex(x => x.RelatedSignature);
    builder.HasIndex(x => new { x.BetId, x.Type });
});
```

In the `GameParameters` block: drop the `Operator` and `OperatorToken` relationships, the
`PaymentMethod` property line and `HasIndex(x => x.OperatorId)`. Keep `HasIndex(x => x.Active)`.

In the `Bet` block: drop `Property(x => x.PaymentMethod)`, rename
`Property(x => x.ExternalTransactionReference)` → `Property(x => x.Signature)`, repoint the player
relationship, and — **critically** — make the ledger FK optional:

```csharp
builder.HasOne(x => x.Player).WithMany(x => x.Bets).HasForeignKey(x => x.PlayerId);
// BetId is nullable on TransactionLog now.
builder.HasMany(x => x.TransactionLogs).WithOne(x => x.Bet).HasForeignKey(x => x.BetId).IsRequired(false);
builder.HasIndex(x => x.Signature);
builder.HasIndex(x => new { x.GameId, x.Choice });
builder.HasIndex(x => new { x.GameId, x.PlayerId, x.Choice });
builder.HasIndex(x => new { x.GameId, x.ExclusiveChoice }).IsUnique();  // unchanged
```

> The `(GameId, PlayerId, Choice)` index is the renamed `(GameId, OperatorPlayerId, Choice)` — it
> still serves both per-round bet-limit queries, so **no new index is needed** (see
> `features/fix-bets-logic` notes in `features/games.md` §9).

### 4.7 Migration

Greenfield. One migration:

```bash
dotnet ef migrations add RemoveOperators \
  --project src/SpaceStroker.Infrastructure \
  --startup-project src/SpaceStroker.Consumers
```

It drops `Operators`, `OperatorTokens`, `CryptoTransactionLogs`; renames `OperatorPlayers` →
`Players`; drops the operator/payment-method columns; renames the two `Bet` columns; and adds the
crypto columns to `TransactionLogs` with a nullable `BetId`. **Never hand-edit
`SpaceStrokerDbContextModelSnapshot.cs`** — regenerate it.

### 4.8 ER diagram (after)

```mermaid
erDiagram
    GameParameters ||--o{ Game : "has rounds"
    GameParameters ||--o{ PresetChoice : "offers"
    GameParameters ||--o| LotteryParameters : "may be a lottery"
    Game ||--o{ Bet : "collects"
    Player ||--o{ Bet : "places"
    Bet ||--o{ TransactionLog : "money movements"
    Bet ||--o{ BetStatusLog : "audit"
    Game ||--o{ GameStatusLog : "audit"
    TransactionLog {
        guid BetId "NULLABLE — bet-less refund"
        int Type "Debit|Credit|Refund"
        string Signature
        string RelatedSignature
    }
```

---

## 5. Configuration

### 5.1 The new `GeneralSettings:Operator` section

`GeneralSettings` loses `Token`, `LaunchTokens`, `OperatorWallets`, `OperatorRgChecks` and `Fx`, and
gains `Operator`:

```csharp
public class GeneralSettings
{
    public GameSettings Games { get; set; }
    public OperatorSettings Operator { get; set; }      // NEW — replaces Token + LaunchTokens
    public WorkerSettings Workers { get; set; }
    public PollySettings Polly { get; set; }
    public ChatSettings Chat { get; set; } = new();
    public CorsSettings Cors { get; set; } = new();
}
```

New `SpaceStroker.Domain/Configs/Entities/OperatorSettings.cs`:

```csharp
public class OperatorSettings
{
    public TokenSettings Token { get; set; }
    public AdminTokenSettings AdminToken { get; set; }
    public List<DefaultGameSettings> DefaultGames { get; set; } = new();
}

public class AdminTokenSettings
{
    public int ClockSkewSeconds { get; set; }        // was LaunchTokens.ClockSkewSeconds (120)
    public string PublicKeyPem { get; set; }         // validates X-Admin-Token
    public string PrivateKeyPem { get; set; }        // MOCK in committed config; used by /dev/admin-token
    public int TtlSeconds { get; set; }              // was MainTokenTtlSeconds (3600)
}

public class DefaultGameSettings
{
    public string Name { get; set; }
    public string Description { get; set; }
    public GameCharacterType AllowedCharacters { get; set; }
    public int ChoiceRequiredLength { get; set; }
    public decimal RequiredAmount { get; set; }
    public int MinimumNumberOfPlayers { get; set; }
    public bool AcceptDuplicatedCharacters { get; set; }
    public List<string> PresetChoices { get; set; } = new();     // empty => free-text race
    public decimal? LotteryPot { get; set; }                     // non-null => lottery
    public TimeSpan? LotteryFrequency { get; set; }
}
```

`TokenSettings` is unchanged as a class (it just moves under `Operator`), keeping `ClusterUrl`,
`FallbackClusterUrl`, `MintAddress`, `SenderAccount`, `TokenOwnerPublicKey`, `TokenOwnerPrivateKey`,
`Decimals`, `CommissionPercentage`, `BalanceCacheSeconds`, `ActiveMintsCacheSeconds`.

### 5.2 `spacestrokersettings.json` (mock values only)

```jsonc
"GeneralSettings": {
  "Games": { /* unchanged */ },
  "Operator": {
    "Token": {
      "ClusterUrl": "https://api.devnet.solana.com/",
      "FallbackClusterUrl": "https://api.devnet.solana.com/",
      "MintAddress": "MOCK_MINT_ADDRESS",
      "SenderAccount": "MOCK_SENDER_ACCOUNT",
      "TokenOwnerPublicKey": "MOCK_TOKEN_OWNER_PUBLIC_KEY",
      "TokenOwnerPrivateKey": "MOCK_TOKEN_OWNER_PRIVATE_KEY",
      "Decimals": 6,
      "CommissionPercentage": 0.5,
      "BalanceCacheSeconds": 10,
      "ActiveMintsCacheSeconds": 300
    },
    "AdminToken": {
      "ClockSkewSeconds": 120,
      "PublicKeyPem": "-----BEGIN PUBLIC KEY-----\nMOCK\n-----END PUBLIC KEY-----\n",
      "PrivateKeyPem": "-----BEGIN PRIVATE KEY-----\nMOCK\n-----END PRIVATE KEY-----\n",
      "TtlSeconds": 3600
    },
    "DefaultGames": [ /* Cosmic Sprint, Beast Mode, Space Jackpot — see §5.4 */ ]
  },
  "Workers": { /* unchanged */ },
  "Polly": { /* unchanged */ },
  "Chat": { /* unchanged */ },
  "Cors": { /* unchanged */ }
}
```

> ⚠️ The committed file currently holds a **real-looking** devnet mint/keypair and real PEMs. Replace
> them with obvious placeholders as above (constraints §1). The local Docker stack and the test
> suites must keep working with mock values — they already do, because the integration harness swaps
> in `FakeSolanaService`.

### 5.3 Token descriptor from config

`SolanaTokenDescriptor` is currently built from a `GameParameters.OperatorToken` row. It is now built
from `settings.Operator.Token` — a single static source. Provide one helper (e.g.
`OperatorSettings.ToDescriptor(bool includePrivateKey)`) so the API (validation, public data only)
and Consumers (transfers, needs the private key) share it.

### 5.4 Default games

Port the three Main types out of `DefaultGameParameters.cs` verbatim into `DefaultGames`:

| Name | Shape | Length | Amount | Notes |
| --- | --- | --- | --- | --- |
| `Cosmic Sprint` | free-text race | 6 letters | 1 | |
| `Beast Mode` | preset, 8 beasts | 6 | 1 | `rabbit, monkey, jaguar, falcon, cougar, turtle, donkey, pigeon` |
| `Space Jackpot` | lottery | 6 | 2 | `LotteryPot` 0, frequency 24h |

`AllowFreeBet` is **not** a config field: it stays derived as `!IsProduction()` (see §6.7).

### 5.5 Deploy (`monkify-meta/deploy`)

Config keys move, so the deploy stack must follow:

| File | Change |
| --- | --- |
| `deploy/terraform/workload/main.tf` (secret-id list, ~L15-16) | Drop `GeneralSettings-LaunchTokens-MainSigningPrivateKeyPem` and `GeneralSettings-SecretProtection-Key`; add `GeneralSettings-Operator-AdminToken-PrivateKeyPem` |
| `deploy/terraform/workload/main.tf` (env→secret map, ~L30-34) | `GeneralSettings__Token__*` → `GeneralSettings__Operator__Token__*`; drop the `LaunchTokens`, `SecretProtection` and `Fx__ApiKey` entries; add the admin key |
| `deploy/secrets.example.json` | Same key renames; drop `SecretProtection`, `LaunchTokens`, `Fx` |
| `deploy/secrets.<env>.json` (**unversioned, real values**) | The operator must re-key these by hand. **Do not read, print or commit these files.** |
| `deploy/terraform/workload/<env>.tfvars.json` | Drop `GeneralSettings:LaunchTokens:MainOperatorPublicKeyPem`; add `GeneralSettings:Operator:AdminToken:PublicKeyPem` |
| `ChimpOut.Deploy` / `SpaceStroker.Deploy` | Update any hard-coded key names in the secret-sync step |

---

## 6. Backend behaviour

### 6.1 Request auth

**Deleted:** `LaunchTokenValidationMiddleware`, `ILaunchContext`/`LaunchContext`, `LaunchController`
(`POST /launch-token/default`), `ILaunchTokenService.Issue`, `TryReadIssuer`,
`LaunchTokenValidationResult.OperatorId`/`ExternalPlayerId`, and the `DevController` launch-token
endpoint. Remove the middleware registration from `SpaceStroker.Api/Program.cs`.

`/games/*`, `/players/*`, `/fx/*` (the last is deleted anyway) become **unauthenticated**.
`/admin/*` keeps `AdminTokenValidationMiddleware`.

**`AdminTokenValidationMiddleware`** loses its `IOperatorService` dependency and its issuer lookup:

```csharp
var token = context.Request.Headers[HeaderName].ToString();
if (string.IsNullOrWhiteSpace(token)) { await Reject(...); return; }

var admin = settings.Operator.AdminToken;
var result = tokenService.ValidateAdmin(token, admin.PublicKeyPem, admin.ClockSkewSeconds);

if (!result.Success) { await Reject(...); return; }
if (!string.Equals(result.Scope, LaunchTokenService.AdminScope, StringComparison.Ordinal))
{ await Reject(context, correlationId, "Token is not admin-scoped."); return; }

await _next(context);
```

`ILaunchTokenService` shrinks to two members — rename the file/type to `AdminTokenService` for
honesty:

```csharp
LaunchTokenValidationResult ValidateAdmin(string token, string publicKeyPem, int clockSkewSeconds);
string IssueAdmin(string privateKeyPem, int ttlSeconds);
```

`ValidateAdmin` sets `ValidateIssuer = false` (there is no issuer to check). `IssueAdmin` no longer
takes an `operatorId` and writes no `iss` claim.

`IAdminContext` loses `OperatorId`. If nothing else remains on it, delete it and drop the
registration; every admin handler stops scoping by operator.

Rejections keep today's shape: a single generic **401** with the real reason logged server-side.

### 6.2 `RegisterBetHandler` — the main surgery

Constructor drops `IOperatorService`, `IPaymentMethodResolver` and `IPreBetCheckResolver`, and gains
`ISolanaService` (or a thin `ITokenPaymentService`) plus `GeneralSettings` (already injected).

Ordered changes inside `HandleRequest`:

1. **Delete** the `GetActiveOperatorById` lookup and its `GameNotValidForBets` guard.
2. `method`/`_isSolana` disappear — every game is Solana. `isMalformedBet` simplifies to
   *paid bet with no signature* **or** *zero-amount bet carrying a signature*.
3. **Identity** is the wallet only:
   ```csharp
   string? resolvedExternalId = !string.IsNullOrWhiteSpace(wallet) ? wallet : null;
   if (!_isFreeBet && resolvedExternalId == null)
       Messaging.ReturnValidationFailureMessage(BetValidationResult.InvalidAmount.StringValueOf());
   ```
4. `UpsertOperatorPlayerAsync` → `UpsertPlayerAsync(externalPlayerId, displayName, ct)`, keyed on
   `ExternalPlayerId` alone; the `Currency` default is **removed**.
5. **Self-exclusion check is unchanged** and stays exactly where it is — before
   `ValidateSolanaPayment`, and it still applies to free bets.
6. `ValidateSolanaPayment`: keep the reuse guard, retargeted at the unified ledger —
   ```csharp
   var signatureHasBeenUsed = await Context.TransactionLogs.AnyAsync(x => x.Signature == signature)
       || await Context.GameBets.AnyAsync(x => x.Signature == signature);
   ```
   then call the Solana validation directly instead of `_paymentResolver.Resolve(...).Debit(...)`.
7. `ValidateBetParameters`: **delete** the operator block —
   ```csharp
   // DELETED: betOperator / ContractIsActive / OperatorId mismatch
   if (betGame is null || !Game.GameAcceptingBets.Contains(betGame.Status))
       await RefundInvalidBet(ErrorMessages.GameNotValidForBets);
   ```
   ⚠️ **Keep the null and status checks** — only the operator conditions go. Rename
   `OperatorPlayerId` → `PlayerId` in the duplicate-bet and per-round-limit queries; the limit rules
   themselves (`OneBetPerPresetRound`, `SecondBetNeedsAnotherPlayer`) and the
   `!_isFreeBet && PlayerId != null` gate are **unchanged**.
8. **Delete** `RunPreBetCheck`, `MapDenyReasonToMessage`, `MapWalletErrorToMessage` and
   `DebitStandard`.
9. `RefundInvalidBet`: the guard drops its payment-method half —
   ```csharp
   if (BetExtensions.IsFreeBet(_bet)) { /* log, reject, return */ }
   ```
10. `WritePlacementCryptoLog` → `WritePlacementLedgerRow`: writes a **`TransactionLog`** with
    `Type = Debit`, the token snapshot from config, and `RelatedSignature = null`. The
    `if (token == null) return;` early-out disappears (config always has a token).

### 6.3 Settlement — one path, no strategy

Delete `Services/Payments/*` (`IPaymentMethodService`, `PaymentResult` method dispatch,
`StandardPaymentMethodService`, `SolanaTokenPaymentMethodService`, `IPaymentMethodResolver`,
`PaymentMethodResolver`), `Services/OperatorWallets/*` and `Services/PreBetChecks/*`. Fold
`SolanaTokenPaymentMethodService`'s `Credit`/`Refund`/`WriteLedger` into a single
`Services/Solana/TokenSettlementService.cs` (or straight into the consumers), calling `ISolanaService`
with a config-built descriptor.

`WriteLedger` collapses to one insert:

```csharp
private async Task WriteLedger(Bet bet, TransactionLogType type, decimal amount,
    BetTransactionAmountResult transferAmount, string signature, string wallet,
    string? reason, string? relatedSignature = null)
{
    var token = _settings.Operator.Token;
    // A refund for a bet that was never persisted arrives on a detached Bet with an empty Id.
    // BetId is nullable now, so this is an ordinary row — no skipped ledger write.
    var betId = bet.Id == Guid.Empty ? (Guid?)null : bet.Id;

    await _context.TransactionLogs.AddAsync(new TransactionLog(
        betId, type, amount, signature, wallet, token.MintAddress, token.SenderAccount,
        (long)transferAmount.ValueInTokens, token.Decimals, reason, relatedSignature));
    await _context.SaveChangesAsync();
}
```

`RewardGameConsumer` and `RefundInvalidBetConsumer` drop the resolver dispatch and call this
directly. `RefundInvalidBetConsumer.RefundWithoutBet` keeps its `RelatedSignature` idempotency —
it now simply writes a normal row.

**`BetDomainService` must keep ignoring `Debit`.** Reward subtraction reads `Credit`; the
already-refunded guard reads `Refund`. Placement rows are `Debit` and must never enter either sum.

### 6.4 Admin surface

**Deleted:** `Handlers/Admin/OperatorTokens/*`, `Handlers/Admin/PaymentMethods/*`, and the
`AdminController` routes `GET/POST /admin/operator-tokens`, `POST /admin/operator-tokens/{id}/deactivate`,
`GET /admin/payment-methods`.

**Kept:** `GET /admin/game-types`, `POST /admin/game-types`,
`POST /admin/game-types/{id}/deactivate`, `POST /admin/bets/refund`.

- `CreateGameTypeHandler` stops forcing an `OperatorId` and stops accepting `PaymentMethod` /
  `OperatorTokenId`; `GameParametersValidation` drops its method/token rules. `AllowFreeBet` stays
  hidden (always `false` for admin-created types).
- `GetOperatorGameTypesHandler` → `GetGameTypesHandler`, no operator filter.
- `DeactivateGameTypeHandler`: the lottery-pot transfer target rule drops "same operator / same
  method / same token" and becomes **"any active lottery"**. The `CarriedExtra > 0` blocking
  behaviour is otherwise unchanged.
- `RefundBetHandler` looks up `Bet.Signature`, with no operator scoping.
- `Contracts/Admin/`: delete `OperatorTokenDto`, `OperatorTokenRequestBody`,
  `PaymentMethodAvailabilityDto`; drop `PaymentMethod`/`OperatorTokenId` from `GameTypeRequestBody`
  and `GameTypeAdminDto`.

### 6.5 `DevController`

- **Delete** `POST /dev/launch-token`.
- **Re-add** `POST /dev/admin-token` — non-production-gated (404 in Production), signing with
  `settings.Operator.AdminToken.PrivateKeyPem` via `IssueAdmin`, returning `{ token }`.

### 6.6 Players, prefs and the generalised nonce

**Nonce.** Replace `GenerateSelfExclusionNonceCacheSettings(Guid operatorId, string wallet)` with:

```csharp
// Purpose-scoped, single-use wallet-ownership challenge. The purpose is part of the key AND of the
// signed message, so a nonce minted for one action cannot be replayed on another.
public static CacheSettings GenerateWalletNonceCacheSettings(string purpose, string wallet)
    => new($"WalletNonce:{purpose}:{wallet}", TimeSpan.FromMinutes(5));
```

Purposes: `self-exclusion`, `preferences`. `GetSelfExclusionNonceHandler` becomes
`GetWalletNonceHandler`, taking the purpose. Endpoint: `GET /players/nonce?wallet=&purpose=`
(keep the old self-exclusion route working by defaulting `purpose`, or update the frontend — the
frontend is in scope, so **update both** and use one route).

**`SetSelfExclusionHandler`** — unchanged logic; it now resolves the `Player` by wallet alone, uses
purpose `self-exclusion`, and drops the `Currency` default on the created row.

**`UpdatePlayerPreferencesHandler`** — now signature-gated:

1. Require `Wallet`, `Nonce`, `Signature` in the body (one vague failure for any of them, mirroring
   self-exclusion).
2. Look up the nonce at `WalletNonce:preferences:{wallet}` and compare.
3. Verify the ed25519 signature over the canonical message.
4. **Only then** consume the nonce (a mistyped signature must not burn the challenge).
5. Upsert the `Player` by `ExternalPlayerId`, set `DisplayName`, `LastSeenAt`.
6. **Delete** `ValidateCurrency` and the `Currency` field entirely.

Canonical message — keep byte-identical with the frontend:

```csharp
public static string BuildMessage(string wallet, string displayName, string nonce)
    => string.Join("\n",
        "Space Stroker display name",
        $"Wallet: {wallet}",
        $"Display name: {displayName}",
        $"Nonce: {nonce}");
```

**`GetPlayerPreferencesHandler`** stays open (reads are harmless): `GET /players/preferences?wallet=`,
returning `{ externalPlayerId, displayName }` — `currency` removed.

**Deleted:** `FxController`, `Handlers/Fx/*`, `Services/Fx/*`, `Contracts/Fx/FxRatesDto.cs`,
`FxSettings`, `Domain/Fx/SupportedCurrencies`, and `GenerateFxRatesCacheSettings`.

### 6.7 Seeding (`SpaceStroker.Consumers/Program.cs`)

- **Delete** `CreateDefaultOperatorAsync`, `EnsureMainOperator`, `EnsureMainOperatorToken` and the
  `ISecretProtector` usage.
- `CreateDefaultGameParametersAsync` becomes operator-free and config-driven:
  - For each `settings.Operator.DefaultGames` entry, if **no `GameParameters` row with that name
    exists**, create it (create-only, decision #18) with `AllowFreeBet = !IsProduction()`.
  - Keep the **free-bet self-heal** via `ExecuteUpdate` (the context is `NoTracking`, so
    load-mutate-`SaveChanges` silently persists nothing), matching on the config names.
  - Keep the cache bust — now the global `ActiveParameters` key.
- `OpenNewGames` **drops the `ContractIsActive` skip**; everything else (live-parameter dedupe,
  lottery carry via `LotteryPotCarry.NextCarriedExtra`) is unchanged.

### 6.8 Caching

| Key today | After |
| --- | --- |
| `ActiveParameters:{operatorId}` | `ActiveParameters` (single global key) |
| `Operator:{operatorId}` | **deleted** |
| `Operator:Admin:{operatorId}` | **deleted** |
| `GlobalStats:{operatorId}` | `GlobalStats` |
| `GamesHistory:{operatorId}:{parametersId}:{page}:{size}` | `GamesHistory:{parametersId}:{page}:{size}` |
| `SelfExclusionNonce:{operatorId}:{wallet}` | `WalletNonce:{purpose}:{wallet}` |
| `Fx:Rates:USD` | **deleted** |

`GameService.GetAllActiveGameParameters(Guid)` and `GetAllActiveGameParametersForAllOperators()`
collapse into a single `GetAllActiveGameParameters()`.

---

## 7. Frontend behaviour

### 7.1 Deleted

`hooks/useOperator.ts`, `hooks/useLaunch.ts`, `hooks/useDisplayCurrency.ts`,
`utils/launchToken.ts`, `utils/operatorRoute.ts`, `components/admin/OperatorTokenManager.vue`.

### 7.2 Boot path

`App.vue` no longer mints or loads an operator. The first call becomes `GET /games/active-types`.
`useBootRecovery` is **kept** but repointed at that call — the retry/backoff behaviour that fixed the
2026-07-26 incident stays, it just wraps a different first request. `applyOperatorTheme` is removed;
`--color-secondary` keeps its static value from `colors.css`, so nothing changes visually.

`/error` is **kept** for terminal failures, but the blanket `401 → /error` interceptor branch is
removed (player endpoints no longer 401).

### 7.3 `services/mainService.ts`

Drop the `X-Launch-Token` request interceptor, the `LAUNCH_TOKEN_HEADER` import, the auto-mint
helpers, `getDefaultLaunchToken`, `getOperator`, and the FX calls. The `singleFlight` dedupe
allow-list must drop entries for removed endpoints and keep the rest untouched.
`services/adminService.ts` (`X-Admin-Token`) is **unchanged**.

### 7.4 Bets and rendering

- `useGame.ts` `bet()`: delete the `paymentMethod` branch — every bet is the SolanaToken path
  (`sendToken` → post signature + wallet). Free bets stay amount 0 with an empty signature.
- `GameHeader.vue`: delete the `hasOperatorContext` gate on the settings gear (always shown) and the
  `showWallet = paymentMethod === SolanaToken` condition (always shown).
- `GameConsole.vue`: the connected-wallet bet gate now applies unconditionally (it was
  SolanaToken-only).
- `formatAmount` in `GameTerminal.vue`, `GameTypeCard.vue`, `GameStatsPanel.vue` and the chat/history
  surfaces collapses to token-native only — `${amount} ${tokenSymbol}` at the token's decimals. No
  `formatUsd`, no display-currency lookup.
- "Mine only" (games history) and own-bet identity (`useGame.currentPlayerId`) key off the **wallet
  alone**; the token-`sub` fallback is gone.

### 7.5 Customization modal gains a signature step

`PlayerCustomizationModal.vue` save flow, mirroring `useSelfExclusion.ts`:

1. `GET /players/nonce?wallet=…&purpose=preferences`
2. Build the canonical message (byte-identical with §6.6 — keep the two in sync)
3. `signMessage(...)` → base64
4. `PATCH /players/preferences { wallet, displayName, nonce, signature }`

The mobile deeplink stash pattern from `useSelfExclusion.ts` applies here too if the wallet is a
mobile in-app browser. The currency selector is **removed**.

### 7.6 Admin UI

`GameTypeForm.vue` drops the payment-method and token dropdowns; `GameTypeList.vue` drops the
method/token column; `OperatorTokenManager.vue` is deleted and unmounted from `GameTypeManager.vue`.
`adminService.ts` drops `getOperatorTokens`, `registerOperatorToken`, `deactivateOperatorToken`,
`getPaymentMethods`.

### 7.7 Types

`types/index.ts` drops `PaymentMethod`, `OperatorBranding`, `OperatorTokenDto`/`OperatorTokenBody`,
`PaymentMethodAvailability`, `FxRates`, and the currency fields on the prefs types. `AppliedBet`
keeps `externalPlayerId`.

> **The ledger merge and the `Signature` rename have zero frontend surface** — `grep` confirms 0
> references to `externalTransactionReference` in `monkify-front/src`.

---

## 8. Flow diagram — a paid bet, after

```mermaid
sequenceDiagram
    participant P as Player (wallet)
    participant SPA as monkify-front
    participant API as SpaceStroker.Api
    participant CH as Solana chain
    participant DB as Postgres

    SPA->>API: GET /games/active-types (no token)
    API-->>SPA: active game types
    P->>CH: transferChecked (stake)
    CH-->>SPA: signature
    SPA->>API: POST /games/{id}/bets {wallet, signature, choice, amount}
    API->>DB: Player upsert (by wallet)
    API->>DB: self-excluded? -> reject/refund
    API->>DB: signature reused? (TransactionLogs.Signature OR GameBets.Signature)
    API->>CH: validate the on-chain payment
    API->>DB: INSERT Bet
    API->>DB: INSERT TransactionLog (Type=Debit, placement)
    API-->>SPA: BetDto
```

---

## 9. Out of scope

- `monkify-meta/sales/` and `monkify-meta/pitch/` — B2B operator pitch material. **Follow-up card needed.**
- `monkify-lp` — the landing page never mentions operators.
- Deleting the iframe/embed stack; re-adding branding or any white-label configuration.
- Any new player-facing capability. This card removes; it does not add.

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

| # | File | Change |
| --- | --- | --- |
| 1 | `Domain/Games/Entities/Operator.cs` | **Delete** |
| 2 | `Domain/Games/Entities/OperatorToken.cs` | **Delete** |
| 3 | `Domain/Games/Entities/OperatorPlayer.cs` | **Rename** → `Player.cs`; drop `OperatorId`, `Currency` |
| 4 | `Domain/Games/Entities/GameParameters.cs` | Drop operator/token/method members |
| 5 | `Domain/Bets/Entities/Bet.cs` | Drop `PaymentMethod`; rename `ExternalTransactionReference`→`Signature`, `OperatorPlayerId`→`PlayerId` |
| 6 | `Domain/Bets/Entities/TransactionLog.cs` | **Rewrite** — merged shape, nullable `BetId` (§4.5) |
| 7 | `Domain/Bets/Entities/CryptoTransactionLog.cs` | **Delete** |
| 8 | `Domain/Games/ValueObjects/PaymentMethod.cs` | **Delete** |
| 9 | `Domain/Bets/Services/BetExtensions.cs` | `GetWalletAddress` reads `Player`; `IsFreeBet` unchanged |
| 10 | `Domain/Bets/Services/BetDomainService.cs` | Keep `Credit`/`Refund` type filters; verify `Debit` never counted |
| 11 | `Domain/Games/Services/GameParametersValidation.cs` | Drop method/token rules |
| 12 | `Domain/Configs/Entities/GeneralSettings.cs` | Replace `Token`/`LaunchTokens`/`OperatorWallets`/`OperatorRgChecks`/`Fx` with `Operator` |
| 13 | `Domain/Configs/Entities/OperatorSettings.cs` | **New** (§5.1) |
| 14 | `Domain/Configs/Entities/{LaunchToken,OperatorWallet,OperatorRgCheck,SecretProtection,Fx}Settings.cs` | **Delete** |
| 15 | `Domain/Configs/Entities/TokenSettings.cs` | Keep; now nested under `Operator` |
| 16 | `Domain/Fx/*` | **Delete** |
| 17 | `Infrastructure/Data/Context/SpaceStrokerDbContext.cs` | DbSets + mapping per §4.6 |
| 18 | `Infrastructure/Migrations/*_RemoveOperators.cs` | **New** (§4.7) |
| 19 | `Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs` | Main surgery (§6.2) |
| 20 | `Infrastructure/Handlers/Games/RegisterBet/RegisterBetRequest.cs` | Drop `OperatorId`, `ExternalPlayerId` |
| 21 | `Infrastructure/Handlers/Games/*` (active-types, active-game, history, stats, verification) | Drop operator params/filters/cache keys |
| 22 | `Infrastructure/Handlers/Operators/*` | **Delete** |
| 23 | `Infrastructure/Handlers/Admin/OperatorTokens/*`, `Admin/PaymentMethods/*` | **Delete** |
| 24 | `Infrastructure/Handlers/Admin/{CreateGameType,GetOperatorGameTypes,DeactivateGameType,RefundBet}/*` | De-scope; rename `GetOperatorGameTypes`→`GetGameTypes` |
| 25 | `Infrastructure/Handlers/Players/*` | Wallet identity; nonce generalisation; prefs signature; drop currency (§6.6) |
| 26 | `Infrastructure/Handlers/Fx/*` | **Delete** |
| 27 | `Infrastructure/Services/{OperatorWallets,PreBetChecks,Encryption,Operators,Payments,Fx}/*` | **Delete** |
| 28 | `Infrastructure/Services/Solana/{ISolanaService,SolanaService}.cs` | Descriptor from config; ledger write folded in |
| 29 | `Infrastructure/Services/Games/GameService.cs` | Collapse the two active-parameter methods; drop operator scoping |
| 30 | `Infrastructure/Services/Cache/CacheHelper.cs` | De-scope keys (§6.8) |
| 31 | `Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs` | Direct Solana call; unified ledger |
| 32 | `Infrastructure/Consumers/RefundInvalidBet/RefundInvalidBetConsumer.cs` | Direct Solana call; bet-less refund writes a normal row |
| 33 | `Infrastructure/Consumers/BetPlaced/BetPlacedConsumer.cs` | `OperatorPlayerId`→`PlayerId` in the distinct-player count |
| 34 | `Infrastructure/Contracts/Games/{GameParametersDto,BetDto,ActiveGameDto,GameHistoryDto}.cs` | Drop method/token/operator fields |
| 35 | `Infrastructure/Contracts/Games/OperatorDto.cs` | **Delete** |
| 36 | `Infrastructure/Contracts/Admin/{OperatorTokenDto,OperatorTokenRequestBody,PaymentMethodAvailabilityDto}.cs` | **Delete** |
| 37 | `Infrastructure/Contracts/Admin/{GameTypeRequestBody,GameTypeAdminDto}.cs` | Drop `PaymentMethod`/`OperatorTokenId` |
| 38 | `Infrastructure/Contracts/Fx/FxRatesDto.cs` | **Delete** |
| 39 | `Infrastructure/DependencyInjection.cs` | Drop deleted registrations |
| 40 | `Api/Middleware/LaunchTokenValidationMiddleware.cs` | **Delete** |
| 41 | `Api/Middleware/AdminTokenValidationMiddleware.cs` | Config key; no issuer/operator lookup (§6.1) |
| 42 | `Api/Launch/LaunchTokenService.cs` | **Rename** → `AdminTokenService`; two members only |
| 43 | `Api/Controllers/LaunchController.cs`, `OperatorsController.cs`, `FxController.cs` | **Delete** |
| 44 | `Api/Controllers/{Games,Players,Admin,Dev}Controller.cs` | Drop launch context; wallet params; dev endpoints (§6.5) |
| 45 | `Api/Program.cs` | Drop launch middleware registration |
| 46 | `Consumers/Program.cs` | Operator-free, config-driven seeding (§6.7) |
| 47 | `Consumers/DefaultGameParameters.cs` | **Delete** |
| 48 | `Common/spacestrokersettings.json` | New `Operator` section, **mock values only** (§5.2) |

### 10.2 Frontend (`monkify-front`)

| # | File | Change |
| --- | --- | --- |
| 1 | `hooks/{useOperator,useLaunch,useDisplayCurrency}.ts` | **Delete** |
| 2 | `utils/{launchToken,operatorRoute}.ts` | **Delete** |
| 3 | `components/admin/OperatorTokenManager.vue` | **Delete** |
| 4 | `services/mainService.ts` | Drop interceptor, mint, branding, FX (§7.3) |
| 5 | `services/adminService.ts` | Drop operator-token + payment-method calls |
| 6 | `App.vue` | Drop operator/launch boot; repoint `useBootRecovery` |
| 7 | `hooks/useBootRecovery.ts` | Wrap the first data call instead of the mint |
| 8 | `hooks/useGame.ts` | Drop the method branch; wallet-only identity |
| 9 | `hooks/useGameChat.ts` | Wallet-only display-name resolution |
| 10 | `hooks/useAdmin.ts`, `utils/adminToken.ts` | **Keep unchanged** |
| 11 | `components/GameHeader.vue` | Always show gear + wallet cluster |
| 12 | `components/game/{GameTerminal,GameConsole,GameStatsPanel}.vue` | Token-native amounts; unconditional wallet gate |
| 13 | `components/game-selection/GameTypeCard.vue` | Token-native amounts |
| 14 | `components/header/PlayerCustomizationModal.vue` | Signature step; remove currency selector (§7.5) |
| 15 | `components/header/DisplayNamePrompt.vue` | Same signature step |
| 16 | `components/admin/{GameTypeForm,GameTypeList,GameTypeManager}.vue` | Drop method/token UI |
| 17 | `pages/GameHistoryPage.vue`, `pages/GameStatsPage.vue` | Token-native amounts; wallet-only "mine" |
| 18 | `types/index.ts` | Drop operator/method/currency types (§7.7) |
| 19 | `routes.ts` | Drop any operator-derived guard |

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/remove-operators.md` | **This spec** |
| 2 | `INDEX.md` | Spec row + "Feature currently in flight" |
| 3 | `deploy/terraform/workload/main.tf` | Secret-id list + env map (§5.5) |
| 4 | `deploy/secrets.example.json`, `deploy/terraform/workload/<env>.tfvars.json` | Key renames |
| 5 | `constraints.md` §6 | **Remove/rewrite at ship** — obsolete |
| 6 | `features/*.md`, `glossary.md`, `architecture.md` | KB rewrite **at ship** (`/monkify-ship-feature`) |

---

## 11. Unit tests

Project: `monkify-backend/src/SpaceStroker.Tests`. Run via `scripts/run_tests.bat`.

| Test file | Test cases | Expected |
| --- | --- | --- |
| `UnitTests/Handlers/RegisterBetHandlerTests.cs` | Paid bet with wallet + signature; paid bet with no wallet; free bet with no wallet; reused signature; self-excluded wallet; duplicate bet; `OneBetPerPresetRound`; `SecondBetNeedsAnotherPlayer`; free bet exempt from both limits | Rewritten without operator/method mocks; limits unchanged |
| `UnitTests/Handlers/RegisterBetHandlerTests.cs` (new) | A placement writes exactly one `TransactionLog` with `Type = Debit`, the config token snapshot and `RelatedSignature = null` | Ledger merge |
| `UnitTests/Domain/BetDomainServiceTests.cs` | **A bet with a `Debit` placement row is not credited less / not treated as refunded** | **Pins decision #7** |
| `UnitTests/Consumers/RefundInvalidBetConsumerTests.cs` | Refund for a persisted bet; **refund for a bet that was never persisted writes a row with `BetId = null` + `RelatedSignature`**; retry is idempotent on `RelatedSignature` | Bet-less refund is now an ordinary row |
| `UnitTests/Background/RewardWinnersHandlerTests.cs` | Winner credited; commission applied; ledger row `Type = Credit` | No resolver |
| `UnitTests/Handlers/UpdatePlayerPreferencesHandlerTests.cs` | Valid signature; wrong signature; unknown/expired nonce; **nonce minted for `self-exclusion` rejected**; nonce not consumed on failure; display-name length bounds | New signature gate |
| `UnitTests/Handlers/SetSelfExclusionHandlerTests.cs` | Existing cases + **a `preferences` nonce is rejected** | Purpose separation |
| `UnitTests/Handlers/AdminGameTypeHandlerTests.cs` | Create/list/deactivate with no operator scoping; lottery transfer target = any active lottery | De-scoped |
| `UnitTests/Launch/AdminTokenServiceTests.cs` | Valid admin token; missing `scope`; wrong key; expired; **no `iss` required** | Renamed from `LaunchTokenServiceTests` |
| `UnitTests/Controllers/DevControllerTests.cs` | `/dev/admin-token` returns a token outside Production, 404 in Production; `/dev/launch-token` gone | Rewritten |
| `UnitTests/Services/OperatorWallets/*`, `Services/PreBetChecks*`, `PaymentMethodResolverTests`, `AesSecretProtectorTests`, `OperatorTokenHandlerTests`, `OperatorServiceTests` | **Delete** | Subjects removed |
| `UnitTests/Shared/BetTestFactory.cs` | Build `Player` + unified ledger rows | Shared fixture |

### 11.1 Integration tests (`SpaceStroker.Tests.Integration`)

| File | Action |
| --- | --- |
| `Harness/IntegrationTestFixture.cs` | **Rewrite** — no operators, no `OperatorToken`, no `ISecretProtector`; seed game types from config; keep `FakeSolanaService` |
| `GameFlowTests.cs` | **The mandatory happy path:** no token → `GET /games/active-types` → paid SolanaToken bet → round runs → winner credited. Drop the Standard variants |
| `RefundSafeguardsTests.cs` | Follow the ledger merge; assert the bet-less refund row |
| `SelfExclusionFlowTests.cs`, `PlayerPreferencesTests.cs` | Wallet-only identity; prefs gains the signature flow |
| `OperatorAdminTests.cs` | **Rename** → `AdminPortalTests`; config-keyed admin token |
| `OperatorPlayerIdentityTests.cs` | **Rename** → `PlayerIdentityTests`; wallet-keyed |
| `OperatorSeedingTests.cs` | **Rewrite** → `DefaultGameSeedingTests` (config-driven, create-only) |
| `LaunchTokenValidationTests.cs`, `OperatorBrandingTests.cs`, `OperatorWalletRefundTests.cs`, `RgPreBetCheckTests.cs` | **Delete** |
| `GameStatsFlowTests.cs`, `GamesHistoryFlowTests.cs`, `GlobalStatsFlowTests.cs`, `ChoiceExclusivityFlowTests.cs`, `LotteryPotStackingFlowTests.cs`, `GameChatTests.cs`, `ProvablyFairFlowTests.cs`, `RateLimitKeyingTests.cs` | Drop token headers / operator setup; behaviour unchanged |

---

## 12. Acceptance criteria

From the Trello **Acceptance Criteria** checklist (26 items):

1. Given a visitor with no token of any kind, when they open `/space-stroker/game`, then the lobby loads the active game types with no `X-Launch-Token` header sent and no mint request made.
2. Given the API is running, when any request hits `/games/*`, `/players/*` or `/admin/*`, then no `LaunchTokenValidationMiddleware` runs, `/launch-token/default` and `/dev/launch-token` return 404, and the `X-Launch-Token` header is ignored everywhere.
3. Given a connected wallet holding the configured token, when the player places a paid bet on a seeded game type and the round runs to completion, then the bet settles on-chain and the winner is credited — with no operator row, no launch token and no `PaymentMethod` anywhere in the path (happy-path integration test).
4. Given a fresh database, when `SpaceStroker.Consumers` starts, then the migration creates a schema with no `Operators`, `OperatorTokens` or `OperatorPlayers` tables, a `Players` table keyed by a unique `ExternalPlayerId`, and no `OperatorId` or `PaymentMethod` column on `GameParameters`, `GameBets` or `TransactionLog`.
5. Given an empty `GameParameters` table and default games defined under `GeneralSettings:Operator:DefaultGames`, when Consumers starts, then one active game type is created per config entry with its name, description, required amount, charset, choice length and preset/lottery settings taken from config.
6. Given a game type row that already exists by name and has been edited or deactivated via the admin portal, when Consumers restarts, then the row is left untouched and is not resurrected or overwritten from config (only the environment free-bet self-heal may change it).
7. Given the Solana token is configured only under `GeneralSettings:Operator:Token`, when a reward or refund transfer runs in Consumers, then the owner keypair is read straight from config with no `ISecretProtector` decrypt and no `OperatorToken` lookup.
8. Given an admin token signed with the private key from `GeneralSettings:Operator` and carrying `scope=admin`, when it is sent as `X-Admin-Token`, then the admin portal loads and can list, create and deactivate game types and refund a bet — with no operator id in the token and no operator scoping applied.
9. Given a token with a missing, expired or wrong-key signature, or one without `scope=admin`, when it is sent to any `/admin` endpoint, then the request is rejected with a vague 401 and the real reason is logged server-side only.
10. Given a non-production environment, when `POST /dev/admin-token` is called, then it returns an admin-scoped token signed with the configured private key; given Production, then it returns 404.
11. Given a player editing their display name, when they save, then the SPA requests a purpose-scoped nonce, prompts the wallet to sign it, and `PATCH /players/preferences` succeeds only when the ed25519 signature verifies against the supplied wallet.
12. Given a nonce minted for the preferences purpose, when it is replayed against the self-exclusion endpoint (or vice versa), then it is rejected; and given a nonce already consumed or expired, when it is reused, then it is rejected.
13. Given a wallet that has self-excluded, when it attempts a paid or free bet after the operator removal, then the bet is blocked exactly as before — resolved by wallet alone, with the extend-only and never-lifted-early rules intact.
14. Given the game screen, lobby, stats, chat and games-history pages, when any amount is rendered, then it is shown token-native using the configured symbol and decimals — with no `formatUsd` path, no `/fx/rates` call and no currency selector in the customization modal.
15. Given the games-history page with "mine only" enabled, when the player has a connected wallet, then their own rounds are returned by matching the wallet to `Player.ExternalPlayerId` with no token `sub` fallback and no operator filter.
16. Given the game is loaded inside an iframe, when it boots, then the postMessage lifecycle and the wallet-bridge popup work unchanged and no `token-expired` / relaunching state can be reached (that branch is gone).
17. Given a repo-wide search of `monkify-backend` and `monkify-front` after the change, when grepping case-insensitively for "operator", "launchtoken", "paymentmethod", "prebetcheck" and "secretprotect", then the only surviving hits are the `GeneralSettings:Operator` config section, its bound settings class, and the admin token plumbing.
18. Given the deploy stack in `monkify-meta/deploy`, when a deploy is prepared, then `secrets.<env>.json`, the hard-coded secret-id lists in `terraform/workload/main.tf` and `SpaceStroker.Deploy` carry the new `GeneralSettings:Operator` keys and no longer reference `LaunchTokens`, `SecretProtection`, `Fx` or `OperatorWallets`.
19. Given the full test suites, when `scripts/run_tests.bat` and the `SpaceStroker.Tests.Integration` suite are run, then both are green — with `OperatorSeedingTests` and `OperatorWalletRefundTests` removed or rewritten, `IntegrationTestFixture` seeding no operators, and the changed/added unit tests reported at the end.
20. Given the admin portal loads with a valid admin token, when the game-type manager is shown, then there is no Solana-token registration tab and no payment-method or token selector on the create form, and `GET /admin/operator-tokens` and `GET /admin/payment-methods` return 404.
21. Given a fresh database, when the migration runs, then there is no `CryptoTransactionLogs` table and a single `TransactionLogs` table carries `Type`, `Amount`, `Signature`, `RelatedSignature`, `Wallet`, `MintAddress`, `SenderAccount`, `RawTokenAmount`, `Decimals`, `Reason` and a **NULLABLE** `BetId`.
22. Given a paid bet that is placed, wins and is credited, when the ledger is inspected, then exactly one `Debit` row (the placement) and one `Credit` row exist for that bet, each carrying its own on-chain signature and the token snapshot — with no second table and no join.
23. Given a bet rejected before it was ever persisted, when its stake is refunded, then a `TransactionLog` row is written with `BetId = null`, `Type = Refund` and `RelatedSignature` set to the original payment — where today no generic ledger row is written at all.
24. Given a bet whose placement wrote a `Debit` row, when `BetDomainService` computes the reward and the already-refunded guard, then the `Debit` row is ignored — a placement is never subtracted as a payout or counted as a refund (dedicated unit test).
25. Given the admin refund endpoint, when `POST /admin/bets/refund` is called with a `paymentSignature`, then the bet is located by the renamed indexed `Bet.Signature` column and the endpoint's request/response contract is unchanged.
26. Given a repo-wide search after the change, when grepping for "ExternalTransactionReference" and "CryptoTransactionLog", then the only hits are inside historical EF migration files — no live code, DTO or test refers to either name.

---

## 13. Manual QA

1. `scripts/setup_local_environment.bat` → the stack boots, migrations apply, three game types seed.
2. Open `http://localhost:8080/space-stroker/game` in a **fresh** private window — the lobby loads with
   no `#launch_token` and no `/launch-token/default` call in the network tab.
3. Connect a devnet wallet holding the seeded token; place a paid bet on **Cosmic Sprint**; let the
   round run; confirm the payout arrives and the header balance refreshes.
4. In the DB: `select "Type", "BetId", "Signature" from "TransactionLogs" order by "CreatedDate";` —
   a `Debit` placement row and a `Credit` payout row, no `CryptoTransactionLogs` table.
5. Change your display name in the gear modal — the wallet prompts for a signature; a rejected
   signature leaves the name unchanged and does **not** burn the nonce (retry works).
6. `POST /dev/admin-token`, open `/space-stroker/admin#admin_token=<jwt>` — game types list, create
   and deactivate all work; there is no token tab.
7. Free bets: outside Production, place a 0-amount bet with no wallet connected — accepted, and no
   ledger row is written.

---

## 14. Trello / pipeline

- **Source card:** [Remove the entire concept of operators from the system.](https://trello.com/c/TWf2oXxi)
  — list **Specs created**; the **Implementation** section on the card carries branch, worktree, spec and level.
- **Level:** `Feature` ⇒ mandatory happy-path integration test + a `features/remove-operators.md` doc at ship.
- **Next:** `/monkify-implement-feature Remove the entire concept of operators from the system.`
- **Then:** `/monkify-ship-feature` — which must also **remove or rewrite `constraints.md` §6** and
  rewrite the operator-* KB docs.

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-07-29 | Initial spec from refined card #128 (decisions + ledger-merge addendum) |

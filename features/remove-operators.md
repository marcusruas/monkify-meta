# Feature — Remove Operators (the deployment is the operator)

> Status: **shipped.** Authoritative spec: `../specs/remove-operators.md`
> (merged via `feature/remove-operators` on 2026-07-30, Trello #128).

## What it does

Space Stroker was built as a multi-tenant B2B platform: an `Operator` row owned every game type, held
an RS256 launch keypair, carried a contract window and branding, and (via `OperatorToken`) owned the
Solana token. Exactly one operator ever existed, so all of it was ceremony carried by **197 of 367
backend source files**.

The concept is gone. **The `GeneralSettings:Operator` config section is the operator** — onboarding
another one means deploying the app again with a different `spacestrokersettings.json`, not inserting
a row. For players the visible change is that nothing is gated any more: the lobby, a round, and a bet
all work from a browser that sends no token of any kind. Identity is the wallet that signed the
transfer.

Two shipped subsystems were deleted outright rather than made operator-less — the off-chain
`Standard` payment method (there is nobody left to debit) and the per-operator responsible-gaming
pre-bet hook (it resolved off an `Operator` and no adapter was ever configured). First-party
per-wallet self-exclusion is untouched and remains the RG control that actually runs.

## Core rules

### Configuration is the tenant

- Everything token-, game- and admin-related reads from **`GeneralSettings:Operator`**, which holds
  three things: **`Token`** (cluster URLs, symbol, mint, sender, owner keypair, decimals,
  `CommissionPercentage`, cache TTLs), **`AdminToken`** (RS256 public + private key PEM, TTL, clock
  skew) and **`DefaultGames`** (the seeded game-type definitions).
- Top-level `Token`, `LaunchTokens`, `OperatorWallets`, `OperatorRgChecks`, `SecretProtection` and
  `Fx` no longer exist.
- **Game-type seeding is create-only by name.** A configured type is inserted only when no row with
  that name exists; an existing row is never rewritten. A restart therefore cannot resurrect an
  admin-deactivated type or mutate a type under a live round. The one exception is the pre-existing
  free-bet self-heal, which forces `AllowFreeBet = !IsProduction()` on the seeded types.
- Three types ship in `DefaultGames`: **Cosmic Sprint** (6-char race), **Beast Mode** (preset,
  8 beasts) and **Space Jackpot** (24h lottery).

### Nothing on the player surface is authenticated

- `X-Launch-Token`, `LaunchTokenValidationMiddleware`, `ILaunchContext`, `LaunchController` and the
  SPA's mint/boot path are deleted. `/games/*` and `/players/*` are public.
- A paid bet still costs real money — it requires a confirmed on-chain signature — so the open
  endpoint is not free to spam. Free bets stay non-production-only.
- **The admin token survives**, keyed from config. `AdminTokenValidationMiddleware` validates
  `scope=admin` against `GeneralSettings:Operator:AdminToken:PublicKeyPem` with **no `iss` and no
  contract check**. `POST /dev/admin-token` is back (non-production only) for the local workflow.

### Identity is the wallet

- `OperatorPlayer` → **`Player`** (`Players` table), uniquely indexed on `ExternalPlayerId` — the
  wallet address. `Bet.OperatorPlayerId` → `Bet.PlayerId`.
- A free bet carries no wallet and stays **anonymous** (`PlayerId` null).
- `Player.Currency` is gone with the `Standard` method: every game settles in one configured token,
  so there is nothing to convert.
- **`PATCH /players/preferences` requires an ed25519 signature.** A wallet address is public, so an
  unsigned write would let anyone rename anyone. The caller fetches a single-use nonce from
  `GET /players/nonce?wallet=…&purpose=preferences`, signs the canonical message, and sends
  `{wallet, displayName, nonce, signature}`. `GET /players/preferences` stays open.
- The nonce is **purpose-scoped** (`WalletNonce:{purpose}:{wallet}`, Redis, 5 min, single use), so a
  challenge minted for `preferences` cannot be replayed against `self-exclusion` or vice versa.

### One ledger

- **`CryptoTransactionLog` is dropped.** `TransactionLog` is now THE ledger: one row per money
  movement, carrying `Type`, token-native `Amount`, `Signature`, `RelatedSignature`, `Wallet`,
  `MintAddress`, `SenderAccount`, `RawTokenAmount`, `Decimals` and `Reason`.
- The merge is lossless because the relationship was never one-to-many, and once `Standard` was gone
  the Solana path was the only writer of the generic table — making it a strict subset of the crypto
  one. Three columns were being stored twice (`ExternalTransactionReference`/`Signature` were literally
  the same value passed to both, plus `Amount` and `Reason`).
- **`BetId` is nullable.** A stake paid on-chain for a bet that was rejected before `InsertBet` now
  writes an ordinary `Refund` row with no bet. Idempotency for that row keys on `RelatedSignature` —
  the payment being reversed — because the row's own `Signature` is the *refund's* transaction.
- **A placement is a `Debit`.** The stake leaving the player is what `Debit` already meant, and
  `BetDomainService` never subtracted `Debit` rows, so reward and already-refunded math is unchanged.
  A placement row must never be read as a payout.
- `ExternalTransactionReference` is renamed **`Signature`** everywhere it survived — on the ledger, on
  `Bet` (index preserved; the admin refund-by-signature lookup uses it) and on `PaymentResult`. The
  wire contract already said `paymentSignature`, so the column now matches its public name.

### Deleted with the concept

- `PaymentMethod` / `CurrencyType` enums and their columns; `IPaymentMethodService` + resolver;
  `IOperatorWalletService` + resolver + mock; `IPreBetCheckService` + resolver; `ISecretProtector` +
  `AesSecretProtector`; `IOperatorService`; the FX rates table and `GET /fx/rates`.
- Encryption at rest bought nothing once no user could type a private key into the admin portal — the
  key lived in the same config as the value protecting it.
- Operator branding: `OperatorsController`, `GET /operators/{id}`, the `Operator:{id}` cache key,
  `useOperator.ts` and `applyOperatorTheme`. `--color-secondary` keeps its static value from
  `colors.css`, so nothing changes visually.
- Redis keys `ActiveParameters:{operatorId}` and the global-stats key lose their suffix and become
  single global keys.

### Migration

Greenfield. Nothing in production was worth preserving, so the migration drops and recreates — no
backfill, no dedupe, no rehearsal. No in-flight round survives the deploy; `CloseOngoingGames` handles
the boot, and a carried lottery `CarriedExtra` is destroyed by the reset.

## Key code touchpoints

**Backend (`monkify-backend`)**

- Config — `src/SpaceStroker.Common/spacestrokersettings.json` → `GeneralSettings:Operator`;
  entities under `src/SpaceStroker.Domain/Configs/Entities/`.
- Ledger — `src/SpaceStroker.Domain/Bets/Entities/TransactionLog.cs` (nullable `BetId`,
  `RelatedSignature`, on-chain columns).
- Identity — `src/SpaceStroker.Domain/Games/Entities/Player.cs`.
- Bet path — `src/SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/`,
  `src/SpaceStroker.Infrastructure/Services/Games/GameService.cs` (`InsertBet` detaches and
  **restores** both `bet.Game` and `bet.Player`; restoring only `Game` left every placement row with a
  blank `Wallet`).
- Wallet proof — `Handlers/Players/GetWalletNonce/`, `Handlers/Players/UpdatePlayerPreferences/`
  (`BuildMessage` is the canonical message the SPA duplicates byte-for-byte).
- Admin — `src/SpaceStroker.Api/Admin/AdminTokenService.cs`, `AdminTokenValidationMiddleware`,
  `Controllers/AdminController.cs`, dev mint in `Controllers/DevController.cs`.
- Seeding — `src/SpaceStroker.Consumers/Program.cs` (config-driven, create-only by name).
- Cache — `Services/Cache/CacheHelper.cs` (`GenerateActiveParametersCacheSettings`,
  `GenerateGlobalStatsCacheSettings`, `GenerateWalletNonceCacheSettings`).

**Frontend (`monkify-front`)**

- Deleted — `hooks/useOperator.ts`, `hooks/useLaunch.ts`, `hooks/useDisplayCurrency.ts`,
  `utils/launchToken.ts`, `utils/operatorRoute.ts`, `components/admin/OperatorTokenManager.vue`.
- `services/mainService.ts` — no `X-Launch-Token` interceptor, no blanket `401 → /error`, no FX calls.
- `hooks/usePlayerPreferences.ts` — builds and signs the same canonical message as the handler.
- `components/PlayerCustomizationModal.vue` — currency selector gone, wallet-signature step on save.
- `formatAmount` is token-native only (`GameTerminal`, `GameTypeCard`, `GameStatsPanel`, chat).

**Tests**

- `src/SpaceStroker.Tests.Integration/OperatorlessFlowTests.cs` — the happy path (lobby → paid bet →
  payout on a client with no auth header) plus the bet-less refund complex flow.
- `src/SpaceStroker.Tests.Integration/Harness/IntegrationGameParameters.cs` — the flat type catalogue
  that replaced per-operator seeding.

## Gotchas

- **The suite catalogue must be seeded create-only by name, not "skip if the table is non-empty".**
  The Consumers host has already seeded its `DefaultGames` types by the time the integration fixture
  runs, so an any-rows guard silently skips every test type.
- **Global stats and active parameters are no longer isolated per test.** Their Redis keys are single
  and global, so tests assert deltas and invalidate explicitly.
- Anything that reads the ledger must tolerate a **null `BetId`** — a `Contains` filter over bet ids
  has to exclude nulls first or it will not translate.

## Superseded documents

`features/operators.md`, `launch-token-validation.md`, `payment-methods.md`, `operator-wallet-api.md`,
`rg-pre-bet-hook.md`, `main-production-operator.md` and `main-only-player-entry.md` were deleted with
the concepts they described. Their specs remain in `specs/` as the design archive.
`operator-admin-page.md` → `admin-page.md` and `operator-player-identity.md` → `player-identity.md`.

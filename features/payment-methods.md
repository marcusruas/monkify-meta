# Feature — Payment methods (per-game settlement)

> Status: **shipped.** Authoritative spec: `../specs/payment-methods-refactor.md`
> (merged via `feature/payment-methods-refactor` on 2026-07-08).
>
> **Supersedes the operator-level payment model.** The old `Operator.PaymentModel`
> (`Regulated | Crypto`) and the `OperatorPaymentModel` enum are **gone**. Anything in
> `features/launch-token-validation.md`, `features/operator-wallet-api.md`,
> `features/operator-admin-page.md`, or `features/player-customization-modal.md` that talks about an
> operator-level payment model now means **per-game `PaymentMethod`** as described here.

## What it does

Payment is now decided **per game type**, not per operator. Each `GameParameters` row carries a
`PaymentMethod`:

- **`Standard`** — off-chain settlement through the operator's wallet adapter, always **USD**. This is
  exactly the old "Regulated (Model A)" mechanic, unchanged, just moved behind a strategy.
- **`SolanaToken`** — on-chain settlement in a Solana SPL token the operator **registers themselves**
  (its own mint / sender / owner-keypair / decimals), in the token's **own units** (never converted to
  USD). This is the old "Crypto (Model B)" mechanic, but parameterized by a per-operator **registered
  token** instead of a single global `GeneralSettings.Token`.

A single operator can offer **both** — Standard game types and SolanaToken game types side by
side. All settlement (Debit / Credit / Refund) is dispatched by a unified **`IPaymentMethodService`
strategy + `IPaymentMethodResolver`**, keyed on the game's `PaymentMethod`, mirroring the existing
`IOperatorWalletService` resolver shape.

## Core rules

- **No operator payment model.** `Operator.PaymentModel` / `OperatorPaymentModel` are removed. Operators
  are uniform; crypto-ness is a property of the **game type**, not the operator. Nothing (handler,
  consumer, DTO, frontend type) branches on an operator-level payment model any more.
- **`GameParameters.PaymentMethod`** (`Standard = 0`, `SolanaToken = 1`) + nullable
  `OperatorTokenId` (set **only** for `SolanaToken`, and it must reference an **active** `OperatorToken`
  owned by the same operator). `GameParametersValidation` enforces: method defined; for `SolanaToken`
  a token that is present, active, and operator-owned; for `Standard` the token id must be null.
- **Strategy dispatch.** `IPaymentMethodResolver.Resolve(GameParameters)` returns the
  `IPaymentMethodService` whose `Method == parameters.PaymentMethod`. `Debit / Credit / Refund` return a
  `PaymentResult` (`{ IsSuccess, PaymentError?, ExternalTransactionReference? }`, reusing the
  `OperatorWalletError` codes: `InsufficientFunds | PlayerBlocked | Duplicate | Timeout | Unavailable`).
  - `StandardPaymentMethodService` delegates to `IOperatorWalletResolver.Resolve(operator)` (USD),
    unchanged mechanics.
  - `SolanaTokenPaymentMethodService` builds a `SolanaTokenDescriptor` from the game's
    `OperatorToken` and calls `ISolanaService`. **Debit at bet time is validation-only** (the player
    already paid on-chain); Credit/Refund transfer tokens. It writes a `CryptoTransactionLog`.
- **Availability gating** (`IPaymentMethodResolver.GetAvailableMethods(operatorId)`, drives the admin
  form): **Standard** is offered iff the operator has a resolvable wallet provider configured
  (`GeneralSettings.OperatorWallets.Operators[operatorId].Provider` set); **SolanaToken** is offered iff
  the operator has **≥ 1 active** registered `OperatorToken`. An operator with neither can create no paid
  game type.
- **`OperatorToken` entity (per operator).** Fields: `Label` (display symbol, e.g. `USDC`, `MONK`),
  `MintAddress`, `SenderAccount`, `TokenOwnerPublicKey`, `TokenOwnerPrivateKey` (**encrypted at rest**),
  `Decimals`, `Active`. Managed in the admin portal: **register / list / deactivate** — **no in-place
  edit** (deactivate-and-recreate). The private key is **never** returned by any DTO and never logged.
- **Encryption at rest (`ISecretProtector`).** `TokenOwnerPrivateKey` is stored as
  **AES-256-GCM** ciphertext (base64 `[12-byte nonce][16-byte tag][ciphertext]`). The 32-byte key is
  derived via **SHA-256** from the shared passphrase `GeneralSettings.SecretProtection.Key`, so both
  hosts derive the same key without a shared key ring. The **API** `Protect`s on register; **Consumers**
  `Unprotect`s only when it needs the private key for a Credit/Refund transfer. Bet-time validation in
  the API uses **public token data only** and never decrypts. *(This ships as AES-GCM keyed from a config
  passphrase — a deliberate deviation from the spec's ASP.NET Data-Protection + Redis key-ring plan;
  it avoids the extra dependency and cross-host key-ring while keeping the key out of the DB.)*
- **Ledger split.**
  - `Bet` **drops** `PaymentSignature`; **gains** `PaymentMethod` (copied from the game at bet time)
    and `ExternalTransactionReference` (SolanaToken → the on-chain signature; Standard → the wallet debit
    ref).
  - `TransactionLog` **drops** `Signature`; **gains** `PaymentMethod`; keeps
    `Type` (`Credit | Debit | Refund`) / `Amount` / `ExternalTransactionReference` / `Reason`.
  - **New `CryptoTransactionLog`** holds the on-chain specifics (wallet, signature, mint, sender,
    token-native `Amount`, `RawTokenAmount`, `Decimals`, reason). Its `TransactionLogId` is **null** for
    the bet-**placement** record ("what the user passed") and **set** for Credit/Refund movements.
  - `BetDomainService` idempotency math is unchanged (reads the ledger by `TransactionLogType`).
- **Identity per game method** (launch token still required everywhere — operator = `iss`):
  - `Standard` → player = launch-token **`sub`**; body `wallet`/`paymentSignature` are ignored.
  - `SolanaToken` → player = connected **wallet** + a valid on-chain **signature** (unless the game
    allows free bets). `RegisterBetRequestBody`'s `wallet`/`paymentSignature` are **optional** and
    validated **conditionally** on the game's method.
- **RG pre-bet hook unchanged.** It still runs after local validation and **before** settlement, for
  **both** methods.
- **Free bets** (amount 0) make no debit/credit/refund under either method; a free **SolanaToken**
  game **skips wallet-connect**. Free-bet detection is now `IsFreeBet => bet.Amount == 0` (the
  dropped `PaymentSignature` check is gone). `AllowFreeBet` stays hidden in the admin form / false in
  prod.
- **Host split preserved.** Standard debit + SolanaToken bet-time validation run in **`SpaceStroker.Api`**
  (validation only, no private-key decrypt); Credit/Refund for both methods run in **`SpaceStroker.Consumers`**
  (SolanaToken decrypts the key there). Token registration `Protect`s in the API.
- **Amounts are method-native and never converted.** Standard = USD; SolanaToken = the token's own units
  (scaled by the token's `Decimals`, not the global `GeneralSettings.Token.Decimals`). No SOL↔USD, no
  price oracle. **Preferred display currency applies to Standard games only** (§ customization modal);
  SolanaToken amounts always render token-native.
- **`GeneralSettings.Token` is no longer read for settlement.** It survives only as (a) the global Solana
  **`ClusterUrl`** for all RPC and (b) the **seed source** for the dev operator's `OperatorToken`.

## Admin portal

- **`GET /admin/operator-tokens`** → active tokens (`OperatorTokenDto[]`, **no private key**).
- **`POST /admin/operator-tokens`** → validate + `Protect` the private key + persist; returns the id.
- **`POST /admin/operator-tokens/{id}/deactivate`** → `Active = false` (deactivate-or-create, no edit).
- **`GET /admin/payment-methods`** → `GetAvailableMethods(operatorId)` + the active token list, so the
  game-type form only offers available methods and (for SolanaToken) selectable tokens.
- The game-type create form (`GameTypeRequestBody` / `CreateGameTypeHandler`) now carries
  `PaymentMethod` + `OperatorTokenId` and validates availability + token ownership/active before create.
  `GameTypeAdminDto` / `GameTypeList` expose the method + token label. All under the existing
  `/admin` guard (`X-Admin-Token`); no middleware change.

## Frontend behaviour

Every operator-level `isCryptoOperator` / `isRegulatedOperator` / `paymentModel` gate is replaced by the
**selected game's `paymentMethod`**.

- **Types (`src/types/index.ts`):** `OperatorPaymentModel` removed; `enum PaymentMethod { Standard,
  SolanaToken }` added. `ActiveGame` gains `paymentMethod` + optional `tokenSymbol`, `tokenDecimals`,
  `tokenMintAddress`, `tokenReceiverPublicKey` (present for SolanaToken). Admin `GameTypeAdmin` /
  `GameTypeBody` gain `paymentMethod` + `operatorTokenId`; new `OperatorTokenDto` / `OperatorTokenBody`
  / `PaymentMethodAvailability` types.
- **`useOperator.ts`:** payment-model state (`paymentModel`, `isCrypto/isRegulated`) removed — branding
  no longer carries a payment model.
- **`useGame.ts` `bet()`:** branches on `selectedGame.paymentMethod`. Standard (non-free) posts
  `{ amount, choice, seed }` with empty wallet/signature (identity via launch token); SolanaToken
  (non-free) `sendToken` to the game's `tokenReceiverPublicKey` / `tokenMintAddress`, then posts with
  the signature + wallet. Free = amount 0, empty signature.
- **`GameHeader.vue`:** the wallet cluster + `TokenBalance` show **only** when the current game is
  SolanaToken (`showWallet = selectedGame?.paymentMethod === PaymentMethod.SolanaToken`), using the
  game's `tokenSymbol` / `tokenMintAddress`; hidden on the games list and for Standard games.
  `TokenBalance` **keeps itself current** (bug-fix `wallet-balance-refresh`, 2026-07-17): a ~10 s
  poll while on a connected SolanaToken game (paused when the tab is hidden, stopped on
  unmount/disconnect) plus an **instant optimistic decrement on the player's own bet**, a **win-payout
  read ~5 s after `Ended`**, and a refresh on refund (`NotEnoughPlayersToStart`) / reward-error. There
  is **no balance surface for Standard games** (SpaceStroker holds no player float). See
  `../specs/wallet-balance-refresh.md`.
  The player-settings **gear** is gated on **operator context derived from the launch token**
  (`hasOperatorContext = operatorId ?? getOperatorIdFromToken()`) so it survives an operatorId reset from
  a hard reload (e.g. a transient 401 → `/error` → back).
- **`GameConsole.vue`:** the connected-wallet bet gate applies **only** to SolanaToken games.
- **`GameTerminal.vue`:** `formatAmount` branches on the method — SolanaToken → `` `${amount}
  ${tokenSymbol}` `` (token decimals); Standard → `formatUsd` (preferred display currency). Applies to
  `> BET:` and `> POT:`.
- **`PlayerCustomizationModal.vue`:** the currency selector is offered to **all** operators (it only
  affects Standard rendering); the old crypto-operator gate is gone.
- **Admin UI:** `GameTypeForm.vue` gains a **Payment method** dropdown (available methods only) and,
  for SolanaToken, a **token** dropdown; `GameTypeList.vue` adds a centered method/token column (and
  the deactivate action no longer overflows the table). New **`OperatorTokenManager.vue`** (register /
  list / deactivate tokens, private key is a write-only input) mounts in the admin game-type manager.
  `adminService.ts` gains `getOperatorTokens`, `registerOperatorToken`, `deactivateOperatorToken`,
  `getPaymentMethods`.
- **Dev entry:** `DevLandingPage.vue` + `DevAdminButton.vue` collapse to a single "Enter (dev)" /
  "Admin (dev)" action; `/dev/launch-token` and `/dev/admin-token` **drop** the `?paymentModel` param.

## Dev seeding & endpoints

- **One dev operator** at a fixed RFC-4122 GUID **`11111111-1111-4111-8111-111111111111`** (the version
  nibble `4` and variant `8` matter — the frontend `isValidOperatorGuid` / `getOperatorIdFromToken`
  rejects a non-RFC-4122 `iss`, which is what made an earlier all-`1`s GUID bounce "Enter (DEV)" to
  `/error`). No `PaymentModel`.
- On seed the dev operator is pre-provisioned with **both** methods: the mock wallet map keyed to that
  GUID (`GeneralSettings.OperatorWallets`, `Provider = "mock"`) → **Standard available**, and **one
  seeded `OperatorToken`** built from `GeneralSettings.Token` with its private key **encrypted** via
  `ISecretProtector` → **SolanaToken available**.
- **Default game types (changed 2026-07-23):** the dev operator now seeds the **production trio**
  (`GenerateMainDefaultParameters` — Cosmic Sprint / Beast Mode / Space Jackpot), all **SolanaToken**
  against the seeded dev token, with `AllowFreeBet` left on. `GenerateDefaultParameters()` (Standard)
  and `GenerateCryptoDefaultParameters()` (**6L Solana Sprint**, **6N Token Tumble**, **6X Crypto
  Clash**) are **no longer seeded** — they are test fixtures only. Standard settlement stays fully
  reachable in dev (the mock wallet map is still provisioned, and the seed falls back to `Standard`
  when no dev token is registered), but the seeded catalogue is crypto. See `games.md`.
- `DevController` resolves the single dev operator; the dev launch token carries `sub = dev-player` so
  Standard identity works, and SolanaToken games still get their wallet from wallet-connect.
- **Config:** `GeneralSettings.SecretProtection.Key` (mock passphrase) added to `spacestrokersettings.json`;
  `OperatorWallets.Operators` re-keyed to the dev GUID. **Mock values only** — the token private key in
  config is a placeholder.

## Key code touchpoints

**Backend (`monkify-backend`)**

- Value objects / enums: `Games/ValueObjects/PaymentMethod.cs` (new),
  `Games/ValueObjects/SolanaTokenDescriptor.cs` (new); `OperatorPaymentModel.cs` **deleted**.
- Entities: `Games/Entities/OperatorToken.cs` (new), `Operator.cs` (drop `PaymentModel`, add `Tokens`),
  `GameParameters.cs` (`PaymentMethod` + `OperatorTokenId` + nav); `Bets/Entities/Bet.cs` (drop
  `PaymentSignature`, add `PaymentMethod` + `ExternalTransactionReference`), `TransactionLog.cs` (drop
  `Signature`, add `PaymentMethod`), `CryptoTransactionLog.cs` (new).
- Domain services: `Bets/Services/BetExtensions.cs` (`IsFreeBet => amount == 0`),
  `Games/Services/GameParametersValidation.cs` (method/token rules).
- Encryption: `Infrastructure/Services/Encryption/{ISecretProtector,AesSecretProtector}.cs` (new);
  `Domain/Configs/Entities/SecretProtectionSettings.cs` (+ `GeneralSettings.SecretProtection`).
- Payments strategy: `Infrastructure/Services/Payments/` — `IPaymentMethodService`, `PaymentResult`,
  `StandardPaymentMethodService`, `SolanaTokenPaymentMethodService`, `IPaymentMethodResolver`,
  `PaymentMethodResolver`. Registered in `Infrastructure/DependencyInjection.cs`.
- Solana: `Infrastructure/Services/Solana/{ISolanaService,SolanaService}.cs` — methods take a
  `SolanaTokenDescriptor`, build the owner account per call, return the signature, write no ledger rows.
- Settlement: `Handlers/Games/RegisterBet/RegisterBetHandler.cs` (+ `RegisterBetRequestBody` optional
  wallet/signature), `Consumers/RewardGame/RewardGameConsumer.cs`,
  `Consumers/RefundInvalidBet/RefundInvalidBetConsumer.cs` (+ event).
- Admin: `Handlers/Admin/OperatorTokens/{Register,Get,Deactivate}OperatorToken*`,
  `Handlers/Admin/PaymentMethods/GetAvailablePaymentMethods*`,
  `Handlers/Admin/CreateGameType/CreateGameTypeHandler.cs`; `Api/Controllers/AdminController.cs`;
  `Contracts/Admin/{OperatorTokenDto,OperatorTokenRequestBody,PaymentMethodAvailabilityDto,
  GameTypeRequestBody,GameTypeAdminDto}.cs`.
- DTOs / prefs: `Contracts/Games/{GameParametersDto,BetDto,OperatorDto}.cs`;
  `Handlers/Players/UpdatePlayerPreferences/UpdatePlayerPreferencesHandler.cs` (fiat allowed for all).
- EF + migration: inline mapping in `SpaceStrokerDbContext.OnModelCreating` (DbSets `OperatorTokens`,
  `CryptoTransactionLogs`; drops + new columns); migration
  `Migrations/20260707210000_RefactorPaymentMethods.cs`.
- Config / dev: `SpaceStroker.Common/spacestrokersettings.json` (`SecretProtection`, `OperatorWallets` re-keyed),
  `SpaceStroker.Consumers/Program.cs` (single dev operator, `EnsureDevOperatorToken`, seeding),
  `SpaceStroker.Consumers/DefaultGameParameters.cs`, `Api/Controllers/DevController.cs`.
- Tests: unit — `PaymentMethodResolverTests`, `AesSecretProtectorTests`, `OperatorTokenHandlerTests`
  (new) + updated `RegisterBetHandlerTests`, `RewardWinnersHandlerTests`, `RefundInvalidBetConsumerTests`,
  `SolanaServiceTests`, `DevControllerTests`, `UpdatePlayerPreferencesHandlerTests`, `BetDtoTests`,
  `OperatorServiceTests`, `GameTypeDeactivatedConsumerTests`, `BetDomainServiceTests`,
  `BetPlacedHandlerTests`, `AdminGameTypeHandlerTests`, `Shared/BetTestFactory`. Integration —
  `GameFlowTests` seeds both a **Standard** (`PresetRace_PaidRegulated…`) and a **SolanaToken**
  (`NumbersRace_PaidCrypto…`, `AlphanumericRace_PaidCrypto…`) bet→reward happy path;
  `OperatorWalletRefundTests` covers the refund path; harness `Harness/{TestDoubles(FakeSolanaService),
  IntegrationTestFixture}` seeds "IT Standard Cassino" / "IT SolanaToken Cassino" + token config.

**Frontend (`monkify-front`)**

- `src/types/index.ts`, `src/hooks/{useOperator,useGame,useGameChat}.ts`,
  `src/services/adminService.ts`.
- `src/components/GameHeader.vue`, `src/components/game/{GameTerminal,GameConsole}.vue`,
  `src/components/header/{PlayerCustomizationModal,DevAdminButton}.vue`, `src/pages/DevLandingPage.vue`.
- `src/components/admin/{GameTypeForm,GameTypeList,GameTypeManager,OperatorTokenManager}.vue`.
- `src/utils/{launchToken.ts,operatorRoute.ts}` (RFC-4122 GUID validation).

## Notes / drift

- **Encryption deviates from the spec** (AES-GCM + config passphrase vs Data-Protection + Redis key
  ring) — see the Encryption rule above; behaviour (encrypted at rest, cross-host decrypt, never
  returned/logged) is equivalent.
- The integration test **method names** still read `…PaidCrypto` / `…PaidRegulated`; they exercise the
  new **SolanaToken** / **Standard** payment methods respectively (semantic carry-over from the old
  model names).

# Feature — Main production casino (default operator + auto-mint)

> Status: **shipped.** Authoritative spec: `../specs/main-production-operator.md`
> (merged via `feature/main-production-operator` on 2026-07-15).

## What it does

Gives production a default operator named **`Main`** so the public game works with **no casino
redirect**. Before this, production seeded **no operator at all** and a token-less visitor dead-ended
on `/error`. Now `ChimpOut.Consumers` seeds Main (its own registered Solana token + three crypto game
types), and when a standalone visitor arrives with **no launch token** the frontend **mints an
anonymous Main token** via a new production-enabled endpoint and uses it. All Main play is crypto
(SolanaToken / wallet-paid), so the auto-minted token carries no player identity.

## Core rules

- **Main is the default production operator.** `ChimpOut.Consumers` seeds an operator named **`Main`**
  (fixed RFC-4122 GUID `22222222-2222-4222-8222-222222222222`, contract **active**, `ContractEndDate =
  null`, its own `LaunchPublicKey`) in **every** environment. In **production it is the only** seeded
  operator; **non-production seeds Main *and* DEV Cassino** (DEV Cassino keeps all its existing default
  game types and dev landing buttons unchanged). Seeding is **idempotent + self-healing** (mirrors
  the dev operator seeding).
- **Main's token + game types.** Main is pre-provisioned with **one** registered `OperatorToken`
  built from `GeneralSettings.Token` (private key encrypted at rest via `ISecretProtector`), and
  **exactly three active `SolanaToken`** default game types — `6X Main Sprint` (free-type race),
  `6X Main Derby` (preset "horse race", back 1 of 8 six-char alphanumeric runners), and `6X Main
  Millions` (24h lottery, base pot 4). All are **6-char alphanumeric** (`NumbersAndLetters`), with
  **free bets OFF**, `RequiredAmount = 1`, `MinimumNumberOfPlayers = 2`, duplicates allowed. Betting
  therefore requires a connected wallet + on-chain signature; amounts are token-native.
- **Anonymous default-operator mint.** **`POST /launch-token/default`** (in `LaunchController`, **not**
  under `/dev`, available in **all** environments incl. production, and **not** token-guarded — its
  route is outside the launch-token middleware's protected prefixes) mints an **anonymous** (empty
  `sub`) RS256 launch token **only** for the Main operator (it resolves Main by name and mints for no
  other operator; returns a vague **404** if Main is not seeded). The token is signed with Main's own
  `MainSigningPrivateKeyPem`, has TTL `MainTokenTtlSeconds` (default 3600s), and is validated by the
  normal `LaunchTokenValidationMiddleware` against Main's stored `LaunchPublicKey` — no middleware
  change.
- **Frontend auto-mint + precedence.** `useLaunch.ensureLaunchToken()` resolves a token in order:
  a fragment `#launch_token=` (a real casino launch) → a token stored for the tab → **(standalone
  only)** an auto-minted **Main** token from `POST /launch-token/default`. A real fragment/stored token
  **always wins**; auto-mint runs only when none exists. The app **never auto-mints when embedded**
  (that would render Main inside another casino's frame — embedded + no token keeps the existing
  `token-expired` relaunch path).
- **Silent re-mint on expiry.** Auto-minted tokens are flagged in `sessionStorage`
  (`chimpout.autoLaunch`). A **401 on an auto-minted token** triggers a **silent re-mint + one retry**
  of the failed request instead of routing to `/error`. A **real operator's 401** still routes to
  `/error` (vague, contract status never revealed) — unchanged.
- **Token-less entry.** In production `/chimpout/game` forwards to `/chimpout/game/games` (whose boot
  auto-mints Main), instead of redirecting to `/error`. The dev landing (`VUE_APP_DEV_LAUNCH`) is
  unchanged — its DEV Cassino Crypto/Regulated buttons still work in non-prod.
- **Secrets.** Main's launch **private** signing key and the reused token owner private key are
  **mock/placeholder** in committed config; the real values are injected via the production
  environment. No schema change / migration (Main is seeded data).

## Key code touchpoints

**Backend (`monkify-backend`)**

- `ChimpOut.Consumers/Program.cs` — `CreateDefaultOperatorAsync` (seeds Main in all envs, DEV
  non-prod-gated), `EnsureMainOperator`, `EnsureMainOperatorToken`; `CreateDefaultGameParametersAsync`
  (Main branch seeds the 3 SolanaToken types).
- `ChimpOut.Consumers/DefaultGameParameters.cs` — `GenerateMainDefaultParameters(operatorTokenId)`
  (+ `MainBase`/`MainRace`/`MainPreset`/`MainLottery`).
- `ChimpOut.Api/Controllers/LaunchController.cs` — `POST /launch-token/default` (anonymous, Main-only).
- `ChimpOut.Api/Launch/LaunchTokenService.cs` — `Issue(...)` reused to sign the Main token.
- `ChimpOut.Domain/Configs/Entities/LaunchTokenSettings.cs` — `MainSigningPrivateKeyPem`,
  `MainOperatorPublicKeyPem`, `MainTokenTtlSeconds`; mock values in `ChimpOut.Common/chimpoutsettings.json`.
- Tests: `ChimpOut.Tests/UnitTests/Controllers/LaunchControllerTests.cs`,
  `ChimpOut.Tests/UnitTests/Consumers/DefaultGameParametersTests.cs`. (No new integration test —
  waived for this card; Main's crypto bet→run→reward is covered by existing `GameFlowTests`.)

**Frontend (`monkify-front`)**

- `src/hooks/useLaunch.ts` — auto-mint fallback (standalone, not embedded; real token wins).
- `src/services/mainService.ts` — `getDefaultLaunchToken()`; 401 interceptor silent re-mint + retry for
  auto-minted tokens (real-operator 401 → `/error`, embedded → `token-expired`).
- `src/utils/launchToken.ts` — `setAutoMintedLaunchToken` / `isAutoMintedLaunch` (`chimpout.autoLaunch`
  marker; `setLaunchToken` clears it).
- `src/routes.ts` — prod `/chimpout/game` → `/chimpout/game/games` (was `/error`).

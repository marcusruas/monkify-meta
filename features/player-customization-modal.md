# Feature — Player Customization Modal

> Status: **shipped.** Authoritative spec: `../specs/player-customization-modal.md`
> (merged via `feature/player-customization-modal` on 2026-07-02).
>
> ⚠️ **Halved and hardened by `features/remove-operators.md` (2026-07-30).** The **preferred display
> currency is gone** — it existed to render off-chain USD amounts, and the `Standard` payment method
> that produced them no longer exists. Every game settles in one configured token, so every amount is
> token-native and there is nothing to convert: `Player.Currency`, `GET /fx/rates`, the whole
> `Fx`/`IExchangeRateProvider` stack and `useDisplayCurrency` were deleted. What remains is the
> **display name**, and saving it now requires an **ed25519 wallet signature** because the endpoint is
> public and a wallet address is not a secret.

## What it does

A **menu entry** in the game header opens a modal where the player sets their **display name**.
Display names render wherever players are identified — the own `YOU` label, recent-bets rows and
winners — falling back to a truncated wallet address when unset.

## Core rules

- **Entry point:** the header menu button in `GameHeader.vue` (rendered once in `App.vue`, so it is
  present on the lobby and live play). Modal built with the design system (DaisyUI modal +
  `PrimaryButton`/`SecondaryButton`).
- **Identity is the connected wallet.** With no wallet connected the modal shows a "Connect wallet to
  customize" gate; there is no other subject to save against.
- **Display name validation:** trimmed, **1–20** characters; empty/whitespace-only rejected. Winner
  names are resolved **client-side** from bets already held — `GameResult` is unchanged.
- **The write is signature-gated.** Saving is a three-step flow:
  1. `GET /players/nonce?wallet=<pubkey>&purpose=preferences` → a single-use nonce
     (`WalletNonce:preferences:{wallet}` in Redis, 5 min).
  2. The SPA builds the canonical message and signs it with the connected wallet (ed25519).
  3. `PATCH /players/preferences { wallet, displayName, nonce, signature }`.

  The handler verifies the signature against the claimed wallet and consumes the nonce. A signature
  from a different keypair, an unsigned body, or a replayed nonce is rejected — otherwise anyone could
  rename anyone, since wallet addresses are public.
- **The canonical message is a shared contract.** `UpdatePlayerPreferencesHandler.BuildMessage(wallet,
  displayName, nonce)` and the frontend's `usePlayerPreferences.ts` must produce byte-identical
  strings; the integration test deliberately calls the handler's own helper to pin this.
- **The nonce is purpose-scoped**, so a challenge minted for `preferences` cannot be replayed against
  `self-exclusion`, which uses the same mechanism. See `features/legal-docs-self-exclusion.md`.
- **`GET /players/preferences?wallet=<pubkey>`** is **public and unsigned** (reading a public wallet's
  chosen nickname discloses nothing). Returns `{ externalPlayerId, displayName }`; `displayName` is
  `null` when no `Player` row exists, and **no row is created on read**.
- **Bet DTO / SignalR:** `DisplayName` on `BetDto` and `BetNotificationEvent`; `GameService.InsertBet`
  captures the display name before detaching `bet.Player` for the event payload.
- **No EF migration** — reuses the existing `Player.DisplayName` column.

## Key code touchpoints

**Backend (`monkify-backend`)**

- `SpaceStroker.Api/Controllers/PlayersController.cs` — `GET /players/nonce`,
  `GET /players/preferences`, `PATCH /players/preferences`
- `SpaceStroker.Infrastructure/Handlers/Players/GetWalletNonce/` — purpose-scoped challenge
- `SpaceStroker.Infrastructure/Handlers/Players/GetPlayerPreferences/GetPlayerPreferencesHandler.cs`
- `SpaceStroker.Infrastructure/Handlers/Players/UpdatePlayerPreferences/UpdatePlayerPreferencesHandler.cs`
  — `BuildMessage` + ed25519 verification
- `SpaceStroker.Infrastructure/Services/Cache/CacheHelper.cs` — `GenerateWalletNonceCacheSettings`
- `SpaceStroker.Infrastructure/Contracts/Games/BetDto.cs`,
  `Events/BetNotification/BetNotificationEvent.cs`
- `SpaceStroker.Infrastructure/Services/Games/GameService.cs` — `DisplayName` capture on insert
- Tests: `GetPlayerPreferencesHandlerTests`, `UpdatePlayerPreferencesHandlerTests`, `BetDtoTests`;
  integration `PlayerPreferencesTests` (signs with real Solnet keypairs — a made-up wallet string
  cannot sign)

**Frontend (`monkify-front`)**

- `src/components/header/PlayerCustomizationModal.vue` — modal UI + the wallet-signature save step
- `src/hooks/usePlayerPreferences.ts` — nonce fetch, canonical message, sign, PATCH
- `src/components/GameHeader.vue` — header menu entry
- `src/services/mainService.ts` — `getPlayerNonce()`, `getPlayerPreferences(wallet)`,
  `updatePlayerPreferences()`
- `src/components/game/GameTerminal.vue` — display names
- `src/hooks/useGame.ts`, `src/types/index.ts` — `AppliedBet.displayName`

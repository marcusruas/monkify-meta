# Domain — Player identity and preferences

> **What this covers.** Who a bet belongs to (the `Player` row keyed on the connected wallet), how a
> display name is set and protected, and the two places a player is asked for one.
>
> Deep design: [`../specs/player-customization-modal.md`](../specs/player-customization-modal.md) and
> [`../specs/wallet-display-name.md`](../specs/wallet-display-name.md). The original identity spec
> (`operator-player-identity.md`) was retired in card #167 — its surviving content is here.

> ⚠️ **Rewritten (2026-07-30) by [`remove-operators.md`](remove-operators.md).** The entity was
> `OperatorPlayer`, keyed `(OperatorId, ExternalPlayerId)`, and identity could arrive either as a
> launch-token `sub` **or** as a wallet depending on the game's payment method. Operators, launch tokens
> and the `Standard` method are gone: the entity is **`Player`**, **the wallet that signed the transfer
> is the only identity**, and `Bet.OperatorPlayerId` is now `Bet.PlayerId`. `Player.Currency` was dropped
> with the `Standard` method. Every "wallet-first vs `sub`-first" conditional described in the old docs
> is now unconditional.

## 1. The `Player` row

Gives every paid bet a stable row to hang display names, own-bet highlighting, "mine only" history
filtering, per-round bet limits and self-exclusion off. **Anonymous free bets deliberately have none.**

- **One `Player` row per wallet.** `ExternalPlayerId` (the wallet address) carries a unique index on its
  own. **No PII columns.** The field is named `ExternalPlayerId` rather than `Wallet` because that is what
  every DTO, SignalR event and history filter already calls it.
- **The wallet is the only identity source.** A paid bet sends `wallet` in the register-bet body; the
  handler upserts the `Player` and sets `Bet.PlayerId`.
- **Anonymous free bet:** no wallet ⇒ no `Player` row, `Bet.PlayerId` stays NULL.
- **Paid bet without identity:** rejected.
- **There is no `Bet.Wallet` column.** Payout and refund addresses resolve from `Player.ExternalPlayerId`
  via `Bet.GetWalletAddress()`.
- **Duplicate-bet key:** `(GameId, PlayerId, Choice)` when `PlayerId` is set; `(GameId, Choice)` when NULL.
- **Min-player count:** distinct key = `PlayerId` when set, else `Bet.Id` per bet (`BetPlacedConsumer`).
  ⚠️ **Exception — free bets always count individually** (`Bet.Id`) even when they resolve to the same
  `Player`, so a single tester can start a game. See [`games.md`](games.md).
- **Self-exclusion lives on this row** (`SelfExcludedAt` + `SelfExcludedUntil`) and is evaluated per bet,
- **Referral attribution lives on this row too** (`ReferralCode` + `ReferredAt`) and is ⚠️ **written
  only when NULL** — first-touch, never overridden, by the two handlers that already prove wallet
  control. `ReferredAt` exists because this row is created by five different paths and can predate
  attribution by days, so `CreatedDate` is not the acquisition date. See
  [`referral-program.md`](referral-program.md).
  so an expiry needs no scheduled job. See [`legal.md`](legal.md).
- **API/DTOs:** bet responses and SignalR events expose `externalPlayerId` (nullable) and, since #186,
  `isHouse` (true when the bettor is a configured house wallet — read from config, not a column). Every
  player name in the live surfaces renders through a tappable player tag that opens a mini profile card —
  see [`player-profile.md`](player-profile.md).

⚠️ **Two bugs this area has produced twice — both in `GameService.InsertBet`.** It detaches `bet.Game`
and `bet.Player` before `AddAsync` so EF does not re-insert a persisted graph:
1. It must **capture the wallet address and display name before detaching**, or `BetNotificationEvent`
   ships a null `ExternalPlayerId` and a player's own live bet fails to render `YOU`.
2. It must **restore both navigations**, not just `Game` — the caller writes the placement ledger row from
   `bet.Player` right after, so restoring only `Game` blanked the `Wallet` on every placement row.

## 2. Setting a display name

Display names render wherever players are identified — the own `YOU` label, recent-bets rows and winners
— falling back to a truncated wallet address when unset.

- **Validation:** trimmed, **1–20** characters; empty/whitespace-only rejected. Winner names are resolved
  **client-side** from bets already held, so `GameResult` is unchanged.
- ⚠️ **The write is signature-gated**, because the endpoint is public and a wallet address is not a
  secret — otherwise anyone could rename anyone. Three steps:
  1. `GET /players/nonce?wallet=<pubkey>&purpose=preferences` → a single-use nonce
     (`WalletNonce:preferences:{wallet}` in Redis, 5 min).
  2. The SPA builds the canonical message and signs it with the connected wallet (ed25519).
  3. `PATCH /players/preferences { wallet, displayName, nonce, signature }`.

  The handler verifies the signature against the claimed wallet and consumes the nonce. A signature from
  a different keypair, an unsigned body, or a replayed nonce is rejected.
- ⚠️ **The canonical message is a shared contract.** `UpdatePlayerPreferencesHandler.BuildMessage(wallet,
  displayName, nonce)` and the frontend's `useDisplayNameSigning.ts` must produce **byte-identical**
  strings; the integration test deliberately calls the handler's own helper to pin this.
- **The nonce is purpose-scoped**, so a challenge minted for `preferences` cannot be replayed against
  `self-exclusion`, which uses the same mechanism.
- **A display name may also ride along on a register-bet body** — safe, because that request already
  proves wallet control by carrying a confirmed on-chain transfer from it.
- **`GET /players/preferences?wallet=<pubkey>` is public and unsigned** (reading a public wallet's chosen
  nickname discloses nothing). Returns `{ externalPlayerId, displayName, profileTheme }` (plus the
  self-exclusion fields); `displayName` / `profileTheme` are `null` when no `Player` row exists, and **no
  row is created on read**.
- The name lives in the existing `Player.DisplayName` column.

### Color profile on the same write (#186)

- `Player.ProfileTheme` (`string?`, max 20; migration `AddPlayerProfileTheme`) holds one of 8 preset keys
  (`amber comet nebula toxic ocean aurora sunset solar`) or `null` = never picked (the front derives a
  default from the wallet). The PATCH body's `profileTheme`: null = unchanged, `"default"` = clear, a key =
  set, anything else = the generic validation failure. A theme-only save (no `displayName`) is allowed.
- ⚠️ **A save that carries a theme signs a different message** — `BuildProfileMessage(wallet, displayName ??
  "", theme, nonce)` = `Space Stroker profile` / `Wallet:` / `Display name:` / `Theme:` / `Nonce:` lines. A
  name-only save still signs `BuildMessage` byte-for-byte, so a name signature can never set a theme. Same
  nonce purpose (`preferences`). The front twin is `buildProfileMessage` in `useDisplayNameSigning.ts`.
- Full rules (where the theme paints, the levels read that carries it): [`player-profile.md`](player-profile.md).

## 3. The settings modal

Opened from the header menu's SETTINGS cell in `GameHeader.vue` (rendered once in `App.vue`, so it is
present on the lobby and in live play), and from **EDIT NAME / EDIT THEME** on the owner's profile page.
It holds **three** things, and **only the first is server-backed**:

| Group | Storage | Notes |
| --- | --- | --- |
| **Display name + COLOR PROFILE** | `PATCH` (signature-gated) | Shows a "Connect wallet to customize" gate with no wallet. The 8-swatch grid opens on the theme in effect (saved pick, else wallet default) and a save sends a theme only when it changed |
| **Provably-fair client seed** | `localStorage` | Applies instantly, never prompts a signature |
| **Graphics controls** (light mode + sky density) | `localStorage` | See [`frontend-ux.md`](frontend-ux.md) |

⚠️ The last two are **not reverted by `CANCEL`**, and ⚠️ **the graphics group renders OUTSIDE the wallet
`v-if / v-else`** — with no wallet connected the `IDENTITY` block is a connect gate rather than the name
field, and a device display setting must not sit behind an identity the player does not have.

*(The preferred-display-currency dropdown was removed with `Standard`: every amount is token-native now,
so `Player.Currency`, `GET /fx/rates`, the whole `Fx`/`IExchangeRateProvider` stack and
`useDisplayCurrency` were deleted.)*

## 4. The "pick your name" prompt

A player who connects a wallet **with no display name for that wallet** is asked once, right then. Before
this, a nameless player kept playing as `ANON` until they happened to open the header menu, so most
crypto players never set a name at all. It is a **small dedicated modal**, not the full settings modal —
seed and graphics controls are noise at the moment someone just connected to play.

- **Trigger** — opens only when **all** hold: the route is a player route (`GameLobby` / `Game` /
  `GameHistory` / `GameStats`, so the `/wallet-bridge` popup and the admin portal are never interrupted);
  **not embedded** (a modal over someone else's iframe is not ours to show); a wallet is actually
  connected; the preferences read **succeeded**; the stored name is empty; and the player has not already
  dismissed it for that wallet.
- **Once per wallet, remembered.** `SKIP` writes `space-stroker.namePrompt.skipped.<wallet>` to
  `localStorage`. Keyed **per wallet** because the name itself is per-wallet identity. Backdrop click and
  `ESC` both behave as `SKIP`, so a dismissal always sticks. Storage unavailable (private mode) degrades
  to "never asked" rather than throwing.
- **A saved name needs no flag** — the gate only fires on a confirmed-empty name, so setting one via the
  header menu suppresses it just as effectively as `SKIP`.
- **Fires on auto-reconnect too**, not only a manual click: a silently reconnected wallet is
  indistinguishable from a fresh connect, and a nameless player is nameless either way. The mount path
  goes through the same evaluation, so an already-hydrated **mobile deeplink session** — which never
  fires the wallet watcher — is still covered.
- ⚠️ **`loadPreferences` returns whether the read succeeded.** `displayName` in `usePlayerPreferences` is
  a shared singleton that keeps its previous value when the GET fails — which would read as "this player
  has no name" and, right after a wallet switch, holds the **previous** wallet's name. The prompt only
  fires on a confirmed-empty name.

## Key code touchpoints

**Backend (`monkify-backend`)**

| File | Role |
| --- | --- |
| `SpaceStroker.Domain/Games/Entities/Player.cs` | The entity |
| `SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs` | Player upsert, identity resolution, duplicate keys |
| `SpaceStroker.Infrastructure/Services/Games/GameService.cs` | ⚠️ `InsertBet` capture-then-detach-then-restore |
| `SpaceStroker.Infrastructure/Handlers/Players/GetWalletNonce/` | Purpose-scoped challenge |
| `.../Players/GetPlayerPreferences/` · `.../UpdatePlayerPreferences/` | Read; `BuildMessage` / `BuildProfileMessage` + ed25519 verify |
| `SpaceStroker.Domain/Players/ProfileThemes.cs` | The 8 theme keys |
| `SpaceStroker.Api/Controllers/PlayersController.cs` | `GET /players/nonce`, `GET`/`PATCH /players/preferences` |
| `SpaceStroker.Infrastructure/Contracts/Games/BetDto.cs` · `BetNotificationEvent` | `DisplayName`, `ExternalPlayerId` |

**Frontend (`monkify-front`)** — `src/components/header/PlayerCustomizationModal.vue`,
`src/components/header/DisplayNamePrompt.vue`, `src/hooks/useDisplayNamePrompt.ts`,
`src/hooks/usePlayerPreferences.ts` (shared name / theme state), `src/hooks/useDisplayNameSigning.ts`
(nonce → canonical message → sign → PATCH), `src/constants/profileThemes.ts`,
`src/App.vue` (`isPlayerRoute()`, `refreshPreferences()`), `src/services/mainService.ts`,
`src/components/game/GameTerminal.vue`, `src/types/index.ts`.

**Tests** — `GetPlayerPreferencesHandlerTests`, `UpdatePlayerPreferencesHandlerTests`, `BetDtoTests`;
integration `PlayerPreferencesTests` ⚠️ **signs with real Solnet keypairs — a made-up wallet string
cannot sign**. `monkify-front` has no test runner (`lint` + `build` only).

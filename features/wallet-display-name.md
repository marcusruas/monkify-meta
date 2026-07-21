# Feature — Display name per connected wallet (SolanaToken)

> Status: **shipped.** Authoritative spec: `../specs/wallet-display-name.md`
> (merged via `feature/wallet-display-name` on 2026-07-14).
>
> Full-stack. Child of the umbrella **#46 refactor solana wallet flow** (escrow-independent).

## What it does

Makes a player's **display name and preferences key off their connected wallet** for **SolanaToken**
games, everywhere: the name they set now shows on their own `YOU` label, on recent-bets rows, and
in the winners list for crypto play, instead of coming back blank / `ANON`. For Standard games
identity stays the launch-token `sub`, unchanged. The customization modal also gates itself behind a
connected wallet when there is no other identity to write to, and hides the (irrelevant) currency
selector in a crypto game.

## Core rules

- **Preference identity is the wallet in a SolanaToken context, else the launch-token `sub`.** The
  backend preference handlers resolve identity **wallet-first when a wallet is provided**
  (`request.Wallet ?? sub`). This matters because the **dev launch token — and any operator offering
  both Standard and SolanaToken games — always carries a non-empty `sub`**; the earlier `sub`-first
  resolution ignored the wallet, so a crypto player's name was saved to the `sub` `OperatorPlayer`
  while their SolanaToken bets (keyed by the **wallet** `OperatorPlayer`) had no name and rendered
  `ANON`.
- **The client sends the wallet to the preference endpoints only in a SolanaToken context** (a
  SolanaToken game, or a crypto-only operator with no usable `sub`). A Standard player with a stray
  connected wallet still writes to their `sub` — the frontend does not send the wallet, and even if it
  did the two identities are distinct `OperatorPlayer` rows.
- **`GET`/`PATCH /players/preferences`** carry the wallet as before — `?wallet=` on GET,
  `body.wallet` on PATCH. No route, DTO, or contract change.
- **Live bet notifications carry the player's external id.** `GameService.InsertBet` captures the
  wallet address **before** detaching the `OperatorPlayer` navigation, so the `BetNotificationEvent`
  ships a non-null `ExternalPlayerId`; a player's own **live** SolanaToken bet matches and renders
  `YOU` (previously the id was dropped and live bets fell back to the name / `ANON`).
- **Reactive per-wallet load (frontend).** Preferences reload when the connected wallet
  connects / switches / disconnects (and when the selected game's payment method changes), not only
  on mount or modal-open — so a crypto player's saved name appears without needing to open the modal.
- **The customization modal gate — "Connect wallet to customize".** The modal shows a connect prompt
  (with the wallet-connect affordance) in place of the name form when **no wallet is connected AND
  (the selected game is `SolanaToken` OR there is no usable launch-token `sub`)**. It re-evaluates
  live as the wallet changes while open; disconnecting in a crypto context reverts to the prompt.
- **Currency selector hidden for SolanaToken.** In a SolanaToken game the modal shows the display
  name only; the preferred-display-currency dropdown (a display-only conversion for Standard/USD
  amounts) is hidden. It stays visible for Standard games and on the games list.
- **Free SolanaToken game (amount 0, wallet-connect skipped):** a player with no wallet has no
  identity (anonymous free bet, `OperatorPlayerId` NULL) → cannot set a display name (connect prompt;
  renders `ANON`), and may optionally connect a wallet to customize.
- **No data / settlement change.** Display-only; no entity, EF mapping, migration, DTO, or SignalR
  contract change. Standard games are unchanged.

## Key code touchpoints

**Backend (`monkify-backend`)**

- `SpaceStroker.Infrastructure/Handlers/Players/GetPlayerPreferences/GetPlayerPreferencesHandler.cs` —
  identity resolution prefers `request.Wallet` when present, else the token `sub`.
- `SpaceStroker.Infrastructure/Handlers/Players/UpdatePlayerPreferences/UpdatePlayerPreferencesHandler.cs` —
  same wallet-first precedence for `request.Body.Wallet`.
- `SpaceStroker.Infrastructure/Services/Games/GameService.cs` (`InsertBet`) — captures
  `bet.GetWalletAddress()` before nulling `OperatorPlayer`; passes it into `BetNotificationEvent`.
- `SpaceStroker.Api/Controllers/PlayersController.cs` — unchanged (`GET [FromQuery] wallet`, `PATCH body.Wallet`).
- Tests: unit — `GetPlayerPreferencesHandlerTests.WalletProvided_TakesPrecedenceOverSub`,
  `UpdatePlayerPreferencesHandlerTests.WalletInBody_TakesPrecedenceOverSub_WritesToWalletIdentity`;
  integration — `PlayerPreferencesTests.CryptoPreferences_TokenAlsoHasSub_WalletTakesPrecedenceOverSub`.

**Frontend (`monkify-front`)**

- `src/App.vue` — watcher on `[publicKey, selectedGame.paymentMethod]` reloads preferences on the
  game routes, sending the wallet **only** for SolanaToken games.
- `src/components/header/PlayerCustomizationModal.vue` — `isCryptoGame` / `hasWallet` /
  `hasUsableSub` / `useWalletIdentity` / `needsWalletToCustomize`; `walletId()` returns the wallet only
  in a wallet-identity context; the "Connect wallet to customize" panel + `WalletButtons` when gated
  (SAVE hidden); currency `<label>` hidden for SolanaToken; load watcher also watches `publicKey`.
- `src/components/game/GameTerminal.vue` — unchanged; `YOU` / winner / recent-bets resolution benefits
  from the per-wallet name now being loaded and the live notification carrying `externalPlayerId`.
- `src/hooks/useDisplayCurrency.ts`, `src/services/mainService.ts` — unchanged (already
  `loadPreferences(wallet?)` / `getPlayerPreferences(wallet?)` / `updatePlayerPreferences(body)`).

## Related

- [[player-customization-modal]] — the modal this extends (display name + currency).
- [[payment-methods]] — per-game `PaymentMethod`; SolanaToken identity = wallet.
- [[operator-player-identity]] — `OperatorPlayer(OperatorId, ExternalPlayerId)`; crypto id = wallet.

# Feature — "Pick your name" prompt on wallet connect

> Status: **shipped.** Merged via `feature/wallet-connect-name-prompt` on 2026-07-29.
>
> **Frontend-only** (`monkify-front`). No spec in `specs/` — brainstormed and built directly.
>
> ⚠️ **Updated by `features/remove-operators.md` (2026-07-30).** Saving a display name is now
> **signature-gated** (nonce → sign → PATCH), so the prompt signs from the connect flow too. The
> `walletForPrefs` "no usable `sub`" branch is gone — there is no `sub`, and the wallet is the only
> identity — and there is no preferred currency for the handler to leave untouched.

## What it does

A player who connects a wallet **without a display name for that wallet** is asked once, right then,
to pick one. Before this, a nameless player kept playing as `ANON` until they happened to open the
header menu → settings, so most crypto players never set a name at all.

The ask is a small dedicated modal — **not** the full PLAYER SETTINGS modal. The currency selector
and the provably-fair seed controls are noise at the moment someone just connected to play, and they
stay behind the header gear.

## Core rules

- **Trigger.** After the preferences load for the active identity, the prompt opens only when **all**
  of the following hold:
  - the route is a player route (`GameLobby` / `Game` / `GameHistory` / `GameStats`) — so the
    `/wallet-bridge` popup and the admin portal are never interrupted;
  - not embedded (`useEmbed().isEmbedded`) — a modal over a regulated casino's iframe is not ours to
    show (see [[iframe-embedding]]);
  - a **wallet-identity context** with a wallet actually connected (see below);
  - the preferences read **succeeded** — see the `loadPreferences` note;
  - the stored display name is empty;
  - the player has not already dismissed the prompt for that wallet.
- **Once per wallet, remembered.** `SKIP` writes `space-stroker.namePrompt.skipped.<wallet>` to
  `localStorage`; that wallet is never asked again on that browser. Keyed **per wallet** because the
  display name itself is per-wallet identity ([[wallet-display-name]]).
  Backdrop click and `ESC` both behave as `SKIP`, so a dismissal always sticks. Storage unavailable
  (private mode) degrades to "never asked" rather than throwing.
- **A saved name needs no flag.** The gate only fires on a confirmed-empty name, so once a name
  exists the prompt can never open for that wallet again — setting one via the header menu suppresses
  it just as effectively as `SKIP`.
- **Fires on auto-reconnect too, not only a manual click.** The wallet adapter silently reconnecting a
  previously-approved wallet is indistinguishable from a fresh connect, and a nameless player is
  nameless either way. The mount path goes through the same evaluation so an **already-hydrated
  mobile deeplink session** — which never fires the wallet watcher — is still covered.
- **Saving** reuses the existing `PATCH /players/preferences` with `{ displayName, wallet }` (no
  currency: that field no longer exists — see the banner).
  Client-side validation mirrors the backend's 1–20 trimmed characters. `Enter` in the field saves.
- **No backend, DB, DTO or SignalR change.** Nothing new was added to the API for this.

## Two supporting changes it required

- **`loadPreferences` now returns whether the read succeeded.** `displayName` in
  `usePlayerPreferences` is a shared singleton that keeps its previous value when the GET fails — which
  would read as "this player has no name" (and, right after a wallet switch, holds the **previous**
  wallet's name). The prompt only fires on a confirmed-empty name. Existing callers ignore the return
  value.
- ~~**`walletForPrefs` in `App.vue` also treats "the launch token has no usable `sub`" as a
  wallet-identity context**~~ *(moot — the wallet is the only identity now)*, not only "a SolanaToken
  game is selected" — mirroring
  `PlayerCustomizationModal`'s `useWalletIdentity` and matching what [[wallet-display-name]] already
  described. This closes a real gap: the **anonymous Main launch token carries no `sub`**
  (the anonymous Main launch token, since removed) and `selectedGame` is null on the lobby, so the lobby was reading
  preferences against an **empty identity** — the player's saved name never rendered there, and the
  prompt could never fire at the very place the header connect button lives. It cannot mis-route a
  `Standard` player: the new arm only engages when there is no `sub` to route to.

## Key code touchpoints (`monkify-front`)

- `src/hooks/useDisplayNamePrompt.ts` — **new.** Shared singleton: open state, the wallet the open
  prompt is bound to (held separately from the live wallet so a mid-prompt switch can't misfile the
  name), and the per-wallet dismissal memory.
- `src/components/header/DisplayNamePrompt.vue` — **new.** The modal: one name field, `SAVE` + `SKIP`.
  `Teleport to="#app"` for the same stacking-context reason as `PlayerCustomizationModal`.
- `src/App.vue` — `isPlayerRoute()`, the widened `walletForPrefs()`, and `refreshPreferences()` which
  loads the preferences then applies the gate; renders `<display-name-prompt />` alongside
  `<mobile-wallet-modal />`.
- `src/hooks/usePlayerPreferences.ts` — `loadPreferences` returns `Promise<boolean>`.

## Testing

`monkify-front` has no test runner (`lint` + `build` only), and this touches no backend, so no unit
or integration tests apply — the same position as other frontend-only features ([[game-pot-display]],
[[mobile-layout]]). Verified with `yarn lint`, a production build, and a local Docker deploy.

## Related

- [[wallet-display-name]] — per-wallet display name / preference identity. This prompt is the "you
  have no name yet" entry point into it, and finishes the identity rule that doc already described.
- [[player-customization-modal]] — the full settings modal (name + currency) behind the header menu,
  still the place to **change** a name.
- [[remove-operators]] — the launch token (and its empty `sub`) no longer exists.
- [[iframe-embedding]] — why the prompt never shows when embedded.

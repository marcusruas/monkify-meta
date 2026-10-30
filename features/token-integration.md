# Feature — Token integration ($STROKE burn icons, balance, in-app swap, jackpot notice)

> Status: **shipped.** Authoritative spec: [`../specs/token-integration.md`](../specs/token-integration.md)
> (merged via `feature/token-integration` on 2026-10-30, Trello [#212](https://trello.com/c/5DDoD8Qk)).
> Level **`Feature`**. Repos: `monkify-backend`, `monkify-front` (not the LP). Builds on
> [`profile-v2.md`](profile-v2.md) (icons), [`token-launch.md`](token-launch.md) (token surfaces) and
> [`space-jackpot-refactor.md`](space-jackpot-refactor.md) (holder draw). Where the spec and the code
> disagree, the code wins (see "Drift from the spec" below).

## What it does

Gives $STROKE things to do inside the app. Players can **burn $STROKE from their own wallet to unlock
profile icons** (14 burn icons, verified on-chain and owned permanently), see their **$STROKE balance** in
the header profile popup and the icon picker, **buy $STROKE in-app** through a Jupiter swap on the Home
token card (every Buy / GET $STROKE link now leads there), and get a **one-time Space Jackpot notice**
explaining the free holder draw. The burn moves no house money: the tokens are destroyed in the player's
own transaction. Rode along on the branch: Monkey Duel **quick play** (pull with an empty word field) and
a duel reveal/result rework, distilled in [`monkey-duel.md`](monkey-duel.md).

## Core rules

### Burn icons — catalog and price

- 14 icons of the new unlock kind **`Burn`** (`PlayerIcons.UnlockKind.Burn`, front `{ kind: 'burn' }`), in
  this catalog (= picker) order: `hobo`, `bandit`, `mage`, `bag`, `space`, `west`, `nerdson`, `sailor`,
  `droid`, `cave`, `mexico`, `sombrero`, `chedson`, `eddie`. Keys are exact, case-sensitive, identical in
  both repos and **must never be renamed after go-live** (a rename orphans `PlayerIconPurchases` rows).
- Price is **per icon, fixed, in $STROKE UI units, in config**: `GeneralSettings:PlayerIcons:BurnPrices:<key>`
  (env `GeneralSettings__PlayerIcons__BurnPrices__hobo=25000`). Committed placeholder **10000** each.
  `PlayerIconSettings.BurnPriceFor(key, decimals)` returns a price only when the key is a Burn icon, the
  price is `> 0` and `price × 10^decimals` is whole; otherwise **not purchasable** (hidden from the picker,
  purchase recorded `no-price` Unmatched if a burn arrives anyway).
- The front reads prices from `GET /players/icon-prices`, never from the bundle, so the owner can change
  them without a front deploy.

### Burning — the transaction

- One **legacy** `Transaction` from the player's own wallet (fee payer + burn authority): an optional
  ComputeBudget priority fee, one Token-2022 **`BurnChecked`** of exactly the price from the wallet's
  Token-2022 ATA for `Operator:Token:HolderToken`, and one **Memo v2** `spacestroker-icon:<key>`.
  ⚠️ Legacy is load-bearing: Solnet 6.1.0 cannot fetch v0 transactions, so a v0 burn could never verify.
- The front (`useIconBurn`, `utils/iconBurn.ts`) checks the **signed** transaction is byte-identical to the
  one it built with the exact memo (`isExpectedBurnShape`); anything else (a wallet that injected
  instructions, a stripped memo) is **never broadcast** (`not-sent`: no tokens moved).
- **Price consent:** the price is refreshed right before signing; if it changed from the one the player saw,
  nothing is built (`price-changed`) and the strip shows the new price.
- **Retry with the same signature, never a second burn.** The signature is stashed per wallet before sending
  (`localStorage` `space-stroker.icon-burn.pending.{wallet}` plus an in-memory copy). A confirmation timeout,
  `IconBurnPending`, a 429/5xx or no response all keep the stash and the action becomes **FINISH UNLOCK**,
  which re-submits that signature. Only the backend's final refusal clears it, or a burn that provably never
  landed (not on chain and its blockhash expired). While one icon's burn is pending, a burn for another icon
  first retries the pending one and refuses to start a new one.

### Verification — `POST /players/icon-purchases`

Body `{ wallet, iconKey, burnSignature }`, anonymous (the chain is the proof, like ReportDonation). In order:

1. Shape: valid wallet, the key is a `Burn` icon, signature present, ≤ 100 chars and base58 of 64 bytes
   (anything else never reaches the RPC). The holder token must be configured.
2. **Replay by signature (no RPC):** an existing row with the same wallet + icon + `Unlocked` → success
   (same DTO); anything else → refused. A concurrent duplicate hitting the unique index (Postgres 23505) is
   answered by the winner's row under the same rule.
3. **Self-excluded wallet → refused**, chain never read (no `Player` row = not excluded; a wallet can burn
   before it ever bets). The picker also blocks before any signature is requested.
4. On-chain check `SolanaService.VerifyIconBurnAsync` → pure `DescribeIconBurn` (`IconBurnCheck`):
   - **`Unreadable`** (RPC down, not found, not yet confirmed) → `IconBurnPending`, nothing written.
   - **`NotABurn`** → refused, nothing written, Warning log. Covers: failed tx, fee payer ≠ wallet, not
     exactly one token instruction, not `BurnChecked`, zero amount, wrong decimals, burn from another
     account / another mint / another authority, the ATA not dropping by exactly the amount, and the
     **mint-wide delta** (Σ post − pre over every account of the mint) not equal to −amount (a transfer
     nets to 0, so a transfer is never a burn).
   - **`Burned`** with a null `Reason` = a clean burn. **`Burned` with a `Reason`** = a real burn in an
     unclean shape: `extra-instructions` (top-level programs other than ComputeBudget, Token-2022 and Memo,
     e.g. a wallet's injected guard instructions) or `multiple-memos` (two or more memos; the first is read).
     These **never unlock**.
5. **A memo naming a different catalog Burn key** than the request → refused with **no row** (anti-grief:
   signatures are public, so a third party must not be able to pin a victim's burn to the wrong icon).
6. Match: clean burn + price configured + `amount ≥ price` + memo exactly `spacestroker-icon:<key>` →
   row `Unlocked`. Otherwise the burn is **still recorded**, as `Unmatched` (Status 2) with
   `UnmatchedReason` `extra-instructions` / `multiple-memos` / `no-price` / `below-price` / `memo-mismatch`,
   logged at **Error** (`Unmatched icon burn {Signature} …`), and the caller is refused. Tokens are never
   silently dropped.
7. Overpaying is accepted and recorded with the real amount. Burning again for an icon already owned is
   recorded `Unlocked` again (Warning log) and succeeds.
8. On `Unlocked`, drops `PlayerProfile:{wallet}`, `PlayerLevel:{wallet}` and `StrokeBalance:{wallet}` and
   returns `PlayerIconPurchaseDto { iconKey, unlockedAvatars }` (catalog order).

- ⚠️ **Messaging (`constraints.md` §6):** every refusal is the one sentence `IconUnlockRefused` *"That icon
  could not be unlocked."*; the only other message is the retryable `IconBurnPending` *"Your burn could not
  be confirmed yet. Please try again."* Real reasons go to the log only.
- **No refunds.** The picker says *"Burned tokens are gone for good. No refunds."* before signing.

### Ownership and equipping

- `PlayerIconPurchases` is the **first persisted icon inventory**; trophy/free/staff icons stay derived on
  read ([`profile-v2.md`](profile-v2.md)). A Burn icon is unlocked iff the wallet has an `Unlocked` row for
  its key (`IPlayerIconPurchases.OwnedKeysAsync`, loaded **live** in the profile read, not from the 60 s
  cached body).
- **Permanent:** selling or moving $STROKE never removes a bought icon; `PlayerAvatarGuard` clears only
  Trophy / AllTrophies picks.
- **Purchase does not equip.** Equipping is the unchanged signed `PATCH /players/preferences` (`avatarId`);
  after an unlock the picker selects the icon so one SAVE equips it. An unowned burn key is the same generic
  refusal as any locked icon.
- The **unavailable** profile body still lists the free keys only, so it never reveals a purchase.

### Icon picker

- Burn icons render as their own group after the player icons, **only when `tokenLive && !isEmbedded`** and
  the key is priced (an owned one always shows; a pending one shows so FINISH UNLOCK stays reachable).
- Selecting a locked burn icon previews it. The strip shows the labelled price, `YOU HOLD` balance (`—` when
  unknown, `…` while loading) and the no-refund line, with one action: **BURN & UNLOCK** · **GET $STROKE**
  (balance known and below price → `/#swap`, closes the picker) · **FINISH UNLOCK** (pending stash) ·
  disabled *"Unavailable while your self-exclusion is active."* · disabled **OPEN IN A WALLET BROWSER TO
  UNLOCK** (mobile deeplink session: no adapter to sign; Orbit can never burn). An unknown balance does not
  block the burn (the wallet refuses an over-balance burn itself).
- A **PROFILE PREVIEW** panel (ArcadeModal's optional second `aside` panel, desktop; a compact strip on
  phones) shows the selected icon with its state line: `EQUIPPED` for the icon on the profile today, `OWNED`
  for other unlocked burn icons, or the price.

### $STROKE balance

- `GET /players/stroke-balance?wallet=` → `StrokeBalanceDto { symbol, decimals, balance }`, a display read:
  always HTTP 200, `balance: null` when unknown (invalid wallet, token not configured, read failed). Reads
  the Token-2022 ATA through `HolderBalanceReader` (`DevAssumedBalance` outside Production); only a
  non-null read is cached, 15 s (`StrokeBalance:{wallet}`).
- Front `useStrokeBalance` (module singleton keyed on `effectivePublicKey`, so a deeplink session sees it
  too; stale responses for a previous wallet are dropped; no polling). Shown as a **`$STROKE` row under
  WALLET in the header profile popup** (`ProfileMenuHead.vue`, chevron, click → `/#swap`) and in the picker.
  **Token count only, no USD**; `null` renders `—`, never `0`. Refreshes after a burn and after a Jupiter
  swap success. Not on the profile page; no public badge.

### Swap card and buy links

- `HomeTokenCard.vue` (`id="swap"`) embeds `JupiterSwap.vue`: the **Jupiter Plugin** (`plugin-v1.js`,
  integrated mode), SOL in, output locked to `VUE_APP_TOKEN_MINT`, the connected adapter wallet passed
  through, connect requests routed to the app's own connect. Compact right column; below the two-column
  width it sits behind a **GET** key that a `/#swap` landing opens.
- **Fallback** `Swap on Jupiter ↗` (`TOKEN_JUPITER_URL`, new tab, `home_jupiter`) when the script errors,
  `window.Jupiter` is missing 8 s after injection, `init` throws, or the session is a mobile deeplink.
  Never a blank box. Jupiter is **mainnet-only**: locally only render + fallback can be QA'd.
- Theming: the plugin's `--jupiter-plugin-*` RGB-triplet variables scoped on the target element, plus a
  stylesheet appended once into the plugin's open shadow root (`src/components/home/jupiterTheme.ts`).
  ⚠️ The shadow stylesheet targets the plugin's Tailwind class names, **verified against plugin-1.0.17**; a
  plugin build that renames them silently drops those rules (colours survive, they use the variables).
- pump.fun is a small secondary `Also on pump.fun ↗` link (`home_buy`). The jackpot mechanic title on the
  card reads **"Play Space Jackpot free"**.
- ⚠️ **Every Buy / GET $STROKE link goes to `/#swap` same-tab** (`useSwapTarget().goToSwap(source)`), scrolls
  to and pulses the card; already on Home it bumps a focus tick instead of navigating, so a repeat click
  still scrolls. This **reverses** [`token-launch.md`](token-launch.md) §6's "new tab is load-bearing" rule
  **for buy links only**; Chart, Solscan and Streamflow links stay new tab. Sources: `menu`,
  `game_terminal`, `race_screen`, `guide_buy`, `home_community`, `icon_picker`, `jackpot_notice`,
  `menu_balance`.

### First-time Space Jackpot notice

- Once per browser (`localStorage` `space-stroker.jackpot.first-visit-seen`), on the first open of a
  **holder-only** type (`LotteryParameters.HolderOnly`), gated on `tokenLive` and not embedded. Triggered from
  `useGame`'s holder watcher; guards mirror `useFirstWinUpsell` (no other modal open incl. the icon picker
  and donate sheet, not recovering, not idle-disconnected, not relaunching, terms accepted). A blocked open is
  **not** marked seen and retries on the next trigger; any dismissal marks it seen; storage failure = seen.
- `JackpotHolderNotice.vue` (`ArcadeModal`, compact, `SPACE JACKPOT`): free daily draw, no stake;
  `Hold at least N $STROKE`; `You hold X` (connected only, `—` when unknown); balance checked at entry and at
  the draw. Not eligible / disconnected → **GET $STROKE** (`/#swap`) + **GOT IT**; eligible → *"You're in:
  enter today's draw"* + **ENTER**. Copy never implies $STROKE is a stake.

### Token copy

- Burn prices, the popup balance and the notice's N / X are **token counts**, the third owner-sanctioned
  exception to `token-launch.md` §7 "percentages only". No em dashes in any of this player copy.

## Edge cases

- Burn landed, API down / RPC lagging → `IconBurnPending`, nothing written; FINISH UNLOCK re-submits the
  same signature until it unlocks. A lost stash is harmless: re-submitting the signature works any time.
- Same signature from another wallet or for another icon → refused. A Unmatched row's signature can never
  unlock later (replay rule).
- Burn below the price, wrong memo, no price, extra programs, two memos → `Unmatched` row + Error log, the
  player sees only the generic refusal. **No admin tooling resolves these** (see gotchas).
- Memo NUL bytes are stripped before storing (Postgres varchar rejects U+0000).
- Embedded or token not live → no swap card, no popup row, no notice, no burn icons.

## Key code touchpoints

**Backend (`monkify-backend/src`)**
- `SpaceStroker.Domain/Players/PlayerIcons.cs` (`UnlockKind.Burn`, 14 rules);
  `SpaceStroker.Domain/Players/Entities/PlayerIconPurchase.cs` (+ `IconPurchaseStatus` Unlocked 1 / Unmatched 2);
  `SpaceStroker.Domain/Configs/Entities/GeneralSettings.cs` (`PlayerIconSettings.BurnPrices`, `BurnPriceFor`).
- `SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` (`PlayerIconPurchases`, unique
  `BurnSignature`, index `(Wallet, Status)`); migration `Migrations/20261027154247_AddPlayerIconPurchases.cs`.
- `Infrastructure/Services/Solana/SolanaService.cs` (`VerifyIconBurnAsync`, static `DescribeIconBurn`),
  `Services/Solana/IconBurnCheck.cs`.
- `Infrastructure/Handlers/Players/PurchasePlayerIcon/`, `GetStrokeBalance/`, `GetIconPrices/`;
  `Services/Players/PlayerIconPurchases.cs`, `PlayerIconRules.cs`, `PlayerAvatarGuard.cs`;
  `UpdatePlayerPreferences/` and `GetPlayerProfile/` handlers (owned keys); `Services/Cache/CacheHelper.cs`.
- `SpaceStroker.Api/Controllers/PlayersController.cs` (3 routes + OPTIONS);
  `SpaceStroker.Common/spacestrokersettings.json` (`BurnPrices`, rate limits `post:/players/icon-purchases`
  10/min, `get:/players/stroke-balance` and `get:/players/icon-prices` 30/min);
  `Common/Resources/ErrorMessages.resx` (`IconUnlockRefused`, `IconBurnPending`).
- Tests: `UnitTests/Services/IconBurnVerificationTests.cs`, `UnitTests/Handlers/PurchasePlayerIconHandlerTests.cs`,
  `GetStrokeBalanceHandlerTests.cs`, `GetIconPricesHandlerTests.cs`, `PlayerIconRulesTests.cs`,
  `PlayerAvatarGuardTests.cs`, `PlayerIconSettingsBindingTests.cs`; integration
  `SpaceStroker.Tests.Integration/IconBurnFlowTests.cs` (7 tests: happy burn → unlock → equip, replay,
  permanence, unreadable → retry, self-excluded, balance + prices, concurrent replay).

**Frontend (`monkify-front/src`)**
- `constants/playerIcons.ts` + `assets/player-icons/<key>.png` / `<key>-48.png`; `constants/token.ts`
  (`TOKEN_JUPITER_URL`).
- `utils/iconBurn.ts` (builder, `isExpectedBurnShape`), `hooks/useIconBurn.ts`,
  `components/player/PlayerIconPicker.vue`, `components/ui/ArcadeModal.vue` (`aside` slot, `asideTitle`).
- `hooks/useStrokeBalance.ts`, `components/header/ProfileMenuHead.vue`, `services/mainService.ts`,
  `types/index.ts`.
- `components/home/HomeTokenCard.vue`, `components/home/JupiterSwap.vue`, `components/home/jupiterTheme.ts`,
  `hooks/useSwapTarget.ts`, `hooks/useAnalytics.ts` (`TokenClickSource`).
- Buy links: `components/GameHeader.vue`, `components/game/GameTerminal.vue`, `components/game/RaceScreen.vue`,
  `components/guide/sections/GuideToken.vue`, `components/home/HomeCommunity.vue`.
- `hooks/useFirstJackpotNotice.ts`, `components/game/JackpotHolderNotice.vue`, `hooks/useGame.ts`.

## Drift from the spec (code wins)

- Spec decision #12 made any extra top-level program `NotABurn` (refused, not recorded). Shipped: a proven
  burn with extra programs is recorded **`Unmatched` (`extra-instructions`)**, and a burn with two or more
  memos is **`Unmatched` (`multiple-memos`)**, because the tokens are already gone. ComputeBudget stays
  allowed.
- A memo naming a **different catalog burn key** is refused with **no row** (anti-grief), not recorded
  `memo-mismatch`.
- The front refuses to broadcast a signed transaction that differs from the one it built, and asks for
  price consent when the price changed.
- Integration suite has 7 tests (spec listed 6; concurrent replay added).

## Verification (as shipped)

Devnet QA with a Token-2022 stand-in mint passed: happy burn + equip in the UI, replay, cross-key refused,
below-price Unmatched, wrong mint, transfer refused, ComputeBudget accepted, extra System transfer
Unmatched, two memos Unmatched. Unit suite 3447 green before merging `main`; `IconBurnFlowTests` 7/7.

## Gotchas

- ⚠️ **`Unmatched` rows need a manual SQL fix.** There is no admin route or tool: review
  `SELECT * FROM "PlayerIconPurchases" WHERE "Status" = 2`, and to grant the icon after a real mistake, update
  that row to `Status = 1` (and drop the wallet's `PlayerProfile:` / `PlayerLevel:` cache, or wait the TTL).
  The Error log line is the only alert.
- ⚠️ Prices are placeholders (10000). Set real ones in the deploy env before go-live; a missing or
  fractional-raw price hides the icon.
- ⚠️ Burn icons and the swap are **invisible locally** without mock mint/treasury env (see the token
  surfaces memory); real burns on devnet need a Token-2022 stand-in mint pointed at by both
  `GeneralSettings__Operator__Token__HolderToken__MintAddress` and `VUE_APP_TOKEN_MINT`.
- The Jupiter shadow-root stylesheet is class-name fragile (plugin-1.0.17); re-check after a plugin update.
- Never use the classic `GetTokenBalance` for $STROKE: it reads Token-2022 holders as 0.

## Known gaps (as shipped)

- Mobile deeplink sessions see prices and balance but cannot burn (no adapter); a deeplink burn hop is a
  follow-up (it also needs deeplink icon SAVE).
- The **PICK YOUR NAME** modal can stay mounted under the icon picker right after a connect.
- The hat tops of Mage, Sailor, Sombrero and Mexico touch the art's top edge.
- Out of scope: USD value, post-win buy nudge, "$STROKE spent" stat, feed entry for burns, public holder
  badge, buyback-and-burn, staking $STROKE, LP changes, admin UI for Unmatched rows.

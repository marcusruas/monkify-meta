# Token integration (#212) — Frontend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Burn-to-unlock icons in the picker, a `$STROKE` balance in the header popup and picker, a Jupiter swap inside the Home `$STROKE` card that every buy link now leads to (`/#swap`, same tab), and a one-time Space Jackpot notice.

**Architecture:** Three new singleton composables (`useStrokeBalance`, `useSwapTarget`, `useIconBurn`) plus `useFirstJackpotNotice`; a pure tx builder `utils/iconBurn.ts`; two new components (`JupiterSwap.vue`, `JackpotHolderNotice.vue`). Everything is gated on `tokenLive && !isEmbedded`.

**Tech Stack:** Vue 3 + TS (Vue CLI 5 / webpack, `Buffer` provided by `ProvidePlugin`), `@solana/web3.js`, `@solana/spl-token` ^0.4.6 (first import in `src`), `solana-wallets-vue`, Tailwind 3 + DaisyUI, axios.

**Spec:** `monkify-meta/specs/token-integration.md` §7, §10.2, §12, §13. Backend contract: `2026-10-27-token-integration-backend.md`. Worktree `C:\Users\kurok\Desktop\monkify\.worktrees\token-integration\monkify-front` on `feature/token-integration`; paths below are relative to its `src\`.

## Global Constraints

- **No test runner exists.** Each task's verification is `yarn lint` + `yarn build` (type-check runs in the build) and, where noted, a browser check against the local stack. Do not add a test framework.
- Setup once: copy the hub's `yarn.lock` and `.env.local` into the worktree (both gitignored), `yarn install --frozen-lockfile`. For QA add mock base58 `VUE_APP_TOKEN_MINT` (and treasury) to the worktree `.env.local`; it is gitignored, never commit it. Serve on a verified-free port (8081 is often taken by another worktree).
- Every new surface is hidden unless `tokenLive && !isEmbedded` (`constants/token.ts`, `useEmbed().isEmbedded` — a plain boolean).
- Token copy: token counts allowed only in the picker, the popup `$STROKE` row and the jackpot notice. **No em dashes** in any new copy.
- Exact copy: `Burned tokens are gone for good. No refunds.` · `Unavailable while your self-exclusion is active.` · `OPEN IN A WALLET BROWSER TO UNLOCK` · `BURN & UNLOCK` · `GET $STROKE` · `FINISH UNLOCK` · `That icon could not be unlocked.` · toast `Icon unlocked` · `Swap on Jupiter ↗` · `Also on pump.fun ↗` · guide button `Get $STROKE`. Notice lines per spec §7.7 verbatim.
- Burn tx must be a **legacy `Transaction`** (backend cannot read v0). Raw amount from integer math (`BigInt`), never float multiply.
- Memo is exactly `spacestroker-icon:<key>`; Memo v2 program `MemoSq4gqABAXKb96qnH8TysNcWxMyWCqXgDLGmfcHr`. No new npm package.
- Catch blocks that show their own message set `err.handled = true` (see `services/handlers.ts`).
- Prettier/ESLint must pass (`yarn lint`); use `yarn lint:fix` only on files you touched.

## Review Focus

1. **Wallet switch while the picker/popup is open**: balance, prices-gated actions and pending stash must follow the *new* wallet, never show the previous wallet's balance or FINISH UNLOCK. → Task 1 (`useStrokeBalance` keyed by wallet, drops stale responses), Task 5 (stash key includes wallet).
2. **`/#swap` clicked while already on `/#swap`** (second click from Home community tile): must still scroll + highlight. → Task 2 (`focusSwap` tick path, no router push).
3. **Jupiter script loads but `init` throws, or never defines `window.Jupiter`**: fallback link within 8 s, no blank box. → Task 3.
4. **Price arrives as a non-integer UI amount** (e.g. `12.5` at 6 decimals): raw = `12500000n` exactly. → Task 5 `toRaw` helper with a dev assertion in the browser check.
5. **Jackpot notice while another modal (icon picker, donate, first-win upsell) is open**: not shown, not marked seen, shown on the next trigger. → Task 6 guards.

---

### Task 1: Types, services, analytics sources, `useStrokeBalance`

**Files:**
- Modify: `types/index.ts` — `StrokeBalance { symbol: string; decimals: number; balance: number | null }`, `IconPrices { symbol: string; decimals: number; mint: string; prices: Record<string, number> }`, `PlayerIconPurchaseBody { wallet; iconKey; burnSignature }`, `PlayerIconPurchaseResult { iconKey: string; unlockedAvatars: string[] }`
- Modify: `services/mainService.ts` (pattern `getHolderEligibility` L400, `linkPartnerCode` L231; add to default export L426): `getStrokeBalance(wallet)` → GET `/players/stroke-balance` `{params:{wallet}}`; `getIconPrices()` → GET `/players/icon-prices`; `purchasePlayerIcon(body)` → POST `/players/icon-purchases`. Each returns `DefaultResponse<T>` data like its neighbours.
- Modify: `hooks/useAnalytics.ts` L191-199 — extract `export type TokenClickSource = 'home_buy' | 'home_read_more' | 'home_community' | 'menu' | 'guide_buy' | 'guide_supply' | 'game_terminal' | 'race_screen' | 'icon_picker' | 'jackpot_notice' | 'menu_balance' | 'home_jupiter'` and use it in `trackTokenClick`.
- Modify: `constants/token.ts` — `TOKEN_JUPITER_URL = \`https://jup.ag/swap/SOL-${TOKEN_MINT}\``, `MEMO_PROGRAM_ID` string.
- Create: `hooks/useStrokeBalance.ts`

**Interfaces — Produces:**
```ts
export const useStrokeBalance = () => ({
  balance,          // Ref<number | null | undefined>  undefined = loading, null = unknown
  symbol,           // Ref<string>
  refresh,          // (): Promise<void>  reads for the current connected wallet
});
```
Module singleton; keyed by `useWallet().publicKey` (base58). Watches the wallet (reset to `undefined` + refresh on change, `null`-free when disconnected). Responses for a wallet that is no longer current are dropped. No polling. Network failure → `null`.

- [ ] Implement → `yarn lint` + `yarn build` green → commit `Stroke balance, icon price/purchase services, token click sources (#212)`.

### Task 2: `useSwapTarget` + every buy link → `/#swap`

**Files:**
- Create: `hooks/useSwapTarget.ts`
- Modify: `components/home/HomeTokenCard.vue` — `<home-section id="swap" tabindex="-1" :class="{ 'swap-highlight': highlighted }" …>` (falls through to `<section>`), `scroll-margin-top: 64px` (matches `HEADER_SCROLL_OFFSET` in `main.ts`), highlight pulse CSS with a `prefers-reduced-motion` variant; on mount `if (route.hash === '#swap') focusSwap()`; watch the focus tick.
- Modify the links (markup `<a href="/#swap" @click.prevent="goToSwap('<source>')">`, keep existing classes/labels):
  - `components/GameHeader.vue` L567-578 (☰ token row, `TOKEN_LP_URL` today) → `menu`; drop `target`/`rel`; aria-label `Buy ${TOKEN_TICKER}`; still closes the menu. Rewrite the "NEW TAB IS LOAD-BEARING" comment block (L548-566) to the new rule (owner reversal 2026-10-26, #212: buy links go to `/#swap` same tab; placed bets unaffected).
  - `components/game/GameTerminal.vue` L1613-1621 → `game_terminal`
  - `components/game/RaceScreen.vue` L565-572 → `race_screen`
  - `components/guide/sections/GuideToken.vue` L672-680 → `guide_buy`, label `Get $STROKE`. Chart ↗ stays external.
  - `components/home/HomeCommunity.vue` L69-85 → `home_community`; remove the duplicate `onCoin` tracking (goToSwap tracks).
- `TOKEN_LP_URL` stays exported (comment: no consumer left, kept on purpose).

**Interfaces — Produces:**
```ts
export const useSwapTarget = () => ({
  goToSwap,   // (source: TokenClickSource) => void  — trackTokenClick, then push {name:'Home', hash:'#swap'} or focusSwap() when already on Home
  focusSwap,  // () => void — bumps focusTick
  focusTick,  // Readonly<Ref<number>> — HomeTokenCard watches it
});
```
HomeTokenCard's focus routine: `nextTick` → `requestAnimationFrame` → `scrollIntoView({behavior:'smooth', block:'start'})`, `focus({preventScroll:true})`, highlight 2 s, re-scroll once after 300 ms (async `GameTypes` above shifts layout).

- [ ] Implement → lint + build → browser: from a game page and from Home, each rerouted link lands scrolled + highlighted; Chart/Solscan still new tab → commit `Every buy link goes to the Home swap card (#212)`.

### Task 3: Jupiter swap inside the `$STROKE` card

**Files:**
- Create: `components/home/JupiterSwap.vue`
- Modify: `components/home/HomeTokenCard.vue` — place `<JupiterSwap />` above the CTA rail (L236); turn the "Buy on pump.fun" button (L257-265) into a small secondary text link `Also on pump.fun ↗` (still `TOKEN_BUY_URL`, new tab, source `home_buy`); update the CTA comment block (L247-256) to say the swap is the primary buy path now and pump.fun is secondary. `Read more` unchanged.

Behaviour (spec §7.5): lazy-inject `https://plugin.jup.ag/plugin-v1.js` once (module-level promise, `injectTag` pattern from `useAnalytics.ts:63-68`); `window.Jupiter.init({ displayMode: 'integrated', integratedTargetId: 'jupiter-swap', formProps: { initialInputMint: 'So11111111111111111111111111111111111111112', initialOutputMint: TOKEN_MINT, fixedMint: TOKEN_MINT }, enableWalletPassthrough: true, onRequestConnectWallet: () => useWalletConnectRequest().requestConnect(), onSuccess: () => useStrokeBalance().refresh(), branding: { name: 'Space Stroker' } })`; `syncProps({ passthroughWalletContextState })` built from `useWallet()` refs on mount + `watch` of `publicKey`/`connected`/`wallet`. Fallback state (render `Swap on Jupiter ↗` → `TOKEN_JUPITER_URL`, new tab, source `home_jupiter`) when: script `onerror`, `window.Jupiter` absent 8 s after injection, `init` throws, or `useMobileWallet().isMobileDeeplinkContext()` is true. Set `--jupiter-plugin-primary` to the brand orange once. Declare a minimal `window.Jupiter` type locally in the component.

- [ ] Implement → lint + build → browser (devnet local): widget box renders or, with the script blocked in DevTools, the fallback link appears within 8 s → commit `Jupiter swap in the Home $STROKE card (#212)`.

### Task 4: `$STROKE` row in the header profile popup

**Files:**
- Modify: `components/header/ProfileMenuHead.vue` — new `<button class="profile-row text-primary">` right after the WALLET row (ends L164), `v-if="tokenLive && !isEmbedded && wallet"`: `AppIcon name="token"`, label `$STROKE`, value `balance === undefined ? '…' : balance === null ? '—' : balance.toLocaleString('en-US', { maximumFractionDigits: 2 })`; click → `goToSwap('menu_balance')`. Call `refresh()` when the popup head mounts/opens.

- [ ] Implement → lint + build → browser: row shows the dev balance; blocking `/players/stroke-balance` shows `—` → commit `$STROKE balance in the profile popup (#212)`.

### Task 5: Burn icons — catalog, tx builder, `useIconBurn`, picker

**Files:**
- Modify: `constants/playerIcons.ts` — `PlayerIconUnlock` gains `| { kind: 'burn' }`; 10 entries `burn-01`…`burn-10`, names `Burn 1`…`Burn 10`, after `beanie`.
- Create: `assets/player-icons/burn-01.png` … `burn-10.png` and `burn-NN-48.png` — byte copies of `edson.png` / `edson-48.png` (placeholder art; file names are final).
- Create: `utils/iconBurn.ts`
- Create: `hooks/useIconBurn.ts`
- Modify: `components/player/PlayerIconPicker.vue`

**Interfaces — Produces:**
```ts
// utils/iconBurn.ts
export const ICON_MEMO_PREFIX = 'spacestroker-icon:';
export const toRaw = (ui: number, decimals: number): bigint;   // string-based: split on '.', pad/trim fraction, no float multiply; throws if more fraction digits than decimals
export const buildIconBurnTransaction = (connection: Connection, owner: PublicKey, mint: PublicKey, iconKey: string, priceUi: number, decimals: number) => Promise<Transaction>;
// legacy Transaction: optional priority ComputeBudget exactly like useConnection.buildTransferTransaction L171-176,
// createBurnCheckedInstruction(getAssociatedTokenAddressSync(mint, owner, false, TOKEN_2022_PROGRAM_ID), mint, owner, toRaw(...), decimals, [], TOKEN_2022_PROGRAM_ID),
// memo TransactionInstruction (keys: owner signer, data Buffer.from(ICON_MEMO_PREFIX + iconKey, 'utf8')); recentBlockhash 'confirmed'; feePayer owner.

// hooks/useIconBurn.ts
export const useIconBurn = () => ({
  prices,          // Ref<IconPrices | null>  loaded once per session by loadPrices()
  loadPrices,      // () => Promise<void>
  pendingFor,      // (wallet: string) => { iconKey: string; signature: string } | null   (localStorage space-stroker.icon-burn.pending.{wallet}, try/catch)
  burning,         // Ref<boolean>
  burn,            // (iconKey: string) => Promise<'unlocked' | 'pending' | 'refused' | 'cancelled'>
  canBurnHere,     // ComputedRef<boolean>  false when no adapter signTransaction (mobile deeplink) — decision #16
});
```
`burn()` follows spec §7.3 steps 1–5 exactly (pending stash → skip to POST; stash before `confirmSignature`; confirmation timeout ≠ failure; on success clear stash, `forgetPlayerProfile(wallet)` from `hooks/usePlayerProfile.ts`, `useStrokeBalance().refresh()`, toast `Icon unlocked`; `IconBurnPending`/network → keep stash, `'pending'`; refusal → clear stash, `'refused'`; wallet rejection → `'cancelled'`, nothing stashed). Send through the adapter: `signTransaction` → `sendRawTransaction(…, { preflightCommitment: 'confirmed' })`, fallback `sendTransaction`. Distinguish the refusal from pending by the API message text (`IconBurnPending` sentence = pending; any other 4xx = refused).

Picker changes (spec §7.3):
- Burn cells render as a separate group after the existing grid, only when `tokenLive && !isEmbedded` and `prices.value?.prices[key]` exists; owned burn keys (in `unlockedAvatars`) behave like unlocked icons.
- `pick()` (L95) lets a **locked burn** cell be selected for preview (other locked kinds still ignored); burn cell hint = `{price} $STROKE`.
- `#strip` for a selected locked burn icon: `PRICE {n} $STROKE · YOU HOLD {x} $STROKE` (`—` when null), and `Burned tokens are gone for good. No refunds.`; existing ERR strip still wins when `errorMessage` is set.
- Footer primary action replaces SAVE for that selection, first match wins: self-excluded (`useSelfExclusion().selfExcluded`) → disabled + `Unavailable while your self-exclusion is active.`; `!canBurnHere` → disabled `OPEN IN A WALLET BROWSER TO UNLOCK`; pending stash for this wallet+key → `FINISH UNLOCK`; balance known and `< price` → `GET $STROKE` (`goToSwap('icon_picker')` then `close()`); else `BURN & UNLOCK`. Balance unknown never blocks.
- After `'unlocked'`: keep the icon selected so SAVE equips it. After `'refused'`: `errorMessage = 'That icon could not be unlocked.'`.
- On open: `loadPrices()`, `useStrokeBalance().refresh()`.

- [ ] Implement → lint + build → browser (Manual QA §13.2-3 with a devnet Token-2022 stand-in mint; at minimum verify the picker states with the mock mint and a wallet without the token: price, balance `—`/0, GET $STROKE, no-refund line, self-excluded disabled) → in DevTools console assert `toRaw(12.5, 6) === 12500000n` and `toRaw(10000, 6) === 10000000000n` → commit `Burn-to-unlock icons in the picker (#212)`.

### Task 6: First-time Space Jackpot notice

**Files:**
- Create: `hooks/useFirstJackpotNotice.ts` (pattern `hooks/useFirstWinUpsell.ts` L80-117)
- Create: `components/game/JackpotHolderNotice.vue` (`ArcadeModal` `size="compact"` title `SPACE JACKPOT`)
- Modify: `hooks/useGame.ts` holder watcher L3173-3190 — when `isHolder && gameTypeId` and the round payload is loaded, call `useFirstJackpotNotice().maybeOpen()`; also call it once eligibility first resolves (so the "you hold" line has data), guarded by the same function.
- Modify: `components/GameHeader.vue` — mount `<JackpotHolderNotice v-if="tokenLive && !isEmbedded" />` next to `OrbitFirstWinModal` (L605-612).

**Interfaces — Produces:**
```ts
export const useFirstJackpotNotice = () => ({ isOpen, maybeOpen, dismiss });   // storage key 'space-stroker.jackpot.first-visit-seen'; storage failure ⇒ treated as seen
```
Guards in order: `tokenLive`, `!seen`, `!isEmbedded`, `!isOpen`, no `settingsOpen/fairOpen/orbitOpen`, first-win upsell not open, icon picker not open (`usePlayerIconPicker().isOpen`), donate modal not open, `!recovering`, `!isIdleDisconnected`, `!relaunching`, `termsAccepted` (same sources `useFirstWinUpsell` reads). Blocked → not marked seen. `dismiss()` marks seen on every exit.

Body (spec §7.7 verbatim): N = `gameInstance.holderMinimum ?? eligibility.minimum` formatted like `formatHolderMinimum`; `You hold {X} $STROKE.` only when connected (`null` → `—`). Footer: eligible → heading `You're in: enter today's draw`, primary `ENTER` (dismiss), secondary `GET $STROKE`; otherwise primary `GET $STROKE` (`goToSwap('jackpot_notice')` + dismiss) and `GOT IT`.

- [ ] Implement → lint + build → browser: clear localStorage, open Space Jackpot → notice once; reload → not again; disconnected variant omits "you hold" → commit `First-time Space Jackpot holder notice (#212)`.

### Task 7: Full verification

- [ ] `yarn lint` and `yarn build` green.
- [ ] Browser pass over spec §13 items 5–10 on the local stack (`?embed=1` hides swap card, popup row, notice and burn icons).
- [ ] Revert mock env (worktree `.env.local` is gitignored; confirm `git status` shows no env/lockfile), push `feature/token-integration`.

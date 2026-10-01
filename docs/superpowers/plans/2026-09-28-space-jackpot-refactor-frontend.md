# Space Jackpot — Free Daily Draw for $STROKE Holders — Frontend + LP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The app and the landing page present Space Jackpot as a free daily draw for $STROKE holders: the lobby card and terminal show the holder minimum, the frozen SOL pot, the countdown and the wallet's $STROKE balance (✓/✗); entering signs a `jackpot-entry` message (desktop and mobile deeplink) instead of sending SOL; below-N players get the specific message and a GET $STROKE link **before** any signature; voided/paused states are shown; all jackpot copy says "token holders get a free daily lottery draw".

**Architecture:** New composable `useHolderJackpot` (eligibility polling via `GET /games/{typeId}/holder-eligibility`, the message builder, desktop + mobile signing). `useGame.runBet` gains a holder branch that short-circuits before wallet-source selection and never touches the transfer path. Display components read new optional fields from the round payload. Copy changes are plain text edits, app and LP twins changed together.

**Tech Stack:** Vue 3 + TypeScript (Vue CLI, yarn), `solana-wallets-vue`, Astro 5 (LP, npm).

**Spec:** `monkify-meta/specs/space-jackpot-refactor.md` §7, §8. Backend contract: `docs/superpowers/plans/2026-09-28-space-jackpot-refactor-backend.md` (Tasks 7-8 define the wire shapes).

## Global Constraints

- Worktrees: `C:\Users\kurok\Desktop\monkify\.worktrees\space-jackpot-refactor\monkify-front` and `...\monkify-lp`, branch `feature/space-jackpot-refactor`. Never edit the hubs. **No commits** until the user approves (implement-feature Step 9).
- The front has **no unit-test runner** (no jest/vitest). Each task's verification = `yarn lint` (0 errors) + `yarn build` (type-check) from the front worktree, plus the browser checks listed. LP verification = `npm run build`.
- Wire contract (camelCase): round (`GameInstance`) gains `holderOnly?: boolean`, `holderMinimum?: number | null`, `houseFundedPot?: number | null`, `entriesPaused?: boolean`; bet rows gain `voided?: boolean`; `lotteryParameters.holderOnly?: boolean`; bet body gains `walletProofNonce?`, `walletProofSignature?`. Eligibility: `{ wallet, symbol, minimum, balance: number | null, eligible, entriesPaused }`.
- Entry message, byte-identical to the backend (`JackpotEntryMessages.Build`), LF-joined:
  `['Space Stroker jackpot entry', `Wallet: ${wallet}`, `Round: ${gameId}`, `Word: ${word}`, `Nonce: ${nonce}`].join('\n')` — `gameId` is the round id string as received (lowercase GUID). Signature = Base64.
- Nonce purpose `'jackpot-entry'`.
- Exact player-facing strings:
  - Below N: `Hold at least ${formatN(minimum)} $STROKE to enter today's draw.` where `formatN = n.toLocaleString('en-US')` (1,000,000) — use `TOKEN_TICKER` for `$STROKE`.
  - Paused: `Jackpot paused — entries reopen with the next draw.`
  - Terminal voided: `> TICKET VOIDED — BALANCE WAS BELOW ${formatN(N)} $STROKE AT THE DRAW.`
  - Terminal paused: `> JACKPOT PAUSED — ENTRIES REOPEN WITH THE NEXT DRAW.`
  - Headline copy everywhere: **"Token holders get a free daily lottery draw."**
- Gate every holder behaviour on `isHolderLottery(game)` (`lotteryParameters.holderOnly`) or `gameInstance.holderOnly` — never on the name.
- `TOKEN_BUY_URL` is dead when `TOKEN_MINT` is empty: render GET $STROKE only when `tokenLive` is true.
- Copy rule reversal: comments that say "nothing may imply the token can place a bet" must be reworded to "the token never STAKES a bet; holding it grants a FREE Space Jackpot entry (card #196 — deliberate)" so nobody reverts the copy.
- Commit trailer (Step 10 only): `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`.

## Spec corrections (front)

| # | Spec says | Plan does |
|---|---|---|
| F-C1 | `Bet.voided` | `AppliedBet.voided` (there is no `Bet` type); also `GameVerificationBet.voided` |
| F-C2 | `submitBet` in `mainService` | `applyBet` is the service; `useGame.submitBet` gains an `options.keepServerMessage` flag so holder errors show the server text |
| F-C3 | countdown "same formatter as the terminal's `lotteryCountdown`" | Move the formatter from `GameTerminal.vue:457-463` into `src/utils/countdown.ts` and use it in both places |
| F-C4 | lobby card reads the round | `useLiveGames.snapshot()` gains `startDate`, `holderMinimum`, `houseFundedPot`, `entriesPaused`, `holderOnly` |
| F-C5 | lever "ENTER FREE instead of the price" | `leverTag` is Hyper-Roll-only today; add a holder branch returning `'ENTER FREE'` |
| F-C6 | Admin `GameTypeForm` pot read-only for holder types | **Not applicable**: the form is create-only and cannot create holder types; the reconciler re-zeroes the pot. No change. |
| F-C7 | `trackBetPlaced({ isFree: amount === 0 })` | Pass `isFree: false, isHolder: true` for holder entries if the tracker accepts extra props; otherwise skip tracking for holder entries (read `trackBetPlaced` first) |

## Review Focus

1. **Wallet connected but eligibility not loaded yet / RPC-unavailable (`balance: null`)** — the lever must still work and let the server decide (no silent block); the terminal shows `UNAVAILABLE`. Checked in Task 3/4 browser QA.
2. **Switching to another game while a mobile sign hop is pending** — the resume must drop the stale entry (round changed) instead of submitting to the wrong round. Task 3 `resumeHolderEntry` checks `gameId` against the loaded round.
3. **A holder entry must never call `sendToken`, the headroom check or `applyDebit`**, even when free-bet mode is toggled on in dev. Task 3 branch sits before source selection and returns.
4. **Paused round** — lever disabled, card shows PAUSED, no signature request. Task 4.
5. **Voided ticket belongs to my wallet only** — other wallets' voided rows must not trigger my "TICKET VOIDED" line. Task 4 matches `externalPlayerId === myWallet`.

---

### Task 1: Types, service, utils, live-game snapshot

**Files:**
- Modify: `monkify-front/src/types/index.ts` (L13-18 `LotteryParameters`, L283-294 `AppliedBet`, L327-361 `GameInstance`, L363-375 `BodyGameBet`, L410-414 `GameVerificationBet`)
- Modify: `monkify-front/src/services/mainService.ts` (L233-255 purpose union; add `getHolderEligibility` near `getTokenBalance` L347; default export L373-397)
- Modify: `monkify-front/src/utils/gameArchetype.ts`, `src/utils/pot.ts`
- Create: `monkify-front/src/utils/countdown.ts`
- Modify: `monkify-front/src/hooks/useLiveGames.ts` (`LiveGame` L20-31, `snapshot` L48-59)
- Modify: `monkify-front/src/components/game/GameTerminal.vue:457-463` (use the util)

**Interfaces — Produces:**
```ts
// types/index.ts
export interface HolderEligibility { wallet: string; symbol: string; minimum: number; balance: number | null; eligible: boolean; entriesPaused: boolean; }
// LotteryParameters: holderOnly?: boolean
// AppliedBet: voided?: boolean
// GameInstance: holderOnly?: boolean; holderMinimum?: number | null; houseFundedPot?: number | null; entriesPaused?: boolean
// BodyGameBet: walletProofNonce?: string; walletProofSignature?: string
// GameVerificationBet: voided?: boolean

// mainService
const getHolderEligibility = async (gameTypeId: string, wallet: string) => {
  const { data } = await mainAPI.get<DefaultResponse<HolderEligibility>>(`/games/${gameTypeId}/holder-eligibility`, { params: { wallet } });
  return data;
};
// getWalletNonce purpose union += 'jackpot-entry'

// gameArchetype.ts
export const isHolderLottery = (game?: ActiveGame | null): boolean => !!game?.lotteryParameters?.holderOnly;

// countdown.ts
export const formatCountdown = (msLeft: number): string => { ... }  // `${h}H ${m}M ${s}S` | `${m}M ${s}S` | `${s}S`; '' when msLeft <= 0 or not finite
export const formatHolderMinimum = (n: number): string => n.toLocaleString('en-US', { maximumFractionDigits: 0 });
export const compactHolderMinimum = (n: number): string => /* 1M, 500K, 2.5M */;

// LiveGame gains: startDate?: string | null; holderOnly?: boolean; holderMinimum?: number | null; houseFundedPot?: number | null; entriesPaused?: boolean
```

- [ ] **Step 1:** add the types and the service function (+ default export entry).
- [ ] **Step 2:** `pot.ts`: widen the `Pick` to include `'houseFundedPot' | 'holderOnly'`; first line after the null check: `if (instance.holderOnly) return toLamportGrid(instance.houseFundedPot ?? 0);` (a holder pot is the frozen house pot, not stakes + base).
- [ ] **Step 3:** `countdown.ts` — move the exact formatting logic from `GameTerminal.vue:457-463` (keep output identical: read it, copy it); `compactHolderMinimum`: `>= 1e6` → `${+(n/1e6).toFixed(1)}M` (trailing `.0` dropped by `+`), `>= 1e3` → `${+(n/1e3).toFixed(1)}K`, else `String(n)`. Replace the terminal's inline formatter with `formatCountdown(msLeft)`.
- [ ] **Step 4:** `useLiveGames.snapshot()`: copy the five fields from the instance.
- [ ] **Step 5:** `yarn lint && yarn build` → 0 errors.

---

### Task 2: `useHolderJackpot` + the mobile sign hop

**Files:**
- Create: `monkify-front/src/hooks/useHolderJackpot.ts`
- Modify: `monkify-front/src/hooks/useMobileWallet.ts` (stash key next to L38/L42; sign/get/clear functions next to `signMobileOrbit` L321-337; clear in `disconnectMobileWallet` L379-387)
- Modify: `monkify-front/src/types/mobileWallet.ts` (`MobilePendingJackpotEntry`)

**Interfaces:**
- Consumes: `mainService.getHolderEligibility`, `mainService.getWalletNonce(wallet, 'jackpot-entry')`, `useWallet()` (`publicKey`, `signMessage`), `useMobileWallet` (`getSession`, `isMobileDeeplinkContext`, `readSignedMessageSignature`), `isHolderLottery`.
- Produces:
```ts
export interface MobilePendingJackpotEntry { walletApp: string; wallet: string; gameId: string; gameTypeId: string; word: string; seed: string; nonce: number; proofNonce: string; createdAt: number; }

// useMobileWallet additions
signMobileJackpotEntry(session, pending: MobilePendingJackpotEntry, message: string): void
getPendingJackpotEntry(): MobilePendingJackpotEntry | null   // 5-minute stale guard like getPendingOrbit
clearPendingJackpotEntry(): void

// useHolderJackpot (module-level shared state, like the other hooks)
export const buildJackpotEntryMessage = (wallet: string, gameId: string, word: string, nonce: string): string
export const useHolderJackpot = () => ({
  eligibility: Ref<HolderEligibility | null>,
  eligibilityLoading: Ref<boolean>,
  refreshEligibility: (gameTypeId: string, wallet: string | null | undefined) => Promise<void>,
  startEligibilityPolling: (gameTypeId: string, getWallet: () => string | null | undefined) => void,  // every 30 s
  stopEligibilityPolling: () => void,
  // Returns the proof, or 'hopped' when a mobile deeplink hop was started (the entry resumes on return).
  signEntry: (args: { wallet: string; gameId: string; gameTypeId: string; word: string; seed: string; nonce: number }) =>
    Promise<{ proofNonce: string; signature: string } | 'hopped'>,
});
```
- [ ] **Step 1:** `buildJackpotEntryMessage` per Global Constraints, with a `KEEP-IN-SYNC` comment naming `JackpotEntryMessages.Build` in the backend.
- [ ] **Step 2:** eligibility: `refreshEligibility` clears to `null` when there is no wallet; on error sets `{ ...unknown, balance: null, eligible: false }` only if a previous value exists, else `null` (never throw); `startEligibilityPolling` replaces any previous interval (single interval).
- [ ] **Step 3:** `signEntry`, template `useSelfExclusion.exclude` L117-152 + `useDuel.cancelRoom` L690-711:
```ts
const nonceResponse = await mainService.getWalletNonce(wallet, 'jackpot-entry');
const proofNonce = nonceResponse?.data?.nonce;
if (!proofNonce) throw new Error('Could not start the entry.');
const message = buildJackpotEntryMessage(wallet, gameId, word, proofNonce);
const session = getSession();
if (session && isMobileDeeplinkContext()) {
  signMobileJackpotEntry(session, { walletApp: session.walletApp, wallet, gameId, gameTypeId, word, seed, nonce, proofNonce, createdAt: Date.now() }, message);
  return 'hopped';
}
if (!signMessage.value) throw new Error('This wallet cannot sign messages.');
const signed = await signMessage.value(new TextEncoder().encode(message));
return { proofNonce, signature: toBase64(signed) };
```
  Copy a private `toBase64` like the other hooks do (there is no shared export).
- [ ] **Step 4:** mobile additions mirror the Orbit versions exactly (key name `PENDING_JACKPOT_ENTRY_KEY`, same storage, same 5-minute stale guard), plus removal in `disconnectMobileWallet`.
- [ ] **Step 5:** `yarn lint && yarn build` → 0 errors.

---

### Task 3: `useGame` — the holder branch, error passthrough, mobile resume

**Files:**
- Modify: `monkify-front/src/hooks/useGame.ts` (`submitBet` L1134-1185, `runBet` L1322-1806 — insert after L1396, `handleMobileDeeplinkReturn` L1925-2028 — new arm before the bet arm at L1977; wire eligibility polling where the selected game changes)

**Interfaces:**
- Consumes: Task 1 (`isHolderLottery`, types), Task 2 (`useHolderJackpot`, `getPendingJackpotEntry`, `clearPendingJackpotEntry`, `readSignedMessageSignature`), `TOKEN_TICKER`, `formatHolderMinimum`.
- Produces: `useGame` exposes `holderBuyLinkVisible: Ref<boolean>` (true after a below-N refusal, reset on game change / successful entry) for the terminal/console to render GET $STROKE.

- [ ] **Step 1: `submitBet` passthrough.** Add a 5th optional param `options?: { keepServerMessage?: boolean }`. In the catch (L1178-1181) when `options?.keepServerMessage` is true: `betOutput.value = apiErrorMessage(error) ?? 'There was an error processing your bet';` and mark the error handled (the same `markHandled` idiom `useDuel` uses — read `services/handlers.ts:14-30` to see how a handled error suppresses the toast) so the message is not also toasted. Holder entries: skip `applyDebit` (amount 0 is already a no-op — leave it) and fix `trackBetPlaced` per F-C7.
- [ ] **Step 2: the holder branch** in `runBet`, immediately after the per-round checks (after L1396):
```ts
if (isHolderLottery(selectedGame.value)) {
  const instance = gameInstance.value;
  const wallet = effectivePublicKey.value;
  if (!instance || !wallet) { betOutput.value = 'Connect a wallet to enter the draw.'; return; }
  if (instance.entriesPaused) { betOutput.value = 'Jackpot paused — entries reopen with the next draw.'; return; }
  const { eligibility, refreshEligibility, signEntry } = useHolderJackpot();
  const minimum = instance.holderMinimum ?? eligibility.value?.minimum ?? null;
  const balance = eligibility.value?.wallet === wallet ? eligibility.value.balance : null;
  if (minimum != null && balance != null && balance < minimum) {
    betOutput.value = `Hold at least ${formatHolderMinimum(minimum)} ${TOKEN_TICKER} to enter today's draw.`;
    holderBuyLinkVisible.value = true;
    return;   // AC 2: no signature requested
  }
  isLoadingBet.value = true;
  try {
    const { seed, nonce } = await useClientSeed().nextBetSeed();
    const proof = await signEntry({ wallet, gameId: instance.gameId, gameTypeId: selectedGame.value!.gameTypeId, word: choice, seed, nonce });
    if (proof === 'hopped') return;
    await submitBet(instance.gameId, choice,
      { amount: 0, choice, paymentSignature: '', wallet, seed, nonce, walletProofNonce: proof.proofNonce, walletProofSignature: proof.signature },
      undefined, { keepServerMessage: true });
  } catch (error) {
    betOutput.value = (error as Error)?.message || 'There was an error processing your bet';
  } finally {
    isLoadingBet.value = false;
    if (selectedGame.value) void refreshEligibility(selectedGame.value.gameTypeId, wallet);   // a below-N server refusal updates the ✓/✗
  }
  return;
}
```
  Adapt names to the file (`gameTypeId` field name on `ActiveGame`, `useClientSeed` import, how `isLoadingBet` is reset elsewhere, `choice` variable from L1348-1353). If `submitBet`'s server message is the below-N text, set `holderBuyLinkVisible = true` (match with `startsWith('Hold at least')`).
- [ ] **Step 3: mobile resume.** In `handleMobileDeeplinkReturn`'s `'sign'` arm, before the bet arm (L1977), add a holder arm: `const pending = getPendingJackpotEntry(); if (pending) { clearPendingJackpotEntry(); const signature = readSignedMessageSignature(params); ... }` — wait for the round (`waitForRoundLoaded`, like L1996-2000), drop it with `betOutput = 'The round changed — try again.'` when `gameInstance.value?.gameId !== pending.gameId`, else `submitBet(pending.gameId, pending.word, { amount: 0, choice: pending.word, paymentSignature: '', wallet: pending.wallet, seed: pending.seed, nonce: pending.nonce, walletProofNonce: pending.proofNonce, walletProofSignature: signature }, undefined, { keepServerMessage: true })`; `return` so the bet arm does not also run. In the `'error'` arm also `clearPendingJackpotEntry()`.
- [ ] **Step 4: eligibility lifecycle.** Where `useGame` reacts to the selected game / wallet changing (Grep `watch(` on `selectedGame` / `effectivePublicKey`), call `startEligibilityPolling(gameTypeId, () => effectivePublicKey.value)` when `isHolderLottery(selectedGame)`, else `stopEligibilityPolling()`; reset `holderBuyLinkVisible` on game change.
- [ ] **Step 5:** `yarn lint && yarn build` → 0 errors.

---

### Task 4: Screens — console, terminal, lobby card

**Files:**
- Modify: `monkify-front/src/components/game/GameConsole.vue` (`allowBets` L216-221, `leverTag` L423-433)
- Modify: `monkify-front/src/components/game/GameTerminal.vue` (idle view L1176-1209, status map L558-580, `betOutput` line L1297, running/ended POT L1039-1044)
- Modify: `monkify-front/src/components/game-selection/GameTypeCard.vue` (`ticketText` L181, `liveChip` L193-208, template L325-346, CSS L976-1019)

**Interfaces:** Consumes Tasks 1-3 (`isHolderLottery`, `formatCountdown`, `formatHolderMinimum`, `compactHolderMinimum`, `useHolderJackpot().eligibility`, `holderBuyLinkVisible`, `TOKEN_BUY_URL`, `tokenLive`, `TOKEN_TICKER`).

- [ ] **Step 1: console.** `allowBets` adds `&& !(isHolderLottery(selectedGame.value) && gameInstance.value?.entriesPaused)`. `leverTag`: `if (isHolderLottery(selectedGame.value)) return 'ENTER FREE';` before the Hyper Roll branch.
- [ ] **Step 2: terminal idle view** for a holder lottery (compute `holder = isHolderLottery(selectedGame)`, `minimum = gameInstance.holderMinimum`, `myBalance` from eligibility when its wallet matches the connected one):
  - hide `> BET:` (L1177-1182) when `holder`;
  - `> ENTRY: FREE FOR HOLDERS (≥ ${formatHolderMinimum(minimum)} ${TOKEN_TICKER})`;
  - `> YOUR ${TOKEN_TICKER}: ${formatHolderMinimum(balance)} ✓|✗` — `CONNECT WALLET` when no wallet, `UNAVAILABLE` when `balance === null` with eligibility loaded, `…` while loading;
  - `> POT:` shows `houseFundedPot` via the existing pot formatter (Task 1 made `computePot` return it), or `PAUSED` when `entriesPaused`;
  - `> DRAW IN ${lotteryCountdown}` (existing countdown; reuse the FREQUENCY/countdown block, don't duplicate);
  - below-N (`holderBuyLinkVisible` or `balance < minimum`) and `tokenLive`: `> GET ${TOKEN_TICKER} →` as an `<a :href="TOKEN_BUY_URL" target="_blank" rel="noopener">` styled like existing terminal links;
  - my ticket (`bets.find(b => b.externalPlayerId === myWallet)`): `> YOUR TICKET: ${choice.toUpperCase()}`; if `voided`: `> TICKET VOIDED — BALANCE WAS BELOW ${formatHolderMinimum(minimum)} ${TOKEN_TICKER} AT THE DRAW.` instead;
  - paused: `> JACKPOT PAUSED — ENTRIES REOPEN WITH THE NEXT DRAW.`
  - Status map lottery arm (L558-580): for holder rounds replace `PLACE YOUR BETS!` with `ENTER FREE!` in both branches, `CONNECT WALLET TO JOIN THE DRAW.` stays, `YOUR BET IS IN!` (L572) → `YOUR TICKET IS IN!`.
  - Bet list rows: render a voided row dimmed with a `VOID` tag (reuse an existing muted class).
- [ ] **Step 3: lobby card** for `isHolderLottery(props.game)`:
  - price chip label `Entry`, value `FREE FOR HOLDERS`, and a second line / suffix `≥ ${compactHolderMinimum(live.holderMinimum)} ${TOKEN_TICKER}` (no coin icon; `showCoin` false); when `live` has no minimum yet, just `FREE FOR HOLDERS`;
  - pot chip = `money(live.pot)` (already `houseFundedPot` via `computePot`), label `Pot`; when `live.entriesPaused` → text `PAUSED` with `chip-value--dim`;
  - new chip `Draw` with `formatCountdown(new Date(live.startDate).getTime() - now)` ticking every 1 s (one shared `setInterval` per card, cleared `onBeforeUnmount`); hidden when empty;
  - keep the card's one-row layout at desktop and wrap at 360px (check `.chips` flex-wrap).
- [ ] **Step 4:** `yarn lint && yarn build` → 0 errors.
- [ ] **Step 5: browser QA** (local stack from the backend plan's Final verification, `yarn serve --port <free port>`; verify the port is free and grep the served bundle for `ENTER FREE` first — memory: port collisions across worktrees): lobby card shows FREE FOR HOLDERS · ≥ 1M $STROKE, pot, countdown; terminal lines; with `DevAssumedBalance=5000000` the lever signs (no SOL transfer popup) and `YOUR TICKET` shows; with `DevAssumedBalance=1` the below-N line + GET $STROKE show and **no** sign prompt; at 360px (CDP `emulate`) the card and terminal lines fit. 0 console errors.

---

### Task 5: App copy

**Files (all under `monkify-front/src/components/`):** `home/HomeTokenCard.vue` (L50-59, L217-229), `guide/sections/GuideToken.vue` (L46, L51, L54-56, L189-196), `guide/GuideModeCards.vue` (L102-103, L127, L156-164), `guide/sections/GuideHowTheGameWorks.vue` (L7, L44, L61, L75-84), `guide/sections/GuideRefunds.vue` (L28-29, L48-50), `guide/sections/GuideWhatYouWin.vue` (L35, L59-60), `guide/sections/GuideTheModes.vue` (L27-30)

- [ ] **Step 1:** apply exactly:
  - `HomeTokenCard.vue` heading `Holders play Space Jackpot free`; body `Token holders get a free daily lottery draw. Hold the day's minimum {{ TOKEN_TICKER }} at the draw.` Rework the L50-59 and L217-222 comments: the "bonus on top of the SOL pot" anchor is gone; the copy is the verbatim twin of LP `TokenSection.vue`; the token never stakes a bet, it grants a free entry (card #196).
  - `GuideToken.vue` row: title `'FREE JACKPOT FOR HOLDERS'`, body `` `Token holders get a free daily lottery draw. Hold at least the day's minimum ${TOKEN_TICKER} when you enter and when Edson draws at 10PM UTC — move it before then and your ticket is voided.` ``; rework the comments at L46/L51/L54-56/L189-194 (the jackpot no longer "pays it out"; the row is no longer a numberless bonus).
  - `GuideModeCards.vue`: lottery blurb for a holder type `` `Hold ${TOKEN_TICKER}, pick your ${game.choiceRequiredLength} characters, and your ticket is free. The house funds the pot; the draw runs on a clock.` `` (keep the old blurb for a non-holder lottery); `priceLabel` → `'ENTRY'` and `price` → `'FREE FOR HOLDERS'` when `isHolderLottery(game)`.
  - `GuideHowTheGameWorks.vue`: L44 → `'Cosmic Sprint puts every stake in the round into one pot and starts as soon as enough players have joined. Space Jackpot is free for token holders: sign to enter, the house funds the pot, and Edson draws at 10PM UTC.'`; L61 → scope the house-cut sentence to Cosmic Sprint: `'Winnings land on-chain in the wallet that bet, the moment the round is done. In Cosmic Sprint a 2% house cut comes off what you win above your own stake, and players holding the same winning word share the pot.'`; L75-84 "Everyone buys in" → `'Everyone in Cosmic Sprint buys in, Edson types, and the first word out takes the pot; Space Jackpot works the same way, except holders enter free and the house puts up the pot.'` Update the L7 comment.
  - `GuideRefunds.vue` row → title `'SPACE JACKPOT HAS NOTHING TO REFUND'`, body `'Entry is free for token holders, so a draw that does not fill is simply called off — no one paid, nothing goes home.'`; intro L48-50 keep (it is about stakes in general) unless it names Space Jackpot.
  - `GuideWhatYouWin.vue` L35 → `'Every stake in the round, piled up. Space Jackpot is different: the house puts up the pot, and it is fixed when the round opens.'`; L59-60 scope "whose word never showed up paid for that" to races (`'In a race, everyone whose word never showed up paid for that…'`, keep the rest of the sentence).
  - `GuideTheModes.vue` L27-30 → `'There are three kinds of table. In a race you and everybody else buy into one pot and the first word out takes it — except Space Jackpot, which is free for token holders and draws once a day. In Hyper Roll you play the house on your own board. In a duel it is your word against one opponent's, the house or another player. These are the tables running right now…'` (keep the original ending after "right now").
- [ ] **Step 2:** `git grep -n -i "jackpot" src | grep -i -E "bonus|earns you|rewarded with|refund|paid bet|buys? in"` → no remaining player-facing promise of paid entry, refunds or the bonus (tokenomics "jackpot seed" lines are out of scope — leave them).
- [ ] **Step 3:** `yarn lint && yarn build` → 0 errors; open `/how-to-play` (needs the backend up) and the home token card in the browser, read the new copy once.

---

### Task 6: LP copy and terms

**Files:** `monkify-lp/src/components/TokenSection.vue` (L56-58, L280-298), `src/components/GameModes.vue` (L69-80), `src/pages/terms.astro` (L33-35, L45-46, L68-73, L86-87, L141-153; date line)

- [ ] **Step 1:** `TokenSection.vue` heading/body = the `HomeTokenCard` pair verbatim (`Holders play Space Jackpot free` / `Token holders get a free daily lottery draw. Hold the day's minimum {{ TOKEN_TICKER }} at the draw.`); rework comments L56-58, L288-292 the same way as the app.
- [ ] **Step 2:** `GameModes.vue` lottery: `tag: "FREE FOR HOLDERS · DAILY"`, `blurb: "Hold $STROKE, grab a free ticket, one holder bags the daily jackpot."`
- [ ] **Step 3:** `terms.astro`: L68-73 house-cut clause names **Cosmic Sprint** only; add the Space Jackpot clause (verbatim from spec §8):
  > Space Jackpot is free to enter for wallets holding at least the round's published minimum of $STROKE. Eligibility is checked when you enter and again at the draw; a ticket whose wallet is below the minimum at the draw is void. The pot is funded by the house, fixed when the round opens, and carries no house cut. A draw with fewer than two valid tickets is cancelled and nothing carries over. We may pause entries for a round when the house cannot fund the pot.
  Scope L33-35 / L45-46 ("You bet on a sequence…", "Stakes are paid in SOL…") so they don't contradict free entry (add "except Space Jackpot, which is free for eligible holders" where they state every game is paid); L86-87 "divided between the paid winners" → "divided between the winners"; refund section L141-153: add that Space Jackpot has no stake to refund. Update the terms' "last updated" date to 2026-09-28 (the Hyper Roll clause commit shows the pattern — `git log -p -1 --grep "Date the terms"`).
- [ ] **Step 4:** `SpaceRoadmap.vue` — no change (checked: nothing implies paid entry).
- [ ] **Step 5:** `npm run build` in the LP worktree → success; `npm run dev -- --port <free>` and read the token section, game modes and terms once.

---

## Final verification

- [ ] Front: `yarn lint` 0 errors, `yarn build` success. LP: `npm run build` success.
- [ ] Browser QA list of Task 4 Step 5 + the copy reads of Tasks 5-6, 0 console errors on `/game-select`, `/game?type=<space jackpot>`, `/how-to-play`, LP `/`, `/terms`.

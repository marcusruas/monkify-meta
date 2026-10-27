# Partner code on profile — Frontend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On the owner's own profile hero, a dashed **+ PARTNER CODE** button opens a dialog that signs and POSTs a partner code once; once linked, a **PARTNER · CODE** badge replaces it.

**Architecture:** A composable `usePartnerCodeLink` (nonce → sign → POST, modelled on `usePartnerRegistration.ts`), a small `PartnerCodeModal.vue` on `ArcadeModal`, and an owner branch in the `PlayerProfilePage.vue` hero. Reads `partnerCode`/`partnerLinkedAt` from the existing `GET /players/profile`.

**Tech Stack:** Vue 3 + TypeScript, `solana-wallets-vue`, Tailwind + `arcade.css`, `vue3-toastify`. No test runner: verification = `yarn lint` + `yarn build` + browser QA.

**Spec:** `monkify-meta/specs/partner-code-on-profile.md` §7. Worktree: `E:\Monkify\.worktrees\partner-code-on-profile\monkify-front`. Design: Option B in <https://claude.ai/design/p/9d33dcc9-6062-4643-864d-c54532c9844f?file=Partner+Code+Options.dc.html>.

## Global Constraints

- Work ONLY in `E:\Monkify\.worktrees\partner-code-on-profile\monkify-front`.
- ⚠️ To run `yarn lint`/`yarn build` in the worktree: copy the hub `E:\Monkify\monkify-front\yarn.lock` into the worktree and junction the hub `node_modules` in (`mklink /J node_modules E:\Monkify\monkify-front\node_modules`). **Remove the junction (`rmdir node_modules`, never `rm -rf`) before finishing** — `git worktree remove` through a junction wipes the hub. Never commit `yarn.lock` changes unless they were already tracked.
- prettier `endOfLine: lf` — new files LF; `prettier --check` false-alarms on CRLF files, ignore those.
- **Copy says "partner", NEVER "streamer"** (owner). Hard-coded English (no i18n).
- Signed message must be byte-identical to the backend (joined with `\n`): `Space Stroker partner code` / `Wallet: ${wallet}` / `Code: ${code}` / `I understand this partner code is permanent.` / `Nonce: ${nonce}` — `code` normalized with `normalizeReferral`.
- Signature is **Base64** (`toBase64` from `src/hooks/usePartnerEarnings.ts`).
- Show backend error sentences verbatim (`apiErrorMessage`) — never string-match them.

---

### Task 1: Types + service

**Files:**
- Modify: `src/types/index.ts` — `PlayerProfile` (L403-417) add `partnerCode: string | null;` and `partnerLinkedAt: string | null;`; add
```ts
export interface PartnerCodeLinkBody { wallet: string; code: string; nonce: string; signature: string }
export interface PartnerCodeLinkResult { partnerCode: string; partnerLinkedAt: string }
```
- Modify: `src/services/mainService.ts` — add `'partner-code'` to the `getWalletNonce` purpose union (L250-263); add (mirroring `updatePlayerPreferences` L219-225 for the axios/`DefaultResponse` shape):
```ts
const linkPartnerCode = (body: PartnerCodeLinkBody) =>
  api.post<DefaultResponse<PartnerCodeLinkResult>>('/players/partner-code', body);
```
(use whatever http client/wrapper the neighbouring functions use) and export it with the others (L423-432).
- [ ] Implement → `yarn lint` clean on these files → commit `"Partner-code types and service (#213)"`.

### Task 2: `usePartnerCodeLink` composable

**Files:** Create `src/hooks/usePartnerCodeLink.ts`.

**Produces:**
```ts
export const buildPartnerCodeMessage = (wallet: string, code: string, nonce: string) =>
  ['Space Stroker partner code', `Wallet: ${wallet}`, `Code: ${code}`,
   'I understand this partner code is permanent.', `Nonce: ${nonce}`].join('\n');

export function usePartnerCodeLink() {
  // returns { submitting: Ref<boolean>, error: Ref<string|null>, link(wallet: string, rawCode: string): Promise<PartnerCodeLinkResult|null> }
}
```
`link` flow (copy structure from `src/hooks/usePartnerRegistration.ts` L95-120):
1. `error.value = null`; `code = normalizeReferral(rawCode)` (`src/utils/referral.ts`); null → `error.value = 'Enter a valid code.'`, return null.
2. `submitting = true`; `nonce = (await mainService.getWalletNonce(wallet, 'partner-code'))?.data?.nonce`; missing → generic error.
3. `const { signMessage } = useWallet()`; `if (!signMessage.value) throw new Error('This wallet cannot sign messages.')`; `signed = await signMessage.value(new TextEncoder().encode(buildPartnerCodeMessage(wallet, code, nonce)))`.
4. `res = await mainService.linkPartnerCode({ wallet, code, nonce, signature: toBase64(signed) })`.
5. Success: `clearReferral()`; `forgetPlayerProfile(wallet)` (`src/hooks/usePlayerProfile.ts`); `toast.success('Partner code linked.')`; return `res.data.data` (match the `DefaultResponse` unwrap the codebase uses).
6. catch: `isUserRejection(e)` (as in `usePartnerRegistration.ts` L32-36 — import or copy) → `error = null`, return null; else `(e as { handled?: boolean }).handled = true;` and `error.value = displayableErrorMessage(e, 'That request could not be processed.')` (`src/utils/apiError.ts` — passes through our own thrown Error message and the backend's validation sentence).
7. `finally submitting = false`.
- [ ] Implement → `yarn lint` → commit `"usePartnerCodeLink composable (#213)"`.

### Task 3: `PartnerCodeModal.vue`

**Files:** Create `src/components/player/PartnerCodeModal.vue`.

Props `open: boolean`, `wallet: string`; emits `close`, `linked(result: PartnerCodeLinkResult)`.
```vue
<arcade-modal :open="open" title="PARTNER CODE" size="compact" :dismissible="!submitting" @close="emit('close')">
  <div v-if="error" class="dialog__strip dialog__strip--err">
    <span class="dialog__strip-tag">ERR</span><span class="dialog__strip-text">{{ error }}</span>
  </div>
  <!-- body -->
  <p class="dialog-copy">Enter the code your partner gave you.</p>
  <label class="dialog-label" for="partner-code-input">CODE</label>
  <input id="partner-code-input" v-model="code" class="input-arcade" maxlength="32"
         autocapitalize="off" autocomplete="off" spellcheck="false" @keydown.enter="submit" />
  <div class="partner-code__warn">
    <span class="partner-code__warn-title">ONE TIME ONLY</span>
    <p>Once linked, this code can never be changed or removed. It only applies to bets you place after linking.</p>
  </div>
  <template #footer>
    <button type="button" class="btn-dialog" :disabled="submitting" @click="emit('close')">CANCEL</button>
    <button type="button" class="btn-dialog btn-dialog--primary" :disabled="submitting || !code.trim()" @click="submit">
      {{ submitting ? 'SIGNING…' : 'LINK · SIGN' }}
    </button>
  </template>
</arcade-modal>
```
- On `open` becoming true: `code.value = readReferral() ?? ''` (pre-fill, decision 8) and clear `error`.
- `submit`: `const r = await link(props.wallet, code.value); if (r) emit('linked', r)`.
- Scoped style `.partner-code__warn`: `background: var(--color-void-deep)`, `border: 2px solid var(--color-primary)`, radius `var(--radius-arcade)`, padding 12px, gap 6px; title Press Start 2P 8px `var(--color-primary)`; body VT323 ~19px white. Copy the slot/markup conventions from `src/components/player/PlayerIconPicker.vue` (footer L197-207) and `PlayerCustomizationModal.vue` (label/input L193-201) — follow their exact class usage if it differs from the sketch above.
- [ ] Implement → lint → commit `"PartnerCodeModal (#213)"`.

### Task 4: Hero button + badge in `PlayerProfilePage.vue`

**Files:** Modify `src/pages/PlayerProfilePage.vue`.

- Script: import `orbitIsStreamer` from `src/hooks/useOrbit.ts`, `PartnerCodeModal`; add
```ts
const partnerOpen = ref(false);
const showPartnerArea = computed(() => isOwner.value && !isEmbedded);
const canLinkPartner = computed(
  () => showPartnerArea.value && !profile.value?.partnerCode && !orbitIsStreamer(effectivePublicKey.value ?? undefined),
);
const partnerSince = computed(() => /* format profile.partnerLinkedAt exactly like `joined` is formatted */);
const onPartnerLinked = () => { partnerOpen.value = false; void reload(); };
```
- Template: in the hero top row, beside the existing `<div v-if="canDonate || canMessage" …>` (L386-399), add:
```vue
<div v-if="showPartnerArea && (canLinkPartner || profile.partnerCode)"
     class="flex w-full flex-col items-start gap-2 md:w-auto md:items-end">
  <template v-if="profile.partnerCode">
    <div class="profile__partner-badge">
      <span class="profile__partner-badge-k"><span class="profile__lock" aria-hidden="true"></span>PARTNER</span>
      <span class="profile__partner-badge-v">{{ profile.partnerCode.toUpperCase() }}</span>
    </div>
    <span v-if="partnerSince" class="vt323-regular text-[18px] text-ink-dim">since {{ partnerSince }}</span>
  </template>
  <template v-else>
    <button type="button" class="profile__partner-add" @click="partnerOpen = true">+ PARTNER CODE</button>
    <span class="vt323-regular text-[18px] text-ink-dim">Have a partner code? One time only.</span>
  </template>
</div>
<partner-code-modal v-if="isOwner && wallet" :open="partnerOpen" :wallet="wallet"
                    @close="partnerOpen = false" @linked="onPartnerLinked" />
```
(`.toUpperCase()` is display-only, matching the mockup.)
- Styles (scoped, beside `.profile__ghost` L706-722):
```css
.profile__partner-add { height: 44px; display: inline-flex; align-items: center; padding: 0 14px;
  background: rgba(0,0,0,.3); border: 2px dashed var(--t); border-radius: var(--radius-arcade);
  color: var(--t); font-family: 'Press Start 2P', system-ui, sans-serif; font-size: 9px; cursor: pointer; }
.profile__partner-add:hover { background: rgba(0,0,0,.45); }
.profile__partner-add:focus-visible { outline: 2px solid var(--color-primary); outline-offset: 3px; }
.profile__partner-badge { display: flex; align-items: stretch; border: 2px solid #000; border-radius: var(--radius-arcade);
  box-shadow: 4px 4px 0 #000; overflow: hidden; }
.profile__partner-badge-k { display: flex; align-items: center; gap: 8px; padding: 0 10px; background: var(--t);
  color: var(--t-ink); font-family: 'Press Start 2P', system-ui, sans-serif; font-size: 8px; }
.profile__partner-badge-v { display: flex; align-items: center; padding: 4px 12px; background: var(--color-void-deep);
  font-family: 'VT323', ui-monospace, monospace; font-size: 26px; color: #fff; }
.profile__lock { position: relative; width: 9px; height: 7px; margin-top: 5px; background: currentColor; }
.profile__lock::before { content: ''; position: absolute; left: 1px; top: -6px; width: 3px; height: 4px;
  border: 2px solid currentColor; border-bottom: 0; border-radius: 3px 3px 0 0; }
```
- [ ] Implement → `yarn lint` + `yarn build` (junction + yarn.lock per Global Constraints) → commit `"Partner code button, badge and dialog on the profile hero (#213)"`.

### Task 5: Verification

- [ ] `grep -ri streamer src/components/player/PartnerCodeModal.vue src/hooks/usePartnerCodeLink.ts` → no matches; the only new `streamer` token in `PlayerProfilePage.vue` is the `orbitIsStreamer` call.
- [ ] `yarn lint` and `yarn build` clean. Remove the `node_modules` junction (`rmdir`).
- [ ] `git push origin feature/partner-code-on-profile`.

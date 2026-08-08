# Space Stroker — "Real API error messages" — Implementation Spec

> **Status: SHIPPED 2026-08-08** — merged to `main` via `feature/api-error-messages` in all three
> repos. Distilled into [`../features/api-error-messages.md`](../features/api-error-messages.md); this
> spec stays the deep design archive. See §16 for what changed between this document and what shipped.
>
> Refined via Trello card
> [response and handling/UI when betting on a reserverd word (#158)](https://trello.com/c/cmb6wfm7)
> (2026-08-07).
> Repos affected: `monkify-backend`, `monkify-front`, and this doc in `monkify-meta`.
> Branch/worktree: `feature/api-error-messages` → `.worktrees/api-error-messages/<repo>`.
>
> **Purpose:** a player whose bet is rejected is currently told **"Request failed with status code
> 400"**. The backend wrote them a real sentence — *"This sequence has already been taken by another
> player. Pick a different one. Your bet should be refunded soon."* — and the frontend throws it away.
> This card makes the shared axios interceptor surface the backend's own message, on **every**
> `mainAPI` endpoint at once, with one friendly fallback when there is no message to show. The
> reserved-word case is the loudest symptom; the fix is the missing behaviour behind it.

---

## 1. Summary

Three moving parts, in dependency order.

| # | Part | Repo | What changes |
| --- | --- | --- | --- |
| 1 | **`apiErrorMessage()` helper** | front | New single extraction of `response.data.messages[].value`. Replaces **5** hand-rolled copies. |
| 2 | **The interceptor** | front | `services/handlers.ts` toasts the extracted message instead of `error.message`. Adds dedupe and a friendly fallback. **Every** `mainAPI` endpoint is fixed at once. |
| 3 | **Copy pass** | backend | Reword the **live, player-facing** payment-signature strings that are now newly visible. Values only — no logic, no migration. |

Plus a **double-toast sweep**: making the interceptor's toast meaningful means every call site that
*already* reports the failure itself (its own toast, or an inline error line) would now report the
**same sentence twice**. Those sites must mark `err.handled = true`, following the precedent
documented at `useOrbit.ts:212`.

**Before / after**, on a bet rejected because the sequence is taken:

| | Terminal | Toast |
| --- | --- | --- |
| **Today** | `> There was an error processing your bet` | `Request failed with status code 400` |
| **After** | `> There was an error processing your bet` *(unchanged)* | `This sequence has already been taken by another player. Pick a different one. Your bet should be refunded soon.` |

The terminal line is deliberately untouched — it stays the persistent trace while the toast carries
the reason.

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo:** `feature/api-error-messages` in `monkify-backend`, `monkify-front`,
  `monkify-meta`; each hub stays on `main`. Branches already created and pushed.
- **Unit tests:** run via **`run_tests.bat`**, never bare `dotnet test` (the script excludes the
  `ProductionGames` Monte Carlo suite by design — a bare run "fails" ~156 and means nothing). Report
  which tests changed.
- **No new backend integration test required.** `AGENTS.md` rule 8 scopes to **new features**; this is
  a UX/bug fix plus a copy pass. `ChoiceExclusivityFlowTests` and `OperatorlessFlowTests` must stay
  green.
- **No EF/migration surface.** No entity, enum value, column or mapping changes.
- **Secrets/config:** untouched.
- **`constraints.md` §6 — vague messaging is preserved.** `GameNotValidForBets` (unknown **or**
  inactive game type must stay indistinguishable), every auth failure, and every wallet-proof failure
  stay exactly as vague as they are today. The copy pass must not make any of them more specific
  about *why*.
- **Orbit invisibility (#150).** The cross-round reserved-word reject stays **byte-identical** to a
  same-round collision — same message, same refund path, same timing. A distinct "reserved" wording is
  **forbidden**: it would announce that somebody on this game type runs Orbit.
- **`yarn lint` CRLF trap.** The front needs uniform CRLF endings. Never `sed -i` these files — prettier
  fails with `Insert ␍`. Fix with `unix2dos`; verify with `od -c`.

---

## 3. Findings from code exploration — read before implementing

The refined card was written against assumptions that the code does not hold. **Five corrections**,
all verified against `main` at spec time. Where a finding contradicts the card, **this spec wins**.

### F1 — The backend copy pass in the card is already done (card retargeted)

The card asks to reword `DuplicateBetError` → *"The requested bet is already set."* and
`DuplicateBetRefundedError` → *"The requested bet was already made."*, and lists five
`BetValidationResult` `[Description]` strings. **Every one of those is byte-identical to the current
value.** As written, the backend section is a no-op.

**Resolution (owner, 2026-08-07): do a real copy pass**, retargeted at the strings that *are* awkward
and *are* live on the bet path — the **payment-signature family** (§6.2). `DuplicateBetError`,
`DuplicateBetRefundedError` and all five `BetValidationResult` descriptions are **already correct and
are not touched**.

### F2 — The tests do not assert literal strings (one exception)

The card says *"Tests asserting exact strings must be updated, not weakened."* In fact
`RegisterBetHandlerTests.cs`, `ChoiceExclusivityFlowTests.cs` and `OperatorlessFlowTests.cs` assert
**symbolically** — `ErrorMessages.ChoiceAlreadyTaken`, `BetValidationResult.X.StringValueOf()` — so a
value change flows through automatically.

**Exactly one literal assertion exists:** `ChoiceExclusivityFlowTests.cs:103` →
`ShouldContain("invalid characters")`. It refers to `BetValidationResult.InvalidCharacters`, which
this card does **not** reword, so it survives untouched. **Verify it, do not pre-emptively change it.**

### F3 — `type` is `2`, not `1` — the two admin extractions are silently broken

`MessageType` is `Informational = 0, Alert = 1, ValidationFailure = 2, Error = 3`
(`SpaceStroker.Common/Messaging/Models/MessageType.cs`), and there is **no `JsonStringEnumConverter`**
configured on the API, so it serializes as a **number**. `ValidationFailure` is therefore **`2`**.

`BetRefundManager.vue:13` and `GameTypeManager.vue:48` both do
`messages?.find((m) => m.type === 1 /* ValidationFailure */)` — they match **`Alert`**, never find the
real message, and always fall through to their generic fallback text. The card's Problem section
repeats the same `type: 1` mistake.

**Resolution (owner, 2026-08-07): fix it.** The shared helper prefers `type === 2`. Consequence, taken
knowingly: the two admin screens start rendering real backend messages instead of their generic
fallbacks. That **amends acceptance criterion 11** — see §12.

### F4 — The pre-pay guard already blocks the card's headline scenario

`useGame.ts:605-616` refuses a same-round taken choice **before any payment**, writing
`That sequence is already taken — pick another.` to the terminal and returning — no request, no toast.
So AC 1 (paid bet on a word another player already bet **this round**) is only reachable when the live
bet list is **stale** (SignalR lag, a race, a tab that missed an update).

**The reliable repro is the cross-round Orbit claim** (AC 2): the guard inspects only the live bet
list, so an `OrbitChoiceClaims` reservation is invisible to it and the request always reaches the
backend. Manual QA (§13) is written around that path.

### F5 — `token-expired` / the relaunch flow has no live emitter

AC 8 references "the existing token-expired postMessage + relaunching flow". In `monkify-front/src`,
`'token-expired'` appears **only** as a member of the `EmbedEventType` union (`utils/embed.ts:5`), and
`useEmbed().setRelaunching` has **zero callers**. Nothing posts it; nothing sets `relaunching`.
`mainAPI` has **no 401 branch at all** — a 401 today is toasted like any other error.

The criterion therefore reduces to a **no-regression** check: this card must not add a 401 branch and
must not make an embedded 401 louder than it is today. See §12 AC 8 as amended.

### F6 — Two dead-string observations (deliberately NOT changed here)

Recorded so a future card can pick them up; **out of scope for this one**:

- The `Wallet*` and `Rg*` resource families (`WalletPlayerBlocked`, `RgSelfExcluded`, `RgLimitReached`,
  `RgKycRequired`, `RgBlocked`, `RgBetNotAllowed`, `RgCoolingOff`, `RgCheckTemporarilyUnavailable`,
  `WalletInsufficientFunds`, `WalletDuplicateBet`) have **0 references** in `src/` — leftovers from the
  operator wallet integration removed in `features/remove-operators.md`. Several still say *"Please
  contact your operator"*, which no longer exists. They are unreachable, so they mislead nobody today.
  Leave them.
- `SignatureForInvalidToken` also has **0 references**. Left as-is for the same reason.
- `ParameterNotFound` ("was not found") and `ParameterHasNoActiveGames` ("has no active games")
  **distinguish** two states of a game type, which sits in tension with `constraints.md` §6. They are
  on the game-lookup path, not the bet-reject path, and predate this card. **Not changed here** —
  flag to the owner separately rather than folding a §6 decision into a copy pass.

---

## 4. Resolved product decisions

| # | Decision | Source |
| --- | --- | --- |
| 1 | Scope is **all** `mainAPI` rejections, not just the reserved word. The reserved word is one symptom of one missing behaviour. | Card |
| 2 | The fix lives in the **interceptor** (`handlers.ts`), so every endpoint is fixed at once and no future endpoint starts mute. No per-call-site wiring. | Card |
| 3 | A shared **`apiErrorMessage(err)`** helper is extracted; the 5 duplicated extractions refactor onto it. | Card |
| 4 | Backend copy is shown **verbatim**. No front-side rewrite map, no string matching on `.resx` wording. | Card |
| 5 | The reserved-word message stays **byte-identical** to the same-round collision. A distinct "reserved" wording is forbidden (#150). | Card |
| 6 | **Toast only.** The terminal keeps `> There was an error processing your bet` unchanged; `GameTerminal.vue` is untouched. | Card |
| 7 | **One friendly fallback** when no message is usable: `Something went wrong — please try again.` The raw error goes to `console.error` only. Status codes never reach the player. | Card |
| 8 | Identical toasts **dedupe** — one is shown/refreshed rather than stacked. Lever buzz + error SFX unchanged. | Card |
| 9 | **`handled === true` still wins.** The interceptor only changes the *text* of toasts it already shows today. | Card |
| 10 | **`adminAPI` is out of it** — separate instance, separate interceptor, no toast, inline rendering. It only gains the shared helper. | Card |
| 11 | **No pre-pay guard for cross-round claims**, and no endpoint exposing reserved words — both leak Orbit. Accepted cost: retrying the same word pays and refunds again. | Card |
| 12 | The backend copy pass is **retargeted** at the live payment-signature strings (F1). `DuplicateBetError`, `DuplicateBetRefundedError` and the `BetValidationResult` descriptions are already correct and stay untouched. | Owner, 2026-08-07 |
| 13 | The helper prefers **`type === 2` (ValidationFailure)**, fixing the wrong `=== 1` constant (F3), and falls back to the first entry carrying a non-empty `value`. Admin screens will start showing real messages — AC 11 is amended accordingly. | Owner, 2026-08-07 |
| 14 | **Double-toast sweep:** every call site that already reports a failure itself (own toast or inline line) marks `err.handled = true`, so the now-meaningful interceptor toast does not duplicate it. | This spec, §7.4 |

---

## 5. Data model & configuration

**Not applicable.** No entity, enum, column, EF mapping, migration, `GeneralSettings` key or
`VUE_APP_*` env var changes. `SupportEmail` (`_settings.Games.SupportEmail`) is read by
`SolanaService.cs:674` and is **not** modified — only the sentence it is interpolated into.

---

## 6. Backend behaviour (`monkify-backend`)

**Copy only.** No handler, consumer, controller, validator or event changes. The error envelope and
every status code stay exactly as they are.

### 6.1 The envelope (reference — unchanged, documented because the front now depends on it)

A rejected request is turned into a response body by `Api/Filters/ExceptionFilter.cs:47`:

```jsonc
{
  "data": "Your request could not be processed. Please check the data and try again.",
  "messages": [ { "type": 2, "value": "This sequence has already been taken by another player. Pick a different one. Your bet should be refunded soon." } ]
}
```

- `type` is the **numeric** `MessageType` — `ValidationFailure = 2` (F3).
- `RegisterBetHandler.RefundInvalidBet` concatenates the refund sentence itself:
  `string.Concat(errorMessage, " ", ErrorMessages.RefundWarning)` — **only on paid bets**. A free bet
  takes the early-return branch and gets **no** refund warning. This is why showing backend text
  verbatim satisfies AC 9 for free without any front-side conditional.
- `ModelValidationFilter` adds `AddValidationFailureMessage` per model error, so body-validation
  failures land in the same shape.

### 6.2 The copy pass — live payment-signature strings

`SpaceStroker.Common/Resources/ErrorMessages.resx` (and its regenerated
`ErrorMessages.Designer.cs`). **Values only.** Every key below has ≥1 live reference and reaches a
player on the bet path.

| Key | Today | New value | Why |
| --- | --- | --- | --- |
| `SignatureWithoutBetAccount` | `The requested payment signature have not sent tokens from the request wallet. Please try sending a valid transaction.` | `That payment did not come from your wallet. Please try again with a new transaction.` | "signature have not sent" is ungrammatical; "the request wallet" is internal vocabulary |
| `SignatureWithoutOwnerAccount` | `The requested payment signature have not sent tokens to our accounts. Please try sending a valid transaction.` | `That payment did not reach our account. Please try again with a new transaction.` | same verb error |
| `SignaturePaidLessThanBetAmount` | `The requested payment signature has sent less tokens than informed in the request. ` | `That payment sent fewer tokens than this bet requires.` | trailing space; "less" → "fewer"; "informed in the request" is internal vocabulary |
| `SignaturePaidMoreThanBetAmount` | `The requested payment signature has sent more tokens than informed in the request. ` | `That payment sent more tokens than this bet requires.` | trailing space; same vocabulary |
| `PaymentSignatureHasBeenUsed` | `The requested payment signature has already been used. Make another transaction with the appropriate amount.` | `That payment has already been used for another bet. Please make a new transaction.` | says *what to do* without naming an amount it cannot know |
| `InvalidPaymentSignature` | `The requested payment signature is not valid. Please check the data and try again.` | `That payment could not be verified. Please try again.` | "check the data" is meaningless to a player — the data is on-chain |
| `AdviceOnDifferentSignatureBetAmount` | `If you requested a transaction and it has a different amount than you desired. You can request a refund for the transaction sending an email with your wallet and signature to {0}` | `If your transaction sent a different amount than you intended, you can request a refund by emailing your wallet address and the transaction signature to {0}.` | sentence fragment; **the `{0}` placeholder must survive** (`SolanaService.cs:674` formats `SupportEmail` into it) |
| `FailedToRegisterBet` | `The system could not register this bet at the moment, please try again later.` | `We could not register your bet right now. Please try again.` | "the system" is internal voice |

**Explicitly NOT changed** (verify, do not edit):

- `ChoiceAlreadyTaken`, `RefundWarning` — byte-identical output is load-bearing for #150.
- `GameNotValidForBets` — `constraints.md` §6; unknown vs inactive must stay indistinguishable.
- `OneBetPerPresetRound`, `SecondBetNeedsAnotherPlayer`, `DuplicateBetError`,
  `DuplicateBetRefundedError` — already correct (F1).
- All five `BetValidationResult` `[Description]` strings — already correct (F1). The file
  `SpaceStroker.Domain/Bets/ValueObjects/BetValidationResult.cs` is **not edited by this card**.
- The `Wallet*` / `Rg*` families and `SignatureForInvalidToken` — dead (F6).

### 6.3 Regenerating the designer file

`ErrorMessages.Designer.cs` is generated from the `.resx`. Let the tooling regenerate it (or mirror the
edits exactly); do not let the two drift.

---

## 7. Frontend behaviour (`monkify-front`)

### 7.1 New — `src/utils/apiError.ts`

```ts
// The API's error envelope: { data, messages: [{ type, value }] }.
// `type` is the NUMERIC MessageType from the backend — Informational 0, Alert 1,
// ValidationFailure 2, Error 3. There is no JsonStringEnumConverter on the API, so it is
// always a number.
//
// ⚠️ Two call sites used to filter `type === 1 /* ValidationFailure */`, which is Alert. They
// therefore never matched and always fell through to their generic fallback. Preferring 2 is
// the fix, not a behaviour change we chose for its own sake.
const VALIDATION_FAILURE = 2;

export const apiErrorMessage = (error: unknown): string | null => { /* … */ };
```

Contract:

1. Read `error?.response?.data?.messages` — anything else (no response, non-array, missing) → `null`.
2. Prefer the **first** entry with `type === VALIDATION_FAILURE` and a non-empty trimmed `value`.
3. Otherwise the **first** entry with a non-empty trimmed `value`, whatever its type.
4. Otherwise `null`.

Pure, dependency-free (no Vue, no axios import) — same discipline as `utils/transientError.ts`, so it
stays unit-testable once the repo has a test runner. Never throws on a malformed body: an HTML error
page, a string body, `messages: []` and `messages: null` all return `null`.

### 7.2 Changed — `src/services/handlers.ts`

The current interceptor:

```ts
setTimeout(() => {
  if (!error.handled) {
    console.error(error);
    toast.error(error.message || 'Unexpected Error, try again later');
  }
}, 200);
```

Becomes, keeping the shape intact:

- **The 200 ms `setTimeout` handshake stays.** It is what lets a call site's `catch` run first and set
  `handled`. The existing `// TODO: improve this logic` comment stays with it — rewriting that
  handshake is out of scope (§9).
- **`if (!error.handled)` stays** — the first and only gate.
- `console.error(error)` stays, **always**, with the raw error. This is the only place a status code
  or axios text may appear.
- The toast text becomes `apiErrorMessage(error) ?? API_ERROR_FALLBACK`, where
  `API_ERROR_FALLBACK = 'Something went wrong — please try again.'` **`error.message` is no longer
  used as a toast source** — it is the axios status line, which is the whole bug.
- **Dedupe:** pass a stable `toastId` derived from the message text so an identical message
  replaces/refreshes rather than stacking. `vue3-toastify` (`^0.2.8`) treats a repeated `toastId` as
  the same toast. Derive it deterministically from the string (the message itself is an acceptable id);
  do **not** use a counter or a timestamp, which would defeat it.

### 7.3 Refactor onto the helper — behaviour-preserving except where F3 applies

| File | Line(s) | Today | After |
| --- | --- | --- | --- |
| `components/admin/BetRefundManager.vue` | 12-13, 26-29 | local `firstValidationMessage`, `type === 1` | `apiErrorMessage(err)`; **starts showing real messages** (F3) |
| `components/admin/GameTypeManager.vue` | 47-48, 59-61, 110-112 | local `firstValidationMessage`, `type === 1` | `apiErrorMessage(err)`; **starts showing real messages** (F3) |
| `hooks/useOrbit.ts` | 202-204 (`serverMessage`) | `messages[0].value` | `apiErrorMessage(error)`; keep the surrounding doc comment — it explains *why* Orbit refusals must survive |
| `components/header/DisplayNamePrompt.vue` | 53-56 | inline `messages[0].value` | `apiErrorMessage(err)` |
| `components/header/PlayerCustomizationModal.vue` | 107-110 | inline `messages[0].value` | `apiErrorMessage(err)` |

After the refactor, **no duplicated `messages[0].value` / `messages.find(...type...)` extraction may
remain anywhere in `src/`** — that is grep-checkable and is AC 11.

### 7.4 The double-toast sweep (new — not in the card)

Today the interceptor's toast is *noise* ("Request failed with status code 400"), so a site that also
reports the failure itself produces one useful message plus one meaningless one. Once the toast
becomes meaningful, those sites report the **same sentence twice**. Each must mark
`err.handled = true` — exactly the fix `useOrbit.ts:212-218` already documents ("one refusal reported
twice … *lets not show that toaster*").

| File | Line | Reports failure itself | Marks `handled` today | Action |
| --- | --- | --- | --- | --- |
| `hooks/useGame.ts` | ~381 | `toast.error('Failed to load game data. Please try again later.')` | ❌ | **Mark handled** |
| `hooks/useTokenBalance.ts` | ~115 | `toast.error('Failed to fetch token balance')` (non-silent reads only) | ❌ | **Mark handled** on the non-silent branch; the `opts.silent` poll branch must mark it too, or a background poll failure starts toasting |
| `components/header/DisplayNamePrompt.vue` | ~52 | inline `errorMessage` | ❌ | **Mark handled** |
| `components/header/PlayerCustomizationModal.vue` | ~106 | inline `errorMessage` | ❌ | **Mark handled** |
| `pages/GamePage.vue` | 80 | own toast | ✅ | unchanged |
| `components/game-selection/GameTypes.vue` | 47 | own toast | ✅ | unchanged |
| `hooks/useBootRecovery.ts` | 53 | retry overlay | ✅ (absorbed attempts only) | unchanged — the **final** rethrow is deliberately left unmarked so a terminal failure still toasts, and it now toasts something useful |
| `hooks/useOrbit.ts`, `useOrbitRedirect.ts`, `OrbitTelegramTab.vue` | various | inline `lastError` | ✅ | unchanged |

⚠️ `useTokenBalance`'s **silent** branch is the trap: it deliberately swallows background poll
failures with `console.warn`. Without `handled = true` it would start toasting every poll blip.

### 7.5 Unchanged — stated so nobody "helpfully" edits them

- **`useGame.ts` `submitBet`'s catch** (~571-578): same generic `betOutput`, same `onBetFail()` (lever
  buzz + error SFX), same `isLoadingBet = false`, same `lastOwnBetChoice` reset. The toast is now
  correct because the interceptor is. **Do not add a message to the terminal.**
- **`GameTerminal.vue`** — not touched.
- **The pre-pay guards** in `bet()` — not touched (F4, and §9).
- **`services/adminService.ts`** — its 401 → `navigateToError()` interceptor is untouched, and it still
  adds no toast.
- **Every bet path** — free, desktop SolanaToken, embedded wallet-bridge popup, mobile deeplink resume
  — already funnels through `submitBet`, so all four inherit the fix with no per-path work (AC 10).

### 7.6 The 23 `mainAPI` call-site sweep

Confirm each surfaces a real message or is intentionally silent, and record the result in the
implementation report (AC 12).

**`services/mainService.ts` (14):** `getGamesTypes`, `getGameInstance`, `applyBet`,
`getGameVerification`, `updatePlayerPreferences`, `getPlayerPreferences`, `getWalletNonce`,
`setSelfExclusion`, `getGameTypeStats`, `getGlobalStats`, `getPlayerStats`, `getRecentRounds`,
`getGamesHistory`, `getTokenBalance`.

**`services/orbitService.ts` (9):** `createOrbitSession`, `getOrbit`, `creditOrbitDeposit`,
`requestOrbitCashOut`, `getOrbitHistory`, `patchOrbitSettings`, `startTelegramLink`,
`verifyTelegramLink`, `unlinkTelegram`.

Two wrappers change *when* the interceptor is reached but not *what* it does — note them rather than
"fixing" them: `singleFlight` (concurrent identical requests share one rejection, so one toast, which
the dedupe reinforces) and `runWithRecovery` (transient attempts are marked `handled`; only the final
terminal error toasts).

---

## 8. Flow diagram

```mermaid
sequenceDiagram
  participant P as Player
  participant G as useGame.submitBet
  participant A as mainAPI
  participant I as handlers.ts interceptor
  participant B as RegisterBetHandler

  P->>G: pull lever (word already reserved cross-round)
  G->>A: POST /games/{id}/bets
  A->>B: register bet
  B->>B: OrbitChoiceClaims hit → RefundInvalidBet(ChoiceAlreadyTaken)
  B-->>A: 400 { messages:[{ type:2, value:"…taken… Your bet should be refunded soon." }] }
  A-->>G: reject
  G->>G: betOutput = "There was an error processing your bet"; onBetFail(); isLoadingBet = false
  Note over G,I: catch runs first — submitBet does NOT set handled
  A->>I: rejected (after the 200 ms handshake)
  I->>I: apiErrorMessage(err) → the backend sentence
  I-->>P: toast (deduped by message text)
```

---

## 9. Out of scope

- Any client-side / pre-pay detection of cross-round Orbit reservations, and any endpoint listing
  reserved or unavailable words. **Both leak Orbit** (#150).
- Changing the terminal (`betOutput`) copy, or putting the message in the terminal.
- Distinct wording for the reserved-word case — **forbidden**.
- Arcade re-styling of the toast component, the sounds, or the lever buzz.
- `adminAPI` toasting, and any change to its 401 → `/error` routing.
- i18n / PT-BR copy.
- Rewriting the `handled` handshake beyond what this needs — the `// TODO: improve this logic` stays.
- The dead `Wallet*` / `Rg*` resource families, `SignatureForInvalidToken`, and the
  `ParameterNotFound` / `ParameterHasNoActiveGames` §6 tension (F6).
- Wiring an emitter for `token-expired` / `setRelaunching` (F5) — this card only must not regress it.

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/SpaceStroker.Common/Resources/ErrorMessages.resx` | Reword the 8 keys in §6.2. Values only. Preserve `{0}` in `AdviceOnDifferentSignatureBetAmount`. |
| 2 | `src/SpaceStroker.Common/Resources/ErrorMessages.Designer.cs` | Regenerate alongside the `.resx` (doc comments carry the value). |

**No other backend file changes.** `BetValidationResult.cs`, `RegisterBetHandler.cs`, the filters and
every consumer are untouched.

### 10.2 Frontend (`monkify-front`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/utils/apiError.ts` | **New.** `apiErrorMessage(err): string \| null` + the `API_ERROR_FALLBACK` constant (§7.1). |
| 2 | `src/services/handlers.ts` | Toast `apiErrorMessage(error) ?? API_ERROR_FALLBACK`; add `toastId` dedupe; keep the `handled` gate, the 200 ms handshake and `console.error` (§7.2). |
| 3 | `src/components/admin/BetRefundManager.vue` | Drop local `firstValidationMessage`; use the helper (§7.3). |
| 4 | `src/components/admin/GameTypeManager.vue` | Drop local `firstValidationMessage` (3 sites); use the helper (§7.3). |
| 5 | `src/hooks/useOrbit.ts` | `serverMessage` delegates to the helper; keep the doc comment (§7.3). |
| 6 | `src/components/header/DisplayNamePrompt.vue` | Use the helper **and** mark `handled` (§7.3, §7.4). |
| 7 | `src/components/header/PlayerCustomizationModal.vue` | Use the helper **and** mark `handled` (§7.3, §7.4). |
| 8 | `src/hooks/useGame.ts` | Mark `handled` at the `getGameInstance` catch (~381) **only**. `submitBet`'s catch is untouched (§7.4, §7.5). |
| 9 | `src/hooks/useTokenBalance.ts` | Mark `handled` on **both** branches of the catch (~112-115) (§7.4). |

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/api-error-messages.md` | This spec. |
| 2 | `INDEX.md` | KB map row + "Feature currently in flight". |
| 3 | `conventions/frontend.md` | At ship: record the envelope contract (`type` is numeric, `ValidationFailure = 2`), `apiErrorMessage` as the single extraction, and the rule that a site reporting a failure itself must mark `handled`. |
| 4 | `features/api-error-messages.md` | At ship (`/monkify-ship-feature`), not now. |

---

## 11. Tests

Project: `monkify-backend/src/SpaceStroker.Tests` — run with **`run_tests.bat`**.

**No new tests are required, and no test file is expected to change.** The copy pass touches only
values that every assertion reaches symbolically (F2). The job is to **prove** that:

| Test file | What to verify | Expected |
| --- | --- | --- |
| `UnitTests/Handlers/RegisterBetHandlerTests.cs` | ~23 assertions built from `ErrorMessages.X` / `BetValidationResult.X.StringValueOf()` (lines 139, 157, 176, 199, 285, 303, 320, 374, 415, 438, 486, 509, 533, 556, 586, 741, 791, 845, 867, 941, 989, 1045) | Green with **zero** edits — none of the reworded keys appears here except via the symbol |
| `Tests.Integration/ChoiceExclusivityFlowTests.cs` | Lines 62, 88 use `ErrorMessages.ChoiceAlreadyTaken` (not reworded). **Line 103 is the one literal:** `ShouldContain("invalid characters")` → `BetValidationResult.InvalidCharacters`, also not reworded | Green with **zero** edits |
| `Tests.Integration/OperatorlessFlowTests.cs` | Line 179 uses `ErrorMessages.ChoiceAlreadyTaken` | Green with **zero** edits |

If any of these **does** need editing, stop — it means a key was reworded that §6.2 said to leave
alone. Fix the `.resx`, not the assertion.

**Frontend:** `monkify-front` has no test harness. The bar is `yarn lint` + `yarn build` clean, plus
the manual pass in §13.

---

## 12. Acceptance criteria

From the Trello checklist, with **two amendments** flagged inline (F3, F5).

1. Given a free-text round where another player already bet `abcdef`, when I place a paid SolanaToken
   bet on `abcdef`, then the toast shows the backend text (*"This sequence has already been taken by
   another player. Pick a different one. Your bet should be refunded soon."*) and never *"Request
   failed with status code 400"*.
   > ⚠️ Per **F4**, the pre-pay guard blocks this before payment when the bet list is current. Verify
   > via criterion 2, or by forcing a stale list.
2. Given a word another player has cross-round reserved for their Orbit on a free-text game type, when
   I bet that word in a round Orbit is not betting in, then I see **exactly** the same message as a
   same-round collision — no wording, timing or extra field reveals that a reservation (or Orbit)
   exists.
3. Given any bet rejected by the backend with a validation message (duplicate bet,
   one-bet-per-preset-round, only-player-so-far, invalid characters/length/amount, game not accepting
   bets, failed to register), when the 400 returns, then the toast shows that specific backend message.
4. Given a bet is rejected for any reason, when the toast appears, then the terminal still shows the
   unchanged generic `> There was an error processing your bet`, the lever buzz and error SFX still
   fire, and the bet loading state is cleared.
5. Given a request fails with no usable message (500, timeout, offline, HTML error page,
   empty/malformed `messages`), when the error surfaces, then the toast shows the single friendly
   fallback and never an HTTP status code or raw axios text, while the raw error still reaches
   `console.error`.
6. Given I retry the same rejected bet several times quickly, when each rejection returns, then
   identical toasts do not stack — one toast is shown/refreshed at a time.
7. Given a call site that sets `err.handled = true` (GamePage, GameTypes, useBootRecovery, useOrbit*,
   OrbitTelegramTab), when its request fails, then **no** toast is shown at all.
8. **[Amended — F5]** Given an embedded (iframe) session, when a request returns 401, then behaviour is
   **unchanged from before this card**: no new 401 branch is added to `mainAPI`, `adminAPI`'s
   401 → `/error` routing is untouched, and nothing new renders over an embedded frame. *(The card's
   original wording assumed a live `token-expired` → relaunch flow; `setRelaunching` and the
   `token-expired` post have zero emitters in `src/`.)*
9. Given a **free** bet is rejected as a taken/reserved choice, when the toast appears, then it does
   **not** promise a refund. *(Comes for free: `RefundInvalidBet` appends `RefundWarning` only on the
   paid branch — §6.1.)*
10. Given the bet is placed from any path (free, desktop SolanaToken, embedded wallet-bridge popup,
    mobile deeplink resume), when it is rejected, then the same correct message is surfaced.
11. **[Amended — F3]** Given the shared `apiErrorMessage()` helper exists, when the 5 previously
    duplicated extractions are refactored onto it, then **no duplicated `messages[0].value` /
    `messages.find(...type...)` extraction remains in `src/`**, the three `messages[0].value` sites
    (useOrbit, DisplayNamePrompt, PlayerCustomizationModal) render **exactly** as before, and the two
    admin sites (BetRefundManager, GameTypeManager) now render the **real backend message** where they
    previously always fell through to a generic fallback because they filtered `type === 1` (Alert)
    instead of `2` (ValidationFailure).
12. Given all 23 `mainAPI` call sites (14 `mainService` + 9 `orbitService`) are audited, when the sweep
    is reported, then each is either confirmed to surface a real backend message or explicitly listed
    as intentionally silent, with the reason.
13. Given the backend copy pass, when `ErrorMessages.resx` is reworded, then `GameNotValidForBets`,
    `ChoiceAlreadyTaken`, `RefundWarning` and every auth/wallet-proof message remain **exactly** as
    vague as before (`constraints.md` §6), and no message becomes more specific about *why*.
14. Given the reworded backend strings, when `run_tests.bat` is executed, then the whole suite is green
    **with zero test-file edits** (F2), and `yarn lint` + `yarn build` pass on the frontend.
15. **[New]** Given a call site that reports a failure itself (`useGame` game-data load,
    `useTokenBalance`, `DisplayNamePrompt`, `PlayerCustomizationModal`), when its request fails, then
    the player sees the message **once** — not the same sentence in both an inline line/own toast and
    the interceptor toast (§7.4).

---

## 13. Manual QA

Local Docker stack (see `AGENTS.md` / memory: `docker start`, never `compose up`, to keep the
5433/6380 remap).

1. **Fallback line.** Stop the API container. Load the lobby → toast reads
   *"Something went wrong — please try again."*, never a status code. `console.error` still shows the
   raw axios error.
2. **Reserved word (the reliable repro, AC 2).** With two players: player A enables Orbit on a
   free-text game type (Cosmic Sprint) and claims a word. As player B, bet that word in a round Orbit
   is **not** betting in → toast shows the same sentence a same-round collision produces, ending in
   *"Your bet should be refunded soon."*
3. **Free-bet variant (AC 9).** Repeat step 2 with the free-bets operator (`POST /dev/launch-token` +
   `#launch_token=`) → same message, **no** refund sentence.
4. **Dedupe (AC 6).** Pull the lever three times fast on the rejected word → one toast, refreshed; not
   a stack of three.
5. **Terminal unchanged (AC 4).** Confirm `> There was an error processing your bet` still appears and
   the lever buzz still fires on each attempt.
6. **No double report (AC 15).** Open the display-name prompt and submit a name the backend rejects →
   the inline error line appears and **no** toast.
7. **Silent poll (AC 15).** Leave the game open with the API stopped → the background token-balance
   poll must stay silent (console only), the number must not blank.
8. **Admin (AC 11).** In the admin portal, submit an invalid game type → the real backend validation
   message now renders inline instead of *"Failed to save game type."*

---

## 14. Trello / pipeline

- **Source card:** [#158 — response and handling/UI when betting on a reserverd word](https://trello.com/c/cmb6wfm7)
  — list **Specs created**; the **Implementation** section on the card carries branch, worktree and
  spec path.
- **Worktree:** `.worktrees/api-error-messages/<repo>` — `monkify-backend`, `monkify-front`,
  `monkify-meta`.
- **Next:** `/monkify-implement-feature` → then `/monkify-ship-feature`.

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-08-07 | Initial spec from refined card #158. Backend copy pass **retargeted** at the live payment-signature strings after F1 showed the card's targets were already the current values; helper's `type` filter corrected to `2` (F3); double-toast sweep (§7.4) and AC 15 added; AC 8 and AC 11 amended against F5 and F3. |
| 2026-08-08 | **Shipped.** Two owner decisions taken at implementation time — see §16. |

---

## 16. What changed at implementation time (2026-08-08)

Two things this document got wrong, both found while implementing and both decided by the owner. The
**code wins**; this section records why.

### D1 — The trailing space was load-bearing (`SolanaService.cs` joins the backend's two files)

§6.2 says to strip the trailing space from `SignaturePaidLessThanBetAmount` /
`SignaturePaidMoreThanBetAmount`, and §10.1 says "no other backend file changes". Both cannot be true:
`SolanaService.GenerateErrorMessageForTokenExchange` (`SolanaService.cs:665-677`) builds one sentence by
concatenation, and that trailing space was the **only** separator before
`AdviceOnDifferentSignatureBetAmount`. Following both instructions literally produces
`…than this bet requires.If your transaction sent…`. No test asserts the concatenation, so nothing
would have caught it.

**Decision: join with an explicit `" "` in code**, so the values can be trimmed as §6.2 intends.
`src/SpaceStroker.Infrastructure/Services/Solana/SolanaService.cs` therefore joins the two files in
§10.1. One line, string joining only — no logic change. Recorded as a convention in
`conventions/backend.md`: never let whitespace inside a resource value be a separator.

### D2 — §7.4's "own toast — unchanged" was wrong for three sites (`displayableErrorMessage`)

§7.4 lists `GamePage.vue:80` and `GameTypes.vue:47` as "own toast — unchanged", on the assumption that a
site with its own toast is already serving the player. It is not: both do
`toast.error(err.message || 'Failed to fetch game types')`, and `err.message` on an AxiosError **is**
`Request failed with status code 500`. The lobby and the game page — the two most-seen screens — would
have kept printing the exact string this spec exists to remove.

A third site is worse. `ResponsibleGamingPage.vue:85` toasted `error.message` **and did not mark
`handled`**, so a failed self-exclusion showed `Request failed with status code 400` **twice** — its own
toast and the interceptor's.

**Decision: fix all three.** They keep `handled = true` (AC 7 untouched — the interceptor still stays
silent) and keep their existing fallback copy. `ResponsibleGamingPage` gains the flag it was missing.

This needed a second helper, because these surfaces render the failure themselves and also receive
**non-HTTP** throws whose `message` is a real sentence worth showing ("This wallet cannot sign
messages."):

```ts
export const displayableErrorMessage = (error: unknown, fallback: string): string;
```

It returns `apiErrorMessage(error)` when the envelope has one; otherwise the thrown `message` **only
when the error carries no `response`** (i.e. it is not an HTTP failure); otherwise the fallback. The
presence of `response` is what separates a real thrown sentence from an axios status line. It also
replaced the `apiErrorMessage(err) ?? (err as Error)?.message ?? fallback` chain in `DisplayNamePrompt`
and `PlayerCustomizationModal`, which had the same latent leak on a 500 with no envelope.

### Left open, deliberately

**8 sites in `useOrbit.ts`** (~646, 730, 781, 997, 1023, 1042, 1075, 1102) still read
`serverMessage(error) ?? (error instanceof Error ? error.message : '…')`, so a 500 with no envelope
leaks the axios status line into Orbit's inline `lastError`. One-line fix each via
`displayableErrorMessage`; left out to keep this card inside the scope §7.3 set for the money
composable. Also still open: the F6 observations (dead `Wallet*`/`Rg*` families,
`ParameterNotFound` vs `ParameterHasNoActiveGames`).

### Verified at ship

`run_tests.bat` **901/901** and `run_integration_tests.bat` **116/116**, both green with **zero
test-file edits** — exactly what §11/F2 predicted. `ChoiceExclusivityFlowTests.cs:103`
(`ShouldContain("invalid characters")`) passes untouched. Frontend: `yarn lint` + `yarn build` clean.
The manual QA in §13 was **not** run — the owner shipped without the local-stack pass.

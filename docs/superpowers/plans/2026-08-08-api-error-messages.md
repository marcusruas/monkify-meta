# Real API Error Messages — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended)
> or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`)
> syntax for tracking.

**Goal:** Make the shared axios interceptor surface the backend's own error sentence on every
`mainAPI` endpoint, instead of `"Request failed with status code 400"`.

**Architecture:** One new pure helper (`src/utils/apiError.ts`) extracts
`response.data.messages[].value` from the API's `ApiResult` envelope, preferring
`type === 2` (`ValidationFailure`). The interceptor (`src/services/handlers.ts`) toasts that value,
falling back to one friendly line, with a message-derived `toastId` so identical toasts dedupe. Five
hand-rolled copies of that extraction refactor onto the helper. Four call sites that already report
their own failure mark `err.handled = true` so the now-meaningful toast does not duplicate them. On
the backend, eight live payment-signature strings get a copy pass — values only.

**Tech Stack:** Vue 3.2 + TypeScript 5.5 (Vue CLI/webpack, yarn), axios, `vue3-toastify ^0.2.8`;
.NET 8 (`SpaceStroker.Common` RESX resources), xUnit + Shouldly.

**Source spec:** `monkify-meta/specs/api-error-messages.md` (authoritative — read §3 findings F1–F6
before starting). **Card:** [#158](https://trello.com/c/cmb6wfm7).

## Global Constraints

- **Worktree-per-repo.** All work happens in `.worktrees/api-error-messages/<repo>` on
  `feature/api-error-messages`. The hubs stay on `main` — never `git checkout`/`switch` in a hub.
- **`yarn lint` CRLF trap.** `monkify-front` files are CRLF. **Never** use `sed -i`, `tr`, or any
  shell rewrite on them — prettier then fails with `Insert ␍`. Use the Edit/Write tools only. If a
  file's endings get mangled, fix with `unix2dos` and verify with `od -c`.
- **Backend tests run via `scripts/run_tests.bat`**, never bare `dotnet test` (a bare run pulls in the
  slow `ProductionGames` Monte Carlo suite and "fails" ~156 by design).
- **Zero test-file edits are expected** (spec §11 / F2). If a test needs editing, a forbidden key was
  reworded — fix the `.resx`, not the assertion.
- **`constraints.md` §6 — vague messaging survives.** `GameNotValidForBets`, `ChoiceAlreadyTaken`,
  `RefundWarning` and every auth/wallet-proof message are **not** touched and must not become more
  specific about *why*.
- **Orbit invisibility (#150).** The cross-round reserved-word reject stays **byte-identical** to a
  same-round collision. A distinct "reserved" wording is forbidden.
- **No new backend integration test** (`AGENTS.md` rule 8 scopes to new features; this is a UX/bug fix
  plus a copy pass). `ChoiceExclusivityFlowTests` and `OperatorlessFlowTests` must stay green.
- **No EF/migration/entity/enum/endpoint surface.** No `VUE_APP_*` or `GeneralSettings` changes.
- **Secrets/config untouched.**
- **Fallback copy, verbatim:** `Something went wrong — please try again.` (em dash, not a hyphen).

## Owner decision taken during planning (2026-08-08)

**The trailing space on `SignaturePaidLessThanBetAmount` / `SignaturePaidMoreThanBetAmount` is
load-bearing.** `SolanaService.GenerateErrorMessageForTokenExchange` (`SolanaService.cs:665-677`)
builds one sentence by concatenation, and that trailing space is the **only** separator between the
amount message and `AdviceOnDifferentSignatureBetAmount`. Spec §6.2 says to strip the space while
§10.1 says "no other backend file changes" — doing both literally yields
`…than this bet requires.If your transaction sent…`. **Owner chose: join with `" "` in code**
(Task 1, Step 5), so the two values can be trimmed as §6.2 intends. This adds
`SolanaService.cs` to the backend file list — one line, string joining only, no logic change. No test
asserts the concatenated string, so nothing else moves.

## Decision taken during planning (agent, low-risk, per `constraints.md` §8)

**`err.handled = true` is inlined at the four new sites, not extracted into a shared helper.** The
repo's existing idiom is the inline cast (`useBootRecovery.ts:53`, `useOrbitRedirect.ts:61/77`,
`GamePage.vue:80`, `GameTypes.vue:47`); `useOrbit.ts` keeps its own local `markHandled` because it
calls it a dozen times. Adding a shared export would create a second idiom the spec did not ask for,
and §9 forbids rewriting the `handled` handshake beyond what this needs.

---

## File Structure

### `monkify-backend` (3 files)

| File | Responsibility |
| --- | --- |
| `src/SpaceStroker.Common/Resources/ErrorMessages.resx` | The 8 reworded values (§6.2). Source of truth for the copy. |
| `src/SpaceStroker.Common/Resources/ErrorMessages.Designer.cs` | Generated accessors; its XML doc comments carry the value and must not drift from the `.resx`. |
| `src/SpaceStroker.Infrastructure/Services/Solana/SolanaService.cs` | Line ~674 only: make the separator between the amount message and the refund advice an explicit `" "`. |

### `monkify-front` (9 files)

| File | Responsibility |
| --- | --- |
| `src/utils/apiError.ts` | **New.** The single extraction of the API error envelope + the fallback constant. Pure, dependency-free. |
| `src/services/handlers.ts` | The `mainAPI` response interceptor: toast the extracted message, dedupe by message, keep the `handled` gate / 200 ms handshake / `console.error`. |
| `src/components/admin/BetRefundManager.vue` | Drops its local `firstValidationMessage`; consumes the helper. |
| `src/components/admin/GameTypeManager.vue` | Drops its local `firstValidationMessage` (2 call sites); consumes the helper. |
| `src/hooks/useOrbit.ts` | `serverMessage` delegates to the helper; the explanatory doc comment stays. |
| `src/components/header/DisplayNamePrompt.vue` | Consumes the helper **and** marks `handled`. |
| `src/components/header/PlayerCustomizationModal.vue` | Consumes the helper **and** marks `handled`. |
| `src/hooks/useGame.ts` | Marks `handled` at the `getGameInstance` catch only. `submitBet`'s catch is untouched. |
| `src/hooks/useTokenBalance.ts` | Marks `handled` on **both** branches of its catch. |

### `monkify-meta` (2 files, this task)

| File | Responsibility |
| --- | --- |
| `docs/superpowers/plans/2026-08-08-api-error-messages.md` | This plan. |
| `specs/api-error-messages.md` | Already committed (`df02011`). Amended only if implementation contradicts it. |

> `INDEX.md`, `conventions/frontend.md` and `features/api-error-messages.md` are **ship-time** work
> (`/monkify-ship-feature`), not this plan.

---

## Task 1: Backend copy pass

**Files:**
- Modify: `src/SpaceStroker.Common/Resources/ErrorMessages.resx` (8 `<value>` elements)
- Modify: `src/SpaceStroker.Common/Resources/ErrorMessages.Designer.cs` (8 XML doc comments)
- Modify: `src/SpaceStroker.Infrastructure/Services/Solana/SolanaService.cs:674`
- Test: no test file changes expected — `SpaceStroker.Tests` must be green with **zero** edits

**Interfaces:**
- Consumes: nothing.
- Produces: the exact player-facing sentences the frontend will render verbatim. No API shape change —
  `ApiResult.messages[].value` still carries them, `type` is still numeric `2`.

Working directory for every command in this task:
`C:\Users\kurok\Desktop\monkify\.worktrees\api-error-messages\monkify-backend`

- [ ] **Step 1: Establish the green baseline BEFORE touching anything**

There are no new tests to write here (spec §11: the assertions are symbolic — `ErrorMessages.X`,
`BetValidationResult.X.StringValueOf()` — so a value change flows through automatically). The
verification discipline is therefore *green before → green after with zero test edits*.

```bash
cd /c/Users/kurok/Desktop/monkify/.worktrees/api-error-messages/monkify-backend
scripts/run_tests.bat
```

Expected: PASS. Record the passed/failed counts — Step 7 must match them exactly.
If this is already red, **stop** and report; do not start a copy pass on a red suite.

- [ ] **Step 2: Reword the 8 values in `ErrorMessages.resx`**

Values only. Do not rename a key, do not reorder, do not add or remove a `<data>` element. Each pair
below is an exact-match edit.

| Key | Old value (exact) | New value (exact) |
| --- | --- | --- |
| `SignatureWithoutBetAccount` | `The requested payment signature have not sent tokens from the request wallet. Please try sending a valid transaction.` | `That payment did not come from your wallet. Please try again with a new transaction.` |
| `SignatureWithoutOwnerAccount` | `The requested payment signature have not sent tokens to our accounts. Please try sending a valid transaction.` | `That payment did not reach our account. Please try again with a new transaction.` |
| `SignaturePaidLessThanBetAmount` | `The requested payment signature has sent less tokens than informed in the request. ` *(note the trailing space)* | `That payment sent fewer tokens than this bet requires.` *(no trailing space)* |
| `SignaturePaidMoreThanBetAmount` | `The requested payment signature has sent more tokens than informed in the request. ` *(note the trailing space)* | `That payment sent more tokens than this bet requires.` *(no trailing space)* |
| `PaymentSignatureHasBeenUsed` | `The requested payment signature has already been used. Make another transaction with the appropriate amount.` | `That payment has already been used for another bet. Please make a new transaction.` |
| `InvalidPaymentSignature` | `The requested payment signature is not valid. Please check the data and try again.` | `That payment could not be verified. Please try again.` |
| `AdviceOnDifferentSignatureBetAmount` | `If you requested a transaction and it has a different amount than you desired. You can request a refund for the transaction sending an email with your wallet and signature to {0}` | `If your transaction sent a different amount than you intended, you can request a refund by emailing your wallet address and the transaction signature to {0}.` |
| `FailedToRegisterBet` | `The system could not register this bet at the moment, please try again later.` | `We could not register your bet right now. Please try again.` |

⚠️ **`{0}` must survive in `AdviceOnDifferentSignatureBetAmount`** — `SolanaService.cs:674` formats
`_settings.Games.SupportEmail` into it. A missing `{0}` throws nothing but silently drops the support
address.

⚠️ **Do NOT touch:** `ChoiceAlreadyTaken`, `RefundWarning`, `GameNotValidForBets`,
`OneBetPerPresetRound`, `SecondBetNeedsAnotherPlayer`, `DuplicateBetError`,
`DuplicateBetRefundedError`, `ParameterNotFound`, `ParameterHasNoActiveGames`,
`SignatureForInvalidToken`, and every `Wallet*` / `Rg*` key. Also do **not** open
`src/SpaceStroker.Domain/Bets/ValueObjects/BetValidationResult.cs` — its five `[Description]` strings
are already correct (F1).

- [ ] **Step 3: Verify no forbidden key moved**

```bash
git -C /c/Users/kurok/Desktop/monkify/.worktrees/api-error-messages/monkify-backend diff --stat
git -C /c/Users/kurok/Desktop/monkify/.worktrees/api-error-messages/monkify-backend diff -- src/SpaceStroker.Common/Resources/ErrorMessages.resx
```

Expected: exactly one file changed so far, exactly 8 `<value>` lines changed (8 additions,
8 deletions). If the count differs, revert the extra edit.

- [ ] **Step 4: Mirror the 8 values into `ErrorMessages.Designer.cs`**

The designer file is generated from the `.resx`; its accessors do not change, only the Portuguese
doc-comment lines that quote the value:

```csharp
/// <summary>
///   Consulta uma cadeia de caracteres localizada semelhante a <VALUE>.
/// </summary>
public static string SignatureWithoutBetAccount {
```

For each of the 8 keys, replace `<VALUE>` with the **new** value from Step 2, preserving the
surrounding `Consulta uma cadeia de caracteres localizada semelhante a ` prefix and the trailing `.`
that the generator appends. Do not touch the accessor bodies, the class header, or any other key.
Regenerating via tooling is equally acceptable — what matters is that the two files do not drift.

- [ ] **Step 5: Make the sentence separator explicit in `SolanaService.cs`**

The two amount values lost their trailing space in Step 2, so the concatenation needs a real
separator. Exact edit at `src/SpaceStroker.Infrastructure/Services/Solana/SolanaService.cs`:

```csharp
// BEFORE
            errorMessage += string.Format(ErrorMessages.AdviceOnDifferentSignatureBetAmount, _settings.Games.SupportEmail);

// AFTER
            // The amount message and this advice are one paragraph to the player. The separator used to
            // be a trailing space inside the .resx value, which is invisible in an editor and was lost the
            // first time anyone tidied the copy — join explicitly instead.
            errorMessage += " " + string.Format(ErrorMessages.AdviceOnDifferentSignatureBetAmount, _settings.Games.SupportEmail);
```

Nothing else in `GenerateErrorMessageForTokenExchange` changes.

- [ ] **Step 6: Confirm the composed sentence reads correctly**

No test covers this concatenation (verified: no reference to `SignaturePaid*` or
`AdviceOnDifferentSignatureBetAmount` exists in `SpaceStroker.Tests` or
`SpaceStroker.Tests.Integration`), so verify by reading the composition:

`That payment sent fewer tokens than this bet requires.` + `" "` +
`If your transaction sent a different amount than you intended, you can request a refund by emailing your wallet address and the transaction signature to support@example.com.`

Expected: one space between the two sentences, no double space, no missing space, `{0}` replaced.

- [ ] **Step 7: Run the suite — must be green with ZERO test edits**

```bash
cd /c/Users/kurok/Desktop/monkify/.worktrees/api-error-messages/monkify-backend
scripts/run_tests.bat
```

Expected: PASS, with the same counts as Step 1.
Then prove no test file moved:

```bash
git -C /c/Users/kurok/Desktop/monkify/.worktrees/api-error-messages/monkify-backend status --porcelain
```

Expected: exactly three modified files — `ErrorMessages.resx`, `ErrorMessages.Designer.cs`,
`SolanaService.cs`. **If any file under `SpaceStroker.Tests*` appears, stop** — a forbidden key was
reworded (F2); revert the `.resx` change for that key instead of editing the assertion.

- [ ] **Step 8: Run the integration suite (Docker must be running)**

```bash
cd /c/Users/kurok/Desktop/monkify/.worktrees/api-error-messages/monkify-backend
scripts/run_integration_tests.bat
```

Expected: PASS. `ChoiceExclusivityFlowTests` (lines 62, 88 use `ErrorMessages.ChoiceAlreadyTaken`;
line 103 is the one literal, `ShouldContain("invalid characters")` → `BetValidationResult.InvalidCharacters`)
and `OperatorlessFlowTests` (line 179) all reference keys this task does **not** reword, so they pass
untouched. Verify line 103 still passes — do not pre-emptively change it.

- [ ] **Step 9: Commit**

```bash
git -C /c/Users/kurok/Desktop/monkify/.worktrees/api-error-messages/monkify-backend add \
  src/SpaceStroker.Common/Resources/ErrorMessages.resx \
  src/SpaceStroker.Common/Resources/ErrorMessages.Designer.cs \
  src/SpaceStroker.Infrastructure/Services/Solana/SolanaService.cs
git -C /c/Users/kurok/Desktop/monkify/.worktrees/api-error-messages/monkify-backend commit -m "Reword the live payment-signature error strings for players (monkify-backend)."
```

> Do **not** push yet — pushing happens once at Step 10 of the skill, after the user signs off.

---

## Task 2: The `apiErrorMessage()` helper

**Files:**
- Create: `src/utils/apiError.ts`
- Test: none — `monkify-front` has no test runner (spec §11). Verification is `yarn lint`, `yarn build`,
  and the shaped-input reasoning in Step 2.

**Interfaces:**
- Consumes: nothing (pure, no Vue, no axios import — same discipline as `utils/transientError.ts`).
- Produces:
  - `export const apiErrorMessage: (error: unknown) => string | null`
  - `export const API_ERROR_FALLBACK: string` — `'Something went wrong — please try again.'`

  Tasks 3 and 4 import both from `@/utils/apiError`.

Working directory: `C:\Users\kurok\Desktop\monkify\.worktrees\api-error-messages\monkify-front`

- [ ] **Step 1: Create the file**

Write `src/utils/apiError.ts` with exactly this content (CRLF endings — use the Write tool, never a
shell heredoc):

```ts
/**
 * The one place the API's error envelope is read.
 *
 * Every rejected request comes back as `{ data, messages: [{ type, value }] }` — the backend wrote
 * the player a real sentence ("This sequence has already been taken by another player…") and the
 * interceptor used to throw it away in favour of `error.message`, which on an AxiosError is the
 * status line: "Request failed with status code 400". That is the whole bug this exists to fix.
 *
 * ⚠️ `type` is the NUMERIC `MessageType` from the backend — Informational 0, Alert 1,
 * ValidationFailure 2, Error 3 (SpaceStroker.Common/Messaging/Models/MessageType.cs). There is no
 * JsonStringEnumConverter on the API, so it is always a number, never "ValidationFailure". Two call
 * sites used to filter `type === 1 /* ValidationFailure *␍/`, which is actually Alert — they never
 * matched and always fell through to their generic fallback. Preferring 2 is that fix, not a
 * behaviour change chosen for its own sake.
 *
 * Deliberately pure and dependency-free — no Vue, no axios import — so it can be called with a
 * hand-built object once this repo has a test runner. See monkify-meta/specs/api-error-messages.md §7.1.
 */
const VALIDATION_FAILURE = 2;

/** Shown when a failure carries no message we can show — a 500, a timeout, an HTML error page, offline. */
export const API_ERROR_FALLBACK = 'Something went wrong — please try again.';

type ApiMessage = { type?: number; value?: string | null };

const usable = (message: ApiMessage | null | undefined): string | null => {
  const value = message?.value;
  if (typeof value !== 'string') return null;
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : null;
};

export const apiErrorMessage = (error: unknown): string | null => {
  const messages = (
    error as { response?: { data?: { messages?: ApiMessage[] | null } | null } } | null | undefined
  )?.response?.data?.messages;

  // A string body (HTML error page), a missing envelope, `messages: null` and a non-array all land here.
  if (!Array.isArray(messages)) return null;

  // The backend's own validation copy is what the player is owed; anything else is a fallback.
  for (const message of messages) {
    if (message?.type === VALIDATION_FAILURE) {
      const value = usable(message);
      if (value) return value;
    }
  }

  for (const message of messages) {
    const value = usable(message);
    if (value) return value;
  }

  return null;
};
```

> The `*␍/` inside the doc comment above is only to keep this plan's own code fence from closing the
> comment early — write a normal `*/` in the file.

- [ ] **Step 2: Walk the contract against every shape the API can produce**

Confirm by reading, one line each — the helper must never throw:

| Input | Expected |
| --- | --- |
| `{ response: { data: { messages: [{ type: 2, value: 'Taken.' }] } } }` | `'Taken.'` |
| `{ response: { data: { messages: [{ type: 3, value: 'Boom' }, { type: 2, value: 'Taken.' }] } } }` | `'Taken.'` (2 wins over order) |
| `{ response: { data: { messages: [{ type: 0, value: 'FYI' }] } } }` | `'FYI'` (no type-2 entry → first usable) |
| `{ response: { data: { messages: [{ type: 2, value: '   ' }, { type: 1, value: 'Real' }] } } }` | `'Real'` (blank type-2 is not usable) |
| `{ response: { data: { messages: [] } } }` | `null` |
| `{ response: { data: { messages: null } } }` | `null` |
| `{ response: { data: '<html>502</html>' } }` | `null` |
| `{ response: undefined }` (offline / edge 429 with no CORS headers) | `null` |
| `undefined`, `null`, `'a string'`, `new Error('x')` | `null` |

- [ ] **Step 3: Lint**

```bash
cd /c/Users/kurok/Desktop/monkify/.worktrees/api-error-messages/monkify-front
yarn lint
```

Expected: PASS. An `Insert ␍` / `Delete ␍` failure means the file was written with LF — fix with
`unix2dos src/utils/apiError.ts` and confirm with `od -c src/utils/apiError.ts | head`.

- [ ] **Step 4: Commit**

```bash
git -C /c/Users/kurok/Desktop/monkify/.worktrees/api-error-messages/monkify-front add src/utils/apiError.ts
git -C /c/Users/kurok/Desktop/monkify/.worktrees/api-error-messages/monkify-front commit -m "Add apiErrorMessage, the single reader of the API error envelope (monkify-front)."
```

---

## Task 3: The interceptor

**Files:**
- Modify: `src/services/handlers.ts` (whole file — it is 25 lines)
- Test: none (no runner). Verified by `yarn lint` + `yarn build` and manual QA §13.

**Interfaces:**
- Consumes: `apiErrorMessage`, `API_ERROR_FALLBACK` from `@/utils/apiError` (Task 2).
- Produces: no exported surface change — `setupInterceptors()` and `AxiosNetworkError` keep their
  current signatures.

- [ ] **Step 1: Rewrite the response handler**

Replace the body of `handlers.ts` with:

```ts
import { AxiosError } from 'axios';
import { toast } from 'vue3-toastify';
import { API_ERROR_FALLBACK, apiErrorMessage } from '@/utils/apiError';
import { mainAPI } from './mainService';

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export interface AxiosNetworkError extends AxiosError<any, any> {
  handled: boolean;
}

export const setupInterceptors = () => {
  mainAPI.interceptors.response.use(
    (response) => response,
    (error: AxiosError & { handled?: boolean }) => {
      // this timeout allows for try catch to run first in the component, then set handled to true
      // TODO: improve this logic
      setTimeout(() => {
        if (!error.handled) {
          // The raw error — status line, config, stack — belongs in the console and NOWHERE else.
          // "Request failed with status code 400" is a developer's sentence, not a player's.
          console.error(error);

          // The backend wrote the player a real message; show it. `error.message` is deliberately no
          // longer a toast source — it is the axios status line, which is the whole bug.
          const message = apiErrorMessage(error) ?? API_ERROR_FALLBACK;

          // Identical failures collapse into one toast instead of stacking: a player retrying a
          // rejected word three times fast would otherwise wall off the mobile terminal. The message
          // IS the id — a counter or a timestamp would defeat it.
          toast.error(message, { toastId: message });
        }
      }, 200);
      return Promise.reject(error);
    }
  );
};
```

What must **not** change, and why:
- The 200 ms `setTimeout` handshake — it is what lets a call site's `catch` run first and set
  `handled`. The `// TODO: improve this logic` comment stays with it (spec §9).
- `if (!error.handled)` — the first and only gate.
- `console.error(error)` — always, with the raw error.
- The `Promise.reject(error)` return.
- No 401 branch is added (AC 8 / F5).

- [ ] **Step 2: Lint and type-check**

```bash
cd /c/Users/kurok/Desktop/monkify/.worktrees/api-error-messages/monkify-front
yarn lint
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git -C /c/Users/kurok/Desktop/monkify/.worktrees/api-error-messages/monkify-front add src/services/handlers.ts
git -C /c/Users/kurok/Desktop/monkify/.worktrees/api-error-messages/monkify-front commit -m "Toast the backend's own error message instead of the axios status line (monkify-front)."
```

---

## Task 4: Refactor the 5 duplicated extractions onto the helper

**Files:**
- Modify: `src/components/admin/BetRefundManager.vue` (lines 12-13 def, 24-31 catch)
- Modify: `src/components/admin/GameTypeManager.vue` (lines 47-48 def, ~57-63 and ~106-114 catches)
- Modify: `src/hooks/useOrbit.ts` (lines ~201-205, `serverMessage`)
- Modify: `src/components/header/DisplayNamePrompt.vue` (catch at ~52-59)
- Modify: `src/components/header/PlayerCustomizationModal.vue` (catch at ~106-113)

**Interfaces:**
- Consumes: `apiErrorMessage` from `@/utils/apiError` (Task 2).
- Produces: nothing new. `useOrbit`'s local `serverMessage` keeps its name and signature
  (`(error: unknown) => string | null`) so its ~12 call sites are untouched.

- [ ] **Step 1: `BetRefundManager.vue`**

Delete the local helper (lines 12-13):

```ts
  const firstValidationMessage = (messages?: { type: number; value: string }[]): string | null =>
    messages?.find((m) => m.type === 1 /* ValidationFailure */)?.value ?? null;
```

Add `apiErrorMessage` to the imports next to `adminService`:

```ts
  import { adminService } from '@/services';
  import { apiErrorMessage } from '@/utils/apiError';
```

Then simplify the catch — the `err` cast goes away with it:

```ts
    } catch (error: unknown) {
      // F3: this used to filter `type === 1` (Alert) and therefore never matched, always falling
      // through to the generic line below. `apiErrorMessage` prefers 2 (ValidationFailure), so the
      // real backend reason now renders.
      toast.error(apiErrorMessage(error) ?? 'Failed to mark the bet for refund.');
    } finally {
```

- [ ] **Step 2: `GameTypeManager.vue`**

Delete the same local helper at lines 47-48, add the same import, and replace **both** catches:

```ts
    // in save()
    } catch (error: unknown) {
      toast.error(apiErrorMessage(error) ?? 'Failed to save game type.');
    } finally {
```

```ts
    // in deactivate()
    } catch (error: unknown) {
      toast.error(apiErrorMessage(error) ?? 'Failed to deactivate game type.');
    }
```

Keep every fallback string byte-identical to today's. The `// 401 is handled by the interceptor
(→ /error); other errors surface a toast.` comment at line 28 stays.

- [ ] **Step 3: `useOrbit.ts` — delegate, keep the doc comment**

`serverMessage` keeps its name (12+ call sites) and its explanatory comment (it records *why* Orbit
refusals must survive). Only the body changes:

```ts
const serverMessage = (error: unknown): string | null => apiErrorMessage(error);
```

Add the import alongside the file's other `@/utils` imports.

> ⚠️ Behaviour note, intended: `serverMessage` used to read `messages[0].value` blindly. It now
> prefers a `type === 2` entry. Every Orbit refusal the backend emits is a single
> `ReturnValidationFailureMessage`, so entry 0 **is** the type-2 entry — identical output, as AC 11
> requires.

- [ ] **Step 4: `DisplayNamePrompt.vue` — helper + `handled` (also Task 5's row)**

```ts
    } catch (err: unknown) {
      // This surface renders the failure itself, in the shell's error strip. Mark it handled so the
      // shared interceptor — now that its toast says something real — does not repeat the same
      // sentence over the top of it (spec §7.4).
      (err as { handled?: boolean }).handled = true;
      errorMessage.value =
        apiErrorMessage(err) ??
        (err as Error)?.message ??
        'Could not save your name. Please try again.';
    } finally {
```

The `(err as Error)?.message` rung stays: this path also throws non-axios wallet errors
("This wallet cannot sign messages."), which carry no envelope.

- [ ] **Step 5: `PlayerCustomizationModal.vue` — helper + `handled` (also Task 5's row)**

```ts
    } catch (err: unknown) {
      // Same reason as DisplayNamePrompt: reported inline in the error strip, so the interceptor
      // must stay quiet rather than say it twice (spec §7.4).
      (err as { handled?: boolean }).handled = true;
      errorMessage.value =
        apiErrorMessage(err) ??
        (err as Error)?.message ??
        'Could not save your preferences. Please try again.';
    } finally {
```

- [ ] **Step 6: Prove no duplicated extraction survives (AC 11)**

```bash
cd /c/Users/kurok/Desktop/monkify/.worktrees/api-error-messages/monkify-front
grep -rn "messages?.\[0\]?.value\|messages?.find\|firstValidationMessage" src/
```

Expected: **no matches** outside `src/utils/apiError.ts`. (`GameChatPanel.vue` and `useGameChat.ts`
also contain the word `messages`, but those are chat messages — unrelated, leave them.)

- [ ] **Step 7: Lint**

```bash
yarn lint
```

Expected: PASS. Watch for now-unused imports (`toast` is still used in every file above; the
`{ type: number; value: string }[]` inline types disappear with the local helpers).

- [ ] **Step 8: Commit**

```bash
git -C /c/Users/kurok/Desktop/monkify/.worktrees/api-error-messages/monkify-front add \
  src/components/admin/BetRefundManager.vue \
  src/components/admin/GameTypeManager.vue \
  src/hooks/useOrbit.ts \
  src/components/header/DisplayNamePrompt.vue \
  src/components/header/PlayerCustomizationModal.vue
git -C /c/Users/kurok/Desktop/monkify/.worktrees/api-error-messages/monkify-front commit -m "Route every API-error extraction through the shared helper (monkify-front)."
```

---

## Task 5: The double-toast sweep

**Files:**
- Modify: `src/hooks/useGame.ts` (catch at ~378-382, the `getGameInstance` load)
- Modify: `src/hooks/useTokenBalance.ts` (catch at ~111-117, **both** branches)
- (`DisplayNamePrompt.vue` and `PlayerCustomizationModal.vue` were already marked in Task 4, Steps 4-5)

**Interfaces:**
- Consumes: nothing.
- Produces: nothing. These are one-line markers on existing catches.

- [ ] **Step 1: `useGame.ts` — the game-data load only**

```ts
    } catch (err) {
      // This catch already tells the player, with its own toast. Mark it handled so the interceptor
      // does not now say the same thing a second time (spec §7.4).
      (err as { handled?: boolean }).handled = true;
      console.error('Error fetching game data:', err);
      toast.error('Failed to load game data. Please try again later.');
    }
```

⚠️ **`submitBet`'s catch (~571-578) is NOT touched.** It writes only the terminal line
`There was an error processing your bet` and never toasts, so the interceptor's toast is the *only*
place the player learns the reason — marking it handled would silently re-break the whole card. Spec
§7.5 says so explicitly.

- [ ] **Step 2: `useTokenBalance.ts` — both branches**

```ts
  } catch (error) {
    // Never blank the number on a transient failure. Only surface a toast for a user-driven read;
    // background poll failures log quietly and retry on the next tick.
    //
    // Both branches mark handled: the loud one because it toasts here already, and the SILENT one
    // because it is deliberately silent — without this, a background poll blip would start toasting
    // the real backend message every tick (spec §7.4).
    (error as { handled?: boolean }).handled = true;
    if (opts.silent) console.warn('Token balance poll failed; keeping last value', error);
    else toast.error('Failed to fetch token balance');
  } finally {
```

- [ ] **Step 3: Confirm the sites that must stay UNMARKED**

Read, do not edit:

| File | Line | Why it stays unmarked |
| --- | --- | --- |
| `src/hooks/useGame.ts` | ~571-578 (`submitBet`) | The terminal line carries no reason; the interceptor toast is the only reason the player gets. This is the card. |
| `src/hooks/useBootRecovery.ts` | 53 | Absorbed retries are already marked; the **final** rethrow is deliberately left unmarked so a terminal failure still toasts — and now toasts something useful. |

And the sites already marked, which must keep behaving identically (AC 7): `GamePage.vue:80`,
`GameTypes.vue:47`, `useOrbit.ts` (`markHandled`, ~218), `useOrbitRedirect.ts:61,77`,
`OrbitTelegramTab.vue:78`.

- [ ] **Step 4: Lint**

```bash
cd /c/Users/kurok/Desktop/monkify/.worktrees/api-error-messages/monkify-front
yarn lint
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/kurok/Desktop/monkify/.worktrees/api-error-messages/monkify-front add \
  src/hooks/useGame.ts src/hooks/useTokenBalance.ts
git -C /c/Users/kurok/Desktop/monkify/.worktrees/api-error-messages/monkify-front commit -m "Mark self-reported failures handled so the toast does not duplicate them (monkify-front)."
```

---

## Task 6: The 23 call-site sweep + full verification

**Files:**
- Modify: none expected. Any change this audit turns up is a finding to report, not a silent fix.
- Test: `yarn lint`, `yarn build`, plus the backend suites already run in Task 1.

**Interfaces:**
- Consumes: everything above.
- Produces: the audit table that satisfies AC 12 and goes into the implementation report.

- [ ] **Step 1: Audit the 14 `mainService.ts` call sites**

For each, record: does a rejection reach the interceptor unmarked (→ real message toasted), or is it
intentionally silent (→ which call site marks `handled`, and why)?

`getGamesTypes` (`/games/active-types`, L83) · `getGameInstance` (L92) · `applyBet` (L99) ·
`getGameVerification` (L106) · `updatePlayerPreferences` (L113) · `getPlayerPreferences` (L123) ·
`getWalletNonce` (L139) · `setSelfExclusion` (L147) · `getGameTypeStats` (L161) · `getGlobalStats`
(L171) · `getPlayerStats` (L186) · `getRecentRounds` (L198) · `getGamesHistory` (L207) ·
`getTokenBalance` (L229).

- [ ] **Step 2: Audit the 9 `orbitService.ts` call sites**

`createOrbitSession` (L33) · `getOrbit` (L42) · `creditOrbitDeposit` (L64) · `requestOrbitCashOut`
(L84) · `getOrbitHistory` (L100) · `patchOrbitSettings` (L108) · `startTelegramLink` (L121) ·
`verifyTelegramLink` (L131) · `unlinkTelegram` (L140).

Every one of these is consumed through `useOrbit` / `useOrbitRedirect` / `OrbitTelegramTab`, which
mark `handled` and render `lastError` inline — expect "intentionally silent, reported inline".

- [ ] **Step 3: Note the two wrappers, do not "fix" them**

- `singleFlight` — concurrent identical requests share one rejection, so one toast. The Task 3 dedupe
  reinforces this rather than replacing it.
- `runWithRecovery` (`useBootRecovery`) — transient attempts are marked `handled`; only the final
  terminal error toasts, and it now toasts something useful.

- [ ] **Step 4: Grep-verify the interceptor is the only toast source for raw axios text**

```bash
cd /c/Users/kurok/Desktop/monkify/.worktrees/api-error-messages/monkify-front
grep -rn "error.message\|err.message" src/ --include=*.ts --include=*.vue | grep -i toast
```

Expected: no match. A raw axios `message` must never reach a toast again.

- [ ] **Step 5: Full frontend build**

```bash
yarn lint
yarn build
```

Expected: both PASS. `yarn build` is the type-check gate this repo has instead of a test runner.

- [ ] **Step 6: Write the audit table into the implementation report**

23 rows, each marked *surfaces a real backend message* or *intentionally silent — <reason>*. This is
AC 12's deliverable; it lives in the hand-off message and the Trello comment, not in a new file.

- [ ] **Step 7: Nothing to commit unless the audit found a real gap**

If it did, fix it in its own commit and say so in the report.

---

## Manual QA (before the user signs off — spec §13)

Local Docker stack. Per memory: use `docker start`, **never** `compose up`, so the 5433/6380 host
remap survives. The backend image does **not** self-rebuild after a code change — rebuild with
`docker compose up -d --build --no-deps api consumers` for the reworded strings to take effect.

1. **Fallback line (AC 5).** Stop the API container, load the lobby → toast reads
   `Something went wrong — please try again.`, never a status code. `console.error` still shows the
   raw axios error.
2. **Reserved word (AC 2 — the reliable repro; AC 1 is blocked by the pre-pay guard, F4).** Player A
   enables Orbit on a free-text type (Cosmic Sprint) and claims a word. As player B, bet that word in a
   round Orbit is **not** betting in → toast shows the same sentence a same-round collision produces,
   ending in *"Your bet should be refunded soon."*
3. **Free-bet variant (AC 9).** Repeat with the free-bets operator (`POST /dev/launch-token` +
   `#launch_token=`) → same message, **no** refund sentence.
4. **Dedupe (AC 6).** Pull the lever three times fast on the rejected word → one toast, refreshed.
5. **Terminal unchanged (AC 4).** `> There was an error processing your bet` still appears; lever buzz
   and error SFX still fire; the bet loading state clears.
6. **No double report (AC 15).** Open the display-name prompt, submit a name the backend rejects → the
   inline error strip appears and **no** toast.
7. **Silent poll (AC 15).** Leave the game open with the API stopped → the background token-balance
   poll stays silent (console only) and the number does not blank.
8. **Admin (AC 11).** In the admin portal, submit an invalid game type → the real backend validation
   message renders inline instead of *"Failed to save game type."*

---

## Self-review — spec coverage

| Spec section | Covered by |
| --- | --- |
| §6.2 copy pass (8 keys) | Task 1, Steps 2 & 4 |
| §6.2 "explicitly NOT changed" | Task 1, Step 2 (⚠️ list) + Step 7 guard |
| §6.3 designer regeneration | Task 1, Step 4 |
| §7.1 `apiErrorMessage` contract (4 rules) | Task 2, Steps 1-2 |
| §7.2 interceptor (fallback, dedupe, `handled` gate, 200 ms, `console.error`) | Task 3, Step 1 |
| §7.3 five refactors | Task 4, Steps 1-5 |
| §7.4 double-toast sweep | Task 4 Steps 4-5 + Task 5 Steps 1-2 |
| §7.5 "unchanged" list | Task 5, Step 3 |
| §7.6 23-call-site sweep | Task 6, Steps 1-3, 6 |
| §11 tests (zero edits, green) | Task 1, Steps 1, 7, 8 |
| §12 AC 1-15 | AC 1-2 QA 2; AC 3 Task 1+3; AC 4 Task 5 Step 3; AC 5 Task 2 Step 2 + QA 1; AC 6 Task 3 + QA 4; AC 7 Task 5 Step 3; AC 8 Task 3 Step 1 (no 401 branch added); AC 9 QA 3; AC 10 Task 3 (single funnel); AC 11 Task 4 Step 6 + QA 8; AC 12 Task 6; AC 13 Task 1 Step 2; AC 14 Task 1 Steps 7-8 + Task 6 Step 5; AC 15 Task 5 + QA 6-7 |
| §13 manual QA | Manual QA section above |

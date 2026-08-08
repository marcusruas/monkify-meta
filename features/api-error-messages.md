# Feature — Real API error messages

> Status: **shipped.** Authoritative spec: `../specs/api-error-messages.md`
> (merged via `feature/api-error-messages` on 2026-08-08).

## What it does

When the API rejects a request, the player now reads the sentence the backend actually wrote —
*"This sequence has already been taken by another player. Pick a different one. Your bet should be
refunded soon."* — instead of `Request failed with status code 400`. The fix lives in the shared
`mainAPI` response interceptor, so **all 23 `mainAPI` call sites** (14 `mainService` + 9
`orbitService`) were corrected at once and a newly added endpoint cannot start out mute. The loudest
symptom was a bet on a word someone else's Orbit had cross-round reserved: the player paid on-chain,
got a status code, learned nothing about the refund, and retried the same word.

## Core rules

- **The envelope is the source of the message.** Every rejection carries
  `{ data, messages: [{ type, value }] }`. `utils/apiError.ts` → `apiErrorMessage(err)` is the **only**
  place in `src/` that reads it.
- **`type` is numeric.** `MessageType` is `Informational 0 / Alert 1 / ValidationFailure 2 / Error 3`
  and there is **no `JsonStringEnumConverter`** on the API, so it serializes as a number.
  `apiErrorMessage` prefers the first `type === 2` entry with a non-empty trimmed `value`, then the
  first usable entry of any type, then `null`.
  ⚠️ Two admin screens used to filter `type === 1`, commented `/* ValidationFailure */` — that is
  **Alert**. They never matched and always fell through to a generic fallback. Anything new that
  filters this field must use `2`.
- **One friendly fallback.** No usable message (500, timeout, offline, HTML error page, empty or
  malformed `messages`) → `Something went wrong — please try again.`
- **Status codes never reach the player.** `error.message` on an AxiosError is the axios status line;
  it goes to `console.error` and nowhere else. A surface that renders a failure itself uses
  `displayableErrorMessage(err, fallback)`, which keeps a genuinely thrown sentence (a wallet that
  cannot sign) but never lets an HTTP status line through — the presence of `response` is what tells
  the two apart.
- **Identical toasts dedupe.** The message text is the `toastId`, so a fast retry refreshes one toast
  instead of stacking three over the mobile terminal. A counter or timestamp would defeat this.
- **`handled === true` still wins.** The interceptor's gate is unchanged, and so is the 200 ms
  `setTimeout` that lets a call site's `catch` run first. A site that reports the failure **itself**
  must set `err.handled = true`, or the player reads the same sentence twice.
- **The terminal is unchanged.** A rejected bet still writes the generic
  `> There was an error processing your bet`, still fires the lever buzz and error SFX, still clears
  the loading state. `submitBet`'s catch is deliberately **not** marked `handled` — the interceptor
  toast is the only reason the player ever gets. `GameTerminal.vue` is untouched.
- **All four bet paths inherit it** — free, desktop SolanaToken, embedded wallet-bridge popup and
  mobile deeplink resume all funnel through `submitBet`.
- **Free bets are never promised a refund.** `RegisterBetHandler.RefundInvalidBet` appends
  `RefundWarning` only on the **paid** branch, so showing backend text verbatim gets this right with no
  client-side conditional.
- **The reserved-word reject stays byte-identical to a same-round collision** — same message, same
  refund path, same timing. A distinct "reserved" wording is **forbidden**: it would announce that
  somebody on this game type runs Orbit (#150). There is no pre-pay claim check and no endpoint listing
  reserved words. Accepted cost: retrying the same word pays and refunds again.
- **Vague messaging survives (`constraints.md` §6).** `GameNotValidForBets` (unknown **or** inactive
  must stay indistinguishable), `ChoiceAlreadyTaken`, `RefundWarning` and every auth/wallet-proof
  message were **not** reworded.
- **`ErrorMessages.resx` values are player-facing verbatim.** The eight live payment-signature strings
  were reworded off internal vocabulary ("signature **have** not sent", "informed in the request",
  "check the data" — the data is on-chain). `AdviceOnDifferentSignatureBetAmount` keeps its `{0}`
  placeholder, which `SolanaService` formats `SupportEmail` into.
- **`adminAPI` is untouched** — its own interceptor still routes `401 → /error` and adds no toast; its
  screens render inline and only gained the shared helper.

## Key code touchpoints

**`monkify-front`**
- `src/utils/apiError.ts` — `apiErrorMessage()`, `displayableErrorMessage()`, `API_ERROR_FALLBACK`.
  Pure and dependency-free (no Vue, no axios import), like `utils/transientError.ts`.
- `src/services/handlers.ts` — the `mainAPI` response interceptor.
- `src/hooks/useOrbit.ts` — local `serverMessage` delegates to the helper; its own `markHandled` is
  unchanged.
- Marks `handled`: `src/hooks/useGame.ts` (game-data load **only**), `src/hooks/useTokenBalance.ts`
  (**both** catch branches — the silent poll branch too, or a background blip starts toasting),
  `src/components/header/DisplayNamePrompt.vue`, `src/components/header/PlayerCustomizationModal.vue`,
  `src/pages/ResponsibleGamingPage.vue`.
- Consume the helper: `src/components/admin/BetRefundManager.vue`,
  `src/components/admin/GameTypeManager.vue`, `src/pages/GamePage.vue`,
  `src/components/game-selection/GameTypes.vue`.

**`monkify-backend`**
- `src/SpaceStroker.Common/Resources/ErrorMessages.resx` (+ `ErrorMessages.Designer.cs` — keep in sync;
  the doc comments carry the value).
- `src/SpaceStroker.Common/Messaging/Models/MessageType.cs` — the numeric enum the front depends on.
- `src/SpaceStroker.Api/Filters/ExceptionFilter.cs` — builds the envelope.
- `src/SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs` —
  `RefundInvalidBet` concatenates `RefundWarning` on the paid branch only.
- `src/SpaceStroker.Infrastructure/Services/Solana/SolanaService.cs` —
  `GenerateErrorMessageForTokenExchange` joins the amount message and the refund advice with an
  explicit `" "`. That separator **used to be a trailing space inside the `.resx` value**, which is
  invisible in an editor; do not reintroduce that pattern.

## Known gaps (not fixed here)

- **8 sites in `useOrbit.ts`** (lines ~646, 730, 781, 997, 1023, 1042, 1075, 1102) still read
  `serverMessage(error) ?? (error instanceof Error ? error.message : '…')`, so a 500 with no envelope
  leaks the axios status line into Orbit's inline `lastError`. One-line fix each via
  `displayableErrorMessage`.
- **Dead resource families** — `Wallet*`, `Rg*` and `SignatureForInvalidToken` have **0 references**
  (operator-era leftovers, several still saying "contact your operator"). Unreachable, so they mislead
  nobody today.
- **`ParameterNotFound` vs `ParameterHasNoActiveGames`** distinguish two states of a game type, which
  sits in tension with `constraints.md` §6. They are on the game-lookup path, not the bet-reject path,
  and predate this feature.
- **`token-expired` / `setRelaunching`** have **zero emitters** — `'token-expired'` is only an
  `EmbedEventType` union member and `mainAPI` has no 401 branch at all. This feature deliberately did
  not add one.

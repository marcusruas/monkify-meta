# Orbit Page Restructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild `/orbit` as a four-step collapse-to-dashboard list on a new violet/black panel that owns every Orbit action inline, shrink the mid-game modal to two blocks, and make deposits free-form whole numbers (which requires the first backend change in the Orbit UI line).

**Architecture:** Three new presentational components (`OrbitStatusBanner`, `OrbitStepRow`, `OrbitAmountField`) are composed by a rebuilt `OrbitPage.vue` and a reduced `OrbitModal.vue`. All Orbit state stays in the existing `useOrbit()` singleton — the new components are dumb and take props. The backend change is a single validator swap in `CreditOrbitDepositHandler` plus two new DTO fields, guarded front-first so no refused amount ever starts an on-chain transfer.

**Tech Stack:** Vue 3.2 Composition API + TypeScript 5.5, Vue CLI 5 (webpack), Tailwind 3 + DaisyUI, `arcade.css` design system. Backend: .NET 8, EF Core, MediatR, xUnit + Testcontainers.

## Global Constraints

Copied verbatim from `specs/orbit-page-restructure.md` §2. **Every task's requirements implicitly include this section.**

- **Worktree-per-repo.** Work lives in `.worktrees/orbit-ui-overhaul/{monkify-front,monkify-meta,monkify-backend}`. All three shared checkouts stay on `main`. Branch `feature/orbit-ui-overhaul` in every repo, pushed to `origin` after every task that commits.
- **Every overlay goes through `ArcadeModal.vue`.** Teleport to `#app`, named `z-*` scale, no consumer `max-width`/`max-height`, no hand-rolled backdrop, no per-component Esc listener (`useOverlayStack` owns Esc).
- **Every control takes its height from `--ctl-h-sm|md|lg`.** No control derives its height from padding. Below 1023px the tokens step themselves up — **no call-site class changes on mobile, ever**.
- **Do not stack Tailwind utilities on an arcade class.** `arcade.css` is imported after `@tailwind utilities` at equal specificity and wins. Use the class as-is, use plain Tailwind for a differently-shaped control, or add a modifier in `arcade.css`. Where an override is genuinely required, `!` is load-bearing and must be commented.
- **Use the app's control families.** `.btn-dialog` / `.btn-dialog--primary` is the **flat control family** — use it wherever a control lines up with an input, dialog or page. `.btn-arcade` is **cabinet furniture** (lever, wallet cluster, dashboard, cards) and must **not** be used on `/orbit`. Inputs are `.input-arcade`. Body copy is `.dialog-copy`. Labels are `.dialog-eyebrow`. Stat grids are `.dialog-cells`.
- **Dither, never blur.** No `backdrop-filter`, no `blur()`, no infinite animation, no wide glow — `usePerfMode` forces `html.light-graphics` on every viewport below `lg`.
- **Colour is semantic (spec §4.1).** Green `--color-live` = running/satisfied. Violet `--color-orbit` / `--color-orbit-deep` = identity + active step. Red `oklch(0.8 0.16 25)` = warning/error. Amber `--color-primary` = **the active step's primary action button only**. Title `ORBIT` is `--color-ink`. **Every value is plain ink** — the only coloured value is `no floor` (red).
- **A healthy, fully-configured running Orbit shows no amber and no red anywhere.**
- **`constraints.md` §6 is preserved.** Forged session / unknown nonce / missing session stay indistinguishable. The deposit-bound message is a sanctioned exception (published bound, leaks nothing).
- **Secrets/config: mock/placeholder values only.**
- **Backend tests run via `run_tests.bat`**, never bare `dotnet test` (the script excludes the `ProductionGames` Monte Carlo suite by design).
- **`monkify-front` has no test framework.** Frontend verification is `yarn lint` + `yarn build` clean plus the manual QA in spec §13. Do not invent a test runner.
- ⚠️ **`yarn lint` requires uniform CRLF.** Never use `sed -i` on a front file (it rewrites to LF and prettier fails with `Insert ␍`). Use the Edit tool; if endings are mangled, fix with `unix2dos`.

---

## File Structure

**`monkify-backend`**
- `src/SpaceStroker.Domain/Configs/Entities/OrbitSettings.cs` — cap `10m` → `100m`
- `src/SpaceStroker.Infrastructure/Handlers/Orbit/CreditOrbitDeposit/CreditOrbitDepositHandler.cs` — ladder check → range check
- `src/SpaceStroker.Infrastructure/Handlers/Orbit/GetOrbit/GetOrbitRequest.cs` — `MinimumDeposit`, `MaxDepositPerTransaction`
- `src/SpaceStroker.Infrastructure/Handlers/Orbit/GetOrbit/GetOrbitHandler.cs` — populate them
- `src/SpaceStroker.Tests/UnitTests/Api/OrbitSettingsBindingTests.cs` — cap assertion
- `src/SpaceStroker.Tests/UnitTests/Handlers/Orbit/…` — denomination → range tests
- `src/SpaceStroker.Tests.Integration/OrbitFlowTests.cs` — happy-path integration test

**`monkify-front`**
- `src/assets/styles/arcade.css` — `.arcade-frame--orbit` + `.orbit-step` modifiers
- `src/types/orbit.ts` — `minimumDeposit`, `maxDepositPerTransaction`
- `src/hooks/useOrbit.ts` — deposit bound guard, `roundsLeft`, `orbitStatus`
- `src/components/orbit/OrbitAmountField.vue` — **new**, integer input + confirm (2 mounts)
- `src/components/orbit/OrbitStatusBanner.vue` — **new**, the one-row auto-play strip
- `src/components/orbit/OrbitStepRow.vue` — **new**, collapse/expand row
- `src/pages/OrbitPage.vue` — rebuilt
- `src/components/orbit/OrbitModal.vue` — reduced to two blocks

**`monkify-meta`**
- `conventions/frontend.md` — "Two panels" → "Three panels", palette rule
- `features/orbit-custodial-wallet.md` — deposits are no longer a ladder
- `constraints.md` — sanctioned §6 exception for the deposit bound message

---

## Task 1: Checkpoint the uncommitted overhaul + create the backend worktree

The front worktree holds ~1,900 lines of uncommitted `orbit-ui-overhaul.md` work. Commit it **before** touching anything, so the restructure is a reviewable diff rather than an indistinguishable blob. `monkify-backend` has no branch or worktree for this card and needs both.

**Files:**
- Commit: all modified + untracked files in `.worktrees/orbit-ui-overhaul/monkify-front`
- Create: `.worktrees/orbit-ui-overhaul/monkify-backend`

**Interfaces:**
- Consumes: nothing
- Produces: a clean front worktree on `feature/orbit-ui-overhaul`; a backend worktree on the same branch name

- [ ] **Step 1: Verify the front worktree state**

```bash
cd .worktrees/orbit-ui-overhaul/monkify-front
git status --short
```

Expected: 19 modified, 5 untracked (`OrbitConsoleModule.vue`, `OrbitGlyph.vue`, `OrbitMark.vue`, `OrbitRedirectToast.vue`, `OrbitPage.vue`).

- [ ] **Step 2: Lint and build the checkpoint before committing it**

```bash
yarn lint && yarn build
```

Expected: both clean. If lint fails with `Insert ␍`, run `unix2dos` on the offending files — do not reformat.

- [ ] **Step 3: Commit the checkpoint**

```bash
git add -A
git commit -m "Implement the Orbit UI/UX overhaul (checkpoint before the page restructure).

Implements specs/orbit-ui-overhaul.md: the compact modal on ArcadeModal, the
/orbit page, the console module, the identity layer (mark + four glyphs), the
first-win reskin, mobile deeplink sign-in and the cancellable redirect.

Committed as-is so the page restructure that follows is a reviewable diff.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
git push origin feature/orbit-ui-overhaul
```

- [ ] **Step 4: Collision pre-flight for the backend worktree**

```bash
cd ../../../monkify-backend
git fetch origin
git worktree list
git branch --list feature/orbit-ui-overhaul
git ls-remote --heads origin feature/orbit-ui-overhaul
git status --short --branch
```

Expected: no existing `feature/orbit-ui-overhaul` branch or worktree; hub clean on `main`. **Stop and report if any of those are false.**

- [ ] **Step 5: Create the backend worktree and push the branch**

```bash
git worktree add -b feature/orbit-ui-overhaul \
    ../.worktrees/orbit-ui-overhaul/monkify-backend origin/main
cd ../.worktrees/orbit-ui-overhaul/monkify-backend
git push -u origin HEAD
```

- [ ] **Step 6: Verify the backend suite is green before changing it**

```bash
scripts/run_tests.bat
```

Expected: PASS. A pre-existing failure must be reported now, not discovered later.

---

## Task 2: Backend — whole-number deposit range

**Files:**
- Modify: `src/SpaceStroker.Domain/Configs/Entities/OrbitSettings.cs:47`
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Orbit/CreditOrbitDeposit/CreditOrbitDepositHandler.cs:60`
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Orbit/GetOrbit/GetOrbitRequest.cs`
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Orbit/GetOrbit/GetOrbitHandler.cs:73`
- Test: `src/SpaceStroker.Tests/UnitTests/Api/OrbitSettingsBindingTests.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/Orbit/` (the deposit handler tests)

**Interfaces:**
- Consumes: nothing
- Produces: `GetOrbitRequest.MinimumDeposit` (`decimal`, always `1m`) and `GetOrbitRequest.MaxDepositPerTransaction` (`decimal`) on the `/orbit` GET payload; a credit handler that accepts any whole `amount` in `[1, MaxDepositPerTransaction]`

- [ ] **Step 1: Write the failing unit tests**

In the existing deposit-handler test file, replace the denomination tests with:

```csharp
[Theory]
[InlineData(1)]
[InlineData(7)]   // was impossible under the [1,5,10] ladder
[InlineData(100)]
public async Task CreditDeposit_AcceptsAnyWholeAmountWithinTheCap(decimal amount)
{
    var result = await CreditDeposit(amount);
    result.ShouldNotBeNull();
}

[Theory]
[InlineData(0)]
[InlineData(-3)]
[InlineData(7.5)]
[InlineData(101)]
public async Task CreditDeposit_RefusesNonWholeOrOutOfRangeAmounts(decimal amount)
{
    var result = await CreditDeposit(amount);
    result.ShouldBeNull();
}
```

And in `OrbitSettingsBindingTests.cs`:

```csharp
[Fact]
public void MaxDepositPerTransaction_DefaultsTo100()
    => new OrbitSettings().MaxDepositPerTransaction.ShouldBe(100m);
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
scripts/run_tests.bat
```

Expected: FAIL — `7`, `100` rejected by the ladder; `MaxDepositPerTransaction` is `10m`.

- [ ] **Step 3: Raise the cap**

`OrbitSettings.cs:47`:

```csharp
        /// <summary>
        /// Raised from 10 to 100 (== MaxDepositPerDay) when deposits became free-form whole numbers:
        /// the [1,5,10] ladder was product policy, not a safety property, and with it gone a cap of 10
        /// would have made "any whole amount" a lie. The DAILY cap is the real bound; this one still
        /// bounds a single mistyped transfer.
        /// </summary>
        public decimal MaxDepositPerTransaction { get; set; } = 100m;
```

- [ ] **Step 4: Swap the validator**

`CreditOrbitDepositHandler.cs:60` — replace the denomination check with:

```csharp
            // 1. Whole-number range + per-transaction cap. Cheapest checks first, so a malformed
            //    request never costs an RPC call.
            //    ⚠️ The [1,5,10] ladder was removed deliberately (specs/orbit-page-restructure.md §7):
            //    the client transfers BEFORE this handler credits, so every denomination refusal cost
            //    a player real USDC that only Recover-a-deposit could rescue — and Recover used the
            //    same ladder. The client now validates this exact bound before it signs anything.
            //    AvailableDepositOptions is NOT deleted: its binder-append hazard is documented on the
            //    property and the DTO still carries it for compatibility.
            if (amount < 1m || amount != decimal.Truncate(amount) || amount > orbit.MaxDepositPerTransaction)
                return Reject($"{amount} is not a whole number within the per-transaction cap");
```

- [ ] **Step 5: Expose the bounds on the DTO**

`GetOrbitRequest.cs` — add beside `DepositOptions`:

```csharp
        /// <summary>The smallest whole deposit the credit handler accepts. Always 1.</summary>
        public decimal MinimumDeposit { get; set; }

        /// <summary>The largest single deposit the credit handler accepts. The client validates
        /// against this BEFORE signing a transfer, so it must be the same value the handler uses.</summary>
        public decimal MaxDepositPerTransaction { get; set; }
```

`GetOrbitHandler.cs:73` — populate them beside `DepositOptions`:

```csharp
                DepositOptions = orbit.AvailableDepositOptions,
                MinimumDeposit = 1m,
                MaxDepositPerTransaction = orbit.MaxDepositPerTransaction,
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
scripts/run_tests.bat
```

Expected: PASS. Report every test file that changed.

- [ ] **Step 7: Commit and push**

```bash
git add -A
git commit -m "Accept any whole deposit amount within the per-transaction cap.

The [1,5,10] ladder was product policy, not a safety property, and the client
transfers BEFORE the credit handler runs — so every denomination refusal cost a
player real USDC that only Recover-a-deposit could rescue, and Recover used the
same ladder.

Raises MaxDepositPerTransaction 10 -> 100 (== MaxDepositPerDay) and exposes both
bounds on the /orbit payload so the client can refuse a bad amount before it
signs anything. The daily cap, pool float cap, cool-off and idempotency checks
are untouched.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
git push origin feature/orbit-ui-overhaul
```

---

## Task 3: Backend — integration test (mandatory, `AGENTS.md` rule 8)

**Files:**
- Modify: `src/SpaceStroker.Tests.Integration/OrbitFlowTests.cs`

**Interfaces:**
- Consumes: the range validator from Task 2
- Produces: nothing consumed downstream

- [ ] **Step 1: Write the failing integration test**

Follow the file's existing fixture and helper style — read the neighbouring tests first and reuse their setup helpers rather than inventing new ones.

```csharp
[Fact]
public async Task Deposit_CreditsAnyWholeAmount_AndStaysIdempotent()
{
    var wallet = await EnrolAndEnableOrbit();

    // 7 was impossible under the old [1,5,10] ladder — this is the behaviour the card adds.
    var signature = NewSignature();
    var first = await CreditDeposit(wallet, signature, amount: 7m);

    first.ShouldNotBeNull();
    first.Balance.ShouldBe(7m);

    // Replaying the SAME signature must not credit twice.
    var replay = await CreditDeposit(wallet, signature, amount: 7m);

    replay.ShouldNotBeNull();
    replay.Balance.ShouldBe(7m);

    // The daily cap still bounds the total, even though a single transaction may now reach it.
    var overCap = await CreditDeposit(wallet, NewSignature(), amount: 100m);

    overCap.ShouldBeNull();
}
```

- [ ] **Step 2: Run it to verify it fails**

Ensure Docker is running (Testcontainers), then:

```bash
scripts/run_tests.bat
```

Expected: FAIL before Task 2's change is present; PASS after. If it passes immediately, verify the test is actually being collected.

- [ ] **Step 3: Confirm it passes and report**

```bash
scripts/run_tests.bat
```

Expected: PASS.

- [ ] **Step 4: Commit and push**

```bash
git add -A
git commit -m "Cover the free-form deposit happy path end to end.

Asserts a non-ladder whole amount (7) credits to exactly 7, that replaying its
signature is idempotent, and that the daily cap still refuses the deposit that
would cross it.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
git push origin feature/orbit-ui-overhaul
```

---

## Task 4: Front — the `.arcade-frame--orbit` material

**Files:**
- Modify: `src/assets/styles/arcade.css` (beside `.arcade-frame--black`, ~line 102)
- Modify (meta): `conventions/frontend.md`

**Interfaces:**
- Consumes: nothing
- Produces: CSS classes `.arcade-frame--orbit`, `.orbit-step`, `.orbit-step--active`, `.orbit-step--warn`, `.orbit-step--locked`

- [ ] **Step 1: Add the material and the step modifiers**

Append after `.arcade-frame--black`:

```css
  /* The ORBIT PANEL as a page surface — void-deep behind a 2px violet-deep frame.
     SCOPE IS DELIBERATE: the /orbit page ONLY. It exists because the money page needed the depth of
     the black panel WITHOUT breaking that panel's rule (black is brief-and-informative and light in
     tone, and NEVER a warning about money — /orbit carries a stop-loss). Rather than bend --black,
     Orbit got its own material in its own identity colour.
     The frame is --color-orbit-DEEP, not --color-orbit: the OPEN STEP inside takes the full-intensity
     violet, and a full-intensity frame would flatten it (specs/orbit-page-restructure.md §4.3).
     Cyan was rejected: it is the focus ring and Beast Mode's archetype accent, and Orbit already owns
     a colour. NO GLOW — every phone runs html.light-graphics, which strips one. */
  .arcade-frame--orbit {
    background-color: var(--color-void-deep);
    border-color: var(--color-orbit-deep);
  }

  /* A step row on /orbit. The base is neutral on purpose: colour marks ATTENTION and IDENTITY, never
     completion, so a SATISFIED step is the quiet default and only its ✓ pip is green. */
  .orbit-step {
    border: 1px solid var(--color-line);
    border-left: 3px solid var(--color-line);
    border-radius: var(--radius-arcade);
    background-color: var(--color-surface-inset);
    overflow: hidden;
  }
  /* "You are here" — full-intensity violet against the frame's deep violet. */
  .orbit-step--active {
    border-color: var(--color-orbit);
    border-left-color: var(--color-orbit);
  }
  /* Money exposure only — an unset floor, a paused Orbit. */
  .orbit-step--warn {
    border-left-color: oklch(0.8 0.16 25);
  }
  .orbit-step--locked {
    opacity: 0.55;
  }
```

- [ ] **Step 2: Verify it compiles and the black panel is unchanged**

```bash
yarn build
```

Expected: clean. Then load `/how-to-play` in the dev server and confirm its tip block (the single `.arcade-frame--black` consumer) renders identically.

- [ ] **Step 3: Update the convention doc**

In `monkify-meta/conventions/frontend.md`, rename the **"Two panels"** section to **"Three panels"** and add the Orbit row: `--orbit` = void-deep + violet-deep frame, scope = the `/orbit` page only, and the reason it is not `--black` (black is never a warning about money). Add the semantic-palette rule from spec §4.1 in the same section.

- [ ] **Step 4: Commit and push (both repos)**

```bash
# monkify-front
git add src/assets/styles/arcade.css
git commit -m "Add the Orbit page material and its step-row modifiers.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
git push origin feature/orbit-ui-overhaul

# monkify-meta
git add conventions/frontend.md
git commit -m "Document the third page panel and Orbit's semantic palette.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
git push origin feature/orbit-ui-overhaul
```

---

## Task 5: Front — state layer (`types/orbit.ts`, `hooks/useOrbit.ts`)

**Files:**
- Modify: `src/types/orbit.ts:25`
- Modify: `src/hooks/useOrbit.ts` (the `deposit` function ~line 425, and the derivations block ~line 408)

**Interfaces:**
- Consumes: `MinimumDeposit` / `MaxDepositPerTransaction` from Task 2's DTO
- Produces, all returned from `useOrbit()`:
  - `depositBounds: ComputedRef<{ min: number; max: number }>`
  - `roundsLeft: ComputedRef<number | null>`
  - `orbitStatus: ComputedRef<'signed-out' | 'off' | 'cooling-off' | 'paused' | 'running'>`
  - `deposit(wallet: string, amount: number)` — unchanged signature, new guard

- [ ] **Step 1: Add the DTO fields to the type**

`src/types/orbit.ts`, beside `depositOptions`:

```ts
  depositOptions: number[];
  minimumDeposit: number;
  maxDepositPerTransaction: number;
```

- [ ] **Step 2: Add the derivations**

In `useOrbit()`, beside the existing `isPausedForFunds` / `activeGameType` derivations:

```ts
  const depositBounds = computed(() => ({
    min: orbit.value?.minimumDeposit ?? 1,
    max: orbit.value?.maxDepositPerTransaction ?? 100,
  }));

  /**
   * How many more rounds the balance can cover. Null when there is no game to divide by — the banner
   * renders "— rounds left" rather than a fake zero, because zero means "paused" and an unknown stake
   * does not.
   */
  const roundsLeft = computed(() => {
    const stake = activeGameType.value?.requiredAmount ?? 0;
    if (stake <= 0) return null;
    return Math.floor((orbit.value?.balance ?? 0) / stake);
  });

  /**
   * ONE resolved state for the page header, the banner and the modal tag, so three surfaces can never
   * disagree. Order matters: the cool-off outranks "off" because it refuses actions "off" allows.
   */
  const orbitStatus = computed(() => {
    if (!hasSession.value) return 'signed-out' as const;
    if (enableLockedUntil.value) return 'cooling-off' as const;
    if (!orbit.value?.enabled) return 'off' as const;
    if (isPausedForFunds.value) return 'paused' as const;
    return 'running' as const;
  });
```

- [ ] **Step 3: Add the deposit guard**

In `deposit()`, immediately after the `isSelfDeposit` guard and **before** `isBusy.value = true` (so it throws before `sendToken` is ever reached):

```ts
    // The transfer happens BEFORE the credit call, so an amount the backend will refuse must never
    // reach sendToken — that is money out of the wallet with nothing credited. This mirrors
    // CreditOrbitDepositHandler's range check exactly.
    // Specific on purpose: it states a PUBLISHED bound and reveals nothing about a session, a nonce
    // or another player, so constraints.md §6 is untouched.
    const { min, max } = depositBounds.value;
    if (!Number.isInteger(amount) || amount < min || amount > max) {
      lastError.value = `Deposits are whole numbers from ${min} to ${max}.`;
      throw new Error(lastError.value);
    }
```

- [ ] **Step 4: Export the new surface**

Add `depositBounds`, `roundsLeft`, `orbitStatus` to the object returned at the end of `useOrbit()`.

- [ ] **Step 5: Verify**

```bash
yarn lint && yarn build
```

Expected: both clean.

- [ ] **Step 6: Commit and push**

```bash
git add src/types/orbit.ts src/hooks/useOrbit.ts
git commit -m "Guard deposits against the backend's range before any transfer starts.

Adds depositBounds, roundsLeft and a single resolved orbitStatus so the page,
the banner and the modal tag can never disagree.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
git push origin feature/orbit-ui-overhaul
```

---

## Task 6: Front — `OrbitAmountField.vue`

The one deposit/recover control, mounted on the page and in the modal.

**Files:**
- Create: `src/components/orbit/OrbitAmountField.vue`

**Interfaces:**
- Consumes: `depositBounds` from Task 5
- Produces:
  ```ts
  defineProps<{ min: number; max: number; disabled?: boolean; busy?: boolean; confirmLabel?: string }>()
  defineEmits<{ (e: 'confirm', amount: number): void }>()
  ```

- [ ] **Step 1: Write the component**

```vue
<script setup lang="ts">
  import { computed, ref } from 'vue';

  /**
   * The ONE amount control — deposit on /orbit, deposit in the modal, and recover-a-deposit.
   * Replaces the [1,5,10] <select>: the ladder is gone from the backend (specs/orbit-page-restructure.md
   * §7), and a select cannot express the amounts that are now legal.
   *
   * The confirm button is disabled until the amount is valid, so useOrbit's guard is a backstop rather
   * than the primary UX — but the guard is what actually protects the money, because the transfer
   * leaves the wallet before the backend ever sees the amount.
   */
  const props = withDefaults(
    defineProps<{
      min: number;
      max: number;
      disabled?: boolean;
      busy?: boolean;
      confirmLabel?: string;
    }>(),
    { disabled: false, busy: false, confirmLabel: 'Confirm' }
  );

  const emit = defineEmits<{ (event: 'confirm', amount: number): void }>();

  const raw = ref('');

  const parsed = computed(() => {
    const value = Number(raw.value);
    return raw.value.trim() === '' || Number.isNaN(value) ? null : value;
  });

  const isValid = computed(
    () =>
      parsed.value !== null &&
      Number.isInteger(parsed.value) &&
      parsed.value >= props.min &&
      parsed.value <= props.max
  );

  // Only complain once they have typed something — an empty field is not an error, it is a start.
  const hint = computed(() => {
    if (parsed.value === null || isValid.value) return null;
    if (!Number.isInteger(parsed.value)) return 'Whole numbers only.';
    return `Between ${props.min} and ${props.max}.`;
  });

  const onConfirm = () => {
    if (!isValid.value || props.disabled || props.busy) return;
    emit('confirm', parsed.value as number);
    raw.value = '';
  };
</script>

<template>
  <div class="flex flex-col gap-2">
    <div class="flex gap-2">
      <input
        v-model="raw"
        type="number"
        inputmode="numeric"
        step="1"
        :min="min"
        :max="max"
        :placeholder="`${min}–${max}`"
        :disabled="disabled || busy"
        class="input-arcade tabular-nums"
        @keyup.enter="onConfirm"
      />
      <button
        type="button"
        class="btn-dialog btn-dialog--primary flex-none"
        :disabled="!isValid || disabled || busy"
        @click="onConfirm"
      >
        {{ busy ? 'Working…' : confirmLabel }}
      </button>
    </div>
    <p v-if="hint" class="dialog-copy !text-[17px] !text-[color:oklch(0.8_0.16_25)]">
      {{ hint }}
    </p>
  </div>
</template>
```

- [ ] **Step 2: Verify**

```bash
yarn lint && yarn build
```

Expected: both clean.

- [ ] **Step 3: Commit and push**

```bash
git add src/components/orbit/OrbitAmountField.vue
git commit -m "Add the shared Orbit amount field.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
git push origin feature/orbit-ui-overhaul
```

---

## Task 7: Front — `OrbitStatusBanner.vue`

**Files:**
- Create: `src/components/orbit/OrbitStatusBanner.vue`

**Interfaces:**
- Consumes: `roundsLeft`, `orbitStatus` from Task 5
- Produces:
  ```ts
  defineProps<{
    walletBalance: number | null;   // null hides the wallet segment (the modal)
    orbitBalance: number;
    stake: number | null;
    tokenLabel: string;
    roundsLeft: number | null;
    status: 'signed-out' | 'off' | 'cooling-off' | 'paused' | 'running';
    coolOffLabel?: string | null;
  }>()
  ```

- [ ] **Step 1: Write the component**

```vue
<script setup lang="ts">
  import { computed } from 'vue';
  import OrbitMark from '@/components/orbit/OrbitMark.vue';

  /**
   * The one-row auto-play strip. It is the balance readout AND the explanation of what Orbit is, in
   * the same strip — which is why the page's three explanatory paragraphs could be deleted rather
   * than merely shortened (specs/orbit-page-restructure.md R8).
   *
   * walletBalance = null drops the first segment: the modal has no room for it and the player is
   * looking at their wallet balance elsewhere on the game screen anyway.
   */
  const props = defineProps<{
    walletBalance: number | null;
    orbitBalance: number;
    stake: number | null;
    tokenLabel: string;
    roundsLeft: number | null;
    status: 'signed-out' | 'off' | 'cooling-off' | 'paused' | 'running';
    coolOffLabel?: string | null;
  }>();

  const money = (value: number) =>
    value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

  // Red only when something is wrong. A running Orbit's readout is muted, not green — green is for
  // the ✓ pips and the status tag, and repeating it here would make the strip shout for no reason.
  const tail = computed(() => {
    if (props.status === 'cooling-off')
      return { text: `Cooling off · ${props.coolOffLabel ?? ''}`.trim(), warn: true };
    if (props.status === 'paused') return { text: 'Not enough for a round', warn: true };
    if (props.status === 'off') return { text: 'Not playing', warn: false };
    if (props.roundsLeft === null) return { text: '— rounds left', warn: false };
    return { text: `${props.roundsLeft} rounds left`, warn: false };
  });
</script>

<template>
  <div
    class="flex flex-wrap items-center gap-x-3 gap-y-2 rounded-arcade border border-line bg-surface-row px-3 py-2.5 md:gap-x-4 md:px-4"
  >
    <div v-if="walletBalance !== null" class="flex flex-col gap-[3px]">
      <span class="dialog-eyebrow">WALLET</span>
      <span class="vt323-regular text-[19px] leading-none tabular-nums text-ink">
        {{ money(walletBalance) }}
      </span>
    </div>
    <span v-if="walletBalance !== null" class="text-[11px] text-ink-mute">▶</span>

    <div class="flex flex-col gap-[3px]">
      <span class="dialog-eyebrow">ORBIT HOLDS</span>
      <span class="flex items-baseline gap-1.5">
        <OrbitMark :size="11" class="text-orbit" />
        <span class="vt323-regular text-[22px] leading-none tabular-nums text-orbit">
          {{ money(orbitBalance) }}
        </span>
        <span class="press-start-2p-regular text-[7px] text-orbit">{{ tokenLabel }}</span>
      </span>
    </div>

    <template v-if="stake !== null">
      <span class="text-[11px] text-ink-mute">▶</span>
      <div class="flex flex-col gap-[3px]">
        <span class="dialog-eyebrow">PER ROUND</span>
        <span class="vt323-regular text-[19px] leading-none tabular-nums text-ink">
          {{ money(stake) }}
        </span>
      </div>
    </template>

    <span
      class="vt323-regular ml-auto text-[18px]"
      :class="tail.warn ? 'text-[color:oklch(0.8_0.16_25)]' : 'text-ink-mute'"
    >
      {{ tail.text }}
    </span>
  </div>
</template>
```

- [ ] **Step 2: Verify**

```bash
yarn lint && yarn build
```

- [ ] **Step 3: Commit and push**

```bash
git add src/components/orbit/OrbitStatusBanner.vue
git commit -m "Add the Orbit auto-play status banner.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
git push origin feature/orbit-ui-overhaul
```

---

## Task 8: Front — `OrbitStepRow.vue`

**Files:**
- Create: `src/components/orbit/OrbitStepRow.vue`

**Interfaces:**
- Consumes: the `.orbit-step*` classes from Task 4
- Produces:
  ```ts
  defineProps<{
    index: number;
    title: string;
    state: 'satisfied' | 'active' | 'todo' | 'warn' | 'locked';
    value?: string | null;      // rendered in plain ink
    valueWarn?: string | null;  // rendered in red, appended after `value`
    open: boolean;
    lockedNote?: string | null;
  }>()
  defineEmits<{ (e: 'toggle'): void }>()
  // slot: default — the expanded body
  ```

- [ ] **Step 1: Write the component**

```vue
<script setup lang="ts">
  import { computed } from 'vue';

  /**
   * One step on /orbit. Collapsed, it states its ANSWER in plain ink; open, it renders its editor.
   *
   * ⚠️ Colour marks attention and identity, never completion (specs/orbit-page-restructure.md §4).
   * A satisfied step is the QUIET default — only its ✓ pip is green. The left bar goes violet only
   * while the step is open, and red only when the step carries real money exposure.
   */
  const props = defineProps<{
    index: number;
    title: string;
    state: 'satisfied' | 'active' | 'todo' | 'warn' | 'locked';
    value?: string | null;
    valueWarn?: string | null;
    open: boolean;
    lockedNote?: string | null;
  }>();

  const emit = defineEmits<{ (event: 'toggle'): void }>();

  const rowClass = computed(() => ({
    'orbit-step--active': props.open,
    'orbit-step--warn': !props.open && (props.state === 'warn' || props.state === 'active'),
    'orbit-step--locked': props.state === 'locked',
  }));

  const pip = computed(() => {
    if (props.open) return { text: String(props.index), cls: 'border-orbit text-orbit' };
    if (props.state === 'satisfied')
      return { text: '✓', cls: 'border-[color:var(--color-live)] text-[color:var(--color-live)]' };
    if (props.state === 'warn' || props.state === 'active')
      return { text: '!', cls: 'border-[color:oklch(0.8_0.16_25)] text-[color:oklch(0.8_0.16_25)]' };
    return { text: String(props.index), cls: 'border-line text-ink-mute' };
  });

  const interactive = computed(() => props.state !== 'locked');
</script>

<template>
  <div class="orbit-step" :class="rowClass">
    <button
      type="button"
      class="flex w-full items-center gap-2.5 px-3 py-2.5 text-left md:px-3.5"
      :class="open ? 'bg-[color:color-mix(in_oklab,var(--color-orbit)_12%,transparent)]' : 'bg-surface-bar'"
      :disabled="!interactive"
      :aria-expanded="open"
      @click="interactive && emit('toggle')"
    >
      <span
        class="flex h-[18px] w-[18px] flex-none items-center justify-center rounded-full border text-[8px] leading-none"
        :class="pip.cls"
      >
        {{ pip.text }}
      </span>

      <span
        class="press-start-2p-regular text-[8px] uppercase leading-[1.4] tracking-[0.06em] md:text-[9px]"
        :class="open ? 'text-ink' : state === 'locked' ? 'text-ink-mute' : 'text-ink-dim'"
      >
        {{ title }}
      </span>

      <span class="ml-auto flex items-center gap-2">
        <span v-if="lockedNote" class="vt323-regular text-[17px] text-ink-mute">
          {{ lockedNote }}
        </span>
        <span v-else-if="value || valueWarn" class="vt323-regular text-[18px] tabular-nums text-ink">
          <template v-if="value">{{ value }}</template>
          <template v-if="value && valueWarn"> / </template>
          <span v-if="valueWarn" class="text-[color:oklch(0.8_0.16_25)]">{{ valueWarn }}</span>
        </span>
        <span v-if="interactive" class="text-[9px] text-ink-mute">{{ open ? '▾' : '▸' }}</span>
      </span>
    </button>

    <div v-if="open" class="px-3 py-3 md:px-3.5"><slot /></div>
  </div>
</template>
```

- [ ] **Step 2: Verify**

```bash
yarn lint && yarn build
```

- [ ] **Step 3: Commit and push**

```bash
git add src/components/orbit/OrbitStepRow.vue
git commit -m "Add the Orbit step row.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
git push origin feature/orbit-ui-overhaul
```

---

## Task 9: Front — rebuild `OrbitPage.vue`

The largest task. Read the existing file first: **every handler (`onSaveThreshold`, `onSaveFloor`, `onClearThreshold`, `onClearFloor`, `onRecover`, `onStartTelegramLink`, `onVerifyTelegram`, `onUnlinkTelegram`, `onSignIn`) is reused verbatim.** Only the template and the step scaffolding change.

**Files:**
- Modify: `src/pages/OrbitPage.vue` (rebuild)

**Interfaces:**
- Consumes: `OrbitStatusBanner`, `OrbitStepRow`, `OrbitAmountField`, and `depositBounds` / `roundsLeft` / `orbitStatus`
- Produces: nothing consumed downstream

- [ ] **Step 1: Add the open-step state and the step model**

```ts
  // Exactly one step open at a time. Opening one closes the others — that is what keeps the page a
  // dashboard rather than a wall.
  const openStep = ref<number | null>(null);
  const toggleStep = (index: number) => {
    openStep.value = openStep.value === index ? null : index;
  };

  // The step that DEMANDS attention, resolved once. It auto-opens on arrival so a player who has to
  // act never has to hunt for the row that is asking.
  const demandingStep = computed(() => {
    if (!orbit.value?.enabled) return 1;
    if (isPausedForFunds.value) return 2;
    if (!hasCashOutFloor.value) return 3;
    return null;
  });

  watch(
    demandingStep,
    (step) => {
      if (openStep.value === null && step !== null) openStep.value = step;
    },
    { immediate: true }
  );
```

- [ ] **Step 2: Write the template shell**

Replace the outer wrapper's `arcade-frame--grey` with `arcade-frame--orbit`, and make the title white:

```html
<div class="mx-auto w-full max-w-[1000px] px-3 py-6 md:px-6 md:py-11">
  <div class="arcade-frame arcade-frame--orbit p-4 md:p-[26px]">
    <div class="flex items-center gap-2.5">
      <OrbitMark :size="18" class="text-orbit md:hidden" />
      <OrbitMark :size="20" class="hidden text-orbit md:block" />
      <h1
        class="press-start-2p-regular text-[13px] uppercase leading-[1.4] tracking-[0.02em] text-ink md:text-[16px]"
      >
        Orbit
      </h1>
      <span class="ml-auto flex items-center gap-1.5">
        <span class="h-[7px] w-[7px] flex-none rounded-full" :class="statusDotClass"></span>
        <span class="dialog-eyebrow">{{ statusLabel }}</span>
      </span>
      <button type="button" class="btn-dialog ml-2 flex-none" @click="goBack">◀ Back</button>
    </div>
```

with:

```ts
  const statusLabel = computed(
    () =>
      ({
        'signed-out': 'SIGNED OUT',
        off: 'OFF',
        'cooling-off': 'COOLING OFF',
        paused: 'PAUSED',
        running: 'RUNNING',
      })[orbitStatus.value]
  );

  const statusDotClass = computed(() =>
    orbitStatus.value === 'running'
      ? 'bg-[color:var(--color-live)]'
      : orbitStatus.value === 'paused' || orbitStatus.value === 'cooling-off'
        ? 'bg-[color:oklch(0.8_0.16_25)]'
        : 'bg-ink-mute'
  );
```

- [ ] **Step 3: Keep the signed-out screen, delete the signed-in explainer**

The `v-if="!signedIn"` block keeps its sign-in button and its six-cell "What Orbit does" grid — that is where a first-timer meets the feature. **Delete** the header intro paragraph from the signed-in view.

- [ ] **Step 4: Write the four steps**

```html
<template v-else>
  <div class="mt-4 md:mt-5">
    <OrbitStatusBanner
      :wallet-balance="walletBalance"
      :orbit-balance="orbit?.balance ?? 0"
      :stake="activeGameType?.requiredAmount ?? null"
      :token-label="orbit?.tokenLabel ?? ''"
      :rounds-left="roundsLeft"
      :status="orbitStatus"
      :cool-off-label="enableLockedLabel"
    />
  </div>

  <div class="mt-4 flex flex-col gap-2 md:mt-5">
    <!-- 1 · GAME — enable and target are ONE patch; the backend refuses an enabled
         enrolment with no target, so they can never be separate steps. -->
    <OrbitStepRow
      :index="1"
      title="Game"
      :state="orbit?.enabled ? 'satisfied' : enableLockedUntil ? 'locked' : 'active'"
      :value="orbit?.enabled && activeGameType ? `${activeGameType.name} · ${formatMoney(activeGameType.requiredAmount)}` : null"
      :locked-note="enableLockedUntil ? enableLockedLabel : null"
      :open="openStep === 1"
      @toggle="toggleStep(1)"
    >
      <p class="dialog-copy mb-3">
        Orbit bets this game for you, round after round, out of its own balance — even with the app
        closed.
      </p>
      <div class="flex flex-col gap-2 md:flex-row">
        <select v-model="gameChoice" class="input-arcade input-arcade--select orbit-select">
          <option value="">Pick a game</option>
          <option v-for="type in eligibleGameTypes" :key="type.gameTypeId" :value="type.gameTypeId">
            {{ type.name }} · {{ formatMoney(type.requiredAmount) }}
          </option>
        </select>
        <button
          type="button"
          class="btn-dialog btn-dialog--primary flex-none"
          :disabled="isBusy || !gameChoice"
          @click="onSaveGame"
        >
          {{ orbit?.enabled ? 'Change' : 'Turn Orbit on' }}
        </button>
      </div>
    </OrbitStepRow>

    <!-- 2 · MONEY -->
    <OrbitStepRow
      :index="2"
      title="Money"
      :state="enableLockedUntil ? 'locked' : isPausedForFunds ? 'warn' : (orbit?.balance ?? 0) > 0 ? 'satisfied' : 'todo'"
      :value="(orbit?.balance ?? 0) > 0 ? formatMoney(orbit?.balance ?? 0) : null"
      :locked-note="enableLockedUntil ? enableLockedLabel : null"
      :open="openStep === 2"
      @toggle="toggleStep(2)"
    >
      <p v-if="isPausedForFunds" class="dialog-copy mb-3">
        <span class="text-[color:oklch(0.8_0.16_25)]">Stopped.</span>
        {{ formatMoney(orbit?.balance ?? 0) }} won't cover a
        {{ formatMoney(activeGameType?.requiredAmount ?? 0) }} round.
      </p>
      <p v-else class="dialog-copy mb-3">Orbit bets from this balance, not your wallet.</p>
      <OrbitAmountField
        :min="depositBounds.min"
        :max="depositBounds.max"
        :disabled="!canDeposit"
        :busy="isBusy"
        confirm-label="Add money"
        @confirm="onDeposit"
      />
      <p v-if="!canDeposit" class="dialog-copy mt-2 !text-[17px] !text-ink-mute">
        Turn Orbit on first — it can't hold money it can't play with.
      </p>
    </OrbitStepRow>

    <!-- 3 · STOP RULES — order is FIXED, rise on top, fall underneath, at every width. -->
    <OrbitStepRow
      :index="3"
      title="Stop rules"
      :state="hasCashOutFloor ? 'satisfied' : 'warn'"
      :value="hasCashOutThreshold ? formatMoney(stopRules.threshold as number) : 'no target'"
      :value-warn="hasCashOutFloor ? null : 'no floor'"
      :open="openStep === 3"
      @toggle="toggleStep(3)"
    >
      <!-- Keep the two existing editors verbatim: onSaveThreshold / onClearThreshold /
           onSaveFloor / onClearFloor, .input-arcade + .btn-dialog, one sentence each. -->
    </OrbitStepRow>

    <!-- 4 · TELEGRAM — hidden entirely when this deployment has no bot. -->
    <OrbitStepRow
      v-if="orbit?.telegramEnabled"
      :index="4"
      title="Telegram"
      :state="orbit.telegramLinked ? 'satisfied' : 'todo'"
      :value="orbit.telegramLinked ? 'linked' : null"
      :open="openStep === 4"
      @toggle="toggleStep(4)"
    >
      <!-- Keep the existing link / verify / unlink flow verbatim. -->
    </OrbitStepRow>
  </div>
```

- [ ] **Step 5: Cash-out row, recover, and the footer strip**

```html
  <!-- Money-out must never be harder to find than money-in. Outline, NOT amber: cashing out is
       available, not urged, and an amber button here would break "a running Orbit shows no amber". -->
  <div
    v-if="(orbit?.balance ?? 0) > 0"
    class="mt-3 flex flex-col gap-2 rounded-arcade border border-orbit-deep px-3 py-2.5 md:flex-row md:items-center md:px-4"
  >
    <p class="dialog-copy !text-[18px]">Take it all back to your wallet.</p>
    <button
      type="button"
      class="btn-dialog ml-auto flex-none"
      :disabled="!canCashOut"
      @click="onCashOut"
    >
      Cash out {{ formatMoney(orbit?.balance ?? 0) }}
    </button>
  </div>

  <details class="mt-3">
    <summary class="dialog-eyebrow cursor-pointer">RECOVER A DEPOSIT</summary>
    <div class="mt-2 flex flex-col gap-2">
      <p class="dialog-copy !text-[18px]">
        Sent a deposit that never showed up? Paste its transaction signature.
      </p>
      <input v-model="recoverySignature" type="text" placeholder="Transaction signature" class="input-arcade" />
      <OrbitAmountField
        :min="depositBounds.min"
        :max="depositBounds.max"
        :busy="isBusy"
        confirm-label="Recover"
        @confirm="onRecover"
      />
    </div>
  </details>

  <div class="mt-4 flex flex-wrap items-center gap-x-4 gap-y-2 border-t border-line pt-3">
    <span class="dialog-eyebrow">WON</span>
    <span class="vt323-regular text-[18px] tabular-nums text-ink-dim">
      {{ orbit?.totalWonViaOrbit ?? 0 }} {{ orbit?.tokenLabel }}
    </span>
    <span class="dialog-eyebrow">BETS</span>
    <span class="vt323-regular text-[18px] tabular-nums text-ink-dim">
      {{ orbit?.orbitBetsPlaced ?? 0 }}
    </span>
    <button type="button" class="btn-dialog ml-auto flex-none" :disabled="isBusy" @click="onDisable">
      Turn Orbit off
    </button>
  </div>
</template>
```

⚠️ `onRecover` now takes the amount as an argument — update its signature to `(amount: number)` and drop the `recoveryAmount` ref.

- [ ] **Step 6: Delete the superseded markup**

Remove: both `Open Orbit panel` buttons, the `orbitIsOff` notice bar, band A's `dialog-cells` strip, band B's ladder + `WHY BOTH` strip, band C's two-card grid, the phone-only counters block, and the closing consequential paragraph. Remove the now-unused `openOrbit` import.

- [ ] **Step 7: Verify**

```bash
yarn lint && yarn build
```

Then in the browser at each of: signed out, off, running, paused, floor-unset, cooling-off — confirm spec §12 ACs 1–12 and **0 console errors**.

- [ ] **Step 8: Commit and push**

```bash
git add src/pages/OrbitPage.vue
git commit -m "Rebuild /orbit as a four-step collapse-to-dashboard list.

Every Orbit action is now inline — the two Open Orbit panel buttons are gone.
Steps collapse to one line carrying their answer in plain ink; exactly one is
open at a time, and the step that demands attention auto-opens.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
git push origin feature/orbit-ui-overhaul
```

---

## Task 10: Front — reduce `OrbitModal.vue` to two blocks

**Files:**
- Modify: `src/components/orbit/OrbitModal.vue`

**Interfaces:**
- Consumes: `OrbitStatusBanner` (with `walletBalance = null`), `OrbitAmountField`, `orbitStatus`
- Produces: nothing consumed downstream

- [ ] **Step 1: Replace the body with two blocks**

Keep the `ArcadeModal` shell, the `resumePendingDeposit` call and the existing `onDeposit` / `onEnable` handlers. The body becomes:

```html
<OrbitStatusBanner
  v-if="orbit?.enabled"
  :wallet-balance="null"
  :orbit-balance="orbit?.balance ?? 0"
  :stake="activeGameType?.requiredAmount ?? null"
  :token-label="orbit?.tokenLabel ?? ''"
  :rounds-left="roundsLeft"
  :status="orbitStatus"
  :cool-off-label="enableLockedLabel"
/>

<!-- Turn on — for the game the player is ALREADY IN. No <select>: the game comes from the route,
     which is the whole reason this modal can be shorter than the page. -->
<div v-if="!orbit?.enabled" class="mt-3 rounded-arcade border border-orbit px-3 py-3">
  <span class="dialog-eyebrow">PLAY THIS GAME AUTOMATICALLY</span>
  <p class="dialog-copy mt-2">
    Orbit will bet <span class="text-ink">{{ activeGameType?.name }}</span> for you,
    {{ formatMoney(activeGameType?.requiredAmount ?? 0) }} a round, from its own balance — even with
    the app closed.
  </p>
  <button
    type="button"
    class="btn-dialog btn-dialog--primary mt-3 w-full"
    :disabled="!canEnable"
    @click="onEnableForCurrentGame"
  >
    Turn Orbit on
  </button>
</div>

<div class="mt-3">
  <span class="dialog-eyebrow">ADD MONEY</span>
  <div class="mt-2">
    <OrbitAmountField
      :min="depositBounds.min"
      :max="depositBounds.max"
      :disabled="!canDeposit"
      :busy="isBusy"
      confirm-label="Add money"
      @confirm="onDeposit"
    />
  </div>
  <p v-if="!canDeposit" class="dialog-copy mt-2 !text-[17px] !text-ink-mute">
    Turn Orbit on first — it can't hold money it can't play with.
  </p>
</div>

<!-- The seam row (specs/orbit-ui-overhaul.md §3.1, relabelled). The WHOLE ROW navigates, values
     included — a bare SETTINGS button would teach nothing, and an unset floor must be legible from
     inside a live round. -->
<button
  type="button"
  class="mt-3 flex w-full items-center gap-2 rounded-arcade border border-line px-3 py-2.5 text-left"
  @click="goToOrbitSettings"
>
  <span class="dialog-eyebrow">STOP RULES</span>
  <span class="vt323-regular text-[18px] tabular-nums text-ink">
    {{ hasCashOutThreshold ? formatMoney(stopRules.threshold as number) : 'no target' }}
    /
    <span v-if="!hasCashOutFloor" class="text-[color:oklch(0.8_0.16_25)]">no floor</span>
    <span v-else>{{ formatMoney(stopRules.floor as number) }}</span>
  </span>
  <span class="dialog-eyebrow ml-auto">ORBIT SETTINGS ▸</span>
</button>
```

- [ ] **Step 2: Add the two handlers**

```ts
  // The game comes from the route, so enabling here needs no picker — but it is still ONE patch,
  // because the backend refuses an enabled enrolment with no target.
  const onEnableForCurrentGame = async () => {
    if (!activeGameType.value) return;
    await updateSettings({ enabled: true, gameTypeId: activeGameType.value.gameTypeId });
  };

  const goToOrbitSettings = () => {
    emit('close');
    router.push('/orbit');
  };
```

- [ ] **Step 3: Delete the superseded markup**

Remove the game `<select>` and its `pendingEnable` / `pendingGameId` refs, the deposit `<select>`, the cash-out block, and any Telegram / recover / rule markup still in the file.

- [ ] **Step 4: Verify**

```bash
yarn lint && yarn build
```

Then on `/game/play?type=<Cosmic Sprint>`: open the modal from the console module, confirm two blocks, no `<select>`, the current game named, and the seam row navigating to `/orbit`. Confirm the modal does **not** mount on Space Jackpot.

- [ ] **Step 5: Commit and push**

```bash
git add src/components/orbit/OrbitModal.vue
git commit -m "Reduce the Orbit modal to add-money and turn-on-for-this-game.

The game comes from the route, so the picker is gone. Everything that is set
once now lives on /orbit; the seam row keeps both live rule values and is
relabelled ORBIT SETTINGS.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
git push origin feature/orbit-ui-overhaul
```

---

## Task 11: KB sync + full verification

**Files:**
- Modify: `monkify-meta/features/orbit-custodial-wallet.md`
- Modify: `monkify-meta/constraints.md`

- [ ] **Step 1: Update the feature doc**

In `features/orbit-custodial-wallet.md`, replace every description of the deposit ladder with the range rule: whole numbers `1 … MaxDepositPerTransaction` (default `100`), validated client-side before the transfer and server-side in `CreditOrbitDepositHandler`. Note that `/orbit` is now the full surface and the modal is the in-round shortcut.

- [ ] **Step 2: Record the §6 exception**

In `constraints.md` §6, add: the deposit-bound message (`Deposits are whole numbers from N to M.`) is a **sanctioned exception** — it states a published bound and reveals nothing about a session, a nonce or another player.

- [ ] **Step 3: Full verification**

```bash
# backend
cd .worktrees/orbit-ui-overhaul/monkify-backend && scripts/run_tests.bat
# front
cd ../monkify-front && yarn lint && yarn build
```

Expected: all green. Then walk **every** step of spec §13 against the local Docker stack, and confirm §12 ACs 1–18.

- [ ] **Step 4: Commit and push**

```bash
git add -A
git commit -m "Sync the KB for the Orbit page restructure.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
git push origin feature/orbit-ui-overhaul
```

---

## Self-Review

**Spec coverage:** §0 supersessions → Tasks 9, 10, 2, 4. §4 palette → Tasks 4, 7, 8. §4.4 material → Task 4. §5 page + states → Task 9. §6 modal → Task 10. §7 deposits → Tasks 2, 5, 6. §9 file checklist → all tasks. §10 worktrees → Task 1. §11 tests → Tasks 2, 3. §12 ACs → Tasks 9, 10, 11. §13 QA → Task 11. **No gaps.**

**Type consistency:** `depositBounds` / `roundsLeft` / `orbitStatus` are defined in Task 5 and consumed with those exact names in Tasks 6, 7, 9, 10. `OrbitStepRow`'s prop names (`index`, `title`, `state`, `value`, `valueWarn`, `open`, `lockedNote`) match every call site in Task 9. `OrbitAmountField` emits `confirm(amount: number)`, and Task 9 Step 5 flags the matching `onRecover(amount)` signature change.

**Known follow-up, deliberately not in scope:** the `orbitStatus` union is duplicated as a literal prop type on `OrbitStatusBanner`. Extracting it to `types/orbit.ts` is a tidy-up, not a behaviour change.

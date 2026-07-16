# Feature — Responsible-gaming pre-bet hook

> Status: **shipped.** Authoritative spec: `../specs/rg-pre-bet-hook.md`
> (merged via `feature/rg-pre-bet-hook` on 2026-07-01).

## What it does

Lets an operator **allow or deny a bet on responsible-gaming (RG) grounds before it is accepted** —
self-exclusion, deposit/time limits, cooling-off, incomplete KYC, or a blocked account. ChimpOut owns
no RG state and stores no player PII; it calls a pluggable **per-operator pre-bet check adapter** and
acts on the answer. The check is **opt-in**: an operator with no RG provider configured behaves exactly
as before. (The pitch-facing RG/operator integration document is a separate, unshipped story.)

## Core rules

- **Opt-in, per operator, both payment models.** The check runs only when an RG provider is configured
  for that operator (in `GeneralSettings.OperatorRgChecks`), regardless of `PaymentModel` — crypto and
  regulated operators can both use it. No provider configured → `IPreBetCheckResolver.Resolve` returns
  `null` → the check is **skipped** and the bet proceeds unchanged. This is the default for every
  existing operator.
- **Placement — after local validation, before the wallet debit.** In `RegisterBetHandler` the check
  runs after `ValidateBetParameters` (choice length, duplicate, game-accepting, operator active all
  passed) and immediately before the regulated wallet debit. So a malformed/duplicate/invalid bet
  **never** calls the RG endpoint, and a **denied bet is never debited or persisted**.
- **Runs for both models at that point:** crypto → RG → persist; regulated → RG → debit → persist.
- **Outcome allow / deny / unavailable.** The adapter returns `PreBetCheckResult`:
  - **Allowed** → the bet continues normally.
  - **Denied(reason)** → the bet is rejected with a **specific** player-facing message. RG denials are
    *player* state (not operator-contract status), so specificity is intended — this is distinct from
    the vague operator-availability rule, which still applies elsewhere. Reasons:
    `SelfExcluded, LimitReached, CoolingOff, KycRequired, Blocked` map to specific `ErrorMessages`
    keys; `Other`/any unmapped code → a generic "you can't place this bet right now" fallback.
  - **Unavailable / timeout / error → fail-closed.** The bet is rejected with a specific "temporarily
    unavailable, please try again" message; no debit, no persistence, no synchronous retry. Any thrown
    exception from the adapter is caught and treated as `Unavailable`.
- **Free bets.** The check applies to a free bet that carries an external player id; it is **skipped**
  for identity-less (anonymous) free bets (nothing to check). The single guard is "run only when a
  resolved external player id exists" — non-free bets always have one by the time the check runs.
- **Audit = log only.** Each decision (outcome + reason + the bet id as correlation) is written to the
  server-side Serilog log. **No DB table, no migration, no PII stored.**
- **Request payload** sent to the adapter: `operatorId`, `externalPlayerId`, `amount`, `currency`
  ("USD"), `gameId`, `gameType` (`GameParameters.Name`), `betId`.
- **Config & secrets.** Per-operator RG config lives under
  `GeneralSettings.OperatorRgChecks.Operators[<operatorId>]` = `{ Provider, BaseUrl, ApiKey,
  TimeoutSeconds }` in `chimpoutsettings.json`, **mock values only**; the committed block ships empty
  (`Operators: {}`). A provider configured with an **unknown** name is a hard configuration error
  (throws) — only an *absent* provider is silently skipped.
- **Adapter selection.** `IPreBetCheckResolver` maps the operator's configured provider name to a
  registered `IPreBetCheckService`. The repo ships one adapter — the mock (provider `"mock"`), which
  always allows; real casino RG adapters are added per onboarding. Frontend is **unchanged** (denials
  ride the existing `ApiResult.messages` → toast path, like wallet-debit errors).

## Key code touchpoints

**Backend (`monkify-backend`)**

- Abstraction: `ChimpOut.Infrastructure/Services/PreBetChecks/` — `IPreBetCheckService`,
  `PreBetCheckRequest`, `PreBetCheckResult` (+ `PreBetCheckOutcome`),
  `PreBetCheckDenyReason` (`SelfExcluded | LimitReached | CoolingOff | KycRequired | Blocked | Other`),
  `IPreBetCheckResolver` + `PreBetCheckResolver` (returns `null` when unconfigured; throws on a
  configured-but-unknown provider).
- Shared mock: `ChimpOut.Infrastructure/Services/OperatorWallets/MockOperatorWalletService.cs` also
  implements `IPreBetCheckService` (provider `"mock"`, always `Allowed`).
- Handler: `ChimpOut.Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs`
  (`RunPreBetCheck`, `MapDenyReasonToMessage`; called after `ValidateBetParameters`, before
  `DebitOperatorWallet`, guarded by `resolvedExternalId != null`; fail-closed try/catch).
- Config: `ChimpOut.Domain/Configs/Entities/OperatorRgCheckSettings.cs`;
  `GeneralSettings.OperatorRgChecks`; empty `OperatorRgChecks` block in
  `ChimpOut.Common/chimpoutsettings.json` (mock).
- Messages: `ChimpOut.Common/Resources/ErrorMessages` — `RgSelfExcluded`, `RgLimitReached`,
  `RgCoolingOff`, `RgKycRequired`, `RgBlocked`, generic `RgBetNotAllowed`, and
  `RgCheckTemporarilyUnavailable`.
- DI: `ChimpOut.Infrastructure/DependencyInjection.cs` registers `IPreBetCheckService` (mock) +
  `IPreBetCheckResolver`.
- Host: `ChimpOut.Api` (synchronous, on the bet path). **No entity, EF mapping, or migration.**
- Tests: `ChimpOut.Tests/UnitTests/Handlers/RegisterBetHandlerTests.cs` (RG allow/deny/unavailable/
  throws/skip/free-bet/crypto cases), `ChimpOut.Tests/UnitTests/Services/PreBetChecks/PreBetCheckResolverTests.cs`.

**Frontend (`monkify-front`)**

- **None.** Denied/unavailable rejections render through the existing `ApiResult.messages` → toast path.

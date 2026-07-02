# Feature — Player Customization Modal

> Status: **shipped.** Authoritative spec: `../specs/player-customization-modal.md`
> (merged via `feature/player-customization-modal` on 2026-07-02).

## What it does

Gives players a **gear icon** in the game header (lobby and live play) that opens a modal to edit
**display name** (all operators) and **preferred display currency** (regulated operators only). Display
names render wherever players are identified (own `YOU` label, recent-bets rows, winners). For
regulated operators, on-screen money amounts re-render in the chosen fiat currency with an `≈`
indicator; crypto operators keep native SPL token display and see display-name only. **Conversion is
display-only** — backend REST and SignalR always carry USD; settlement, persistence, and
operator-wallet API amounts stay USD.

## Core rules

- **Entry point:** gear icon on the right of `GameHeader.vue` (rendered once in `App.vue`, so it
  appears on session select and live play). Modal built with the design system (DaisyUI modal +
  `PrimaryButton`/`SecondaryButton`).
- **Modal fields:** display name (text, all operators) + preferred currency (dropdown, **regulated
  only** — hidden for crypto). One **Save** → single `PATCH /players/preferences` with both fields;
  on success currency state refreshes and live amounts re-render.
- **Display name validation:** trimmed, **1–20** characters; empty/whitespace-only rejected. Renders
  for self and others on bet rows and winners; falls back to truncated `externalPlayerId` when unset.
  Winner names are resolved **client-side** from bets already held — `SessionResult` is unchanged.
- **Currency model:** fiat-only, USD-based, **regulated operators only**. Definitive set = 33 codes
  from `SupportedCurrencies` (FreeCurrencyAPI `latest` with USD base; USD rate = 1.0). Crypto
  allow-list = **USD only** (fiat rejected on `PATCH`).
- **Backend source of truth:** currency metadata (`code → { symbol, name }`) and allow-list served via
  `GET /fx/rates`; frontend does not hardcode the list.
- **Conversion:** **client-side only.** Backend returns USD everywhere (REST + SignalR — hubs have no
  token). Frontend multiplies USD amounts by the chosen currency's USD→code rate via
  `useDisplayCurrency.formatUsd`. Never feeds settlement, payout, refund, persistence, or operator API.
- **Rate source:** `IExchangeRateProvider` — `FreeCurrencyApiExchangeRateProvider` (prod; first
  outbound `HttpClient` in the codebase) or `MockExchangeRateProvider` (dev/tests). Full USD table
  cached in Redis (`Fx:Rates:USD`, **6-hour TTL**). Provider key is a **MOCK placeholder** in config;
  real key from environment only.
- **`GET /fx/rates`** (token-gated, `/fx` in launch-token middleware): returns
  `{ base: "USD", asOf, rates: [{ code, symbol, name, rate }] }`. Rate may be `null` when provider
  unavailable and cache empty; never 5xx on provider outage — frontend falls back to USD silently.
- **`GET /players/preferences`** (token-gated): returns
  `{ externalPlayerId, displayName, currency }` (defaults `null` / `"USD"` when no `OperatorPlayer`
  row; **no row created on read**). Regulated identity = token `sub`; crypto = optional `?wallet=`
  query param (token `sub` is empty for crypto).
- **`PATCH /players/preferences`:** extended with payment-model-scoped currency allow-list and display
  name validation (reuses generic validation-failure messaging).
- **Bet DTO / SignalR:** `DisplayName` on `BetDto` and `BetNotificationEvent`; `SessionService.InsertBet`
  captures display name before nulling `OperatorPlayer` for the event payload.
- **Formatting (regulated):** chosen currency primary; when `!= USD` show `≈ {symbol}{value}` (2 dp)
  with authoritative USD on hover; USD selected → plain USD, no `≈`. Rate unavailable → silent USD
  fallback, no error toast.
- **No EF migration** — reuses existing `OperatorPlayer.DisplayName` and `Currency` columns.
- **Auth failures:** token-gated endpoints return generic **401** (real reason logged server-side only).

## Key code touchpoints

**Backend (`monkify-backend`)**

- `Monkify.Api/Controllers/FxController.cs` — `GET /fx/rates`
- `Monkify.Api/Controllers/PlayersController.cs` — `GET /players/preferences`, `PATCH /players/preferences`
- `Monkify.Api/Middleware/LaunchTokenValidationMiddleware.cs` — `/fx` protected prefix
- `Monkify.Domain/Fx/SupportedCurrencies.cs` — definitive currency registry
- `Monkify.Infrastructure/Services/Fx/IExchangeRateProvider.cs`, `FreeCurrencyApiExchangeRateProvider.cs`,
  `MockExchangeRateProvider.cs`
- `Monkify.Infrastructure/Handlers/Fx/GetFxRates/GetFxRatesHandler.cs` — Redis cache
- `Monkify.Infrastructure/Handlers/Players/GetPlayerPreferences/GetPlayerPreferencesHandler.cs`
- `Monkify.Infrastructure/Handlers/Players/UpdatePlayerPreferences/UpdatePlayerPreferencesHandler.cs`
- `Monkify.Infrastructure/Contracts/Sessions/BetDto.cs`, `Events/BetNotification/BetNotificationEvent.cs`
- `Monkify.Infrastructure/Services/Sessions/SessionService.cs` — `DisplayName` capture on insert
- `Monkify.Common/monkifysettings.json` — `GeneralSettings.Fx` (Provider, BaseUrl, ApiKey mock, CacheTtlHours)
- Tests: `GetFxRatesHandlerTests`, `GetPlayerPreferencesHandlerTests`, `UpdatePlayerPreferencesHandlerTests`,
  `BetDtoTests`, `FreeCurrencyApiExchangeRateProviderTests`, `MockExchangeRateProviderTests`

**Frontend (`monkify-front`)**

- `src/components/header/PlayerCustomizationModal.vue` — modal UI
- `src/components/GameHeader.vue` — gear icon + modal trigger
- `src/hooks/useDisplayCurrency.ts` — rates, prefs, `formatUsd`
- `src/services/mainService.ts` — `getFxRates()`, `getPlayerPreferences(wallet?)`
- `src/components/game/GameTerminal.vue` — display names + converted amounts
- `src/hooks/useSession.ts`, `src/types/index.ts` — `AppliedBet.displayName`, FX types
- `src/App.vue` — loads rates + preferences on mount

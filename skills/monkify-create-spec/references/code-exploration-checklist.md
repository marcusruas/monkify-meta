# Code exploration checklist (for spec writing)

Use this when drafting a spec from a refined card. Search and **read** the live code — the KB
`features/` docs may be stubs.

## Always read first

- `monkify-meta/constraints.md`
- Relevant `monkify-meta/features/*.md`
- `monkify-meta/specs/remove-operators.md` (format reference only)

## By feature area

For each area the card touches, find the current implementation and note **file + line** edit points.

### Sessions & typer

- `Monkify.Domain/Sessions/Services/MonkifyTyper.cs`
- `Monkify.Infrastructure/Services/Sessions/SessionService.cs`
- `Monkify.Infrastructure/Consumers/RunSession/*`
- `Monkify.Domain/Sessions/Entities/Session.cs`, `SessionParameters.cs`
- Front: `src/hooks/useSession.ts`, `GameTerminal.vue`, `GameTyper.vue`

### Bets & registration

- `Monkify.Infrastructure/Handlers/Sessions/RegisterBet/RegisterBetHandler.cs`
- `RegisterBetRequestBody.cs` — model validation attributes
- `Monkify.Domain/Bets/Entities/Bet.cs`
- `Monkify.Domain/Bets/Services/BetDomainService.cs` — choice validation, pot math
- `Monkify.Tests/UnitTests/Handlers/RegisterBetHandlerTests.cs`

### Payments / Solana

- `Monkify.Infrastructure/Services/Solana/SolanaService.cs` — ValidateBetPayment, Transfer, Refund
- `monkify-front/src/hooks/useConnection.ts` — sendToken

### Rewards & refunds

- `Monkify.Infrastructure/Consumers/RewardSession/RewardSessionConsumer.cs`
- `Monkify.Infrastructure/Consumers/RefundInvalidBet/RefundInvalidBetConsumer.cs`
- `Monkify.Infrastructure/Consumers/BetPlaced/BetPlacedConsumer.cs` — min players, lottery refunds
- `Monkify.Tests/UnitTests/Background/RewardWinnersHandlerTests.cs`

### Tenancy / configuration (there is no multi-tenancy)

- `SpaceStroker.Common/spacestrokersettings.json` — `GeneralSettings:Operator` (Token / AdminToken /
  DefaultGames): the deployment *is* the operator
- `monkify-front/src/services/mainService.ts` — interceptor
- `SpaceStroker.Api/Admin/AdminTokenService.cs` + `AdminTokenValidationMiddleware` — the only auth left
- `monkify-meta/features/remove-operators.md`

### API & DTOs

- `Monkify.Infrastructure/Contracts/Sessions/SessionParametersDto.cs`
- `GetActiveParametersHandler.cs`
- `monkify-front/src/types/index.ts`

### EF & migrations

- `Monkify.Infrastructure/Data/Context/MonkifyDbContext.cs`
- Latest migration in `Monkify.Infrastructure/Migrations/`

### Config

- `Monkify.Domain/Configs/Entities/GeneralSettings.cs`, `SessionSettings.cs`
- `Monkify.Common/monkifysettings.json`
- `monkify-front/.env.example` — env var naming pattern

## Questions to answer in the spec

For each change, document in the spec:

1. **What blocks today?** (validation, required fields, UI gates)
2. **What is the new detection rule?** (API shape, flags, DB columns)
3. **Which host owns it?** — `Monkify.Api` vs `Monkify.Consumers`
4. **What happens on failure?** — refund, vague error, log-only
5. **Mid-session / concurrency edge** from the card
6. **Tests** — existing file to extend vs new test class

## Common hidden blockers (call out in spec when relevant)

- `[Required]` on DTO fields that must become optional for a new path
- Duplicate-key checks (payment signature, wallet+choice) that break new flows
- Player-count logic using `DistinctBy` wallet when wallet can be empty
- Pot/reward math assuming every bet has `Amount == RequiredAmount`
- Frontend `connected` wallet gate in `GameConsole.vue` / status labels in `GameTerminal.vue`
- Env var prefix: codebase uses **`VUE_APP_*`**, not Vite `VITE_*`

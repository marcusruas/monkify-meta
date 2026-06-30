# Backend conventions (`monkify-backend`, .NET 8)

> Status: **stub** — to be fully drafted. Captures "how we write backend code here".

## Patterns observed (fill in / verify)

- **EF mapping is INLINE** in `MonkifyDbContext.OnModelCreating` — no `IEntityTypeConfiguration`
  classes. Add new entity config as a `modelBuilder.Entity<T>(builder => { … })` block in the
  existing style.
- **Migrations** live in `Monkify.Infrastructure/Migrations/` and are applied **only** at
  `Monkify.Consumers` startup via `Database.Migrate()`. Generate with `dotnet ef migrations add`;
  never hand-edit the snapshot.
- **CQRS-ish via MediatR:** each use case is a `Request` + `Handler` pair under
  `Monkify.Infrastructure/Handlers/<Area>/<UseCase>/`. Handlers extend `BaseRequestHandler`.
- **Single-responsibility classes read top-down — isolate behaviours into named methods.** When a
  class exists to do one thing (a MediatR handler, a MassTransit consumer, a domain service entry
  point), the **main method should orchestrate, not implement**. Pull each distinct behaviour /
  branch into a **well-named private method** and have the main method call it, so the main method
  reads like a summary of what the class does. Don't inline a multi-statement branch (resolve a
  service → call it → persist → log) directly in the main method.

  ```csharp
  // Prefer — the main method shows the shape of the work:
  if (paymentModel == OperatorPaymentModel.Regulated)
      await ProcessRegulatedRefund(scope, dbContext, bet, refundAmount, message.Reason);
  else
      await solanaService.RefundTokens(message.Wallet, refundAmount, message.Reason, bet.Id);

  // Avoid — the whole regulated branch (resolve + call + persist + log) inlined in the main method.
  ```

  Each extracted method should have one clear purpose and a name that says what it does
  (`ProcessRegulatedRefund`, `DebitOperatorWallet`, `CreditWinner`). This keeps the entry method
  short and the behaviours independently readable/testable.
- **Controllers are thin:** extend `BaseController`, build a MediatR request, return
  `ProcessRequest(...)` (wraps `ApiResult<T>` = `{ data, messages }`). Validation failures via
  `IMessaging.ReturnValidationFailureMessage(...)`.
- **Operator/player identity comes from the launch token, not headers.**
  `LaunchTokenValidationMiddleware` (`Monkify.Api/Middleware/`) validates the `X-Launch-Token` (RS256)
  on operator-scoped paths and populates the scoped `ILaunchContext` (`{ operatorId, externalPlayerId }`);
  controllers read it instead of binding a header. Any token failure → generic **HTTP 401** (vague;
  real reason logged with a correlation id). The old `x-operator-id` `[FromHeader]` binding is gone.
- **Cached operator lookups:** use `IOperatorService.GetActiveOperatorById` (cached under the
  per-operator key) rather than querying `Operators` directly — it's shared by the middleware,
  `GetOperatorByIdHandler`, and `RegisterBetHandler`. When signing/validating JWTs, build the
  `RsaSecurityKey` from exported `RSAParameters` with provider caching off (avoids a disposed-RSA error).
- **Async messaging via MassTransit/RabbitMQ:** consumers in `Monkify.Infrastructure/Consumers/…`
  (one receive endpoint each), registered in `Monkify.Consumers/Program.cs`. Publish via
  `IRabbitMQService`.
- **Caching via `ICacheService`** (Redis or in-memory). Keys built by `CacheHelper`; active-params
  cache is operator-aware.
- **Domain stays pure:** entities/value-objects/domain-services in `Monkify.Domain` have no
  EF/infra dependencies. All entities extend `TableEntity`.
- **Solana payments** via `ISolanaService` / `SolanaService` — validate signatures, transfer
  rewards, refund invalid bets.
- **Tests:** `Monkify.Tests` (xUnit + Shouldly + Moq + Bogus, in-memory EF). Update + add + report
  changed tests on every behavior change (see `constraints.md`).
- **Running tests — always use `scripts/run_tests.bat`.** Whenever you need to run the test suite to
  assert changes made to `monkify-backend`, run `scripts/run_tests.bat` (it executes
  `dotnet test` over the unit tests). It **excludes the `ProductionSessions` metrics tests** on
  purpose — that is the long-running typer-pacing Monte Carlo suite, which paces each simulated
  session in real wall-clock time and is not part of the normal change-assertion loop.
- **Typer-pacing metrics suite:** lives under the `…Domain.ProductionSessions` namespace as **one
  test class per default session parameter** (named for the parameters they exercise — character
  count, charset, race/preset/lottery). These tests measure each session's real paced wall-clock
  duration, so xUnit **collection parallelization is disabled** (`xunit.runner.json`): running games
  in parallel oversubscribes the CPU and inflates the measured durations past the bounds. The safe
  concurrency (the `RunCount` sessions within each case) still runs concurrently. Run it on demand
  via `scripts/run_production_session_tests.bat`. It runs only that suite with the detailed console
  logger, captures a `.trx`, and (via `scripts/generate_production_report.ps1`) renders a **PDF
  report** that opens automatically when the run finishes. The report lists, per case, pass/fail +
  duration, the per-session metrics (duration, characters typed, average char/s, winner) and the
  aggregate mean / p95 / fallback-winner lines. PDFs are rendered with headless Edge (Chrome
  fallback; HTML is opened if neither is available) and written to `TestReports/` (git-ignored).
  Set `MONKIFY_TEST_RUNCOUNT` to shrink the Monte Carlo sample size for faster local runs.
- **Application settings** live in **`Monkify.Common/monkifysettings.json`** — the single source
  for backend configuration. Both `Monkify.Api` and `Monkify.Consumers` load it via
  `AddMonkifySettings()` (environment variables can override keys at runtime). Do **not** add
  `appsettings.json` / `appsettings.Development.json` overlays for app settings; put new keys in
  `monkifysettings.json` under the appropriate `GeneralSettings` section.

<!-- TODO: confirm naming conventions, file/folder rules, error-message/resource usage, logging
     (Serilog) patterns, and add concrete examples. -->

# Backend conventions (`monkify-backend`, .NET 8)

> Status: **stub** — to be fully drafted. Captures "how we write backend code here".

## Patterns observed (fill in / verify)

- **EF mapping is INLINE** in `SpaceStrokerDbContext.OnModelCreating` — no `IEntityTypeConfiguration`
  classes. Add new entity config as a `modelBuilder.Entity<T>(builder => { … })` block in the
  existing style.
- **Migrations** live in `SpaceStroker.Infrastructure/Migrations/` and are applied **only** at
  `SpaceStroker.Consumers` startup via `Database.Migrate()`. Generate with `dotnet ef migrations add`;
  never hand-edit the snapshot.
- **CQRS-ish via MediatR:** each use case is a `Request` + `Handler` pair under
  `SpaceStroker.Infrastructure/Handlers/<Area>/<UseCase>/`. Handlers extend `BaseRequestHandler`.
- **Single-responsibility classes read top-down — isolate behaviours into named methods.** When a
  class exists to do one thing (a MediatR handler, a MassTransit consumer, a domain service entry
  point), the **main method should orchestrate, not implement**. Pull each distinct behaviour /
  branch into a **well-named private method** and have the main method call it, so the main method
  reads like a summary of what the class does. Don't inline a multi-statement branch (resolve a
  service → call it → persist → log) directly in the main method.

  ```csharp
  // Prefer — the main method shows the shape of the work:
  var service = paymentResolver.Resolve(game.Parameters);   // by game PaymentMethod
  var result = await service.Refund(bet, refundAmount, message.Reason, ct);

  // Avoid — the whole method branch (resolve + call + persist + log) inlined in the main method.
  ```

  Each extracted method should have one clear purpose and a name that says what it does
  (`DebitOperatorWallet`, `CreditWinner`, `WritePlacementCryptoLog`). This keeps the entry method
  short and the behaviours independently readable/testable.
- **Controllers are thin:** extend `BaseController`, build a MediatR request, return
  `ProcessRequest(...)` (wraps `ApiResult<T>` = `{ data, messages }`). Validation failures via
  `IMessaging.ReturnValidationFailureMessage(...)`.
- **Operator/player identity comes from the launch token, not headers.**
  `LaunchTokenValidationMiddleware` (`SpaceStroker.Api/Middleware/`) validates the `X-Launch-Token` (RS256)
  on operator-scoped paths and populates the scoped `ILaunchContext` (`{ operatorId, externalPlayerId }`);
  controllers read it instead of binding a header. Any token failure → generic **HTTP 401** (vague;
  real reason logged with a correlation id). The old `x-operator-id` `[FromHeader]` binding is gone.
- **Cached operator lookups:** use `IOperatorService.GetActiveOperatorById` (cached under the
  per-operator key) rather than querying `Operators` directly — it's shared by the middleware,
  `GetOperatorByIdHandler`, and `RegisterBetHandler`. When signing/validating JWTs, build the
  `RsaSecurityKey` from exported `RSAParameters` with provider caching off (avoids a disposed-RSA error).
- **Async messaging via MassTransit/RabbitMQ:** consumers in `SpaceStroker.Infrastructure/Consumers/…`
  (one receive endpoint each), registered in `SpaceStroker.Consumers/Program.cs`. Publish via
  `IRabbitMQService`.
- **Caching via `ICacheService`** (Redis or in-memory). Keys built by `CacheHelper`; active-params
  cache is operator-aware.
- **Domain stays pure:** entities/value-objects/domain-services in `SpaceStroker.Domain` have no
  EF/infra dependencies. All entities extend `TableEntity`.
- **Money movement routes by the game's `PaymentMethod`, via `IPaymentMethodResolver`.** Handlers and
  consumers call `paymentResolver.Resolve(game.Parameters).Debit/Credit/Refund(...)` — **never** branch
  on an operator enum (`Operator.PaymentModel` is gone). `StandardPaymentMethodService` settles off-chain
  via `IOperatorWalletService` (USD; resolved per operator by `IOperatorWalletResolver` from
  `GeneralSettings.OperatorWallets`); `SolanaTokenPaymentMethodService` settles on-chain via
  `ISolanaService` using a per-call `SolanaTokenDescriptor` built from the game's registered
  `OperatorToken` (its private key decrypted via `ISecretProtector` in Consumers only). Every movement is
  recorded in the **unified `TransactionLog`** ledger (`TransactionLogType` = `Credit`/`Debit`/`Refund`,
  carrying `PaymentMethod` + `ExternalTransactionReference`), plus a `CryptoTransactionLog` for SolanaToken;
  the ledger backs reward/refund idempotency (subtract by type). There is **no commission** in pot math.
  See `features/payment-methods.md`.
- **Secrets encrypted at rest** go through `ISecretProtector` (`AesSecretProtector`, AES-256-GCM keyed
  from the `GeneralSettings.SecretProtection.Key` passphrase): `Protect` in the API before persisting,
  `Unprotect` in Consumers only when the plaintext is needed for an outbound call. Never return an
  encrypted secret in a DTO or log it.
- **Tests:** `SpaceStroker.Tests` (xUnit + Shouldly + Moq + Bogus, in-memory EF). Update + add + report
  changed tests on every behavior change (see `constraints.md`).
- **Game-flow changes MUST update the integration tests.** End-to-end coverage of the game
  lifecycle lives in the separate **`SpaceStroker.Tests.Integration`** project (Testcontainers:
  PostgreSQL + RabbitMQ + Redis; boots the `SpaceStroker.Api` + `SpaceStroker.Consumers` hosts and runs real
  games bet→run→reward with Solana/operator-wallet mocked). It is **excluded** from the default
  fast suite (`scripts/run_tests.bat`, which scopes to `SpaceStroker.Tests` / `~UnitTests`) and runs on
  demand via **`scripts/run_integration_tests.bat`**. **Any change to the game lifecycle/flow**
  (consumers `BetPlaced`/`RunGame`/`RewardGame`/`RefundInvalidBet`, `GameService`,
  `SpaceStrokerTyper`, game/bet state machine, reward/refund/ledger paths, or the register-bet handler)
  **must also update `SpaceStroker.Tests.Integration`** so the end-to-end assertions still reflect reality.
- **Every new feature MUST add a happy-path integration test here.** Beyond game-flow changes,
  `constraints.md` → "Integration tests are mandatory for every new feature" requires each **new
  feature** to ship with at least one `SpaceStroker.Tests.Integration` test that drives its happy path end
  to end (and one per particularly complex flow). Follow the `GameFlowTests` pattern: seed via the
  fixture, drive the real API, let the Consumers host process, assert terminal DB state + ledger +
  no error logs. Bug fixes / QOL / minor changes are exempt unless they touch game flow.
- **Running tests — always use `scripts/run_tests.bat`.** Whenever you need to run the test suite to
  assert changes made to `monkify-backend`, run `scripts/run_tests.bat` (it executes
  `dotnet test` over the unit tests). It **excludes the `ProductionGames` metrics tests** on
  purpose — that is the long-running typer-pacing Monte Carlo suite, which paces each simulated
  game in real wall-clock time and is not part of the normal change-assertion loop.
- **Typer-pacing metrics suite:** lives under the `…Domain.ProductionGames` namespace as **one
  test class per default game parameter** (named for the parameters they exercise — character
  count, charset, race/preset/lottery). These tests measure each game's real paced wall-clock
  duration, so xUnit **collection parallelization is disabled** (`xunit.runner.json`): running games
  in parallel oversubscribes the CPU and inflates the measured durations past the bounds. The safe
  concurrency (the `RunCount` games within each case) still runs concurrently. Run it on demand
  via `scripts/run_production_game_tests.bat`. It runs only that suite with the detailed console
  logger, captures a `.trx`, and (via `scripts/generate_production_report.ps1`) renders a **PDF
  report** that opens automatically when the run finishes. The report lists, per case, pass/fail +
  duration, the per-game metrics (duration, characters typed, average char/s, winner) and the
  aggregate mean / p95 / fallback-winner lines. PDFs are rendered with headless Edge (Chrome
  fallback; HTML is opened if neither is available) and written to `TestReports/` (git-ignored).
  Set `SPACESTROKER_TEST_RUNCOUNT` to shrink the Monte Carlo sample size for faster local runs.
- **Application settings** live in **`SpaceStroker.Common/spacestrokersettings.json`** — the single source
  for backend configuration. Both `SpaceStroker.Api` and `SpaceStroker.Consumers` load it via
  `AddSpaceStrokerSettings()` (environment variables can override keys at runtime). Do **not** add
  `appsettings.json` / `appsettings.Development.json` overlays for app settings; put new keys in
  `spacestrokersettings.json` under the appropriate `GeneralSettings` section.

- **Logging is Serilog, console-only.** Both hosts configure Serilog once via
  `builder.AddLogs()` (`SpaceStroker.Infrastructure/DependencyInjection.cs`) — a single **Console** sink
  (stdout), minimum level **Information** in non-production and **Warning** in production. **Logs are
  not persisted to the database** (the old `Serilog.Sinks.PostgreSQL` sink and its `SpaceStrokerApiLogs`/
  `SpaceStrokerConsumerLogs` tables were removed — see `specs/no-db-logging.md`); stdout is captured by
  Cloud Run → Cloud Logging in production and shown in the terminal locally. Code logs via the static
  `Serilog.Log`.

<!-- TODO: confirm naming conventions, file/folder rules, error-message/resource usage, and add
     concrete examples. -->


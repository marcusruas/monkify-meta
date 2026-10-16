# Backend conventions (`monkify-backend`, .NET 8)

> Status: **stub** — to be fully drafted. Captures "how we write backend code here".

## Patterns observed (fill in / verify)

- **EF mapping is INLINE** in `SpaceStrokerDbContext.OnModelCreating` — no `IEntityTypeConfiguration`
  classes. Add new entity config as a `modelBuilder.Entity<T>(builder => { … })` block in the
  existing style.
- **Migrations** live in `SpaceStroker.Infrastructure/Migrations/` and are applied **only** at
  `SpaceStroker.Consumers` startup via `Database.Migrate()`. Generate with `dotnet ef migrations add`;
  never hand-edit the snapshot.
  ⚠️ **Never pass `--no-build` to `dotnet ef migrations add`.** It compares the model from *stale*
  assemblies, so a migration for a property you just added comes out with an **empty `Up`/`Down`** and
  no error. Always read the generated file before committing it.
- **⚠️ Every new query must land on a suitable index — find it (or add it) before you ship the query.**
  When you write a new EF query, or change the filter/sort of an existing one, open the entity's
  `modelBuilder.Entity<T>(...)` block in `SpaceStrokerDbContext` and confirm an existing index actually
  serves it. If none does, add one in the same change. Nothing in the test suite catches a missing
  index — the in-memory unit tests ignore indexes entirely and the Testcontainers suite runs on data
  volumes too small for a seq scan to hurt — so an unindexed query passes green and only shows up as
  production latency.

  How to tell whether an index serves your query:
  - **A composite index only helps a query whose filter matches a *leading prefix* of its columns.**
    `(ParametersId, Status, CreatedDate)` serves `ParametersId = …`, `ParametersId = … AND Status = …`,
    and all three; it does **nothing** for a query that filters `Status` alone.
  - **Order the columns equality-first, range/sort-last.** Everything compared with `==` comes first,
    then the column you range over (`CreatedDate <= x`) or sort by. A range column in the middle stops
    the index being used for anything after it.
  - **`OrderBy…().First()` / `.Take(n)` needs the sort column *in* the index, in the right
    direction** — otherwise Postgres filters and then sorts the whole match set. Spell the direction
    with `.IsDescending(false, false, true)` so a `ORDER BY … DESC LIMIT 1` becomes one index probe.
  - **Prefer extending an existing index to adding a new one.** An extended index still serves every
    query its old prefix served, so it *replaces* rather than adds — and EF's foreign-key convention
    skips its automatic FK index when an existing index already leads with that column. Watch the
    generated migration: a `DropIndex` of the old FK index is the expected, correct signal.
  - **The classic miss is a background job.** A timer that scans one low-cardinality column
    (`Status == NeedsRefunding`, `Type == CashOut AND Status == Pending`) is invisible in review and a
    full scan of the biggest table in production. `GameBets` and `OrbitTransactions` are the two tables
    where this actually bites.
  - **Index changes are schema changes** — they need a `dotnet ef migrations add` like any other, and
    they are applied by `SpaceStroker.Consumers` at startup (§4 of `constraints.md`). On a populated
    production table a plain `CREATE INDEX` blocks writes for its duration; for a large table, consider
    running it as `CREATE INDEX CONCURRENTLY` by hand ahead of the deploy.

  The current index set was audited query-by-query on 2026-08-04 (`AddQueryCoveringIndexes`); every
  table has at least one index, and each one carries a comment in `SpaceStrokerDbContext` naming the
  query it exists for. **Keep that comment habit** — an index with no stated query is the one nobody
  dares delete later.

  ⚠️ **An index is a WRITE cost too — measure both sides before you keep one.** Read latency alone will
  always say "add the index". `SpaceStroker.Tests.Integration/Benchmarks/` has both rigs
  (`scripts/run_index_benchmarks.bat`): the read rig compares plans and timings across index sets, and
  the write rig times inserts and updates and reads the **HOT-update ratio** from
  `pg_stat_user_tables`. Two findings from that audit are worth internalising:

  - **Indexing a column that an UPDATE touches can forfeit HOT updates entirely.** Postgres can apply
    an update in place, with zero index writes, only when no indexed column changed. `GameBets(Status)`
    made the refund sweep 45.8ms → 6.3ms, but `UpdateBetStatusWithoutSaving` touches only `Status` and
    `UpdatedDate` — so indexing `Status` took bet settlement from **92% HOT to 0% HOT** and +0.323ms per
    update. Against ~80ms/day earned from a twice-daily sweep, it went net-negative past ~124 settled
    bets/day. **It was dropped.** Do not re-add it without re-running the write rig.
  - **A "redundant" prefix index is not always free.** Dropping `Games(ParametersId, Status)` because it
    is a prefix of a wider composite cost the history `COUNT(*)` its cheap index-only scan (6.9ms →
    11.0ms at 1M): the wider index carries 16 more bytes per entry. It was restored.

  If you are measuring `pg_stat_user_tables` yourself: the clear and the read must happen on the **same
  connection** (each backend caches a snapshot), and you must **poll** for the counter rather than
  sleeping — the collector reports asynchronously and a fixed sleep races it.
- **Startup self-heal writes use `ExecuteUpdate`, and their change predicate must handle NULL.** The
  `SpaceStrokerDbContext` is `NoTracking` by default, so a load-mutate-`SaveChanges` at startup
  silently persists nothing. When such a patch is guarded by "only rows that differ", remember SQL
  three-valued logic: `x.Col != @value` is **NULL, not true**, when `x.Col` is NULL, so a bare
  inequality **skips exactly the rows a newly-added nullable column needs to fill**. Compare the
  null-ness explicitly — `(x.Col == null) != (@value == null) || x.Col != @value`. Seen in the
  `AllowFreeBet` and lottery-schedule self-heals in `SpaceStroker.Consumers/Program.cs`.
- **⚠️ Put a self-heal loop in a class, not inline in `Program.cs` — otherwise it ships unproven.** A
  startup patch written as a top-level statement is **unreachable from both suites**: the unit suite
  cannot call top-level program statements at all, and `ExecuteUpdate` is a silent **no-op** on the
  in-memory provider it uses. The integration suite seeds a **fresh** database, where the row is
  *created* in its final state — so the `false → true` transition, the very thing the patch exists for,
  never runs there either. Extracting the loop (`ComingSoonReconciler`, taking the context + the config
  list and returning what changed) is the only seam where the behaviour can be asserted against real
  Postgres. `Program.cs` keeps the side effects — the cache drop and the event publish — so the
  reconciler stays a pure single-column write.
- **Changing what an existing seeded type IS takes two steps: a reconciler, then a transition.** Seen in
  #196 (`HolderLotteryReconciler` + `HolderJackpotTransition`). The reconciler moves the type's **row**
  onto the new config; the transition deals with the **live round opened under the old rules** — refund
  its paid bets through the ordinary `RefundInvalidBetEvent` path (save first, publish after), close it
  `GameEndedAbruptely`, and let `OpenNewGames` open the first round of the new kind. Run it right after
  default-parameter seeding and **before** `CorrectScheduledLotteryDraws` / `ReplanOpenRounds`, make it
  idempotent by selecting on a marker the new rounds carry (`HolderMinimum == null`), and wrap it in
  `try`/`catch` + `Log.Error` so it can never take the host down.
- **A dependency only Consumers may have goes in as an OPTIONAL constructor parameter.** `GameService`
  is built by both hosts; `IHolderJackpotRoundPlanner? holderPlanner = null` is registered in
  **Consumers only** (Microsoft DI honours the default when the service is unregistered). The host
  without it must **fail closed** on that path — the API opens a holder round PAUSED and logs at Error,
  it never sizes a pot. Do not register such a service in the shared `DependencyInjection` to "make it
  resolve".
- **CQRS-ish via MediatR:** each use case is a `Request` + `Handler` pair under
  `SpaceStroker.Infrastructure/Handlers/<Area>/<UseCase>/`. Handlers extend `BaseRequestHandler`.
- **⚠️ The folder tree must show the architecture. Never leave a project as a flat pile of `.cs` files.**
  A reader — human or agent — should be able to infer how a project is built from `find . -name "*.cs"`
  alone, without opening anything. That means **every** .NET project, from the biggest service down to a
  single-purpose console tool, groups its files into folders that carry a **matching namespace**
  (`Domain/FundingPlan.cs` ⇒ `namespace <Project>.Domain`), not a flat root directory.

  **Use the layer vocabulary the solution already speaks** — the backend's own projects are the
  reference (`SpaceStroker.Domain`, `SpaceStroker.Infrastructure`, `SpaceStroker.Api`,
  `SpaceStroker.Common`). Inside a **single-project** app, mirror that split as folders:

  | Folder | Holds | Depends on |
  | --- | --- | --- |
  | `Domain/` | Entities, value objects, pure rules and calculations. **No IO, no HTTP, no filesystem, no clock.** | nothing |
  | `Infrastructure/` | Everything that talks to the outside world, one **sub-folder per external system** (`Infrastructure/Coinbase/`, `Infrastructure/Solana/`) | `Domain` |
  | `Application/` | Orchestration, CLI/entry-point wiring, presentation (console output, formatting) | `Domain`, `Infrastructure` |

  `Program.cs` stays at the project root — it is the entry point, not a layer.

  **Mirror the same folders in the test project** (`Tests/Domain/…`, `Tests/Infrastructure/…`), and put
  each test beside the layer of the type it exercises — a test for a file-backed store belongs in
  `Tests/Infrastructure/`, even if the rules it asserts are domain rules.

  Two reasons this is a rule and not a preference:
  - **The layer boundary is the testability boundary.** Keeping `Domain` free of IO is what lets the
    rules that matter be unit-tested with no fake, no container and no network. When a domain type
    needs an external value (an account balance, the current time), **pass the value in** rather than
    the client that fetches it — `Preflight.Check(pool, availableSol, …)` in
    `tools/SpaceStroker.WalletFunder` is the worked example: it is a pure function precisely because the
    balance is a `decimal`, not a `CoinbaseClient`.
  - **Flat projects hide their own architecture.** Fifteen files in one folder read as fifteen equal
    things; the same fifteen under three folders read as a design. This is the file-level counterpart of
    the "main method orchestrates, not implements" rule above.

  Applies to `monkify-backend` **and** to the standalone console apps in `monkify-meta/tools/` and
  `monkify-meta/deploy/tools/`. (`deploy/tools/SpaceStroker.Deploy` predates the rule and is still flat —
  bring it into line opportunistically, not as a drive-by refactor in an unrelated card.)
- **Single-responsibility classes read top-down — isolate behaviours into named methods.** When a
  class exists to do one thing (a MediatR handler, a MassTransit consumer, a domain service entry
  point), the **main method should orchestrate, not implement**. Pull each distinct behaviour /
  branch into a **well-named private method** and have the main method call it, so the main method
  reads like a summary of what the class does. Don't inline a multi-statement branch (resolve a
  service → call it → persist → log) directly in the main method.

  ```csharp
  // Prefer — the main method shows the shape of the work:
  var refund = await _solanaService.RefundTokens(bet, refundAmount, ct);
  await WriteRefundLedgerRow(bet, refund, message.Reason, ct);

  // Avoid — the whole method branch (resolve + call + persist + log) inlined in the main method.
  ```

  Each extracted method should have one clear purpose and a name that says what it does
  (`ValidateBetParameters`, `CreditWinner`, `WritePlacementLedgerRow`). This keeps the entry method
  short and the behaviours independently readable/testable.
- **Controllers are thin:** extend `BaseController`, build a MediatR request, return
  `ProcessRequest(...)` (wraps `ApiResult<T>` = `{ data, messages }`). Validation failures via
  `IMessaging.ReturnValidationFailureMessage(...)`.
- **Player endpoints are public; identity is the wallet in the request.** There is no launch token and
  no request-scoped tenant — `/games/*` and `/players/*` take no auth header. A paid bet proves who the
  player is by carrying a confirmed on-chain transfer signature from their wallet.
- **A write that only a wallet's owner may make must be signature-gated.** A wallet address is public,
  so an unproven `PATCH` would let anyone act as anyone. Mint a purpose-scoped single-use nonce
  (`GET /players/nonce?wallet=…&purpose=…`, `WalletNonce:{purpose}:{wallet}` in Redis, 5 min), have the
  client sign a canonical message, verify with `PublicKey.Verify`, and consume the nonce. **Give every
  new flow its own `purpose`** or a nonce minted for one becomes a replay against another. The
  canonical message must be built by a shared helper (e.g.
  `UpdatePlayerPreferencesHandler.BuildMessage`) that the frontend duplicates byte-for-byte.
- **The admin token is the only auth left.** `AdminTokenValidationMiddleware` validates `X-Admin-Token`
  (`scope=admin`) against `GeneralSettings:Operator:AdminToken:PublicKeyPem` — no `iss`, no contract
  check — and guards `/admin` only. Failures → generic **HTTP 401** (vague; real reason logged with a
  correlation id). When signing/validating JWTs, build the `RsaSecurityKey` from exported
  `RSAParameters` with provider caching off (avoids a disposed-RSA error).
- **Async messaging via the MassTransit Postgres SQL transport** (no broker): consumers in
  `SpaceStroker.Infrastructure/Consumers/…` (one receive endpoint each), registered in
  `SpaceStroker.Consumers/Program.cs`. Publish via `IMessageBus`. See
  `features/masstransit-postgres-transport.md`.
- **Caching via `ICacheService`** (Redis or in-memory). Keys built by `CacheHelper`. There is one
  deployment, so `ActiveParameters` and the global-stats key are **global** — they carry no tenant
  suffix, which means they are shared across tests too (assert deltas, invalidate explicitly).
- **Domain stays pure:** entities/value-objects/domain-services in `SpaceStroker.Domain` have no
  EF/infra dependencies. All entities extend `TableEntity`.
- **There is one settlement path: `ISolanaService`, against the token in
  `GeneralSettings:Operator:Token`.** No strategy, no resolver, no per-game branch — the
  `PaymentMethod` enum and the off-chain `Standard` method are gone, so every amount is token-native.
- **Every money movement is exactly one `TransactionLog` row** — `Debit` for the stake placement,
  `Credit` for a payout, `Refund` for a returned stake — carrying the on-chain detail inline
  (`Signature`, `RelatedSignature`, `Wallet`, `MintAddress`, `SenderAccount`, `RawTokenAmount`,
  `Decimals`). `CryptoTransactionLog` was merged in and dropped.
  **`BetId` is nullable**: a stake rejected before `InsertBet` still gets a row, keyed for idempotency
  on `RelatedSignature` (the payment being reversed) rather than a bet id. Any query that filters by bet
  id must exclude nulls first, or the `Contains` will not translate. A `Debit` must **never** be read
  as a payout — reward subtraction reads `Credit`, the already-refunded guard reads `Refund`.
  See `features/remove-operators.md`.
- **No application-level secret encryption.** `ISecretProtector`/`AesSecretProtector` are deleted: the
  token owner private key lives in `GeneralSettings:Operator:Token`, and encrypting a config value
  with a key stored in the same config bought nothing once no user could type a private key into the
  admin portal. Deployed environments inject it via **Secret Manager**; the committed
  `spacestrokersettings.json` carries a placeholder. Never return a secret in a DTO or log it, and
  mask RPC URLs (the provider api-key rides in the query string).
- **Tests:** `SpaceStroker.Tests` (xUnit + Shouldly + Moq + Bogus, in-memory EF). Update + add + report
  changed tests on every behavior change (see `constraints.md`).
- **Game-flow changes MUST update the integration tests.** End-to-end coverage of the game
  lifecycle lives in the separate **`SpaceStroker.Tests.Integration`** project (Testcontainers:
  PostgreSQL + Redis; boots the `SpaceStroker.Api` + `SpaceStroker.Consumers` hosts and runs real
  games bet→run→reward with Solana mocked, on a client that sends **no auth header**). It is
  **excluded** from the default
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

- **Logging is Serilog, console-only — and Serilog is the *only* provider.** Both hosts configure
  Serilog once via `builder.AddLogs()` (`SpaceStroker.Infrastructure/DependencyInjection.cs`) — a single
  **Console** sink (stdout), minimum level **Information** in non-production and **Warning** in
  production. **Logs are not persisted to the database** (the old `Serilog.Sinks.PostgreSQL` sink and its
  `SpaceStrokerApiLogs`/`SpaceStrokerConsumerLogs` tables were removed — see `specs/no-db-logging.md`);
  stdout is captured by Cloud Run → Cloud Logging in production and shown in the terminal locally. Code
  logs via the static `Serilog.Log`.

  **The console sink's *format* is environment-dependent: Cloud-Logging JSON in Production, plain text
  everywhere else** (2026-07-28, Trello #114, `specs/gcp-dashboards.md`). `AddLogs` picks
  `WriteTo.Console(new GoogleCloudJsonFormatter())` when `builder.Environment.IsProduction()`, and the
  default plain-text template otherwise — so local and Docker output is unchanged and a developer never
  reads JSON. **This is not cosmetic.** Cloud Run files a plain-text stdout line as `textPayload` at
  severity **`DEFAULT`**, so before this an `Error` and a `Warning` were *indistinguishable* in Cloud
  Logging and "count the errors" had nothing to count. The JSON line carries a top-level `severity`,
  which is what makes the built-in `logging.googleapis.com/log_entry_count` metric (labelled by
  `severity`) usable — that metric drives the production dashboard's error/warning panels, so **no
  log-based metric was needed for them at all**.

  `GoogleCloudJsonFormatter` lives in **`SpaceStroker.Common/Logging/`** (an `ITextFormatter`; `Common`
  already referenced Serilog, so **no new NuGet package**). Three details are load-bearing:
  - **The exception is appended into `message`**, not written after it. Cloud Run treats every stdout
    line as its own entry, so a stack trace on following lines becomes **N entries** — inflating error
    counts and orphaning the trace from its message. In one entry it also lets Error Reporting
    recognise the stack trace.
  - **Do not reach for `Serilog.Formatting.Compact`.** It writes the level as `@l`, which Cloud Run
    does **not** read as a severity — it would look right and change nothing.
  - `severity` maps `Verbose`/`Debug`→`DEBUG`, `Information`→`INFO`, `Warning`→`WARNING`,
    `Error`→`ERROR`, `Fatal`→`CRITICAL`.

  Pinned by `SpaceStroker.Tests/UnitTests/Common/GoogleCloudJsonFormatterTests.cs` (severity mapping,
  one-line output, exception-in-the-same-entry, quote escaping) and by two cases appended to
  `AddLogsProviderTests` covering the Production-vs-non-Production choice. Note the **integration
  suite is unaffected by design**: `IntegrationTestFixture` replaces `Log.Logger` *after* the hosts
  build (`Harness/IntegrationTestFixture.cs`), so its "no error logs" assertion never sees this
  formatter.

  **`AddLogs` calls `builder.Logging.ClearProviders()` before `AddSerilog`, and that call is
  load-bearing — do not drop it.** `AddSerilog` only *adds* a provider; it does not replace the host's
  built-in Console/Debug providers. Left registered, those write the **same stdout** but are filtered by
  `Logging:LogLevel` in `spacestrokersettings.json` (`Default: Information`, with **no per-environment
  override**) rather than by Serilog's `MinimumLevel`. That is why production emitted Information-level
  noise into Cloud Logging for months despite `MinimumLevel.Warning()` — most visibly four
  `IHttpClientFactory` lines per Solana RPC call under category `System.Net.Http.HttpClient.*`, which
  none of the `Microsoft.*` → `Error` rules cover. Fixed 2026-07-28 and pinned by
  `SpaceStroker.Tests/UnitTests/Common/AddLogsProviderTests.cs`, which asserts exactly one
  `ILoggerProvider` (the `SerilogLoggerProvider`) survives `AddLogs()` in every environment.

  Note that **Cloud Run's own request log is separate** and unaffected by any of the above: one INFO
  entry per HTTP request (`httpRequest.requestMethod`/`status`/`latency`), emitted by the platform, not
  the app. Silencing those needs a Cloud Logging exclusion filter, not a code change.

## Validating a wallet string — `WalletAddress.IsValid` (#186)

- Use `SpaceStroker.Infrastructure/Services/Players/WalletAddress.IsValid` for any new "is this a Solana
  public key" check. ⚠️ A bare `try { new PublicKey(w) } catch` is **not enough**: Solnet accepts any base58
  string (`"bad"` decodes), so the helper also requires a **32-byte** key. Three older private try/catch
  copies predate it and were left as they are.
- Integration tests that hit a wallet-validated read must use **real keypairs**
  (`new Account().PublicKey.Key`), not `stats-{run}-rabbit`-style strings.
- Public reads keyed on an arbitrary wallet follow the profile/levels shape: invalid and house wallets
  produce the **same** empty/unavailable answer as an unknown one (HTTP 200, no reason), and only wallets
  with a `Player` row — or a bounded, capped input — are cached, so a caller cannot mint cache keys.

## `ErrorMessages.resx` values are player-facing verbatim

Since #158 the frontend renders `ApiResult.messages[].value` **exactly as written** — there is no
front-side rewrite map and no string matching on the wording. That makes a `.resx` edit a **UX change**,
not a refactor:

- **Write for a player, not a developer.** "The requested payment signature have not sent tokens from
  the request wallet" was live copy for months. Internal vocabulary ("the request wallet", "informed in
  the request", "check the data" — the data is on-chain) reads as noise to the person it is addressed to.
- **`ErrorMessages.Designer.cs` must not drift.** It is generated from the `.resx`, and its XML doc
  comments quote the value. Regenerate it, or mirror the edit exactly.
- **Preserve format placeholders.** `AdviceOnDifferentSignatureBetAmount` carries `{0}`, which
  `SolanaService.cs` formats `_settings.Games.SupportEmail` into. Dropping it silently loses the support
  address — nothing throws.
- **Never let whitespace be a separator.** `SignaturePaid{Less,More}ThanBetAmount` used to end in a
  trailing space, which was the *only* thing separating them from the advice sentence they get
  concatenated with. It is invisible in an editor and did not survive the first copy tidy.
  `GenerateErrorMessageForTokenExchange` joins with an explicit `" "` now.
- **Some keys are load-bearing and must NOT be reworded** — `ChoiceAlreadyTaken` and `RefundWarning`
  (byte-identical output is what keeps an Orbit cross-round reservation indistinguishable from a
  same-round collision, #150), `GameNotValidForBets` and every auth/wallet-proof message
  (`constraints.md` §6 — unknown vs inactive must stay indistinguishable).
- **Tests assert symbolically** (`ErrorMessages.X`, `BetValidationResult.X.StringValueOf()`), so a value
  change flows through and `run_tests.bat` stays green with **zero test edits**. If a test *does* need
  editing, you reworded a key that was supposed to stay put — fix the `.resx`, not the assertion.

## Middleware ordering and client-IP keying (`SpaceStroker.Api/Program.cs`)

The API's middleware order is load-bearing, and two ordering/config traps silently disabled the
in-app rate limiter in production until 2026-07-28 (Trello #112, `specs/rate-limiter-keying.md`).
Both are the kind of bug that looks fine in review and passes every test.

**The order, and why each position matters:**

```csharp
app.UseForwardedHeaders(forwardedHeadersOptions);   // 1. FIRST — everything downstream reads the client IP
if (dev) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseCors("DevOrigins");                          // 2. BEFORE any short-circuiting middleware
app.UseIpRateLimiting();                            // 3. after CORS, so its 429 carries CORS headers
app.UseHttpsRedirection();
app.UseMiddleware<LaunchTokenValidationMiddleware>();
app.UseMiddleware<AdminTokenValidationMiddleware>();
```

- **A middleware that can short-circuit must sit AFTER `UseCors()`.** `CorsMiddleware` writes its
  headers *before* invoking the rest of the pipeline, so anything that returns early above it produces
  a response with **no `Access-Control-Allow-Origin`** — and the browser then reports a **CORS error**,
  not the actual status. `UseIpRateLimiting()` sat above `UseCors()`, so every 429 reached players as
  an indistinguishable-from-outage CORS failure. Applies to any future short-circuiting middleware, not
  just the limiter.
- **`UseForwardedHeaders` must stay first**, and **`ForwardLimit` must be set explicitly**. ASP.NET's
  default is `1`, which walks one hop from the **right** of `X-Forwarded-For` — and the rightmost entry
  is the proxy that *added* the header, never the client. Behind the Google external ALB (which sends
  `<client>, <lb>`) that resolved every request to the load balancer, so every `AspNetCoreRateLimit`
  rule became **one global bucket shared by the whole player base**. It is now
  `ForwardLimit = 2`, matching the measured hop count.
- **Never set `ForwardLimit = null`** and never set `IpRateLimitOptions.RealIpHeader` to
  `"X-Forwarded-For"`. Both end up trusting the **leftmost** entry, which is fully attacker-controlled,
  so every request could mint its own bucket — strictly worse than the bug above.
- **⚠️ `RealIpHeader` is pinned to `""` in `spacestrokersettings.json`, and must stay pinned.**
  AspNetCoreRateLimit **4.0.2 defaults it to `"X-Real-IP"`** and resolves the client from that header
  **in preference to** `Connection.RemoteIpAddress`. Google's ALB does not originate that header — but
  it forwards whatever the **client** sends, so a caller supplying its own `X-Real-IP` got a **fresh
  rate-limit bucket per request** and walked through every rule over the ordinary public path. That was
  a live production bypass, independent of `ForwardLimit`, and it would have made the `ForwardLimit`
  fix worthless. A package upgrade that reintroduces a non-empty default re-opens it, which is why
  `RateLimitSettingsTests` asserts the pinned value.
- **Adding a rate-limit rule is a money-path decision.** `POST /games/{id}/bets` is deliberately
  **unlimited** app-side: for `SolanaToken` the client transfers on-chain *first*, then posts the
  signature, so a rejected bet strands tokens with no `Bet` row, no auto-refund, and nothing for
  `POST /admin/bets/refund` to find. `RateLimitSettingsTests` asserts the rule list **exactly** so a
  new catch-all fails the build instead of a player quietly losing money. A rule whose `Endpoint` has
  no `:` (e.g. `api/*`) matches **nothing** — the first segment is parsed as the *hostname*.

## Meta-repo console tools (`monkify-meta/tools`, `monkify-meta/deploy`)

Neither solution is part of the deployed application. **Nothing here is built into a container image,
deployed to Cloud Run, or run by the local Docker stack.** They are built and run by hand, on purpose —
three of them move real money.

| Solution | Holds | Binaries |
| --- | --- | --- |
| `tools/SpaceStroker.Tools.sln` | `SpaceStroker.WalletFunder` · `SpaceStroker.TokenSniper` · `SpaceStroker.Nightfall` · `SpaceStroker.DbConsole` | `spacestroker-fund-wallets` · `spacestroker-snipe-token` · `spacestroker-nightfall` · `spacestroker-db` |
| `deploy/SpaceStroker.Deploy.sln` | `SpaceStroker.Deploy` + its tests, next to the Terraform stack | `spacestroker-deploy` |

- **One shared, git-ignored `tools/secrets.json` for the whole tools solution.** Shared keys
  (`solana.rpcUrl`, the `wallets[]` pool) sit at the **top level**; each tool gets one **named
  section** (`coinbase`/`travelRule`/`funding`, `snipe`, `nightfall`). Every tool **ignores unknown
  fields** in a pool entry — that is what lets one array serve all three. The only tracked config is
  the placeholder-only `secrets.example.json`.
- **Documented exception: `SpaceStroker.DbConsole` reads `deploy/secrets.<env>.json`,** not the shared
  file. Cloud SQL connection strings are environment-scoped and owned by the deploy tool; copying
  production credentials into a second untracked file would create drift and a second place to leak
  from. It finds that file by walking up from `AppContext.BaseDirectory` for
  `<dir>/monkify-meta/deploy/secrets.<env>.json`, so the lookup is independent of where the project
  itself sits.
- **All run state, receipts and reports go to the git-ignored `tools/Receipts/`.** Never write run
  state to the solution root. ⚠️ **Changing a receipts default path is a money-safety change for
  `spacestroker-fund-wallets`:** its Coinbase idempotency tokens are derived from the plan, so a
  receipts file the tool cannot see reads as "no prior run" and a re-run issues genuinely new
  transfers. It refuses to start when a pre-refactor file is about to be missed
  (`Domain/LegacyReceipts.cs`). The sniper and Nightfall only warn — both recompute from live
  on-chain state, which is their real resume truth.
- **The Domain / Infrastructure / Application split applies here too** — see "The folder tree must
  show the architecture" above. `Domain/` stays IO-free, which is precisely what makes the
  money-safety rules unit-testable without a fake sender or a filesystem.
- **Secrets never reach output.** Nothing beyond public addresses may appear in console output,
  receipts or a report; a keyed RPC URL is masked to host-only wherever it is logged. ⚠️ In
  `spacestroker-deploy` this is why the stored-value comparison reads the payload to a **temp file**
  (`gcloud secrets versions access --out-file`) instead of `--format="value(payload.data)"`, which
  **decodes and prints** the value — that one flag would put every private key in the deploy log.
- **Secret Manager bills per VERSION, so the deploy tool keeps exactly 2 per secret.** The
  *"Secret version replica storage"* SKU charges per **enabled version per month** (first 6 free),
  **not** per secret, and access operations are effectively free (10,000/month against ~1,800 used).
  `SyncSecrets` therefore **skips the `versions add` entirely when the value has not changed** and
  trims each secret to its newest **2** versions; `spacestroker-deploy prune-secrets` does the same
  standalone, over the **live** `spacestroker-<envCode>-*` list rather than the keys in
  `secrets.<env>.json`, so a key that drifted out of that file cannot keep billing unnoticed.
  Retention is 2 rather than 1 because the second version is the only cloud-side rollback for a bad
  value — `secrets.<env>.json` is git-ignored and lives on one workstation. Before this, 24 secrets
  had grown to 692 versions of near-identical duplicates (R$118.77 in half a month); see
  [`specs/secret-version-retention.md`](../specs/secret-version-retention.md). ⚠️ **Destroying a
  version is irreversible** — the newest enabled version is never touched, ordering is numeric (a
  text sort puts `"9"` above `"10"`), and destroys run oldest-first.

<!-- TODO: confirm naming conventions, file/folder rules, error-message/resource usage, and add
     concrete examples. -->


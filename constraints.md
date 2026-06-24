# Monkify — Constraints (non-negotiable)

> Read this **before** writing code, running commands, or touching git. These rules override
> convenience. When in doubt, stop and ask the user.

## 1. Secrets & configuration — only ever write MOCK values

- You **may** read, create, and edit secrets/config files (e.g. `appsettings.json`,
  `appsettings.*.json`, `secrets.json`, `web.config`, `monkifysettings.json`, `.env`, `.env.*`),
  **but only ever put MOCK / placeholder values in them.** Never write a real credential, key,
  token, connection string, or secret into any file.
- Never print, echo, copy, or commit a **real** secret value. If you encounter a real secret while
  editing one of these files, leave it untouched (or replace it with an obvious mock only when the
  task requires it) — do not surface it in chat, logs, or commits.
- Use clearly fake placeholders (e.g. `MOCK_VALUE`, `your-key-here`, `Host=localhost;...;Password=postgres`)
  so it is unmistakable that the value is not production.
- Real provider credentials (Solana owner keys, PayPal, Braintree/Venmo, Stripe, DB, RabbitMQ,
  Redis) are assumed to already exist in the real environment and are bound via the existing
  settings mechanism (`GeneralSettings`). Your job is never to supply the real ones.
- Still never commit files containing real secrets (see §2).

## 2. Git — a new branch per repo, per feature

- For a feature that spans repos, create and push a **new branch in EACH affected repository**
  (`monkify-backend` and `monkify-front`). Use the feature's agreed name, e.g.
  `feature/operators`, `feature/payment-methods`.
- Never commit directly to the default branch. Only commit when the user asks; never push force or
  destructive git operations unless explicitly requested.
- Never update git config. Never commit secrets/config files (see §1).

## 3. Unit tests (backend changes)

- When you change **backend** behavior in `monkify-backend`, **update the affected unit tests so
  they pass and assert the intended behavior**, and **add tests** for new behavior. Test project:
  `Monkify.Tests` (xUnit + Shouldly + Moq + Bogus; in-memory EF).
- At the **end of the work, report exactly which unit tests were changed or added.**
- Run the full suite before declaring done.

> Note on the team rule "always run unit tests for Central de Reservas": that rule is for **CR**,
> a different app. For **Monkify** the equivalent expectation is the test discipline above
> (update + add + report changed tests).

## 4. Database & migrations

- **EF configuration is inline** in `MonkifyDbContext.OnModelCreating`. Do **not** introduce
  `IEntityTypeConfiguration` classes — keep new mapping inline in the existing style.
- **Migrations are applied only by `Monkify.Consumers`** at startup (`Database.Migrate()`). The API
  does not migrate. Add new migrations under `Monkify.Infrastructure/Migrations/`.
- Never hand-edit `MonkifyDbContextModelSnapshot.cs`; regenerate it with `dotnet ef migrations add`.
- When two in-flight features both add migrations, merge **Operators first**, then rebase
  **Payment Methods** and regenerate its migration on top (see the specs' reconciliation sections).

## 5. Production data, queries & external APIs

- **Do not run database queries or external/Unidas API requests** that could touch or expose
  sensitive/production data. These may contain PII and must not pass through the agent. If a query
  or request is needed, **ask the user to run it** and share only what is safe.
- Do not point tools at production endpoints. Local docker-compose is the safe environment.

## 6. User-facing messaging (Operators feature)

- For expired/inactive operators, only ever surface a **vague** "not found / unavailable" message.
  **Never reveal contract status** (e.g. never say "the contract expired"). Reuse the existing
  generic error messages; do not add expiry-specific ones.

## 8. Scope & safety defaults

- Prefer editing existing files over creating new ones; don't create docs/files unless asked.
- For reversible, low-risk choices (naming, formatting, equivalent approaches), decide and note it.
  For scope changes or destructive actions, ask first.
- Keep the two deployable hosts' responsibilities intact: the **API** stays stateless and does not
  migrate; background/money/typer work stays in **`Monkify.Consumers`**.

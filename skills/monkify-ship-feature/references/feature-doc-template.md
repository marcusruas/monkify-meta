# Feature doc template (`features/<name>.md`)

Distilled summary for agents. Authoritative deep design stays in `specs/`. Match the tone of
`features/remove-operators.md`.

```markdown
# Feature — <Human name>

> Status: **shipped.** Authoritative spec: `../specs/<spec-file>.md`
> (merged via `<branch>` on <YYYY-MM-DD>).

## What it does

<One short paragraph: user-visible outcome and who it is for.>

## Core rules

<Bullet list of business rules as present-tense behavior — scoping, lifecycle, validation, errors,
payments, concurrency. Each bullet should be testable.>
- ...

## Key code touchpoints

<Concrete paths on main in monkify-backend and monkify-front — entities, handlers, controllers,
consumers, routes, composables, services.>
- ...
```

## Writing rules

- **Present tense** — "Players must send…", "The API returns…", not "We will add…".
- **Behavior over implementation** — rules first; code paths second.
- **Vague auth errors** — never document a user-facing message that distinguishes *why* an auth or
  wallet-proof check failed (see `constraints.md` §6).
- **Link the spec** — do not paste the whole spec; distill what agents need on every task.
- **Key code touchpoints** — verify paths exist on `main` after merge; no planned paths.

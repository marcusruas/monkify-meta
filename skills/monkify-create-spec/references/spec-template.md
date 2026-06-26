# Implementation spec template

Write `monkify-meta/specs/<slug>.md` using this outline. Scale sections to the feature — omit only
when genuinely N/A and say so. Follow the tone and depth of `specs/operator-feature.md` and
`specs/free-bet-mode.md`.

```markdown
# Monkify — "<Feature Title>" Feature — Implementation Spec

> **Status: Ready for implementation** — refined via Trello card
> [<Card title>](<card url>) (YYYY-MM-DD).
> Repos affected: `monkify-backend`, `monkify-front`, and this doc in `monkify-meta`.
>
> **Purpose:** <One short paragraph — what and for whom.>

---

## 1. Summary

<What we're building. Use tables for mode comparisons, before/after, or paid vs free paths when helpful.>

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Branch-per-repo:** `feature/<slug>` in each affected repo.
- **Unit tests:** update/add in `Monkify.Tests`; report changed tests at end of implementation.
- **EF mapping:** inline in `MonkifyDbContext.OnModelCreating`; no `IEntityTypeConfiguration`.
- **Migrations:** under `Monkify.Infrastructure/Migrations/`; applied only by `Monkify.Consumers`.
- **Secrets/config:** mock-only values in committed files.
- **Operator messaging:** vague errors only; never reveal contract status.
- <Any feature-specific constraints from the card.>

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | <From card Decisions + conversation context> |
| 2 | ... |

---

## 4. Data model

### 4.1 Entity / column changes

<File paths + property snippets>

### 4.2 EF configuration (inline)

<MonkifyDbContext snippets>

### 4.3 Migration

<Migration name + dotnet ef command>

### 4.4 ER diagram (optional)

```mermaid
...
```

---

## 5. Configuration (if applicable)

<GeneralSettings, env vars, seed data — with mock-only note>

---

## 6. Backend behaviour

<Handler/consumer changes in execution order. Call out skips, new validation, events.>

### 6.x Subsections per area touched

---

## 7. Frontend behaviour

<Types, composables, components, env flags, user-facing copy>

---

## 8. Flow diagrams (when non-obvious)

```mermaid
sequenceDiagram
  ...
```

---

## 9. Out of scope

<Bullet list from card>

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

| # | File | Change |
| --- | --- | --- |

### 10.2 Frontend (`monkify-front`)

| # | File | Change |

### 10.3 Meta (`monkify-meta`)

| # | File | Change |

---

## 11. Unit tests

Project: `monkify-backend/src/Monkify.Tests`.

| Test file | Test cases | Expected |
| --- | --- | --- |

---

## 12. Acceptance criteria

<Map 1:1 from Trello **Acceptance Criteria** checklist — Given / When / Then.>

1. Given ..., when ..., then ...
2. ...

---

## 13. Manual QA (optional)

<Short numbered steps for local verification>

---

## 14. Trello / pipeline

- **Source card:** <link> — list **Specs created**; **Implementation** section on card lists branch + spec
- **After merge:** `/monkify-ship-feature <card name>` (reads branch from card)

---

## 15. Revision log

| Date | Change |
| --- | --- |
| YYYY-MM-DD | Initial spec from refined card |
```

## Quality bar

A spec is **ready** when an implementer agent can:

- Create the right migration and EF mapping without guessing column types.
- Find every file to edit from §10 without ripgrep-first discovery.
- Write tests from §11 without inventing scenarios.
- Verify done-ness against §12 acceptance criteria.

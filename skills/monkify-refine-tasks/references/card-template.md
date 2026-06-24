# Refined card template

Write the refined card description with this structure (Markdown). Scale each section to the task —
omit a section only if it genuinely does not apply, and say so rather than leaving it blank.
Acceptance criteria do **not** go in the description; they go in the **"Acceptance Criteria"**
checklist (see SKILL.md Step 5).

```markdown
## Problem
<One short paragraph: what we're solving and for whom.>

## Decisions (from refinement)
<Bullet list of the concrete answers reached during refinement — especially the "how does it behave
when X" clarifications. This is what makes the card unambiguous.>
- ...

## Backend impact (monkify-backend)
<Entities/enums, handlers/consumers, EF mapping + migration, endpoints/headers, which host owns it.
Write "None" if the card is frontend-only.>

## Frontend impact (monkify-front)
<Composables/services/types, API calls + headers, live UI behavior, user-facing copy/states.
Write "None" if the card is backend-only.>

## Edge cases & failure modes
<Mid-session changes, operator contract states, payment failure/refund, free-coin vs paid,
concurrency/idempotency, backward compatibility.>

## Out of scope
<What this card explicitly does NOT include.>

## Affected repos
- [ ] monkify-backend
- [ ] monkify-front

## Constraints
<Only the ones that apply, from constraints.md — e.g. branch-per-repo, unit tests, vague operator
messaging, mock-only secrets.>

## Status
Refined — ready for spec.
```

## Acceptance criteria (checklist, not description)

Each item is one testable statement in Given / When / Then form, added to the **"Acceptance
Criteria"** checklist:

- `Given <context>, when <action>, then <observable outcome>.`

Examples:

- `Given no operator-id header, when the player lists sessions, then only sessions with OperatorId == null are returned.`
- `Given an operator whose contract has expired, when the player opens the game, then a vague "not found / unavailable" message is shown and no sessions load.`

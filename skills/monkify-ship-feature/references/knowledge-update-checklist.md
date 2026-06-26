# Knowledge base update checklist (post-merge)

Use after the feature branch is on `main` in every affected code repo. Work top to bottom; skip a
section only when it genuinely does not apply — say "N/A" in the report, do not silently omit.

> **Source priority:** live code on `main` > spec > Trello card (refinement intent) > existing KB docs.
> If sources disagree, document what **runs** and flag drift for the user.

## 1. Feature digest (internal — drives all edits)

Before editing files, capture (from code, spec, **and Trello card when available**):

- [ ] One-sentence **what it does** (player/operator/ops perspective).
- [ ] **Core business rules** (bullets: scoping, lifecycle, payments, errors, concurrency).
- [ ] **Refinement decisions** from the Trello description/comments (if loaded).
- [ ] **Edge cases** that shipped (mid-session, expiry, missing headers, refunds, etc.) — cross-check
      Trello "Edge cases" and acceptance criteria against code.
- [ ] **API surface** — new/changed routes, headers, hub behavior.
- [ ] **Data model** — entities, required fields, FKs, migrations.
- [ ] **Frontend** — routes, composables, services, theming/copy.
- [ ] **Out of scope** — from spec and/or Trello; do not document as current behavior.

## 2. `features/<feature>.md` (required)

- [ ] Create or rewrite using `feature-doc-template.md`.
- [ ] Present tense; link to spec: `../specs/<file>.md`.
- [ ] "Key code touchpoints" points to real paths on `main`.
- [ ] Remove stale "in flight" / branch-only language.

## 3. `architecture.md`

Update when the feature changed any of:

- [ ] Deployable hosts or project responsibilities.
- [ ] Request/event flow (MediatR, MassTransit, SignalR, Hangfire).
- [ ] External integrations (Solana, Redis, PostgreSQL shapes).
- [ ] API controller inventory or major endpoint groups.
- [ ] Cross-cutting headers or tenancy model.

Keep the doc describing **current** architecture — delete paragraphs that describe pre-feature behavior.

## 4. `glossary.md`

- [ ] Add terms introduced by the feature.
- [ ] Update definitions that changed (e.g. nullable → required, renamed headers).
- [ ] Remove or fix entries that contradict `main`.

## 5. `conventions/`

- [ ] `backend.md` — new handler/consumer/EF patterns worth repeating.
- [ ] `frontend.md` — new composable/service/header conventions.

Only document patterns that appear in merged code, not one-offs.

## 6. `constraints.md`

- [ ] Add a rule **only** when the feature enforced a new non-negotiable in code *and* agents must
      know it (e.g. new header requirement, new error-message policy).
- [ ] Do **not** duplicate feature-specific rules that belong in `features/` — link instead.

## 7. `INDEX.md`

- [ ] Update the feature row in "Map of this knowledge base" (status → distilled / current).
- [ ] Clear or replace **"Feature currently in flight"** if this was that feature.
- [ ] Ensure the spec path in the map matches the file the user named.

## 8. Spec file (`specs/…`)

- [ ] Add or update top banner: **Status: Shipped** + date + branch name.
- [ ] Do not rewrite the spec body to match KB — optional small "Post-ship notes" if code diverged.

## 9. Consistency pass

- [ ] `features/`, `architecture.md`, and `glossary.md` agree on the same terms (header names, entity
      fields, error behavior).
- [ ] Operator/user-facing error rules still say **vague** messaging where required.
- [ ] No doc still describes removed behavior (e.g. house sessions, deleted endpoints).

## 10. Exit bar

- [ ] A new agent reading only `INDEX.md` → `features/<feature>.md` understands how the feature
      behaves in production.
- [ ] Business rules are stated as behavior, not implementation tasks.
- [ ] User has seen a summary of KB diffs before any commit.

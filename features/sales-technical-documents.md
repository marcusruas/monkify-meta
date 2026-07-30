# Feature — Sales technical documents (B2B SaaS)

> Status: **shipped.** Authoritative spec: `../specs/sales-technical-documents.md`
> (merged via `feature/sales-technical-documents` on 2026-07-01).
>
> ⚠️ **The product these documents describe no longer exists** (`features/remove-operators.md`,
> 2026-07-30). The whole `sales/` set is written around **multi-tenant operators** — Model A
> (regulated, operator wallet, launch-token `sub`) and Model B (crypto) — and every one of those
> concepts was deleted: there is no operator, no launch token, no `Standard`/off-chain settlement and
> no RG pre-bet hook. **Rewriting `sales/` (and `pitch/`) was explicitly out of scope for that card
> and needs its own.** Treat the content below and the files under `sales/` as historical until then.

## What it does

Provides the **technical / commercial Markdown document set** sales uses to take a casino prospect
from first contact through an under-NDA integration deep-dive. The deliverable lives under
`sales/` in this repo (not in application code). Docs 01 (pitch) and 02 (integration overview) ship
in **per-model versions** — send Model A to regulated buyers and Model B to crypto-native buyers.
Doc 03 (anti-clone playbook) is single and internal. Every doc has an EN master and a PT-BR variant.

Sibling assets: pitch **strategy** on Trello [#1 (ZowgQHZg)](https://trello.com/c/ZowgQHZg); **legal**
documents in `legal/` (`specs/legal-pitch-documents-batch-1.md`).

## Core rules

- **Two integration tracks** are defined once in `sales/README.md` and reused **verbatim** in every
  doc via the canonical track table and `MODEL-A-PROFILE` / `MODEL-B-PROFILE` blocks (EN + PT-BR).
  - **Model A (Regulated):** operator-owned wallet; player = launch-token `sub` (`ExternalPlayerId`);
    settlement via operator wallet API (debit / credit / refund); optional RG pre-bet hook.
    ⚠️ **This model no longer exists in the product** — see the banner at the top of this file.
  - **Model B (Crypto-native):** SpaceStroker-hosted on-chain payments; player = wallet public key;
    settlement via Solana SPL transfers; launch token has empty `sub`.
- **Per-model docs:** `sales/model-a/` and `sales/model-b/` each hold `01-pitch` and
  `02-integration-overview`. Doc 03 stays at `sales/03-anti-clone-playbook.md`.
- **Doc 01** is cold-sendable and self-contained; pricing headline = **setup fee + monthly platform
  fee only** (no %NGR / revenue share); 12-month minimum and exclusivity-as-paid-add-on are editable
  levers tagged **Draft**.
- **Doc 02** is **Confidential — under NDA**; single-path launch→bet→settle flow per model, grounded
  read-only in shipped backend behavior; mock-only secrets; auth failures described as
  generic **401 / not found / unavailable** (never contract status).
- **Doc 03** is internal: anti-clone positioning + pre-sales-call checklist; links to per-model docs.
- **Discipline:** placeholders only (`[OWNER]`, `[ENTITY]`, `MOCK_…`); operator names (BetMGM,
  BetFury, Hub88, …) are **illustrative, never claimed clients**; GLI/BMM cert, lawyer review, and
  trademark filing are **DEFERRED**; commercial sections carry **Draft — not legal advice**.
- **PDF / slide-deck export is deferred** — Markdown is the deliverable; local EN PDFs may render to
  `sales/pdf/` (gitignored).

## Key code touchpoints

**Deliverable (this repo):**

- `sales/README.md` — index, canonical track table, MODEL-A/MODEL-B profile blocks
- `sales/_TEMPLATE.md` — shared document template
- `sales/model-a/01-pitch.md`, `sales/model-b/01-pitch.md` (+ `.pt-BR.md`)
- `sales/model-a/02-integration-overview.md`, `sales/model-b/02-integration-overview.md` (+ `.pt-BR.md`)
- `sales/03-anti-clone-playbook.md` (+ `.pt-BR.md`)

**Backend features Doc 02 is grounded in (read-only; unchanged by this card):**

- `specs/launch-token-validation.md` — `X-Launch-Token` RS256, `iss`/`sub`, vague 401 *(removed)*
- `specs/operator-wallet-api.md` — Model A debit/credit/refund, unified `TransactionLog` *(removed)*
- `specs/rg-pre-bet-hook.md` — optional pre-bet allow/deny/fail-closed *(removed)*
- `features/games.md` — game state machine (BetPlaced → RunGame → RewardGame)

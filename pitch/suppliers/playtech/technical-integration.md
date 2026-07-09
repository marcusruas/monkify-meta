# Monkify × Playtech — Technical Integration Overview

> **Confidential — shared under NDA.** A high-level view of how Monkify integrates with the Playtech
> Open Platform, for your technical and integration teams. The full, operator-facing detail is in the
> accompanying **Operator Integration Guide**.

## Settlement — regulated and fiat

Playtech's operators are fiat/regulated, so Monkify uses **standard (fiat) settlement**: off-chain, in
**USD**, through the operator's own wallet. Monkify holds **no player funds and no player personal
data** — that stays with the operator, where the licences are. There is **no crypto surface** in the
Playtech integration.

Monkify is **multi-tenant** — one integration reaches many operators, each scoped by a signed token —
the same "submit your game, distribute to the network" model POP already runs.

## What your operators build vs. consume

| Your operators expose to Monkify | Your operators consume from Monkify |
| --- | --- |
| A **Wallet API** — Debit / Credit / Refund (USD) | A signed **launch/auth token** flow |
| An optional **Responsible-Gaming pre-bet check** | The **game API** + real-time streams (for the lobby/client) |
| | An **iframe embed** with a lifecycle event channel |

Full endpoint contracts, request/response examples, and the onboarding checklist are in the **Operator
Integration Guide**.

## Why it fits POP

- **One integration, many operators** — mirrors POP's distribution model, so onboarding is fast.
- **Regulated-friendly** — fiat wallet settlement in USD, no player funds or personal data on our side.
- **Provably fair by design and independently verifiable** — but we're upfront that Monkify is **not
  yet lab-certified**. Because POP's operators sit in tightly regulated markets, we propose a **staged
  plan: integrate now, certify in parallel, go live per market as certification completes** — with the
  setup fee funding certification.

## Onboarding path

1. NDA in place.
2. Technical deep-dive with your Games Marketplace / integration team.
3. Operators integrate against our **sandbox + mock wallet** to exercise the full bet → reward → refund
   loop.
4. Wire the real operator wallet; validate in staging.
5. Certification track runs in parallel; go live per market as it completes.

## Security & data

- The **signed launch token** is the integration's security boundary — operators sign, we validate
  against the public key they register; key rotation supported.
- Monkify **stores no player personal data**; RG decisions are not persisted by us.
- Contract/expiry status is never surfaced to players (deliberately generic messaging).
- Amounts are **USD**; display currency is render-only and never affects settlement.

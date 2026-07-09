# Monkify × SOFTSWISS — Technical Integration Overview

> **Confidential — shared under NDA.** A high-level view of how Monkify integrates with the SOFTSWISS
> Game Aggregator, for your technical and integration teams. The full, operator-facing detail is in the
> accompanying **Operator Integration Guide**.

## One integration, both rails

SOFTSWISS uniquely spans MGA/Curaçao fiat operators **and** a deep crypto-native base. Monkify settles
**per game type**, so a single integration serves both:

- **Standard (fiat)** — off-chain settlement in the operator's own wallet, in **USD**. Monkify holds
  no player funds and no player personal data.
- **Crypto** — on-chain settlement. **Solana today; we're open to adding more chains/tokens as your
  operators need them.**

Monkify is **multi-tenant** — one integration reaches all of your operator brands, each scoped by a
signed token, mirroring your single-API distribution model.

## What your operators build vs. consume

| Your operators expose to Monkify | Your operators consume from Monkify |
| --- | --- |
| A **Wallet API** — Debit / Credit / Refund (USD) for fiat settlement | A signed **launch/auth token** flow |
| An optional **Responsible-Gaming pre-bet check** | The **game API** + real-time streams (for the lobby/client) |
| For crypto: **register a token** — nothing to build | An **iframe embed** with a lifecycle event channel |

Full endpoint contracts, request/response examples, and the onboarding checklist are in the **Operator
Integration Guide**.

## Why it fits SOFTSWISS

- **One integration, ~800 operator brands** — the same single-API model you already run.
- **Crypto-native** — the on-chain rail maps directly to your "#1 Crypto Gambling Provider" heritage
  and per-operator token model (Solana today, more as needed).
- **Provably fair by design and independently verifiable** — but we're upfront that Monkify is **not
  yet lab-certified** (the setup fee funds certification). Recommended rollout: crypto/Curaçao operators
  first, MGA-tier listings as certification completes.

## Onboarding path

1. NDA in place.
2. Technical deep-dive with your Game Aggregator team.
3. Your operators integrate against our **sandbox + mock wallet** to exercise the full bet → reward →
   refund loop.
4. Wire real operator wallets (fiat) and register tokens (crypto).
5. Roll out crypto/Curaçao operators first; regulated listings follow certification.

## Security & data

- The **signed launch token** is the integration's security boundary — operators sign, we validate
  against the public key they register; key rotation supported.
- Monkify **stores no player personal data** under fiat settlement; RG decisions are not persisted by us.
- Operators never see contract/expiry status surfaced to players (deliberately generic messaging).
- Crypto payout keys are **encrypted at rest** and never exposed.
- Fiat amounts are **USD**; crypto amounts are token-native and never converted.

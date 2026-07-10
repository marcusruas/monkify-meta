# Monkify × BC.Game — Technical Integration Overview

> **Confidential — shared under NDA.** A high-level view of how Monkify integrates into BC.Game, for
> your engineering / integration team. The full, hands-on detail is in the accompanying **Operator
> Integration Guide**.

## Crypto-native, with a fiat path too

BC.Game is crypto-native, and so is Monkify. Settlement is chosen **per game type**:

- **On-chain (crypto)** — settles on-chain and **drops into your existing wallet stack**. **Solana
  today; we're open to adding more chains/tokens as you need them.**
- **Off-chain wallet path** — if you'd rather settle against player balances, Monkify calls your wallet
  API (debit / credit / refund). Your choice, per game.

One integration puts the game live for your players, scoped by a signed token — nothing for your team to
build beyond the wallet hookup.

## What your team builds vs. consumes

| You expose to Monkify | You consume from Monkify |
| --- | --- |
| A **wallet hookup** — on-chain token registration, and/or a Debit / Credit / Refund wallet API | A signed **launch/auth token** flow |
| An optional **Responsible-Gaming pre-bet check** | The **game API** + real-time streams (for the lobby/client) |
| | An **iframe embed** with a lifecycle event channel |

Full endpoint contracts, request/response examples, and the onboarding checklist are in the **Operator
Integration Guide**.

## Why it fits BC.Game

- **A new original with almost no lift** — one integration, live for your 9M+ players, running alongside
  your BC Originals with your branding.
- **Same fairness DNA** — Monkify is **provably fair by design and independently replay-verifiable**,
  the same self-verifiable spirit as your hash-chain originals. We're upfront that it is **not yet
  independently lab-certified**; that's on our roadmap for regulated markets and is nothing your players
  need.
- **Crypto-native settlement** — on-chain (Solana today) drops into your wallet stack; a fiat/off-chain
  path is there if you prefer.

## Onboarding path

1. NDA in place.
2. Technical deep-dive with your content / integration team.
3. Integrate against our **sandbox** to exercise the full bet → reward → refund loop.
4. Wire your wallet hookup (on-chain token and/or wallet API).
5. Pilot live in your lobby, then expand game modes.

## Security & data

- The **signed launch token** is the integration's security boundary — you sign, we validate against
  the public key you register; key rotation supported.
- Monkify **stores no player personal data**; RG decisions (if used) are not persisted by us.
- On-chain payout keys are **encrypted at rest** and never exposed.
- Amounts are settled in your chosen unit and never silently converted.

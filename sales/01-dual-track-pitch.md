# Monkify — Dual-track pitch

> **Version:** 1 · **Date:** 2026-07-01
> **Audience:** Casino commercial / business-development teams
> **Confidentiality:** Cold-sendable — contains no source, credentials, or internal architecture.
> **Status:** Draft — not legal advice.

## Purpose

A self-contained commercial one-pager that explains **what Monkify is**, the **two ways a casino can
integrate it** (regulated vs crypto-native), and the **SaaS commercial model**. It is designed to be
sent on its own to open a conversation; the deeper technical detail lives in the
[Integration overview](02-integration-overview.md), shared only under NDA. For the full outreach and
staged-reveal strategy, see pitch card [#1 (ZowgQHZg)](https://trello.com/c/ZowgQHZg) — this document
references it but does not depend on it.

## What Monkify is

Monkify is a **ready-to-integrate, non-traditional casino game**: players bet on the **first word a
simulated monkey types**. A provably-fair engine types random characters in real time and streams
them live to every player; when a player's chosen sequence appears, the matching bets win and are
paid the full pot. The product is **fully built** — backend, web, mobile-web, and ops tooling — and
Monkify **licenses and operates it for you** under an SLA.

**Why it is different:** it is not another slot or table clone. It is a live, shared, spectacle game
with a mascot (Edson) and a simple, viral hook ("bet on the word"), already implemented and running.

## How you integrate — two tracks

> The messaging guardrail for every conversation: **"you integrate Monkify with *your* wallet and
> *your* player accounts."** We are not saying "we don't do payments" — we adapt to how *you* settle.

<!-- CANONICAL-TRACKS:EN START -->
### The two integration tracks

Monkify integrates with a casino on one of two tracks:

| | **Model A — Regulated** | **Model B — Crypto-native** |
| --- | --- | --- |
| **Buyer example** | BetMGM-style / licensed operators | BetFury-style crypto casinos |
| **Payments** | Operator-owned wallet; Monkify holds **no** player float | Monkify-hosted, on-chain (SOL / USDT / SPL tokens) |
| **Player identity** | Operator's `ExternalPlayerId`, delivered as the launch-token `sub` claim | Player's crypto wallet public key |
| **Settlement** | Monkify calls the operator wallet API — **debit** stake, **credit** winnings, **refund** cancellations (amounts in USD) | On-chain Solana SPL-token transfers |
| **Responsible gaming** | Operator-owned; optional Monkify pre-bet check hook (allow / deny / fail-closed) | Operator-owned; pre-bet hook optional |
| **Integration surface** | Signed launch token **+** operator wallet API **+** optional RG pre-bet hook | Signed launch token **+** player wallet connect |

In **both** tracks the casino signs a short-lived **launch token** (RS256 JWT) that tells Monkify
which operator launched the game and — for Model A — which player it acts for. Monkify takes **no
commission**: winners receive the full pot.
<!-- CANONICAL-TRACKS:EN END -->

## What Monkify owns vs what you own

| Responsibility | Model A — Regulated | Model B — Crypto-native |
| --- | --- | --- |
| Player account & identity | **You** (we reference your `ExternalPlayerId`) | Player wallet (self-custody) |
| KYC / AML | **You** | **You** (as your licence requires) |
| Player funds / float | **You** (your wallet) | Player's on-chain balance |
| Responsible gaming controls | **You** (optionally enforced via our pre-bet hook) | **You** |
| Game logic, RNG, fairness, live streaming | **Monkify** | **Monkify** |
| Hosting, monitoring, bug fixes, customer service (for the game) | **Monkify** | **Monkify** |

The point: Monkify plugs into your existing wallet and player system. You keep the regulated
surface; we run the game.

## Commercial model (SaaS / managed license)

- **Setup fee** — one-time integration & onboarding. *(figure editable; e.g. `[SETUP_FEE]`.)*
- **Monthly platform fee** — covers **hosting + bug fixes + customer service** for the game.
  *(figure editable; e.g. `[MONTHLY_FEE]`.)*
- **New feature development is quoted and billed separately** — per-project or hourly; not bundled
  into the monthly fee.
- **Minimum term:** 12 months (editable).
- **Exclusivity** (by territory, vertical, or operator group) is an optional **paid add-on** — never
  free.

> There is **no revenue share / % of NGR** in this model — the headline is setup + monthly only.

Closing line for the deck: *"Detailed game logic, integration specifics, and diligence materials are
available under NDA / non-use for serious counterparties."*

## Next step

1. Book a short evaluation call (~25 min) — see the outreach sequence in card #1.
2. Sign a mutual **NDA + non-use** (template in [`../legal/`](../legal/)).
3. Share the [Integration overview](02-integration-overview.md) and run the technical deep-dive.

## Document control

- **Model A/B source of truth:** [`README.md`](README.md) (block reused verbatim).
- **Grounded in (read-only):** n/a — commercial. Pricing per card #1 (revenue-share line omitted).
- **Deferred (paid) steps:** lab RNG certification (GLI/BMM) — **DEFERRED**; presented only as a
  roadmap item, never as done.

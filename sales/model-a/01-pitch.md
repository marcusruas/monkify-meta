# Monkify — Pitch (Model A — Regulated)

> **Version:** 1 · **Date:** 2026-07-01
> **Audience:** Casino commercial / business-development teams (regulated / licensed operators)
> **Confidentiality:** Cold-sendable — contains no source, credentials, or internal architecture.
> **Status:** Draft — not legal advice.

## Purpose

A self-contained commercial one-pager for **regulated / licensed operators**. It explains **what
Monkify is**, how it integrates on the **regulated (Model A)** track — plugging into *your* wallet and
player accounts — and the **SaaS commercial model**. It is designed to be sent on its own; the deeper
technical detail lives in the [Integration overview](02-integration-overview.md), shared only under
NDA. For the full outreach and staged-reveal strategy, see pitch card
[#1 (ZowgQHZg)](https://trello.com/c/ZowgQHZg).

> Crypto-native operator? See the [Model B version](../model-b/01-pitch.md) instead.

## What Monkify is

Monkify is a **ready-to-integrate, non-traditional casino game**: players bet on the **first word a
simulated monkey types**. A provably-fair engine types random characters in real time and streams
them live to every player; when a player's chosen sequence appears, the matching bets win and are
paid the full pot. The product is **fully built** — backend, web, mobile-web, and ops tooling — and
Monkify **licenses and operates it for you** under an SLA.

**Why it is different:** it is not another slot or table clone. It is a live, shared, spectacle game
with a mascot (Edson) and a simple, viral hook ("bet on the word"), already implemented and running.

## How you integrate — the regulated track

> The messaging guardrail: **"you integrate Monkify with *your* wallet and *your* player accounts."**
> We are not saying "we don't do payments" — we settle through *your* regulated wallet.

<!-- MODEL-A-PROFILE:EN START -->
### Model A — Regulated (single-model profile)

- **Who it's for:** licensed / regulated operators (BetMGM-style).
- **Payments:** your own wallet — Monkify holds **no** player float.
- **Player identity:** your `ExternalPlayerId`, delivered as the launch-token `sub` claim.
- **Settlement:** Monkify calls your wallet API — **debit** the stake, **credit** winnings, **refund** cancellations (amounts in USD).
- **Responsible gaming:** yours; optionally enforced via Monkify's pre-bet hook (allow / deny / fail-closed).
- **Integration surface:** signed launch token **+** operator wallet API **+** optional RG pre-bet hook.

**You keep** the player account, KYC/AML, funds/float, and RG controls. **Monkify runs** the game
logic, RNG/fairness, live streaming, hosting, bug fixes, and support. Monkify takes **no commission**
— winners receive the full pot.
<!-- MODEL-A-PROFILE:EN END -->

## Commercial model (SaaS / managed license)

- **Setup fee** — one-time integration & onboarding. *(figure editable; e.g. `[SETUP_FEE]`.)*
- **Monthly platform fee** — covers **hosting + bug fixes + customer service** for the game.
  *(figure editable; e.g. `[MONTHLY_FEE]`.)*
- **New feature development is quoted and billed separately** — per-project or hourly; not bundled.
- **Minimum term:** 12 months (editable).
- **Exclusivity** (by territory, vertical, or operator group) is an optional **paid add-on** — never
  free.

> There is **no revenue share / % of NGR** in this model — the headline is setup + monthly only.

Closing line for the deck: *"Detailed game logic, integration specifics, and diligence materials are
available under NDA / non-use for serious counterparties."*

## Next step

1. Book a short evaluation call (~25 min) — see the outreach sequence in card #1.
2. Sign a mutual **NDA + non-use** (template in [`../../legal/`](../../legal/)).
3. Share the [Integration overview (Model A)](02-integration-overview.md) and run the technical
   deep-dive.

## Document control

- **Model A/B source of truth:** [`../README.md`](../README.md) (profile block reused verbatim).
- **Grounded in (read-only):** n/a — commercial. Pricing per card #1 (revenue-share line omitted).
- **Deferred (paid) steps:** lab RNG certification (GLI/BMM) — **DEFERRED**; presented only as a
  roadmap item, never as done.
